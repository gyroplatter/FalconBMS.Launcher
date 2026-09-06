using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Writes complete keyboard binding snapshots from the in-memory BindingModel.
/// 
/// These JSON files are intended to become the launcher's user-state format and
/// future BMS input format. They are separate from legacy AUTO key files, which
/// remain generated compatibility outputs.
/// 
/// This writer intentionally preserves every BindingRow, including headers,
/// locked rows, hidden rows, remarks, and SimDoNothing rows. Import/merge rules
/// will be added later.
/// </summary>
public sealed class JsonKeyboardBindingWriterService
{
    public void Write(string baseDir, BindingModel bindingModel)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("JSONKEY");
        DebugDiagnosticsService.Info($"Keyboard JSON write begin. | ActionId={actionId}");

        string jsonDir = Path.Combine(baseDir, "User", "Config", "JSON");
        Directory.CreateDirectory(jsonDir);

        WriteProfile(
            jsonDir,
            bindingModel,
            aircraftProfile: "F-16",
            fileName: "KeyboardBindings_F-16.json",
            actionId: actionId);

        WriteProfile(
            jsonDir,
            bindingModel,
            aircraftProfile: "F-15ABCD",
            fileName: "KeyboardBindings_F-15ABCD.json",
            actionId: actionId);

        DeleteLegacyF16KeyboardJson(jsonDir, actionId);

