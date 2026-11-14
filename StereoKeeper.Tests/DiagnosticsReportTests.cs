using StereoKeeper.App;
using StereoKeeper.Core;
using Xunit;

namespace StereoKeeper.Tests;

public sealed class DiagnosticsReportTests
{
    [Fact]
    public void ReportListsVersionsStatusAndLogWithoutTheUserFolder()
    {
        var text = DiagnosticsReport.Format(
            new DiagnosticsInput(
                "Windows 11 Pro 25H2 (Build 26300.7462)",
                ".NET 10.0.0, X64 process on X64",
                IsAdministrator: false,
                "ru-RU",
                new DateTimeOffset(2026, 11, 14, 15, 4, 12, TimeSpan.FromHours(3)),
                "Status\nBluetooth Audio Gateway (HFP): running, manual\n",
                @"C:\Users\gamer\AppData\Local\StereoKeeper\logs\app.log",
                ["2026-11-14 14:00:00.000 [2.2, pid 1] Copying the report failed", @"   at Copy() in C:\Users\Gamer\src\x.cs"]),
            @"C:\Users\gamer\");

        Assert.StartsWith(AppInfo.AppDisplayName, text, StringComparison.Ordinal);
        Assert.Contains("Created: 2026-11-14 15:04:12 +03:00", text, StringComparison.Ordinal);
        Assert.Contains("Windows: Windows 11 Pro 25H2 (Build 26300.7462)", text, StringComparison.Ordinal);
        Assert.Contains("Runtime: .NET 10.0.0, X64 process on X64", text, StringComparison.Ordinal);
        Assert.Contains("Administrator: no", text, StringComparison.Ordinal);
        Assert.Contains("Language: ru-RU", text, StringComparison.Ordinal);
        Assert.Contains("Bluetooth Audio Gateway (HFP): running, manual", text, StringComparison.Ordinal);
        Assert.Contains(@"Log: %USERPROFILE%\AppData\Local\StereoKeeper\logs\app.log", text, StringComparison.Ordinal);
        Assert.Contains(@"at Copy() in %USERPROFILE%\src\x.cs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("gamer", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyLogIsStatedPlainly()
    {
        var text = DiagnosticsReport.Format(
            new DiagnosticsInput("Windows 10", ".NET 10", true, "en", DateTimeOffset.UnixEpoch, "Status", "app.log", []),
            userProfile: null);

        Assert.Contains("Administrator: yes", text, StringComparison.Ordinal);
        Assert.EndsWith("Log: app.log" + Environment.NewLine + "(no errors recorded)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LogTailKeepsTheLastLinesAndReadsWhileTheLogIsOpen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "StereoKeeper.Tests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"tail-{Guid.NewGuid():N}.log");
        File.WriteAllLines(path, Enumerable.Range(1, 100).Select(index => $"line {index}"));

        try
        {
            using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                Assert.Equal(["line 98", "line 99", "line 100"], DiagnosticsReport.ReadLogTail(path, 3));
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Empty(DiagnosticsReport.ReadLogTail(path, 3));
    }

    [Fact]
    public void CollectReadsThisMachineWithoutChangingIt()
    {
        var text = DiagnosticsReport.Collect(new ToggleEngine());

        Assert.StartsWith(AppInfo.AppDisplayName, text, StringComparison.Ordinal);
        Assert.Contains("Windows: ", text, StringComparison.Ordinal);
        Assert.Contains("Runtime: .NET", text, StringComparison.Ordinal);
        Assert.Contains("Log: ", text, StringComparison.Ordinal);
    }
}
