using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Provides the complete Falcon BMS logical axis table used by:
/// - the Controls grid axis rows
/// - device JSON profile shells
/// - stock XML axis parsing
/// - axismapping.dat output
/// - axis assignment popup labels/options
///
/// Static because the axis table is fixed at compile time and shared across
/// all services. No instance state exists or is needed.
/// </summary>
public static class AxisDefinitionService
{
    private static readonly List<DeviceAxisDefinition> _definitions = new()
    {
        FlightAxis(0, "Pitch", "Pitch", "Pitch Down", "Pitch Up"),
        FlightAxis(1, "Roll", "Roll", "Left Wing Down", "Right Wing Down"),
        FlightAxis(2, "Yaw", "Rudder / Yaw", "Yaw Left", "Yaw Right"),

        Throttle(3, "Throttle", "Throttle", "Afterward", "Forward"),
        GenericAxis(4, "Throttle_Right", "Throttle Right", "Afterward", "Forward"),

        GenericAxis(5, "Toe_Brake", "Toe Brake", "Release", "Apply"),
        GenericAxis(6, "Toe_Brake_Right", "Toe Brake Right", "Release", "Apply"),

        GenericAxis(7, "FOV", "FOV", "Narrow", "Wide"),

        FlightAxis(8, "Trim_Pitch", "Trim Pitch", "Pitch Down", "Pitch Up"),
        FlightAxis(9, "Trim_Yaw", "Trim Yaw", "Yaw Left", "Yaw Right"),
        FlightAxis(10, "Trim_Roll", "Trim Roll", "Left Wing Down", "Right Wing Down"),

        FlightAxis(11, "Radar_Antenna_Elevation", "TQS Antenna Elevation", "Elevation Down", "Elevation Up"),
        FlightAxis(12, "Range_Knob", "TQS Range Knob", "Clock Wise", "Counter CW"),
        FlightAxis(13, "Cursor_X", "TQS Cursor X", "Cursor Left", "Cursor Right"),
        FlightAxis(14, "Cursor_Y", "TQS Cursor Y", "Cursor Afterward", "Cursor Forward"),

        GenericAxis(15, "COMM_Channel_1", "Audio Comm Ch1", "Volume Down", "Volume Up"),
        GenericAxis(16, "COMM_Channel_2", "Audio Comm Ch2", "Volume Down", "Volume Up"),
        GenericAxis(17, "MSL_Volume", "Audio Missile Volume", "Volume Down", "Volume Up"),
        GenericAxis(18, "Threat_Volume", "Audio Threat Volume", "Volume Down", "Volume Up"),
        GenericAxis(19, "IntercomVolumeVolume", "Audio IntercomVolumem Volume", "Volume Down", "Volume Up"),
        GenericAxis(20, "AI_vs_IVC", "Audio AI vs IVC", "Volume Down", "Volume Up"),

        GenericAxis(21, "HUD_Brightness", "ICP HUD Brightness", "Dark", "Bright"),
        GenericAxis(22, "FLIR_Brightness", "ICP FLIR Brightness", "Dark", "Bright"),
        GenericAxis(23, "HMS_Brightness", "ICP HMS Brightness", "Dark", "Bright"),
        GenericAxis(24, "Reticle_Depression", "ICP Reticle Depr", "Dark", "Bright"),

        GenericAxis(25, "Camera_Distance", "Camera Distance", "Close", "Leave"),

        GenericAxis(26, "HSI_Course_Knob", "HSI Course", "Decrease", "Increase"),
        GenericAxis(27, "HSI_Heading_Knob", "HSI Heading", "Decrease", "Increase"),
        GenericAxis(28, "Altimeter_Knob", "Altimeter Setting", "Decrease", "Increase"),
        GenericAxis(29, "ILS_Volume_Knob", "Audio ILS Vol", "Volume Down", "Volume Up")
    };

    public static IReadOnlyList<DeviceAxisDefinition> GetDefinitions() => _definitions;

    public static DeviceAxisDefinition? Find(string logicalAxisName)
    {
        if (string.IsNullOrWhiteSpace(logicalAxisName))
            return null;

        string canonicalName = NormalizeLogicalAxisName(logicalAxisName);

        return _definitions.FirstOrDefault(definition =>
            string.Equals(definition.LogicalAxisName, canonicalName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryGetMappingIndex(string logicalAxisName, out int mappingIndex)
    {
        DeviceAxisDefinition? definition = Find(logicalAxisName);

        if (definition is null)
        {
            mappingIndex = -1;
            return false;
        }

        mappingIndex = definition.MappingIndex;
        return true;
    }

    /// <summary>
    /// Keeps old/transitional JSON names readable.
    /// Earlier binding-project builds used Comm_Ch_1 / Comm_Ch_2, while the
    /// old launcher and Falcon axis table use COMM_Channel_1 / COMM_Channel_2.
    /// </summary>
    public static string NormalizeLogicalAxisName(string logicalAxisName)
    {
        if (string.IsNullOrWhiteSpace(logicalAxisName))
            return "";

        string trimmedName = logicalAxisName.Trim();

        if (string.Equals(trimmedName, "Comm_Ch_1", StringComparison.OrdinalIgnoreCase))
            return "COMM_Channel_1";

        if (string.Equals(trimmedName, "Comm_Ch_2", StringComparison.OrdinalIgnoreCase))
            return "COMM_Channel_2";

        return trimmedName;
    }

    private static DeviceAxisDefinition Throttle(int mappingIndex, string logicalAxisName, string displayName, string leftLabel, string rightLabel)
    {
        return new DeviceAxisDefinition
        {
            LogicalAxisName = logicalAxisName,
            DisplayName = displayName,
            MappingIndex = mappingIndex,
            LeftLabel = leftLabel,
            RightLabel = rightLabel,
            LayoutKind = DeviceAxisAssignmentLayoutKind.Throttle,
            SupportsSaturation = true,
            SupportsDeadzone = false,
            SupportsInvert = true,
            SupportsAfterburnerDetent = true,
            SupportsIdleDetent = true
        };
    }

    private static DeviceAxisDefinition FlightAxis(int mappingIndex, string logicalAxisName, string displayName, string leftLabel, string rightLabel)
    {
        return new DeviceAxisDefinition
        {
            LogicalAxisName = logicalAxisName,
            DisplayName = displayName,
            MappingIndex = mappingIndex,
            LeftLabel = leftLabel,
            RightLabel = rightLabel,
            LayoutKind = DeviceAxisAssignmentLayoutKind.Flight,
            SupportsSaturation = true,
            SupportsDeadzone = true,
            SupportsInvert = true,
            SupportsAfterburnerDetent = false,
            SupportsIdleDetent = false
        };
    }

    private static DeviceAxisDefinition GenericAxis(int mappingIndex, string logicalAxisName, string displayName, string leftLabel, string rightLabel)
    {
        return new DeviceAxisDefinition
        {
            LogicalAxisName = logicalAxisName,
            DisplayName = displayName,
            MappingIndex = mappingIndex,
            LeftLabel = leftLabel,
            RightLabel = rightLabel,
            LayoutKind = DeviceAxisAssignmentLayoutKind.Generic,
            SupportsSaturation = true,
            SupportsDeadzone = false,
            SupportsInvert = true,
            SupportsAfterburnerDetent = false,
            SupportsIdleDetent = false
        };
    }
}