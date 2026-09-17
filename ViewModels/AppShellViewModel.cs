using CommunityToolkit.Mvvm.ComponentModel;

namespace RadiusXtractUI.ViewModels;

public partial class AppShellViewModel : ObservableObject
{
    [ObservableProperty] private int _selectedNavIndex;
    [ObservableProperty] private string _statusText = "No file loaded";
    [ObservableProperty] private string _statusDotColor = "#f5b754";
    [ObservableProperty] private string _pointCountLabel = "— points";
    [ObservableProperty] private string _unitLabel = "mm";
    [ObservableProperty] private string _activeFileName = "No file";
    [ObservableProperty] private bool _isConsoleOpen;

    public EstimateViewModel EstimateVM { get; } = new();
    public BatchViewModel BatchVM { get; } = new();
    public HistoryViewModel HistoryVM { get; } = new();
    public SettingsViewModel SettingsVM { get; } = new();

    public void UpdateStatusForEstimate(string state)
    {
        switch (state)
        {
            case "empty":
                StatusText = "No file loaded";
                StatusDotColor = "#f5b754";
                PointCountLabel = "— points";
                ActiveFileName = "No file";
                break;
            case "idle":
                StatusText = "Ready · pipe1.pcd";
                StatusDotColor = "#8fd8ff";
                PointCountLabel = "63,086 points";
                ActiveFileName = "pipe1.pcd";
                break;
            case "estimating":
                StatusText = "Estimating…";
                StatusDotColor = "#3dc7eb";
                break;
            case "result":
                StatusText = "Estimate complete";
                StatusDotColor = "#6fe0a3";
                PointCountLabel = "63,086 points";
                ActiveFileName = "pipe1.pcd";
                break;
        }
    }
}
