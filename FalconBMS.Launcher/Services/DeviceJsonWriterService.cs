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

    private static void WriteProfile(string configDir, DeviceBindingProfile profile, string actionId)
    {
        string fileName = BuildFileName(profile);
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
            "DeviceJsonWriterService.WriteProfile",
            profile.ProductName,
            actionId);
    }

    private static string BuildFileName(DeviceBindingProfile profile)
    {
        string productName = SanitizeFileNameSegment(profile.ProductName);

        if (string.IsNullOrWhiteSpace(productName))
            productName = "Unknown Device";

        // Preserve the real product name in JSON, but avoid a filename boundary like "H.O.T.A.S..json".
        string fileNameProductName = productName.TrimEnd('.');

        return $"DeviceBindings_{profile.DurableDeviceKey}_{fileNameProductName}.json";
    }

    private static string BuildProfileJson(DeviceBindingProfile profile)
    {
        var sb = new StringBuilder();

        sb.AppendLine("{");
        WriteProperty(sb, 1, "schema_version", 1, comma: true);
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

        WriteAxisBindings(sb, profile.AxisBindings, comma: true);
        WriteAircraftProfiles(sb, profile.AircraftProfiles, comma: false);

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void WriteAxisBindings(StringBuilder sb, IReadOnlyList<DeviceAxisBinding> axisBindings, bool comma)
    {
        Indent(sb, 1);
        sb.AppendLine("\"axis_bindings\": [");

        for (int i = 0; i < axisBindings.Count; i++)
        {
            DeviceAxisBinding axis = axisBindings[i];
            bool isLast = i == axisBindings.Count - 1;

            Indent(sb, 2);
            sb.AppendLine("{");

            WriteProperty(sb, 3, "logical_axis_name", axis.LogicalAxisName, comma: true);
            WriteProperty(sb, 3, "physical_axis_index", axis.PhysicalAxisIndex, comma: true);
            WriteProperty(sb, 3, "saturation", axis.Saturation, comma: true);
            WriteProperty(sb, 3, "deadzone", axis.Deadzone, comma: true);

            if (string.Equals(axis.LogicalAxisName, "Throttle", StringComparison.OrdinalIgnoreCase))
            {
                WriteProperty(sb, 3, "invert", axis.Invert, comma: true);
                WriteProperty(sb, 3, "afterburner_detent", axis.AfterburnerDetent, comma: true);
                WriteProperty(sb, 3, "idle_detent", axis.IdleDetent, comma: false);
            }
            else
            {
                WriteProperty(sb, 3, "invert", axis.Invert, comma: false);
            }

            Indent(sb, 2);
            sb.Append('}');
            if (!isLast)
                sb.Append(',');

            sb.AppendLine();
        }

        Indent(sb, 1);
        sb.Append(']');
        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void WriteAircraftProfiles(StringBuilder sb, IReadOnlyList<DeviceAircraftBindingProfile> aircraftProfiles, bool comma)
    {
        Indent(sb, 1);
        sb.AppendLine("\"aircraft_profiles\": [");

        for (int i = 0; i < aircraftProfiles.Count; i++)
        {
            DeviceAircraftBindingProfile aircraft = aircraftProfiles[i];
            bool isLastAircraft = i == aircraftProfiles.Count - 1;

            Indent(sb, 2);
            sb.AppendLine("{");

            WriteProperty(sb, 3, "aircraft_profile", aircraft.AircraftProfile, comma: true);
            WriteButtonBindings(sb, aircraft.ButtonBindings, comma: true);
            WritePovBindings(sb, aircraft.PovBindings, comma: false);

            Indent(sb, 2);
            sb.Append('}');
            if (!isLastAircraft)
                sb.Append(',');

            sb.AppendLine();
        }

        Indent(sb, 1);
        sb.Append(']');
        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void WriteButtonBindings(StringBuilder sb, IReadOnlyList<DeviceButtonBinding> buttonBindings, bool comma)
    {
        Indent(sb, 3);
        sb.AppendLine("\"button_bindings\": [");

        for (int i = 0; i < buttonBindings.Count; i++)
        {
            DeviceButtonBinding button = buttonBindings[i];
            bool isLast = i == buttonBindings.Count - 1;

            Indent(sb, 4);
            sb.AppendLine("{");

            WriteProperty(sb, 5, "button_index", button.ButtonIndex, comma: true);
            WriteProperty(sb, 5, "assignment_index", button.AssignmentIndex, comma: true);
            WriteProperty(sb, 5, "shift_state", DeviceButtonBinding.GetShiftState(button.AssignmentIndex), comma: true);
            WriteProperty(sb, 5, "trigger", DeviceButtonBinding.GetTrigger(button.AssignmentIndex), comma: true);
            WriteProperty(sb, 5, "callback_name", button.CallbackName, comma: true);
            WriteProperty(sb, 5, "invoke", button.Invoke, comma: true);
            WriteProperty(sb, 5, "sound_id", button.SoundId, comma: false);

            Indent(sb, 4);
            sb.Append('}');
            if (!isLast)
                sb.Append(',');

            sb.AppendLine();
        }

        Indent(sb, 3);
        sb.Append(']');
        if (comma)
            sb.Append(',');

        sb.AppendLine();
    }

    private static void WritePovBindings(StringBuilder sb, IReadOnlyList<DevicePovBinding> povBindings, bool comma)
    {
        Indent(sb, 3);
        sb.AppendLine("\"pov_bindings\": [");

        for (int i = 0; i < povBindings.Count; i++)
        {
            DevicePovBinding pov = povBindings[i];
            bool isLast = i == povBindings.Count - 1;

            Indent(sb, 4);
            sb.AppendLine("{");

            WriteProperty(sb, 5, "pov_index", pov.PovIndex, comma: true);
            WriteProperty(sb, 5, "direction", pov.Direction, comma: true);
            WriteProperty(sb, 5, "callback_name", pov.CallbackName, comma: true);
            WriteProperty(sb, 5, "invoke", pov.Invoke, comma: true);
            WriteProperty(sb, 5, "sound_id", pov.SoundId, comma: false);

            Indent(sb, 4);
            sb.Append('}');
            if (!isLast)
                sb.Append(',');

            sb.AppendLine();
        }

        Indent(sb, 3);
        sb.Append(']');
        if (comma)
            sb.Append(',');

        sb.AppendLine();
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

    private static void WriteProperty(StringBuilder sb, int indentLevel, string name, int? value, bool comma)
    {
        Indent(sb, indentLevel);
        sb.Append('"');
        sb.Append(EscapeJson(name));
        sb.Append("\": ");
        sb.Append(value.HasValue ? value.Value.ToString() : "null");

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

    private static string? FormatGuid(Guid? value)
    {
        return value.HasValue ? value.Value.ToString("B") : null;
    }

    private static string SanitizeFileNameSegment(string value)
    {
        string sanitized = value.Trim();

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            sanitized = sanitized.Replace(invalidChar, '_');

        return sanitized;
    }

    private static void Indent(StringBuilder sb, int indentLevel)
    {
        sb.Append(' ', indentLevel * 2);
    }

    private static void WritePropertyNullableString(StringBuilder sb, int indentLevel, string name, string? value, bool comma)
    {
        Indent(sb, indentLevel);
        sb.Append('"');
        sb.Append(EscapeJson(name));
        sb.Append("\": ");

        if (value == null)
            sb.Append("null");
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