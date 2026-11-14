namespace StereoKeeper.App;

public enum LaunchMode
{
    /// <summary>The tray app with its flyout (default).</summary>
    Gui,

    /// <summary>A short-lived elevated process that runs one operation for the tray app.</summary>
    ElevatedWorker
}

public enum OperationKind
{
    Soft,
    Hard,
    Restore
}

public sealed record LaunchOptions(
    LaunchMode Mode,
    bool StartHidden = false,
    OperationKind? Operation = null,
    string? PipeName = null,
    string? LanguageCode = null)
{
    public const string TrayArgument = "--tray";
    public const string WorkerArgument = "--elevated-worker";
    public const string PipeArgument = "--pipe";
    public const string LanguageArgument = "--lang";

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Contains(WorkerArgument, StringComparer.OrdinalIgnoreCase))
        {
            return new LaunchOptions(
                LaunchMode.ElevatedWorker,
                Operation: TryParseOperation(ValueAfter(args, WorkerArgument)),
                PipeName: ValueAfter(args, PipeArgument),
                LanguageCode: ValueAfter(args, LanguageArgument));
        }

        return new LaunchOptions(
            LaunchMode.Gui,
            StartHidden: args.Contains(TrayArgument, StringComparer.OrdinalIgnoreCase));
    }

    public static string FormatWorkerArguments(OperationKind operation, string pipeName, string languageCode)
        => $"{WorkerArgument} {ToArgument(operation)} {PipeArgument} {pipeName} {LanguageArgument} {languageCode}";

    public static string ToArgument(OperationKind operation) => operation switch
    {
        OperationKind.Soft => "soft",
        OperationKind.Hard => "hard",
        OperationKind.Restore => "restore",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
    };

    public static OperationKind? TryParseOperation(string? value) => value?.ToUpperInvariant() switch
    {
        "SOFT" => OperationKind.Soft,
        "HARD" => OperationKind.Hard,
        "RESTORE" => OperationKind.Restore,
        _ => null
    };

    private static string? ValueAfter(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }
}
