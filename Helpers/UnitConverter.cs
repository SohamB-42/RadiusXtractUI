namespace RadiusXtractUI.Helpers;

public static class UnitConverter
{
    public static double Convert(double meters, string unit)
    {
        return unit switch
        {
            "mm" => meters * 1000.0,
            "in" => meters * 39.3701,
            _ => meters
        };
    }

    public static string Format(double meters, string unit, int? decimals = null)
    {
        double val = Convert(meters, unit);
        int dp = decimals ?? unit switch
        {
            "m" => 4,
            "mm" => 2,
            "in" => 3,
            _ => 2
        };
        return val.ToString($"N{dp}");
    }
}
