using System.Buffers.Binary;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace BluetoothHandsFreeToggle.Gui.Controls;

/// <summary>
/// The application logo, read from the frames of Assets\AppIcon.ico. The small frames are drawn by
/// hand (16 and 20 px leave out the gamepad details), so every use takes the frame made for its
/// pixel size instead of scaling the large one down.
/// </summary>
public static class AppLogo
{
    internal const string ResourceName = "BluetoothHandsFreeToggle.AppIcon.ico";

    private const int DirectorySize = 6;
    private const int EntrySize = 16;

    private static readonly Lazy<List<Bitmap>> Frames = new(LoadFrames);

    /// <summary>Pixel sizes of the available frames, smallest first.</summary>
    public static IReadOnlyList<int> FrameSizes => Frames.Value.Select(frame => frame.PixelSize.Width).ToList();

    /// <summary>The smallest frame at least <paramref name="pixels"/> wide, or the largest one.</summary>
    public static Bitmap ForPixelSize(int pixels)
    {
        var frames = Frames.Value;
        foreach (var frame in frames)
        {
            if (frame.PixelSize.Width >= pixels)
                return frame;
        }

        return frames[^1];
    }

    /// <summary>Draws the logo into <paramref name="bounds"/>, which cover <paramref name="pixels"/> device pixels.</summary>
    public static void Draw(DrawingContext context, Rect bounds, int pixels)
    {
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
        {
            context.DrawImage(ForPixelSize(pixels), bounds);
        }
    }

    /// <summary>
    /// Returns the PNG frames of an icon file. The directory is a 6-byte header (reserved, type 1,
    /// count) followed by one 16-byte entry per frame whose last two fields are size and offset.
    /// </summary>
    internal static List<byte[]> ReadPngFrames(byte[] icon)
    {
        var data = icon.AsSpan();
        if (data.Length < DirectorySize ||
            BinaryPrimitives.ReadUInt16LittleEndian(data) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != 1)
        {
            throw new InvalidDataException("The logo is not an icon file.");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        var frames = new List<byte[]>(count);
        for (var index = 0; index < count; index++)
        {
            var entry = DirectorySize + (index * EntrySize);
            if (entry + EntrySize > data.Length)
                break;

            var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(entry + 8)..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(data[(entry + 12)..]);
            if (offset > (uint)data.Length || length > (uint)data.Length - offset)
                continue;

            // Frames stored as bitmaps instead of PNG are skipped; the icon carries PNG frames only.
            var frame = data.Slice((int)offset, (int)length);
            if (frame.StartsWith(PngSignature))
                frames.Add(frame.ToArray());
        }

        return frames.Count > 0 ? frames : throw new InvalidDataException("The logo has no PNG frames.");
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static List<Bitmap> LoadFrames()
    {
        using var resource = typeof(AppLogo).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The logo resource {ResourceName} is missing.");
        using var copy = new MemoryStream();
        resource.CopyTo(copy);

        var bitmaps = new List<Bitmap>();
        foreach (var frame in ReadPngFrames(copy.ToArray()))
        {
            using var stream = new MemoryStream(frame, writable: false);
            bitmaps.Add(new Bitmap(stream));
        }

        return bitmaps.OrderBy(bitmap => bitmap.PixelSize.Width).ToList();
    }
}