        DebugDiagnosticsService.Info($"Keyboard JSON write end. | ActionId={actionId}");
    }

    public void WriteExportFile(
    BindingAircraftProfile profile,
    string destinationPath,
    string actionId)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(destinationPath) ?? "");

        string beforeSignature =
            DebugDiagnosticsService.GetFileSignature(destinationPath);

        string content =
            BuildProfileJson(profile);

        if (File.Exists(destinationPath))
            File.SetAttributes(destinationPath, File.GetAttributes(destinationPath) & ~FileAttributes.ReadOnly);

        File.WriteAllText(
            destinationPath,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        DebugDiagnosticsService.LogFileWriteResult(
            Path.GetFileName(destinationPath),
            destinationPath,
            beforeSignature,
            "JsonKeyboardBindingWriterService.WriteExportFile",
            profile.AircraftProfile,
            actionId);
    }

    private static void WriteProfile(
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
            DebugDiagnosticsService.Warn($"Keyboard JSON write skipped. Missing profile: {aircraftProfile} | ActionId={actionId}");
            return;
        }

        string path = Path.Combine(configDir, fileName);
        string beforeSignature = DebugDiagnosticsService.GetFileSignature(path);
        string content = BuildProfileJson(profile);

        if (File.Exists(path))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);

        if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            File.WriteAllText(
                path,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        DebugDiagnosticsService.LogFileWriteResult(
            fileName,
            path,
            beforeSignature,
            "JsonKeyboardBindingWriterService.WriteProfile",
            aircraftProfile,
            actionId);
    }

    private static void DeleteLegacyF16KeyboardJson(string configDir, string actionId)
    {
        string legacyPath = Path.Combine(configDir, "KeyboardBindings.json");

        if (!File.Exists(legacyPath))
            return;

        try
        {
            File.SetAttributes(legacyPath, File.GetAttributes(legacyPath) & ~FileAttributes.ReadOnly);
            File.Delete(legacyPath);

            DebugDiagnosticsService.Info(
                $"Legacy F-16 keyboard JSON removed after aircraft-specific keyboard JSON write | Json=\"KeyboardBindings.json\" | ActionId={actionId}");
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, $"Legacy F-16 keyboard JSON delete failed: {legacyPath}");
        }
    }

    private static string BuildProfileJson(BindingAircraftProfile profile)
    {
        var sb = new StringBuilder();

        // Build the current set of combinations actually owned by user-modified
        // rows. A suppressed FULL default is preserved in JSON only while one
        // of these current mappings still claims that same combination.
        HashSet<string> userModifiedCombos =
            BuildUserModifiedComboSet(profile.Rows);

        sb.AppendLine("{");
        WriteProperty(sb, 1, "schema_version", 1, comma: true);
        WriteProperty(sb, 1, "binding_type", "keyboard", comma: true);
        WriteProperty(sb, 1, "aircraft_profile", profile.AircraftProfile, comma: true);
        WriteProperty(sb, 1, "source_catalog_path", profile.SourceCatalogPath, comma: true);

        Indent(sb, 1);
        sb.AppendLine("\"rows\": [");

        for (int i = 0; i < profile.Rows.Count; i++)
        {
            BindingRow row = profile.Rows[i];
            bool isLastRow = i == profile.Rows.Count - 1;

            Indent(sb, 2);
            sb.AppendLine("{");

            WriteProperty(sb, 3, "source_line_number", row.SourceLineNumber, comma: true);
            WriteProperty(sb, 3, "callback_name", row.CallbackName, comma: true);

            WriteProperty(sb, 3, "description", row.Description, comma: true);
            WriteProperty(sb, 3, "category_name", row.CategoryName, comma: true);
            WriteProperty(sb, 3, "section_name", row.SectionName, comma: true);

            WriteProperty(sb, 3, "sound_id", row.SoundId, comma: true);

            // While a user-modified row still owns the conflicting combination,
            // preserve the real FULL assignment in JSON. This keeps suppression
            // runtime-only and prevents the automatically blank row from becoming
            // a permanent user clear.
            //
            // If the user frees that combination during this session, keep the
            // row visually suppressed for the remainder of the session but write
            // its temporary blank value with is_modified=false. On next startup,
            // the normal "unmodified JSON loses to FULL" rule restores the FULL
            // default and schedules the normal catalog/output synchronization.
            bool usePreservedFullDefault =
                row.IsKeyboardDefaultSuppressed &&
                !row.IsModified &&
                userModifiedCombos.Contains(
                    CreateComboKey(
                        row.SuppressedDefaultKeyScancode,
                        row.SuppressedDefaultKeyModifierFlags,
                        row.SuppressedDefaultChordScancode,
                        row.SuppressedDefaultChordModifierFlags));

            string jsonKeyScancode = usePreservedFullDefault
                ? row.SuppressedDefaultKeyScancode
                : row.KeyScancode;

            int jsonKeyModifierFlags = usePreservedFullDefault
                ? row.SuppressedDefaultKeyModifierFlags
                : row.KeyModifierFlags;

            string jsonChordScancode = usePreservedFullDefault
                ? row.SuppressedDefaultChordScancode
                : row.ChordScancode;

            int jsonChordModifierFlags = usePreservedFullDefault
                ? row.SuppressedDefaultChordModifierFlags
                : row.ChordModifierFlags;

            WriteProperty(sb, 3, "key_scancode", jsonKeyScancode, comma: true);
            WriteProperty(sb, 3, "key_modifier_flags", jsonKeyModifierFlags, comma: true);
            WriteProperty(sb, 3, "chord_scancode", jsonChordScancode, comma: true);
            WriteProperty(sb, 3, "chord_modifier_flags", jsonChordModifierFlags, comma: true);

            WriteProperty(sb, 3, "unused", row.Unused, comma: true);
            WriteProperty(sb, 3, "visibility", row.Visibility, comma: true);

            WriteProperty(sb, 3, "row_kind", row.RowKind.ToString(), comma: true);
            WriteProperty(sb, 3, "is_modified", row.IsModified, comma: false);

            Indent(sb, 2);
            sb.Append('}');
            if (!isLastRow)
                sb.Append(',');

            sb.AppendLine();
        }

        Indent(sb, 1);
        sb.AppendLine("]");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static HashSet<string> BuildUserModifiedComboSet(
        IReadOnlyList<BindingRow> rows)
    {
        var claimedCombos =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (BindingRow row in rows)
        {
            if (!row.IsModified ||
                string.Equals(
                    row.CallbackName,
                    "CommandsSetKeyCombo",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    row.KeyScancode,
                    "0xFFFFFFFF",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            claimedCombos.Add(
                CreateComboKey(
                    row.KeyScancode,
                    row.KeyModifierFlags,
                    row.ChordScancode,
                    row.ChordModifierFlags));
        }

        return claimedCombos;
    }

    private static string CreateComboKey(
        string keyScancode,
        int keyModifierFlags,
        string chordScancode,
        int chordModifierFlags)
    {
        return
            $"{keyScancode}|{keyModifierFlags}|{chordScancode}|{chordModifierFlags}";
    }

    private static void WriteProperty(StringBuilder sb, int indentLevel, string name, string value, bool comma)
    {
        Indent(sb, indentLevel);
        sb.Append('"');
        sb.Append(EscapeJson(name));
        sb.Append("\": ");
        sb.Append('"');
        sb.Append(EscapeJson(value));
        sb.Append('"');

        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void WriteProperty(StringBuilder sb, int indentLevel, string name, int value, bool comma)
    {
        Indent(sb, indentLevel);
        sb.Append('"');
        sb.Append(EscapeJson(name));
        sb.Append("\": ");
        sb.Append(value);

        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void WriteProperty(StringBuilder sb, int indentLevel, string name, bool value, bool comma)
    {
        Indent(sb, indentLevel);
        sb.Append('"');
        sb.Append(EscapeJson(name));
        sb.Append("\": ");
        sb.Append(value ? "true" : "false");

        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void Indent(StringBuilder sb, int indentLevel)
    {
        sb.Append(' ', indentLevel * 2);
    }

    private static string EscapeJson(string? value)
    {
        string safeValue = value ?? "";
        if (safeValue.Length == 0)
            return "";

        var sb = new StringBuilder(safeValue.Length + 8);

        foreach (char c in safeValue)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;

                case '"':
                    sb.Append("\\\"");
                    break;

                case '\b':
                    sb.Append("\\b");
                    break;

                case '\f':
                    sb.Append("\\f");
                    break;

                case '\n':
                    sb.Append("\\n");
                    break;

                case '\r':
                    sb.Append("\\r");
                    break;

                case '\t':
                    sb.Append("\\t");
                    break;

                default:
                    if (c < 32)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }
}