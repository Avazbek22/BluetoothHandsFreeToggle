using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StereoKeeper.Core;
using StereoKeeper.Gui;
using StereoKeeper.Gui.Model;
using StereoKeeper.Gui.Tray;
using StereoKeeper.Gui.ViewModels;
using Xunit;
using static StereoKeeper.Tests.Gui.Fixtures;

namespace StereoKeeper.Tests.Gui;

[Collection(PanelTests.Name)]
public sealed class PanelRobustnessTests
{
    [Fact]
    public void ARepeatedCheckWithTheSameResultIsEquivalent()
    {
        Assert.True(CallMode("Discord").IsEquivalentTo(CallMode("Discord")));
        Assert.True(CallModeWithProcessId("Discord", 4242).IsEquivalentTo(CallMode("Discord")));
        Assert.True(Stereo().IsEquivalentTo(Stereo()));

        Assert.False(CallMode("Discord").IsEquivalentTo(CallMode("Zoom")));
        Assert.False(CallMode("Discord").IsEquivalentTo(CallMode("Discord", "Zoom")));
        Assert.False(Stereo().IsEquivalentTo(CallMode("Discord")));
        Assert.False(Stereo().IsEquivalentTo(Hard()));
        Assert.False(Stereo().IsEquivalentTo(Empty()));
        Assert.False(Stereo().IsEquivalentTo(null));
        Assert.False(Stereo().IsEquivalentTo(LiveStatus.Evaluate(Services(ServiceRunState.Stopped), IdleHeadset())));
    }

    [Fact]
    public Task ACheckWithNoNewsLeavesTheUiAlone() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(CallMode("Discord"));
        var apps = panel.MicApps;
        var changes = CountChanges(panel);

        for (var check = 0; check < 200; check++)
            panel.ApplyStatus(CallModeWithProcessId("Discord", 5000 + check));

