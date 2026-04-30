namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents one POV hat binding for a device.
/// POV index is zero-based; POV number is the user-facing POV number.
/// </summary>
public sealed class DevicePovBinding
{
    public int PovIndex { get; init; }

    public int PovNumber => PovIndex + 1;

    public string Direction { get; set; } = "";
    public string CallbackName { get; set; } = "";
}