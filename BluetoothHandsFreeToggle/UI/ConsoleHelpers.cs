namespace BluetoothHandsFreeToggle.Ui;

public static class ConsoleHelpers
{
    public static void WriteHeader(string text)
    {
        Console.WriteLine(text);
        Console.WriteLine(new string('=', Math.Max(10, text.Length)));
    }

    public static void Pause(string message = "Press any key to continue...")
    {
        Console.WriteLine();
        Console.WriteLine(message);
        Console.ReadKey(true);
    }

    public static string ReadMenuChoice()
    {
        Console.Write("Select: ");
        return (Console.ReadLine() ?? "").Trim();
    }
}