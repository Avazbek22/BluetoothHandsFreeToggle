using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using StereoKeeper.Core;
using StereoKeeper.Localization;

namespace StereoKeeper.App;

/// <summary>What the diagnostics are made of, gathered by <see cref="DiagnosticsReport.Collect"/>.</summary>
internal sealed record DiagnosticsInput(
    string Windows,
    string Runtime,
    bool IsAdministrator,
    string Language,
    DateTimeOffset CreatedAt,
    string Status,
    string LogPath,
    IReadOnlyList<string> LogTail);

/// <summary>
/// The text a user copies from the settings for a bug report: app and Windows versions, the HFP
/// services, the microphone check and the end of the app log. Read-only, needs no administrator
/// rights. The user's profile folder is replaced with <c>%USERPROFILE%</c>.
/// </summary>
public static class DiagnosticsReport
{
    public const int LogTailLines = 40;

    private const string UserProfileToken = "%USERPROFILE%";

    public static string Collect(ToggleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var info = AppInfo.Create();
        string status;
        try
        {
            status = engine.GetStatusReport().ToPrettyText();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            status = $"Status check failed: {exception.Message}";
        }

        var input = new DiagnosticsInput(
            info.OsDisplayName,
            $"{info.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture} process on {RuntimeInformation.OSArchitecture}",
            info.IsAdministrator,
            Text.CurrentLanguageDefinition.Code,
            DateTimeOffset.Now,
            status,
            CrashLog.FilePath,
            ReadLogTail(CrashLog.FilePath, LogTailLines));

        return Format(input, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    internal static string Format(DiagnosticsInput input, string? userProfile)
    {
        ArgumentNullException.ThrowIfNull(input);

        var text = new StringBuilder()
            .AppendLine(AppInfo.AppDisplayName)
            .AppendLine(CultureInfo.InvariantCulture, $"Created: {input.CreatedAt:yyyy-MM-dd HH:mm:ss zzz}")
            .AppendLine(CultureInfo.InvariantCulture, $"Windows: {input.Windows}")
            .AppendLine(CultureInfo.InvariantCulture, $"Runtime: {input.Runtime}")
            .AppendLine(CultureInfo.InvariantCulture, $"Administrator: {(input.IsAdministrator ? "yes" : "no")}")
            .AppendLine(CultureInfo.InvariantCulture, $"Language: {input.Language}")
            .AppendLine()
            .AppendLine(input.Status.TrimEnd())
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Log: {input.LogPath}");

        if (input.LogTail.Count == 0)
            text.AppendLine("(no errors recorded)");
        else
            foreach (var line in input.LogTail)
                text.AppendLine(line);

        var result = text.ToString().TrimEnd();
        return string.IsNullOrWhiteSpace(userProfile)
            ? result
            : result.Replace(userProfile.TrimEnd('\\'), UserProfileToken, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The last <paramref name="count"/> lines of the log, or none when it cannot be read.</summary>
    internal static IReadOnlyList<string> ReadLogTail(string path, int count)
    {
        try
        {
            // The tray app may be writing to the log right now.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var tail = new Queue<string>(count);
            while (reader.ReadLine() is { } line)
            {
                if (tail.Count == count)
                    tail.Dequeue();
                tail.Enqueue(line);
            }

            return tail.ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException or ArgumentException)
        {
            return [];
        }
    }
}
