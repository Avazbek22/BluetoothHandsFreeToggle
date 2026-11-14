using Avalonia.Controls.Primitives;

namespace StereoKeeper.Gui.Controls;

/// <summary>
/// A rounded on/off switch. With <see cref="AutoToggle"/> off, a click only runs the command and
/// the switch keeps showing the bound state until the action behind it actually completes.
/// </summary>
public sealed class PillSwitch : ToggleButton
{
    public bool AutoToggle { get; set; } = true;

    protected override Type StyleKeyOverride => typeof(PillSwitch);

    protected override void Toggle()
    {
        if (AutoToggle)
            base.Toggle();
    }
}
