namespace BluetoothHandsFreeToggle.Windows;

public static class WindowsPaths
{
    public static string GetProgramDataFile(string appFolder, string fileName)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(programData, appFolder, fileName);
    }
}