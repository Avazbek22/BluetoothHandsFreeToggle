using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace BluetoothHandsFreeToggle.Gui.Controls;

/// <summary>
/// The status ring: a faint track and an arc from twelve o'clock with round caps. Value changes
/// and colour changes animate smoothly.
/// </summary>
public sealed class ProgressRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> RingBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(RingBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(
            nameof(TrackBrush),
            new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Thickness), 8);

    static ProgressRing()
    {
        AffectsRender<ProgressRing>(ValueProperty, RingBrushProperty, TrackBrushProperty, ThicknessProperty);
    }

    public ProgressRing()
    {
        Transitions =
        [
            new DoubleTransition
            {
                Property = ValueProperty,
                Duration = TimeSpan.FromMilliseconds(420),
                Easing = new CubicEaseOut()
            },
            new BrushTransition
            {
                Property = RingBrushProperty,
                Duration = TimeSpan.FromMilliseconds(300)
            }
        ];
    }

    /// <summary>From 0 (empty) to 1 (full circle).</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? RingBrush
    {
        get => GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= Thickness)
            return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = (size - Thickness) / 2;

        if (TrackBrush is { } track)
            context.DrawEllipse(null, new Pen(track, Thickness), center, radius, radius);

        var value = Math.Clamp(Value, 0, 1);
        if (RingBrush is not { } ring || value <= 0.001)
            return;

        var pen = new Pen(ring, Thickness, lineCap: PenLineCap.Round);
        if (value >= 0.999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var sweep = value * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(
            center.X + (radius * Math.Sin(sweep)),
            center.Y - (radius * Math.Cos(sweep)));

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(start, isFilled: false);
            stream.ArcTo(end, new Size(radius, radius), 0, sweep > Math.PI, SweepDirection.Clockwise);
            stream.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
