using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FalconBMS.Launcher.Services;

public sealed class DeviceJsonReaderService
{
    private readonly DeviceBindingProfileBuilderService _fallbackBuilder = new();
    private readonly AxisDefinitionService _axisDefinitions = new();

    public IReadOnlyList<DeviceBindingProfile> LoadOrBuild(
        string baseDir,
        IReadOnlyList<StockDeviceSetupMatch> matches)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("JSONHOTASREAD");
        DebugDiagnosticsService.Info($"Device JSON read begin. DeviceCount={matches.Count} | ActionId={actionId}");

        string jsonDir = Path.Combine(baseDir, "User", "Config", "JSON");
        var profiles = new List<DeviceBindingProfile>();

        foreach (StockDeviceSetupMatch match in matches)
        {
            string? jsonPath = FindDeviceJsonPath(jsonDir, match.Device.DurableDeviceKey);

            if (jsonPath is null)
            {
                profiles.Add(BuildFallbackProfile(match, actionId));
                continue;
            }

            try
            {
                JsonDeviceBindingDocument? document = ReadDocument(jsonPath);

                if (document is null)
                {
                    DebugDiagnosticsService.Warn($"Device JSON read skipped. Empty document: {jsonPath} | ActionId={actionId}");
                    profiles.Add(BuildFallbackProfile(match, actionId));
                    continue;
                }

                DeviceBindingProfile profile = CreateProfileFromJson(match.Device, jsonPath, document);

                profiles.Add(profile);

                int assignedAxes = profile.AxisBindings.Count(axis => axis.PhysicalAxisIndex.HasValue);
                int buttonBindings = profile.AircraftProfiles.Sum(a => a.ButtonBindings.Count);
                int povBindings = profile.AircraftProfiles.Sum(a => a.PovBindings.Count);

                DebugDiagnosticsService.Info(
                    $"Device JSON loaded | Device=\"{profile.ProductName}\" | PIDVID={profile.PidVid} | DurableKey={profile.DurableDeviceKey} | " +
                    $"Json=\"{Path.GetFileName(jsonPath)}\" | AxisBindings={profile.AxisBindings.Count} | AssignedAxes={assignedAxes} | " +
                    $"ButtonBindings={buttonBindings} | PovBindings={povBindings} | AircraftProfiles={profile.AircraftProfiles.Count} | ActionId={actionId}");
            }
            catch (Exception ex)
            {
                DebugDiagnosticsService.Exception(ex, $"Device JSON read failed: {jsonPath}");
                profiles.Add(BuildFallbackProfile(match, actionId));
            }
        }

