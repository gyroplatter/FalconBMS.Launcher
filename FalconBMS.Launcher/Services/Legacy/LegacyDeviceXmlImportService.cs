using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Models.Legacy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FalconBMS.Launcher.Services.Legacy;

public sealed class LegacyDeviceXmlImportService
{
    private static readonly Regex LegacyXmlFileRegex = new(
        @"^Setup\.v100\.(?<name>.+?)\s+\{(?<instanceGuid>[0-9A-Fa-f\-]+)\}\.xml$",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase);

    private readonly DeviceStockXmlAxisParserService
        _axisParser = new();

    private readonly DeviceStockXmlButtonParserService
        _buttonParser = new();

    private readonly DeviceStockXmlPovParserService
        _povParser = new();

    private readonly DeviceBindingProfileBuilderService
        _stockProfileBuilder = new();

    public IReadOnlyList<LegacyDeviceXmlFile> FindLegacyXmlFiles(
        string configDirectory)
    {
        var results =
            new List<LegacyDeviceXmlFile>();

        if (!Directory.Exists(configDirectory))
            return results;

        foreach (string path in Directory.GetFiles(
                     configDirectory,
                     "Setup.v100.*.xml",
                     SearchOption.TopDirectoryOnly))
        {
            string fileName =
                Path.GetFileName(path);

            Match match =
                LegacyXmlFileRegex.Match(
                    fileName);

            if (!match.Success)
                continue;

            Guid.TryParse(
                match.Groups["instanceGuid"].Value,
                out Guid instanceGuid);

            results.Add(
                new LegacyDeviceXmlFile
                {
                    DeviceName =
                        match.Groups["name"].Value.Trim(),
                    InstanceGuid =
                        instanceGuid,
                    Path =
                        path
                });
        }

        return results;
    }

