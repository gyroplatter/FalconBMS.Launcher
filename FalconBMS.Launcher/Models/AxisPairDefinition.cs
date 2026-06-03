namespace FalconBMS.Launcher.Models;

/// <summary>
/// Defines two logical BMS axes that should be displayed and edited as one physical X/Y control.
/// Example: Pitch + Roll are two BMS axes, but one physical flight stick.
/// </summary>
public sealed class AxisPairDefinition
{
    public string PairId { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string PrimaryLogicalAxisName { get; init; } = "";
    public string SecondaryLogicalAxisName { get; init; } = "";

    public string PrimaryTitle { get; init; } = "";
    public string SecondaryTitle { get; init; } = "";

    public string PrimaryMapButtonText { get; init; } = "";
    public string SecondaryMapButtonText { get; init; } = "";
}