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
/// Axis capabilities (slot index, display name, deadzone/saturation support, etc.)
/// are defined once and are aircraft-independent — they are fixed by the sim engine.
///
/// Section placement (SectionName) is per-aircraft because the same physical control
/// may live under a different cockpit section depending on the airframe. The section
/// name must match what KeyCatalogService.CleanHeaderText produces when parsing
/// the corresponding .key file header.
///
/// To add a new aircraft: add a placement dictionary method and a case in
/// BuildPlacementMap(). Axes with no entry in the placement map are returned with
/// an empty SectionName and will appear in the fallback group in the Controls grid.
/// </summary>
public static class AxisDefinitionService
{
    // ---------------------------------------------------------------------------
    // Capabilities — aircraft-independent
    //
    // All 30 BMS logical axis slots with their fixed slot index, display name,
    // direction labels, layout kind, and supported editing controls.
    // SectionName is intentionally left empty here; it is applied per-aircraft
    // by GetDefinitions() via the placement map.
    // ---------------------------------------------------------------------------

    private static readonly List<DeviceAxisDefinition> _capabilities = new()
    {
        FlightAxis(0,  "Pitch",                   "Pitch",               "Down",            "Up"),
        FlightAxis(1,  "Roll",                    "Roll",                "Left",            "Right"),
        FlightAxis(2,  "Yaw",                     "Rudder / Yaw",        "Left",            "Right"),

        Throttle   (3,  "Throttle",               "Throttle",            "Back",       "Forward"),
        GenericAxis(4, "Throttle_Right",          "Throttle Right",      "Back",       "Forward"),

        GenericAxis(5, "Toe_Brake",               "Toe Brake",           "Release",         "Apply"),
        GenericAxis(6, "Toe_Brake_Right",         "Toe Brake Right",     "Release",         "Apply"),

        GenericAxis(7, "FOV",                     "FOV",                 "Narrow",          "Wide"),

        FlightAxis(8,  "Trim_Pitch",              "Trim Pitch",          "Down",            "Up"),
        FlightAxis(9,  "Trim_Yaw",                "Trim Yaw",            "Left",            "Right"),
        FlightAxis(10, "Trim_Roll",               "Trim Roll",           "Left",            "Right"),

        FlightAxis(11, "Radar_Antenna_Elevation", "Antenna Elevation",      "Down",           "Up"),
        FlightAxis(12, "Range_Knob",              "Range Knob",             "Clockwise",      "Counter"),
        FlightAxis(13, "Cursor_X",                "Cursor X",               "Left",           "Right"),
        FlightAxis(14, "Cursor_Y",                "Cursor Y",               "Back",           "Forward"),

        GenericAxis(15, "COMM_Channel_1",         "Audio Comm Ch1",        "Decrease",     "Increase"),
        GenericAxis(16, "COMM_Channel_2",         "Audio Comm Ch2",        "Decrease",     "Increase"),
        GenericAxis(17, "MSL_Volume",             "Missile Volume",        "Decrease",     "Increase"),
        GenericAxis(18, "Threat_Volume",          "Audio Threat Volume",   "Decrease",     "Increase"),
        GenericAxis(19, "intercom",   "Audio Intercom Volume", "Decrease",     "Increase"),
        GenericAxis(20, "AI_vs_IVC",              "Audio AI vs IVC",       "Decrease",     "Increase"),

        GenericAxis(21, "HUD_Brightness",         "HUD Brightness",         "Dark",            "Bright"),
        GenericAxis(22, "FLIR_Brightness",        "FLIR Brightness",        "Dark",            "Bright"),
        GenericAxis(23, "HMS_Brightness",         "HMS Brightness",         "Dark",            "Bright"),
        GenericAxis(24, "Reticle_Depression",     "Reticle Depr",           "Dark",            "Bright"),

        GenericAxis(25, "Camera_Distance",        "Camera Distance",        "Close",           "Far"),

        GenericAxis(26, "HSI_Course_Knob",        "HSI Course",             "Decrease",        "Increase"),
        GenericAxis(27, "HSI_Heading_Knob",       "HSI Heading",            "Decrease",        "Increase"),
        GenericAxis(28, "Altimeter_Knob",         "Altimeter Setting",      "Decrease",        "Increase"),
        GenericAxis(29, "ILS_Volume_Knob",        "Audio ILS Vol",          "Decrease",        "Increase")
    };

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the full axis capability list without any section placement applied.
    /// Use this overload when you only need slot indices, display names, or supported
    /// controls, all of which are aircraft-independent. SectionName will be empty
    /// on every returned definition.
    /// </summary>
    public static IReadOnlyList<DeviceAxisDefinition> GetDefinitions() => _capabilities;

