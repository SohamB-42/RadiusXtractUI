using RadiusXtractUI.Models;

namespace RadiusXtractUI.Helpers;

public static class StubData
{
    public static PipeResult GetDefaultResult() => new()
    {
        FileName = "pipe1.pcd",
        ActualRadius = 0.208838,
        EstimatedRadius = 0.197525,
        PaperRadius = 0.199504,
        TotalPoints = 63086,
        InlierPoints = 58669,
        Confidence = 97,
        RmsError = 0.00031,
        AxisX = 0.018,
        AxisY = 0.072,
        AxisZ = 0.997,
        Inclination = 4.2,
        Length = 1.482,
    };

    public static List<BatchFileItem> GetBatchFiles() =>
    [
        new()
        {
            FileName = "pipe1.pcd", PointCount = "63,086", Status = "Done",
            Progress = 100, RadiusDisplay = "197.53 mm",
            StatusColor = "#6fe0a3", StatusBg = "#244CD18C",
            ProgressColor = "#6fe0a3", DotColor = "#6fe0a3"
        },
        new()
        {
            FileName = "Pipe2.pcd", PointCount = "66,609", Status = "Done",
            Progress = 100, RadiusDisplay = "185.56 mm",
            StatusColor = "#6fe0a3", StatusBg = "#244CD18C",
            ProgressColor = "#6fe0a3", DotColor = "#6fe0a3"
        },
        new()
        {
            FileName = "pipe3.pcd", PointCount = "29,233", Status = "Processing",
            Progress = 64, RadiusDisplay = "—",
            StatusColor = "#3dc7eb", StatusBg = "#243DC7EB",
            ProgressColor = "#3dc7eb", DotColor = "#3dc7eb"
        },
        new()
        {
            FileName = "pipe4.pcd", PointCount = "55,724", Status = "Queued",
            Progress = 0, RadiusDisplay = "—",
            StatusColor = "#9aa2ab", StatusBg = "#12FFFFFF",
            ProgressColor = "#3dc7eb", DotColor = "#9aa2ab"
        },
        new()
        {
            FileName = "pipe5.pcd", PointCount = "115,194", Status = "Queued",
            Progress = 0, RadiusDisplay = "—",
            StatusColor = "#9aa2ab", StatusBg = "#12FFFFFF",
            ProgressColor = "#3dc7eb", DotColor = "#9aa2ab"
        },
        new()
        {
            FileName = "pipe14.pcd", PointCount = "86,966", Status = "Error",
            Progress = 100, RadiusDisplay = "fit failed",
            StatusColor = "#f5705a", StatusBg = "#24F5705A",
            ProgressColor = "#f5705a", DotColor = "#f5705a"
        },
    ];

    public static List<HistoryItem> GetHistoryItems() =>
    [
        new()
        {
            FileName = "pipe1.pcd", Date = "Jun 16, 2026 · 13:42",
            PointCount = "63,086", RadiusDisplay = "197.53 mm",
            DiameterDisplay = "395.05 mm", FitPercent = 97,
            FitLabel = "97%", FitColor = "#6fe0a3", FitBg = "#244CD18C"
        },
        new()
        {
            FileName = "pipe1.pcd (rev A)", Date = "Jun 16, 2026 · 11:08",
            PointCount = "63,086", RadiusDisplay = "196.88 mm",
            DiameterDisplay = "393.76 mm", FitPercent = 96,
            FitLabel = "96%", FitColor = "#6fe0a3", FitBg = "#244CD18C"
        },
        new()
        {
            FileName = "Pipe2.pcd", Date = "Jun 15, 2026 · 17:20",
            PointCount = "66,609", RadiusDisplay = "185.56 mm",
            DiameterDisplay = "371.13 mm", FitPercent = 94,
            FitLabel = "94%", FitColor = "#6fe0a3", FitBg = "#244CD18C"
        },
        new()
        {
            FileName = "pipe4.pcd", Date = "Jun 14, 2026 · 09:55",
            PointCount = "55,724", RadiusDisplay = "107.91 mm",
            DiameterDisplay = "215.81 mm", FitPercent = 88,
            FitLabel = "88%", FitColor = "#e6c84e", FitBg = "#24E6C84E"
        },
        new()
        {
            FileName = "pipe13.pcd", Date = "Jun 13, 2026 · 16:02",
            PointCount = "44,271", RadiusDisplay = "56.29 mm",
            DiameterDisplay = "112.58 mm", FitPercent = 79,
            FitLabel = "79%", FitColor = "#f5905a", FitBg = "#24F5905A"
        },
    ];
}
