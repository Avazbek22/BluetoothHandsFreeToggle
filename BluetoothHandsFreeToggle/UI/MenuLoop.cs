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
            PrintActionLine(2, "Disable Hands-Free Services (HFP)", requiresAdmin: true);
            PrintActionLine(3, "Enable Hands-Free Services (HFP)", requiresAdmin: true);
            Console.WriteLine("[0] Exit");
            Console.WriteLine();

            var choice = ConsoleHelpers.ReadMenuChoice();

            if (choice == "0")
                return;

            if (choice == "1")
            {
                ShowStatus();
                continue;
            }

            if (choice == "2")
            {
                RunDisable();
                continue;
            }

            if (choice == "3")
            {
                RunEnable();
                continue;
            }

            ConsoleHelpers.WriteWarning("Invalid choice.");
            ConsoleHelpers.Pause();
        }
    }

    private void PrintActionLine(int id, string text, bool requiresAdmin)
    {
        var isAllowed = !requiresAdmin || appInfo.IsAdministrator;

        if (isAllowed)
        {
            Console.WriteLine($"[{id}] {text}");
            return;
        }

        ConsoleHelpers.WithColor(ConsoleColor.DarkGray, () =>
        {
            Console.WriteLine($"[{id}] {text} (Admin required)");
        });
    }

    private void RenderHeader()
    {
        ConsoleHelpers.WriteHeader(AppInfo.AppName);
        Console.WriteLine($"OS: {appInfo.OsDisplayName}");
        Console.WriteLine($"Runtime: {appInfo.FrameworkDescription}");

        if (appInfo.IsAdministrator)
            ConsoleHelpers.WithColor(ConsoleColor.Green, () => Console.WriteLine("Admin: Yes"));
        else
            ConsoleHelpers.WithColor(ConsoleColor.Yellow, () => Console.WriteLine("Admin: No (read-only mode)"));

        Console.WriteLine();
    }

    private void ShowStatus()
    {
        Console.Clear();
        RenderHeader();

        var report = engine.GetStatusReport();
        ConsoleTable.PrintStatusTable(report.Snapshots);

        ConsoleHelpers.Pause();
    }

    private void RunDisable()
    {
        if (!appInfo.IsAdministrator)
        {
            ConsoleHelpers.WriteWarning("This action requires Administrator.");
            ConsoleHelpers.WriteInfo("Please restart the app and allow UAC once.");
            ConsoleHelpers.Pause();
            return;
        }

        var options = new ToggleOptions
        {
            ApplyRegistry = true,
            StartServicesOnEnable = true
        };

        var report = engine.DisableHandsFree(options);
        ShowReport(report);
    }

    private void RunEnable()
    {
        if (!appInfo.IsAdministrator)
        {
            ConsoleHelpers.WriteWarning("This action requires Administrator.");
            ConsoleHelpers.WriteInfo("Please restart the app and allow UAC once.");
            ConsoleHelpers.Pause();
            return;
        }

        var options = new ToggleOptions
        {
            ApplyRegistry = true,
            StartServicesOnEnable = true
        };

        var report = engine.EnableHandsFree(options);
        ShowReport(report);
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

        foreach (var line in report.Lines)
            Console.WriteLine(line);

        Console.WriteLine();
        ConsoleTable.PrintStatusTable(report.Snapshots);

        ConsoleHelpers.Pause();
    }
}
