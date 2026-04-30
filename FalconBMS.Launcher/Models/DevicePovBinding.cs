namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents one POV direction binding.
/// PovIndex is zero-based (POV0, POV1, etc).
/// Direction is the slot (0=Up,1=Right,2=Down,3=Left).
/// </summary>
public sealed class DevicePovBinding
{
    public int PovIndex { get; init; }

    public int Direction { get; init; }

    public string CallbackName { get; set; } = "";

    public string Invoke { get; set; } = "Default";

    public int SoundId { get; set; }
}