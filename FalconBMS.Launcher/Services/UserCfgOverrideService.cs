using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Writes launcher-managed non-control overrides into Falcon BMS User.cfg.
/// Control/device/POV/button overrides have intentionally been removed.
/// </summary>
public sealed class UserCfgOverrideService
{
    private const string OverrideComment = "// LAUNCHER OVERRIDE";
    private const string OverrideMarker = "// LAUNCHER OVERRIDES BEGIN HERE - DO NOT EDIT OR ADD BELOW THIS LINE";
    private const string VrOverrideComment = "// SETUP OVERRIDE";
    private const string VrOverrideMarker = "// VR OVERRIDES BEGIN HERE - EDITS MUST BE MADE IN 'Falcon BMS VR.cfg' - DO NOT EDIT THIS DIRECTLY";

    public void SaveOverrides(string baseDir, bool exportRttTextures, bool vrEnabled)
    {
        DebugDiagnosticsService.Info("Ammending Falcon BMS User.cfg..");

        string configDir = Path.Combine(baseDir, "User", "Config");
        Directory.CreateDirectory(configDir);

        string userCfgPath = Path.Combine(configDir, "Falcon BMS User.cfg");
        string vrCfgPath = Path.Combine(configDir, "Falcon BMS VR.cfg");
        string preservedPrefix = LoadTextAboveLauncherOverrides(userCfgPath);

        List<string> vrOverrideLines = LoadVrOverrideLines(vrCfgPath, vrEnabled);

        using var sw = new StreamWriter(userCfgPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        sw.NewLine = "\r\n";

        if (!string.IsNullOrEmpty(preservedPrefix))
            sw.Write(preservedPrefix);

        sw.WriteLine();
        sw.WriteLine();
        sw.WriteLine(OverrideMarker);

        if (exportRttTextures)
            sw.WriteLine($"set g_bExportRTTTextures 1 {OverrideComment}");

        // VR overrides are appended after launcher overrides, at the very end of Falcon BMS User.cfg.
        if (vrOverrideLines.Count > 0)
        {
            sw.WriteLine();
            sw.WriteLine(VrOverrideMarker);

            foreach (string line in vrOverrideLines)
                sw.WriteLine($"{line} {VrOverrideComment}");
        }
    }

    private static string LoadTextAboveLauncherOverrides(string userCfgPath)
    {
        if (!File.Exists(userCfgPath))
            return string.Empty;

        string text = File.ReadAllText(userCfgPath);

        int markerIndex = text.IndexOf(OverrideMarker, StringComparison.Ordinal);
        string preserved = markerIndex >= 0 ? text[..markerIndex] : text;

        return preserved.TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Loads Falcon BMS VR.cfg as an optional input source.
    /// Blank lines are skipped. "set" lines are normalized to match original launcher behavior.
    /// </summary>
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

    /// <summary>
    /// Matches the original launcher normalization rules for cfg lines:
    /// trim "set" lines, collapse repeated spaces, and replace smart quotes with normal quotes.
    /// </summary>
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