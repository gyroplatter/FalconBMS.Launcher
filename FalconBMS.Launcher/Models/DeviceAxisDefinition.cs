using System.Collections.Generic;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Defines one logical Falcon BMS axis row and which editing controls it supports.
/// 
/// MappingIndex is the Falcon-native axismapping.dat slot number.
/// Falcon BMS uses 30 logical axis slots, indexed 0..29.
/// </summary>
public sealed class DeviceAxisDefinition
{
    public string LogicalAxisName { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public int MappingIndex { get; init; }

    public string LeftLabel { get; init; } = "";

    public string RightLabel { get; init; } = "";

    public DeviceAxisAssignmentLayoutKind LayoutKind { get; init; }

    public bool SupportsSaturation { get; init; }

    public bool SupportsDeadzone { get; init; }

    public bool SupportsInvert { get; init; }

    public bool SupportsAfterburnerDetent { get; init; }

    public bool SupportsIdleDetent { get; init; }

    public IReadOnlyList<DeviceAxisControlKind> SupportedControls
    {
        get
        {
            var controls = new List<DeviceAxisControlKind>
            {
                DeviceAxisControlKind.PhysicalAxis
            };

            if (SupportsSaturation)
                controls.Add(DeviceAxisControlKind.Saturation);

            if (SupportsDeadzone)
                controls.Add(DeviceAxisControlKind.Deadzone);

            if (SupportsInvert)
                controls.Add(DeviceAxisControlKind.Invert);

            if (SupportsAfterburnerDetent)
                controls.Add(DeviceAxisControlKind.AfterburnerDetent);

            if (SupportsIdleDetent)
                controls.Add(DeviceAxisControlKind.IdleDetent);

            return controls;
        }
    }
}