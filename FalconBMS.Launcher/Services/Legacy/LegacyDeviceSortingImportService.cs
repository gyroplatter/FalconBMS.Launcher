using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FalconBMS.Launcher.Services.Legacy;

public sealed class LegacyDeviceSortingImportService
{
    private static readonly Regex DeviceLineRegex = new(
        @"^\s*\{(?<guid>[0-9A-Fa-f\-]+)\}\s+""(?<name>.*)""\s*$",
        RegexOptions.Compiled);

    public IReadOnlyList<LegacySortedDevice> Read(
        string deviceSortingPath)
    {
        var devices = new List<LegacySortedDevice>();

        if (string.IsNullOrWhiteSpace(deviceSortingPath) ||
            !File.Exists(deviceSortingPath))
        {
            return devices;
        }

        int order = 0;

        foreach (string rawLine in File.ReadLines(deviceSortingPath))
        {
            Match match =
                DeviceLineRegex.Match(rawLine);

            if (!match.Success)
                continue;

            if (!Guid.TryParse(
                    match.Groups["guid"].Value,
                    out Guid productGuid))
            {
                continue;
            }

            devices.Add(new LegacySortedDevice
            {
                Order = order++,
                ProductGuid = productGuid,
                ProductName =
                    match.Groups["name"].Value.Trim()
            });
        }

        return devices;
    }
}

public sealed class LegacySortedDevice
{
    public int Order { get; init; }

    public Guid ProductGuid { get; init; }

    public string ProductName { get; init; } = "";
}