using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BluetoothHandsFreeToggle.Gui.Controls;
using Microsoft.Win32;

namespace BluetoothHandsFreeToggle.Gui.Tray;

/// <summary>
/// Draws the tray icon at runtime: a headphones glyph in the taskbar's text colour with a small
/// status dot cut into its lower right corner, rendered at the exact small-icon size for the DPI.
/// </summary>
public static class TrayIconRenderer
{
    private const int SmallIconMetric = 49; // SM_CXSMICON

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

    /// <summary>True when the taskbar uses the light theme (dark glyph needed).</summary>
    public static bool TaskbarIsLight
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    public static byte[] RenderPng(int size, Color glyph, Color? dot)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var scale = size / 24.0;
            var pen = new Pen(new SolidColorBrush(glyph), 2.1, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

            var dotRadius = size * 0.21;
            var dotCenter = new Point(size - dotRadius - (size * 0.02), size - dotRadius - (size * 0.02));

            // Cut a gap around the dot so it stays readable on any taskbar colour.
            var gap = dot is null ? 0 : dotRadius + Math.Max(1.0, size * 0.09);
            Geometry visible = dot is null
                ? new RectangleGeometry(new Rect(0, 0, size, size))
                : new CombinedGeometry(
                    GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(0, 0, size, size)),
                    new EllipseGeometry(new Rect(dotCenter.X - gap, dotCenter.Y - gap, gap * 2, gap * 2)));

            using (context.PushGeometryClip(visible))
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                context.DrawGeometry(null, pen, Icons.Headphones);
            }

            if (dot is { } dotColor)
                context.DrawEllipse(new SolidColorBrush(dotColor), null, dotCenter, dotRadius, dotRadius);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>A larger, colourful version for notifications: the app's red tile with a white glyph.</summary>
    public static byte[] RenderNotificationPng(int size)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var radius = size * 0.22;
            context.DrawRectangle(new SolidColorBrush(Palette.AccentButton), null, new Rect(0, 0, size, size), radius, radius);

            var glyph = size * 0.6;
            var offset = (size - glyph) / 2;
            var pen = new Pen(Brushes.White, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            using (context.PushTransform(Matrix.CreateScale(glyph / 24, glyph / 24) * Matrix.CreateTranslation(offset, offset)))
            {
                context.DrawGeometry(null, pen, Icons.Headphones);
            }
        }

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
