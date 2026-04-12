using FalconBMS.Launcher.Input;
using System;
using System.IO;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Writes keymapping override output back to Falcon key/config files.
/// </summary>
public sealed class KeyMappingOverrideWriter
{
    // Mirrors ORIGINAL:
    // OverrideSetting.SaveKeyMapping + WriteKeyLines
    public void SaveKeyMapping(string baseDir, KeyFile keyFileF16, KeyFile keyFileF15, JoyAssgnLite[] joys, int rollJoyId, int throttleJoyId)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("KEYOUT");
        DebugDiagnosticsService.Info($"Emitting BMS - Auto.key and BMS - Auto-F15ABCD.key.. | ActionId={actionId}");

        string configDir = Path.Combine(baseDir, "User", "Config");
        Directory.CreateDirectory(configDir);

        string filename = Path.Combine(configDir, "BMS - Auto.key");
        string filenameF15 = Path.Combine(configDir, "BMS - Auto-F15ABCD.key");

        string beforeF16 = DebugDiagnosticsService.GetFileSignature(filename);
        string beforeF15 = DebugDiagnosticsService.GetFileSignature(filenameF15);

        if (File.Exists(filename))
            File.SetAttributes(filename, File.GetAttributes(filename) & ~FileAttributes.ReadOnly);

        if (File.Exists(filenameF15))
            File.SetAttributes(filenameF15, File.GetAttributes(filenameF15) & ~FileAttributes.ReadOnly);

        string? previousProfile = joys.Length > 0 ? joys[0].CurrentAvionicsProfile : null;

        try
        {
            // Build the exact outgoing key text first, then only write when the
            // file content has actually changed. This removes redundant launch-time rewrites.
            SelectAvionicsProfile(joys, null);
            WriteKeyLinesIfChanged(filename, keyFileF16, joys, rollJoyId, throttleJoyId);

            SelectAvionicsProfile(joys, JoyAssgnLite.F15ProfileTag);
            WriteKeyLinesIfChanged(filenameF15, keyFileF15, joys, rollJoyId, throttleJoyId);
        }
        finally
        {
            SelectAvionicsProfile(joys, previousProfile);
        }

        DebugDiagnosticsService.LogFileWriteResult("BMS - Auto.key", filename, beforeF16, "KeyMappingOverrideWriter.SaveKeyMapping", "WriteF16", actionId);
        DebugDiagnosticsService.LogFileWriteResult("BMS - Auto-F15ABCD.key", filenameF15, beforeF15, "KeyMappingOverrideWriter.SaveKeyMapping", "WriteF15", actionId);

        string backupDir = Path.Combine(baseDir, "User", "Config", "Backup");
        Directory.CreateDirectory(backupDir);

        File.Copy(filename, Path.Combine(backupDir, "BMS - Auto.key"), overwrite: true);
        File.Copy(filenameF15, Path.Combine(backupDir, "BMS - Auto-F15ABCD.key"), overwrite: true);

        DebugDiagnosticsService.Info($"Backup key copies complete. | ActionId={actionId}");
    }

    private static void SelectAvionicsProfile(JoyAssgnLite[] joys, string? profile)
    {
        for (int i = 0; i < joys.Length; i++)
            joys[i].SelectAvionicsProfile(profile);
    }

    private static void WriteKeyLinesIfChanged(string filename, KeyFile keyFile, JoyAssgnLite[] joyAssgns, int rollJoyId, int throttleJoyId)
    {
        string newContent = BuildKeyFileContent(keyFile, joyAssgns, rollJoyId, throttleJoyId);

        if (File.Exists(filename))
        {
            string existingContent = File.ReadAllText(filename);
            if (string.Equals(existingContent, newContent, StringComparison.Ordinal))
                return;
        }

        File.WriteAllText(
            filename,
            newContent,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string BuildKeyFileContent(KeyFile keyFile, JoyAssgnLite[] joyAssgns, int rollJoyId, int throttleJoyId)
    {
        using var sw = new StringWriter();
        sw.NewLine = "\n";

        for (int i = 0; i < keyFile.keyAssign.Length; i++)
            sw.Write(keyFile.keyAssign[i].GetKeyLine());

        for (int i = 0; i < joyAssgns.Length; i++)
            sw.Write(joyAssgns[i].GetKeyLineDX(i, joyAssgns.Length));

        int rollSlot = NormalizeSlotIndex(rollJoyId, joyAssgns.Length);
        int throttleSlot = NormalizeSlotIndex(throttleJoyId, joyAssgns.Length);

        if (rollSlot < 0 || rollSlot >= joyAssgns.Length)
            return sw.ToString();

        bool singleDevice = throttleSlot < 0 || throttleSlot == rollSlot;
        var joyStick = joyAssgns[rollSlot];

        if (joyStick.Pov.Length > 0)
            sw.Write(joyStick.GetKeyLinePOV(povBase: 0, hatId: 0));

        if (singleDevice)
        {
            if (joyStick.Pov.Length > 1)
                sw.Write(joyStick.GetKeyLinePOV(povBase: 1, hatId: 1));
        }
        else
        {
            if (throttleSlot >= 0 && throttleSlot < joyAssgns.Length)
            {
                var joyThrottle = joyAssgns[throttleSlot];
                if (joyThrottle.Pov.Length > 0)
                    sw.Write(joyThrottle.GetKeyLinePOV(povBase: 1, hatId: 0));
            }
        }

        return sw.ToString();
    }

    private static int NormalizeSlotIndex(int joyId, int deviceCount)
    {
        if (joyId >= 0 && joyId < deviceCount)
            return joyId;

        int slot = joyId - 2;
        if (slot >= 0 && slot < deviceCount)
            return slot;

        return joyId;
    }
}
