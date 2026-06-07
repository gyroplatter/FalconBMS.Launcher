using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services.Legacy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Writes launcher-managed overrides into Falcon BMS User.cfg.
/// </summary>
public sealed class UserCfgOverrideService
{
    private const string OverrideComment = "// LAUNCHER OVERRIDE";
    private const string OverrideMarker = "// LAUNCHER OVERRIDES BEGIN HERE - DO NOT EDIT OR ADD BELOW THIS LINE";
    private const string VrOverrideComment = "// SETUP OVERRIDE";
    private const string VrOverrideMarker = "// VR OVERRIDES BEGIN HERE - EDITS MUST BE MADE IN 'Falcon BMS VR.cfg' - DO NOT EDIT THIS DIRECTLY";

    public void SaveOverrides(
        string baseDir,
        IReadOnlyList<DeviceBindingProfile> deviceProfiles,
        bool exportRttTextures,
        bool vrEnabled)
    {
        DebugDiagnosticsService.Info("Ammending Falcon BMS User.cfg..");

        string configDir = Path.Combine(baseDir, "User", "Config");
        Directory.CreateDirectory(configDir);

        string userCfgPath = Path.Combine(configDir, "Falcon BMS User.cfg");
        string vrCfgPath = Path.Combine(configDir, "Falcon BMS VR.cfg");
        string preservedPrefix = LoadTextAboveLauncherOverrides(userCfgPath);

        int deviceCount = deviceProfiles.Count;
        int pinkyMagnitude = deviceCount * 128;

        int rollSlot = FindDeviceSlotForLogicalAxis(deviceProfiles, "Roll");
        int throttleSlot = FindDeviceSlotForLogicalAxis(deviceProfiles, "Throttle");

        if (rollSlot < 0 || rollSlot >= deviceCount)
            rollSlot = 0;

        bool sameDeviceOrNoThrottle = throttleSlot < 0 || throttleSlot >= deviceCount || throttleSlot == rollSlot;

        int pov1DeviceId = rollSlot + 2;
        int pov2DeviceId = sameDeviceOrNoThrottle ? rollSlot + 2 : throttleSlot + 2;
        int pov2Id = sameDeviceOrNoThrottle ? 1 : 0;

        List<string> vrOverrideLines = LoadVrOverrideLines(vrCfgPath, vrEnabled);

        using var sw = new StreamWriter(userCfgPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        sw.NewLine = "\r\n";

        if (!string.IsNullOrEmpty(preservedPrefix))
            sw.Write(preservedPrefix);

        sw.WriteLine();
        sw.WriteLine();
        sw.WriteLine(OverrideMarker);

        sw.WriteLine($"set g_nButtonsPerDevice 128 {OverrideComment}");
        sw.WriteLine($"set g_nHotasPinkyShiftMagnitude {pinkyMagnitude} {OverrideComment}");
        sw.WriteLine($"set g_nNumOfPOVs 2 {OverrideComment}");
        sw.WriteLine($"set g_nPOV1DeviceID {pov1DeviceId} {OverrideComment}");
        sw.WriteLine($"set g_nPOV1ID 0 {OverrideComment}");
        sw.WriteLine($"set g_nPOV2DeviceID {pov2DeviceId} {OverrideComment}");
        sw.WriteLine($"set g_nPOV2ID {pov2Id} {OverrideComment}");

        LegacyAxisCurveUserCfgWriterService.WriteOverrides(
            sw,
            deviceProfiles,
            OverrideComment);

        if (exportRttTextures)
            sw.WriteLine($"set g_bExportRTTTextures 1 {OverrideComment}");

        if (vrOverrideLines.Count > 0)
        {
            sw.WriteLine();
            sw.WriteLine(VrOverrideMarker);

            foreach (string line in vrOverrideLines)
                sw.WriteLine($"{line} {VrOverrideComment}");
        }
    }

    private static int FindDeviceSlotForLogicalAxis(
        IReadOnlyList<DeviceBindingProfile> deviceProfiles,
        string logicalAxisName)
    {
        for (int i = 0; i < deviceProfiles.Count; i++)
        {
            bool hasAxis = deviceProfiles[i].AxisBindings.Any(axis =>
                string.Equals(axis.LogicalAxisName, logicalAxisName, StringComparison.OrdinalIgnoreCase) &&
                axis.PhysicalAxisIndex.HasValue);

            if (hasAxis)
                return i;
        }

        return -1;
    }

    // New header message
    private const string UserHeaderBlock =
    "///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////\r\n" +
    "// Add custom configuration overrides here. These settings will take precedence over Falcon BMS.cfg. // \r\n" +
    "// Do not edit Falcon BMS.cfg directly, use this file for all user-specific changes. // \r\n" +
    "///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////";

    // Legacy header from original launcher (used for migration/cleanup)
    private const string OldUserHeaderBlock =
    "///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////\r\n" +
    "// User can place here his or her specific configurations lines that will superseed the main ones located in the Falcon BMS.cfg file //\r\n" +
    "///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////";

    private static string LoadTextAboveLauncherOverrides(string userCfgPath)
    {
        if (!File.Exists(userCfgPath))
        {
            // First run - just return header
            return UserHeaderBlock;
        }

        string text = File.ReadAllText(userCfgPath);

        int markerIndex = text.IndexOf(OverrideMarker, StringComparison.Ordinal);
        string preserved = markerIndex >= 0 ? text[..markerIndex] : text;

        preserved = preserved.TrimEnd('\r', '\n');

        // Remove legacy header if present
        preserved = RemoveOldHeaderBlock(preserved);

        // Ensure new header exists at top (only once)
        if (!preserved.StartsWith(UserHeaderBlock, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(preserved))
                return UserHeaderBlock;

            return UserHeaderBlock + "\r\n\r\n" + preserved;
        }

        return preserved;
    }

    private static string RemoveOldHeaderBlock(string preserved)
    {
        string trimmed = preserved.TrimStart('\r', '\n');

        if (trimmed.StartsWith(OldUserHeaderBlock, StringComparison.Ordinal))
        {
            return trimmed
                .Substring(OldUserHeaderBlock.Length)
                .TrimStart('\r', '\n');
        }

        return preserved;
    }

    private static List<string> LoadVrOverrideLines(string vrCfgPath, bool vrEnabled)
    {
        List<string> lines = new();

        if (!vrEnabled || !File.Exists(vrCfgPath))
            return lines;

        using StreamReader reader = new(vrCfgPath, Encoding.UTF8);

        while (true)
        {
            string? line = reader.ReadLine();
            if (line is null)
                break;

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            lines.Add(NormalizeCfgLine(line));
        }

        return lines;
    }

    private static string NormalizeCfgLine(string line)
    {
        string trimmed = line.Trim();

        if (!trimmed.StartsWith("set", StringComparison.Ordinal))
            return line;

        line = trimmed;

        while (line.Contains("  "))
            line = line.Replace("  ", " ");

        while (line.Contains("\x201C") || line.Contains("\x201D"))
            line = line.Replace("\x201C", "\x0022")
                       .Replace("\x201D", "\x0022");

        return line;
    }
}