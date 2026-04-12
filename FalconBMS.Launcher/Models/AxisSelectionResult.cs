namespace FalconBMS.Launcher.Models;

/// <summary>
/// Result object produced by axis detection/assignment, containing selected device/axis and tuning choices.
/// </summary>

public sealed class AxisSelectionResult
{
    public required string DeviceName { get; init; }
    public required System.Guid DeviceInstanceGuid { get; init; }
    public required System.Guid DeviceProductGuid { get; init; }

    // 0..n physical axis index we map in XML (we will map in this order):
    // 0:X, 1:Y, 2:Z, 3:Rx, 4:Ry, 5:Rz, 6:Slider0, 7:Slider1
    public required int PhysicalAxisIndex { get; init; }

    public bool Invert { get; init; }

    public AxCurve Deadzone { get; init; } = AxCurve.None;
    public AxCurve Saturation { get; init; } = AxCurve.None;
}