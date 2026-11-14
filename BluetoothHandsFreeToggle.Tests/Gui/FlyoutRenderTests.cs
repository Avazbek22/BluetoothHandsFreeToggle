using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Core;
using BluetoothHandsFreeToggle.Gui;
using BluetoothHandsFreeToggle.Gui.Model;
using BluetoothHandsFreeToggle.Gui.Services;
using BluetoothHandsFreeToggle.Gui.ViewModels;
using BluetoothHandsFreeToggle.Localization;
using Xunit;
using static BluetoothHandsFreeToggle.Tests.Gui.Fixtures;

namespace BluetoothHandsFreeToggle.Tests.Gui;

/// <summary>
/// Renders the real panel window off-screen in every state and checks that no label is cut off or
/// sticks out of the panel. Set <c>HFT_RENDER_DIR</c> to also save the frames as PNG files.
/// </summary>
[Collection(PanelTests.Name)]
public sealed class FlyoutRenderTests
{
    private const string HandsFree = "Bluetooth Hands-Free Service (HFP)";
    private const string Gateway = "Bluetooth Audio Gateway (HFP)";

    private static readonly (string Name, Func<PanelHarness, Task<FlyoutViewModel>> Arrange)[] Scenarios =
    [
        ("01-loading", harness => Task.FromResult(harness.Create())),
        ("02-no-headset", harness => Task.FromResult(harness.Create(Empty()))),
        ("03-stereo", harness => Task.FromResult(harness.Create(LiveStatus.Evaluate(
            Services(), IdleHeadset(Client(UsbMicrophone.Id, 5, "OBS Studio")))))),
        ("04-call", harness => Task.FromResult(harness.Create(CallMode("Discord", "Google Chrome", "Microsoft Teams")))),
        ("05-uac", async harness =>
        {
            harness.Runner.PassesUac = false;
            harness.Runner.HoldUntilReleased = true;
            var panel = harness.Create(CallMode("Discord"));
            _ = panel.RunAsync(OperationKind.Soft);
            await Task.Yield();
            return panel;
        }),
        ("06-working", async harness =>
        {
            harness.Runner.HoldUntilReleased = true;
            harness.Runner.Events.Add(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.Stopping, Service: HandsFree));
            harness.Runner.Events.Add(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.Starting, Service: HandsFree));
            harness.Runner.Events.Add(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.Stopping, Service: Gateway));
            harness.Runner.Events.Add(new OperationEvent(
                OperationEventKind.Waiting, "", TimeSpan.FromSeconds(3), OperationStep.WaitingStop, Gateway));
            var panel = harness.Create(CallMode("Discord"));
            _ = panel.RunAsync(OperationKind.Soft);
            await Task.Yield();
            return panel;
        }),
        ("07-done", async harness =>
        {
            var panel = harness.Create(CallMode("Discord"));
            await panel.RunAsync(OperationKind.Soft);
            panel.ApplyStatus(Stereo());
            return panel;
        }),
        ("08-done-mic-busy", async harness =>
        {
            var panel = harness.Create(CallMode("Discord"));
            await panel.RunAsync(OperationKind.Soft);
            return panel;
        }),
        ("09-hard", harness => Task.FromResult(harness.Create(Hard()))),
        ("10-reboot", async harness =>
        {
            harness.Runner.Outcome = OperationOutcome.Completed(Report(
                true,
                rebootRequired: true,
                "[WARN] Bluetooth Audio Gateway (HFP) did not stop and its host process also runs other services.",
                "[WARN] Restart Windows to finish."));
            var panel = harness.Create(Stereo());
            await panel.RunAsync(OperationKind.Hard);
            return panel;
        }),
        ("11-failed", async harness =>
        {
            harness.Runner.Outcome = OperationOutcome.Completed(Report(
                false,
                false,
                "[ERROR] Bluetooth Hands-Free Service (HFP) did not stop within 60 s.",
                "[ERROR] Access is denied."));
            var panel = harness.Create(CallMode("Discord"));
            await panel.RunAsync(OperationKind.Soft);
            return panel;
        }),
        ("12-hung", harness => Task.FromResult(harness.Create(LiveStatus.Evaluate(
            [Service("BthHFSrv"), Service("BTAGService", ServiceRunState.StopPending)],
            IdleHeadset(),
            new HashSet<string> { "BTAGService" })))),
        ("13-unavailable", harness => Task.FromResult(harness.Create(LiveStatus.Evaluate(
            [Service("BthHFSrv", querySucceeded: false, note: "OpenService failed: access is denied (5)."), Service("BTAGService")],
            IdleHeadset())))),
        ("14-settings", harness =>
        {
            var panel = harness.Create(Stereo());
            panel.ShowSettingsCommand.Execute(null);
            return Task.FromResult(panel);
        }),
        ("15-help", harness =>
        {
            var panel = harness.Create(Stereo());
            panel.ShowHelpCommand.Execute(null);
            return Task.FromResult(panel);
        })
    ];

    public static TheoryData<AppLanguage> Languages()
    {
        var languages = new TheoryData<AppLanguage>();
        foreach (var definition in AppLanguageCatalog.All)
            languages.Add(definition.Language);
        return languages;
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public Task EveryStateRendersWithoutClippedText(AppLanguage language) => Ui.RunAsync(async () =>
    {
        Text.SetLanguage(language);
        try
        {
            foreach (var (name, arrange) in Scenarios)
            {
                var harness = new PanelHarness();
                var panel = await arrange(harness).ConfigureAwait(true);
                Render(panel, $"{AppLanguageCatalog.Get(language).ResourceCode}-{name}");
                harness.Runner.Release();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            Text.SetLanguage(AppLanguage.English);
        }
    });

    private static void Render(FlyoutViewModel panel, string name)
    {
        var window = new FlyoutWindow { DataContext = panel };
        window.SetRenderScaling(1.5);
        window.Show();
        try
        {
            Pump();
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(600, 1200), frame.PixelSize);

            AssertLabelsFit(window, name);
            Save(frame, name);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Lets bindings, layout, transitions and page animations finish.</summary>
    private static void Pump()
    {
        for (var tick = 0; tick < 40; tick++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(12);
        }
    }

    private static void AssertLabelsFit(FlyoutWindow window, string scenario)
    {
        var card = window.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Card");
        var labels = card.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(label => label.IsEffectivelyVisible && !string.IsNullOrEmpty(label.Text))
            .ToList();
        Assert.NotEmpty(labels);

        foreach (var label in labels)
        {
            if (label.TextTrimming == TextTrimming.None)
            {
                Assert.True(
                    label.TextLayout.Width <= label.Bounds.Width + 1,
                    $"{scenario}: \"{label.Text}\" needs {label.TextLayout.Width:F1}px but gets {label.Bounds.Width:F1}px");
            }

            var left = label.TranslatePoint(default, card);
            var right = label.TranslatePoint(new Point(label.Bounds.Width, 0), card);
            Assert.True(
                left is { X: >= -1 } && right is { } end && end.X <= card.Bounds.Width + 1,
                $"{scenario}: \"{label.Text}\" sticks out of the panel");
        }
    }

    private static void Save(Bitmap frame, string name)
    {
        var folder = Environment.GetEnvironmentVariable("HFT_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(folder))
            return;

        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
