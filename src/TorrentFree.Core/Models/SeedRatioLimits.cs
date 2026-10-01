namespace TorrentFree.Models;

public static class SeedRatioLimits
{
    public const double Maximum = 100;
    public static double Normalize(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0, Maximum) : 0;
}
