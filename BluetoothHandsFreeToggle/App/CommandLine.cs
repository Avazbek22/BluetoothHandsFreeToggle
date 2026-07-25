using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Localization;

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

        if (args[0].Equals("language", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("lang", StringComparison.OrdinalIgnoreCase))
        {
            return HandleLanguageCommand(args);
        }

        if (args.Length != 1)
        {
            WriteUsageError(Text.Get("cli.extraArguments"));
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
            WriteUsageError(Text.Format("cli.unknownCommand", args[0]));
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
            Console.Error.WriteLine(Text.Get("cli.invalidElevatedArguments"));
            Environment.ExitCode = 2;
            return true;
        }

        if (!appInfo.IsAdministrator)
        {
            var notElevated = new ElevationIpc.ElevatedResult(
                false,
                Text.Get("cli.notElevatedTitle"),
                [Text.Get("cli.notElevated"), Text.Get("cli.noChanges")]);

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
            _ => ToggleReport.Failed(
                Text.Get("title.invalidAction"),
                [Text.Format("cli.unsupportedAction", action)])
        };

    private static bool HandleLanguageCommand(string[] args)
    {
        if (args.Length != 2)
        {
            WriteUsageError(Text.Get("cli.languageUsage"));
            return true;
        }

        if (!AppLanguageCode.TryParse(args[1], out var language))
        {
            WriteUsageError(Text.Format("cli.invalidLanguage", args[1]));
            return true;
        }

        Text.SetLanguage(language);
        Console.WriteLine(Text.Get("cli.languageChanged"));
        Environment.ExitCode = 0;
        return true;
    }

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
        Console.Error.WriteLine(Text.Get("cli.runHelp"));
        Environment.ExitCode = 2;
    }

    private static void WriteHelp()
    {
        Console.WriteLine(AppInfo.AppName);
        Console.WriteLine();
        Console.WriteLine(Text.Get("cli.help.usage"));
        Console.WriteLine("  BluetoothHandsFreeToggle status");
        Console.WriteLine("  BluetoothHandsFreeToggle soft");
        Console.WriteLine("  BluetoothHandsFreeToggle hard");
        Console.WriteLine("  BluetoothHandsFreeToggle restore");
        Console.WriteLine("  BluetoothHandsFreeToggle language <en|ru>");
        Console.WriteLine();
        Console.WriteLine(Text.Get("cli.help.commands"));
        Console.WriteLine(Text.Get("cli.help.status"));
        Console.WriteLine(Text.Get("cli.help.soft"));
        Console.WriteLine(Text.Get("cli.help.softDetail"));
        Console.WriteLine(Text.Get("cli.help.hard"));
        Console.WriteLine(Text.Get("cli.help.hardDetail"));
        Console.WriteLine(Text.Get("cli.help.restore"));
        Console.WriteLine(Text.Get("cli.help.language"));
        Console.WriteLine();
        Console.WriteLine(Text.Get("cli.help.aliases"));
        Console.WriteLine(Text.Get("cli.help.aliasDisable"));
        Console.WriteLine(Text.Get("cli.help.aliasEnable"));
    }
}
