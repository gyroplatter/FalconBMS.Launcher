using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Coordinates device discovery and stock XML matching for the selected
/// BMS install. Logs all detected devices and their associated stock
/// configuration files. Serves as the entry point for device initialization.
/// </summary>

public sealed class DeviceDiscoveryService
{
    private readonly StockDeviceSetupMatcherService _stockMatcher = new();

    public IReadOnlyList<StockDeviceSetupMatch> DiscoverAndMatchStockXml(string installBaseDir)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("DEVICES");

        DebugDiagnosticsService.Info($"Device discovery begin. | ActionId={actionId}");

        try
        {
            using var directInput = new DirectInputManager();
            IReadOnlyList<InputDeviceInfo> devices = directInput.DiscoverGameControllers();

            DebugDiagnosticsService.Info(
                $"Device discovery complete. | Count={devices.Count} | ActionId={actionId}");

            foreach (InputDeviceInfo device in devices)
            {
                DebugDiagnosticsService.Info(
                    $"Device discovered | Index={device.DiscoveryIndex} | ProductName=\"{device.ProductName}\" | InstanceName=\"{device.InstanceName}\" | PIDVID={device.PidVid} | DurableKey={device.DurableDeviceKey} | DuplicateSeq={FormatNullable(device.DuplicatePidVidSequenceNumber)} | CapsRead={device.Capabilities.WasReadSuccessfully} | Axes={device.Capabilities.AxisCount} | Buttons={device.Capabilities.ButtonCount} | POVs={device.Capabilities.PovCount} | ProductGuid={device.ProductGuid:B} | InstanceGuid={device.InstanceGuid:B} | ActionId={actionId}");
            }

            IReadOnlyList<StockDeviceSetupMatch> matches = _stockMatcher.Match(installBaseDir, devices);

            foreach (StockDeviceSetupMatch match in matches)
            {
                if (match.HasStockXml)
                {
                    DebugDiagnosticsService.Info(
                        $"Stock XML matched | Device=\"{match.Device.ProductName}\" | PIDVID={match.Device.PidVid} | DurableKey={match.Device.DurableDeviceKey} | File=\"{Path.GetFileName(match.StockXmlPath!)}\" | Path=\"{match.StockXmlPath}\" | ActionId={actionId}");
                }
                else
                {
                    DebugDiagnosticsService.Warn(
                        $"Stock XML missing | Device=\"{match.Device.ProductName}\" | PIDVID={match.Device.PidVid} | DurableKey={match.Device.DurableDeviceKey} | ProductGuid={match.Device.ProductGuid:B} | ActionId={actionId}");
                }
            }

            return matches;
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, $"Device discovery failed. | ActionId={actionId}");
            return Array.Empty<StockDeviceSetupMatch>();
        }
    }

    private static string FormatNullable(int? value)
    {
        return value.HasValue ? value.Value.ToString() : "";
    }
}