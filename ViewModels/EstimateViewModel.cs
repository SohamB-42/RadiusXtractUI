using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadiusXtractUI.Helpers;
using RadiusXtractUI.Models;

namespace RadiusXtractUI.ViewModels;

public partial class EstimateViewModel : ObservableObject
{
    // State: empty, idle, estimating, result
    [ObservableProperty] private string _estimateState = "idle";
    [ObservableProperty] private string _selectedTab = "results";
    [ObservableProperty] private string _selectedUnit = "mm";
    [ObservableProperty] private double _pointSize = 2.4;
    [ObservableProperty] private string _colorMode = "axis";
    [ObservableProperty] private bool _showOverlay = true;
    [ObservableProperty] private bool _showOutliers;
    [ObservableProperty] private bool _showGrid;
    [ObservableProperty] private bool _autoRotate = true;
    [ObservableProperty] private int _estimateProgress;

    [ObservableProperty] private PipeResult _currentResult = StubData.GetDefaultResult();

    // AI Segregation Properties
    [ObservableProperty] private bool _useAiSegregation = true;
    [ObservableProperty] private string _engineHardware = "DirectML GPU";
    [ObservableProperty] private string _aiStatusMessage = "Ready";
    [ObservableProperty] private int _totalPoints;
    [ObservableProperty] private int _pipePointsCount;
    [ObservableProperty] private int _clutterPointsCount;
    [ObservableProperty] private double _aiInferenceTimeMs;

    public string RadiusDisplay => UnitConverter.Format(CurrentResult.EstimatedRadius, SelectedUnit);
    public string DiameterDisplay => UnitConverter.Format(CurrentResult.EstimatedRadius * 2, SelectedUnit);
    public string LengthDisplay => UnitConverter.Format(CurrentResult.Length, SelectedUnit,
        SelectedUnit == "m" ? 3 : 1);
    public string RmsDisplay => UnitConverter.Format(CurrentResult.RmsError, SelectedUnit,
        SelectedUnit == "m" ? 5 : SelectedUnit == "in" ? 4 : 2);
    public string ConfidenceDisplay => $"{CurrentResult.Confidence}%";
    public double ConfidenceWidth => CurrentResult.Confidence;
    public string PointSizeLabel => PointSize.ToString("F1");

    public string TotalPointsDisplay => TotalPoints > 0 ? TotalPoints.ToString("N0") : (CurrentResult.TotalPoints > 0 ? CurrentResult.TotalPoints.ToString("N0") : "0");
    public string PipePointsDisplay => PipePointsCount.ToString("N0");
    public string ClutterPointsDisplay => ClutterPointsCount.ToString("N0");
    public string InlierPointsDisplay => CurrentResult.InlierPoints > 0 ? CurrentResult.InlierPoints.ToString("N0") : "0";
    public string InferenceTimeDisplay => $"{AiInferenceTimeMs:F0} ms";
    public string SegregationSummary => TotalPoints > 0
        ? $"{PipePointsCount:N0} pipe ({PipePointsCount * 100.0 / Math.Max(1, TotalPoints):F1}%) · {ClutterPointsCount:N0} clutter"
        : "No points";
    public string EngineLabel => "PointNet++ (AI Pipeline)";

    public string AxisXDisplay => CurrentResult.AxisX.ToString("F3");
    public string AxisYDisplay => CurrentResult.AxisY.ToString("F3");
    public string AxisZDisplay => CurrentResult.AxisZ.ToString("F3");
    public string InclinationDisplay => $"{CurrentResult.Inclination:F1}°";

    partial void OnUseAiSegregationChanged(bool value)
    {
        OnPropertyChanged(nameof(EngineLabel));
    }

    partial void OnTotalPointsChanged(int value)
    {
        OnPropertyChanged(nameof(TotalPointsDisplay));
        OnPropertyChanged(nameof(SegregationSummary));
    }

    partial void OnPipePointsCountChanged(int value)
    {
        OnPropertyChanged(nameof(PipePointsDisplay));
        OnPropertyChanged(nameof(SegregationSummary));
    }

    partial void OnClutterPointsCountChanged(int value)
    {
        OnPropertyChanged(nameof(ClutterPointsDisplay));
        OnPropertyChanged(nameof(SegregationSummary));
    }

    partial void OnAiInferenceTimeMsChanged(double value)
    {
        OnPropertyChanged(nameof(InferenceTimeDisplay));
    }

