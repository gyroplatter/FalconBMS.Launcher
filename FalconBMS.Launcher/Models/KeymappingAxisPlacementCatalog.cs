using FalconBMS.Launcher.Models;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Models.Keymapping;

/// <summary>
/// Profile-specific placement map for showing axis rows under Falcon key file section headers.
/// AxisCatalog remains the source of axis definitions; this file only controls placement.
/// </summary>
public static class KeymappingAxisPlacementCatalog
{
    public sealed record AxisPlacement(KeyProfile Profile, AxisFunction Function, string SectionId);

    private static readonly AxisPlacement[] Placements =
    [
        // F16
        new(KeyProfile.F16, AxisFunction.Pitch, "5.11"),
        new(KeyProfile.F16, AxisFunction.Roll, "5.11"),
        new(KeyProfile.F16, AxisFunction.Yaw, "6.03"),
        new(KeyProfile.F16, AxisFunction.Throttle, "2.19"),
        new(KeyProfile.F16, AxisFunction.Throttle_Right, "2.19"),
        new(KeyProfile.F16, AxisFunction.Toe_Brake, "6.03"),
        new(KeyProfile.F16, AxisFunction.Toe_Brake_Right, "6.03"),
        new(KeyProfile.F16, AxisFunction.FOV, "7.01"),
        new(KeyProfile.F16, AxisFunction.Trim_Pitch, "2.04"),
        new(KeyProfile.F16, AxisFunction.Trim_Yaw, "2.04"),
        new(KeyProfile.F16, AxisFunction.Trim_Roll, "2.04"),
        new(KeyProfile.F16, AxisFunction.Radar_Antenna_Elevation, "2.19"),
        new(KeyProfile.F16, AxisFunction.Range_Knob, "2.19"),
        new(KeyProfile.F16, AxisFunction.Cursor_X, "2.19"),
        new(KeyProfile.F16, AxisFunction.Cursor_Y, "2.19"),
        new(KeyProfile.F16, AxisFunction.COMM_Channel_1, "2.14"),
        new(KeyProfile.F16, AxisFunction.COMM_Channel_2, "2.13"),
        new(KeyProfile.F16, AxisFunction.MSL_Volume, "2.14"),
        new(KeyProfile.F16, AxisFunction.Threat_Volume, "2.14"),
        new(KeyProfile.F16, AxisFunction.IntercomVolumeVolume, "2.14"),
        new(KeyProfile.F16, AxisFunction.AI_vs_IVC, "2.14"),
        new(KeyProfile.F16, AxisFunction.HUD_Brightness, "5.02"),
        new(KeyProfile.F16, AxisFunction.FLIR_Brightness, "4.06"),
        new(KeyProfile.F16, AxisFunction.HMS_Brightness, "4.06"),
        new(KeyProfile.F16, AxisFunction.Reticle_Depression, "4.06"),
        new(KeyProfile.F16, AxisFunction.Camera_Distance, "7.01"),
        new(KeyProfile.F16, AxisFunction.HSI_Course_Knob, "4.07"),
        new(KeyProfile.F16, AxisFunction.HSI_Heading_Knob, "4.07"),
        new(KeyProfile.F16, AxisFunction.Altimeter_Knob, "4.07"),
        new(KeyProfile.F16, AxisFunction.ILS_Volume_Knob, "2.16"),

        // F15ABCD
        new(KeyProfile.F15ABCD, AxisFunction.Pitch, "3.18"),
        new(KeyProfile.F15ABCD, AxisFunction.Roll, "3.18"),
        new(KeyProfile.F15ABCD, AxisFunction.Yaw, "5.03"),
        new(KeyProfile.F15ABCD, AxisFunction.Throttle, "2.12"),
        new(KeyProfile.F15ABCD, AxisFunction.Throttle_Right, "2.12"),
        new(KeyProfile.F15ABCD, AxisFunction.Toe_Brake, "5.03"),
        new(KeyProfile.F15ABCD, AxisFunction.Toe_Brake_Right, "5.03"),
        new(KeyProfile.F15ABCD, AxisFunction.FOV, "6.01"),
        new(KeyProfile.F15ABCD, AxisFunction.Trim_Pitch, "5.03"),
        new(KeyProfile.F15ABCD, AxisFunction.Trim_Yaw, "5.03"),
        new(KeyProfile.F15ABCD, AxisFunction.Trim_Roll, "5.03"),
        new(KeyProfile.F15ABCD, AxisFunction.Radar_Antenna_Elevation, "2.11"),
        new(KeyProfile.F15ABCD, AxisFunction.Range_Knob, "2.11"),
        new(KeyProfile.F15ABCD, AxisFunction.Cursor_X, "2.11"),
        new(KeyProfile.F15ABCD, AxisFunction.Cursor_Y, "2.11"),
        new(KeyProfile.F15ABCD, AxisFunction.COMM_Channel_1, "3.03"),
        new(KeyProfile.F15ABCD, AxisFunction.COMM_Channel_2, "2.08"),
        new(KeyProfile.F15ABCD, AxisFunction.MSL_Volume, "2.05"),
        new(KeyProfile.F15ABCD, AxisFunction.Threat_Volume, "2.09"),
        new(KeyProfile.F15ABCD, AxisFunction.IntercomVolumeVolume, "2.05"),
        new(KeyProfile.F15ABCD, AxisFunction.AI_vs_IVC, "2.05"),
        new(KeyProfile.F15ABCD, AxisFunction.HUD_Brightness, "3.04"),
        new(KeyProfile.F15ABCD, AxisFunction.FLIR_Brightness, "3.04"),
        new(KeyProfile.F15ABCD, AxisFunction.HMS_Brightness, "3.04"),
        new(KeyProfile.F15ABCD, AxisFunction.Reticle_Depression, "3.04"),
        new(KeyProfile.F15ABCD, AxisFunction.Camera_Distance, "6.01"),
        new(KeyProfile.F15ABCD, AxisFunction.HSI_Course_Knob, "3.12"),
        new(KeyProfile.F15ABCD, AxisFunction.HSI_Heading_Knob, "3.12"),
        new(KeyProfile.F15ABCD, AxisFunction.Altimeter_Knob, "3.12"),
        new(KeyProfile.F15ABCD, AxisFunction.ILS_Volume_Knob, "2.15"),
    ];

    public static IReadOnlyList<AxisPlacement> GetPlacements(KeyProfile profile)
    {
        return Placements
            .Where(x => x.Profile == profile)
            .OrderBy(x => AxisCatalog.Get(x.Function).MappingIndex)
            .ToArray();
    }
}