using System;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Record that describes an already-existing axis binding found in FalconBMS config.
/// </summary>

public sealed record AxisExistingBinding(
    string DeviceName,
    Guid? ProductGuid,
    int PhysicalAxisIndex,
    bool Invert,
    AxCurve Deadzone,
    AxCurve Saturation,
    DetentPosition? Detents = null
);