    partial void OnCurrentResultChanged(PipeResult value)
    {
        OnPropertyChanged(nameof(RadiusDisplay));
        OnPropertyChanged(nameof(DiameterDisplay));
        OnPropertyChanged(nameof(LengthDisplay));
        OnPropertyChanged(nameof(RmsDisplay));
        OnPropertyChanged(nameof(ConfidenceDisplay));
        OnPropertyChanged(nameof(ConfidenceWidth));
        OnPropertyChanged(nameof(InlierPointsDisplay));
        OnPropertyChanged(nameof(TotalPointsDisplay));
        OnPropertyChanged(nameof(AxisXDisplay));
        OnPropertyChanged(nameof(AxisYDisplay));
        OnPropertyChanged(nameof(AxisZDisplay));
        OnPropertyChanged(nameof(InclinationDisplay));
    }

    public bool IsEmpty => EstimateState == "empty";
    public bool IsIdle => EstimateState == "idle";
    public bool IsEstimating => EstimateState == "estimating";
    public bool HasResult => EstimateState == "result";
    public bool CanEstimate => !IsEstimating && !IsEmpty;
    public bool ShowResults => SelectedTab == "results";
    public bool ShowDisplay => SelectedTab == "display";

    partial void OnSelectedUnitChanged(string value)
    {
        OnPropertyChanged(nameof(RadiusDisplay));
        OnPropertyChanged(nameof(DiameterDisplay));
        OnPropertyChanged(nameof(LengthDisplay));
        OnPropertyChanged(nameof(RmsDisplay));
    }

    partial void OnEstimateStateChanged(string value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsEstimating));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(CanEstimate));
    }

    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(ShowResults));
        OnPropertyChanged(nameof(ShowDisplay));
    }

    partial void OnPointSizeChanged(double value)
    {
        OnPropertyChanged(nameof(PointSizeLabel));
    }

    // Added properties
    public string ActiveFileName { get; private set; } = "No file loaded";
    public event EventHandler<string>? FileLoaded;
    
    [RelayCommand]
    private void LoadDemo()
    {
        // kept for backwards compatibility or rename? 
        // We will just rename to LoadFile internally, but let's just make a new method.
    }

    public void LoadFile(string filePath)
    {
        ActiveFileName = System.IO.Path.GetFileName(filePath);
        OnPropertyChanged(nameof(ActiveFileName));
        EstimateState = "idle";
        
        FileLoaded?.Invoke(this, filePath);
    }

    public event EventHandler? EstimateRequested;

    [RelayCommand]
    private void RunEstimate()
    {
        if (EstimateState == "estimating") return;
        EstimateRequested?.Invoke(this, EventArgs.Empty);
    }

    public string GetReportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== RadiusXtract Estimation Report ===");
        sb.AppendLine($"File: {ActiveFileName}");
        sb.AppendLine($"Date: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Engine: {EngineLabel}");
        sb.AppendLine();
        if (UseAiSegregation)
        {
            sb.AppendLine("--- AI PointNet++ Segregation ---");
            sb.AppendLine($"Hardware: {EngineHardware}");
            sb.AppendLine($"Inference Time: {InferenceTimeDisplay}");
            sb.AppendLine($"Total Points: {TotalPointsDisplay}");
            sb.AppendLine($"Pipe Surface Points: {PipePointsDisplay} ({PipePointsCount * 100.0 / Math.Max(1, TotalPoints):F1}%)");
            sb.AppendLine($"Clutter Rejected: {ClutterPointsDisplay}");
            sb.AppendLine();
        }
        sb.AppendLine("--- Measurements ---");
        sb.AppendLine($"Estimated Radius: {RadiusDisplay}");
        sb.AppendLine($"Diameter: {DiameterDisplay}");
        sb.AppendLine($"Length / Extent: {LengthDisplay}");
        sb.AppendLine();
        sb.AppendLine("--- Fit Quality ---");
        sb.AppendLine($"RMS Error: {RmsDisplay}");
        sb.AppendLine($"Fit Confidence: {ConfidenceDisplay}");
        sb.AppendLine($"Inlier Points: {CurrentResult.InlierPoints:N0} / {CurrentResult.TotalPoints:N0}");
        sb.AppendLine();
        sb.AppendLine("--- Orientation ---");
        sb.AppendLine($"Axis Vector: X={CurrentResult.AxisX:F3}, Y={CurrentResult.AxisY:F3}, Z={CurrentResult.AxisZ:F3}");
        sb.AppendLine($"Inclination: {CurrentResult.Inclination:F1}°");
        return sb.ToString();
    }
}
