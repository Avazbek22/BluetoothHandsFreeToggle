using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.Ui;

public sealed class MenuLoop(AppInfo appInfo, ToggleEngine engine)
{
    public void Run()
    {
        while (true)
        {
            ConsoleHelpers.TryClearScreen();
            RenderHeader();

            Console.WriteLine($"[1] {Text.Get("menu.status")}");
            PrintActionLine(2, Text.Get("menu.soft"), requiresAdmin: true);
            PrintActionLine(3, Text.Get("menu.hard"), requiresAdmin: true);
            PrintActionLine(4, Text.Get("menu.restore"), requiresAdmin: true);
            Console.WriteLine($"[5] {Text.Get("menu.help")}");
            Console.WriteLine($"[6] {Text.Get("menu.about")}");
            Console.WriteLine($"[7] {Text.Get("menu.changeLanguage")}");
            Console.WriteLine($"[0] {Text.Get("menu.exit")}");
            Console.WriteLine();

            switch (ConsoleHelpers.ReadMenuChoice())
            {
                case "0":
                    return;
                case "1":
                    ShowStatus();
                    break;
                case "2":
                    RunAction(Text.Get("menu.soft"), engine.SoftResetHandsFree);
                    break;
                case "3":
                    if (ConfirmHardMode())
                        RunAction(Text.Get("menu.hard"), engine.HardDisableHandsFree);
                    break;
                case "4":
                    RunAction(Text.Get("menu.restore"), engine.RestoreHandsFree);
                    break;
                case "5":
                    ShowDocumentation(Text.Get("screen.help"), DocumentationDocument.Help);
                    break;
                case "6":
                    ShowDocumentation(Text.Get("screen.about"), DocumentationDocument.About);
                    break;
                case "7":
                    Text.ToggleLanguage();
                    break;
                default:
                    ConsoleHelpers.WriteWarning(Text.Get("common.invalidChoice"));
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
            () => Console.WriteLine(
                $"[{id}] {text} ({Text.Get("menu.adminRequiredSuffix")})"));
    }

    private void RenderHeader()
    {
        ConsoleHelpers.WriteHeader(AppInfo.AppName);
        Console.WriteLine($"{Text.Get("header.version")}: {appInfo.Version}");
        Console.WriteLine($"{Text.Get("header.os")}: {appInfo.OsDisplayName}");
        Console.WriteLine($"{Text.Get("header.runtime")}: {appInfo.FrameworkDescription}");

        if (appInfo.IsAdministrator)
            ConsoleHelpers.WithColor(
                ConsoleColor.Green,
                () => Console.WriteLine(Text.Get("header.adminYes")));
        else
            ConsoleHelpers.WithColor(
                ConsoleColor.Yellow,
                () => Console.WriteLine(Text.Get("header.adminNo")));

        Console.WriteLine();
    }

    private void ShowStatus()
    {
        ConsoleHelpers.TryClearScreen();
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
            ConsoleHelpers.WriteWarning(Text.Format("ui.actionRequiresAdmin", actionName));
            ConsoleHelpers.WriteInfo(Text.Get("ui.restartAndAllowUac"));
            ConsoleHelpers.Pause();
            return;
        }

        ShowReport(action());
    }

    private bool ConfirmHardMode()
    {
        if (!appInfo.IsAdministrator)
        {
            ConsoleHelpers.WriteWarning(
                Text.Format("ui.actionRequiresAdmin", Text.Get("menu.hard")));
            ConsoleHelpers.Pause();
            return false;
        }

        ConsoleHelpers.TryClearScreen();
        RenderHeader();
        ConsoleHelpers.WriteWarning(Text.Get("ui.hard.warningGlobal"));
        ConsoleHelpers.WriteWarning(Text.Get("ui.hard.warningMicrophone"));
        Console.Write(Text.Get("ui.hard.confirm"));

        var answer = Console.ReadLine()?.Trim();
        return answer is not null &&
               (answer.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                answer.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                answer.Equals("д", StringComparison.OrdinalIgnoreCase) ||
                answer.Equals("да", StringComparison.OrdinalIgnoreCase));
    }

    private void ShowReport(ToggleReport report)
    {
        ConsoleHelpers.TryClearScreen();
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

    private static void ShowDocumentation(
        string title,
        DocumentationDocument document)
    {
        ConsoleHelpers.TryClearScreen();
        ConsoleHelpers.WriteHeader(AppInfo.AppName);
        ConsoleHelpers.WithColor(
            ConsoleColor.White,
            () => Console.WriteLine(title));
        Console.WriteLine();

        foreach (var line in DocumentationProvider.GetLines(document))
            Console.WriteLine(line);

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
