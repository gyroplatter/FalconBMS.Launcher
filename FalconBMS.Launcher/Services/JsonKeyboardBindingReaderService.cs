using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Reads keyboard binding JSON snapshots and overlays saved keyboard values onto the current BindingModel.
/// 
/// FULL key files still define structure, ordering, headers, and available callbacks.
/// JSON only restores saved user-modified binding values onto matching rows.
/// 
/// Catalog changes are tracked separately from user edits:
/// - FULL adds a row missing from JSON.
/// - FULL removes a row still present in JSON.
/// - FULL changes defaults/metadata for a row that the user has not modified.
/// </summary>
public sealed class JsonKeyboardBindingReaderService
{
    public bool Apply(string baseDir, BindingModel bindingModel)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("JSONREAD");
        DebugDiagnosticsService.Info($"Keyboard JSON read begin. | ActionId={actionId}");

        string jsonDir = Path.Combine(baseDir, "User", "Config", "JSON");

        bool f16NeedsCatalogSync = ApplyProfile(
            jsonDir,
            bindingModel,
            "F-16",
            "KeyboardBindings_F-16.json",
            fallbackFileName: "KeyboardBindings.json",
            actionId);

        bool f15NeedsCatalogSync = ApplyProfile(
            jsonDir,
            bindingModel,
            "F-15ABCD",
            "KeyboardBindings_F-15ABCD.json",
            fallbackFileName: null,
            actionId);

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
        string? fallbackFileName,
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

        if (!File.Exists(path) && !string.IsNullOrWhiteSpace(fallbackFileName))
        {
            string fallbackPath = Path.Combine(configDir, fallbackFileName);

            if (File.Exists(fallbackPath))
            {
                path = fallbackPath;

                DebugDiagnosticsService.Info(
                    $"Keyboard JSON read using fallback file. Aircraft={aircraftProfile} | " +
                    $"Requested={Path.Combine(configDir, fileName)} | Fallback={fallbackPath} | ActionId={actionId}");
            }
        }

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
        int userModifiedRows = 0;
        int defaultChangedRows = 0;
        int metadataChangedRows = 0;

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

            bool jsonRowIsUserModified = IsJsonRowUserModified(jsonRow, bindingRow);
            bool jsonBindingValuesDifferFromFull = JsonBindingValuesDifferFromFull(bindingRow, jsonRow);
            bool jsonCatalogMetadataDiffersFromFull = JsonCatalogMetadataDiffersFromFull(bindingRow, jsonRow);

            if (jsonRowIsUserModified)
            {
                ApplyUserModifiedJsonRow(bindingRow, jsonRow);
                userModifiedRows++;

                // User-modified bindings should keep their JSON assignment, but JSON
                // should still be rewritten if FULL changed metadata such as line number,
                // description, category, section, visibility, or row kind.
                if (jsonCatalogMetadataDiffersFromFull)
                    metadataChangedRows++;
            }
            else
            {
                // This row was not modified by the user, so the current FULL file should win.
                // Do not overlay old JSON defaults onto the current FULL row.
                bindingRow.IsModified = false;

                // If old JSON defaults differ from current FULL defaults, the launcher should
                // mark a catalog sync so JSON is rewritten with the new FULL defaults.
                if (jsonBindingValuesDifferFromFull)
                    defaultChangedRows++;

                if (jsonCatalogMetadataDiffersFromFull)
                    metadataChangedRows++;
            }

            applied++;
        }

        // If the FULL key catalog has rows that were not present in JSON, the JSON
        // snapshot needs to be backfilled. If JSON has rows that no longer exist in
        // the FULL key catalog, the JSON snapshot needs to be pruned. If FULL changed
        // default values for unmodified rows, JSON needs to be refreshed to those new
        // defaults. None of these cases are treated as user binding edits.
        int newCatalogRows = profile.Rows.Count - matchedRows.Count;
        int staleJsonRows = missing;

        bool needsCatalogSync =
            newCatalogRows > 0 ||
            staleJsonRows > 0 ||
            defaultChangedRows > 0 ||
            metadataChangedRows > 0;

        DebugDiagnosticsService.Info(
            $"Keyboard JSON applied | Aircraft={aircraftProfile} | " +
            $"Rows={document.Rows.Count} | Applied={applied} | " +
            $"MatchedByLine={matchedByLine} | MatchedByCallback={matchedByCallback} | " +
            $"Missing={missing} | UserModifiedRows={userModifiedRows} | " +
            $"DefaultChangedRows={defaultChangedRows} | MetadataChangedRows={metadataChangedRows} | " +
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

    private static bool IsJsonRowUserModified(JsonKeyboardBindingRow jsonRow, BindingRow fullRow)
    {
        if (jsonRow.IsModified.HasValue)
            return jsonRow.IsModified.Value;

        // Older JSON snapshots may not have is_modified. Preserve those values rather
        // than risking accidental loss of user bindings.
        return JsonBindingValuesDifferFromFull(fullRow, jsonRow);
    }

    private static void ApplyUserModifiedJsonRow(BindingRow bindingRow, JsonKeyboardBindingRow jsonRow)
    {
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

        bindingRow.IsModified = true;
    }

    private static bool JsonBindingValuesDifferFromFull(BindingRow fullRow, JsonKeyboardBindingRow jsonRow)
    {
        if (jsonRow.SoundId.HasValue && jsonRow.SoundId.Value != fullRow.SoundId)
            return true;

        if (jsonRow.Unused.HasValue && jsonRow.Unused.Value != fullRow.Unused)
            return true;

        if (jsonRow.KeyScancode is not null &&
            !string.Equals(jsonRow.KeyScancode, fullRow.KeyScancode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (jsonRow.KeyModifierFlags.HasValue && jsonRow.KeyModifierFlags.Value != fullRow.KeyModifierFlags)
            return true;

        if (jsonRow.ChordScancode is not null &&
            !string.Equals(jsonRow.ChordScancode, fullRow.ChordScancode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (jsonRow.ChordModifierFlags.HasValue && jsonRow.ChordModifierFlags.Value != fullRow.ChordModifierFlags)
            return true;

        return false;
    }

    private static bool JsonCatalogMetadataDiffersFromFull(BindingRow fullRow, JsonKeyboardBindingRow jsonRow)
    {
        if (jsonRow.SourceLineNumber != fullRow.SourceLineNumber)
            return true;

        if (!string.Equals(jsonRow.CallbackName ?? "", fullRow.CallbackName, StringComparison.Ordinal))
            return true;

        if (!string.Equals(jsonRow.Description ?? "", fullRow.Description, StringComparison.Ordinal))
            return true;

        if (!string.Equals(jsonRow.CategoryName ?? "", fullRow.CategoryName, StringComparison.Ordinal))
            return true;

        if (!string.Equals(jsonRow.SectionName ?? "", fullRow.SectionName, StringComparison.Ordinal))
            return true;

        if (jsonRow.Visibility.HasValue && jsonRow.Visibility.Value != fullRow.Visibility)
            return true;

        if (!string.Equals(jsonRow.RowKind ?? "", fullRow.RowKind.ToString(), StringComparison.Ordinal))
            return true;

        return false;
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