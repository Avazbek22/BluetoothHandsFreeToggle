using BluetoothHandsFreeToggle.App;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests.Gui;

public sealed class LaunchOptionsTests
{
    [Fact]
    public void NoArgumentsOpensTheTrayAppWithThePanel()
    {
        var options = LaunchOptions.Parse([]);

        Assert.Equal(LaunchMode.Gui, options.Mode);
        Assert.False(options.StartHidden);
    }

    [Fact]
    public void TrayArgumentStartsHidden()
    {
        var options = LaunchOptions.Parse(["--TRAY"]);

        Assert.Equal(LaunchMode.Gui, options.Mode);
        Assert.True(options.StartHidden);
    }

    [Fact]
    public void ConsoleArgumentOpensTheClassicMenu()
        => Assert.Equal(LaunchMode.Console, LaunchOptions.Parse(["--console"]).Mode);

    [Theory]
    [InlineData(OperationKind.Soft)]
    [InlineData(OperationKind.Hard)]
    [InlineData(OperationKind.Restore)]
    public void WorkerArgumentsRoundTrip(OperationKind operation)
    {
        var arguments = LaunchOptions.FormatWorkerArguments(operation, "BluetoothHandsFreeToggle.42.abc", "ru-RU");

        var options = LaunchOptions.Parse(arguments.Split(' '));

        Assert.Equal(LaunchMode.ElevatedWorker, options.Mode);
        Assert.Equal(operation, options.Operation);
        Assert.Equal("BluetoothHandsFreeToggle.42.abc", options.PipeName);
        Assert.Equal("ru-RU", options.LanguageCode);
    }

    [Fact]
    public void WorkerWinsOverOtherModes()
        => Assert.Equal(LaunchMode.ElevatedWorker, LaunchOptions.Parse(["--console", "--elevated-worker", "soft"]).Mode);

    [Fact]
    public void UnknownOrMissingWorkerOperationIsRejected()
    {
        Assert.Null(LaunchOptions.Parse(["--elevated-worker", "format-disk", "--pipe", "p"]).Operation);
        Assert.Null(LaunchOptions.Parse(["--elevated-worker"]).Operation);
        Assert.Null(LaunchOptions.Parse(["--elevated-worker", "soft", "--pipe"]).PipeName);
    }
}
