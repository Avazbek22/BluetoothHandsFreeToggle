using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;

namespace BluetoothHandsFreeToggle.Ui;

public sealed class MenuLoop
{
    private readonly AppInfo _appInfo;
    private readonly ToggleEngine _engine;

    public MenuLoop(AppInfo appInfo, ToggleEngine engine)
    {
        _appInfo = appInfo;
        _engine = engine;
    }

    public void Run()
    {
        while (true)
        {
            Console.Clear();
            RenderHeader();

            Console.WriteLine("[1] Hands-Free Service Status");
            Console.WriteLine("[2] Disable Hands-Free Services");
            Console.WriteLine("[3] Enable Hands-Free Services");
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
                RunActionWithSmartElevation("disable");
                continue;
            }

            if (choice == "3")
            {
                RunActionWithSmartElevation("enable");
                continue;
            }

            ConsoleHelpers.Pause("Invalid choice. Press any key...");
        }
    }

    private void RenderHeader()
    {
        ConsoleHelpers.WriteHeader("BluetoothHandsFreeToggle");
        Console.WriteLine($"OS: {_appInfo.OsDescription}");
        Console.WriteLine($"Runtime: {_appInfo.FrameworkDescription}");
        Console.WriteLine($"Admin: {(_appInfo.IsAdministrator ? "Yes" : "No")}");
        Console.WriteLine();
    }

    private void ShowStatus()
    {
        Console.Clear();
        RenderHeader();

        var report = _engine.GetStatusReport();
        ConsoleTable.PrintStatusTable(report.Snapshots);

        if (report.Lines.Count > 0)
        {
            Console.WriteLine();
            foreach (var line in report.Lines)
                Console.WriteLine(line);
        }

        ConsoleHelpers.Pause();
    }

    private void RunActionWithSmartElevation(string action)
    {
        // Run disable/enable. If not admin, elevate a child process and collect result via pipe.
        var options = new ToggleOptions
        {
            ApplyRegistry = true,
            StartServicesOnEnable = true
        };

        if (_appInfo.IsAdministrator)
        {
            var report = action == "disable"
                ? _engine.DisableHandsFree(options)
                : _engine.EnableHandsFree(options);

            ShowReport(report);
            return;
        }

        var elevatedArgs = new[]
        {
            "--elevated",
            "--action",
            action
        };

        var result = ElevationIpc.RunElevatedAndWait(_appInfo.ExePath, elevatedArgs, TimeSpan.FromSeconds(60));

        Console.Clear();
        RenderHeader();

        ConsoleHelpers.WriteHeader(result.Title);
        foreach (var line in result.Lines)
            Console.WriteLine(line);

        ConsoleHelpers.Pause();
    }

    private void ShowReport(ToggleReport report)
    {
        Console.Clear();
        RenderHeader();

        ConsoleHelpers.WriteHeader(report.Title);
        foreach (var line in report.ToPrettyLines())
            Console.WriteLine(line);

        ConsoleHelpers.Pause();
    }
}
