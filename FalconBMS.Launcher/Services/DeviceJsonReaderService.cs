using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace FalconBMS.Launcher.Services;

public sealed class DeviceJsonReaderService
{
    // Axis bindings are shared across aircraft profiles.
    // This aircraft profile supplies the authoritative axis block if files differ.
    private const string PrimarySharedAxisAircraftProfile = "F-16";

    private readonly DeviceBindingProfileBuilderService _fallbackBuilder = new();

    /// <summary>
    /// True when one or more device JSON files failed to read during the most
    /// recent LoadOrBuild pass.
    /// 
    /// MainViewModel copies this into BindingModel so output writing can be
    /// blocked for the rest of the launcher run.
    /// </summary>
    public bool HasReadFailuresBlockingSave { get; private set; }

    public List<string> ReadFailureMessages { get; } = new();

    public IReadOnlyList<DeviceBindingProfile> LoadOrBuild(
        string baseDir,
        IReadOnlyList<StockDeviceSetupMatch> matches)
    {
        HasReadFailuresBlockingSave = false;
        ReadFailureMessages.Clear();

        string actionId = DebugDiagnosticsService.CreateActionId("JSONHOTASREAD");
        DebugDiagnosticsService.Info($"Device JSON read begin. DetectedDeviceCount={matches.Count} | ActionId={actionId}");

        string jsonDir = Path.Combine(baseDir, "User", "Config", "JSON");
        var profiles = new List<DeviceBindingProfile>();
        Dictionary<string, SavedDeviceJsonGroup> savedProfilesByDurableKey = LoadSavedDeviceJson(jsonDir, actionId);
        var matchedSavedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (StockDeviceSetupMatch match in matches)
        {
            string durableDeviceKey = match.Device.DurableDeviceKey;

            if (!savedProfilesByDurableKey.TryGetValue(durableDeviceKey, out SavedDeviceJsonGroup? savedProfileGroup))
            {
                profiles.Add(BuildFallbackProfile(match, actionId));
                continue;
            }

            try
            {
                DeviceBindingProfile profile = CreateConnectedProfileFromJson(match.Device, savedProfileGroup, actionId);
                profiles.Add(profile);
                matchedSavedKeys.Add(durableDeviceKey);

                LogLoadedProfile(profile, savedProfileGroup.DisplayFileName, actionId);
            }
            catch (Exception ex)
            {
                DebugDiagnosticsService.Exception(ex, $"Device JSON read failed: {savedProfileGroup.DisplayPath}");

                // Show the actual JSON parser message to the user so they can see
                // the bad file and the line/position reported by System.Text.Json.
                MarkReadFailureBlockingSave($"Device JSON read failed:\n{ex.Message}");

                profiles.Add(BuildFallbackProfile(match, actionId));
                matchedSavedKeys.Add(durableDeviceKey);
            }
        }

        foreach (SavedDeviceJsonGroup savedProfileGroup in savedProfilesByDurableKey.Values.OrderBy(profile => profile.DisplayFileName, StringComparer.OrdinalIgnoreCase))
        {
            string durableDeviceKey = savedProfileGroup.DurableDeviceKey;

            if (string.IsNullOrWhiteSpace(durableDeviceKey) || matchedSavedKeys.Contains(durableDeviceKey))
                continue;

            try
            {
                DeviceBindingProfile offlineProfile = CreateOfflineProfileFromJson(savedProfileGroup);
                profiles.Add(offlineProfile);

                DebugDiagnosticsService.Warn(
                    $"Saved device profile is offline and was retained | Device=\"{offlineProfile.ProductName}\" | PIDVID={offlineProfile.PidVid} | DurableKey={offlineProfile.DurableDeviceKey} | Json=\"{savedProfileGroup.DisplayFileName}\" | LastSeenInstanceGuid={FormatGuid(offlineProfile.LastSeenInstanceGuid)} | ActionId={actionId}");
            }
            catch (Exception ex)
            {
                DebugDiagnosticsService.Exception(ex, $"Offline device JSON read failed: {savedProfileGroup.DisplayPath}");

                // Show the actual JSON parser message to the user so they can see
                // the bad file and the line/position reported by System.Text.Json.
                MarkReadFailureBlockingSave($"Offline device JSON read failed:\n{ex.Message}");
            }
        }

        DebugDiagnosticsService.Info($"Device JSON read end. LoadedOrBuilt={profiles.Count} | Connected={profiles.Count(profile => profile.IsConnected)} | Offline={profiles.Count(profile => !profile.IsConnected)} | ActionId={actionId}");
        return profiles;
    }