        DebugDiagnosticsService.Info($"Device JSON read end. LoadedOrBuilt={profiles.Count} | ActionId={actionId}");
        return profiles;
    }

    private DeviceBindingProfile BuildFallbackProfile(StockDeviceSetupMatch match, string actionId)
    {
        DebugDiagnosticsService.Info(
            $"Device JSON missing. Falling back to stock XML/empty model | Device=\"{match.Device.ProductName}\" | DurableKey={match.Device.DurableDeviceKey} | ActionId={actionId}");

        return _fallbackBuilder.Build(new[] { match }).First();
    }

    private static string? FindDeviceJsonPath(string configDir, string durableDeviceKey)
    {
        if (!Directory.Exists(configDir))
            return null;

        string prefix = $"DevicesBinding_{durableDeviceKey}_";

        return Directory
            .GetFiles(configDir, prefix + "*.json")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static JsonDeviceBindingDocument? ReadDocument(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        using var stream = new MemoryStream(bytes);

        var serializer = new DataContractJsonSerializer(typeof(JsonDeviceBindingDocument));
        return serializer.ReadObject(stream) as JsonDeviceBindingDocument;
    }

    private DeviceBindingProfile CreateProfileFromJson(
        InputDeviceInfo device,
        string jsonPath,
        JsonDeviceBindingDocument document)
    {
        var profile = new DeviceBindingProfile
        {
            DiscoveryIndex = device.DiscoveryIndex,
            InstanceGuid = device.InstanceGuid,
            ProductGuid = device.ProductGuid,
            InstanceName = device.InstanceName,
            ProductName = device.ProductName,
            VendorIdHex = device.VendorIdHex,
            ProductIdHex = device.ProductIdHex,
            DuplicatePidVidSequenceNumber = device.DuplicatePidVidSequenceNumber,
            AxisCount = device.Capabilities.AxisCount,
            ButtonCount = device.Capabilities.ButtonCount,
            PovCount = device.Capabilities.PovCount,
            CapabilitiesReadSuccessfully = device.Capabilities.WasReadSuccessfully,
            Source = DeviceBindingSource.Json,
            StockXmlPath = null,
            JsonPath = jsonPath
        };

        ApplyAxisBindings(profile, document.AxisBindings);
        ApplyAircraftProfiles(profile, document.AircraftProfiles);

        return profile;
    }

    private void ApplyAxisBindings(
        DeviceBindingProfile profile,
        List<JsonDeviceAxisBinding>? axisBindings)
    {
        if (axisBindings is null || axisBindings.Count == 0)
        {
            foreach (DeviceAxisDefinition definition in _axisDefinitions.GetDefinitions())
            {
                profile.AxisBindings.Add(new DeviceAxisBinding
                {
                    LogicalAxisName = definition.LogicalAxisName,
                    PhysicalAxisIndex = null
                });
            }

            return;
        }

        foreach (JsonDeviceAxisBinding axis in axisBindings)
        {
            string logicalAxisName = AxisDefinitionService.NormalizeLogicalAxisName(axis.LogicalAxisName ?? "");

            profile.AxisBindings.Add(new DeviceAxisBinding
            {
                LogicalAxisName = logicalAxisName,
                PhysicalAxisIndex = axis.PhysicalAxisIndex,
                Saturation = axis.Saturation ?? "None",
                Deadzone = axis.Deadzone ?? "None",
                Invert = axis.Invert.GetValueOrDefault(),
                AfterburnerDetent = axis.AfterburnerDetent,
                IdleDetent = axis.IdleDetent
            });
        }

        // Older JSON files may only contain the first partial axis set.
        // Add any newly-supported Falcon logical axes as unmapped rows so the UI and
        // regenerated JSON always converge to the full 30-axis model.
        foreach (DeviceAxisDefinition definition in _axisDefinitions.GetDefinitions())
        {
            bool alreadyExists = profile.AxisBindings.Any(axis =>
                string.Equals(axis.LogicalAxisName, definition.LogicalAxisName, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
                continue;

            profile.AxisBindings.Add(new DeviceAxisBinding
            {
                LogicalAxisName = definition.LogicalAxisName,
                PhysicalAxisIndex = null
            });
        }
    }

    private static void ApplyAircraftProfiles(
        DeviceBindingProfile profile,
        List<JsonDeviceAircraftProfile>? aircraftProfiles)
    {
        if (aircraftProfiles is null || aircraftProfiles.Count == 0)
        {
            profile.AircraftProfiles.Add(new DeviceAircraftBindingProfile
            {
                AircraftProfile = "F-16"
            });

            profile.AircraftProfiles.Add(new DeviceAircraftBindingProfile
            {
                AircraftProfile = "F-15ABCD"
            });

            return;
        }

        foreach (JsonDeviceAircraftProfile jsonAircraft in aircraftProfiles)
        {
            var aircraft = new DeviceAircraftBindingProfile
            {
                AircraftProfile = jsonAircraft.AircraftProfile ?? ""
            };

            if (jsonAircraft.ButtonBindings is not null)
            {
                foreach (JsonDeviceButtonBinding button in jsonAircraft.ButtonBindings)
                {
                    aircraft.ButtonBindings.Add(new DeviceButtonBinding
                    {
                        ButtonIndex = button.ButtonIndex.GetValueOrDefault(),
                        AssignmentIndex = button.AssignmentIndex.GetValueOrDefault(),
                        CallbackName = button.CallbackName ?? "",
                        Invoke = button.Invoke ?? "Default",
                        SoundId = button.SoundId.GetValueOrDefault()
                    });
                }
            }

            if (jsonAircraft.PovBindings is not null)
            {
                foreach (JsonDevicePovBinding pov in jsonAircraft.PovBindings)
                {
                    aircraft.PovBindings.Add(new DevicePovBinding
                    {
                        PovIndex = pov.PovIndex.GetValueOrDefault(),
                        Direction = pov.Direction.GetValueOrDefault(),
                        CallbackName = pov.CallbackName ?? "",
                        Invoke = pov.Invoke ?? "Default",
                        SoundId = pov.SoundId.GetValueOrDefault()
                    });
                }
            }

            profile.AircraftProfiles.Add(aircraft);
        }
    }

    [DataContract]
    private sealed class JsonDeviceBindingDocument
    {
        [DataMember(Name = "schema_version")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "binding_type")]
        public string? BindingType { get; set; }

        [DataMember(Name = "durable_device_key")]
        public string? DurableDeviceKey { get; set; }

        [DataMember(Name = "pidvid")]
        public string? PidVid { get; set; }

        [DataMember(Name = "product_name")]
        public string? ProductName { get; set; }

        [DataMember(Name = "instance_name")]
        public string? InstanceName { get; set; }

        [DataMember(Name = "vendor_id_hex")]
        public string? VendorIdHex { get; set; }

        [DataMember(Name = "product_id_hex")]
        public string? ProductIdHex { get; set; }

        [DataMember(Name = "duplicate_pidvid_sequence_number")]
        public int? DuplicatePidVidSequenceNumber { get; set; }

        [DataMember(Name = "axis_bindings")]
        public List<JsonDeviceAxisBinding>? AxisBindings { get; set; }

        [DataMember(Name = "aircraft_profiles")]
        public List<JsonDeviceAircraftProfile>? AircraftProfiles { get; set; }
    }

    [DataContract]
    private sealed class JsonDeviceAxisBinding
    {
        [DataMember(Name = "logical_axis_name")]
        public string? LogicalAxisName { get; set; }

        [DataMember(Name = "physical_axis_index")]
        public int? PhysicalAxisIndex { get; set; }

        [DataMember(Name = "saturation")]
        public string? Saturation { get; set; }

        [DataMember(Name = "deadzone")]
        public string? Deadzone { get; set; }

        [DataMember(Name = "invert")]
        public bool? Invert { get; set; }

        [DataMember(Name = "afterburner_detent")]
        public int? AfterburnerDetent { get; set; }

        [DataMember(Name = "idle_detent")]
        public int? IdleDetent { get; set; }
    }

    [DataContract]
    private sealed class JsonDeviceAircraftProfile
    {
        [DataMember(Name = "aircraft_profile")]
        public string? AircraftProfile { get; set; }

        [DataMember(Name = "button_bindings")]
        public List<JsonDeviceButtonBinding>? ButtonBindings { get; set; }

        [DataMember(Name = "pov_bindings")]
        public List<JsonDevicePovBinding>? PovBindings { get; set; }
    }

    [DataContract]
    private sealed class JsonDeviceButtonBinding
    {
        [DataMember(Name = "button_index")]
        public int? ButtonIndex { get; set; }

        [DataMember(Name = "assignment_index")]
        public int? AssignmentIndex { get; set; }

        [DataMember(Name = "callback_name")]
        public string? CallbackName { get; set; }

        [DataMember(Name = "invoke")]
        public string? Invoke { get; set; }

        [DataMember(Name = "sound_id")]
        public int? SoundId { get; set; }
    }

    [DataContract]
    private sealed class JsonDevicePovBinding
    {
        [DataMember(Name = "pov_index")]
        public int? PovIndex { get; set; }

        [DataMember(Name = "direction")]
        public int? Direction { get; set; }

        [DataMember(Name = "callback_name")]
        public string? CallbackName { get; set; }

        [DataMember(Name = "invoke")]
        public string? Invoke { get; set; }

        [DataMember(Name = "sound_id")]
        public int? SoundId { get; set; }
    }
}