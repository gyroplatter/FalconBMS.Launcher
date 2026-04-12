using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FalconBMS.Launcher.Models;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Reads FalconBMS device sorting file into structured slot/device data.
/// </summary>

public sealed class DeviceSortingReader
{
    private static readonly Regex LineRx =
        new(@"^\s*\{(?<guid>[0-9A-Fa-f\-]{36})\}\s*""(?<name>.*)""\s*$",
            RegexOptions.Compiled);

    public string GetPath(string baseDir) =>
        Path.Combine(baseDir, "User", "Config", "DeviceSorting.txt");

    public IReadOnlyList<DeviceSortingEntry> Read(string baseDir)
    {
        var path = GetPath(baseDir);
        var list = new List<DeviceSortingEntry>();
        if (!File.Exists(path)) return list;

        var lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            var m = LineRx.Match(lines[i]);
            if (!m.Success) continue;

            list.Add(new DeviceSortingEntry
            {
                SlotIndex = list.Count, // slot is order of valid parsed entries
                ProductGuid = Guid.Parse(m.Groups["guid"].Value),
                Name = m.Groups["name"].Value
            });
        }

        return list;
    }
}