    private Dictionary<string, SavedDeviceJsonGroup> LoadSavedDeviceJson(string jsonDir, string actionId)
    {
        // Group all device/aircraft JSON files by durable device key so the rest of
        // the app still receives one in-memory DeviceBindingProfile per device.
        var savedProfilesByDurableKey = new Dictionary<string, SavedDeviceJsonGroup>(StringComparer.OrdinalIgnoreCase);

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

                if (!savedProfilesByDurableKey.TryGetValue(durableDeviceKey, out SavedDeviceJsonGroup? group))
                {
                    group = new SavedDeviceJsonGroup(durableDeviceKey);
                    savedProfilesByDurableKey[durableDeviceKey] = group;
                }

                group.Add(new SavedDeviceJson(path, document));
            }
            catch (Exception ex)
            {
                DebugDiagnosticsService.Exception(ex, $"Device JSON read failed: {path}");

                // Show the actual JSON parser message to the user so they can see
                // the bad file and the line/position reported by System.Text.Json.
                MarkReadFailureBlockingSave($"Device JSON read failed:\n{ex.Message}");
            }
        }

        foreach (SavedDeviceJsonGroup group in savedProfilesByDurableKey.Values)
        {
            group.SortDocuments();

            DebugDiagnosticsService.Info(
                $"Device JSON group loaded | DurableKey={group.DurableDeviceKey} | FileCount={group.Documents.Count} | Files=\"{string.Join(", ", group.Documents.Select(document => Path.GetFileName(document.Path)))}\" | ActionId={actionId}");
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
        return JsonFileHelper.FromJsonFile<JsonDeviceBindingDocument>(path);
    }

    private void MarkReadFailureBlockingSave(string message)
    {
        HasReadFailuresBlockingSave = true;
        ReadFailureMessages.Add(message);

        DebugDiagnosticsService.Warn(
            $"Device JSON read failure marked output saving unsafe for this launcher run. {message}");
    }

    private DeviceBindingProfile CreateConnectedProfileFromJson(
        InputDeviceInfo device,
        SavedDeviceJsonGroup savedProfileGroup,
        string actionId)
    {
        JsonDeviceBindingDocument metadataDocument = savedProfileGroup.MetadataDocument;
        Guid? previousInstanceGuid = ParseGuid(metadataDocument.LastSeenInstanceGuid);

        if (previousInstanceGuid.HasValue && previousInstanceGuid.Value != device.InstanceGuid)
        {
            DebugDiagnosticsService.Warn(
                $"Device InstanceGuid changed. Reconciled by DurableDeviceKey | Device=\"{device.ProductName}\" | DurableKey={device.DurableDeviceKey} | Previous={previousInstanceGuid.Value:B} | Current={device.InstanceGuid:B} | Json=\"{savedProfileGroup.DisplayFileName}\" | ActionId={actionId}");
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
            JsonPath = savedProfileGroup.DisplayPath
        };

        ApplyAxisBindings(profile, SelectSharedAxisSource(savedProfileGroup.Documents));
        ApplyAircraftProfiles(profile, savedProfileGroup.Documents);

        return profile;
    }

    private DeviceBindingProfile CreateOfflineProfileFromJson(SavedDeviceJsonGroup savedProfileGroup)
    {
        JsonDeviceBindingDocument metadataDocument = savedProfileGroup.MetadataDocument;
        string pidVid = NormalizeHex(metadataDocument.PidVid);
        string vendorIdHex = NormalizeHex(metadataDocument.VendorIdHex);
        string productIdHex = NormalizeHex(metadataDocument.ProductIdHex);

        if (string.IsNullOrWhiteSpace(pidVid))
            pidVid = ParseDurableKeyPrefix(GetDocumentDurableDeviceKey(metadataDocument));

        if ((string.IsNullOrWhiteSpace(productIdHex) || string.IsNullOrWhiteSpace(vendorIdHex)) && pidVid.Length >= 8)
        {
            productIdHex = pidVid.Substring(0, 4);
            vendorIdHex = pidVid.Substring(4, 4);
        }

        Guid? lastSeenInstanceGuid = ParseGuid(metadataDocument.LastSeenInstanceGuid);

        var profile = new DeviceBindingProfile
        {
            DiscoveryIndex = int.MaxValue,
            InstanceGuid = lastSeenInstanceGuid ?? Guid.Empty,
            LastSeenInstanceGuid = lastSeenInstanceGuid,
            IsConnected = false,
            ProductGuid = Guid.Empty,
            InstanceName = metadataDocument.InstanceName ?? "",
            ProductName = metadataDocument.ProductName ?? Path.GetFileNameWithoutExtension(savedProfileGroup.DisplayPath),
            VendorIdHex = vendorIdHex,
            ProductIdHex = productIdHex,
            DuplicatePidVidSequenceNumber = metadataDocument.DuplicatePidVidSequenceNumber,
            AxisCount = metadataDocument.AxisCount,
            ButtonCount = metadataDocument.ButtonCount,
            PovCount = metadataDocument.PovCount,
            CapabilitiesReadSuccessfully = false,
            Source = DeviceBindingSource.Json,
            StockXmlPath = null,
            JsonPath = savedProfileGroup.DisplayPath
        };

        ApplyAxisBindings(profile, SelectSharedAxisSource(savedProfileGroup.Documents));
        ApplyAircraftProfiles(profile, savedProfileGroup.Documents);

        return profile;
    }

    private static List<JsonDeviceAxisBinding>? SelectSharedAxisSource(IReadOnlyList<SavedDeviceJson> savedDocuments)
    {
        // Rebuild the shared in-memory AxisBindings collection from one axis block.
        // F-16 is the authoritative source when multiple aircraft files are present.
        SavedDeviceJson? primaryAircraftDocument = savedDocuments.FirstOrDefault(savedDocument =>
            string.Equals(savedDocument.Document.AircraftProfile, PrimarySharedAxisAircraftProfile, StringComparison.OrdinalIgnoreCase) &&
            savedDocument.Document.AxisBindings is not null);

        if (primaryAircraftDocument is not null)
            return primaryAircraftDocument.Document.AxisBindings;

        SavedDeviceJson? anyAxisDocument = savedDocuments.FirstOrDefault(savedDocument => savedDocument.Document.AxisBindings is not null);

        return anyAxisDocument?.Document.AxisBindings;
    }

    private static void ApplyAxisBindings(
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

                jsonAxisByLogicalName[logicalAxisName] = axis;
            }
        }

        // Always rebuild axis bindings in AxisDefinitionService order.
        // This keeps the current full axis table available in memory.
        foreach (DeviceAxisDefinition definition in AxisDefinitionService.GetDefinitions())
        {
            jsonAxisByLogicalName.TryGetValue(definition.LogicalAxisName, out JsonDeviceAxisBinding? axis);

            profile.AxisBindings.Add(new DeviceAxisBinding
            {
                LogicalAxisName = definition.LogicalAxisName,
                PhysicalAxisIndex = axis?.PhysicalAxisIndex,
                Saturation = axis?.Saturation ?? "None",
                Deadzone = axis?.Deadzone ?? "None",
                Curve = NormalizeAxisCurve(axis?.Curve),
                Invert = axis?.Invert.GetValueOrDefault() ?? false,
                AfterburnerDetent = axis?.AfterburnerDetent,
                IdleDetent = axis?.IdleDetent
            });
        }
    }

    private static int NormalizeAxisCurve(int? curve)
    {
        if (!curve.HasValue || curve.Value < 1)
            return 1;

        return curve.Value;
    }

    private static void ApplyAircraftProfiles(
        DeviceBindingProfile profile,
        IReadOnlyList<SavedDeviceJson> savedDocuments)
    {
        // Merge each aircraft file into this device's in-memory aircraft profiles.
        // Button and POV bindings remain aircraft-specific.
        var aircraftProfilesByName = new Dictionary<string, DeviceAircraftBindingProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (SavedDeviceJson savedDocument in savedDocuments)
            ApplyAircraftProfilesFromDocument(aircraftProfilesByName, savedDocument.Document);

        if (aircraftProfilesByName.Count == 0)
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

        foreach (DeviceAircraftBindingProfile aircraft in aircraftProfilesByName.Values.OrderBy(aircraft => GetAircraftSortKey(aircraft.AircraftProfile), StringComparer.OrdinalIgnoreCase))
            profile.AircraftProfiles.Add(aircraft);
    }

    private static void ApplyAircraftProfilesFromDocument(
        Dictionary<string, DeviceAircraftBindingProfile> aircraftProfilesByName,
        JsonDeviceBindingDocument document)
    {
        if (IsSplitDeviceAircraftDocument(document))
        {
            DeviceAircraftBindingProfile aircraft = BuildAircraftProfile(
                string.IsNullOrWhiteSpace(document.AircraftProfile) ? "F-16" : document.AircraftProfile!,
                document.ButtonBindings,
                document.PovBindings);

            AddAircraftProfileIfMissing(aircraftProfilesByName, aircraft);
            return;
        }

        if (document.AircraftProfiles is null)
            return;

        foreach (JsonDeviceAircraftProfile jsonAircraft in document.AircraftProfiles)
        {
            DeviceAircraftBindingProfile aircraft = BuildAircraftProfile(
                string.IsNullOrWhiteSpace(jsonAircraft.AircraftProfile) ? "F-16" : jsonAircraft.AircraftProfile!,
                jsonAircraft.ButtonBindings,
                jsonAircraft.PovBindings);

            AddAircraftProfileIfMissing(aircraftProfilesByName, aircraft);
        }
    }

    private static bool IsSplitDeviceAircraftDocument(JsonDeviceBindingDocument document)
    {
        // A device/aircraft JSON stores its aircraft name and bindings at the top level.
        return !string.IsNullOrWhiteSpace(document.AircraftProfile) ||
               document.ButtonBindings is not null ||
               document.PovBindings is not null;
    }

    private static DeviceAircraftBindingProfile BuildAircraftProfile(
        string aircraftProfileName,
        List<JsonDeviceButtonBinding>? buttonBindings,
        List<JsonDevicePovBinding>? povBindings)
    {
        var aircraft = new DeviceAircraftBindingProfile
        {
            AircraftProfile = aircraftProfileName
        };

        if (buttonBindings is not null)
        {
            foreach (JsonDeviceButtonBinding button in buttonBindings)
            {
                int assignmentIndex = GetButtonAssignmentIndex(button);

                aircraft.ButtonBindings.Add(new DeviceButtonBinding
                {
                    ButtonIndex = button.ButtonIndex.GetValueOrDefault(),
                    AssignmentIndex = assignmentIndex,
                    CallbackName = button.CallbackName ?? "",
                    Invoke = button.Invoke ?? DeviceButtonBinding.GetDefaultInvoke(assignmentIndex),
                    SoundId = button.SoundId.GetValueOrDefault()
                });
            }
        }

        if (povBindings is not null)
        {
            foreach (JsonDevicePovBinding pov in povBindings)
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

        return aircraft;
    }

    private static void AddAircraftProfileIfMissing(
        Dictionary<string, DeviceAircraftBindingProfile> aircraftProfilesByName,
        DeviceAircraftBindingProfile aircraft)
    {
        if (string.IsNullOrWhiteSpace(aircraft.AircraftProfile))
            return;

        if (aircraftProfilesByName.ContainsKey(aircraft.AircraftProfile))
            return;

        aircraftProfilesByName[aircraft.AircraftProfile] = aircraft;
    }

    private static string GetAircraftSortKey(string aircraftProfile)
    {
        if (string.Equals(aircraftProfile, "F-16", StringComparison.OrdinalIgnoreCase))
            return "000_F-16";

        if (string.Equals(aircraftProfile, "F-15ABCD", StringComparison.OrdinalIgnoreCase))
            return "001_F-15ABCD";

        return "999_" + aircraftProfile;
    }

    private static int GetButtonAssignmentIndex(JsonDeviceButtonBinding button)
    {
        bool hasShiftState = !string.IsNullOrWhiteSpace(button.ShiftState);
        bool hasTrigger = !string.IsNullOrWhiteSpace(button.Trigger);

        if (hasShiftState || hasTrigger)
        {
            string shiftState = hasShiftState
                ? button.ShiftState!
                : DeviceButtonBinding.GetShiftState(button.AssignmentIndex.GetValueOrDefault());

            string trigger = hasTrigger
                ? button.Trigger!
                : DeviceButtonBinding.GetTrigger(button.AssignmentIndex.GetValueOrDefault());

            return DeviceButtonBinding.GetAssignmentIndex(shiftState, trigger);
        }

        return button.AssignmentIndex.GetValueOrDefault();
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

    private sealed class SavedDeviceJsonGroup
    {
        public SavedDeviceJsonGroup(string durableDeviceKey)
        {
            DurableDeviceKey = durableDeviceKey;
        }

        public string DurableDeviceKey { get; }
        public List<SavedDeviceJson> Documents { get; } = new();

        public string DisplayPath => Documents.FirstOrDefault()?.Path ?? "";
        public string DisplayFileName => Documents.Count == 1
            ? Path.GetFileName(DisplayPath)
            : string.Join(", ", Documents.Select(document => Path.GetFileName(document.Path)));

        public JsonDeviceBindingDocument MetadataDocument => Documents.First().Document;

        public void Add(SavedDeviceJson savedDeviceJson)
        {
            Documents.Add(savedDeviceJson);
        }

        public void SortDocuments()
        {
            Documents.Sort((left, right) =>
            {
                // Use device/aircraft JSON files before broader device-level files
                // so aircraft-specific bindings provide the final in-memory values.
                int leftFormatSort = IsSplitDeviceAircraftDocument(left.Document) ? 0 : 1;
                int rightFormatSort = IsSplitDeviceAircraftDocument(right.Document) ? 0 : 1;

                int formatCompare = leftFormatSort.CompareTo(rightFormatSort);
                if (formatCompare != 0)
                    return formatCompare;

                return string.Compare(Path.GetFileName(left.Path), Path.GetFileName(right.Path), StringComparison.OrdinalIgnoreCase);
            });
        }
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

        [DataMember(Name = "aircraft_profile")]
        public string? AircraftProfile { get; set; }

        [DataMember(Name = "axis_binding_scope")]
        public string? AxisBindingScope { get; set; }

        [DataMember(Name = "axis_bindings")]
        public List<JsonDeviceAxisBinding>? AxisBindings { get; set; }

        [DataMember(Name = "button_bindings")]
        public List<JsonDeviceButtonBinding>? ButtonBindings { get; set; }

        [DataMember(Name = "pov_bindings")]
        public List<JsonDevicePovBinding>? PovBindings { get; set; }

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

        [DataMember(Name = "curve")]
        public int? Curve { get; set; }

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

        [DataMember(Name = "shift_state")]
        public string? ShiftState { get; set; }

        [DataMember(Name = "trigger")]
        public string? Trigger { get; set; }

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