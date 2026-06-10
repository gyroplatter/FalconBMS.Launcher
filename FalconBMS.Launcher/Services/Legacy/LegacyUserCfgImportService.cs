using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FalconBMS.Launcher.Services.Legacy;

public sealed class LegacyUserCfgImportService
{
    private static readonly Regex SetLineRegex = new(
        @"^\s*set\s+(?<name>\S+)\s+(?<value>-?\d+)(?:\s+.*)?$",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase);

    private static readonly IReadOnlyDictionary<string, string>
        CurveSettings =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["g_nAxisExp_AXIS_CURSOR_Y"] = "Cursor_Y",
                ["g_nAxisExp_AXIS_CURSOR_X"] = "Cursor_X",
                ["g_nAxisExp_AXIS_ROLL"] = "Roll",
                ["g_nAxisExp_AXIS_PITCH"] = "Pitch",
                ["g_nAxisExp_AXIS_YAW"] = "Yaw"
            };

    public LegacyUserCfgImportResult Read(
        string userCfgPath)
    {
        var result = new LegacyUserCfgImportResult();

        if (string.IsNullOrWhiteSpace(userCfgPath) ||
            !File.Exists(userCfgPath))
        {
            return result;
        }

        foreach (string rawLine in File.ReadLines(userCfgPath))
        {
            Match match = SetLineRegex.Match(rawLine);

            if (!match.Success)
                continue;

            string settingName =
                match.Groups["name"].Value;

            if (!int.TryParse(
                    match.Groups["value"].Value,
                    out int settingValue))
            {
                continue;
            }

            if (CurveSettings.TryGetValue(
                    settingName,
                    out string? logicalAxisName))
            {
                result.AxisCurves[logicalAxisName] =
                    Math.Max(1, settingValue);

                continue;
            }

            if (string.Equals(
                    settingName,
                    "g_bExportRTTTextures",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.ExportRttTexturesFound = true;
                result.ExportRttTextures =
                    settingValue != 0;
            }
        }

        return result;
    }

    public void ApplyCurves(
        BindingModel bindingModel,
        IReadOnlyDictionary<string, int> curves)
    {
        foreach (KeyValuePair<string, int> curve in curves)
        {
            DeviceAxisBinding? axisBinding =
                bindingModel.DeviceProfiles
                    .SelectMany(profile => profile.AxisBindings)
                    .FirstOrDefault(axis =>
                        axis.PhysicalAxisIndex.HasValue &&
                        string.Equals(
                            axis.LogicalAxisName,
                            curve.Key,
                            StringComparison.OrdinalIgnoreCase));

            if (axisBinding is null)
                continue;

            axisBinding.Curve =
                Math.Max(1, curve.Value);
        }
    }
}

public sealed class LegacyUserCfgImportResult
{
    public Dictionary<string, int> AxisCurves { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool ExportRttTexturesFound { get; set; }

    public bool ExportRttTextures { get; set; }
}