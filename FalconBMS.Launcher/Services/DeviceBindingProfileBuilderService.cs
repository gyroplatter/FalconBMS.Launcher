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
    public IReadOnlyList<DeviceBindingProfile> Build(IReadOnlyList<StockDeviceSetupMatch> matches)
    {
        var profiles = new List<DeviceBindingProfile>();

        foreach (StockDeviceSetupMatch match in matches)
        {
            DeviceBindingProfile profile = CreateProfile(match);
            profiles.Add(profile);

            DebugDiagnosticsService.Info(
                $"Device binding profile built | Device=\"{profile.ProductName}\" | PIDVID={profile.PidVid} | Source={profile.Source} | StockXml=\"{Path.GetFileName(profile.StockXmlPath ?? "")}\" | ButtonBindings={profile.ButtonBindings.Count} | PovBindings={profile.PovBindings.Count} | AxisBindings={profile.AxisBindings.Count}");
        }

        DebugDiagnosticsService.Info(
            $"Device binding profiles built | Count={profiles.Count}");

        return profiles;
    }

    private static DeviceBindingProfile CreateProfile(StockDeviceSetupMatch match)
    {
        InputDeviceInfo device = match.Device;

        return new DeviceBindingProfile
        {
            DiscoveryIndex = device.DiscoveryIndex,
            InstanceGuid = device.InstanceGuid,
            ProductGuid = device.ProductGuid,
            InstanceName = device.InstanceName,
            ProductName = device.ProductName,
            VendorIdHex = device.VendorIdHex,
            ProductIdHex = device.ProductIdHex,
            Source = match.HasStockXml
                ? DeviceBindingSource.StockXml
                : DeviceBindingSource.Empty,
            StockXmlPath = match.StockXmlPath
        };
    }
}