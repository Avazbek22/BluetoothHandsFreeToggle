using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;

namespace BluetoothHandsFreeToggle.Gui.Tray;

public sealed record TrayMenuItem(int Id, string Text, bool Enabled = true, bool Checked = false, bool IsDefault = false)
{
    public static TrayMenuItem Separator { get; } = new(0, "");
}

/// <summary>
/// The notification-area icon, implemented directly on Shell_NotifyIcon (version 4) so the panel
/// can be anchored to the exact icon position and notifications can be shown. A hidden top-level
/// window receives the icon's messages and re-adds the icon when Explorer restarts.
/// </summary>
public sealed unsafe class NotifyIcon : IDisposable
{
    private const uint IconId = 1;
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1

    private const uint WmNull = 0x0000;
    private const uint WmDestroy = 0x0002;
    private const uint WmSettingChange = 0x001A;
    private const uint WmContextMenu = 0x007B;
    private const uint WmTimer = 0x0113;
    private const uint MsgfltAllow = 1;
    private const nuint RetryTimerId = 1;
    private const uint RetryIntervalMs = 3000;
    private const uint NinSelect = 0x0400;
    private const uint NinKeySelect = 0x0401;
    private const uint NinBalloonUserClick = 0x0405;

    private const uint NimAdd = 0x0;
    private const uint NimModify = 0x1;
    private const uint NimDelete = 0x2;
    private const uint NimSetVersion = 0x4;
    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;
    private const uint NifInfo = 0x10;
    private const uint NifShowTip = 0x80;
    private const uint NiifUser = 0x4;
    private const uint NiifLargeIcon = 0x20;
    private const uint NotifyIconVersion4 = 4;

    private const uint MfString = 0x0;
    private const uint MfGrayed = 0x1;
    private const uint MfChecked = 0x8;
    private const uint MfSeparator = 0x800;
    private const uint TpmRightButton = 0x2;
    private const uint TpmBottomAlign = 0x20;
    private const uint TpmRightAlign = 0x8;
    private const uint TpmReturnCmd = 0x100;

    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
    private static readonly Dictionary<IntPtr, NotifyIcon> Instances = [];

    private readonly IntPtr _hwnd;
    private readonly string _className;
    private IntPtr _icon;
    private IntPtr _balloonIcon;
    private string _tooltip = "";
    private bool _added;
    private bool _retrying;
    private bool _disposed;

