namespace FalconBMS.Launcher.Models;

/// <summary>
/// Converts between axis curve enum values and the integer values stored in Falcon config data.
/// </summary>

public static class AxCurveCodec
{
    public static int DeadzoneToInt(AxCurve curve) => curve switch
    {
        AxCurve.None => 0,
        AxCurve.Small => 100,
        AxCurve.Medium => 500,
        AxCurve.Large => 1000,
        _ => 0
    };

    public static int SaturationToInt(AxCurve curve) => curve switch
    {
        AxCurve.None => -1,
        AxCurve.Small => 9500,
        AxCurve.Medium => 9000,
        AxCurve.Large => 8500,
        _ => -1
    };

    public static AxCurve DeadzoneFromInt(int v) => v switch
    {
        0 => AxCurve.None,
        100 => AxCurve.Small,
        500 => AxCurve.Medium,
        1000 => AxCurve.Large,
        _ => AxCurve.None
    };

    public static AxCurve SaturationFromInt(int v) => v switch
    {
        -1 => AxCurve.None,
        9500 => AxCurve.Small,
        9000 => AxCurve.Medium,
        8500 => AxCurve.Large,
        _ => AxCurve.None
    };
}