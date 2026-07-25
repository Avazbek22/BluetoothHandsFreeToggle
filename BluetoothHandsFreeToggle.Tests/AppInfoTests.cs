using BluetoothHandsFreeToggle.App;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests;

public sealed class AppInfoTests
{
    [Fact]
    public void CreateReturnsRuntimeMetadata()
    {
        var appInfo = AppInfo.Create();

        Assert.False(string.IsNullOrWhiteSpace(appInfo.ExePath));
        Assert.False(string.IsNullOrWhiteSpace(appInfo.OsDisplayName));
        Assert.False(string.IsNullOrWhiteSpace(appInfo.FrameworkDescription));
        Assert.False(string.IsNullOrWhiteSpace(appInfo.Version));
    }

    [Fact]
    public void AppNameUsesExpectedValue()
    {
        Assert.Equal("BluetoothHandsFreeToggle", AppInfo.AppName);
    }

    [Fact]
    public void AppDisplayNameIncludesVersionTag()
    {
        Assert.Equal("v2", AppInfo.AppVersionTag);
        Assert.Equal("BluetoothHandsFreeToggle v2", AppInfo.AppDisplayName);
    }
}
