using Avalonia.Controls;
using BluetoothHandsFreeToggle.Gui.ViewModels;

namespace BluetoothHandsFreeToggle.Gui.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        LanguageList.SelectionChanged += (_, _) =>
        {
            if (LanguageList.SelectedItem is not LanguageOption option)
                return;

            LanguageButton.Flyout?.Hide();
            LanguageList.SelectedItem = null;
            if (DataContext is FlyoutViewModel viewModel)
                viewModel.SetLanguage(option.Language);
        };
    }
}
