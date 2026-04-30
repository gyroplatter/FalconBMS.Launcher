namespace FalconBMS.Launcher.Models;

/// <summary>
/// Identifies which optional control groups are supported by a logical axis.
/// The physical axis assignment is separate from these options.
/// </summary>
public enum DeviceAxisControlKind
{
    PhysicalAxis = 0,
    Saturation = 1,
    Deadzone = 2,
    Invert = 3,
    AfterburnerDetent = 4,
    IdleDetent = 5
}