namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents one DirectInput button-to-callback binding for a device.
/// ButtonIndex is zero-based; ButtonNumber is the user-facing DX number.
/// </summary>
public sealed class DeviceButtonBinding
{
    public int ButtonIndex { get; init; }

    public int ButtonNumber => ButtonIndex + 1;

    public string CallbackName { get; set; } = "";
}