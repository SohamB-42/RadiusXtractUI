using Microsoft.UI.Xaml.Controls;
using RadiusXtractUI.ViewModels;

namespace RadiusXtractUI.Views;

public sealed partial class HistoryPage : UserControl
{
    public HistoryViewModel ViewModel { get; } = new();

    public HistoryPage()
    {
        this.InitializeComponent();
    }
}
