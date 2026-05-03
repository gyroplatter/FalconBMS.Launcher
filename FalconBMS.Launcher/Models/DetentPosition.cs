namespace FalconBMS.Launcher.Models;

/// <summary>
/// Falcon BMS stores throttle detents in the same 0..65535 range used by DirectInput axis values.
/// Keeping these defaults centralized preserves the old launcher's behavior: idle starts at the far left
/// of the bar and afterburner starts at the far right until the user intentionally changes them.
/// </summary>
public static class DetentPosition
{
    public const int MinAxisValue = 0;
    public const int MaxAxisValue = 65535;

    public const int DefaultIdleDetent = MinAxisValue;
    public const int DefaultAfterburnerDetent = MaxAxisValue;
}
