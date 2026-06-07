using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FalconBMS.Launcher.Services.Legacy;

/// <summary>
/// Writes legacy Falcon BMS exponential-axis settings to User.cfg.
///
/// These values are only needed while Falcon BMS requires axis curves
/// to be stored as User.cfg settings rather than in device binding files.
/// </summary>
public static class LegacyAxisCurveUserCfgWriterService
{
    public static void WriteOverrides(
        StreamWriter writer,
        IReadOnlyList<DeviceBindingProfile> deviceProfiles,
        string overrideComment)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        if (deviceProfiles is null)
            throw new ArgumentNullException(nameof(deviceProfiles));

        WriteAxisCurveOverride(
            writer,
            deviceProfiles,
            "Cursor_Y",
            "g_nAxisExp_AXIS_CURSOR_Y",
            overrideComment);

        WriteAxisCurveOverride(
            writer,
            deviceProfiles,
            "Cursor_X",
            "g_nAxisExp_AXIS_CURSOR_X",
            overrideComment);

        WriteAxisCurveOverride(
            writer,
            deviceProfiles,
            "Roll",
            "g_nAxisExp_AXIS_ROLL",
            overrideComment);

        WriteAxisCurveOverride(
            writer,
            deviceProfiles,
            "Pitch",
            "g_nAxisExp_AXIS_PITCH",
            overrideComment);

        WriteAxisCurveOverride(
            writer,
            deviceProfiles,
            "Yaw",
            "g_nAxisExp_AXIS_YAW",
            overrideComment);
    }

    private static void WriteAxisCurveOverride(
        StreamWriter writer,
        IReadOnlyList<DeviceBindingProfile> deviceProfiles,
        string logicalAxisName,
        string cfgSettingName,
        string overrideComment)
    {
        int curveValue = FindAxisCurveValue(
            deviceProfiles,
            logicalAxisName);

        // Curve 1 is the Falcon BMS default linear response.
        // Do not add to User.cfg if default.
        if (curveValue <= 1)
            return;

        writer.WriteLine(
            $"set {cfgSettingName} {curveValue} {overrideComment}");
    }

    private static int FindAxisCurveValue(
        IReadOnlyList<DeviceBindingProfile> deviceProfiles,
        string logicalAxisName)
    {
        foreach (DeviceBindingProfile deviceProfile in deviceProfiles)
        {
            DeviceAxisBinding? binding =
                deviceProfile.AxisBindings.FirstOrDefault(axis =>
                    string.Equals(
                        axis.LogicalAxisName,
                        logicalAxisName,
                        StringComparison.OrdinalIgnoreCase) &&
                    axis.PhysicalAxisIndex.HasValue);

            if (binding is null)
                continue;

            // BMS requires x to be greater than zero.
            // x = 1 produces a linear response with no curve.
            return Math.Max(1, binding.Curve);
        }

        return 1;
    }
}