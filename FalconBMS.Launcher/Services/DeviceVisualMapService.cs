using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Windows;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Loads built-in visual device maps from application resources.
/// These maps are visual-only cheat sheets. They do not read or write binding data.
/// </summary>
public sealed class DeviceVisualMapService
{
    private static readonly string[] BuiltInMapResourcePaths =
    {
        // Add JSON image maps here:
        "Assets/Devices/Joystick - HOTAS Warthog.map.json",
        "Assets/Devices/F16 MFD 1.map.json",
        "Assets/Devices/F16 MFD 2.map.json"
    };

    public IReadOnlyList<DeviceVisualMap> LoadMaps()
    {
        var maps = new List<DeviceVisualMap>();

        foreach (string resourcePath in BuiltInMapResourcePaths)
        {
            try
            {
                DeviceVisualMap? map = LoadMap(resourcePath);

                if (map is not null && !string.IsNullOrWhiteSpace(map.StockDeviceName))
                    maps.Add(map);
            }
            catch (Exception ex)
            {
                DebugDiagnosticsService.Exception(ex, $"Device visual map load failed: {resourcePath}");
            }
        }

        return maps;
    }

    public DeviceVisualMap? FindMapForDevice(DeviceBindingProfile deviceProfile, IReadOnlyList<DeviceVisualMap> maps)
    {
        return maps.FirstOrDefault(map =>
            string.Equals(map.StockDeviceName, deviceProfile.ProductName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(map.StockDeviceName, deviceProfile.InstanceName, StringComparison.OrdinalIgnoreCase));
    }

    private static DeviceVisualMap? LoadMap(string resourcePath)
    {
        string normalizedPath = resourcePath.Replace('\\', '/');

        string escapedPath = string.Join(
            "/",
            normalizedPath
                .Split('/')
                .Select(Uri.EscapeDataString));

        var uri = new Uri($"pack://application:,,,/{escapedPath}", UriKind.Absolute);
        var resource = Application.GetResourceStream(uri);

        if (resource is null)
            return null;

        using var stream = resource.Stream;
        using var reader = new System.IO.StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        string json = reader.ReadToEnd();

        // Visual Studio may save JSON resources as UTF-8 with BOM.
        // DataContractJsonSerializer can see that BOM as an unexpected character,
        // so trim it before deserializing the visual map.
        json = json.TrimStart('\uFEFF');

        using var jsonStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var serializer = new DataContractJsonSerializer(typeof(DeviceVisualMap));
        return serializer.ReadObject(jsonStream) as DeviceVisualMap;
    }
}