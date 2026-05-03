using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Provides the logical BMS axis capability table used by the binding model and axis assignment UI.
/// </summary>
public sealed class AxisDefinitionService
{
    private readonly List<DeviceAxisDefinition> _definitions = new()
    {
        Throttle("Throttle", "Throttle", "Afterward", "Forward"),

        FlightAxis("Pitch", "Pitch", "Pitch Down", "Pitch Up"),
        FlightAxis("Roll", "Roll", "Left Wing Down", "Right Wing Down"),
        FlightAxis("Yaw", "Yaw", "Yaw Left", "Yaw Right"),
        FlightAxis("Trim_Pitch", "Trim Pitch", "Pitch Down", "Pitch Up"),
        FlightAxis("Trim_Yaw", "Trim Yaw", "Yaw Left", "Yaw Right"),
        FlightAxis("Trim_Roll", "Trim Roll", "Left Wing Down", "Right Wing Down"),
        FlightAxis("Radar_Antenna_Elevation", "Radar Antenna Elevation", "Elevation Down", "Elevation Up"),
        FlightAxis("Cursor_X", "Cursor X", "Cursor Left", "Cursor Right"),
        FlightAxis("Cursor_Y", "Cursor Y", "Cursor Afterward", "Cursor Forward"),
        FlightAxis("Range_Knob", "Range Knob", "Clock Wise", "Counter CW"),

        GenericAxis("HUD_Brightness", "HUD Brightness", "Dark", "Bright"),
        GenericAxis("Comm_Ch_1", "Comm Ch 1", "Volume Down", "Volume Up"),
        GenericAxis("Comm_Ch_2", "Comm Ch 2", "Volume Down", "Volume Up"),
        GenericAxis("MSL_Volume", "MSL Volume", "Volume Down", "Volume Up"),
        GenericAxis("Threat_Volume", "Threat Volume", "Volume Down", "Volume Up"),
        GenericAxis("UHF_Volume", "UHF Volume", "Volume Down", "Volume Up"),
        GenericAxis("VHF_Volume", "VHF Volume", "Volume Down", "Volume Up")
    };

    public IReadOnlyList<DeviceAxisDefinition> GetDefinitions()
    {
        return _definitions;
    }

    public DeviceAxisDefinition? Find(string logicalAxisName)
    {
        if (string.IsNullOrWhiteSpace(logicalAxisName))
            return null;

        return _definitions.FirstOrDefault(definition =>
            string.Equals(definition.LogicalAxisName, logicalAxisName, StringComparison.OrdinalIgnoreCase));
    }

    private static DeviceAxisDefinition Throttle(
        string logicalAxisName,
        string displayName,
        string leftLabel,
        string rightLabel)
    {
        return new DeviceAxisDefinition
        {
            LogicalAxisName = logicalAxisName,
            DisplayName = displayName,
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

    private static DeviceAxisDefinition FlightAxis(
        string logicalAxisName,
        string displayName,
        string leftLabel,
        string rightLabel)
    {
        return new DeviceAxisDefinition
        {
            LogicalAxisName = logicalAxisName,
            DisplayName = displayName,
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

    private static DeviceAxisDefinition GenericAxis(
        string logicalAxisName,
        string displayName,
        string leftLabel,
        string rightLabel)
    {
        return new DeviceAxisDefinition
        {
            LogicalAxisName = logicalAxisName,
            DisplayName = displayName,
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