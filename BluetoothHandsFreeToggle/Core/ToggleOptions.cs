namespace BluetoothHandsFreeToggle.Core;

public sealed class ToggleOptions
{
    public bool ApplyRegistry { get; set; } = true;
    public bool StartServicesOnEnable { get; set; } = true;

    public static ToggleOptions FromArgs(string[] args)
    {
        var o = new ToggleOptions();
        foreach (var a in args)
        {
            if (a.Equals("--no-registry", StringComparison.OrdinalIgnoreCase))
                o.ApplyRegistry = false;

            if (a.Equals("--no-start", StringComparison.OrdinalIgnoreCase))
                o.StartServicesOnEnable = false;
        }
        return o;
    }
}