    /// <summary>
    /// Returns the full axis definition list for the given aircraft profile, with
    /// SectionName populated from the per-aircraft placement map.
    /// Axes with no placement entry are returned with SectionName = "" so the
    /// Controls grid can collect them into a fallback group rather than losing them.
    /// </summary>
    public static IReadOnlyList<DeviceAxisDefinition> GetDefinitions(string aircraftProfile)
    {
        Dictionary<string, string> placement = BuildPlacementMap(aircraftProfile);

        return _capabilities
            .Select(axis => ApplyPlacement(axis, placement))
            .ToList();
    }

    /// <summary>
    /// Finds a single axis definition by logical axis name for the given aircraft profile.
    /// Returns null if not found.
    /// </summary>
    public static DeviceAxisDefinition? Find(string logicalAxisName)
    {
        if (string.IsNullOrWhiteSpace(logicalAxisName))
            return null;

        string canonicalName = NormalizeLogicalAxisName(logicalAxisName);

        // Search _capabilities directly — no placement needed here.
        return _capabilities.FirstOrDefault(definition =>
            string.Equals(definition.LogicalAxisName, canonicalName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds a single axis definition by logical axis name for the given aircraft profile,
    /// with SectionName populated from the per-aircraft placement map.
    /// Use this overload when you need to know where the axis appears in the Controls grid.
    /// Returns null if the axis name is not recognised.
    /// </summary>
    public static DeviceAxisDefinition? Find(string logicalAxisName, string aircraftProfile)
    {
        if (string.IsNullOrWhiteSpace(logicalAxisName))
            return null;

        string canonicalName = NormalizeLogicalAxisName(logicalAxisName);

        return GetDefinitions(aircraftProfile).FirstOrDefault(definition =>
            string.Equals(definition.LogicalAxisName, canonicalName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the BMS slot index for the given logical axis name.
    /// Slot numbers are aircraft-independent so no profile is needed.
    /// </summary>
    public static bool TryGetMappingIndex(string logicalAxisName, out int mappingIndex)
    {
        // Search capabilities directly — slot numbers never vary by aircraft.
        DeviceAxisDefinition? definition = _capabilities.FirstOrDefault(axis =>
            string.Equals(axis.LogicalAxisName,
                NormalizeLogicalAxisName(logicalAxisName),
                StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            mappingIndex = -1;
            return false;
        }

        mappingIndex = definition.MappingIndex;
        return true;
    }

    /// <summary>
    /// Normalises legacy or transitional logical axis name variants to their
    /// canonical form. Earlier builds used Comm_Ch_1 / Comm_Ch_2 while the
    /// Falcon axis table uses COMM_Channel_1 / COMM_Channel_2.
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

    // ---------------------------------------------------------------------------
    // Placement maps — per aircraft
    //
    // Each dictionary maps LogicalAxisName → SectionName.
    // SectionName must match KeyCatalogRow.SectionName exactly as produced by
    // KeyCatalogService.CleanHeaderText (= signs removed, whitespace collapsed).
    //
    // To add a new aircraft: copy one of the methods below, adjust the entries,
    // then add a case for it in BuildPlacementMap().
    // ---------------------------------------------------------------------------

    private static Dictionary<string, string> BuildPlacementMap(string aircraftProfile)
    {
        if (string.Equals(aircraftProfile, "F-15ABCD", StringComparison.OrdinalIgnoreCase))
            return F15Placement();

        // F-16 is the default for any unrecognised profile.
        return F16Placement();
    }

    // F-16 Groupings
    private static Dictionary<string, string> F16Placement() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pitch"] = "5.11 FLIGHT STICK",
        ["Roll"] = "5.11 FLIGHT STICK",

        ["Yaw"] = "6.03 KEYBOARD FLIGHT CONTROLS",
        ["Toe_Brake"] = "6.03 KEYBOARD FLIGHT CONTROLS",
        ["Toe_Brake_Right"] = "6.03 KEYBOARD FLIGHT CONTROLS",

        ["Throttle"] = "2.19 Throttle Quadrant System",
        ["Throttle_Right"] = "2.19 Throttle Quadrant System",
        ["Radar_Antenna_Elevation"] = "2.19 Throttle Quadrant System",
        ["Range_Knob"] = "2.19 Throttle Quadrant System",
        ["Cursor_X"] = "2.19 Throttle Quadrant System",
        ["Cursor_Y"] = "2.19 Throttle Quadrant System",

        ["COMM_Channel_1"] = "2.06 AUX COMM PANEL",
        ["COMM_Channel_2"] = "2.06 AUX COMM PANEL",

        ["MSL_Volume"] = "2.14 AUDIO 1 PANEL",
        ["Threat_Volume"] = "2.14 AUDIO 1 PANEL",

        ["intercom"] = "2.13 AUDIO 2 PANEL",
        ["ILS_Volume_Knob"] = "2.13 AUDIO 2 PANEL",

        ["AI_vs_IVC"] = "2.16 UHF PANEL",

        ["Trim_Pitch"] = "6.03 KEYBOARD FLIGHT CONTROLS",
        ["Trim_Roll"] = "6.03 KEYBOARD FLIGHT CONTROLS",
        ["Trim_Yaw"] = "6.03 KEYBOARD FLIGHT CONTROLS",

        ["HMS_Brightness"] = "3.03 HMCS PANEL",

        ["FLIR_Brightness"] = "4.06 ICP",
        ["Reticle_Depression"] = "4.06 ICP",

        // Note: key file spells INTRUMENT without the S, match it exactly
        ["HSI_Course_Knob"] = "4.07 MAIN INTRUMENT",
        ["HSI_Heading_Knob"] = "4.07 MAIN INTRUMENT",
        ["Altimeter_Knob"] = "4.07 MAIN INTRUMENT",

        ["HUD_Brightness"] = "5.02 HUD PANEL",

        ["FOV"] = "7.01 VIEW GENERAL CONTROL",
        ["Camera_Distance"] = "7.01 VIEW GENERAL CONTROL",
    };

    // F-15 Groupings
    private static Dictionary<string, string> F15Placement() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pitch"] = "3.18 CONTROL STICK",
        ["Roll"] = "3.18 CONTROL STICK",
        ["Trim_Pitch"] = "3.18 CONTROL STICK",
        ["Trim_Yaw"] = "3.18 CONTROL STICK",

        ["Yaw"] = "5.03 KEYBOARD FLIGHT CONTROLS",
        ["Toe_Brake"] = "5.03 KEYBOARD FLIGHT CONTROLS",
        ["Toe_Brake_Right"] = "5.03 KEYBOARD FLIGHT CONTROLS",
        ["Trim_Roll"] = "5.03 KEYBOARD FLIGHT CONTROLS",

        ["Throttle"] = "2.12 THROTTLE",
        ["Throttle_Right"] = "2.12 THROTTLE",
        ["Radar_Antenna_Elevation"] = "2.12 THROTTLE",
        ["Range_Knob"] = "2.12 THROTTLE",
        ["Cursor_X"] = "2.12 THROTTLE",
        ["Cursor_Y"] = "2.12 THROTTLE",

        ["COMM_Channel_1"] = "2.08 INTEGRATED COMMUNICATIONS CONTROL PANEL (ICCP)",
        ["COMM_Channel_2"] = "2.08 INTEGRATED COMMUNICATIONS CONTROL PANEL (ICCP)",
        ["MSL_Volume"] = "2.08 INTEGRATED COMMUNICATIONS CONTROL PANEL (ICCP)",
        ["Threat_Volume"] = "2.08 INTEGRATED COMMUNICATIONS CONTROL PANEL (ICCP)",
        ["intercom"] = "2.08 INTEGRATED COMMUNICATIONS CONTROL PANEL (ICCP)",
        ["AI_vs_IVC"] = "2.08 INTEGRATED COMMUNICATIONS CONTROL PANEL (ICCP)",

        ["ILS_Volume_Knob"] = "2.15 ILS/TCN PANEL",

        ["HUD_Brightness"] = "3.04 HUD SET PANEL",
        ["FLIR_Brightness"] = "3.04 HUD SET PANEL",
        ["HMS_Brightness"] = "3.04 HUD SET PANEL",
        ["Reticle_Depression"] = "3.04 HUD SET PANEL",

        ["HSI_Course_Knob"] = "3.12 MAIN INSTRUMENT",
        ["HSI_Heading_Knob"] = "3.12 MAIN INSTRUMENT",
        ["Altimeter_Knob"] = "3.12 MAIN INSTRUMENT",

        ["FOV"] = "6.01 VIEW GENERAL CONTROL",
        ["Camera_Distance"] = "6.01 VIEW GENERAL CONTROL",
    };

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns a new DeviceAxisDefinition with SectionName set from the placement map.
    /// If the axis has no entry in the map it is returned unchanged (SectionName stays
    /// empty and HasSectionPlacement will be false).
    /// </summary>
    private static DeviceAxisDefinition ApplyPlacement(
        DeviceAxisDefinition axis,
        Dictionary<string, string> placement)
    {
        if (!placement.TryGetValue(axis.LogicalAxisName, out string? sectionName))
            return axis;

        return new DeviceAxisDefinition
        {
            LogicalAxisName = axis.LogicalAxisName,
            DisplayName = axis.DisplayName,
            MappingIndex = axis.MappingIndex,
            LeftLabel = axis.LeftLabel,
            RightLabel = axis.RightLabel,
            LayoutKind = axis.LayoutKind,
            SupportsSaturation = axis.SupportsSaturation,
            SupportsDeadzone = axis.SupportsDeadzone,
            SupportsInvert = axis.SupportsInvert,
            SupportsAfterburnerDetent = axis.SupportsAfterburnerDetent,
            SupportsIdleDetent = axis.SupportsIdleDetent,
            SectionName = sectionName,
        };
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