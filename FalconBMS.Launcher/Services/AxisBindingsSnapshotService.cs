using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Builds a single in-memory snapshot of axis bindings for an install so multiple tabs
/// can reuse the same parsed config data instead of re-reading the same files.
/// </summary>
public sealed class AxisBindingsSnapshotService
{
    private readonly AxisMappingDatService _axisDat = new();
    private readonly DeviceSortingReader _sorting = new();
    private readonly SetupXmlService _setupXml = new();

    public sealed record AxisBindingInfo(
        bool IsMapped,
        string BindingText,
        string? DeviceName,
        Guid? ProductGuid,
        int PhysicalAxisIndex,
        bool Invert,
        DetentPosition Detents
    );

    public sealed record AxisBindingsSnapshot(
        IReadOnlyDictionary<AxisFunction, AxisBindingInfo> Bindings
    );

    public AxisBindingsSnapshot Build(string baseDir, IEnumerable<AxisFunction> functions)
    {
        var axisAll = _axisDat.ReadAll(baseDir);
        var sortingAll = _sorting.Read(baseDir);
        var setupSnap = _setupXml.ReadSnapshot(baseDir);

        var axisByMappingIndex = axisAll.Entries
            .Where(e => e.JoyNum >= 0 && e.AxisIndex >= 0)
            .ToDictionary(e => e.Index, e => e);

        var deviceBySlot = sortingAll
            .GroupBy(d => d.SlotIndex)
            .ToDictionary(g => g.Key, g => g.First());

        var bindings = new Dictionary<AxisFunction, AxisBindingInfo>();

        foreach (var function in functions.Distinct())
        {
            var def = AxisCatalog.Get(function);

            if (!axisByMappingIndex.TryGetValue(def.MappingIndex, out var entry))
            {
                bindings[function] = new AxisBindingInfo(
                    IsMapped: false,
                    BindingText: "Not set",
                    DeviceName: null,
                    ProductGuid: null,
                    PhysicalAxisIndex: -1,
                    Invert: false,
                    Detents: DetentPosition.Default
                );
                continue;
            }

            int slot = entry.JoyNum - 2;
            string deviceName = deviceBySlot.TryGetValue(slot, out var device)
                ? device.Name
                : $"Device Slot {slot}";

            Guid? productGuid = deviceBySlot.TryGetValue(slot, out var deviceGuid)
                ? deviceGuid.ProductGuid
                : null;

            bool invert = false;
            if (setupSnap.Axis.TryGetValue(function, out var axisInfo))
                invert = axisInfo.Invert;

            DetentPosition detents = DetentPosition.Default;
            if (function == AxisFunction.Throttle)
            {
                var safeName = SetupXmlService.SanitizeDeviceNameForLookup(deviceName);
                if (safeName is not null &&
                    setupSnap.DetentsBySafeDeviceName.TryGetValue(safeName, out var foundDetents))
                {
                    detents = foundDetents;
                }
            }

            bindings[function] = new AxisBindingInfo(
                IsMapped: true,
                BindingText: $"{deviceName}  {AxisIndexToName(entry.AxisIndex)}",
                DeviceName: deviceName,
                ProductGuid: productGuid,
                PhysicalAxisIndex: entry.AxisIndex,
                Invert: invert,
                Detents: detents
            );
        }

        return new AxisBindingsSnapshot(bindings);
    }

    private static string AxisIndexToName(int idx) =>
        idx switch
        {
            0 => "X",
            1 => "Y",
            2 => "Z",
            3 => "Rx",
            4 => "Ry",
            5 => "Rz",
            6 => "Slider0",
            7 => "Slider1",
            _ => idx.ToString()
        };
}