    public bool CanReadXml(
        string path)
    {
        try
        {
            XDocument.Load(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public DeviceBindingProfile BuildConnectedProfile(
        StockDeviceSetupMatch match,
        string? legacyXmlPath,
        ICollection<LegacyImportSkippedItem> skippedItems)
    {
        string legacyXmlPathValue =
            legacyXmlPath ?? "";

        if (!string.IsNullOrWhiteSpace(
                legacyXmlPathValue) &&
            File.Exists(
                legacyXmlPathValue) &&
            CanReadXml(
                legacyXmlPathValue))
        {
            DeviceBindingProfile profile =
                CreateConnectedProfileShell(
                    match,
                    legacyXmlPathValue);

            ApplyXml(
                profile,
                skippedItems);

            return profile;
        }

        return _stockProfileBuilder
            .Build(new[] { match })
            .Single();
    }

    public DeviceBindingProfile BuildOfflineProfile(
        LegacyDeviceXmlFile legacyXml,
        LegacySortedDevice sortedDevice,
        string? stockXmlPath,
        int discoveryIndex,
        int? duplicateSequenceNumber,
        ICollection<LegacyImportSkippedItem> skippedItems)
    {
        string vendorIdHex =
            GetVendorIdHex(
                sortedDevice.ProductGuid);

        string productIdHex =
            GetProductIdHex(
                sortedDevice.ProductGuid);

        DeviceXmlCapabilities capabilities =
            ReadCapabilities(
                legacyXml.Path);

        string sourceXmlPath =
            CanReadXml(legacyXml.Path)
                ? legacyXml.Path
                : stockXmlPath ?? "";

        var profile =
            new DeviceBindingProfile
            {
                DiscoveryIndex =
                    discoveryIndex,
                InstanceGuid =
                    Guid.Empty,
                LastSeenInstanceGuid =
                    legacyXml.InstanceGuid == Guid.Empty
                        ? null
                        : legacyXml.InstanceGuid,
                IsConnected =
                    false,
                ProductGuid =
                    sortedDevice.ProductGuid,
                InstanceName =
                    sortedDevice.ProductName,
                ProductName =
                    sortedDevice.ProductName,
                VendorIdHex =
                    vendorIdHex,
                ProductIdHex =
                    productIdHex,
                DuplicatePidVidSequenceNumber =
                    duplicateSequenceNumber,
                AxisCount =
                    capabilities.AxisCount,
                ButtonCount =
                    capabilities.ButtonCount,
                PovCount =
                    capabilities.PovCount,
                CapabilitiesReadSuccessfully =
                    true,
                Source =
                    string.IsNullOrWhiteSpace(sourceXmlPath)
                        ? DeviceBindingSource.Empty
                        : DeviceBindingSource.StockXml,
                StockXmlPath =
                    string.IsNullOrWhiteSpace(sourceXmlPath)
                        ? null
                        : sourceXmlPath
            };

        AddModelContainers(
            profile);

        if (!string.IsNullOrWhiteSpace(sourceXmlPath))
        {
            ApplyXml(
                profile,
                skippedItems);
        }

        return profile;
    }

    public string? FindStockXmlByName(
        string deviceName,
        IReadOnlyList<StockDeviceSetupMatch> connectedMatches)
    {
        StockDeviceSetupMatch? connectedMatch =
            connectedMatches.FirstOrDefault(match =>
                NamesMatch(
                    match.Device.ProductName,
                    deviceName) ||
                NamesMatch(
                    match.Device.InstanceName,
                    deviceName));

        if (connectedMatch?.HasStockXml == true)
            return connectedMatch.StockXmlPath;

        string stockDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "Stock");

        if (!Directory.Exists(stockDirectory))
            return null;

        return Directory.GetFiles(
                stockDirectory,
                "Setup.v100.*.xml",
                SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path =>
                NamesMatch(
                    GetStockDeviceName(path),
                    deviceName));
    }

    public static bool NamesMatch(
        string left,
        string right)
    {
        return string.Equals(
            NormalizeDeviceName(left),
            NormalizeDeviceName(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private DeviceBindingProfile CreateConnectedProfileShell(
        StockDeviceSetupMatch match,
        string xmlPath)
    {
        InputDeviceInfo device =
            match.Device;

        var profile =
            new DeviceBindingProfile
            {
                DiscoveryIndex =
                    device.DiscoveryIndex,
                InstanceGuid =
                    device.InstanceGuid,
                LastSeenInstanceGuid =
                    device.InstanceGuid,
                IsConnected =
                    true,
                ProductGuid =
                    device.ProductGuid,
                InstanceName =
                    device.InstanceName,
                ProductName =
                    device.ProductName,
                VendorIdHex =
                    device.VendorIdHex,
                ProductIdHex =
                    device.ProductIdHex,
                DuplicatePidVidSequenceNumber =
                    device.DuplicatePidVidSequenceNumber,
                AxisCount =
                    device.Capabilities.AxisCount,
                ButtonCount =
                    device.Capabilities.ButtonCount,
                PovCount =
                    device.Capabilities.PovCount,
                CapabilitiesReadSuccessfully =
                    device.Capabilities.WasReadSuccessfully,
                Source =
                    DeviceBindingSource.StockXml,
                StockXmlPath =
                    xmlPath
            };

        AddModelContainers(
            profile);

        return profile;
    }

    private static void AddModelContainers(
        DeviceBindingProfile profile)
    {
        foreach (DeviceAxisDefinition definition in
                 AxisDefinitionService.GetDefinitions())
        {
            profile.AxisBindings.Add(
                new DeviceAxisBinding
                {
                    LogicalAxisName =
                        definition.LogicalAxisName,
                    PhysicalAxisIndex =
                        null
                });
        }

        profile.AircraftProfiles.Add(
            new DeviceAircraftBindingProfile
            {
                AircraftProfile = "F-16"
            });

        profile.AircraftProfiles.Add(
            new DeviceAircraftBindingProfile
            {
                AircraftProfile = "F-15ABCD"
            });
    }

    private void ApplyXml(
        DeviceBindingProfile profile,
        ICollection<LegacyImportSkippedItem> skippedItems)
    {
        _axisParser.ApplyAxes(
            profile,
            skippedItems);

        _buttonParser.ApplyButtons(
            profile);

        _povParser.ApplyPovs(
            profile);
    }

    private static DeviceXmlCapabilities ReadCapabilities(
        string xmlPath)
    {
        try
        {
            XDocument document =
                XDocument.Load(
                    xmlPath);

            int axisCount =
                document.Root?
                    .Element("axis")?
                    .Elements("AxAssgn")
                    .Count() ?? 0;

            int buttonCount =
                GetLargestSectionCount(
                    document,
                    "dx",
                    "DxAssgn");

            int povCount =
                GetLargestSectionCount(
                    document,
                    "pov",
                    "PovAssgn");

            return new DeviceXmlCapabilities
            {
                AxisCount =
                    axisCount,
                ButtonCount =
                    buttonCount,
                PovCount =
                    povCount
            };
        }
        catch
        {
            return new DeviceXmlCapabilities();
        }
    }

    private static int GetLargestSectionCount(
        XDocument document,
        string sectionName,
        string childName)
    {
        int largest =
            document.Root?
                .Element(sectionName)?
                .Elements(childName)
                .Count() ?? 0;

        foreach (string profileName in
                 new[]
                 {
                     "profileDefaultF16",
                     "profileF15ABCD"
                 })
        {
            int count =
                document.Root?
                    .Element(profileName)?
                    .Element(sectionName)?
                    .Elements(childName)
                    .Count() ?? 0;

            largest =
                Math.Max(
                    largest,
                    count);
        }

        return largest;
    }

    private static string GetStockDeviceName(
        string stockXmlPath)
    {
        string fileName =
            Path.GetFileNameWithoutExtension(
                stockXmlPath);

        const string prefix =
            "Setup.v100.";

        if (fileName.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            fileName =
                fileName.Substring(
                    prefix.Length);
        }

        int stockMarker =
            fileName.LastIndexOf(
                "{Stock}",
                StringComparison.OrdinalIgnoreCase);

        if (stockMarker >= 0)
        {
            fileName =
                fileName.Substring(
                    0,
                    stockMarker);
        }

        return fileName.Trim();
    }

    private static string NormalizeDeviceName(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string normalized =
            value.Trim()
                .Replace(
                    "H.O.T.A.S.",
                    "HOTAS")
                .Replace(
                    "Flight Controller",
                    "")
                .Replace(
                    "USB",
                    "");

        while (normalized.Contains("  "))
        {
            normalized =
                normalized.Replace(
                    "  ",
                    " ");
        }

        return normalized.Trim();
    }

    private static string GetVendorIdHex(
        Guid productGuid)
    {
        byte[] bytes =
            productGuid.ToByteArray();

        uint data1 =
            unchecked((uint)bytes[0]) |
            ((uint)bytes[1] << 8) |
            ((uint)bytes[2] << 16) |
            ((uint)bytes[3] << 24);

        return (data1 & 0xFFFF)
            .ToString("X4");
    }

    private static string GetProductIdHex(
        Guid productGuid)
    {
        byte[] bytes =
            productGuid.ToByteArray();

        uint data1 =
            unchecked((uint)bytes[0]) |
            ((uint)bytes[1] << 8) |
            ((uint)bytes[2] << 16) |
            ((uint)bytes[3] << 24);

        return ((data1 >> 16) & 0xFFFF)
            .ToString("X4");
    }
}

public sealed class LegacyDeviceXmlFile
{
    public string DeviceName { get; init; } = "";

    public Guid InstanceGuid { get; init; }

    public string Path { get; init; } = "";
}

internal sealed class DeviceXmlCapabilities
{
    public int AxisCount { get; init; }

    public int ButtonCount { get; init; }

    public int PovCount { get; init; }
}