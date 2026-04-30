using System;
using System.Collections.Generic;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents the in-memory binding profile for one discovered DirectInput device.
/// Device-level data includes identity, source, capabilities, axes, and detents.
/// Aircraft-specific data lives under AircraftProfiles.
/// </summary>
public sealed class DeviceBindingProfile
{
    public int DiscoveryIndex { get; init; }

    /// <summary>
    /// Runtime-only DirectInput instance GUID. Do not use this as persistent binding identity.
    /// </summary>
    public Guid InstanceGuid { get; init; }

    /// <summary>
    /// DirectInput product GUID used to derive PID/VID for durable device identity.
    /// </summary>
    public Guid ProductGuid { get; init; }

    public string InstanceName { get; init; } = "";
    public string ProductName { get; init; } = "";

    public string VendorIdHex { get; init; } = "";
    public string ProductIdHex { get; init; } = "";
    public string PidVid => ProductIdHex + VendorIdHex;

    /// <summary>
    /// One-based sequence number assigned only when multiple discovered devices
    /// share the same PID/VID.
    /// </summary>
    public int? DuplicatePidVidSequenceNumber { get; init; }

    public bool HasDuplicatePidVidSequence => DuplicatePidVidSequenceNumber.HasValue;

    /// <summary>
    /// Stable filename identity segment for future JSON names. Normal devices use PIDVID.
    /// Duplicate PID/VID devices use PIDVID_sequence.
    /// </summary>
    public string DurableDeviceKey =>
        HasDuplicatePidVidSequence
            ? $"{PidVid}_{DuplicatePidVidSequenceNumber!.Value}"
            : PidVid;

    public int AxisCount { get; init; }
    public int ButtonCount { get; init; }
    public int PovCount { get; init; }
    public bool CapabilitiesReadSuccessfully { get; init; }

    public DeviceBindingSource Source { get; init; }

    public string? StockXmlPath { get; init; }
    public string? JsonPath { get; init; }

    public List<DeviceAxisBinding> AxisBindings { get; } = new();

    public List<DeviceAircraftBindingProfile> AircraftProfiles { get; } = new();
}