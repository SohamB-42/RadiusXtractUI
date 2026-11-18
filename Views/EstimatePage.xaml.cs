using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using RadiusXtractUI.ViewModels;
using RadiusXtractUI.Helpers;
using RadiusXtractUI.Models;
using RadiusXtractUI.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace RadiusXtractUI.Views;

public sealed partial class EstimatePage : UserControl
{
    public EstimateViewModel ViewModel { get; } = new();

    public event EventHandler? ToggleConsoleRequested;

    private bool _isResultsPanelOpen = true;
    private bool _isGpuReady = false;
    private List<Vector3>? _cachedPoints;
    private int[]? _cachedLabels;
    private PipeResult? _cachedResult;
    private string? _cachedFilePath;

    public EstimatePage()
    {
        this.InitializeComponent();

        this.Loaded += OnLoaded;
        ViewModel.FileLoaded += OnFileLoaded;
        ViewModel.EstimateRequested += OnEstimateRequested;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitializeGpuViewportAsync();
    }

    private async Task InitializeGpuViewportAsync()
    {
        try
        {
            await GpuViewport.EnsureCoreWebView2Async();
            GpuViewport.CoreWebView2.WebMessageReceived += OnGpuWebMessageReceived;

            string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "pointcloud_viewer.html");
            if (File.Exists(htmlPath))
            {
                GpuViewport.CoreWebView2.Navigate(htmlPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPU Viewport Initialization Exception: {ex.Message}");
        }
    }

    private void OnGpuWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            if (doc.RootElement.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "ready")
            {
                _isGpuReady = true;
                UpdateGpuOptions();

                if (_cachedPoints != null && _cachedPoints.Count > 0)
                {
                    UploadPointsToGpu(_cachedPoints, _cachedLabels, _cachedResult);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebMessage error: {ex.Message}");
        }
    }

    private void OnFileLoaded(object? sender, string filePath)
    {
        _cachedFilePath = filePath;
        Task.Run(async () =>
        {
            try
            {
                string pcdPathToRead = filePath;

                // Check if raw LiDAR conversion is required (.laz, .las, .ply, etc.)
                if (LidarConversionService.Instance.RequiresConversion(filePath))
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ViewModel.AiStatusMessage = $"Converting raw LiDAR ({Path.GetExtension(filePath).ToUpperInvariant()}) to origin-shifted PCD...";
                        ViewModel.EstimateProgress = 5;
                    });

                    var conversionProgress = new Progress<string>(msg =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            ViewModel.AiStatusMessage = msg;
                        });
                    });

