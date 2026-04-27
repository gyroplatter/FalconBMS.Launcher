using FalconBMS.Launcher.Models;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Writes legacy Falcon BMS AUTO key files from the editable in-memory BindingModel.
/// 
/// This writer exists for BMS compatibility and third-party tools that still parse
/// BMS - Auto.key files.
/// 
/// The BindingModel remains the source of user-editable state. AUTO key files are
/// generated output only.
/// </summary>
public sealed class LegacyAutoKeyWriterService
{
    public void Write(string baseDir, BindingModel bindingModel)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("KEYOUT");
        DebugDiagnosticsService.Info($"Legacy AUTO key write begin. | ActionId={actionId}");

        string configDir = Path.Combine(baseDir, "User", "Config");
        Directory.CreateDirectory(configDir);

        WriteProfile(
            configDir,
            bindingModel,
            aircraftProfile: "F-16",
            fileName: "BMS - Auto.key",
            actionId: actionId);

        WriteProfile(
            configDir,
            bindingModel,
            aircraftProfile: "F-15ABCD",
            fileName: "BMS - Auto-F15ABCD.key",
            actionId: actionId);

        DebugDiagnosticsService.Info($"Legacy AUTO key write end. | ActionId={actionId}");
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
            DebugDiagnosticsService.Warn($"Legacy AUTO key write skipped. Missing profile: {aircraftProfile} | ActionId={actionId}");
            return;
        }

        string path = Path.Combine(configDir, fileName);
        string beforeSignature = DebugDiagnosticsService.GetFileSignature(path);
        string content = BuildProfileContent(profile);

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
            "LegacyAutoKeyWriterService.WriteProfile",
            aircraftProfile,
            actionId);

        string backupDir = Path.Combine(configDir, "Backup");
        Directory.CreateDirectory(backupDir);

        File.Copy(path, Path.Combine(backupDir, fileName), overwrite: true);
    }

    private static string BuildProfileContent(BindingAircraftProfile profile)
    {
        var sb = new StringBuilder();

        foreach (var row in profile.Rows)
        {
            if (row.RowKind == BindingRowKind.Other)
                continue;

            if (row.Visibility == -1)
                sb.Append("#===================================================================================\n");

            sb.Append(row.CallbackName);
            sb.Append(' ');
            sb.Append(row.SoundId);
            sb.Append(' ');
            sb.Append(0);
            sb.Append(' ');
            sb.Append(row.KeyScancode);
            sb.Append(' ');
            sb.Append(row.KeyModifierFlags);
            sb.Append(' ');
            sb.Append(row.ChordScancode);
            sb.Append(' ');
            sb.Append(row.ChordModifierFlags);
            sb.Append(' ');
            sb.Append(FormatVisibility(row.Visibility));
            sb.Append(' ');
            sb.Append(QuoteDescription(row.Description));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string FormatVisibility(int visibility)
    {
        return visibility switch
        {
            -2 => "-2",
            -1 => "-1",
            0 => "-0",
            1 => "1",
            _ => "-0"
        };
    }

    private static string QuoteDescription(string description)
    {
        string cleaned = description ?? "";
        cleaned = cleaned.Replace("\"", "\\\"");
        return $"\"{cleaned}\"";
    }
}