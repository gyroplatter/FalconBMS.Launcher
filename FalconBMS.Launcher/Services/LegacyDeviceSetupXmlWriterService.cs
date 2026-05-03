using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FalconBMS.Launcher.Services;

public sealed class LegacyDeviceSetupXmlWriterService
{
    private const string DoNothingCallback = "SimDoNothing";
    private const string DefaultInvoke = "Default";

    private static readonly Regex OldNameSanitizeRx =
        new(@"[^A-Za-z0-9\~\`\[\]\{\}\-_\=\'\x20]", RegexOptions.Compiled);

    public void Write(string baseDir, IReadOnlyList<DeviceBindingProfile> deviceProfiles)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("SETUPXML");

        string configDir = Path.Combine(baseDir, "User", "Config");
        Directory.CreateDirectory(configDir);

        foreach (DeviceBindingProfile profile in deviceProfiles)
            WriteProfile(configDir, profile, actionId);
    }

    private static void WriteProfile(string configDir, DeviceBindingProfile profile, string actionId)
    {
        string fileName = BuildFileName(profile);
        string path = Path.Combine(configDir, fileName);

        string beforeSignature = DebugDiagnosticsService.GetFileSignature(path);
        string content = BuildXml(profile);

        if (File.Exists(path))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);

        if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        DebugDiagnosticsService.LogFileWriteResult(
            fileName,
            path,
            beforeSignature,
            "LegacyDeviceSetupXmlWriterService.WriteProfile",
            profile.ProductName,
            actionId);
    }

    private static string BuildFileName(DeviceBindingProfile profile)
    {
        string safeDeviceName = SanitizeDeviceName(profile.ProductName);

        return $"Setup.v100.{safeDeviceName} {{{profile.InstanceGuid.ToString().ToUpperInvariant()}}}.xml";
    }

    private static string BuildXml(DeviceBindingProfile profile)
    {
        XElement root = new(
            "JoyAssgn",
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
            BuildDetentPosition(profile),
            BuildAxisBlock(profile),

            // Legacy root POV/DX blocks carry the active/default F-16 bindings.
            BuildPovBlock(GetAircraftProfile(profile, "F-16")),
            BuildDxBlock(GetAircraftProfile(profile, "F-16")),

            new XElement(
                "profileDefaultF16",
                BuildPovBlock(GetAircraftProfile(profile, "F-16")),
                BuildDxBlock(GetAircraftProfile(profile, "F-16"))),
            new XElement(
                "profileF15ABCD",
                BuildPovBlock(GetAircraftProfile(profile, "F-15ABCD")),
                BuildDxBlock(GetAircraftProfile(profile, "F-15ABCD")))
        );

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = false,
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace
        };

        using var stream = new MemoryStream();

        using (XmlWriter writer = XmlWriter.Create(stream, settings))
            document.Save(writer);

        return settings.Encoding.GetString(stream.ToArray());
    }

    private static XElement BuildDetentPosition(DeviceBindingProfile profile)
    {
        DeviceAxisBinding? throttle = profile.AxisBindings.FirstOrDefault(axis =>
            string.Equals(axis.LogicalAxisName, "Throttle", StringComparison.OrdinalIgnoreCase));

        return new XElement(
            "detentPosition",
            new XElement("AB", throttle?.AfterburnerDetent ?? DetentPosition.DefaultAfterburnerDetent),
            new XElement("IDLE", throttle?.IdleDetent ?? DetentPosition.DefaultIdleDetent));
    }

    private static XElement BuildAxisBlock(DeviceBindingProfile profile)
    {
        var byPhysicalAxis = profile.AxisBindings
            .Where(axis => axis.PhysicalAxisIndex is >= 0 and < 8)
            .GroupBy(axis => axis.PhysicalAxisIndex!.Value)
            .ToDictionary(group => group.Key, group => group.First());

        var axisElements = new List<XElement>();

        for (int physicalAxisIndex = 0; physicalAxisIndex < 8; physicalAxisIndex++)
        {
            byPhysicalAxis.TryGetValue(physicalAxisIndex, out DeviceAxisBinding? axis);

            axisElements.Add(
            new XElement(
                "AxAssgn",
                axis is null || string.IsNullOrWhiteSpace(axis.LogicalAxisName)
                    ? new XElement("AxisName")
                    : new XElement("AxisName", axis.LogicalAxisName),
                new XElement("AssgnDate", "1998-12-12T12:00:00"),
                new XElement("Invert", axis?.Invert.ToString().ToLowerInvariant() ?? "false"),
                new XElement("Saturation", axis?.Saturation ?? "None"),
                new XElement("Deadzone", axis?.Deadzone ?? "None")));
        }

        return new XElement("axis", axisElements);
    }

    private static XElement BuildPovBlock(DeviceAircraftBindingProfile? aircraftProfile)
    {
        var povBindings = aircraftProfile?.PovBindings ?? new List<DevicePovBinding>();

        XElement MakeDirAssgn(int povIndex, int direction)
        {
            DevicePovBinding? unshifted = povBindings.FirstOrDefault(pov =>
                pov.PovIndex == povIndex &&
                pov.Direction == direction &&
                string.Equals(pov.Invoke, "Default", StringComparison.OrdinalIgnoreCase));

            DevicePovBinding? shifted = povBindings.FirstOrDefault(pov =>
                pov.PovIndex == povIndex &&
                pov.Direction == direction &&
                string.Equals(pov.Invoke, "Shift", StringComparison.OrdinalIgnoreCase));

            string unshiftedCallback = string.IsNullOrWhiteSpace(unshifted?.CallbackName)
                ? DoNothingCallback
                : unshifted!.CallbackName;

            string shiftedCallback = string.IsNullOrWhiteSpace(shifted?.CallbackName)
                ? DoNothingCallback
                : shifted!.CallbackName;

            int unshiftedSoundId = unshifted?.SoundId ?? 0;
            int shiftedSoundId = shifted?.SoundId ?? 0;

            return new XElement(
                "DirAssgn",
                new XElement(
                    "Callback",
                    new XElement("string", unshiftedCallback),
                    new XElement("string", shiftedCallback)),
                new XElement(
                    "SoundID",
                    new XElement("int", unshiftedSoundId.ToString()),
                    new XElement("int", shiftedSoundId.ToString())));
        }

        XElement MakePovAssgn(int povIndex)
        {
            return new XElement(
                "PovAssgn",
                new XElement(
                    "direction",
                    Enumerable.Range(0, 8).Select(direction => MakeDirAssgn(povIndex, direction))));
        }

        return new XElement(
            "pov",
            Enumerable.Range(0, 4).Select(MakePovAssgn));
    }

    private static XElement BuildDxBlock(DeviceAircraftBindingProfile? aircraftProfile)
    {
        var buttonBindings = aircraftProfile?.ButtonBindings ?? new List<DeviceButtonBinding>();

        XElement MakeAssgn(int buttonIndex, int assignmentIndex)
        {
            DeviceButtonBinding? binding = buttonBindings.FirstOrDefault(button =>
                button.ButtonIndex == buttonIndex && button.AssignmentIndex == assignmentIndex);

            return new XElement(
                "Assgn",
                new XElement("Callback", string.IsNullOrWhiteSpace(binding?.CallbackName) ? DoNothingCallback : binding!.CallbackName),
                new XElement("Invoke", string.IsNullOrWhiteSpace(binding?.Invoke) ? DefaultInvoke : binding!.Invoke),
                new XElement("SoundID", (binding?.SoundId ?? 0).ToString()));
        }

        XElement MakeDxAssgn(int buttonIndex)
        {
            return new XElement(
                "DxAssgn",
                new XElement(
                    "assign",
                    Enumerable.Range(0, 4).Select(assignmentIndex => MakeAssgn(buttonIndex, assignmentIndex))));
        }

        return new XElement(
            "dx",
            Enumerable.Range(0, 128).Select(MakeDxAssgn));
    }

    private static DeviceAircraftBindingProfile? GetAircraftProfile(DeviceBindingProfile profile, string aircraftProfile)
    {
        return profile.AircraftProfiles.FirstOrDefault(aircraft =>
            string.Equals(aircraft.AircraftProfile, aircraftProfile, StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitizeDeviceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return OldNameSanitizeRx.Replace(value, "").Trim();
    }
}