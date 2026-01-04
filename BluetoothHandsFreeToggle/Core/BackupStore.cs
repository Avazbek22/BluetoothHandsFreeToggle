using System.Text.Json;
using BluetoothHandsFreeToggle.Windows;

namespace BluetoothHandsFreeToggle.Core;

public sealed class BackupStore
{
    private const string FolderName = "BluetoothHandsFreeToggle";
    private const string FileName = "backup.json";

    public sealed record BackupFile(string CreatedUtc, Dictionary<string, ServiceSnapshot> Services);

    public string BackupPath => WindowsPaths.GetProgramDataFile(FolderName, FileName);

    public bool TryLoad(out BackupFile? backup)
    {
        backup = null;
        try
        {
            if (!File.Exists(BackupPath))
                return false;

            var json = File.ReadAllText(BackupPath);
            backup = JsonSerializer.Deserialize<BackupFile>(json);
            return backup is not null;
        }
        catch
        {
            return false;
        }
    }

    public void Save(IEnumerable<ServiceSnapshot> snapshots)
    {
        try
        {
            var dir = Path.GetDirectoryName(BackupPath)!;
            Directory.CreateDirectory(dir);

            var map = snapshots.ToDictionary(s => s.ServiceName, s => s, StringComparer.OrdinalIgnoreCase);
            var bf = new BackupFile(DateTime.UtcNow.ToString("O"), map);

            var json = JsonSerializer.Serialize(bf, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(BackupPath, json);
        }
        catch
        {
            // Backup failing should not block the main action.
        }
    }
}