using System;
using System.Linq;
using FalconBMS.Launcher.Models;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Formats axis binding information into readable display strings for the UI.
/// </summary>

public static class AxisBindingFormatter
{
    private const int JoyNumOffset = 2;

    public static string? Format(BmsConfigState state, int mappingIndex) // 0..29
    {
        var entry = state.AxisMappings.Entries.FirstOrDefault(e => e.Index == mappingIndex);
        if (entry == null) return null;

        if (entry.JoyNum < 0 || entry.AxisIndex < 0) return null;

        int slot = entry.JoyNum - JoyNumOffset;
        if (slot < 0) return null;

        var dev = state.DeviceOrder.FirstOrDefault(d => d.SlotIndex == slot);
        var devName = dev?.Name ?? $"Device Slot {slot}";

        return $"{devName}  Axis {AxisIndexToName(entry.AxisIndex)}";
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