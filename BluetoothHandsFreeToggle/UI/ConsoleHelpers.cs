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
        Console.ReadKey(true);
    }

    public static string? ReadMenuChoice()
    {
        Console.Write(Text.Get("common.select"));
        return Console.ReadLine()?.Trim();
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
