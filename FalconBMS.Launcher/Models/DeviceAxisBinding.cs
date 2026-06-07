namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents one logical BMS axis binding for a discovered device.
/// The supported options are determined by DeviceAxisDefinition, not by this binding itself.
/// </summary>
public sealed class DeviceAxisBinding
{
    public string LogicalAxisName { get; init; } = "";

    /// <summary>
    /// Zero-based physical axis index from the device XML/input layer.
    /// Null means the logical axis is currently unassigned for this device.
    /// </summary>
    public int? PhysicalAxisIndex { get; set; }

    public string Saturation { get; set; } = "None";
    public string Deadzone { get; set; } = "None";

    /// <summary>
    /// BMS exponential-axis value used as x in:
    /// output = (input^3 * (x - 1) + input) / x
    ///
    /// 1 means no curve.
    /// The AxisPair UI currently exposes values 1 through 5.
    /// </summary>
    public int Curve { get; set; } = 1;

    public bool Invert { get; set; }

    public int? AfterburnerDetent { get; set; }
    public int? IdleDetent { get; set; }
}