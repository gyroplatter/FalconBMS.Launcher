using System;
using System.Collections.Generic;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents the in-memory binding profile for one discovered DirectInput device.
/// This shell stores device identity and source information only; button, POV,
/// and axis binding extraction will be added after XML/JSON parsing is implemented.
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

    public DeviceBindingSource Source { get; init; }

    public string? StockXmlPath { get; init; }
    public string? JsonPath { get; init; }

    public List<object> ButtonBindings { get; } = new();
    public List<object> PovBindings { get; } = new();
    public List<object> AxisBindings { get; } = new();
}