using System.Globalization;
using System.Text;

namespace BluetoothHandsFreeToggle.Ui;

public static class ConsoleText
{
    public static int GetDisplayWidth(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var width = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.EnclosingMark or
                UnicodeCategory.Format)
            {
                continue;
            }

            width += IsWide(rune.Value) ? 2 : 1;
        }

        return width;
    }

    public static string PadRight(string value, int totalWidth)
    {
        ArgumentNullException.ThrowIfNull(value);
        var padding = Math.Max(0, totalWidth - GetDisplayWidth(value));
        return value + new string(' ', padding);
    }

    private static bool IsWide(int value)
        => value is >= 0x1100 and <= 0x115F or
           0x231A or 0x231B or 0x2329 or 0x232A or
           >= 0x23E9 and <= 0x23EC or 0x23F0 or 0x23F3 or
           >= 0x25FD and <= 0x25FE or
           >= 0x2614 and <= 0x2615 or
           >= 0x2648 and <= 0x2653 or 0x267F or 0x2693 or 0x26A1 or
           >= 0x26AA and <= 0x26AB or
           >= 0x26BD and <= 0x26BE or
           >= 0x26C4 and <= 0x26C5 or 0x26CE or 0x26D4 or 0x26EA or
           >= 0x26F2 and <= 0x26F3 or 0x26F5 or 0x26FA or 0x26FD or
           0x2705 or >= 0x270A and <= 0x270B or 0x2728 or 0x274C or
           0x274E or >= 0x2753 and <= 0x2755 or 0x2757 or
           >= 0x2795 and <= 0x2797 or 0x27B0 or 0x27BF or
           >= 0x2B1B and <= 0x2B1C or 0x2B50 or 0x2B55 or
           >= 0x2E80 and <= 0xA4CF or
           >= 0xAC00 and <= 0xD7A3 or
           >= 0xF900 and <= 0xFAFF or
           >= 0xFE10 and <= 0xFE19 or
           >= 0xFE30 and <= 0xFE6F or
           >= 0xFF01 and <= 0xFF60 or
           >= 0xFFE0 and <= 0xFFE6 or
           >= 0x1F004 and <= 0x1FAFF or
           >= 0x20000 and <= 0x3FFFD;
}
