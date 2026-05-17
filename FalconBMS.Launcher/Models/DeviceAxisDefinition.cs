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

    /// <summary>
    /// The section this axis belongs to in the Controls grid, e.g. "2.19 Throttle
    /// Quadrant System". Must match KeyCatalogRow.SectionName exactly as produced
    /// by KeyCatalogService.CleanHeaderText (= signs stripped, whitespace collapsed).
    /// Empty string means no placement is defined for this axis in the current
    /// aircraft profile — it will appear in the fallback group at the bottom.
    /// </summary>
    public string SectionName { get; init; } = "";

    /// <summary>
    /// True when this axis has a valid section placement defined for the current
    /// aircraft profile. Axes without placement fall back to the bottom
    /// of the Controls grid so they always display.
    /// </summary>
    public bool HasSectionPlacement => !string.IsNullOrWhiteSpace(SectionName);

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