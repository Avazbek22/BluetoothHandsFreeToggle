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
    private const long MaximumBackupSize = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly bool _protectPath;

    public BackupStore()
        : this(
            WindowsPaths.GetProgramDataFile(FolderName, FileName),
            protectPath: true)
    {
    }

    public BackupStore(string backupPath)
        : this(backupPath, protectPath: false)
    {
    }

    private BackupStore(string backupPath, bool protectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        BackupPath = Path.GetFullPath(backupPath);
        _protectPath = protectPath;
    }

    public string BackupPath { get; }

    public BackupWriteResult Prepare()
    {
        if (!_protectPath)
            return BackupWriteResult.Ok();

        try
        {
            BackupPathSecurity.Prepare(BackupPath);
            return BackupWriteResult.Ok();
        }
        catch (UnsafeBackupPathException ex)
        {
            return BackupWriteResult.Failed(Text.Format("backup.unsafePath", ex.Message));
        }
        catch (Exception ex)
        {
            return BackupWriteResult.Failed(
                Text.Format("backup.prepareFailed", BackupPath, ex.Message));
        }
    }

    public BackupLoadResult Load()
    {
        try
        {
            if (_protectPath)
                BackupPathSecurity.Validate(BackupPath);

            if (!File.Exists(BackupPath))
                return BackupLoadResult.Missing();

            var backupLength = new FileInfo(BackupPath).Length;
            if (backupLength > MaximumBackupSize)
            {
                return BackupLoadResult.Failed(Text.Format(
                    "backup.tooLarge",
                    backupLength,
                    MaximumBackupSize));
            }

            var json = File.ReadAllText(BackupPath);
            var backup = JsonSerializer.Deserialize<BackupFile>(json, JsonOptions);
            if (backup is null)
                return BackupLoadResult.Failed(Text.Get("backup.empty"));

            if (backup.SchemaVersion is < 0 or > CurrentSchemaVersion)
                return BackupLoadResult.Failed(
                    Text.Format("backup.unsupportedSchema", backup.SchemaVersion));

            if (backup.Services is null)
                return BackupLoadResult.Failed(Text.Get("backup.missingServices"));

            var validationError = ValidateServiceStates(backup.Services);
            if (validationError is not null)
                return BackupLoadResult.Failed(validationError);

            Dictionary<string, BackupServiceState> normalizedServices;
            try
            {
                normalizedServices = new Dictionary<string, BackupServiceState>(
                    backup.Services,
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return BackupLoadResult.Failed(Text.Get("backup.duplicateService"));
            }

            return BackupLoadResult.Loaded(backup with { Services = normalizedServices });
        }
        catch (UnsafeBackupPathException ex)
        {
            return BackupLoadResult.Failed(Text.Format("backup.unsafePath", ex.Message));
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
            if (_protectPath)
                BackupPathSecurity.Prepare(BackupPath);
            else
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

            if (_protectPath)
                BackupPathSecurity.ProtectFile(BackupPath);

            return BackupWriteResult.Ok();
        }
        catch (UnsafeBackupPathException ex)
        {
            return BackupWriteResult.Failed(Text.Format("backup.unsafePath", ex.Message));
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

    private static string? ValidateServiceStates(
        IReadOnlyDictionary<string, BackupServiceState> services)
    {
        var knownServiceNames = Targets.Services
            .Select(target => target.ServiceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (serviceName, state) in services)
        {
            if (!knownServiceNames.Contains(serviceName))
                return Text.Format("backup.unexpectedService", serviceName);

            if (state is null ||
                !Enum.IsDefined(state.RunState) ||
                !Enum.IsDefined(state.StartType))
            {
                return Text.Format("backup.invalidServiceState", serviceName);
            }

            if (!state.Exists)
            {
                if (state.RunState is not ServiceRunState.NotPresent ||
                    state.StartType is not ServiceStartType.NotPresent)
                {
                    return Text.Format("backup.invalidServiceState", serviceName);
                }

                continue;
            }

            if (state.StartType is not (
                    ServiceStartType.Auto or
                    ServiceStartType.Manual or
                    ServiceStartType.Disabled) ||
                state.RunState is ServiceRunState.NotPresent or ServiceRunState.Unknown)
            {
                return Text.Format("backup.invalidServiceState", serviceName);
            }
        }

        return null;
    }

}
