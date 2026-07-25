using BluetoothHandsFreeToggle.Core;

namespace BluetoothHandsFreeToggle.Ui;

public static class ConsoleTable
{
    public static void PrintStatusTable(IEnumerable<ServiceSnapshot> snapshots)
    {
        var rows = snapshots.Select(snapshot => new[]
        {
            snapshot.FriendlyName,
            PresenceText(snapshot),
            snapshot.Exists ? snapshot.RunState.ToString() : "-",
            snapshot.Exists ? snapshot.StartType.ToString() : "-",
            snapshot.NativeStartValue?.ToString() ?? "-"
        }).ToList();

        var headers = new[] { "Component", "Present", "State", "Startup", "StartCode" };
        PrintTable(headers, rows);
    }

    private static string PresenceText(ServiceSnapshot snapshot)
        => !snapshot.QuerySucceeded ? "Error" : snapshot.Exists ? "Yes" : "No";

    private static void PrintTable(string[] headers, List<string[]> rows)
    {
        var widths = new int[headers.Length];
        for (var index = 0; index < headers.Length; index++)
            widths[index] = headers[index].Length;

        foreach (var row in rows)
        {
            for (var index = 0; index < row.Length; index++)
                widths[index] = Math.Max(widths[index], row[index].Length);
        }

        string FormatLine(string[] columns)
            => string.Join(
                "  ",
                columns.Select((column, index) => column.PadRight(widths[index])));

        ConsoleHelpers.WithColor(ConsoleColor.DarkCyan, () =>
        {
            Console.WriteLine(FormatLine(headers));
            Console.WriteLine(string.Join("  ", widths.Select(width => new string('-', width))));
        });

        foreach (var row in rows)
            Console.WriteLine(FormatLine(row));
    }
}
