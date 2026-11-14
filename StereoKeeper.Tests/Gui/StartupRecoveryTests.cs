using StereoKeeper.App;
using StereoKeeper.Core;
using StereoKeeper.Gui.Services;
using Xunit;
using static StereoKeeper.Tests.Gui.Fixtures;

namespace StereoKeeper.Tests.Gui;

// Shares the panel collection: the texts come from the global language, which panel tests switch.
[Collection(PanelTests.Name)]
public sealed class StartupRecoveryTests
{
    private const string LogPath = @"C:\Users\test\AppData\Local\StereoKeeper\logs\app.log";

    private static readonly InvalidOperationException StartFailure = new("No graphics device.");

    [Fact]
    public void WithoutAlwaysStereoOnlyTheErrorIsShown()
    {
        var dialog = new RecordingDialog();
        var runner = new ScriptedRunner();

        Recovery(dialog, runner, Services()).Report(StartFailure, LogPath);

        var shown = Assert.Single(dialog.Shown);
        Assert.Equal(StartupDialogIcon.Error, shown.Icon);
        Assert.Equal("StereoKeeper could not start", shown.Title);
        Assert.Contains("No graphics device.", shown.Body, StringComparison.Ordinal);
        Assert.Contains(LogPath, shown.Body, StringComparison.Ordinal);
        Assert.Empty(dialog.Questions);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void WithAlwaysStereoOnTheUserCanPutWindowsBack()
    {
        var dialog = new RecordingDialog { Answer = true };
        var runner = new ScriptedRunner();

        Recovery(dialog, runner, HardServices()).Report(StartFailure, LogPath);

        var question = Assert.Single(dialog.Questions);
        Assert.Contains("No graphics device.", question.Body, StringComparison.Ordinal);
        Assert.EndsWith(
            "“Always stereo” is on, so the headphone microphone is off. Put everything back as it was? Windows will ask for permission.",
            question.Body,
            StringComparison.Ordinal);
        Assert.Equal([OperationKind.Restore], runner.Calls);
        var shown = Assert.Single(dialog.Shown);
        Assert.Equal(StartupDialogIcon.Information, shown.Icon);
        Assert.Equal(
            "Done: Windows is set up exactly as without the app. The headphone microphone works again.",
            shown.Body);
    }

    [Fact]
    public void DecliningTheOfferChangesNothing()
    {
        var dialog = new RecordingDialog { Answer = false };
        var runner = new ScriptedRunner();

        Recovery(dialog, runner, HardServices()).Report(StartFailure, LogPath);

        Assert.Single(dialog.Questions);
        Assert.Empty(dialog.Shown);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void CancelledPermissionPromptSaysNothingChanged()
    {
        var dialog = new RecordingDialog { Answer = true };
        var runner = new ScriptedRunner { PassesUac = false, Outcome = OperationOutcome.Cancelled };

        Recovery(dialog, runner, HardServices()).Report(StartFailure, LogPath);

        var shown = Assert.Single(dialog.Shown);
        Assert.Equal(StartupDialogIcon.Warning, shown.Icon);
        Assert.Equal("Cancelled: “No” was chosen in the Windows prompt. Nothing changed.", shown.Body);
    }

    [Fact]
    public void FailedRestoreShowsTheReport()
    {
        var dialog = new RecordingDialog { Answer = true };
        var runner = new ScriptedRunner
        {
            Outcome = OperationOutcome.Completed(Report(false, false, "[ERROR] BTAGService did not start."))
        };

        Recovery(dialog, runner, HardServices()).Report(StartFailure, LogPath);

        var shown = Assert.Single(dialog.Shown);
        Assert.Equal(StartupDialogIcon.Error, shown.Icon);
        Assert.Equal("Could not put things back", shown.Title);
        Assert.Contains("BTAGService did not start.", shown.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreThatNeedsARestartSaysSo()
    {
        var dialog = new RecordingDialog { Answer = true };
        var runner = new ScriptedRunner { Outcome = OperationOutcome.Completed(Report(true, rebootRequired: true)) };

        Recovery(dialog, runner, HardServices()).Report(StartFailure, LogPath);

        var shown = Assert.Single(dialog.Shown);
        Assert.Equal(StartupDialogIcon.Warning, shown.Icon);
        Assert.Equal("Restart required", shown.Title);
    }

    [Fact]
    public void RunnerThatThrowsIsShownAsAFailure()
    {
        var dialog = new RecordingDialog { Answer = true };

        Recovery(dialog, new BrokenRunner(), HardServices()).Report(StartFailure, LogPath);

        var shown = Assert.Single(dialog.Shown);
        Assert.Equal(StartupDialogIcon.Error, shown.Icon);
        Assert.Equal("pipe broke", shown.Body);
    }

    [Fact]
    public void UnreadableServicesFallBackToThePlainError()
    {
        var dialog = new RecordingDialog { Answer = true };
        var runner = new ScriptedRunner();

        new StartupRecovery(dialog, runner, () => throw new InvalidOperationException("SCM unavailable"))
            .Report(StartFailure, LogPath);

        Assert.Equal(StartupDialogIcon.Error, Assert.Single(dialog.Shown).Icon);
        Assert.Empty(dialog.Questions);
        Assert.Empty(runner.Calls);
    }

    private static StartupRecovery Recovery(
        IStartupDialog dialog,
        IOperationRunner runner,
        IReadOnlyList<ServiceSnapshot> services)
        => new(dialog, runner, () => services);

    private static List<ServiceSnapshot> HardServices() => Services(ServiceRunState.Stopped, ServiceStartType.Disabled);

    private sealed class RecordingDialog : IStartupDialog
    {
        public bool Answer { get; init; }

        public List<(string Title, string Body, StartupDialogIcon Icon)> Shown { get; } = [];

        public List<(string Title, string Body)> Questions { get; } = [];

        public void Show(string title, string body, StartupDialogIcon icon) => Shown.Add((title, body, icon));

        public bool Ask(string title, string body)
        {
            Questions.Add((title, body));
            return Answer;
        }
    }

    private sealed class BrokenRunner : IOperationRunner
    {
        public Task<OperationOutcome> RunAsync(
            OperationKind operation,
            Action<OperationEvent> onEvent,
            Action? onElevated = null,
            CancellationToken cancellationToken = default)
            => throw new IOException("pipe broke");
    }
}
