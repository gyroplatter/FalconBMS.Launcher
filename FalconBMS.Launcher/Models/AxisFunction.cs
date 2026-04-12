namespace FalconBMS.Launcher.Models;

/// <summary>
/// Enum listing the FalconBMS axis functions the launcher can assign or edit.
/// </summary>

public enum AxisFunction
{
    Pitch,
    Roll,
    Yaw,

    Throttle,
    Throttle_Right,

    Toe_Brake,
    Toe_Brake_Right,

    Trim_Pitch,
    Trim_Yaw,
    Trim_Roll,

    Radar_Antenna_Elevation,
    Range_Knob,
    Cursor_X,
    Cursor_Y,

    COMM_Channel_1,
    COMM_Channel_2,
    MSL_Volume,
    Threat_Volume,
    IntercomVolumeVolume,
    AI_vs_IVC,

    HUD_Brightness,
    FLIR_Brightness,
    HMS_Brightness,
    Reticle_Depression,

    HSI_Course_Knob,
    HSI_Heading_Knob,
    Altimeter_Knob,
    ILS_Volume_Knob,

    FOV,
    Camera_Distance
}