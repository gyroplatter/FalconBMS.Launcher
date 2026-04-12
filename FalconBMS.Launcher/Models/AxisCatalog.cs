namespace FalconBMS.Launcher.Models;

/// <summary>
/// Central lookup table for FalconBMS axis functions, labels, and metadata used across the controls/audio UI.
/// </summary>

public sealed record AxisActionDef(
    AxisFunction Function,
    string DisplayName,
    int MappingIndex,
    string? LeftLabel = null,
    string? RightLabel = null
);

public static class AxisCatalog
{
    // Stock Falcon BMS axis mapping indices (0..29)
    public static readonly AxisActionDef[] All =
    [
        new(AxisFunction.Pitch, "Pitch", 0, "Pitch Down", "Pitch Up"),
        new(AxisFunction.Roll, "Roll", 1, "Left Wing Down", "Right Wing Down"),
        new(AxisFunction.Yaw, "Rudder / Yaw", 2, "Yaw Left", "Yaw Right"),
        new(AxisFunction.Throttle, "Throttle", 3, "Afterward", "Forward"),
        new(AxisFunction.Throttle_Right, "Throttle Right", 4, "Afterward", "Forward"),

        new(AxisFunction.Toe_Brake, "Toe Brake", 5, "Release", "Apply"),
        new(AxisFunction.Toe_Brake_Right, "Toe Brake Right", 6, "Release", "Apply"),

        new(AxisFunction.FOV, "FOV", 7, "Narrow", "Wide"),

        new(AxisFunction.Trim_Pitch, "Trim Pitch", 8, "Pitch Down", "Pitch Up"),
        new(AxisFunction.Trim_Yaw, "Trim Yaw", 9, "Yaw Left", "Yaw Right"),
        new(AxisFunction.Trim_Roll, "Trim Roll", 10, "Left Wing Down", "Right Wing Down"),

        new(AxisFunction.Radar_Antenna_Elevation, "TQS Antenna Elevation", 11, "Elevation Down", "Elevation Up"),
        new(AxisFunction.Range_Knob, "TQS Range Knob", 12, "Clock Wise", "Counter CW"),
        new(AxisFunction.Cursor_X, "TQS Cursor X", 13, "Cursor Left", "Cursor Right"),
        new(AxisFunction.Cursor_Y, "TQS Cursor Y", 14, "Cursor Afterward", "Cursor Forward"),

        new(AxisFunction.COMM_Channel_1, "Audio Comm Ch1", 15, "Volume Down", "Volume Up"),
        new(AxisFunction.COMM_Channel_2, "Audio Comm Ch2", 16, "Volume Down", "Volume Up"),
        new(AxisFunction.MSL_Volume, "Audio Missile Volume", 17, "Volume Down", "Volume Up"),
        new(AxisFunction.Threat_Volume, "Audio Threat Volume", 18, "Volume Down", "Volume Up"),
        new(AxisFunction.IntercomVolumeVolume, "Audio IntercomVolumem Volume", 19, "Volume Down", "Volume Up"),
        new(AxisFunction.AI_vs_IVC, "Audio AI vs IVC", 20, "Volume Down", "Volume Up"),

        new(AxisFunction.HUD_Brightness, "ICP HUD Brightness", 21, "Dark", "Bright"),
        new(AxisFunction.FLIR_Brightness, "ICP FLIR Brightness", 22, "Dark", "Bright"),
        new(AxisFunction.HMS_Brightness, "ICP HMS Brightness", 23, "Dark", "Bright"),
        new(AxisFunction.Reticle_Depression, "ICP Reticle Depr", 24, "Dark", "Bright"),

        new(AxisFunction.Camera_Distance, "Camera Distance", 25, "Close", "Leave"),

        new(AxisFunction.HSI_Course_Knob, "HSI Course", 26, "Decrease", "Increase"),
        new(AxisFunction.HSI_Heading_Knob, "HSI Heading", 27, "Decrease", "Increase"),
        new(AxisFunction.Altimeter_Knob, "Altimeter Setting", 28, "Decrease", "Increase"),
        new(AxisFunction.ILS_Volume_Knob, "Audio ILS Vol", 29, "Volume Down", "Volume Up"),
    ];

    public static AxisActionDef Get(AxisFunction f) => All.First(a => a.Function == f);
}