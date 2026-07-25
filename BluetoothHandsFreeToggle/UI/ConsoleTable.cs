using System.Globalization;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.Ui;

public static class ConsoleTable
{
    public static void PrintStatusTable(IEnumerable<ServiceSnapshot> snapshots)
    {
        var rows = snapshots.Select(snapshot => new[]
        {
            snapshot.FriendlyName,
            PresenceText(snapshot),
            snapshot.Exists ? ServiceStateText.Get(snapshot.RunState) : "-",
            snapshot.Exists ? ServiceStateText.Get(snapshot.StartType) : "-",
            snapshot.NativeStartValue?.ToString(CultureInfo.InvariantCulture) ?? "-"
        }).ToList();

        var headers = new[]
        {
            Text.Get("table.component"),
            Text.Get("table.present"),
            Text.Get("table.state"),
            Text.Get("table.startup"),
            Text.Get("table.startCode")
        };
        PrintTable(headers, rows);
    }

    private static string PresenceText(ServiceSnapshot snapshot)
        => !snapshot.QuerySucceeded
            ? Text.Get("common.error")
            : snapshot.Exists
                ? Text.Get("common.yes")
                : Text.Get("common.no");

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
