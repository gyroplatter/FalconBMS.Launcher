using System.Collections.Generic;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Stores aircraft-specific button and POV bindings for one device.
/// Axes and detents remain device-level, while buttons/POVs may differ by aircraft.
/// </summary>
public sealed class DeviceAircraftBindingProfile
{
    public string AircraftProfile { get; init; } = "";

    public List<DeviceButtonBinding> ButtonBindings { get; } = new();

    public List<DevicePovBinding> PovBindings { get; } = new();
}