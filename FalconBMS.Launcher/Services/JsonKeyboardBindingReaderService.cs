using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Reads keyboard binding JSON snapshots and overlays saved keyboard values onto the current BindingModel.
/// 
/// FULL key files still define structure, ordering, headers, and available callbacks.
/// JSON only restores saved binding values onto matching rows.
/// </summary>
public sealed class JsonKeyboardBindingReaderService
{
    public bool Apply(string baseDir, BindingModel bindingModel)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("JSONREAD");
        DebugDiagnosticsService.Info($"Keyboard JSON read begin. | ActionId={actionId}");

        string jsonDir = Path.Combine(baseDir, "User", "Config", "JSON");

        bool f16NeedsCatalogSync = ApplyProfile(jsonDir, bindingModel, "F-16", "KeyboardBindings.json", actionId);
        bool f15NeedsCatalogSync = ApplyProfile(jsonDir, bindingModel, "F-15ABCD", "KeyboardBindings_F-15ABCD.json", actionId);

        bool needsCatalogSync = f16NeedsCatalogSync || f15NeedsCatalogSync;

        DebugDiagnosticsService.Info(
            $"Keyboard JSON read end. NeedsCatalogSync={needsCatalogSync} | ActionId={actionId}");

        return needsCatalogSync;
    }

    private static bool ApplyProfile(
        string configDir,
        BindingModel bindingModel,
        string aircraftProfile,
        string fileName,
        string actionId)
    {
        var profile = bindingModel.AircraftProfiles.FirstOrDefault(
            x => string.Equals(x.AircraftProfile, aircraftProfile, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            DebugDiagnosticsService.Warn($"Keyboard JSON read skipped. Missing profile: {aircraftProfile} | ActionId={actionId}");
            return false;
        }

        string path = Path.Combine(configDir, fileName);
        if (!File.Exists(path))
        {
            bool missingJsonNeedsCatalogSync = profile.Rows.Count > 0;

            DebugDiagnosticsService.Info(
                $"Keyboard JSON read skipped. File not found: {path} | " +
                $"NeedsCatalogSync={missingJsonNeedsCatalogSync} | ActionId={actionId}");

            return missingJsonNeedsCatalogSync;
        }

        JsonKeyboardBindingDocument? document;

        try
        {
            document = ReadDocument(path);
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, $"Keyboard JSON read failed: {path}");

            // Do not auto-overwrite unreadable JSON. Leave the file for manual inspection.
            return false;
        }

        if (document?.Rows is null)
        {
            DebugDiagnosticsService.Warn($"Keyboard JSON read skipped. No rows found: {path} | ActionId={actionId}");
            return false;
        }

        var rowsByLineAndCallback = profile.Rows.ToDictionary(
            CreateLineAndCallbackKey,
            StringComparer.OrdinalIgnoreCase);

        var rowsByCallback = profile.Rows
            .Where(x => !string.Equals(x.CallbackName, "SimDoNothing", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.CallbackName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matchedRows = new HashSet<BindingRow>();

        int applied = 0;
        int matchedByLine = 0;
        int matchedByCallback = 0;
        int missing = 0;
        int changedFromFull = 0;

        foreach (var jsonRow in document.Rows)
        {
            if (jsonRow is null)
                continue;

            string callbackName = jsonRow.CallbackName ?? "";
            if (callbackName.Length == 0 || string.IsNullOrWhiteSpace(callbackName))
                continue;

            BindingRow? bindingRow = null;

            string lineKey = CreateLineAndCallbackKey(jsonRow.SourceLineNumber, callbackName);

            if (rowsByLineAndCallback.TryGetValue(lineKey, out var lineMatch))
            {
                bindingRow = lineMatch;
                matchedByLine++;
            }
            else if (!string.Equals(callbackName, "SimDoNothing", StringComparison.OrdinalIgnoreCase) &&
                     rowsByCallback.TryGetValue(callbackName, out var callbackMatch))
            {
                bindingRow = callbackMatch;
                matchedByCallback++;
            }

            if (bindingRow is null)
            {
                missing++;
                continue;
            }

            matchedRows.Add(bindingRow);

            bool modified = ApplyJsonRow(bindingRow, jsonRow);
            applied++;

            if (modified)
                changedFromFull++;
        }

        // If the FULL key catalog has rows that were not present in JSON, the JSON
        // snapshot needs to be backfilled. If JSON has rows that no longer exist in
        // the FULL key catalog, the JSON snapshot needs to be pruned.
        // Neither case is treated as a user binding edit.
        int newCatalogRows = profile.Rows.Count - matchedRows.Count;
        int staleJsonRows = missing;
        bool needsCatalogSync = newCatalogRows > 0 || staleJsonRows > 0;

        DebugDiagnosticsService.Info(
            $"Keyboard JSON applied | Aircraft={aircraftProfile} | " +
            $"Rows={document.Rows.Count} | Applied={applied} | " +
            $"MatchedByLine={matchedByLine} | MatchedByCallback={matchedByCallback} | " +
            $"Missing={missing} | ModifiedFromFull={changedFromFull} | " +
            $"NewCatalogRows={newCatalogRows} | StaleJsonRows={staleJsonRows} | NeedsCatalogSync={needsCatalogSync} | " +
            $"Path={path} | ActionId={actionId}");

        return needsCatalogSync;
    }

    private static JsonKeyboardBindingDocument? ReadDocument(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        using var stream = new MemoryStream(bytes);

        var serializer = new DataContractJsonSerializer(typeof(JsonKeyboardBindingDocument));
        return serializer.ReadObject(stream) as JsonKeyboardBindingDocument;
    }

    private static bool ApplyJsonRow(BindingRow bindingRow, JsonKeyboardBindingRow jsonRow)
    {
        int fullSoundId = bindingRow.SoundId;
        int fullUnused = bindingRow.Unused;
        string fullKeyScancode = bindingRow.KeyScancode;
        int fullKeyModifierFlags = bindingRow.KeyModifierFlags;
        string fullChordScancode = bindingRow.ChordScancode;
        int fullChordModifierFlags = bindingRow.ChordModifierFlags;

        if (jsonRow.SoundId.HasValue)
            bindingRow.SoundId = jsonRow.SoundId.Value;

        if (jsonRow.Unused.HasValue)
            bindingRow.Unused = jsonRow.Unused.Value;

        if (jsonRow.KeyScancode is not null)
            bindingRow.KeyScancode = jsonRow.KeyScancode;

        if (jsonRow.KeyModifierFlags.HasValue)
            bindingRow.KeyModifierFlags = jsonRow.KeyModifierFlags.Value;

        if (jsonRow.ChordScancode is not null)
            bindingRow.ChordScancode = jsonRow.ChordScancode;

        if (jsonRow.ChordModifierFlags.HasValue)
            bindingRow.ChordModifierFlags = jsonRow.ChordModifierFlags.Value;

        bool modified =
            bindingRow.SoundId != fullSoundId ||
            bindingRow.Unused != fullUnused ||
            !string.Equals(bindingRow.KeyScancode, fullKeyScancode, StringComparison.Ordinal) ||
            bindingRow.KeyModifierFlags != fullKeyModifierFlags ||
            !string.Equals(bindingRow.ChordScancode, fullChordScancode, StringComparison.Ordinal) ||
            bindingRow.ChordModifierFlags != fullChordModifierFlags;

        bindingRow.IsModified = modified;
        return modified;
    }

    private static string CreateLineAndCallbackKey(BindingRow row)
    {
        return CreateLineAndCallbackKey(row.SourceLineNumber, row.CallbackName);
    }

    private static string CreateLineAndCallbackKey(int sourceLineNumber, string callbackName)
    {
        return $"{sourceLineNumber}|{callbackName}";
    }

    [DataContract]
    private sealed class JsonKeyboardBindingDocument
    {
        [DataMember(Name = "schema_version")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "binding_type")]
        public string? BindingType { get; set; }

        [DataMember(Name = "aircraft_profile")]
        public string? AircraftProfile { get; set; }

        [DataMember(Name = "source_catalog_path")]
        public string? SourceCatalogPath { get; set; }

        [DataMember(Name = "rows")]
        public List<JsonKeyboardBindingRow>? Rows { get; set; }
    }

    [DataContract]
    private sealed class JsonKeyboardBindingRow
    {
        [DataMember(Name = "source_line_number")]
        public int SourceLineNumber { get; set; }

        [DataMember(Name = "row_kind")]
        public string? RowKind { get; set; }

        [DataMember(Name = "callback_name")]
        public string? CallbackName { get; set; }

        [DataMember(Name = "description")]
        public string? Description { get; set; }

        [DataMember(Name = "category_name")]
        public string? CategoryName { get; set; }

        [DataMember(Name = "section_name")]
        public string? SectionName { get; set; }

        [DataMember(Name = "sound_id")]
        public int? SoundId { get; set; }

        [DataMember(Name = "key_scancode")]
        public string? KeyScancode { get; set; }

        [DataMember(Name = "key_modifier_flags")]
        public int? KeyModifierFlags { get; set; }

        [DataMember(Name = "chord_scancode")]
        public string? ChordScancode { get; set; }

        [DataMember(Name = "chord_modifier_flags")]
        public int? ChordModifierFlags { get; set; }

        [DataMember(Name = "unused")]
        public int? Unused { get; set; }

        [DataMember(Name = "visibility")]
        public int? Visibility { get; set; }

        [DataMember(Name = "is_modified")]
        public bool? IsModified { get; set; }
    }
}