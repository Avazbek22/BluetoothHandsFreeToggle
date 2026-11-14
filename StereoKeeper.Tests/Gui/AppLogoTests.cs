using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StereoKeeper.Gui;
using StereoKeeper.Gui.Controls;
using StereoKeeper.Gui.Tray;
using Xunit;
using static StereoKeeper.Tests.Gui.Fixtures;

namespace StereoKeeper.Tests.Gui;

[Collection(PanelTests.Name)]
public sealed class AppLogoTests
{
    private static readonly int[] HandDrawnSizes = [16, 20, 24, 32, 48, 64, 256];

    [Fact]
    public Task TheLogoKeepsItsHandDrawnSmallFrames() => Ui.RunAsync(() =>
    {
        var sizes = AppLogo.FrameSizes;

        Assert.Equal(sizes.Order().ToList(), sizes);
        foreach (var size in HandDrawnSizes)
            Assert.Contains(size, sizes);
    });

    [Theory]
    [InlineData(16, 16)]
    [InlineData(20, 20)]
    [InlineData(24, 24)]
    [InlineData(28, 30)]
    [InlineData(32, 32)]
    [InlineData(45, 48)]
    [InlineData(4000, 256)]
    public Task EachPixelSizeGetsTheFrameDrawnForIt(int pixels, int frame) => Ui.RunAsync(()
        => Assert.Equal(frame, AppLogo.ForPixelSize(pixels).PixelSize.Width));

    [Fact]
    public void OnlyWellFormedPngFramesOfAnIconAreRead()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        byte[] bitmap = [0x28, 0, 0, 0, 9, 9, 9];

        var frames = AppLogo.ReadPngFrames(Icon((png, null), (bitmap, null), (png, 5000)));

        Assert.Equal(png, Assert.Single(frames));
        Assert.Throws<InvalidDataException>(() => AppLogo.ReadPngFrames([1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => AppLogo.ReadPngFrames(Icon((bitmap, null))));
    }

    [Fact]
    public Task TheTrayIconIsTheLogoWithANotchOnlyForTheStatusDot() => Ui.RunAsync(() =>
    {
        using var plain = Decode(TrayIconRenderer.RenderPng(32, null));
        using var withDot = Decode(TrayIconRenderer.RenderPng(32, Palette.Green));

        AssertLogoRed(Pixel(plain, 3, 15));
        AssertLogoRed(Pixel(withDot, 3, 15));

        // The logo covers the lower right area, and only a status dot cuts a gap into it.
        Assert.Equal(255, Pixel(plain, 18, 18).A);
        Assert.Equal(0, Pixel(withDot, 18, 18).A);

        var dot = Pixel(withDot, 24, 24);
        Assert.InRange(dot.R, Palette.Green.R - 2, Palette.Green.R + 2);
        Assert.InRange(dot.G, Palette.Green.G - 2, Palette.Green.G + 2);
        Assert.InRange(dot.B, Palette.Green.B - 2, Palette.Green.B + 2);
    });

    [Fact]
    public Task TheNotificationIconIsTheLogo() => Ui.RunAsync(() =>
    {
        using var icon = Decode(TrayIconRenderer.RenderNotificationPng(64));

        AssertLogoRed(Pixel(icon, 6, 32));
    });

    [Fact]
    public Task ThePanelHeaderShowsTheLogo() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Stereo());
        var window = new FlyoutWindow { DataContext = panel };
        window.SetRenderScaling(1.5);
        window.Show();
        try
        {
            Pump();
            var logo = window.GetVisualDescendants().OfType<AppLogoImage>().Single(image => image.Name == "HeaderLogo");
            Assert.Equal(new Size(30, 30), logo.Bounds.Size);

            var origin = logo.TranslatePoint(default, window) ?? throw new InvalidOperationException("The logo is not in the window.");
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was rendered.");

            AssertLogoRed(Pixel(frame, (int)((origin.X + 3) * 1.5), (int)((origin.Y + 15) * 1.5)));
        }
        finally
        {
            window.Close();
        }
    });

    private static byte[] Icon(params (byte[] Data, uint? Offset)[] frames)
    {
        const int directory = 6;
        const int entry = 16;
        var dataStart = directory + (frames.Length * entry);
        var icon = new byte[dataStart + frames.Sum(frame => frame.Data.Length)];

        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(4), (ushort)frames.Length);

        var position = dataStart;
        for (var index = 0; index < frames.Length; index++)
        {
            var (data, offset) = frames[index];
            var header = icon.AsSpan(directory + (index * entry), entry);
            BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], offset ?? (uint)position);
            data.CopyTo(icon, position);
            position += data.Length;
        }

        return icon;
    }

    private static WriteableBitmap Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        return WriteableBitmap.Decode(stream);
    }

    private static (byte R, byte G, byte B, byte A) Pixel(WriteableBitmap bitmap, int x, int y)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[4];
        Marshal.Copy(buffer.Address + (y * buffer.RowBytes) + (x * 4), bytes, 0, bytes.Length);
        return buffer.Format == PixelFormats.Rgba8888
            ? (bytes[0], bytes[1], bytes[2], bytes[3])
            : (bytes[2], bytes[1], bytes[0], bytes[3]);
    }

    private static void AssertLogoRed((byte R, byte G, byte B, byte A) pixel)
        => Assert.True(
            pixel is { A: 255, R: > 180, G: < 90, B: < 90 },
            $"Expected the logo's red, got {pixel}.");

    /// <summary>Lets bindings, layout and the slide animations finish.</summary>
    private static void Pump()
    {
        for (var tick = 0; tick < 50; tick++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(12);
        }
    }
}
