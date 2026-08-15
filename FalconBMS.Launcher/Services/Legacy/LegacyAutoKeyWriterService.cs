using FalconBMS.Launcher.Models;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
    private const int DirectInputButtonCount = 128;
    private const string DoNothingCallback = "SimDoNothing";

    private static readonly Regex OldNameSanitizeRx =
        new(@"[^A-Za-z0-9\~\`\[\]\{\}\-_\=\'\x20]", RegexOptions.Compiled);

    public void Write(
        string baseDir,
        BindingModel bindingModel,
        System.Collections.Generic.IReadOnlyList<DeviceBindingProfile> connectedDeviceProfiles)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("KEYOUT");
        DebugDiagnosticsService.Info($"Legacy AUTO key write begin. | ActionId={actionId}");

        string configDir = Path.Combine(baseDir, "User", "Config");
        Directory.CreateDirectory(configDir);

        WriteProfile(
            configDir,
            bindingModel,
            connectedDeviceProfiles,
            aircraftProfile: "F-16",
            fileName: "BMS - Auto.key",
            actionId: actionId);

        WriteProfile(
            configDir,
            bindingModel,
            connectedDeviceProfiles,
            aircraftProfile: "F-15ABCD",
            fileName: "BMS - Auto-F15ABCD.key",
            actionId: actionId);

        DebugDiagnosticsService.Info($"Legacy AUTO key write end. | ActionId={actionId}");
    }

    private static void WriteProfile(
        string configDir,
        BindingModel bindingModel,
        System.Collections.Generic.IReadOnlyList<DeviceBindingProfile> connectedDeviceProfiles,
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

        // Use the same connected-device list as every other BMS legacy output.
        // This keeps normal and shifted DX numbering aligned with DeviceSorting.txt.
        string content = BuildProfileContent(profile, connectedDeviceProfiles, aircraftProfile);

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

    private static string BuildProfileContent(
        BindingAircraftProfile profile,
        System.Collections.Generic.IReadOnlyList<DeviceBindingProfile> deviceProfiles,
        string aircraftProfile)
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

        AppendDeviceDxSections(sb, deviceProfiles, aircraftProfile);
        AppendDevicePovSections(sb, deviceProfiles, aircraftProfile);

        return sb.ToString();
    }

    private static void AppendDeviceDxSections(
        StringBuilder sb,
        System.Collections.Generic.IReadOnlyList<DeviceBindingProfile> deviceProfiles,
        string aircraftProfile)
    {
        int deviceCount = deviceProfiles.Count;

        for (int deviceSlotIndex = 0; deviceSlotIndex < deviceProfiles.Count; deviceSlotIndex++)
        {
            DeviceBindingProfile deviceProfile = deviceProfiles[deviceSlotIndex];

            DeviceAircraftBindingProfile? aircraft = deviceProfile.AircraftProfiles.FirstOrDefault(x =>
                string.Equals(x.AircraftProfile, aircraftProfile, StringComparison.OrdinalIgnoreCase));

            if (aircraft is null)
                continue;

            string deviceName = SanitizeDeviceName(deviceProfile.ProductName);

            sb.Append('\n');
            sb.Append("#======== ");
            sb.Append(deviceName);
            sb.Append(" ========\n");

            foreach (DeviceButtonBinding button in aircraft.ButtonBindings
                         .OrderBy(x => x.ButtonIndex)
                         .ThenBy(x => x.AssignmentIndex))
            {
                if (string.IsNullOrWhiteSpace(button.CallbackName))
                    continue;

                if (string.Equals(button.CallbackName, DoNothingCallback, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(button.CallbackName, "SimHotasPinkyShift", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(button.CallbackName, "SimHotasShift", StringComparison.OrdinalIgnoreCase))
                {
                    if (button.AssignmentIndex != 0)
                        continue;

                    AppendDxLine(
                        sb,
                        button.CallbackName,
                        deviceSlotIndex * DirectInputButtonCount + button.ButtonIndex,
                        invokeValue: -1,
                        releaseFlag: "0",
                        button.SoundId);

                    AppendDxLine(
                        sb,
                        button.CallbackName,
                        deviceCount * DirectInputButtonCount + deviceSlotIndex * DirectInputButtonCount + button.ButtonIndex,
                        invokeValue: -1,
                        releaseFlag: "0",
                        button.SoundId);

                    continue;
                }

                bool shifted = button.AssignmentIndex == 1 || button.AssignmentIndex == 3;
                int dxNumber =
                    (shifted ? deviceCount * DirectInputButtonCount : 0) +
                    deviceSlotIndex * DirectInputButtonCount +
                    button.ButtonIndex;

                AppendDxLine(
                    sb,
                    button.CallbackName,
                    dxNumber,
                    InvokeToInt(button.Invoke),
                    button.AssignmentIndex == 2 || button.AssignmentIndex == 3 ? "0x42" : "0",
                    button.SoundId);
            }
        }
    }

    private static void AppendDevicePovSections(
        StringBuilder sb,
        System.Collections.Generic.IReadOnlyList<DeviceBindingProfile> deviceProfiles,
        string aircraftProfile)
    {
        int rollSlot = FindDeviceSlotForLogicalAxis(deviceProfiles, "Roll");
        int throttleSlot = FindDeviceSlotForLogicalAxis(deviceProfiles, "Throttle");

        if (rollSlot < 0 || rollSlot >= deviceProfiles.Count)
            return;

        bool singleDevice = throttleSlot < 0 || throttleSlot == rollSlot;

        AppendPovSectionForDevice(
            sb,
            deviceProfiles[rollSlot],
            aircraftProfile,
            povBase: 0,
            hatId: 0,
            requirePhysicalHat: true);

        if (singleDevice)
        {
            // Legacy AL writes POV #1 as an empty/commented section even when
            // the device only reports one physical POV hat.
            AppendPovSectionForDevice(
                sb,
                deviceProfiles[rollSlot],
                aircraftProfile,
                povBase: 1,
                hatId: 1,
                requirePhysicalHat: false);
        }
        else if (throttleSlot >= 0 && throttleSlot < deviceProfiles.Count)
        {
            AppendPovSectionForDevice(
                sb,
                deviceProfiles[throttleSlot],
                aircraftProfile,
                povBase: 1,
                hatId: 0,
                requirePhysicalHat: true);
        }
    }

    private static void AppendPovSectionForDevice(
        StringBuilder sb,
        DeviceBindingProfile deviceProfile,
        string aircraftProfile,
        int povBase,
        int hatId,
        bool requirePhysicalHat)
    {
        DeviceAircraftBindingProfile? aircraft = deviceProfile.AircraftProfiles.FirstOrDefault(x =>
            string.Equals(x.AircraftProfile, aircraftProfile, StringComparison.OrdinalIgnoreCase));

        if (aircraft is null)
            return;

        if (requirePhysicalHat && deviceProfile.PovCount <= hatId)
            return;

        string deviceName = SanitizeDeviceName(deviceProfile.ProductName);

        sb.AppendLine("\n");
        sb.Append("#======== ");
        sb.Append(deviceName);
        sb.Append(" : POV #");
        sb.Append(povBase.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(" ========");

        for (int direction = 0; direction < 8; direction++)
        {
            DevicePovBinding? unshifted = aircraft.PovBindings.FirstOrDefault(x =>
                x.PovIndex == hatId &&
                x.Direction == direction &&
                string.Equals(x.Invoke, "Default", StringComparison.OrdinalIgnoreCase));

            DevicePovBinding? shifted = aircraft.PovBindings.FirstOrDefault(x =>
                x.PovIndex == hatId &&
                x.Direction == direction &&
                string.Equals(x.Invoke, "Shift", StringComparison.OrdinalIgnoreCase));

            AppendPovLine(
                sb,
                unshifted?.CallbackName,
                povBase,
                direction,
                unshifted?.SoundId ?? 0);

            AppendPovLine(
                sb,
                shifted?.CallbackName,
                povBase + 2,
                direction,
                shifted?.SoundId ?? 0);
        }
    }

    private static int FindDeviceSlotForLogicalAxis(
        System.Collections.Generic.IReadOnlyList<DeviceBindingProfile> deviceProfiles,
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

    private static void AppendDxLine(
        StringBuilder sb,
        string callbackName,
        int dxNumber,
        int invokeValue,
        string releaseFlag,
        int soundId)
    {
        sb.Append(callbackName);
        sb.Append(' ');
        sb.Append(dxNumber.ToString(CultureInfo.InvariantCulture));
        sb.Append(' ');
        sb.Append(invokeValue.ToString(CultureInfo.InvariantCulture));
        sb.Append(" -2 ");
        sb.Append(releaseFlag);
        sb.Append(" 0x0 ");
        sb.Append(soundId.ToString(CultureInfo.InvariantCulture));
        sb.Append('\n');
    }

    private static void AppendPovLine(
        StringBuilder sb,
        string? callbackName,
        int povNumber,
        int direction,
        int soundId)
    {
        string callback = string.IsNullOrWhiteSpace(callbackName)
            ? DoNothingCallback
            : callbackName!;

        if (string.Equals(callback, DoNothingCallback, StringComparison.OrdinalIgnoreCase))
            sb.Append("# ");

        sb.Append(callback);
        sb.Append(' ');
        sb.Append(povNumber.ToString(CultureInfo.InvariantCulture));
        sb.Append(" -1 -3 ");
        sb.Append(direction.ToString(CultureInfo.InvariantCulture));
        sb.Append(" 0x0 ");
        sb.Append(soundId.ToString(CultureInfo.InvariantCulture));
        sb.Append('\n');
    }

    private static int InvokeToInt(string invoke)
    {
        return invoke switch
        {
            "Default" => -1,
            "Down" => -2,
            "Up" => -4,
            "UI" => 8,
            _ => -1
        };
    }

    private static string SanitizeDeviceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return OldNameSanitizeRx.Replace(value, "").Trim();
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