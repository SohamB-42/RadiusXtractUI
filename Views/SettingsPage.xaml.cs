using Microsoft.UI.Xaml.Controls;
using RadiusXtractUI.ViewModels;

namespace RadiusXtractUI.Views;

public sealed partial class SettingsPage : UserControl
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        this.InitializeComponent();

        ViewModel.PropertyChanged += (s, e) => 
        {
            if (e.PropertyName == nameof(ViewModel.DefaultUnit))
            {
                if (ViewModel.DefaultUnit == "mm") RbMm.IsChecked = true;
                else if (ViewModel.DefaultUnit == "m") RbM.IsChecked = true;
                else if (ViewModel.DefaultUnit == "in") RbIn.IsChecked = true;
            }
        };
    }

    private void OnUnitChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag != null)
        {
            ViewModel.DefaultUnit = rb.Tag.ToString() ?? "mm";
        }
    }
}
