using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;

namespace BluetoothHandsFreeToggle.Ui;

public sealed class MenuLoop(AppInfo appInfo, ToggleEngine engine)
{
    public void Run()
    {
        while (true)
        {
            Console.Clear();
            RenderHeader();

            Console.WriteLine("[1] Get status");
            PrintActionLine(2, "Soft reset (preserve HFP microphone)", requiresAdmin: true);
            PrintActionLine(3, "Hard mode (force high-quality playback, disable HFP microphone)", requiresAdmin: true);
            PrintActionLine(4, "Restore HFP microphone and original service state", requiresAdmin: true);
            Console.WriteLine("[0] Exit");
            Console.WriteLine();

            switch (ConsoleHelpers.ReadMenuChoice())
            {
                case "0":
                    return;
                case "1":
                    ShowStatus();
                    break;
                case "2":
                    RunAction("Soft reset", engine.SoftResetHandsFree);
                    break;
                case "3":
                    if (ConfirmHardMode())
                        RunAction("Hard mode", engine.HardDisableHandsFree);
                    break;
                case "4":
                    RunAction("Restore", engine.RestoreHandsFree);
                    break;
                default:
                    ConsoleHelpers.WriteWarning("Invalid choice.");
                    ConsoleHelpers.Pause();
                    break;
            }
        }
    }

    private void PrintActionLine(int id, string text, bool requiresAdmin)
    {
        if (!requiresAdmin || appInfo.IsAdministrator)
        {
            Console.WriteLine($"[{id}] {text}");
            return;
        }

        ConsoleHelpers.WithColor(
            ConsoleColor.DarkGray,
            () => Console.WriteLine($"[{id}] {text} (Admin required)"));
    }

    private void RenderHeader()
    {
        ConsoleHelpers.WriteHeader(AppInfo.AppName);
        Console.WriteLine($"Version: {appInfo.Version}");
        Console.WriteLine($"OS: {appInfo.OsDisplayName}");
        Console.WriteLine($"Runtime: {appInfo.FrameworkDescription}");

        if (appInfo.IsAdministrator)
            ConsoleHelpers.WithColor(ConsoleColor.Green, () => Console.WriteLine("Admin: Yes"));
        else
            ConsoleHelpers.WithColor(
                ConsoleColor.Yellow,
                () => Console.WriteLine("Admin: No (read-only mode)"));

        Console.WriteLine();
    }

    private void ShowStatus()
    {
        Console.Clear();
        RenderHeader();

        var report = engine.GetStatusReport();
        WriteReportLines(report);
        ConsoleTable.PrintStatusTable(report.Snapshots);
        ConsoleHelpers.Pause();
    }

    private void RunAction(string actionName, Func<ToggleReport> action)
    {
        if (!appInfo.IsAdministrator)
        {
            ConsoleHelpers.WriteWarning($"{actionName} requires Administrator.");
            ConsoleHelpers.WriteInfo("Restart the app and allow the UAC prompt.");
            ConsoleHelpers.Pause();
            return;
        }

        ShowReport(action());
    }

    private bool ConfirmHardMode()
    {
        if (!appInfo.IsAdministrator)
        {
            ConsoleHelpers.WriteWarning("Hard mode requires Administrator.");
            ConsoleHelpers.Pause();
            return false;
        }

        Console.Clear();
        RenderHeader();
        ConsoleHelpers.WriteWarning("Hard mode disables Bluetooth Classic HFP system-wide.");
        ConsoleHelpers.WriteWarning("The Bluetooth headset microphone will not work until Restore is used.");
        Console.Write("Continue? [y/N]: ");

        return string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowReport(ToggleReport report)
    {
        Console.Clear();
        RenderHeader();

        if (report.Success)
            ConsoleHelpers.WriteSuccess(report.Title);
        else
            ConsoleHelpers.WriteError(report.Title);

        Console.WriteLine(new string('-', Math.Max(10, report.Title.Length)));
        WriteReportLines(report);

        if (report.Snapshots.Count > 0)
            ConsoleTable.PrintStatusTable(report.Snapshots);

        ConsoleHelpers.Pause();
    }

    private static void WriteReportLines(ToggleReport report)
    {
        foreach (var line in report.Lines)
        {
            if (line.StartsWith("[ERROR]", StringComparison.Ordinal))
                ConsoleHelpers.WriteError(line);
            else if (line.StartsWith("[WARN]", StringComparison.Ordinal))
                ConsoleHelpers.WriteWarning(line);
            else if (line.StartsWith("[OK]", StringComparison.Ordinal))
                ConsoleHelpers.WriteSuccess(line);
            else
                Console.WriteLine(line);
        }

        if (report.Lines.Count > 0)
            Console.WriteLine();
    }
}
