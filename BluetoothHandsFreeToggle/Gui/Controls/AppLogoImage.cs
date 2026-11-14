using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BluetoothHandsFreeToggle.Gui.Controls;

/// <summary>
/// Shows the application logo, drawn from the icon frame that matches the control's size in device
/// pixels, so it stays sharp at every display scale and follows the window to another monitor.
/// </summary>
public sealed class AppLogoImage : Control
{
    private TopLevel? _topLevel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
            _topLevel.ScalingChanged += OnScalingChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
            _topLevel.ScalingChanged -= OnScalingChanged;

        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
            return;

        var scaling = _topLevel?.RenderScaling ?? 1.0;
        var area = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
        AppLogo.Draw(context, area, (int)Math.Ceiling(size * scaling));
    }

    private void OnScalingChanged(object? sender, EventArgs e) => InvalidateVisual();
}
