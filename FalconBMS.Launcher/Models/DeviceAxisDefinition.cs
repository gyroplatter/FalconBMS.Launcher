using System.Collections.Generic;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Defines the UI/editing capabilities for one logical BMS axis.
/// This prevents every axis from incorrectly showing every possible option.
/// </summary>
public sealed class DeviceAxisDefinition
{
    public string LogicalAxisName { get; init; } = "";

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