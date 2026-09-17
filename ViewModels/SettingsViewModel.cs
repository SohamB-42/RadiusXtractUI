using CommunityToolkit.Mvvm.ComponentModel;

namespace RadiusXtractUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    // RANSAC parameters
    [ObservableProperty] private double _maxIterations = 1000;
    [ObservableProperty] private double _distanceThreshold = 3.0;
    [ObservableProperty] private double _minInlierRatio = 60;

    // Units
    [ObservableProperty] private string _defaultUnit = "mm";

    // Viewport defaults
    [ObservableProperty] private bool _autoRotateOnLoad = true;
    [ObservableProperty] private bool _showAxisGizmo = true;
    [ObservableProperty] private bool _loadFullDensity;

    public string MaxIterationsDisplay => MaxIterations.ToString("N0");
    public string ThresholdDisplay => $"{DistanceThreshold:F1} mm";
    public string InlierRatioDisplay => $"{MinInlierRatio}%";

    partial void OnMaxIterationsChanged(double value) => OnPropertyChanged(nameof(MaxIterationsDisplay));
    partial void OnDistanceThresholdChanged(double value) => OnPropertyChanged(nameof(ThresholdDisplay));
    partial void OnMinInlierRatioChanged(double value) => OnPropertyChanged(nameof(InlierRatioDisplay));
}
