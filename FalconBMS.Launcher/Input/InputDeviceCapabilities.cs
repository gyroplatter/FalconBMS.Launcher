namespace FalconBMS.Launcher.Models;

/// <summary>
/// Stores DirectInput capability counts for one discovered device.
/// These counts are runtime facts from the hardware/driver and are used to
/// avoid old artificial limits on buttons, POV hats, and axes.
/// </summary>
public sealed class InputDeviceCapabilities
{
    public int AxisCount { get; init; }

    public int ButtonCount { get; init; }

    public int PovCount { get; init; }

    public bool WasReadSuccessfully { get; init; }

    public static InputDeviceCapabilities Unknown { get; } = new();
}