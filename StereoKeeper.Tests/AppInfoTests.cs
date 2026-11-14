using System.Reflection;
using StereoKeeper.App;
using Xunit;

namespace StereoKeeper.Tests;

public sealed class AppInfoTests
{
    [Fact]
    public void CreateReturnsRuntimeMetadata()
    {
        var appInfo = AppInfo.Create();

        Assert.False(string.IsNullOrWhiteSpace(appInfo.ExePath));
        Assert.False(string.IsNullOrWhiteSpace(appInfo.OsDisplayName));
        Assert.False(string.IsNullOrWhiteSpace(appInfo.FrameworkDescription));
    }

    [Fact]
    public void AppNameUsesExpectedValue()
    {
        Assert.Equal("StereoKeeper", AppInfo.AppName);
    }

    [Fact]
    public void AppDisplayNameIncludesVersionTag()
    {
        Assert.Equal("v3.0", AppInfo.AppVersionTag);
        Assert.Equal("StereoKeeper v3.0", AppInfo.AppDisplayName);
    }

    // The release script names the files after the project version, the panel shows this constant.
    [Fact]
    public void DisplayVersionMatchesTheProjectVersion()
    {
        var projectVersion = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.Equal(AppInfo.AppDisplayVersion, projectVersion);
    }
}
