using System.Text.Json;
using System.Text.Json.Serialization;
using BluetoothHandsFreeToggle.Localization;
using BluetoothHandsFreeToggle.Windows;

namespace BluetoothHandsFreeToggle.Core;

public sealed class BackupStore : IBackupStore
{
    private const string FolderName = "BluetoothHandsFreeToggle";
    private const string FileName = "backup.json";
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public BackupStore()
        : this(WindowsPaths.GetProgramDataFile(FolderName, FileName))
    {
    }

    public BackupStore(string backupPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        BackupPath = Path.GetFullPath(backupPath);
    }

    public string BackupPath { get; }

    public BackupLoadResult Load()
    {
        try
        {
            if (!File.Exists(BackupPath))
                return BackupLoadResult.Missing();

            var json = File.ReadAllText(BackupPath);
            var backup = JsonSerializer.Deserialize<BackupFile>(json, JsonOptions);
            if (backup is null)
                return BackupLoadResult.Failed(Text.Get("backup.empty"));

            if (backup.SchemaVersion is < 0 or > CurrentSchemaVersion)
                return BackupLoadResult.Failed(
                    Text.Format("backup.unsupportedSchema", backup.SchemaVersion));

            if (backup.Services is null)
                return BackupLoadResult.Failed(Text.Get("backup.missingServices"));

            var normalizedServices = new Dictionary<string, BackupServiceState>(
                backup.Services,
                StringComparer.OrdinalIgnoreCase);

            return BackupLoadResult.Loaded(backup with { Services = normalizedServices });
        }
        catch (Exception ex)
        {
            return BackupLoadResult.Failed(
                Text.Format("backup.readFailed", BackupPath, ex.Message));
        }
    }

    public BackupWriteResult Save(IEnumerable<ServiceSnapshot> snapshots)
    {
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(BackupPath)!;
            Directory.CreateDirectory(directory);

            var services = snapshots.ToDictionary(
                snapshot => snapshot.ServiceName,
                snapshot => new BackupServiceState(snapshot.Exists, snapshot.RunState, snapshot.StartType),
                StringComparer.OrdinalIgnoreCase);

            var backup = new BackupFile(CurrentSchemaVersion, DateTimeOffset.UtcNow, services);
            var json = JsonSerializer.Serialize(backup, JsonOptions);

            temporaryPath = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, BackupPath, overwrite: true);
            temporaryPath = null;

            return BackupWriteResult.Ok();
        }
        catch (Exception ex)
        {
            return BackupWriteResult.Failed(
                Text.Format("backup.saveFailed", BackupPath, ex.Message));
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Best-effort cleanup of an incomplete temporary file.
                }
            }
        }
    }

    public BackupWriteResult Delete()
    {
        try
        {
            if (File.Exists(BackupPath))
                File.Delete(BackupPath);

            return BackupWriteResult.Ok();
        }
        catch (Exception ex)
        {
            return BackupWriteResult.Failed(
                Text.Format("backup.deleteFailed", BackupPath, ex.Message));
        }
    }
}
