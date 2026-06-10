using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FalconBMS.Launcher.Services.Legacy;

public sealed class LegacyAutoKeyImportService
{
    private static readonly Regex KeyRowRegex = new(
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

    public LegacyAutoKeyImportResult Apply(
        string autoKeyPath,
        string aircraftProfileName,
        BindingModel bindingModel)
    {
        var result = new LegacyAutoKeyImportResult();

        if (string.IsNullOrWhiteSpace(autoKeyPath) ||
            !File.Exists(autoKeyPath))
        {
            return result;
        }

        BindingAircraftProfile? aircraftProfile =
            bindingModel.AircraftProfiles.FirstOrDefault(profile =>
                string.Equals(
                    profile.AircraftProfile,
                    aircraftProfileName,
                    StringComparison.OrdinalIgnoreCase));

        if (aircraftProfile is null)
        {
            result.Warnings.Add(
                $"The {aircraftProfileName} control catalog could not be found.");

            return result;
        }

        Dictionary<string, List<BindingRow>> rowsByCallback =
            aircraftProfile.Rows
                .Where(row => row.IsCallback)
                .GroupBy(
                    row => row.CallbackName,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.OrdinalIgnoreCase);

        var callbackUseCounts =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

        foreach (string rawLine in File.ReadLines(autoKeyPath))
        {
            string line = rawLine.Trim();

            if (line.Length == 0 ||
                line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            Match match = KeyRowRegex.Match(line);

            if (!match.Success)
                continue;

            string callbackName =
                match.Groups["callback"].Value;

            if (string.Equals(
                    callbackName,
                    "SimDoNothing",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!rowsByCallback.TryGetValue(
                    callbackName,
                    out List<BindingRow>? matchingRows))
            {
                result.MissingCallbacks.Add(callbackName);
                continue;
            }

            int useIndex = callbackUseCounts.TryGetValue(
                callbackName,
                out int previousUseCount)
                ? previousUseCount
                : 0;

            BindingRow? targetRow =
                matchingRows.ElementAtOrDefault(useIndex);

            if (targetRow is null)
            {
                // More legacy occurrences than current Full-file rows.
                result.MissingCallbacks.Add(callbackName);
                continue;
            }

            callbackUseCounts[callbackName] = useIndex + 1;

            string keyScancode =
                match.Groups["keyScancode"].Value;

            int keyModifierFlags =
                ParseNumber(
                    match.Groups["keyModifierFlags"].Value);

            string chordScancode =
                match.Groups["chordScancode"].Value;

            int chordModifierFlags =
                ParseNumber(
                    match.Groups["chordModifierFlags"].Value);

            targetRow.KeyScancode = keyScancode;
            targetRow.KeyModifierFlags = keyModifierFlags;
            targetRow.ChordScancode = chordScancode;
            targetRow.ChordModifierFlags = chordModifierFlags;

            targetRow.IsModified =
                BindingDiffersFromFull(targetRow);

            result.AssignmentsImported++;
        }

        return result;
    }

    private static bool BindingDiffersFromFull(
        BindingRow row)
    {
        Match fullMatch =
            KeyRowRegex.Match(row.SourceRawLine.Trim());

        if (!fullMatch.Success)
            return true;

        string fullKeyScancode =
            fullMatch.Groups["keyScancode"].Value;

        int fullKeyModifierFlags =
            ParseNumber(
                fullMatch.Groups["keyModifierFlags"].Value);

        string fullChordScancode =
            fullMatch.Groups["chordScancode"].Value;

        int fullChordModifierFlags =
            ParseNumber(
                fullMatch.Groups["chordModifierFlags"].Value);

        return
            !string.Equals(
                row.KeyScancode,
                fullKeyScancode,
                StringComparison.OrdinalIgnoreCase) ||
            row.KeyModifierFlags != fullKeyModifierFlags ||
            !string.Equals(
                row.ChordScancode,
                fullChordScancode,
                StringComparison.OrdinalIgnoreCase) ||
            row.ChordModifierFlags != fullChordModifierFlags;
    }

    private static int ParseNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        value = value.Trim();

        if (value.StartsWith(
                "0x",
                StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt32(value, 16);
        }

        return int.TryParse(value, out int result)
            ? result
            : 0;
    }
}

public sealed class LegacyAutoKeyImportResult
{
    public int AssignmentsImported { get; set; }

    public HashSet<string> MissingCallbacks { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> Warnings { get; } = new();
}