        Assert.Empty(changes);
        Assert.Same(apps, panel.MicApps);
    });

    [Fact]
    public Task NewsStillReachesTheUi() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(CallMode("Discord"));
        var changes = CountChanges(panel);

        panel.ApplyStatus(CallMode("Discord", "Zoom"));

        Assert.NotEmpty(changes);
        Assert.Equal(["Discord", "Zoom"], panel.MicApps.Select(app => app.Name));
    });

    [Fact]
    public Task TheAppListKeepsItsIdentityWhenOnlyTheModeChanges() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(CallMode("Discord"));
        var apps = panel.MicApps;

        panel.ApplyStatus(LiveStatus.Evaluate(Services(), HeadsetInUse("Discord")) with { Error = "transient" });

        Assert.Same(apps, panel.MicApps);
    });

    [Fact]
    public Task ApplyingManyStatusesStaysFast() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Stereo());
        LiveStatus[] cycle = [Stereo(), CallMode("Discord"), CallMode("Discord", "Zoom"), Hard(), Empty()];

        // Warm up once so the timing measures the steady state, not JIT.
        foreach (var status in cycle)
            panel.ApplyStatus(status);

        var stopwatch = Stopwatch.StartNew();
        for (var round = 0; round < 200; round++)
            panel.ApplyStatus(cycle[round % cycle.Length]);
        stopwatch.Stop();

        // A real refresh happens at most every 1.5 s; 200 of them must take well under a second.
        Assert.True(stopwatch.ElapsedMilliseconds < 1500, $"200 refreshes took {stopwatch.ElapsedMilliseconds} ms");
    });

    [Fact]
    public Task TheSameNotificationIsNotRepeatedRightAway() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Stereo());
        var notifications = new List<(string Title, string Body)>();
        panel.NotificationRequested += (_, notification) => notifications.Add(notification);

        // A call app that opens and closes the microphone every few seconds.
        for (var flap = 0; flap < 5; flap++)
        {
            panel.ApplyStatus(CallMode("Discord"));
            panel.ApplyStatus(Stereo());
        }

        Assert.Single(notifications);

        panel.ApplyStatus(CallMode("Zoom"));
        Assert.Equal(2, notifications.Count);
        Assert.Contains("Zoom", notifications[1].Body, StringComparison.Ordinal);
    });

    [Fact]
    public void BrushesAreSharedSoTransitionsDoNotRestart()
    {
        foreach (var tone in Enum.GetValues<Tone>())
        {
            Assert.Same(Palette.Foreground(tone), Palette.Foreground(tone));
            Assert.Same(Palette.Ring(tone), Palette.Ring(tone));
            Assert.Same(Palette.ChipBackground(tone), Palette.ChipBackground(tone));
        }

        Assert.Equal(Palette.Accent, ((ISolidColorBrush)Palette.Ring(Tone.Red)).Color);
        Assert.Equal(Palette.Green, ((ISolidColorBrush)Palette.Foreground(Tone.Green)).Color);
    }

    [Fact]
    public Task EscapeGoesBackFromSettingsThenHidesThePanel() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Stereo());
        var window = Open(panel);
        var hidden = 0;
        window.PanelHidden += (_, _) => hidden++;
        try
        {
            Click(window, "SettingsButton");
            Assert.Equal(FlyoutPage.Settings, panel.Page);

            PressEscape(window);
            Assert.Equal(FlyoutPage.Main, panel.Page);
            Assert.True(window.IsShown);

            PressEscape(window);
            Pump();
            Assert.False(window.IsVisible);
            Assert.Equal(1, hidden);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task HelpReturnsToThePageItWasOpenedFrom() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Stereo());
        var window = Open(panel);
        try
        {
            Click(window, "SettingsButton");
            Click(window, "SettingsHelpButton");
            Assert.Equal(FlyoutPage.Help, panel.Page);

            Click(window, "HelpBackButton");
            Assert.Equal(FlyoutPage.Settings, panel.Page);

            Click(window, "BackButton");
            Assert.Equal(FlyoutPage.Main, panel.Page);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task AClickOutsideThePanelHidesIt() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Stereo());
        var window = Open(panel);
        try
        {
            var outside = new Point(4, 4);
            window.MouseDown(outside, MouseButton.Left);
            window.MouseUp(outside, MouseButton.Left);
            Pump();

            Assert.False(window.IsVisible);
            Assert.False(window.IsShown);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task HidingTwiceOrShowingWhileHidingIsSafe() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Stereo());
        var window = Open(panel);
        var hidden = 0;
        window.PanelHidden += (_, _) => hidden++;
        try
        {
            window.HideAnimated();
            window.HideAnimated();
            window.ShowPanel(null);
            Pump();

            Assert.True(window.IsVisible);
            Assert.True(window.IsShown);
            Assert.Equal(0, hidden);
            Assert.False(window.IsOpaqueFallback);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task TheHiddenPanelStartsOnTheMainPageNextTime() => Ui.RunAsync(() =>
    {
        var panel = new PanelHarness().Create(Stereo());
        var window = Open(panel);
        window.PanelHidden += (_, _) => panel.OnHidden();
        try
        {
            Click(window, "SettingsButton");
            window.HideAnimated();
            Pump();

            Assert.Equal(FlyoutPage.Main, panel.Page);
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(40)]
    [InlineData(48)]
    public Task TrayIconsAreValidPngsOfTheRequestedSize(int size) => Ui.RunAsync(() =>
    {
        foreach (var dot in new Color?[] { null, Palette.Green, Palette.Accent, Palette.Amber, Palette.Light })
            AssertPng(TrayIconRenderer.RenderPng(size, dot), size);

        AssertPng(TrayIconRenderer.RenderNotificationPng(size), size);
    });

    [Fact]
    public Task RenderingTheTrayIconRepeatedlyStaysCheap() => Ui.RunAsync(() =>
    {
        _ = TrayIconRenderer.RenderPng(16, Palette.Green);
        var stopwatch = Stopwatch.StartNew();

        for (var update = 0; update < 300; update++)
            _ = TrayIconRenderer.RenderPng(16, update % 2 == 0 ? Palette.Green : Palette.Accent);

        Assert.True(stopwatch.ElapsedMilliseconds < 3000, $"300 icons took {stopwatch.ElapsedMilliseconds} ms");
    });

    private static LiveStatus CallModeWithProcessId(string app, int processId) => LiveStatus.Evaluate(
        Services(),
        MicrophoneDiagnostics.Analyze(new MicrophoneProbeResult(
            [HeadsetMicrophone, UsbMicrophone],
            [Client(HeadsetMicrophone.Id, processId, app)],
            null) { Headsets = [HeadsetName] }));

    private static List<string?> CountChanges(INotifyPropertyChanged source)
    {
        var changes = new List<string?>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        return changes;
    }

    private static FlyoutWindow Open(FlyoutViewModel panel)
    {
        var window = new FlyoutWindow { DataContext = panel };
        window.Show();
        Pump();
        return window;
    }

    private static void Click(FlyoutWindow window, string buttonName)
    {
        var button = window.GetVisualDescendants()
            .OfType<Button>()
            .Single(candidate => candidate.Name == buttonName && candidate.IsEffectivelyVisible);
        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)
                     ?? throw new InvalidOperationException($"{buttonName} is not in the window.");

        window.MouseMove(center);
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Pump();
    }

    private static void PressEscape(FlyoutWindow window)
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Pump();
    }

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

    private static void AssertPng(byte[] png, int size)
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.True(png.Length > 24, "The PNG is empty.");
        Assert.Equal(signature, png[..8]);
        Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }
}
