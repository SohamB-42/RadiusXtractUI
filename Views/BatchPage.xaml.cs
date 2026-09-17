using Microsoft.UI.Xaml.Controls;
using RadiusXtractUI.ViewModels;

namespace RadiusXtractUI.Views;

public sealed partial class BatchPage : UserControl
{
    public BatchViewModel ViewModel { get; } = new();

    public BatchPage()
    {
        this.InitializeComponent();
    }
}
