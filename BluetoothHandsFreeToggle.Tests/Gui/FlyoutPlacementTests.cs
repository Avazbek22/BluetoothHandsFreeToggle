using Avalonia;
using BluetoothHandsFreeToggle.Gui;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests.Gui;

public sealed class FlyoutPlacementTests
{
    private const double PanelWidth = 360;
    private const double PanelHeight = 760;
    private const double Margin = 20;
    private const int TaskbarDips = 48;

    public enum Taskbar
    {
        Bottom,
        Top,
        Left,
        Right,
        AutoHide
    }

    public static TheoryData<Taskbar, double> Layouts()
    {
        var data = new TheoryData<Taskbar, double>();
        foreach (var taskbar in Enum.GetValues<Taskbar>())
        {
            foreach (var scaling in new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 3.0 })
                data.Add(taskbar, scaling);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void PanelSitsInTheTrayCornerInsideTheWorkingArea(Taskbar taskbar, double scaling)
    {
        var bounds = new PixelRect(0, 0, (int)(1920 * scaling), (int)(1080 * scaling));
        var area = WorkingArea(bounds, taskbar, scaling);

        var placement = FlyoutPlacement.Compute(bounds, area, scaling, PanelWidth, PanelHeight, Margin);
        var card = Card(placement, scaling, Margin);
        var gap = FlyoutPlacement.ScreenGap * scaling;

        Assert.True(card.Left >= area.X - 1 && card.Right <= area.Right + 1, $"{card} leaves {area} sideways");
        Assert.True(card.Top >= area.Y - 1 && card.Bottom <= area.Bottom + 1, $"{card} leaves {area} vertically");

        if (taskbar == Taskbar.Left)
            Assert.InRange(card.Left - area.X, gap - 1, gap + 1);
        else
            Assert.InRange(area.Right - card.Right, gap - 1, gap + 1);

        if (taskbar == Taskbar.Top)
            Assert.InRange(card.Top - area.Y, gap - 1, gap + 1);
        else
            Assert.InRange(area.Bottom - card.Bottom, gap - 1, gap + 1);
    }

    [Fact]
    public void FullHdWithABottomTaskbarMatchesTheDesign()
    {
        var placement = FlyoutPlacement.Compute(
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1032),
            1,
            PanelWidth,
            PanelHeight,
            Margin);

        Assert.Equal(400, placement.Width);
        Assert.Equal(800, placement.Height);
        Assert.Equal(new PixelPoint(1528, 240), placement.Position);
    }

    [Fact]
    public void ALowScreenShortensThePanelToFit()
    {
        // 1366 x 768 at 125 %: 576 DIPs of working area above a 48 DIP taskbar.
        var bounds = new PixelRect(0, 0, 1366, 768);
        var area = new PixelRect(0, 0, 1366, 708);

        var placement = FlyoutPlacement.Compute(bounds, area, 1.25, PanelWidth, PanelHeight, Margin);
        var card = Card(placement, 1.25, Margin);

        Assert.Equal((708 / 1.25) - 24 + (2 * Margin), placement.Height, 3);
        Assert.InRange(card.Top - area.Y, (12 * 1.25) - 1, (12 * 1.25) + 1);
    }

    [Fact]
    public void ATinyWorkingAreaStillGetsAUsablePanel()
    {
        var placement = FlyoutPlacement.Compute(
            new PixelRect(0, 0, 800, 300),
            new PixelRect(0, 0, 800, 260),
            1,
            PanelWidth,
            PanelHeight,
            Margin);

        Assert.Equal(FlyoutPlacement.MinPanelHeight + (2 * Margin), placement.Height);
    }

    [Fact]
    public void ASecondScreenLeftOfThePrimaryUsesItsOwnCoordinates()
    {
        var bounds = new PixelRect(-2560, -200, 2560, 1440);
        var area = new PixelRect(-2560, -200, 2560, 1392);

        var placement = FlyoutPlacement.Compute(bounds, area, 1, PanelWidth, PanelHeight, Margin);
        var card = Card(placement, 1, Margin);

        Assert.Equal(-12, card.Right, 0);
        Assert.Equal(area.Bottom - 12, card.Bottom, 0);
    }

    [Fact]
    public void TheOpaqueFallbackHasNoShadowMargin()
    {
        var placement = FlyoutPlacement.Compute(
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1032),
            1,
            PanelWidth,
            PanelHeight,
            margin: 0);

        Assert.Equal(PanelWidth, placement.Width);
        Assert.Equal(PanelHeight, placement.Height);
        Assert.Equal(new PixelPoint(1920 - 12 - 360, 1032 - 12 - 760), placement.Position);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ABrokenScaleIsTreatedAsOneHundredPercent(double scaling)
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);
        var area = new PixelRect(0, 0, 1920, 1032);

        Assert.Equal(
            FlyoutPlacement.Compute(bounds, area, 1, PanelWidth, PanelHeight, Margin),
            FlyoutPlacement.Compute(bounds, area, scaling, PanelWidth, PanelHeight, Margin));
    }

    private static PixelRect WorkingArea(PixelRect bounds, Taskbar taskbar, double scaling)
    {
        var bar = (int)Math.Round(TaskbarDips * scaling);
        return taskbar switch
        {
            Taskbar.Bottom => new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height - bar),
            Taskbar.Top => new PixelRect(bounds.X, bounds.Y + bar, bounds.Width, bounds.Height - bar),
            Taskbar.Left => new PixelRect(bounds.X + bar, bounds.Y, bounds.Width - bar, bounds.Height),
            Taskbar.Right => new PixelRect(bounds.X, bounds.Y, bounds.Width - bar, bounds.Height),
            _ => bounds
        };
    }

    /// <summary>The visible panel inside the window, in physical pixels.</summary>
    private static Rect Card(FlyoutPlacement placement, double scaling, double margin)
        => new(
            placement.Position.X + (margin * scaling),
            placement.Position.Y + (margin * scaling),
            (placement.Width - (2 * margin)) * scaling,
            (placement.Height - (2 * margin)) * scaling);
}
