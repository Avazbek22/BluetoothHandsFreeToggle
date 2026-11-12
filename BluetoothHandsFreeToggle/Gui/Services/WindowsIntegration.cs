using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using BluetoothHandsFreeToggle.App;
using Microsoft.Win32;

namespace BluetoothHandsFreeToggle.Gui.Services;

/// <summary>Starts the tray app at sign-in through the per-user Run key (no administrator rights).</summary>
public sealed class RegistryAutostart(string executablePath) : IAutostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = AppInfo.AppName;

    private string Command => $"\"{executablePath}\" {LaunchOptions.TrayArgument}";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                if (run?.GetValue(ValueName) is not string)
                    return false;

                // Task Manager's "Disable" keeps the Run value and marks it here (odd first byte).
                using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedPath);
                return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                run.SetValue(ValueName, Command, RegistryValueKind.String);
                using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedPath, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Points an enabled entry at this executable again after the app was moved or updated.</summary>
    public void RefreshPath()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (run?.GetValue(ValueName) is string current &&
                !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
            {
                run.SetValue(ValueName, Command, RegistryValueKind.String);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Best effort.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GuiSettings))]
internal sealed partial class GuiSettingsJsonContext : JsonSerializerContext;

public sealed class FileGuiSettingsStore(string? filePath = null) : IGuiSettingsStore
{
    private readonly string _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppInfo.AppName,
        "gui-settings.json");

    public GuiSettings Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(_filePath), GuiSettingsJsonContext.Default.GuiSettings)
                  ?? new GuiSettings()
                : new GuiSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new GuiSettings();
        }
    }

    public void Save(GuiSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, GuiSettingsJsonContext.Default.GuiSettings));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; the app keeps working with the values in memory.
        }
    }
}

internal static class ShellCommands
{
    public static void OpenUrl(string url) => Start(url);

    /// <summary>The Recording tab of the classic Sound dialog, where the default microphone is chosen.</summary>
    public static void OpenSoundRecordingSettings()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "control.exe"),
                Arguments = "mmsys.cpl,,1",
                UseShellExecute = false
            });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Start("ms-settings:sound");
        }
    }

    private static void Start(string target)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing to fall back to; the link is also shown in Help.
        }
    }
}
