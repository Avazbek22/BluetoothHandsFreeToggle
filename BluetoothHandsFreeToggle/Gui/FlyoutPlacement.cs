using Avalonia;

namespace BluetoothHandsFreeToggle.Gui;

/// <summary>
/// Where the panel window goes: in the corner next to the notification area, on the side of the
/// taskbar (bottom, top, left or right), sized to fit the working area of that screen.
/// </summary>
/// <param name="Width">Window width in device-independent pixels, including the shadow margin.</param>
/// <param name="Height">Window height in device-independent pixels, including the shadow margin.</param>
/// <param name="Position">Top-left corner of the window in physical screen pixels.</param>
public readonly record struct FlyoutPlacement(double Width, double Height, PixelPoint Position)
{
    /// <summary>Gap between the visible panel and the taskbar or screen edge, in DIPs.</summary>
    public const double ScreenGap = 12;

    /// <summary>The panel never gets shorter than this, even on a tiny working area.</summary>
    public const double MinPanelHeight = 320;

    /// <param name="bounds">The whole screen, physical pixels.</param>
    /// <param name="workingArea">The screen without the taskbar, physical pixels.</param>
    /// <param name="scaling">Display scale of that screen (1.5 for 150 %).</param>
    /// <param name="panelWidth">Visible panel width, DIPs.</param>
    /// <param name="panelHeight">Preferred visible panel height, DIPs.</param>
    /// <param name="margin">Transparent margin around the panel for its shadow, DIPs (0 when opaque).</param>
    public static FlyoutPlacement Compute(
        PixelRect bounds,
        PixelRect workingArea,
        double scaling,
        double panelWidth,
        double panelHeight,
        double margin)
    {
        if (!(scaling > 0) || double.IsInfinity(scaling))
            scaling = 1;

        var available = (workingArea.Height / scaling) - (2 * ScreenGap);
        var height = Math.Max(Math.Min(panelHeight, available), Math.Min(MinPanelHeight, panelHeight));

        // From the working-area edge to the window edge on the far side of the panel.
        var right = (int)Math.Round((ScreenGap + panelWidth + margin) * scaling);
        var down = (int)Math.Round((ScreenGap + height + margin) * scaling);
        var inset = (int)Math.Round((ScreenGap - margin) * scaling);

        var taskbarOnLeft = workingArea.X > bounds.X;
        var taskbarOnTop = workingArea.Y > bounds.Y && workingArea.Bottom >= bounds.Bottom;

        var x = taskbarOnLeft ? workingArea.X + inset : workingArea.Right - right;
        var y = taskbarOnTop ? workingArea.Y + inset : workingArea.Bottom - down;

        return new FlyoutPlacement(panelWidth + (2 * margin), height + (2 * margin), new PixelPoint(x, y));
    }
}
