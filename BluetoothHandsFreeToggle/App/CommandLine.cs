using BluetoothHandsFreeToggle.Core;

namespace BluetoothHandsFreeToggle.App;

public sealed class CommandLine(string[] arguments)
{
    private static readonly TimeSpan ElevatedOperationTimeout = TimeSpan.FromSeconds(90);
    private readonly string[] _arguments = arguments;

    public bool TryHandleNonInteractive(AppInfo appInfo)
    {
        if (_arguments.Length == 0)
            return false;

        var args = _arguments.Where(argument => !string.IsNullOrWhiteSpace(argument)).ToArray();
        if (args.Length == 0)
            return false;

        if (args[0].Equals("--elevated", StringComparison.OrdinalIgnoreCase))
            return HandleElevatedChild(appInfo, args);

        if (args.Length != 1)
        {
            WriteUsageError("Commands do not accept additional arguments.");
            return true;
        }

        var command = args[0].ToLowerInvariant();
        if (command is "help" or "--help" or "-h" or "/?")
        {
            WriteHelp();
            return true;
        }

        if (command is not ("status" or "soft" or "hard" or "restore" or "disable" or "enable"))
        {
            WriteUsageError($"Unknown command: {args[0]}");
            return true;
        }

        var normalizedAction = command switch
        {
            "disable" => "hard",
            "enable" => "restore",
            _ => command
        };

        var engine = new ToggleEngine();
        if (normalizedAction == "status")
        {
            PrintReport(engine.GetStatusReport());
            return true;
        }

        if (!appInfo.IsAdministrator)
        {
            var result = ElevationIpc.RunElevatedAndWait(
                appInfo.ExePath,
                ["--elevated", "--action", normalizedAction],
                ElevatedOperationTimeout);

            Console.WriteLine(result.Title);
            foreach (var line in result.Lines)
                Console.WriteLine(line);

            Environment.ExitCode = result.Success ? 0 : 1;
            return true;
        }

        var report = RunAction(engine, normalizedAction);
        PrintReport(report);
        return true;
    }

    private static bool HandleElevatedChild(AppInfo appInfo, string[] args)
    {
        var pipeName = GetArgValue(args, "--pipe");
        var token = GetArgValue(args, "--token");
        var action = GetArgValue(args, "--action")?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(pipeName) ||
            string.IsNullOrWhiteSpace(token) ||
            action is not ("soft" or "hard" or "restore"))
        {
            Console.Error.WriteLine("Invalid elevated-child arguments.");
            Environment.ExitCode = 2;
            return true;
        }

        if (!appInfo.IsAdministrator)
        {
            var notElevated = new ElevationIpc.ElevatedResult(
                false,
                "NOT ELEVATED",
                ["The child process is not running as Administrator.", "No changes were made."]);

            Environment.ExitCode = ElevationIpc.RunAsElevatedChildAndReply(
                pipeName,
                token,
                () => notElevated);
            return true;
        }

        Environment.ExitCode = ElevationIpc.RunAsElevatedChildAndReply(
            pipeName,
            token,
            () =>
            {
                var report = RunAction(new ToggleEngine(), action);
                return new ElevationIpc.ElevatedResult(
                    report.Success,
                    report.Title,
                    report.ToPrettyLines());
            });

        return true;
    }

    private static ToggleReport RunAction(ToggleEngine engine, string action)
        => action switch
        {
            "soft" => engine.SoftResetHandsFree(),
            "hard" => engine.HardDisableHandsFree(),
            "restore" => engine.RestoreHandsFree(),
            _ => ToggleReport.Failed("INVALID ACTION", [$"Unsupported action: {action}"])
        };

    private static void PrintReport(ToggleReport report)
    {
        Console.WriteLine(report.ToPrettyText());
        Environment.ExitCode = report.Success ? 0 : 1;
    }

    private static string? GetArgValue(string[] args, string key)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(key, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static void WriteUsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Run with --help to see supported commands.");
        Environment.ExitCode = 2;
    }

    private static void WriteHelp()
    {
        Console.WriteLine(
            """
            BluetoothHandsFreeToggle

            Usage:
              BluetoothHandsFreeToggle status
              BluetoothHandsFreeToggle soft
              BluetoothHandsFreeToggle hard
              BluetoothHandsFreeToggle restore

            Commands:
              status   Show HFP service and backup state. Does not require Administrator.
              soft     Restart active HFP services without changing startup configuration.
                       This can clear a stuck HFP session while keeping the microphone available.
              hard     Stop and disable HFP services to force high-quality playback.
                       The Bluetooth microphone is unavailable until restore is used.
              restore  Restore the startup and running state saved by hard mode.

            Backward-compatible aliases:
              disable = hard
              enable  = restore
            """);
    }
}
