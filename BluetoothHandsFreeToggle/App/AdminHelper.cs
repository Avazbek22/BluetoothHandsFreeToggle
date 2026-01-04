using System.Diagnostics;
using System.Security.Principal;

namespace BluetoothHandsFreeToggle.App;

public static class AdminHelper
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryRelaunchAsAdministrator(string exePath, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = BuildArgs(args),
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC.
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildArgs(string[] args)
    {
        if (args.Length == 0) return string.Empty;
        return string.Join(" ", args.Select(QuoteIfNeeded));
    }

    private static string QuoteIfNeeded(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "\"\"";

        if (s.Any(char.IsWhiteSpace) || s.Contains('"'))
            return "\"" + s.Replace("\"", "\\\"") + "\"";

        return s;
    }
}