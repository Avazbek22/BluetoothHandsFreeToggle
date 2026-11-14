using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using BluetoothHandsFreeToggle.Gui.Controls;

namespace BluetoothHandsFreeToggle.Gui.Tray;

/// <summary>
/// Draws the tray icon at runtime: the app logo with a small status dot cut into its lower right
/// corner, rendered at the exact small-icon size for the DPI from the logo frame made for that size.
/// </summary>
public static class TrayIconRenderer
{
    private const int SmallIconMetric = 49; // SM_CXSMICON

    private static readonly IBrush DotEdge = new ImmutableSolidColorBrush(Color.FromArgb(70, 0, 0, 0));

    public static int SmallIconSize
    {
        get
        {
            try
            {
                var size = GetSystemMetricsForDpi(SmallIconMetric, GetDpiForSystem());
                return size > 0 ? size : 16;
            }
            catch (EntryPointNotFoundException)
            {
                return 16;
            }
        }
    }

    public static byte[] RenderPng(int size, Color? dot)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var bounds = new Rect(0, 0, size, size);
            var dotRadius = size * 0.21;
            var dotCenter = new Point(size - dotRadius - (size * 0.02), size - dotRadius - (size * 0.02));

            // Cut a gap around the dot so it stays readable on the red logo and on any taskbar colour.
            var gap = dot is null ? 0 : dotRadius + Math.Max(1.0, size * 0.09);
            Geometry visible = dot is null
                ? new RectangleGeometry(bounds)
                : new CombinedGeometry(
                    GeometryCombineMode.Exclude,
                    new RectangleGeometry(bounds),
                    new EllipseGeometry(new Rect(dotCenter.X - gap, dotCenter.Y - gap, gap * 2, gap * 2)));

            using (context.PushGeometryClip(visible))
            {
                AppLogo.Draw(context, bounds, size);
            }

            if (dot is { } dotColor)
            {
                // A faint edge inside the dot keeps the light Hard mode dot visible on a light taskbar.
                var edge = Math.Max(1.0, size / 16.0);
                var radius = dotRadius - (edge / 2);
                context.DrawEllipse(new SolidColorBrush(dotColor), new Pen(DotEdge, edge), dotCenter, radius, radius);
            }
        }

        return Encode(bitmap);
    }

    /// <summary>A larger version for notifications: the logo alone.</summary>
    public static byte[] RenderNotificationPng(int size)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            AppLogo.Draw(context, new Rect(0, 0, size, size), size);
        }

        return Encode(bitmap);
    }

    private static byte[] Encode(RenderTargetBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetDpiForSystem();
}
