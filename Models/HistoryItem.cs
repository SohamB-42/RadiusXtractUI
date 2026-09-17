namespace RadiusXtractUI.Models;

public class HistoryItem
{
    public string FileName { get; set; } = "";
    public string Date { get; set; } = "";
    public string PointCount { get; set; } = "";
    public string Method { get; set; } = "RANSAC";
    public string RadiusDisplay { get; set; } = "";
    public string DiameterDisplay { get; set; } = "";
    public int FitPercent { get; set; }
    public string FitLabel { get; set; } = "";
    public string FitColor { get; set; } = "#6fe0a3";
    public string FitBg { get; set; } = "#2418CC8C";
}