    public NotifyIcon()
    {
        _className = $"BluetoothHandsFreeToggle.Tray.{Environment.ProcessId}";
        var instance = GetModuleHandleW(null);

        fixed (char* className = _className)
        {
            var windowClass = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                WndProc = &WndProc,
                Instance = instance,
                ClassName = className
            };

            if (RegisterClassExW(&windowClass) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            // A real (never shown) top-level window: message-only windows miss the TaskbarCreated broadcast.
            _hwnd = CreateWindowExW(0, className, className, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        }

        if (_hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        Instances[_hwnd] = this;

        // When the app runs as administrator, Explorer (a lower integrity level) may otherwise not
        // deliver clicks or the TaskbarCreated broadcast to this window.
        _ = ChangeWindowMessageFilterEx(_hwnd, CallbackMessage, MsgfltAllow, IntPtr.Zero);
        if (TaskbarCreatedMessage != 0)
            _ = ChangeWindowMessageFilterEx(_hwnd, TaskbarCreatedMessage, MsgfltAllow, IntPtr.Zero);

        DarkMenus.Enable(_hwnd);
    }

    /// <summary>Left click or Enter on the icon; the argument is the icon's screen rectangle.</summary>
    public event EventHandler<PixelRect?>? Activated;

    /// <summary>Right click or Shift+F10; the argument is the point to open the menu at.</summary>
    public event EventHandler<PixelPoint>? ContextMenuRequested;

    public event EventHandler? BalloonClicked;

    /// <summary>The taskbar theme or the display scale changed, or Explorer restarted: redraw the icon.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>Sets the icon (PNG data at the small-icon size) and the tooltip, adding the icon on first use.</summary>
    public void Update(byte[] png, int size, string tooltip)
    {
        ArgumentNullException.ThrowIfNull(png);

        var icon = CreateIcon(png, size);
        if (icon != IntPtr.Zero)
        {
            if (_icon != IntPtr.Zero)
                _ = DestroyIcon(_icon);
            _icon = icon;
        }

        _tooltip = tooltip;
        Apply(_added ? NimModify : NimAdd);
    }

    /// <summary>Optional large icon for notifications (PNG data).</summary>
    public void SetBalloonIcon(byte[] png, int size)
    {
        var icon = CreateIcon(png, size);
        if (icon == IntPtr.Zero)
            return;

        if (_balloonIcon != IntPtr.Zero)
            _ = DestroyIcon(_balloonIcon);
        _balloonIcon = icon;
    }

    public void ShowNotification(string title, string text)
    {
        if (!_added)
            return;

        var data = CreateData(NifInfo);
        Copy(title, data.InfoTitle, 64);
        Copy(text, data.Info, 256);
        if (_balloonIcon != IntPtr.Zero)
        {
            data.InfoFlags = NiifUser | NiifLargeIcon;
            data.BalloonIcon = _balloonIcon;
        }

        _ = ShellNotifyIconW(NimModify, &data);
    }

    /// <summary>The icon's current rectangle on screen, in physical pixels.</summary>
    public PixelRect? GetIconRect()
    {
        var identifier = new NotifyIconIdentifier
        {
            Size = (uint)sizeof(NotifyIconIdentifier),
            Window = _hwnd,
            Id = IconId
        };

        if (ShellNotifyIconGetRect(&identifier, out var rect) != 0)
            return null;

        return new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>Shows a native context menu and returns the chosen item id, or 0.</summary>
    public int ShowMenu(IReadOnlyList<TrayMenuItem> items, PixelPoint position)
    {
        ArgumentNullException.ThrowIfNull(items);

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return 0;

        try
        {
            foreach (var item in items)
            {
                if (ReferenceEquals(item, TrayMenuItem.Separator))
                {
                    _ = AppendMenuW(menu, MfSeparator, 0, null);
                    continue;
                }

                var flags = MfString | (item.Enabled ? 0 : MfGrayed) | (item.Checked ? MfChecked : 0);
                _ = AppendMenuW(menu, flags, (nuint)item.Id, item.Text);
                if (item.IsDefault)
                    _ = SetMenuDefaultItem(menu, (uint)item.Id, 0);
            }

            // Required so the menu closes when the user clicks elsewhere.
            _ = SetForegroundWindow(_hwnd);
            var chosen = TrackPopupMenuEx(
                menu,
                TpmReturnCmd | TpmRightButton | TpmBottomAlign | TpmRightAlign,
                position.X,
                position.Y,
                _hwnd,
                IntPtr.Zero);
            _ = PostMessageW(_hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);
            return chosen;
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopRetry();
        if (_added)
        {
            var data = CreateData(0);
            _ = ShellNotifyIconW(NimDelete, &data);
            _added = false;
        }

        if (_icon != IntPtr.Zero)
            _ = DestroyIcon(_icon);
        if (_balloonIcon != IntPtr.Zero)
            _ = DestroyIcon(_balloonIcon);

        Instances.Remove(_hwnd);
        _ = DestroyWindow(_hwnd);
        _ = UnregisterClassW(_className, GetModuleHandleW(null));
    }

    private void Apply(uint message)
    {
        if (_disposed || _icon == IntPtr.Zero)
            return;

        var data = CreateData(NifMessage | NifIcon | NifTip | NifShowTip);
        data.CallbackMessage = CallbackMessage;
        data.Icon = _icon;
        Copy(_tooltip, data.Tip, 128);

        if (ShellNotifyIconW(message, &data) != 0)
        {
            if (message == NimAdd)
                Added(ref data);
            return;
        }

        if (message == NimModify)
        {
            // The icon is gone (Explorer restarted and the broadcast was missed): add it again.
            _added = false;
            Apply(NimAdd);
            return;
        }

        // Adding fails while Explorer is still starting at sign-in, and under load it can time out
        // although the icon was added. Updating tells the two apart; otherwise try again shortly.
        if (ShellNotifyIconW(NimModify, &data) != 0)
        {
            Added(ref data);
            return;
        }

        StartRetry();
    }

    private void Added(ref NotifyIconData data)
    {
        _added = true;
        StopRetry();
        data.TimeoutOrVersion = NotifyIconVersion4;
        fixed (NotifyIconData* pointer = &data)
            _ = ShellNotifyIconW(NimSetVersion, pointer);
    }

    private void StartRetry()
    {
        if (_retrying)
            return;

        _retrying = SetTimer(_hwnd, RetryTimerId, RetryIntervalMs, IntPtr.Zero) != 0;
    }

    private void StopRetry()
    {
        if (!_retrying)
            return;

        _retrying = false;
        _ = KillTimer(_hwnd, RetryTimerId);
    }

    private NotifyIconData CreateData(uint flags) => new()
    {
        Size = (uint)sizeof(NotifyIconData),
        Window = _hwnd,
        Id = IconId,
        Flags = flags
    };

    private static void Copy(string value, char* destination, int capacity)
    {
        var length = Math.Min(value.Length, capacity - 1);
        for (var i = 0; i < length; i++)
            destination[i] = value[i];
        destination[length] = '\0';
    }

    private static IntPtr CreateIcon(byte[] png, int size)
    {
        fixed (byte* data = png)
        {
            // Icon resources may hold PNG data since Windows Vista.
            return CreateIconFromResourceEx(data, (uint)png.Length, 1, 0x00030000, size, size, 0);
        }
    }

    private IntPtr HandleMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            var notification = (uint)(lParam.ToInt64() & 0xFFFF);
            var x = (short)(wParam.ToInt64() & 0xFFFF);
            var y = (short)((wParam.ToInt64() >> 16) & 0xFFFF);

            switch (notification)
            {
                case NinSelect:
                case NinKeySelect:
                    Activated?.Invoke(this, GetIconRect());
                    break;

                case WmContextMenu:
                    ContextMenuRequested?.Invoke(this, new PixelPoint(x, y));
                    break;

                case NinBalloonUserClick:
                    BalloonClicked?.Invoke(this, EventArgs.Empty);
                    break;
            }

            return IntPtr.Zero;
        }

        if (message == TaskbarCreatedMessage && TaskbarCreatedMessage != 0)
        {
            // Explorer restarted, or the display scale changed: the icon is gone and may need a
            // different size. Subscribers redraw it (which adds it); otherwise add the old one.
            _added = false;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
            if (!_added)
                Apply(NimAdd);
            return IntPtr.Zero;
        }

        if (message == WmTimer && (nuint)wParam == RetryTimerId)
        {
            if (_added)
                StopRetry();
            else
                Apply(NimAdd);
            return IntPtr.Zero;
        }

        if (message == WmSettingChange && lParam != IntPtr.Zero &&
            Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        return DefWindowProcW(_hwnd, message, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (message != WmDestroy && Instances.TryGetValue(hwnd, out var instance))
                return instance.HandleMessage(message, wParam, lParam);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Never let an exception cross the native boundary.
            BluetoothHandsFreeToggle.App.CrashLog.Write($"Tray message 0x{message:X4} failed", exception);
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    /// <summary>Lets native menus follow the dark Windows theme (Windows 10 1903 and later).</summary>
    private static class DarkMenus
    {
        private const int AllowDark = 1;

        public static void Enable(IntPtr hwnd)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
                return;

            try
            {
                _ = SetPreferredAppMode(AllowDark);
                _ = AllowDarkModeForWindow(hwnd, true);
                FlushMenuThemes();
            }
            catch (EntryPointNotFoundException)
            {
                // Undocumented exports; light menus are an acceptable fallback.
            }
        }

        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int SetPreferredAppMode(int mode);

        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowDarkModeForWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool allow);

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern void FlushMenuThemes();
    }

    // ----- Native structures and functions -----

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public char* MenuName;
        public char* ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint TimeoutOrVersion;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public Guid Item;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern ushort RegisterClassExW(WndClassEx* windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, IntPtr instance);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle, char* className, char* windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ShellNotifyIconW(uint message, NotifyIconData* data);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ShellNotifyIconGetRect(NotifyIconIdentifier* identifier, out NativeRect rect);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CreateIconFromResourceEx(
        byte* data, uint size, int isIcon, uint version, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nuint SetTimer(IntPtr hwnd, nuint id, uint elapse, IntPtr timerProc);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(IntPtr hwnd, nuint id);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeFilterStruct);
}
