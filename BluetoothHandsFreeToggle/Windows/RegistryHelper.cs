using Microsoft.Win32;

namespace BluetoothHandsFreeToggle.Windows;

public sealed class RegistryHelper
{
    public int? TryGetRegistryStartValue(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: false);
            if (key is null) return null;

            var val = key.GetValue("Start");
            return val is int i ? i : null;
        }
        catch
        {
            return null;
        }
    }

    public bool TrySetRegistryStartValue(string serviceName, int startValue, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
            if (key is null)
            {
                error = "Registry key not found.";
                return false;
            }

            key.SetValue("Start", startValue, RegistryValueKind.DWord);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            error = "Access denied (requires Administrator).";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}