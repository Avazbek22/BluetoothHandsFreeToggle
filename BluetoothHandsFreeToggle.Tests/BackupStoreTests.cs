using BluetoothHandsFreeToggle.Core;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests;

public sealed class BackupStoreTests
{
    [Fact]
    public void LoadReadsLegacyVersionOneBackup()
    {
        var directory = Directory.CreateTempSubdirectory("BluetoothHandsFreeToggle.Tests.");
        try
        {
            var path = Path.Combine(directory.FullName, "backup.json");
            File.WriteAllText(
                path,
                """
                {
                  "CreatedUtc": "2026-01-06T11:36:31.7042345Z",
                  "Services": {
                    "BTAGService": {
                      "ServiceName": "BTAGService",
                      "FriendlyName": "Bluetooth Audio Gateway (HFP)",
                      "Exists": true,
                      "RunState": 1,
                      "StartType": 2,
                      "RegistryStartValue": 3,
                      "Note": null
                    }
                  }
                }
                """);

            var result = new BackupStore(path).Load();

            Assert.True(result.Success);
            var backup = Assert.IsType<BackupFile>(result.Backup);
            Assert.Equal(0, backup.SchemaVersion);
            Assert.Equal(ServiceRunState.Running, backup.Services["BTAGService"].RunState);
            Assert.Equal(ServiceStartType.Manual, backup.Services["BTAGService"].StartType);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoadRoundTripUsesCurrentSchema()
    {
        var directory = Directory.CreateTempSubdirectory("BluetoothHandsFreeToggle.Tests.");
        try
        {
            var path = Path.Combine(directory.FullName, "backup.json");
            var store = new BackupStore(path);
            var snapshots = new[]
            {
                new ServiceSnapshot(
                    "BTAGService",
                    "Bluetooth Audio Gateway (HFP)",
                    Exists: true,
                    QuerySucceeded: true,
                    RunState: ServiceRunState.Stopped,
                    StartType: ServiceStartType.Auto,
                    NativeStartValue: 2,
                    Note: null)
            };

            var save = store.Save(snapshots);
            var load = store.Load();

            Assert.True(save.Success);
            Assert.True(load.Success);
            var backup = Assert.IsType<BackupFile>(load.Backup);
            Assert.Equal(1, backup.SchemaVersion);
            Assert.Equal(ServiceRunState.Stopped, backup.Services["BTAGService"].RunState);
            Assert.Equal(ServiceStartType.Auto, backup.Services["BTAGService"].StartType);
            Assert.Empty(directory.EnumerateFiles("*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadRejectsUnsupportedStartupState()
    {
        var directory = Directory.CreateTempSubdirectory("BluetoothHandsFreeToggle.Tests.");
        try
        {
            var path = Path.Combine(directory.FullName, "backup.json");
            File.WriteAllText(
                path,
                """
                {
                  "SchemaVersion": 1,
                  "CreatedUtc": "2026-08-18T00:00:00Z",
                  "Services": {
                    "BTAGService": {
                      "Exists": true,
                      "RunState": "Running",
                      "StartType": 99
                    }
                  }
                }
                """);

            var result = new BackupStore(path).Load();

            Assert.False(result.Success);
            Assert.False(result.Found);
            Assert.Contains("invalid", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadRejectsUnexpectedServiceEntry()
    {
        var directory = Directory.CreateTempSubdirectory("BluetoothHandsFreeToggle.Tests.");
        try
        {
            var path = Path.Combine(directory.FullName, "backup.json");
            File.WriteAllText(
                path,
                """
                {
                  "SchemaVersion": 1,
                  "CreatedUtc": "2026-08-18T00:00:00Z",
                  "Services": {
                    "UnrelatedService": {
                      "Exists": true,
                      "RunState": "Running",
                      "StartType": "Manual"
                    }
                  }
                }
                """);

            var result = new BackupStore(path).Load();

            Assert.False(result.Success);
            Assert.Contains("UnrelatedService", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadRejectsOversizedBackupBeforeDeserialization()
    {
        var directory = Directory.CreateTempSubdirectory("BluetoothHandsFreeToggle.Tests.");
        try
        {
            var path = Path.Combine(directory.FullName, "backup.json");
            File.WriteAllText(path, new string('x', 70 * 1024));

            var result = new BackupStore(path).Load();

            Assert.False(result.Success);
            Assert.Contains("large", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
