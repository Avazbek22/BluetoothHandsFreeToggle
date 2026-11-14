using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace StereoKeeper.Gui;

public enum Tone
{
    Green,
    Red,
    Amber,
    Light,
    Muted
}

/// <summary>The approved flyout palette: graphite surfaces, one red accent, green for success.</summary>
public static class Palette
{
    public static readonly Color Accent = Color.Parse("#E5484D");
    public static readonly Color AccentButton = Shade(Accent, 0.88);
    public static readonly Color AccentText = Tint(Accent, 0.25);
    public static readonly Color Green = Color.Parse("#3DD68C");
    public static readonly Color Amber = Color.Parse("#F5B94A");
    public static readonly Color Light = Color.Parse("#EDEDEF");
    public static readonly Color Muted = Color.Parse("#96969E");
    public static readonly Color Sub = Color.Parse("#B4B4BB");

    // One brush per colour: a new brush object on every refresh would look like a change to the
    // bindings and restart their colour transitions.
    private static readonly IBrush GreenBrush = Brush(Green);
    private static readonly IBrush RedTextBrush = Brush(AccentText);
    private static readonly IBrush AmberBrush = Brush(Amber);
    private static readonly IBrush LightBrush = Brush(Light);
    private static readonly IBrush MutedBrush = Brush(Muted);
    private static readonly IBrush AccentRing = Brush(Accent);
    private static readonly IBrush MutedRing = Brush(Color.FromArgb(0x47, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush GreenChip = Brush(Color.FromArgb(31, Green.R, Green.G, Green.B));
    private static readonly IBrush RedChip = Brush(Color.FromArgb(38, Accent.R, Accent.G, Accent.B));
    private static readonly IBrush AmberChip = Brush(Color.FromArgb(33, Amber.R, Amber.G, Amber.B));
    private static readonly IBrush NeutralChip = Brush(Color.FromArgb(20, 255, 255, 255));

    public static IBrush Foreground(Tone tone) => tone switch
    {
        Tone.Green => GreenBrush,
        Tone.Red => RedTextBrush,
        Tone.Amber => AmberBrush,
        Tone.Light => LightBrush,
        _ => MutedBrush
    };

    /// <summary>The ring colour: the accent itself for red, so the ring matches the main button.</summary>
    public static IBrush Ring(Tone tone) => tone switch
    {
        Tone.Red => AccentRing,
        Tone.Muted => MutedRing,
        _ => Foreground(tone)
    };

    public static IBrush ChipBackground(Tone tone) => tone switch
    {
        Tone.Green => GreenChip,
        Tone.Red => RedChip,
        Tone.Amber => AmberChip,
        _ => NeutralChip
    };

    public static Color ToneColor(Tone tone) => tone switch
    {
        Tone.Green => Green,
        Tone.Red => Accent,
        Tone.Amber => Amber,
        Tone.Light => Light,
        _ => Muted
    };

    private static ImmutableSolidColorBrush Brush(Color color) => new(color);

    private static Color Shade(Color color, double factor)
        => Color.FromRgb(Scale(color.R, factor), Scale(color.G, factor), Scale(color.B, factor));

    private static Color Tint(Color color, double factor)
        => Color.FromRgb(Mix(color.R, factor), Mix(color.G, factor), Mix(color.B, factor));

    private static byte Scale(byte value, double factor) => (byte)Math.Round(value * factor);

    private static byte Mix(byte value, double factor) => (byte)Math.Round(value + ((255 - value) * factor));
}
