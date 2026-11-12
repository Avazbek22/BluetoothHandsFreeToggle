namespace BluetoothHandsFreeToggle.Ui;

using BluetoothHandsFreeToggle.Localization;

public static class ConsoleHelpers
{
    public static void WriteHeader(string text)
    {
        WithColor(ConsoleColor.Cyan, () =>
        {
            Console.WriteLine(text);
            Console.WriteLine($"\r\n{Text.Get("common.developedBy")}");
            Console.WriteLine(new string('=', Math.Max(10, text.Length)) + "\r\n");
        });
    }

    public static void WriteSuccess(string text) => WithColor(ConsoleColor.Green, () => Console.WriteLine(text));
    public static void WriteWarning(string text) => WithColor(ConsoleColor.Yellow, () => Console.WriteLine(text));
    public static void WriteError(string text) => WithColor(ConsoleColor.Red, () => Console.WriteLine(text));
    public static void WriteInfo(string text) => WithColor(ConsoleColor.Gray, () => Console.WriteLine(text));

    public static void WithColor(ConsoleColor color, Action action)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        try { action(); }
        finally { Console.ForegroundColor = old; }
    }

    public static void Pause(string? message = null)
    {
        if (Console.IsInputRedirected)
            return;

        Console.WriteLine();
        WithColor(
            ConsoleColor.DarkGray,
            () => Console.WriteLine(message ?? Text.Get("common.pressAnyKey")));

        // Keys pressed while an operation was running must not skip its report.
        DiscardPendingInput();
        Console.ReadKey(true);
    }

    public static string? ReadMenuChoice()
    {
        DiscardPendingInput();
        Console.Write(Text.Get("common.select"));
        return Console.ReadLine()?.Trim();
    }

    /// <summary>
    /// Asks for Enter (continue) or Esc (cancel). Single keys work regardless of the active
    /// keyboard layout, unlike typed answers such as "y" or "1".
    /// </summary>
    public static bool ConfirmWithEnter(string prompt)
    {
        Console.WriteLine();
        WithColor(ConsoleColor.White, () => Console.WriteLine(prompt));

        if (Console.IsInputRedirected)
        {
            var answer = Console.ReadLine()?.Trim();
            return answer is not null &&
                   (answer.Length == 0 ||
                    answer.Equals("1", StringComparison.Ordinal) ||
                    answer.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                    answer.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }

        DiscardPendingInput();
        while (true)
        {
            var key = Console.ReadKey(true).Key;
            if (key is ConsoleKey.Enter)
                return true;

            if (key is ConsoleKey.Escape)
                return false;
        }
    }

    public static void DiscardPendingInput()
    {
        if (Console.IsInputRedirected)
            return;

        try
        {
            while (Console.KeyAvailable)
                _ = Console.ReadKey(true);
        }
        catch (InvalidOperationException)
        {
            // No console input is attached.
        }
    }

    public static void TryClearScreen()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            // Some redirected and legacy console hosts do not support clearing.
        }
    }
}
