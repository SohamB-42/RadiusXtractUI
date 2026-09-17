namespace RadiusXtractUI.Models;

public class PipeResult
{
    public string FileName { get; set; } = "";
    public double ActualRadius { get; set; }
    public double EstimatedRadius { get; set; }
    public double PaperRadius { get; set; }
    public int TotalPoints { get; set; }
    public int InlierPoints { get; set; }
    public double Confidence { get; set; }
    public double RmsError { get; set; }
    public double AxisX { get; set; }
    public double AxisY { get; set; }
    public double AxisZ { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double CenterZ { get; set; }
    public double Inclination { get; set; }
    public double Length { get; set; }
}
