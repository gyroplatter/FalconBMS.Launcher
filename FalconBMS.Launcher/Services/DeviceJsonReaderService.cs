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

    public IReadOnlyList<DeviceBindingProfile> LoadOrBuild(
        string baseDir,
        IReadOnlyList<StockDeviceSetupMatch> matches)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("JSONHOTASREAD");
        DebugDiagnosticsService.Info($"Device JSON read begin. DetectedDeviceCount={matches.Count} | ActionId={actionId}");

        string jsonDir = Path.Combine(baseDir, "User", "Config", "JSON");
        var profiles = new List<DeviceBindingProfile>();
        Dictionary<string, SavedDeviceJson> savedProfilesByDurableKey = LoadSavedDeviceJson(jsonDir, actionId);
        var matchedSavedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (StockDeviceSetupMatch match in matches)
        {
            string durableDeviceKey = match.Device.DurableDeviceKey;

            if (!savedProfilesByDurableKey.TryGetValue(durableDeviceKey, out SavedDeviceJson? savedProfile))
            {
                profiles.Add(BuildFallbackProfile(match, actionId));
                continue;
            }

            try
            {
                DeviceBindingProfile profile = CreateConnectedProfileFromJson(match.Device, savedProfile.Path, savedProfile.Document, actionId);
                profiles.Add(profile);
                matchedSavedKeys.Add(durableDeviceKey);

                LogLoadedProfile(profile, Path.GetFileName(savedProfile.Path), actionId);
            }
            catch (Exception ex)
            {
                DebugDiagnosticsService.Exception(ex, $"Device JSON read failed: {savedProfile.Path}");
                profiles.Add(BuildFallbackProfile(match, actionId));
                matchedSavedKeys.Add(durableDeviceKey);
            }
        }

        foreach (SavedDeviceJson savedProfile in savedProfilesByDurableKey.Values.OrderBy(profile => Path.GetFileName(profile.Path), StringComparer.OrdinalIgnoreCase))
        {
            string durableDeviceKey = GetDocumentDurableDeviceKey(savedProfile.Document);

            if (string.IsNullOrWhiteSpace(durableDeviceKey) || matchedSavedKeys.Contains(durableDeviceKey))
                continue;

            try
            {
                DeviceBindingProfile offlineProfile = CreateOfflineProfileFromJson(savedProfile.Path, savedProfile.Document);
                profiles.Add(offlineProfile);

                DebugDiagnosticsService.Warn(
                    $"Saved device profile is offline and was retained | Device=\"{offlineProfile.ProductName}\" | PIDVID={offlineProfile.PidVid} | DurableKey={offlineProfile.DurableDeviceKey} | Json=\"{Path.GetFileName(savedProfile.Path)}\" | LastSeenInstanceGuid={FormatGuid(offlineProfile.LastSeenInstanceGuid)} | ActionId={actionId}");
            }
            catch (Exception ex)
            {
                DebugDiagnosticsService.Exception(ex, $"Offline device JSON read failed: {savedProfile.Path}");
            }
        }

        DebugDiagnosticsService.Info($"Device JSON read end. LoadedOrBuilt={profiles.Count} | Connected={profiles.Count(profile => profile.IsConnected)} | Offline={profiles.Count(profile => !profile.IsConnected)} | ActionId={actionId}");
        return profiles;
    }

    private Dictionary<string, SavedDeviceJson> LoadSavedDeviceJson(string jsonDir, string actionId)
    {
        var savedProfilesByDurableKey = new Dictionary<string, SavedDeviceJson>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(jsonDir))
            return savedProfilesByDurableKey;

        foreach (string path in Directory.GetFiles(jsonDir, "DeviceBindings_*.json").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                JsonDeviceBindingDocument? document = ReadDocument(path);

                if (document is null)
                {
                    DebugDiagnosticsService.Warn($"Device JSON read skipped. Empty document: {path} | ActionId={actionId}");
                    continue;
                }

                string durableDeviceKey = GetDocumentDurableDeviceKey(document);

                if (string.IsNullOrWhiteSpace(durableDeviceKey))
                {
                    DebugDiagnosticsService.Warn($"Device JSON read skipped. Missing durable_device_key/pidvid: {path} | ActionId={actionId}");
                    continue;
                }

                if (savedProfilesByDurableKey.ContainsKey(durableDeviceKey))
                {
                    DebugDiagnosticsService.Warn(
                        $"Duplicate DeviceBindings JSON durable key. Keeping first file. DurableKey={durableDeviceKey} | Skipped=\"{Path.GetFileName(path)}\" | ActionId={actionId}");
                    continue;
                }

                savedProfilesByDurableKey[durableDeviceKey] = new SavedDeviceJson(path, document);
            }
            catch (Exception ex)
            {
                DebugDiagnosticsService.Exception(ex, $"Device JSON read failed: {path}");
            }
        }

        return savedProfilesByDurableKey;
    }

    private DeviceBindingProfile BuildFallbackProfile(StockDeviceSetupMatch match, string actionId)
    {
        DebugDiagnosticsService.Info(
            $"Device JSON missing. Falling back to stock XML/empty model | Device=\"{match.Device.ProductName}\" | DurableKey={match.Device.DurableDeviceKey} | InstanceGuid={match.Device.InstanceGuid:B} | ActionId={actionId}");

        return _fallbackBuilder.Build(new[] { match }).First();
    }

    private static JsonDeviceBindingDocument? ReadDocument(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        using var stream = new MemoryStream(bytes);

        var serializer = new DataContractJsonSerializer(typeof(JsonDeviceBindingDocument));
        return serializer.ReadObject(stream) as JsonDeviceBindingDocument;
    }

    private DeviceBindingProfile CreateConnectedProfileFromJson(
        InputDeviceInfo device,
        string jsonPath,
        JsonDeviceBindingDocument document,
        string actionId)
    {
        Guid? previousInstanceGuid = ParseGuid(document.LastSeenInstanceGuid);

        if (previousInstanceGuid.HasValue && previousInstanceGuid.Value != device.InstanceGuid)
        {
            DebugDiagnosticsService.Warn(
                $"Device InstanceGuid changed. Reconciled by DurableDeviceKey | Device=\"{device.ProductName}\" | DurableKey={device.DurableDeviceKey} | Previous={previousInstanceGuid.Value:B} | Current={device.InstanceGuid:B} | Json=\"{Path.GetFileName(jsonPath)}\" | ActionId={actionId}");
        }

        var profile = new DeviceBindingProfile
        {
            DiscoveryIndex = device.DiscoveryIndex,
            InstanceGuid = device.InstanceGuid,
            LastSeenInstanceGuid = device.InstanceGuid,
            IsConnected = true,
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

    private DeviceBindingProfile CreateOfflineProfileFromJson(
        string jsonPath,
        JsonDeviceBindingDocument document)
    {
        string pidVid = NormalizeHex(document.PidVid);
        string vendorIdHex = NormalizeHex(document.VendorIdHex);
        string productIdHex = NormalizeHex(document.ProductIdHex);

        if (string.IsNullOrWhiteSpace(pidVid))
            pidVid = ParseDurableKeyPrefix(GetDocumentDurableDeviceKey(document));

        if ((string.IsNullOrWhiteSpace(productIdHex) || string.IsNullOrWhiteSpace(vendorIdHex)) && pidVid.Length >= 8)
        {
            productIdHex = pidVid.Substring(0, 4);
            vendorIdHex = pidVid.Substring(4, 4);
        }

        Guid? lastSeenInstanceGuid = ParseGuid(document.LastSeenInstanceGuid);

        var profile = new DeviceBindingProfile
        {
            DiscoveryIndex = int.MaxValue,
            InstanceGuid = lastSeenInstanceGuid ?? Guid.Empty,
            LastSeenInstanceGuid = lastSeenInstanceGuid,
            IsConnected = false,
            ProductGuid = Guid.Empty,
            InstanceName = document.InstanceName ?? "",
            ProductName = document.ProductName ?? Path.GetFileNameWithoutExtension(jsonPath),
            VendorIdHex = vendorIdHex,
            ProductIdHex = productIdHex,
            DuplicatePidVidSequenceNumber = document.DuplicatePidVidSequenceNumber,
            AxisCount = document.AxisCount,
            ButtonCount = document.ButtonCount,
            PovCount = document.PovCount,
            CapabilitiesReadSuccessfully = false,
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
        var jsonAxisByLogicalName = new Dictionary<string, JsonDeviceAxisBinding>(StringComparer.OrdinalIgnoreCase);

        if (axisBindings is not null)
        {
            foreach (JsonDeviceAxisBinding axis in axisBindings)
            {
                string logicalAxisName = AxisDefinitionService.NormalizeLogicalAxisName(axis.LogicalAxisName ?? "");

                if (string.IsNullOrWhiteSpace(logicalAxisName))
                    continue;

                // Canonicalize older/transitional names while loading. If an older JSON
                // somehow contains both the transitional and current name, the last value
                // wins and the writer will output one clean row.
                jsonAxisByLogicalName[logicalAxisName] = axis;
            }
        }

        // Always rebuild axis bindings in AxisDefinitionService order.
        // This guarantees the in-memory model has the current full 30-axis table
        // even when loading an older partial JSON file.
        foreach (DeviceAxisDefinition definition in AxisDefinitionService.GetDefinitions())
        {
            jsonAxisByLogicalName.TryGetValue(definition.LogicalAxisName, out JsonDeviceAxisBinding? axis);

            profile.AxisBindings.Add(new DeviceAxisBinding
            {
                LogicalAxisName = definition.LogicalAxisName,
                PhysicalAxisIndex = axis?.PhysicalAxisIndex,
                Saturation = axis?.Saturation ?? "None",
                Deadzone = axis?.Deadzone ?? "None",
                Invert = axis?.Invert.GetValueOrDefault() ?? false,
                AfterburnerDetent = axis?.AfterburnerDetent,
                IdleDetent = axis?.IdleDetent
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
                AircraftProfile = string.IsNullOrWhiteSpace(jsonAircraft.AircraftProfile)
                    ? "F-16"
                    : jsonAircraft.AircraftProfile!
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

    private static void LogLoadedProfile(DeviceBindingProfile profile, string jsonFileName, string actionId)
    {
        int assignedAxes = profile.AxisBindings.Count(axis => axis.PhysicalAxisIndex.HasValue);
        int buttonBindings = profile.AircraftProfiles.Sum(a => a.ButtonBindings.Count);
        int povBindings = profile.AircraftProfiles.Sum(a => a.PovBindings.Count);

        DebugDiagnosticsService.Info(
            $"Device JSON loaded | Device=\"{profile.ProductName}\" | PIDVID={profile.PidVid} | DurableKey={profile.DurableDeviceKey} | Connected={profile.IsConnected} | " +
            $"InstanceGuid={profile.InstanceGuid:B} | Json=\"{jsonFileName}\" | AxisBindings={profile.AxisBindings.Count} | AssignedAxes={assignedAxes} | " +
            $"ButtonBindings={buttonBindings} | PovBindings={povBindings} | AircraftProfiles={profile.AircraftProfiles.Count} | ActionId={actionId}");
    }

    private static string GetDocumentDurableDeviceKey(JsonDeviceBindingDocument document)
    {
        string durableDeviceKey = document.DurableDeviceKey ?? "";

        if (!string.IsNullOrWhiteSpace(durableDeviceKey))
            return durableDeviceKey;

        string pidVidValue = document.PidVid ?? "";

        if (string.IsNullOrWhiteSpace(pidVidValue))
            return "";

        string pidVid = NormalizeHex(pidVidValue);

        return document.DuplicatePidVidSequenceNumber.HasValue
            ? $"{pidVid}_{document.DuplicatePidVidSequenceNumber.Value}"
            : pidVid;
    }

    private static string ParseDurableKeyPrefix(string durableDeviceKey)
    {
        if (string.IsNullOrWhiteSpace(durableDeviceKey))
            return "";

        int underscoreIndex = durableDeviceKey.IndexOf('_');
        string prefix = underscoreIndex >= 0
            ? durableDeviceKey.Substring(0, underscoreIndex)
            : durableDeviceKey;

        return NormalizeHex(prefix);
    }

    private static string NormalizeHex(string? value)
    {
        string safeValue = value ?? "";

        if (string.IsNullOrWhiteSpace(safeValue))
            return "";

        return safeValue.Trim().ToUpperInvariant();
    }

    private static Guid? ParseGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Guid.TryParse(value, out Guid guid)
            ? guid
            : null;
    }

    private static string FormatGuid(Guid? guid)
    {
        return guid.HasValue ? guid.Value.ToString("B") : "";
    }

    private sealed class SavedDeviceJson
    {
        public SavedDeviceJson(string path, JsonDeviceBindingDocument document)
        {
            Path = path;
            Document = document;
        }

        public string Path { get; }
        public JsonDeviceBindingDocument Document { get; }
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

        [DataMember(Name = "axis_count")]
        public int AxisCount { get; set; }

        [DataMember(Name = "button_count")]
        public int ButtonCount { get; set; }

        [DataMember(Name = "pov_count")]
        public int PovCount { get; set; }

        [DataMember(Name = "capabilities_read_successfully")]
        public bool CapabilitiesReadSuccessfully { get; set; }

        [DataMember(Name = "last_seen_instance_guid")]
        public string? LastSeenInstanceGuid { get; set; }

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