using System;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents a single DirectInput game controller discovered at runtime,
/// including both transient (InstanceGuid) and durable (PID/VID) identity.
/// Used for matching devices to stock XML and future JSON binding files.
/// </summary>

public sealed class InputDeviceInfo
{
    public int DiscoveryIndex { get; init; }

    /// <summary>
    /// Runtime-only DirectInput instance GUID. Do not use this as persistent binding identity.
    /// </summary>
    public Guid InstanceGuid { get; init; }

    /// <summary>
    /// DirectInput product GUID. Used to derive VID/PID for durable device identity.
    /// </summary>
    public Guid ProductGuid { get; init; }

    public string InstanceName { get; init; } = "";
    public string ProductName { get; init; } = "";

    public string VendorIdHex { get; init; } = "";
    public string ProductIdHex { get; init; } = "";

    public string PidVid => ProductIdHex + VendorIdHex;

    /// <summary>
    /// One-based sequence number assigned only when multiple discovered devices share
    /// the same PID/VID. This supports vJoy-style devices that cannot be distinguished
    /// by PID/VID alone.
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

    public InputDeviceCapabilities Capabilities { get; init; } = InputDeviceCapabilities.Unknown;
}