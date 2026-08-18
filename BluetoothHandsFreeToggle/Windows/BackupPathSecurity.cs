using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace BluetoothHandsFreeToggle.Windows;

internal static class BackupPathSecurity
{
    private static readonly SecurityIdentifier AdministratorsSid = new(
        WellKnownSidType.BuiltinAdministratorsSid,
        domainSid: null);
    private static readonly SecurityIdentifier LocalSystemSid = new(
        WellKnownSidType.LocalSystemSid,
        domainSid: null);
    private static readonly SecurityIdentifier UsersSid = new(
        WellKnownSidType.BuiltinUsersSid,
        domainSid: null);

    public static void Validate(string filePath)
        => EnsureStorage(filePath, createDirectory: false, protectAcl: false);

    public static void Prepare(string filePath)
        => EnsureStorage(filePath, createDirectory: true, protectAcl: true);

    public static void ProtectFile(string filePath)
        => ApplySecureFileAcl(filePath);

    private static void EnsureStorage(
        string filePath,
        bool createDirectory,
        bool protectAcl)
    {
        var directoryPath = Path.GetDirectoryName(filePath)
                            ?? throw new UnsafeBackupPathException(
                                "The backup directory could not be resolved.");

        if (!Directory.Exists(directoryPath))
        {
            if (!createDirectory)
                return;

            Directory.CreateDirectory(directoryPath);
        }

        EnsureNotReparsePoint(directoryPath);
        var directoryInfo = new DirectoryInfo(directoryPath);
        var directorySecurity = GetSecurity(directoryInfo);
        EnsureTrustedOwner(directoryInfo, directorySecurity);
        if (protectAcl)
        {
            EnsureSecureAcl(
                directoryPath,
                directorySecurity,
                ApplySecureDirectoryAcl);
        }

        if (!File.Exists(filePath))
            return;

        EnsureNotReparsePoint(filePath);
        EnsureSingleLinkFile(filePath);
        var fileInfo = new FileInfo(filePath);
        var fileSecurity = GetSecurity(fileInfo);
        EnsureTrustedOwner(fileInfo, fileSecurity);
        if (protectAcl)
            EnsureSecureAcl(filePath, fileSecurity, ApplySecureFileAcl);
    }

    private static void EnsureSecureAcl(
        string path,
        FileSystemSecurity security,
        Action<string> applySecureAcl)
    {
        if (HasSecureAcl(security))
            return;

        applySecureAcl(path);
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnsafeBackupPathException(
                $"Reparse points are not allowed: {path}");
        }
    }

    private static void EnsureSingleLinkFile(string filePath)
    {
        using var handle = File.OpenHandle(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete);

        if (!GetFileInformationByHandle(handle, out var information))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (information.NumberOfLinks != 1)
        {
            throw new UnsafeBackupPathException(
                $"Hard-linked backup files are not allowed: {filePath}");
        }
    }

    private static FileSystemSecurity GetSecurity(FileSystemInfo fileSystemInfo)
        => fileSystemInfo switch
        {
            DirectoryInfo directory => directory.GetAccessControl(
                AccessControlSections.Owner | AccessControlSections.Access),
            FileInfo file => file.GetAccessControl(
                AccessControlSections.Owner | AccessControlSections.Access),
            _ => throw new InvalidOperationException(
                $"Unsupported filesystem object: {fileSystemInfo.FullName}")
        };

    private static void EnsureTrustedOwner(
        FileSystemInfo fileSystemInfo,
        FileSystemSecurity security)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is not null && IsTrustedOwner(owner))
            return;

        throw new UnsafeBackupPathException(
            $"Untrusted owner on {fileSystemInfo.FullName}: {owner?.Value ?? "unknown"}");
    }

    private static bool IsTrustedOwner(SecurityIdentifier owner)
    {
        if (owner.Equals(AdministratorsSid) || owner.Equals(LocalSystemSid))
            return true;

        using var identity = WindowsIdentity.GetCurrent();
        return identity.User is not null &&
               owner.Equals(identity.User) &&
               new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool HasSecureAcl(FileSystemSecurity security)
    {
        if (!security.AreAccessRulesProtected)
            return false;

        var administratorsHaveFullControl = false;
        var localSystemHasFullControl = false;

        foreach (FileSystemAccessRule rule in security.GetAccessRules(
                     includeExplicit: true,
                     includeInherited: true,
                     typeof(SecurityIdentifier)))
        {
            if (rule.IdentityReference is not SecurityIdentifier identity ||
                rule.AccessControlType is AccessControlType.Deny)
            {
                return false;
            }

            if (identity.Equals(AdministratorsSid))
            {
                administratorsHaveFullControl |= HasFullControl(rule.FileSystemRights);
                continue;
            }

            if (identity.Equals(LocalSystemSid))
            {
                localSystemHasFullControl |= HasFullControl(rule.FileSystemRights);
                continue;
            }

            if (GrantsWriteAccess(rule.FileSystemRights))
                return false;
        }

        return administratorsHaveFullControl && localSystemHasFullControl;
    }

    private static bool HasFullControl(FileSystemRights rights)
        => (rights & FileSystemRights.FullControl) == FileSystemRights.FullControl;

    private static bool GrantsWriteAccess(FileSystemRights rights)
    {
        const FileSystemRights writeAccess =
            FileSystemRights.Write |
            FileSystemRights.Modify |
            FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership;

        return (rights & writeAccess) != 0;
    }

    private static void ApplySecureDirectoryAcl(string directoryPath)
    {
        const InheritanceFlags inheritance =
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(AdministratorsSid);
        security.AddAccessRule(new FileSystemAccessRule(
            LocalSystemSid,
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            AdministratorsSid,
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            UsersSid,
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));

        new DirectoryInfo(directoryPath).SetAccessControl(security);
    }

    private static void ApplySecureFileAcl(string filePath)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(AdministratorsSid);
        security.AddAccessRule(new FileSystemAccessRule(
            LocalSystemSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            AdministratorsSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            UsersSid,
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
            AccessControlType.Allow));

        new FileInfo(filePath).SetAccessControl(security);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public NativeFileTime CreationTime;
        public NativeFileTime LastAccessTime;
        public NativeFileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}

internal sealed class UnsafeBackupPathException(string message) : Exception(message);
