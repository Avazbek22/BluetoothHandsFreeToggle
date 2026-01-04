using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BluetoothHandsFreeToggle.App;

public sealed class AppInfo
{
    public string ExePath { get; }
    public bool IsAdministrator { get; }
    public string OsDescription { get; }
    public string FrameworkDescription { get; }
    public string Version { get; }

    private AppInfo(string exePath, bool isAdministrator, string osDescription, string frameworkDescription, string version)
    {
        ExePath = exePath;
        IsAdministrator = isAdministrator;
        OsDescription = osDescription;
        FrameworkDescription = frameworkDescription;
        Version = version;
    }

    public static AppInfo Create()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? "BluetoothHandsFreeToggle.exe";
        var isAdmin = AdminHelper.IsAdministrator();
        var os = RuntimeInformation.OSDescription.Trim();
        var fw = RuntimeInformation.FrameworkDescription.Trim();
        var ver = typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        return new AppInfo(exePath, isAdmin, os, fw, ver);
    }
}