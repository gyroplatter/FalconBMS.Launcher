namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents one DirectInput button-to-callback binding for a device.
/// ButtonIndex is zero-based; ButtonNumber is the user-facing DX number.
/// AssignmentIndex is the zero-based slot inside the XML button assignment list.
/// </summary>
public sealed class DeviceButtonBinding
{
    public int ButtonIndex { get; init; }

    public int ButtonNumber => ButtonIndex + 1;

    public int AssignmentIndex { get; init; }

    public string CallbackName { get; set; } = "";

    public string Invoke { get; set; } = "Default";

    public int SoundId { get; set; }
}