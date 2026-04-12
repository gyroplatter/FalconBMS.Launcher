using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Maintains device sorting slots, ensures devices are registered, and resolves slot/name/product info.
/// </summary>
public sealed class DeviceSortingService
{
    private static readonly Regex LineRx =
        new(@"^\s*\{(?<guid>[0-9A-Fa-f\-]{36})\}\s*""(?<name>.*)""\s*$",
            RegexOptions.Compiled);

    private static readonly Regex OldNameSanitizeRx =
        new(@"[^A-Za-z0-9\~\`\[\]\{\}\-_\=\'\x20]", RegexOptions.Compiled);

    private static string SanitizeDeviceName(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        return OldNameSanitizeRx.Replace(s, string.Empty).Trim();
    }

    public string GetPath(string baseDir) =>
        Path.Combine(baseDir, "User", "Config", "DeviceSorting.txt");

    public int EnsureDeviceAndGetSlot(string baseDir, Guid productGuid, string deviceName)
    {
        var slots = EnsureDevicesAndGetSlots(
            baseDir,
            new[] { (ProductGuid: productGuid, Name: deviceName) });

        return slots[productGuid];
    }

    /// <summary>
    /// Ensures all provided devices exist in DeviceSorting.txt, updates sanitized names if needed,
    /// and writes the file at most once for the entire batch.
    /// </summary>
    public Dictionary<Guid, int> EnsureDevicesAndGetSlots(
        string baseDir,
        IEnumerable<(Guid ProductGuid, string Name)> devices)
    {
        var path = GetPath(baseDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var list = Load(path);
        var original = new List<(Guid ProductGuid, string Name)>(list);

        foreach (var device in devices)
        {
            var sanitized = SanitizeDeviceName(device.Name);
            var idx = list.FindIndex(x => x.ProductGuid == device.ProductGuid);

            if (idx >= 0)
            {
                if (!string.Equals(list[idx].Name, sanitized, StringComparison.Ordinal))
                    list[idx] = (device.ProductGuid, sanitized);

                continue;
            }

            list.Add((device.ProductGuid, sanitized));
        }

        if (!ListsEqual(original, list))
        {
            DebugDiagnosticsService.Info($"FILE WRITE REQUEST | File=DeviceSorting.txt | Caller=DeviceSortingService.EnsureDevicesAndGetSlots | Reason=DeviceListChanged | OldCount={original.Count} | NewCount={list.Count}");
            Save(path, list);
        }
        else
        {
            DebugDiagnosticsService.Info($"FILE WRITE SKIPPED | File=DeviceSorting.txt | Caller=DeviceSortingService.EnsureDevicesAndGetSlots | Reason=NoContentChange | Count={list.Count}");
        }

        var slotMap = new Dictionary<Guid, int>();
        for (int i = 0; i < list.Count; i++)
        {
            if (!slotMap.ContainsKey(list[i].ProductGuid))
                slotMap[list[i].ProductGuid] = i;
        }

        return slotMap;
    }

    public string? GetDeviceNameBySlot(string baseDir, int slotIndex)
    {
        var path = GetPath(baseDir);
        var list = Load(path);
        if (slotIndex < 0 || slotIndex >= list.Count) return null;
        return list[slotIndex].Name;
    }

    public Guid? GetProductGuidBySlot(string baseDir, int slotIndex)
    {
        var path = GetPath(baseDir);
        var list = Load(path);
        if (slotIndex < 0 || slotIndex >= list.Count) return null;
        return list[slotIndex].ProductGuid;
    }

    public int GetDeviceCount(string baseDir)
    {
        var path = GetPath(baseDir);
        return Load(path).Count;
    }

    private static List<(Guid ProductGuid, string Name)> Load(string path)
    {
        var list = new List<(Guid, string)>();
        if (!File.Exists(path)) return list;

        foreach (var line in File.ReadAllLines(path))
        {
            var m = LineRx.Match(line);
            if (!m.Success) continue;

            list.Add((Guid.Parse(m.Groups["guid"].Value), m.Groups["name"].Value));
        }

        return list;
    }

    private static bool ListsEqual(
        List<(Guid ProductGuid, string Name)> a,
        List<(Guid ProductGuid, string Name)> b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a.Count != b.Count)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].ProductGuid != b[i].ProductGuid)
                return false;

            if (!string.Equals(a[i].Name, b[i].Name, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static void Save(string path, List<(Guid ProductGuid, string Name)> list)
    {
        DebugDiagnosticsService.Info("Overwriting DeviceSorting.txt..");

        using var sw = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (var (g, n) in list)
            sw.WriteLine($"{{{g.ToString().ToUpperInvariant()}}} \"{n}\"");
    }
}