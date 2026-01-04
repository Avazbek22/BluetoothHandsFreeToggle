using BluetoothHandsFreeToggle.Core;

namespace BluetoothHandsFreeToggle.App;

public sealed class CommandLine
{
    private readonly string[] _args;

    public CommandLine(string[] args) => _args = args;

    public bool TryHandleNonInteractive(AppInfo appInfo)
    {
        // Supported:
        //  status
        //  disable
        //  enable
        //
        // Internal elevated child mode:
        //  --elevated --action disable|enable --pipe <name> [--no-registry] [--no-start]
        //
        if (_args.Length == 0)
            return false;

        var args = _args.Select(a => a.Trim()).Where(a => a.Length > 0).ToArray();

        if (args.Contains("--elevated", StringComparer.OrdinalIgnoreCase))
        {
            return HandleElevatedChild(appInfo, args);
        }

        var cmd = args[0].ToLowerInvariant();
        if (cmd is not ("status" or "disable" or "enable"))
            return false;

        var options = ToggleOptions.FromArgs(args.Skip(1).ToArray());

        var engine = new ToggleEngine();

        if (cmd == "status")
        {
            var report = engine.GetStatusReport();
            Console.WriteLine(report.ToPrettyText());
            return true;
        }

        // disable/enable from CLI:
        // if not admin -> elevate and wait (non-interactive)
        if (!appInfo.IsAdministrator)
        {
            var elevatedArgs = BuildElevatedArgs(cmd, options);
            var result = ElevationIpc.RunElevatedAndWait(appInfo.ExePath, elevatedArgs, TimeSpan.FromSeconds(60));

            Console.WriteLine(result.Title);
            foreach (var line in result.Lines)
                Console.WriteLine(line);

            Environment.ExitCode = result.Success ? 0 : 1;
            return true;
        }

        // already admin
        var res = cmd == "disable"
            ? engine.DisableHandsFree(options)
            : engine.EnableHandsFree(options);

        Console.WriteLine(res.ToPrettyText());
        Environment.ExitCode = res.Success ? 0 : 1;
        return true;
    }

    private static bool HandleElevatedChild(AppInfo appInfo, string[] args)
    {
        // Must be admin. If somehow not, return with a failure message through pipe if possible.
        var pipeName = GetArgValue(args, "--pipe");
        var action = GetArgValue(args, "--action");

        var options = ToggleOptions.FromArgs(args);

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            Console.WriteLine("Missing --pipe argument.");
            Environment.ExitCode = 2;
            return true;
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            return Reply(pipeName, new ElevationIpc.ElevatedResult(false, "Invalid arguments", new[]
            {
                "Missing --action disable|enable."
            }));
        }

        if (!appInfo.IsAdministrator)
        {
            return Reply(pipeName, new ElevationIpc.ElevatedResult(false, "Not elevated", new[]
            {
                "This process is not running as Administrator.",
                "No changes were made."
            }));
        }

        var engine = new ToggleEngine();

        return ElevationIpc.RunAsElevatedChildAndReply(pipeName, () =>
        {
            var report = action.Equals("disable", StringComparison.OrdinalIgnoreCase)
                ? engine.DisableHandsFree(options)
                : action.Equals("enable", StringComparison.OrdinalIgnoreCase)
                    ? engine.EnableHandsFree(options)
                    : ToggleReport.Fail("Invalid action", new[] { "Supported actions: disable, enable." });

            return new ElevationIpc.ElevatedResult(
                report.Success,
                report.Title,
                report.ToPrettyLines());
        }) is 0;
    }

    private static bool Reply(string pipeName, ElevationIpc.ElevatedResult result)
    {
        ElevationIpc.RunAsElevatedChildAndReply(pipeName, () => result);
        Environment.ExitCode = result.Success ? 0 : 1;
        return true;
    }

    private static string[] BuildElevatedArgs(string cmd, ToggleOptions options)
    {
        var list = new List<string>
        {
            "--elevated",
            "--action",
            cmd
        };

        if (!options.ApplyRegistry)
            list.Add("--no-registry");
        if (!options.StartServicesOnEnable)
            list.Add("--no-start");

        return list.ToArray();
    }

    private static string? GetArgValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
