using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Provides the logical BMS axis capability table used by the binding model and future UI.
/// This centralizes which axes support saturation, deadzone, invert, and detent controls.
/// </summary>
public sealed class AxisDefinitionService
{
    private readonly List<DeviceAxisDefinition> _definitions = new()
    {
        Throttle("Throttle"),

        DeadzoneAxis("Pitch"),
        DeadzoneAxis("Roll"),
        DeadzoneAxis("Yaw"),
        DeadzoneAxis("Trim_Pitch"),
        DeadzoneAxis("Trim_Yaw"),
        DeadzoneAxis("Trim_Roll"),
        DeadzoneAxis("Radar_Antenna_Elevation"),
        DeadzoneAxis("Cursor_X"),
        DeadzoneAxis("Cursor_Y"),
        DeadzoneAxis("Range_Knob"),

        SimpleAxis("HUD_Brightness"),
        SimpleAxis("Comm_Ch_1"),
        SimpleAxis("Comm_Ch_2"),
        SimpleAxis("MSL_Volume"),
        SimpleAxis("Threat_Volume"),
        SimpleAxis("UHF_Volume"),
        SimpleAxis("VHF_Volume")
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

    private static DeviceAxisDefinition Throttle(string logicalAxisName)
    {
        return new DeviceAxisDefinition
        {
            LogicalAxisName = logicalAxisName,
            SupportsSaturation = true,
            SupportsDeadzone = false,
            SupportsInvert = false,
            SupportsAfterburnerDetent = true,
            SupportsIdleDetent = true
        };
    }

    private static DeviceAxisDefinition DeadzoneAxis(string logicalAxisName)
    {
        return new DeviceAxisDefinition
        {
            LogicalAxisName = logicalAxisName,
            SupportsSaturation = true,
            SupportsDeadzone = true,
            SupportsInvert = true,
            SupportsAfterburnerDetent = false,
            SupportsIdleDetent = false
        };
    }

    private static DeviceAxisDefinition SimpleAxis(string logicalAxisName)
    {
        return new DeviceAxisDefinition
        {
            LogicalAxisName = logicalAxisName,
            SupportsSaturation = true,
            SupportsDeadzone = false,
            SupportsInvert = false,
            SupportsAfterburnerDetent = false,
            SupportsIdleDetent = false
        };
    }
}