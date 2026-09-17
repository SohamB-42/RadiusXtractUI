using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using RadiusXtractUI.ViewModels;
using RadiusXtractUI.Views;
using System;
using System.Threading.Tasks;

namespace RadiusXtractUI;

public sealed partial class MainWindow : Window
{
    private readonly AppShellViewModel ViewModel;
    private double _pointerX;
    private double _pointerY;
    private bool _isDraggingConsole;

    public MainWindow()
    {
        this.InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ViewModel = new AppShellViewModel();
        
        // Initial setup
        UpdateNavIndicators(0);
        PageEstimate.Visibility = Visibility.Visible;
        
        // Subscribe to view model events
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.StatusText))
                StatusLabel.Text = ViewModel.StatusText;
            if (e.PropertyName == nameof(ViewModel.StatusDotColor))
                StatusDot.Fill = GetSolidColorBrush(ViewModel.StatusDotColor);
            if (e.PropertyName == nameof(ViewModel.PointCountLabel))
                PointCountLabel.Text = ViewModel.PointCountLabel;
            if (e.PropertyName == nameof(ViewModel.ActiveFileName))
                ActiveFileText.Text = "— " + ViewModel.ActiveFileName;
            if (e.PropertyName == nameof(ViewModel.UnitLabel))
                UnitLabel.Text = ViewModel.UnitLabel;
            if (e.PropertyName == nameof(ViewModel.IsConsoleOpen))
                ConsoleCanvas.Visibility = ViewModel.IsConsoleOpen ? Visibility.Visible : Visibility.Collapsed;
        };

        // Hook up estimate page events
        PageEstimate.ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PageEstimate.ViewModel.EstimateState))
            {
                ViewModel.UpdateStatusForEstimate(PageEstimate.ViewModel.EstimateState);
                if (PageEstimate.ViewModel.EstimateState == "estimating")
                {
                    LogToConsole("RANSAC cylinder · 1,000 iter · threshold 0.003", "#8a929b");
                }
                else if (PageEstimate.ViewModel.EstimateState == "result")
                {
                    LogToConsole("Converged · 58,669 inliers (93.0%) · RMS 0.31 mm", "#6fe0a3");
                    LogToConsole($"Radius of the cylinder: {PageEstimate.ViewModel.RadiusDisplay}", "#3dc7eb");
                    LogToConsole("Done.", "#aab1ba");
                }
            }
            if (e.PropertyName == nameof(PageEstimate.ViewModel.SelectedUnit))
            {
                ViewModel.UnitLabel = PageEstimate.ViewModel.SelectedUnit;
                if (PageSettings.ViewModel.DefaultUnit != PageEstimate.ViewModel.SelectedUnit)
                {
                    PageSettings.ViewModel.DefaultUnit = PageEstimate.ViewModel.SelectedUnit;
                }
            }
        };

        // Hook up settings page events
        PageSettings.ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PageSettings.ViewModel.DefaultUnit))
            {
                if (PageEstimate.ViewModel.SelectedUnit != PageSettings.ViewModel.DefaultUnit)
                {
                    PageEstimate.ViewModel.SelectedUnit = PageSettings.ViewModel.DefaultUnit;
                }
            }
        };

        PageEstimate.ViewModel.FileLoaded += (s, filePath) =>
        {
            OnConsoleClear(this, new RoutedEventArgs());
            LogToConsole($"Selected file: {filePath}", "#aab1ba");
            LogToConsole("Loaded points · bounds [4.41 × 2.01 × 2.00]", "#aab1ba");
            LogToConsole("Downsampling (voxel 0.004) → 28,803 points", "#8a929b");
        };
        
        // Initialize status
        ViewModel.UpdateStatusForEstimate("empty");
        
        // Set up console toggle event from Estimate page
        PageEstimate.ToggleConsoleRequested += (s, e) =>
        {
            ViewModel.IsConsoleOpen = !ViewModel.IsConsoleOpen;
        };
    }

    private void OnNavClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int index))
        {
            ViewModel.SelectedNavIndex = index;
            UpdateNavIndicators(index);
            
            PageEstimate.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            PageBatch.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            PageHistory.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
            PageSettings.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateNavIndicators(int selectedIndex)
    {
        NavEstimateIndicator.Visibility = selectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        NavBatchIndicator.Visibility = selectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        NavHistoryIndicator.Visibility = selectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        NavSettingsIndicator.Visibility = selectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;

        // Reset colors
        SetIconColor(NavEstimate, "#8b939c");
        SetIconColor(NavBatch, "#8b939c");
        SetIconColor(NavHistory, "#8b939c");
        SetIconColor(NavSettings, "#8b939c");

        // Set active color
        switch (selectedIndex)
        {
            case 0: SetIconColor(NavEstimate, "#3dc7eb"); break;
            case 1: SetIconColor(NavBatch, "#3dc7eb"); break;
            case 2: SetIconColor(NavHistory, "#3dc7eb"); break;
            case 3: SetIconColor(NavSettings, "#3dc7eb"); break;
        }
    }

    private void SetIconColor(Button btn, string hex)
    {
        if (btn.Content is Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is FontIcon icon)
                {
                    icon.Foreground = GetSolidColorBrush(hex);
                }
            }
        }
    }

    // --- Console Overlay Logic ---
    private void OnConsoleClose(object sender, RoutedEventArgs e) => ViewModel.IsConsoleOpen = false;
    
    private void OnConsoleClear(object sender, RoutedEventArgs e)
    {
        // Keep only the blinking cursor
        if (ConsoleLines.Children.Count > 0)
        {
            var cursor = ConsoleLines.Children[^1];
            ConsoleLines.Children.Clear();
            ConsoleLines.Children.Add(cursor);
        }
    }

    public void LogToConsole(string message, string colorHex = "#aab1ba")
    {
        var grid = new Grid { ColumnSpacing = 11 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var timeBlock = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm:ss"),
            FontSize = 12,
            Foreground = GetSolidColorBrush("#566069"),
            FontFamily = new FontFamily("Cascadia Mono, Consolas")
        };

        var msgBlock = new TextBlock
        {
            Text = message,
            FontSize = 12,
            Foreground = GetSolidColorBrush(colorHex),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(msgBlock, 1);

        grid.Children.Add(timeBlock);
        grid.Children.Add(msgBlock);

        // Insert before the blinking cursor
        if (ConsoleLines.Children.Count > 0)
        {
            ConsoleLines.Children.Insert(ConsoleLines.Children.Count - 1, grid);
        }
        else
        {
            ConsoleLines.Children.Add(grid);
        }

        if (ConsoleLines.Parent is ScrollViewer sv)
        {
            sv.UpdateLayout();
            sv.ChangeView(null, sv.ScrollableHeight, null);
        }
    }

    private void OnConsoleDragStart(object sender, PointerRoutedEventArgs e)
    {
        var pointerPoint = e.GetCurrentPoint(ConsoleCanvas);
        _pointerX = pointerPoint.Position.X;
        _pointerY = pointerPoint.Position.Y;
        _isDraggingConsole = true;
        ((UIElement)sender).CapturePointer(e.Pointer);
    }

    private void OnConsoleDrag(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingConsole)
        {
            var pointerPoint = e.GetCurrentPoint(ConsoleCanvas);
            double deltaX = pointerPoint.Position.X - _pointerX;
            double deltaY = pointerPoint.Position.Y - _pointerY;

            double currentLeft = Canvas.GetLeft(ConsolePanel);
            double currentTop = Canvas.GetTop(ConsolePanel);

            if (double.IsNaN(currentLeft)) currentLeft = 312;
            if (double.IsNaN(currentTop)) currentTop = 350;

            double newLeft = currentLeft + deltaX;
            double newTop = currentTop + deltaY;

            Canvas.SetLeft(ConsolePanel, newLeft);
            Canvas.SetTop(ConsolePanel, newTop);

            _pointerX = pointerPoint.Position.X;
            _pointerY = pointerPoint.Position.Y;
        }
    }

    private void OnConsoleDragEnd(object sender, PointerRoutedEventArgs e)
    {
        _isDraggingConsole = false;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
    }

    // Helper
    private SolidColorBrush GetSolidColorBrush(string hex)
    {
        hex = hex.Replace("#", "");
        byte a = 255;
        byte r, g, b;

        if (hex.Length == 8)
        {
            a = Convert.ToByte(hex.Substring(0, 2), 16);
            r = Convert.ToByte(hex.Substring(2, 2), 16);
            g = Convert.ToByte(hex.Substring(4, 2), 16);
            b = Convert.ToByte(hex.Substring(6, 2), 16);
        }
        else if (hex.Length == 6)
        {
            r = Convert.ToByte(hex.Substring(0, 2), 16);
            g = Convert.ToByte(hex.Substring(2, 2), 16);
            b = Convert.ToByte(hex.Substring(4, 2), 16);
        }
        else
        {
            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
    }
}
