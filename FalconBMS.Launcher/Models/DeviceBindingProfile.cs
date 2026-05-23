using System;
using System.Collections.Generic;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents the in-memory binding profile for one DirectInput device.
/// Stable launcher identity is based on PID/VID/DurableDeviceKey.
/// DirectInput InstanceGuid is runtime-only and is used only when generating
/// BMS compatibility files for a currently connected device.
/// </summary>
public sealed class DeviceBindingProfile
{
    public int DiscoveryIndex { get; init; }

    /// <summary>
    /// Runtime-only DirectInput instance GUID for the current session.
    /// Do not use this as persistent binding identity.
    /// Offline saved profiles may carry Guid.Empty here.
    /// </summary>
    public Guid InstanceGuid { get; init; }

    /// <summary>
    /// Last DirectInput instance GUID seen for this profile.
    /// This is diagnostic/reconciliation metadata only; it is not stable identity.
    /// </summary>
    public Guid? LastSeenInstanceGuid { get; init; }

    /// <summary>
    /// True when this saved profile was matched to a currently discovered DirectInput device.
    /// False means the profile came from JSON but the physical device is offline/missing.
    /// </summary>
    public bool IsConnected { get; init; } = true;

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
    /// Stable filename identity segment for JSON names. Normal devices use PIDVID.
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