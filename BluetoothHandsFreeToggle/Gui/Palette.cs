using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace BluetoothHandsFreeToggle.Gui;

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

    public static IBrush Foreground(Tone tone) => tone switch
    {
        Tone.Green => new ImmutableSolidColorBrush(Green),
        Tone.Red => new ImmutableSolidColorBrush(AccentText),
        Tone.Amber => new ImmutableSolidColorBrush(Amber),
        Tone.Light => new ImmutableSolidColorBrush(Light),
        _ => new ImmutableSolidColorBrush(Muted)
    };

    /// <summary>The ring colour: the accent itself for red, so the ring matches the main button.</summary>
    public static IBrush Ring(Tone tone) => tone switch
    {
        Tone.Red => new ImmutableSolidColorBrush(Accent),
        Tone.Muted => new ImmutableSolidColorBrush(Color.FromArgb(0x47, 0xFF, 0xFF, 0xFF)),
        _ => Foreground(tone)
    };

    public static IBrush ChipBackground(Tone tone) => tone switch
    {
        Tone.Green => new ImmutableSolidColorBrush(Color.FromArgb(31, Green.R, Green.G, Green.B)),
        Tone.Red => new ImmutableSolidColorBrush(Color.FromArgb(38, Accent.R, Accent.G, Accent.B)),
        Tone.Amber => new ImmutableSolidColorBrush(Color.FromArgb(33, Amber.R, Amber.G, Amber.B)),
        _ => new ImmutableSolidColorBrush(Color.FromArgb(20, 255, 255, 255))
    };

    public static Color ToneColor(Tone tone) => tone switch
    {
        Tone.Green => Green,
        Tone.Red => Accent,
        Tone.Amber => Amber,
        Tone.Light => Light,
        _ => Muted
    };

    private static Color Shade(Color color, double factor)
        => Color.FromRgb(Scale(color.R, factor), Scale(color.G, factor), Scale(color.B, factor));

    private static Color Tint(Color color, double factor)
        => Color.FromRgb(Mix(color.R, factor), Mix(color.G, factor), Mix(color.B, factor));

    private static byte Scale(byte value, double factor) => (byte)Math.Round(value * factor);

    private static byte Mix(byte value, double factor) => (byte)Math.Round(value + ((255 - value) * factor));
}
