using System.Globalization;
using System.Text;

namespace BluetoothHandsFreeToggle.App;

/// <summary>
/// A small diagnostic log for errors the app recovered from or could not recover from, kept in
/// <c>%LocalAppData%\BluetoothHandsFreeToggle\logs</c>. Writing never throws and the file never
/// grows past <see cref="MaxBytes"/> (the previous file is kept as <c>app.old.log</c>).
/// </summary>
public static class CrashLog
{
    public const long MaxBytes = 512 * 1024;

    private static readonly Lock Gate = new();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static string? _pathOverride;

    public static string FilePath => _pathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppInfo.AppName,
        "logs",
        "app.log");

    /// <summary>Redirects the log (tests); <c>null</c> restores the default location.</summary>
    internal static void UsePath(string? path)
    {
        lock (Gate)
            _pathOverride = path;
    }

    public static void Write(string context, Exception? exception = null)
        => WriteTo(FilePath, context, exception, MaxBytes);

    internal static void WriteTo(string path, string context, Exception? exception, long maxBytes)
    {
        try
        {
            var entry = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(" [").Append(AppInfo.AppDisplayVersion).Append(", pid ")
                .Append(Environment.ProcessId.ToString(CultureInfo.InvariantCulture)).Append("] ")
                .AppendLine(context);
            if (exception is not null)
                entry.AppendLine(exception.ToString());

            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Rotate(path, maxBytes);
                File.AppendAllText(path, entry.ToString(), Utf8NoBom);
            }
        }
        catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException
                                               or NotSupportedException or System.Security.SecurityException
                                               or ArgumentException)
        {
            // Logging is best effort; there is nowhere else to report this.
        }
    }

    private static void Rotate(string path, long maxBytes)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < maxBytes)
            return;

        var previous = Path.ChangeExtension(path, ".old.log");
        File.Move(path, previous, overwrite: true);
    }
}
