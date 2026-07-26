using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BluetoothHandsFreeToggle.App;

public sealed class AppInfo
{
    public const string AppName = "BluetoothHandsFreeToggle";
    public const string AppDisplayVersion = "2.0";
    public const string AppVersionTag = "v" + AppDisplayVersion;
    public const string AppDisplayName = AppName + " " + AppVersionTag;
    public const string SupportUrl = "https://boosty.to/avazbek22";

    public string ExePath { get; }
    public bool IsAdministrator { get; }
    public string OsDisplayName { get; }
    public string FrameworkDescription { get; }
    public string Version { get; }

    private AppInfo(string exePath, bool isAdministrator, string osDisplayName, string frameworkDescription, string version)
    {
        ExePath = exePath;
        IsAdministrator = isAdministrator;
        OsDisplayName = osDisplayName;
        FrameworkDescription = frameworkDescription;
        Version = version;
    }

    public static AppInfo Create()
    {
        using var process = Process.GetCurrentProcess();
        var exePath = Environment.ProcessPath
                      ?? process.MainModule?.FileName
                      ?? $"{AppName}.exe";

        var isAdmin = AdminHelper.IsAdministrator();

        var osDisplayName = BuildWindowsDisplayName();

        var fw = RuntimeInformation.FrameworkDescription.Trim();
        var ver = typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        return new AppInfo(exePath, isAdmin, osDisplayName, fw, ver);
    }

    private static string BuildWindowsDisplayName()
    {
        // 1) Accurate build number without manifest/version-lie issues
        var (major, minor, build) = TryGetRealNtVersion() ?? (0u, 0u, 0u);

        // 2) Full build like winver shows: Build + UBR (revision)
        var (displayVersion, ubrFromReg) = ReadDisplayVersionAndUbr();
        var ubr = ubrFromReg;

        // 3) “Real” product name: winver uses Windows branding (winbrand.dll)
        // Prefer %WINDOWS_LONG% (it returns "Windows 11 Pro" on Win11; %WINDOWS_SHORT% can still say Windows 10 on some builds).
        var brandedName = TryGetBrandingString("%WINDOWS_LONG%");

        // 4) Fallback: registry product name can be "Windows 10" even on Windows 11 (happens on many systems)
        var regProductName = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");

        var name = FirstNonEmpty(brandedName, regProductName, "Windows");

        // If registry fallback lies (Windows 10) but build is clearly Win11, fix it.
        // Windows 11 started at build 22000 (client). This avoids breaking Win10 (190xx) and most server builds.
        if (IsLikelyWindows11(major, minor, build) && name.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
            name = ReplaceWindows10With11(name);

        name = name.Trim();

        var buildStr = build > 0
            ? (ubr.HasValue ? $"Build {build}.{ubr.Value}" : $"Build {build}")
            : "Build ?";

        // Avoid duplicating DisplayVersion if it's already included in branding (rare, but safe).
        var dv = string.IsNullOrWhiteSpace(displayVersion) ? null : displayVersion.Trim();
        var includeDv = dv is not null && !name.Contains(dv, StringComparison.OrdinalIgnoreCase);

        return includeDv
            ? $"{name} {dv} ({buildStr})"
            : $"{name} ({buildStr})";
    }

    private static bool IsLikelyWindows11(uint major, uint minor, uint build)
    {
        // Internally Windows 11 is still major=10, minor=0.
        // Client Windows 11 initial build is 22000.
        return major == 10 && minor == 0 && build >= 22000;
    }

    private static string ReplaceWindows10With11(string s)
    {
        // Minimal, safe replace. Keeps edition text: "Windows 10 Pro" -> "Windows 11 Pro"
        // If localized/other casing, we handle common case-insensitive patterns.
        var idx = s.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return s;

        // Preserve original casing for "Windows" part as-is, just replace "10" with "11"
        // by replacing the matched substring with "Windows 11".
        var before = s[..idx];
        var after = s[(idx + "Windows 10".Length)..];
        return before + "Windows 11" + after;
    }

    private static (string? displayVersion, int? ubr) ReadDisplayVersionAndUbr()
    {
        // DisplayVersion is recommended over ReleaseId (ReleaseId is deprecated / frozen on newer builds).
        // UBR gives the full build revision like winver (e.g. 26200.7462).
        var displayVersion = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion")
                             ?? ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ReleaseId");

        int? ubr = null;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
            var ubrObj = key?.GetValue("UBR");
            if (ubrObj is int ubrInt)
                ubr = ubrInt;
        }
        catch
        {
            // ignore
        }

        return (displayVersion, ubr);
    }

    private static string? ReadRegistryString(string subKeyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static (uint major, uint minor, uint build)? TryGetRealNtVersion()
    {
        try
        {
            var info = new RTL_OSVERSIONINFOEXW();
            info.dwOSVersionInfoSize = (uint)Marshal.SizeOf<RTL_OSVERSIONINFOEXW>();

            var status = RtlGetVersion(ref info);
            if (status != 0)
                return null;

            return (info.dwMajorVersion, info.dwMinorVersion, info.dwBuildNumber);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetBrandingString(string token)
    {
        // winver uses winbrand.dll BrandingFormatString internally.
        // The returned PWSTR must be freed with GlobalFree to avoid leaks.
        var ptr = IntPtr.Zero;
        try
        {
            ptr = BrandingFormatString(token);
            if (ptr == IntPtr.Zero)
                return null;

            var s = Marshal.PtrToStringUni(ptr)?.Trim();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (ptr != IntPtr.Zero)
                _ = GlobalFree(ptr);
        }
    }

    private static string FirstNonEmpty(params string?[] items)
        => items.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? string.Empty;

    // ===== P/Invoke =====

    // RtlGetVersion is a reliable way to get the real OS build without manifest/version-lie issues.
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEXW lpVersionInformation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RTL_OSVERSIONINFOEXW
    {
        public uint dwOSVersionInfoSize;
        public uint dwMajorVersion;
        public uint dwMinorVersion;
        public uint dwBuildNumber;
        public uint dwPlatformId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;

        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    [DllImport("winbrand.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr BrandingFormatString(string format);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
