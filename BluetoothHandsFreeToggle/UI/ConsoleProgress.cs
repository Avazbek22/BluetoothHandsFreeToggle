using BluetoothHandsFreeToggle.Core;

namespace BluetoothHandsFreeToggle.Ui;

/// <summary>
/// Renders live operation events synchronously. "Waiting" updates overwrite a single line
/// in place so a long wait shows a ticking counter instead of a frozen screen.
/// </summary>
public sealed class ConsoleProgress : IProgress<OperationEvent>
{
    private int _openLineWidth;

    public void Report(OperationEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Kind is OperationEventKind.Waiting && !Console.IsOutputRedirected)
        {
            WriteInPlace("   " + value.Message);
            return;
        }

        Complete();

        switch (value.Kind)
        {
            case OperationEventKind.Step:
                ConsoleHelpers.WriteInfo("> " + value.Message);
                break;
            case OperationEventKind.Waiting:
                ConsoleHelpers.WriteInfo("   " + value.Message);
                break;
            case OperationEventKind.Warning:
                ConsoleHelpers.WriteWarning("[WARN] " + value.Message);
                break;
            case OperationEventKind.Success:
                ConsoleHelpers.WriteSuccess("[OK] " + value.Message);
                break;
        }
    }

    /// <summary>Ends an in-place waiting line, if one is open.</summary>
    public void Complete()
    {
        if (_openLineWidth == 0)
            return;

        Console.WriteLine();
        _openLineWidth = 0;
    }

    private void WriteInPlace(string text)
    {
        text = Truncate(text, MaxLineWidth());
        var width = ConsoleText.GetDisplayWidth(text);
        var padding = new string(' ', Math.Max(0, _openLineWidth - width));

        ConsoleHelpers.WithColor(
            ConsoleColor.DarkGray,
            () => Console.Write("\r" + text + padding));
        _openLineWidth = width;
    }

    private static int MaxLineWidth()
    {
        try
        {
            // One column short of the window, so the cursor never wraps to a new line.
            return Math.Max(20, Console.WindowWidth - 1);
        }
        catch (IOException)
        {
            return 119;
        }
    }

    private static string Truncate(string text, int maxWidth)
    {
        if (ConsoleText.GetDisplayWidth(text) <= maxWidth)
            return text;

        var result = new System.Text.StringBuilder();
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeWidth = ConsoleText.GetDisplayWidth(rune.ToString());
            if (width + runeWidth > maxWidth - 3)
                break;

            result.Append(rune.ToString());
            width += runeWidth;
        }

        return result.Append("...").ToString();
    }
}
