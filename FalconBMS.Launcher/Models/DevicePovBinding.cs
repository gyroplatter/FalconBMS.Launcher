namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents one POV direction binding.
/// PovIndex is zero-based (POV0, POV1, etc).
/// Direction is the legacy BMS 8-way POV slot:
/// 0=Up, 2=Right, 4=Down, 6=Left.
/// Odd values are diagonals.
/// </summary>
public sealed class DevicePovBinding
{
    public int PovIndex { get; init; }

    public int Direction { get; init; }

    public string CallbackName { get; set; } = "";

    public string Invoke { get; set; } = "Default";

    public int SoundId { get; set; }
}