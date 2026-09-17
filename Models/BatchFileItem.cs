namespace RadiusXtractUI.Models;

public class BatchFileItem
{
    public string FileName { get; set; } = "";
    public string PointCount { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public double Progress { get; set; }
    public string RadiusDisplay { get; set; } = "—";
    public string StatusColor { get; set; } = "#9aa2ab";
    public string StatusBg { get; set; } = "#12FFFFFF";
    public string ProgressColor { get; set; } = "#3dc7eb";
    public string DotColor { get; set; } = "#9aa2ab";
}
