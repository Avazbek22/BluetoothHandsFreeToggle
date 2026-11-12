using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace BluetoothHandsFreeToggle.Windows;

/// <summary>
/// Minimal, source-generated (trim- and AOT-safe) Core Audio interop: the MMDevice API and the
/// WASAPI session API. Interfaces declare their vtable slots in the exact order of the Windows SDK
/// headers (mmdeviceapi.h, audiopolicy.h, propsys.h), up to the last slot used. Every method keeps
/// its HRESULT, and every interface pointer is returned raw so <see cref="ComRef{T}"/> can release
/// it deterministically. Nothing here creates an IAudioClient, so no audio stream is ever opened.
/// </summary>
internal static class CoreAudioInterop
{
    public static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    public static StrategyBasedComWrappers ComWrappers { get; } = new();

    public const int ClsctxAll = 0x17;
    public const int DataFlowCapture = 1;
    public const int RoleConsole = 0;
    public const int RoleCommunications = 2;
    public const uint DeviceStateActive = 0x1;
    public const int StgmRead = 0;
    public const int AudioSessionStateActive = 1;
    public const int CoinitMultithreaded = 0x0;

    public const int SOk = 0;
    public const int ENotFound = unchecked((int)0x80070490);

    // AUDCLNT_S_NO_SINGLE_PROCESS: the session spans processes; the PID returned is the initial one.
    public const int AudclntSNoSingleProcess = 0x0889000D;

    private const ushort VtLpwstr = 31;
    private const ushort VtClsid = 72;

    public static readonly PropertyKey DeviceFriendlyName =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public static readonly PropertyKey DeviceContainerId =
        new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);

    [DllImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int CoCreateInstance(
        in Guid classId,
        IntPtr outer,
        int classContext,
        in Guid interfaceId,
        out IntPtr instance);

    [DllImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int PropVariantClear(ref PropVariant value);

    public static string? TryGetString(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(in key, out var value) != SOk)
            return null;

        try
        {
            return value.VarType == VtLpwstr && value.Value != IntPtr.Zero
                ? Marshal.PtrToStringUni(value.Value)
                : null;
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    public static Guid? TryGetGuid(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(in key, out var value) != SOk)
            return null;

        try
        {
            return value.VarType == VtClsid && value.Value != IntPtr.Zero
                ? Marshal.PtrToStructure<Guid>(value.Value)
                : null;
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    /// <summary>Takes ownership of a CoTaskMem string returned by a COM method.</summary>
    public static string? TakeString(IntPtr value)
    {
        if (value == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUni(value);
        }
        finally
        {
            Marshal.FreeCoTaskMem(value);
        }
    }

    public static string FormatHResult(string operation, int hresult)
    {
        var message = Marshal.GetPInvokeErrorMessage(hresult);
        return string.IsNullOrWhiteSpace(message) || message.StartsWith("Unknown error", StringComparison.Ordinal)
            ? $"{operation}: 0x{hresult:X8}"
            : $"{operation}: 0x{hresult:X8} ({message.Trim()})";
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PropertyKey(Guid FormatId, int PropertyId);

/// <summary>
/// PROPVARIANT: an 8-byte header and a pointer-sized two-slot union (16 bytes on x86, 24 bytes on
/// x64 and ARM64), matching the native layout on every supported architecture.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort VarType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Value;
    public IntPtr Value2;
}

/// <summary>Owns one COM reference and releases it on dispose instead of waiting for the GC.</summary>
internal sealed class ComRef<T> : IDisposable where T : class
{
    private readonly ComObject _object;

    private ComRef(ComObject comObject, T value)
    {
        _object = comObject;
        Value = value;
    }

    public T Value { get; }

    /// <summary>Wraps an owned interface pointer; the pointer's reference is transferred.</summary>
    public static ComRef<T>? TryWrap(IntPtr unknown)
    {
        if (unknown == IntPtr.Zero)
            return null;

        ComObject comObject;
        try
        {
            comObject = (ComObject)CoreAudioInterop.ComWrappers.GetOrCreateObjectForComInstance(
                unknown,
                CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(unknown);
        }

        if (comObject is T value)
            return new ComRef<T>(comObject, value);

        comObject.FinalRelease();
        return null;
    }

    public void Dispose() => _object.FinalRelease();
}

[GeneratedComInterface]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr endpoint);
}

[GeneratedComInterface]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal partial interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int Item(uint index, out IntPtr device);
}

[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    [PreserveSig]
    int Activate(in Guid interfaceId, int classContext, IntPtr activationParams, out IntPtr instance);

    [PreserveSig]
    int OpenPropertyStore(int access, out IntPtr properties);

    [PreserveSig]
    int GetId(out IntPtr id);
}

[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, out PropertyKey key);

    [PreserveSig]
    int GetValue(in PropertyKey key, out PropVariant value);
}

[GeneratedComInterface]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
internal partial interface IAudioSessionManager2
{
    // IAudioSessionManager
    [PreserveSig]
    int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr sessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr audioVolume);

    // IAudioSessionManager2
    [PreserveSig]
    int GetSessionEnumerator(out IntPtr sessionEnumerator);
}

[GeneratedComInterface]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
internal partial interface IAudioSessionEnumerator
{
    [PreserveSig]
    int GetCount(out int count);

    [PreserveSig]
    int GetSession(int index, out IntPtr session);
}

[GeneratedComInterface]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
internal partial interface IAudioSessionControl2
{
    // IAudioSessionControl
    [PreserveSig]
    int GetState(out int state);

    [PreserveSig]
    int GetDisplayName(out IntPtr displayName);

    [PreserveSig]
    int SetDisplayName(IntPtr displayName, IntPtr eventContext);

    [PreserveSig]
    int GetIconPath(out IntPtr iconPath);

    [PreserveSig]
    int SetIconPath(IntPtr iconPath, IntPtr eventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid groupingParam);

    [PreserveSig]
    int SetGroupingParam(in Guid groupingParam, IntPtr eventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IntPtr notifications);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IntPtr notifications);

    // IAudioSessionControl2
    [PreserveSig]
    int GetSessionIdentifier(out IntPtr sessionIdentifier);

    [PreserveSig]
    int GetSessionInstanceIdentifier(out IntPtr sessionInstanceIdentifier);

    [PreserveSig]
    int GetProcessId(out uint processId);

    /// <returns>S_OK for the system-sounds session, S_FALSE otherwise.</returns>
    [PreserveSig]
    int IsSystemSoundsSession();
}
