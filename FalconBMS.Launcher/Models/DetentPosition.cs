namespace FalconBMS.Launcher.Models;

/// <summary>
/// Throttle detent positions stored in Falcon-native axis units (0..65535).
/// 0 = fully idle/cutoff, 65535 = full forward / max burner.
/// </summary>
public sealed record DetentPosition(int AB, int IDLE)
{
    public const int AxisMin = 0;
    public const int AxisMax = 65535;

    public static DetentPosition Default => new(AxisMax, AxisMin);

    public static int Clamp(int v)
    {
        if (v < AxisMin) return AxisMin;
        if (v > AxisMax) return AxisMax;
        return v;
    }
}