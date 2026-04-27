using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Loads and parses BMS "Full" key files into structured in-memory catalogs.
/// 
/// This service reads BMS - Full*.key files from the selected install and
/// produces ordered KeyCatalog objects that preserve the original file layout.
/// 
/// Responsibilities:
/// - locate and load key files for each aircraft profile
/// - parse rows including headers, callbacks, and metadata
/// - classify rows based on visibility and formatting rules
/// - maintain original ordering for UI reconstruction
/// 
/// This service does not modify files, generate bindings, or perform any
/// user-specific logic. It is strictly a read-only catalog loader.
/// </summary>
public sealed class KeyCatalogService
{
    private static readonly Regex KeyRowRegex = new Regex(
        @"^(?<callback>\S+)\s+" +
        @"(?<sound>-?\d+)\s+" +
        @"(?<unused>-?\d+)\s+" +
        @"(?<keyScancode>\S+)\s+" +
        @"(?<keyModifierFlags>\S+)\s+" +
        @"(?<chordScancode>\S+)\s+" +
        @"(?<chordModifierFlags>\S+)\s+" +
        @"(?<visibility>-?\d+)\s+" +
        @"""(?<description>.*)""\s*$",
        RegexOptions.Compiled);

    public IReadOnlyList<KeyCatalog> LoadForInstall(string baseDir)
    {
        var catalogs = new List<KeyCatalog>();

        if (string.IsNullOrWhiteSpace(baseDir))
            return catalogs;

        string configDir = Path.Combine(baseDir, "User", "Config");

        if (!Directory.Exists(configDir))
        {
            DebugDiagnosticsService.Warn($"KEY catalog load skipped. Config folder not found: {configDir}");
            return catalogs;
        }

        var keyFiles = Directory
            .GetFiles(configDir, "BMS - Full*.key", SearchOption.TopDirectoryOnly)
            .OrderBy(GetSortKey)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (keyFiles.Count == 0)
        {
            DebugDiagnosticsService.Warn($"KEY catalog load found no BMS - Full*.key files in: {configDir}");
            return catalogs;
        }

        foreach (string keyFile in keyFiles)
        {
            try
            {
                var catalog = LoadFile(keyFile);
                catalogs.Add(catalog);

                DebugDiagnosticsService.Info(
                    $"KEY catalog loaded | Aircraft={catalog.AircraftProfile} | " +
                    $"Rows={catalog.ParsedRowCount} | VisibleRows={catalog.VisibleGridRowCount} | " +
                    $"Callbacks={catalog.CallbackRowCount} | Editable={catalog.EditableCallbackCount} | " +
                    $"Locked={catalog.LockedCallbackCount} | Hidden={catalog.HiddenCallbackCount} | " +
                    $"Categories={catalog.CategoryHeaderCount} | Sections={catalog.SectionHeaderCount} | " +
                    $"Remarks={catalog.RemarkCount} | SkippedLines={catalog.SkippedLineCount} | " +
                    $"Path={catalog.SourcePath}");
            }
            catch (Exception ex)
            {
                DebugDiagnosticsService.Exception(ex, $"KEY catalog load failed: {keyFile}");
            }
        }

        return catalogs;
    }

    private static KeyCatalog LoadFile(string path)
    {
        var catalog = new KeyCatalog
        {
            AircraftProfile = GetAircraftProfile(path),
            SourcePath = path
        };

        string currentCategory = "";
        string currentSection = "";

        int lineNumber = 0;

        foreach (string rawLine in File.ReadLines(path))
        {
            lineNumber++;
            catalog.TotalLineCount++;

            string trimmed = rawLine.Trim();

            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;

            var match = KeyRowRegex.Match(trimmed);
            if (!match.Success)
                continue;

            string callbackName = match.Groups["callback"].Value;
            int soundId = ParseNumber(match.Groups["sound"].Value);
            int unused = ParseNumber(match.Groups["unused"].Value);
            string keyScancode = match.Groups["keyScancode"].Value;
            int keyModifierFlags = ParseNumber(match.Groups["keyModifierFlags"].Value);
            string chordScancode = match.Groups["chordScancode"].Value;
            int chordModifierFlags = ParseNumber(match.Groups["chordModifierFlags"].Value);
            int visibility = ParseNumber(match.Groups["visibility"].Value);
            string description = match.Groups["description"].Value.Trim();

            KeyCatalogRowKind rowKind = DetermineRowKind(visibility, description);

            if (rowKind == KeyCatalogRowKind.CategoryHeader)
            {
                currentCategory = description;
                currentSection = "";
            }
            else if (rowKind == KeyCatalogRowKind.SectionHeader)
            {
                currentSection = CleanHeaderText(description);
            }

            catalog.Rows.Add(new KeyCatalogRow
            {
                LineNumber = lineNumber,
                RawLine = rawLine,
                RowKind = rowKind,
                CallbackName = callbackName,
                SoundId = soundId,
                Unused = unused,
                KeyScancode = keyScancode,
                KeyModifierFlags = keyModifierFlags,
                ChordScancode = chordScancode,
                ChordModifierFlags = chordModifierFlags,
                Visibility = visibility,
                Description = description,
                CategoryName = currentCategory,
                SectionName = currentSection
            });
        }

        return catalog;
    }

    private static KeyCatalogRowKind DetermineRowKind(int visibility, string description)
    {
        if (visibility == -1)
        {
            if (description.StartsWith("========", StringComparison.Ordinal))
                return KeyCatalogRowKind.SectionHeader;

            return KeyCatalogRowKind.CategoryHeader;
        }

        if (description.StartsWith("REM:", StringComparison.OrdinalIgnoreCase))
            return KeyCatalogRowKind.Remark;

        if (visibility == 1)
            return KeyCatalogRowKind.EditableCallback;

        if (visibility == 0)
            return KeyCatalogRowKind.LockedCallback;

        if (visibility == -2)
            return KeyCatalogRowKind.HiddenCallback;

        return KeyCatalogRowKind.Other;
    }

    private static string CleanHeaderText(string text)
    {
        string cleaned = text.Replace("=", " ").Trim();

        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");

        return cleaned;
    }

    private static int ParseNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        value = value.Trim();

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt32(value, 16);

        return int.TryParse(value, out int result) ? result : 0;
    }

    private static string GetAircraftProfile(string path)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);

        if (fileName.IndexOf("F15ABCD", StringComparison.OrdinalIgnoreCase) >= 0)
            return "F-15ABCD";

        return "F-16";
    }

    private static int GetSortKey(string path)
    {
        return string.Equals(GetAircraftProfile(path), "F-16", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;
    }
}