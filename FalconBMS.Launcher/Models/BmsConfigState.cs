using System;
using System.Collections.Generic;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Aggregate model for config data read from FalconBMS files, including device sorting and axis mapping state.
/// </summary>

public sealed class BmsConfigState
{
    public required IReadOnlyList<DeviceSortingEntry> DeviceOrder { get; init; }
    public required AxisMappingDatData AxisMappings { get; init; }
}

public sealed class DeviceSortingEntry
{
    public required int SlotIndex { get; init; }         // 0-based in DeviceSorting.txt
    public required Guid ProductGuid { get; init; }      // Product GUID style
    public required string Name { get; init; }
}

public sealed class AxisMappingDatData
{
    public required int DeviceCount { get; init; }       // header deviceCount
    public required int HeaderJoyNum { get; init; }      // header joy num (slot+2) or -1
    public required Guid HeaderInstanceGuid { get; init; }
    public required IReadOnlyList<AxisMapEntry> Entries { get; init; } // 30 entries
}

public sealed class AxisMapEntry
{
    public required int Index { get; init; }       // 0..29
    public required int JoyNum { get; init; }      // slot+2 or -1
    public required int AxisIndex { get; init; }   // 0..7 (X,Y,Z,Rx,Ry,Rz,Sl0,Sl1) or -1
    public required int Deadzone { get; init; }    // usually 100
    public required int Saturation { get; init; }  // usually -1
}