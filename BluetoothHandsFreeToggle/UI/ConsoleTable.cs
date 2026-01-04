using BluetoothHandsFreeToggle.Core;

namespace BluetoothHandsFreeToggle.Ui;

public static class ConsoleTable
{
    public static void PrintStatusTable(IEnumerable<ServiceSnapshot> snapshots)
    {
        var rows = snapshots.Select(s => new[]
        {
            s.FriendlyName,
            s.Exists ? "Yes" : "No",
            s.Exists ? s.RunState.ToString() : "-",
            s.Exists ? s.StartType.ToString() : "-",
            s.RegistryStartValue?.ToString() ?? "-",
        }).ToList();

        var headers = new[] { "Component", "Present", "State", "Startup", "RegStart" };
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

        ConsoleHelpers.WithColor(ConsoleColor.DarkCyan, () =>
        {
            Console.WriteLine(Line(headers));
            Console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        });

        foreach (var r in rows)
            Console.WriteLine(Line(r));
    }
}