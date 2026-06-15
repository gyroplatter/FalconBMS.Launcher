using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FalconBMS.Launcher.Services;

public sealed class DeviceJsonWriterService
{
    public void Write(string baseDir, IReadOnlyList<DeviceBindingProfile> deviceProfiles)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("JSONHOTAS");
        DebugDiagnosticsService.Info($"Device JSON write begin. DeviceCount={deviceProfiles.Count} | ActionId={actionId}");

        string jsonDir = Path.Combine(baseDir, "User", "Config", "JSON");
        Directory.CreateDirectory(jsonDir);

        foreach (DeviceBindingProfile profile in deviceProfiles)
            WriteProfile(jsonDir, profile, actionId);

        DebugDiagnosticsService.Info($"Device JSON write end. | ActionId={actionId}");
    }

    public void WriteExportFile(
    DeviceBindingProfile profile,
    DeviceAircraftBindingProfile aircraft,
    string destinationPath,
    string actionId)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(destinationPath) ?? "");

        string beforeSignature =
            DebugDiagnosticsService.GetFileSignature(destinationPath);

        string content =
            BuildProfileJson(profile, aircraft);

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
            "DeviceJsonWriterService.WriteExportFile",
            profile.ProductName,
            actionId);
    }

    private static void WriteProfile(string configDir, DeviceBindingProfile profile, string actionId)
    {
        // Persist one JSON file per physical device + aircraft profile.
        // This makes each file a shareable binding set for one device in one aircraft.
        foreach (DeviceAircraftBindingProfile aircraft in profile.AircraftProfiles)
            WriteAircraftProfile(configDir, profile, aircraft, actionId);

        DeleteLegacySingleDeviceProfile(configDir, profile, actionId);
    }

    private static void WriteAircraftProfile(
        string configDir,
        DeviceBindingProfile profile,
        DeviceAircraftBindingProfile aircraft,
        string actionId)
    {
        string fileName = BuildFileName(profile, aircraft.AircraftProfile);
        string path = Path.Combine(configDir, fileName);

        string beforeSignature = DebugDiagnosticsService.GetFileSignature(path);
        string content = BuildProfileJson(profile, aircraft);

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
            "DeviceJsonWriterService.WriteAircraftProfile",
            profile.ProductName,
            actionId);
    }

    private static void DeleteLegacySingleDeviceProfile(string configDir, DeviceBindingProfile profile, string actionId)
    {
        string legacyFileName = BuildLegacySingleDeviceFileName(profile);
        string legacyPath = Path.Combine(configDir, legacyFileName);

        if (!File.Exists(legacyPath))
            return;

        try
        {
            File.SetAttributes(legacyPath, File.GetAttributes(legacyPath) & ~FileAttributes.ReadOnly);
            File.Delete(legacyPath);

            DebugDiagnosticsService.Info(
                $"Legacy single-device JSON removed after device/aircraft JSON write | Device=\"{profile.ProductName}\" | Json=\"{legacyFileName}\" | ActionId={actionId}");
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, $"Legacy single-device JSON delete failed: {legacyPath}");
        }
    }

    private static string BuildFileName(DeviceBindingProfile profile, string aircraftProfile)
    {
        // Filename format identifies the aircraft profile, durable device key,
        // and human-readable device name for easier sharing and troubleshooting.
        string safeAircraftProfile = SanitizeFileNameSegment(aircraftProfile);

        if (string.IsNullOrWhiteSpace(safeAircraftProfile))
            safeAircraftProfile = "Unknown Aircraft";

        string fileNameAircraftProfile = safeAircraftProfile.TrimEnd('.');
        string fileNameProductName = BuildFileNameProductName(profile);

        return $"DeviceBindings_{fileNameAircraftProfile}_{profile.DurableDeviceKey}_{fileNameProductName}.json";
    }

    private static string BuildLegacySingleDeviceFileName(DeviceBindingProfile profile)
    {
        string fileNameProductName = BuildFileNameProductName(profile);

        return $"DeviceBindings_{profile.DurableDeviceKey}_{fileNameProductName}.json";
    }

    private static string BuildFileNameProductName(DeviceBindingProfile profile)
    {
        string productName = SanitizeFileNameSegment(profile.ProductName);

        if (string.IsNullOrWhiteSpace(productName))
            productName = "Unknown Device";

        // Preserve the real product name in JSON, but avoid a filename boundary like "H.O.T.A.S..json".
        return productName.TrimEnd('.');
    }

    private static string BuildProfileJson(DeviceBindingProfile profile, DeviceAircraftBindingProfile aircraft)
    {
        var sb = new StringBuilder();

        sb.AppendLine("{");
        WriteProperty(sb, 1, "schema_version", 2, comma: true);
        WriteProperty(sb, 1, "binding_type", "hotas", comma: true);
        WriteProperty(sb, 1, "durable_device_key", profile.DurableDeviceKey, comma: true);
        WriteProperty(sb, 1, "pidvid", profile.PidVid, comma: true);
        WriteProperty(sb, 1, "product_name", profile.ProductName, comma: true);
        WriteProperty(sb, 1, "instance_name", profile.InstanceName, comma: true);
        WriteProperty(sb, 1, "vendor_id_hex", profile.VendorIdHex, comma: true);
        WriteProperty(sb, 1, "product_id_hex", profile.ProductIdHex, comma: true);
        WriteProperty(sb, 1, "duplicate_pidvid_sequence_number", profile.DuplicatePidVidSequenceNumber, comma: true);
        WriteProperty(sb, 1, "axis_count", profile.AxisCount, comma: true);
        WriteProperty(sb, 1, "button_count", profile.ButtonCount, comma: true);
        WriteProperty(sb, 1, "pov_count", profile.PovCount, comma: true);
        WriteProperty(sb, 1, "capabilities_read_successfully", profile.CapabilitiesReadSuccessfully, comma: true);
        WritePropertyNullableString(sb, 1, "last_seen_instance_guid", FormatGuid(profile.LastSeenInstanceGuid), comma: true);
        WriteProperty(sb, 1, "aircraft_profile", aircraft.AircraftProfile, comma: true);

        // Axis bindings are currently shared across aircraft profiles.
        // Each device/aircraft JSON includes the same axis block so the file is complete.
        // If axis blocks differ, the reader uses the F-16 axis block as authoritative.
        WriteProperty(sb, 1, "axis_binding_scope", "shared_currently_f16_wins_if_files_differ", comma: true);

        WriteAxisBindings(sb, profile.AxisBindings, indentLevel: 1, itemIndentLevel: 2, comma: true);
        WriteButtonBindings(sb, aircraft.ButtonBindings, indentLevel: 1, itemIndentLevel: 2, comma: true);
        WritePovBindings(sb, aircraft.PovBindings, indentLevel: 1, itemIndentLevel: 2, comma: false);

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void WriteAxisBindings(
        StringBuilder sb,
        IReadOnlyList<DeviceAxisBinding> axisBindings,
        int indentLevel,
        int itemIndentLevel,
        bool comma)
    {
        Indent(sb, indentLevel);
        sb.AppendLine("\"axis_bindings\": [");

        for (int i = 0; i < axisBindings.Count; i++)
        {
            DeviceAxisBinding axis = axisBindings[i];
            bool isLast = i == axisBindings.Count - 1;

            Indent(sb, itemIndentLevel);
            sb.AppendLine("{");

            WriteProperty(sb, itemIndentLevel + 1, "logical_axis_name", axis.LogicalAxisName, comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "physical_axis_index", axis.PhysicalAxisIndex, comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "saturation", axis.Saturation, comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "deadzone", axis.Deadzone, comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "curve", axis.Curve, comma: true);

            if (string.Equals(axis.LogicalAxisName, "Throttle", StringComparison.OrdinalIgnoreCase))
            {
                WriteProperty(sb, itemIndentLevel + 1, "invert", axis.Invert, comma: true);
                WriteProperty(sb, itemIndentLevel + 1, "afterburner_detent", axis.AfterburnerDetent, comma: true);
                WriteProperty(sb, itemIndentLevel + 1, "idle_detent", axis.IdleDetent, comma: false);
            }
            else
            {
                WriteProperty(sb, itemIndentLevel + 1, "invert", axis.Invert, comma: false);
            }

            Indent(sb, itemIndentLevel);
            sb.Append('}');
            if (!isLast)
                sb.Append(',');

            sb.AppendLine();
        }

        Indent(sb, indentLevel);
        sb.Append(']');
        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void WriteButtonBindings(
        StringBuilder sb,
        IReadOnlyList<DeviceButtonBinding> buttonBindings,
        int indentLevel,
        int itemIndentLevel,
        bool comma)
    {
        Indent(sb, indentLevel);
        sb.AppendLine("\"button_bindings\": [");

        for (int i = 0; i < buttonBindings.Count; i++)
        {
            DeviceButtonBinding button = buttonBindings[i];
            bool isLast = i == buttonBindings.Count - 1;

            Indent(sb, itemIndentLevel);
            sb.AppendLine("{");

            WriteProperty(sb, itemIndentLevel + 1, "button_index", button.ButtonIndex, comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "assignment_index", button.AssignmentIndex, comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "shift_state", DeviceButtonBinding.GetShiftState(button.AssignmentIndex), comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "trigger", DeviceButtonBinding.GetTrigger(button.AssignmentIndex), comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "callback_name", button.CallbackName, comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "invoke", button.Invoke, comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "sound_id", button.SoundId, comma: false);

            Indent(sb, itemIndentLevel);
            sb.Append('}');
            if (!isLast)
                sb.Append(',');

            sb.AppendLine();
        }

        Indent(sb, indentLevel);
        sb.Append(']');
        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void WritePovBindings(
        StringBuilder sb,
        IReadOnlyList<DevicePovBinding> povBindings,
        int indentLevel,
        int itemIndentLevel,
        bool comma)
    {
        Indent(sb, indentLevel);
        sb.AppendLine("\"pov_bindings\": [");

        for (int i = 0; i < povBindings.Count; i++)
        {
            DevicePovBinding pov = povBindings[i];
            bool isLast = i == povBindings.Count - 1;

            Indent(sb, itemIndentLevel);
            sb.AppendLine("{");

            WriteProperty(sb, itemIndentLevel + 1, "pov_index", pov.PovIndex, comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "direction", pov.Direction, comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "callback_name", pov.CallbackName, comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "invoke", pov.Invoke, comma: true);
            WriteProperty(sb, itemIndentLevel + 1, "sound_id", pov.SoundId, comma: false);

            Indent(sb, itemIndentLevel);
            sb.Append('}');
            if (!isLast)
                sb.Append(',');

            sb.AppendLine();
        }

        Indent(sb, indentLevel);
        sb.Append(']');
        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void WriteProperty(StringBuilder sb, int level, string name, string value, bool comma)
    {
        Indent(sb, level);
        sb.Append('"');
        sb.Append(EscapeJson(name));
        sb.Append("\": \"");
        sb.Append(EscapeJson(value));
        sb.Append('"');
        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void WritePropertyNullableString(StringBuilder sb, int level, string name, string? value, bool comma)
    {
        Indent(sb, level);
        sb.Append('"');
        sb.Append(EscapeJson(name));
        sb.Append("\": ");

        if (string.IsNullOrWhiteSpace(value))
        {
            sb.Append("null");
        }
        else
        {
            sb.Append('"');
            sb.Append(EscapeJson(value));
            sb.Append('"');
        }

        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void WriteProperty(StringBuilder sb, int level, string name, int value, bool comma)
    {
        Indent(sb, level);
        sb.Append('"');
        sb.Append(EscapeJson(name));
        sb.Append("\": ");
        sb.Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void WriteProperty(StringBuilder sb, int level, string name, int? value, bool comma)
    {
        Indent(sb, level);
        sb.Append('"');
        sb.Append(EscapeJson(name));
        sb.Append("\": ");

        if (value.HasValue)
            sb.Append(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        else
            sb.Append("null");

        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void WriteProperty(StringBuilder sb, int level, string name, bool value, bool comma)
    {
        Indent(sb, level);
        sb.Append('"');
        sb.Append(EscapeJson(name));
        sb.Append("\": ");
        sb.Append(value ? "true" : "false");
        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static string EscapeJson(string? value)
    {
        if (value is null)
            return "";

        var sb = new StringBuilder(value.Length + 8);

        foreach (char c in value)
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
                    if (char.IsControl(c))
                        sb.Append("\\u" + ((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string SanitizeFileNameSegment(string value)
    {
        string safeValue = value ?? "";

        foreach (char invalid in Path.GetInvalidFileNameChars())
            safeValue = safeValue.Replace(invalid, '_');

        return safeValue.Trim();
    }

    private static string FormatGuid(Guid? guid)
    {
        return guid.HasValue ? guid.Value.ToString("B") : "";
    }

    private static void Indent(StringBuilder sb, int level)
    {
        sb.Append(new string(' ', level * 2));
    }
}