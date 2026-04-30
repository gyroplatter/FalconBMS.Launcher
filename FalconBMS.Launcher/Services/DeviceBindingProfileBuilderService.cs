using FalconBMS.Launcher.Models;
using System.Collections.Generic;
using System.IO;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Builds in-memory device binding profile shells from discovered devices and
/// stock XML match results. This service does not parse XML or JSON yet; it only
/// establishes one profile per discovered device so every device has a model.
/// </summary>
public sealed class DeviceBindingProfileBuilderService
{
    private readonly AxisDefinitionService _axisDefinitions = new();

    public IReadOnlyList<DeviceBindingProfile> Build(IReadOnlyList<StockDeviceSetupMatch> matches)
    {
        var profiles = new List<DeviceBindingProfile>();

        foreach (StockDeviceSetupMatch match in matches)
        {
            DeviceBindingProfile profile = CreateProfile(match);
            profiles.Add(profile);

            DebugDiagnosticsService.Info(
                $"Device binding profile built | Device=\"{profile.ProductName}\" | PIDVID={profile.PidVid} | DurableKey={profile.DurableDeviceKey} | DuplicateSeq={FormatNullable(profile.DuplicatePidVidSequenceNumber)} | Source={profile.Source} | StockXml=\"{Path.GetFileName(profile.StockXmlPath ?? "")}\" | CapsRead={profile.CapabilitiesReadSuccessfully} | Axes={profile.AxisCount} | Buttons={profile.ButtonCount} | POVs={profile.PovCount} | AxisBindings={profile.AxisBindings.Count} | AircraftProfiles={profile.AircraftProfiles.Count}");
        }

        DebugDiagnosticsService.Info(
            $"Device binding profiles built | Count={profiles.Count}");

        return profiles;
    }

    private DeviceBindingProfile CreateProfile(StockDeviceSetupMatch match)
    {
        InputDeviceInfo device = match.Device;

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
            Source = match.HasStockXml
                ? DeviceBindingSource.StockXml
                : DeviceBindingSource.Empty,
            StockXmlPath = match.StockXmlPath
        };

        foreach (DeviceAxisDefinition definition in _axisDefinitions.GetDefinitions())
        {
            profile.AxisBindings.Add(new DeviceAxisBinding
            {
                LogicalAxisName = definition.LogicalAxisName,
                PhysicalAxisIndex = null
            });
        }

        profile.AircraftProfiles.Add(new DeviceAircraftBindingProfile
        {
            AircraftProfile = "F-16"
        });

        profile.AircraftProfiles.Add(new DeviceAircraftBindingProfile
        {
            AircraftProfile = "F-15ABCD"
        });

        return profile;
    }

    private static string FormatNullable(int? value)
    {
        return value.HasValue ? value.Value.ToString() : "";
    }
}