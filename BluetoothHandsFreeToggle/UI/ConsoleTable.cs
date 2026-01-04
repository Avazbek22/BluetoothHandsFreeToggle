using BluetoothHandsFreeToggle.Core;

namespace BluetoothHandsFreeToggle.Ui;

public static class ConsoleTable
{
    public static void PrintStatusTable(IEnumerable<ServiceSnapshot> snapshots)
    {
        var rows = snapshots.Select(s => new[]
        {
            s.ServiceName,
            s.Exists ? "Yes" : "No",
            s.RunState.ToString(),
            s.StartType.ToString(),
            s.RegistryStartValue?.ToString() ?? "-",
            s.FriendlyName
        }).ToList();

        var headers = new[] { "Service", "Exists", "State", "Startup", "RegStart", "Description" };
        PrintTable(headers, rows);
    }

    private static void PrintTable(string[] headers, List<string[]> rows)
    {
        var widths = new int[headers.Length];
        for (var i = 0; i < headers.Length; i++)
            widths[i] = headers[i].Length;

        foreach (var r in rows)
        {
            for (var i = 0; i < r.Length; i++)
                widths[i] = Math.Max(widths[i], r[i]?.Length ?? 0);
        }

        string Line(string[] cols)
        {
            return string.Join("  ", cols.Select((c, i) => (c ?? "").PadRight(widths[i])));
        }

        Console.WriteLine(Line(headers));
        Console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));

        foreach (var r in rows)
            Console.WriteLine(Line(r));
    }
}