                    var convResult = await LidarConversionService.Instance.ConvertToPcdAsync(filePath, conversionProgress);
                    if (!convResult.Success)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            ViewModel.AiStatusMessage = $"LiDAR conversion failed: {convResult.ErrorMessage}";
                            ViewModel.EstimateProgress = 0;
                        });
                        return;
                    }

                    pcdPathToRead = convResult.ConvertedPcdPath;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.AiStatusMessage = "Loading point cloud...";
                });

                var points = PcdReader.Read(pcdPathToRead);
                _cachedPoints = points;
                _cachedLabels = null;
                _cachedResult = null;

                if (points.Count == 0)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ViewModel.AiStatusMessage = "No points could be loaded from the point cloud.";
                        ViewModel.TotalPoints = 0;
                    });
                    return;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.TotalPoints = points.Count;
                    ViewModel.PipePointsCount = 0;
                    ViewModel.ClutterPointsCount = 0;
                    ViewModel.AiInferenceTimeMs = 0;
                    ViewModel.AiStatusMessage = "Point cloud loaded. Click 'Estimate radius' to begin AI analysis.";
                    ViewModel.EstimateProgress = 0;
                    ViewModel.EstimateState = "idle";

                    UploadPointsToGpu(points, null, null);
                    UpdateGpuOptions();
                });
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.AiStatusMessage = $"Error loading point cloud: {ex.Message}";
                });
            }
        });
    }

    private async void OnEstimateRequested(object? sender, EventArgs e)
    {
        if (_cachedPoints != null && _cachedPoints.Count > 0)
        {
            await ExecuteEstimationAsync(_cachedPoints, _cachedFilePath);
        }
    }

    private async Task ExecuteEstimationAsync(List<Vector3> points, string? filePath)
    {
        string fileName = Path.GetFileName(filePath ?? ViewModel.ActiveFileName);
        
        DispatcherQueue.TryEnqueue(() =>
        {
            ViewModel.EstimateState = "estimating";
            ViewModel.EstimateProgress = 10;
            ViewModel.TotalPoints = points.Count;
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int[] labels;
        List<Vector3> pointsToFit;

        if (ViewModel.UseAiSegregation)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.AiStatusMessage = "Running PointNet++ segmentation on GPU...";
            });

            var progressHandler = new Progress<double>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.EstimateProgress = 10 + (int)(p * 60); // 10% to 70%
                });
            });

            try
            {
                labels = await DeepSegEngine.Instance.PredictLabelsAsync(points, cellSize: 0.5f, maxPoints: 2048, progress: progressHandler);
                string hw = DeepSegEngine.Instance.UsedDirectML ? "DirectML GPU" : "CPU Fallback";
                DispatcherQueue.TryEnqueue(() => ViewModel.EngineHardware = hw);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AI Inference Exception: {ex.Message}");
                labels = new int[points.Count];
                Array.Fill(labels, 1);
                DispatcherQueue.TryEnqueue(() => ViewModel.EngineHardware = "CPU (Fallback)");
            }

            int pipeCount = 0;
            var filteredPoints = new List<Vector3>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                if (labels[i] == 1)
                {
                    pipeCount++;
                    filteredPoints.Add(points[i]);
                }
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.PipePointsCount = pipeCount;
                ViewModel.ClutterPointsCount = points.Count - pipeCount;
            });

            // If AI detected a meaningful pipe segment (at least 200 points and >= 5% of points), fit to segmented points.
            // Otherwise, fall back to the full point cloud to prevent degenerate micro-cylinder fits on low consensus.
            if (filteredPoints.Count >= 200 && filteredPoints.Count >= points.Count * 0.05)
            {
                pointsToFit = filteredPoints;
            }
            else
            {
                pointsToFit = points;
                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.AiStatusMessage = "Low AI pipe consensus; fitting cylinder across full point cloud...";
                });
            }
        }
        else
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.AiStatusMessage = "Running Algebraic Taubin/RANSAC fitting...";
                ViewModel.EstimateProgress = 50;
                ViewModel.PipePointsCount = points.Count;
                ViewModel.ClutterPointsCount = 0;
                ViewModel.EngineHardware = "Algebraic (No AI)";
            });

            labels = new int[points.Count];
            Array.Fill(labels, 1);
            pointsToFit = points;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            ViewModel.AiStatusMessage = "Refining cylinder parameters...";
            ViewModel.EstimateProgress = 80;
        });

        var fitResult = await Task.Run(() => CylinderFitter.Fit(pointsToFit, fileName));
        sw.Stop();

        DispatcherQueue.TryEnqueue(() =>
        {
            ViewModel.AiInferenceTimeMs = sw.ElapsedMilliseconds;
            ViewModel.CurrentResult = fitResult;
            ViewModel.EstimateProgress = 100;
            ViewModel.EstimateState = "result";

            _cachedPoints = points;
            _cachedLabels = labels;
            _cachedResult = fitResult;

            if (ViewModel.UseAiSegregation && ViewModel.ColorMode != "ai")
            {
                ViewModel.ColorMode = "ai";
                if (RbAi != null) RbAi.IsChecked = true;
            }
            else if (!ViewModel.UseAiSegregation && ViewModel.ColorMode == "ai")
            {
                ViewModel.ColorMode = "axis";
                if (RbAxis != null) RbAxis.IsChecked = true;
            }

            UploadPointsToGpu(points, labels, fitResult);
        });
    }

    private void UploadPointsToGpu(List<Vector3> points, int[]? labels, PipeResult? result)
    {
        if (!_isGpuReady || points.Count == 0) return;

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        float[] posArray = new float[points.Count * 3];
        float[] outlierArray = new float[points.Count];
        float[] labelArray = new float[points.Count];

        Vector3 axisDir = result != null && (result.AxisX != 0 || result.AxisY != 0 || result.AxisZ != 0)
            ? Vector3.Normalize(new Vector3((float)result.AxisX, (float)result.AxisY, (float)result.AxisZ))
            : Vector3.UnitZ;
        Vector3 axisCenter = result != null ? new Vector3((float)result.CenterX, (float)result.CenterY, (float)result.CenterZ) : Vector3.Zero;
        float targetRadius = result != null ? (float)result.EstimatedRadius : 0.05f;
        float outlierTol = result != null && result.RmsError > 0
            ? Math.Max((float)result.RmsError * 2.0f, targetRadius * 0.08f)
            : Math.Max(0.005f, targetRadius * 0.08f);

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z > maxZ) maxZ = p.Z;

            posArray[i * 3] = p.X;
            posArray[i * 3 + 1] = p.Y;
            posArray[i * 3 + 2] = p.Z;

            if (result != null)
            {
                var relP = p - axisCenter;
                float distAxis = Vector3.Cross(relP, axisDir).Length();
                outlierArray[i] = Math.Abs(distAxis - targetRadius) > outlierTol ? 1.0f : 0.0f;
            }

            labelArray[i] = (labels != null && i < labels.Length) ? labels[i] : 1.0f;
        }

        float cx = (minX + maxX) / 2f;
        float cy = (minY + maxY) / 2f;
        float cz = (minZ + maxZ) / 2f;
        float modelRad = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)) / 2f;
        if (modelRad < 0.001f) modelRad = 1.0f;

        byte[] posBytes = new byte[posArray.Length * sizeof(float)];
        Buffer.BlockCopy(posArray, 0, posBytes, 0, posBytes.Length);
        string posBase64 = Convert.ToBase64String(posBytes);

        byte[] outBytes = new byte[outlierArray.Length * sizeof(float)];
        Buffer.BlockCopy(outlierArray, 0, outBytes, 0, outBytes.Length);
        string outBase64 = Convert.ToBase64String(outBytes);

        byte[] lblBytes = new byte[labelArray.Length * sizeof(float)];
        Buffer.BlockCopy(labelArray, 0, lblBytes, 0, lblBytes.Length);
        string lblBase64 = Convert.ToBase64String(lblBytes);

        var msg = new
        {
            type = "loadPoints",
            pointsBase64 = posBase64,
            outliersBase64 = outBase64,
            labelsBase64 = lblBase64,
            center = new[] { cx, cy, cz },
            boundsMin = new[] { minX, minY, minZ },
            boundsMax = new[] { maxX, maxY, maxZ },
            modelRadius = modelRad
        };

        SendJsonToGpu(msg);

        if (result != null)
        {
            UpdateGpuCylinder(result);
        }
        UpdateGpuOptions();
    }

    private void UpdateGpuOptions()
    {
        if (!_isGpuReady) return;
        var msg = new
        {
            type = "setOptions",
            pointSize = ViewModel.PointSize,
            colorMode = ViewModel.ColorMode?.ToLower() ?? "axis",
            showOutliers = ViewModel.ShowOutliers,
            showOverlay = ViewModel.ShowOverlay,
            showGrid = ViewModel.ShowGrid
        };
        SendJsonToGpu(msg);
    }

    private void UpdateGpuCylinder(PipeResult res)
    {
        if (!_isGpuReady) return;
        var msg = new
        {
            type = "setCylinder",
            center = new[] { (float)res.CenterX, (float)res.CenterY, (float)res.CenterZ },
            axis = new[] { (float)res.AxisX, (float)res.AxisY, (float)res.AxisZ },
            radius = (float)res.EstimatedRadius,
            length = (float)res.Length,
            rms = (float)res.RmsError
        };
        SendJsonToGpu(msg);
    }

    private void SendJsonToGpu(object payload)
    {
        if (GpuViewport?.CoreWebView2 == null) return;
        string json = JsonSerializer.Serialize(payload);
        GpuViewport.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_isGpuReady) return;

        if (e.PropertyName == nameof(ViewModel.ColorMode) ||
            e.PropertyName == nameof(ViewModel.PointSize) ||
            e.PropertyName == nameof(ViewModel.ShowOutliers) ||
            e.PropertyName == nameof(ViewModel.ShowOverlay) ||
            e.PropertyName == nameof(ViewModel.ShowGrid))
        {
            UpdateGpuOptions();
        }
        else if (e.PropertyName == nameof(ViewModel.CurrentResult) && ViewModel.CurrentResult != null)
        {
            UpdateGpuCylinder(ViewModel.CurrentResult);
            if (_cachedPoints != null && _cachedPoints.Count > 0)
            {
                UploadPointsToGpu(_cachedPoints, _cachedLabels, ViewModel.CurrentResult);
            }
        }
    }

    private async void OnOpenFileClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add(".pcd");
        picker.FileTypeFilter.Add(".laz");
        picker.FileTypeFilter.Add(".las");
        picker.FileTypeFilter.Add(".ply");
        picker.FileTypeFilter.Add(".xyz");
        picker.FileTypeFilter.Add(".pts");
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add(".e57");

        var window = (Application.Current as App)?.MainWindow;
        if (window != null)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            ViewModel.LoadFile(file.Path);
        }
    }

    private void OnEmptyStateClick(object sender, PointerRoutedEventArgs e)
    {
        OnOpenFileClick(sender, e);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Load point cloud";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var file = items.OfType<StorageFile>().FirstOrDefault();
            if (file != null && LidarConversionService.Instance.IsSupportedFormat(file.Path))
            {
                ViewModel.LoadFile(file.Path);
            }
        }
    }

    private void OnResetViewClick(object sender, RoutedEventArgs e)
    {
        SendJsonToGpu(new { type = "resetView" });
    }

    private void OnToggleOverlayClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowOverlay = !ViewModel.ShowOverlay;
    }

    private void OnUnitChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag != null)
        {
            ViewModel.SelectedUnit = rb.Tag.ToString() ?? "mm";
        }
    }

    private void OnColorModeChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag != null)
        {
            ViewModel.ColorMode = rb.Tag.ToString() ?? "axis";
            UpdateGpuOptions();
        }
    }

    private void OnConsoleClick(object sender, RoutedEventArgs e)
    {
        ToggleConsoleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnToggleResultsClick(object sender, RoutedEventArgs e)
    {
        _isResultsPanelOpen = !_isResultsPanelOpen;

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = _isResultsPanelOpen ? 0 : 368,
            Duration = new Duration(TimeSpan.FromMilliseconds(250)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut }
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, ResultsPanelTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "X");

        storyboard.Children.Add(animation);
        storyboard.Begin();

        ToggleResultsIcon.Glyph = _isResultsPanelOpen ? "\uE76C" : "\uE76B";
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var report = ViewModel.GetReportText();
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(report);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("Text File", new List<string>() { ".txt" });
        picker.SuggestedFileName = $"RadiusXtract_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

        var window = (Application.Current as App)?.MainWindow;
        if (window != null)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            var report = ViewModel.GetReportText();
            await Windows.Storage.FileIO.WriteTextAsync(file, report);
        }
    }
}
