using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using BluetoothHandsFreeToggle.App;

namespace BluetoothHandsFreeToggle.Tests;

internal static class TestSetup
{
    /// <summary>Errors the tests provoke on purpose must not land in the user's real app log.</summary>
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255", Justification = "The test assembly is the only consumer.")]
    internal static void RedirectCrashLog()
        => CrashLog.UsePath(Path.Combine(
            Path.GetTempPath(),
            "BluetoothHandsFreeToggle.Tests",
            "app.log"));
}
