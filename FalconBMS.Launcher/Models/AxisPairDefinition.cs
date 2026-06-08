using System;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Defines one or two logical BMS axes that share the advanced axis assignment
/// window.
///
/// Two-axis definitions represent one physical X/Y control, such as:
/// - Pitch and Roll
/// - Cursor X and Cursor Y
///
/// Single-axis definitions use the same window and response graph, but only
/// display the primary axis editor and a one-dimensional position plot.
/// </summary>
public sealed class AxisPairDefinition
{
    private readonly DeviceAxisDefinition? _secondaryAxis;

    /// <summary>
    /// Creates a two-axis X/Y definition.
    /// </summary>
    public AxisPairDefinition(
        string pairId,
        string displayName,
        string plotTitle,
        DeviceAxisDefinition horizontalAxis,
        DeviceAxisDefinition verticalAxis)
    {
        PairId = pairId;
        DisplayName = displayName;
        PlotTitle = plotTitle;

        HorizontalAxis = horizontalAxis;
        VerticalAxis = verticalAxis;

        PrimaryAxis =
            horizontalAxis.MappingIndex <= verticalAxis.MappingIndex
                ? horizontalAxis
                : verticalAxis;

        _secondaryAxis =
            ReferenceEquals(PrimaryAxis, horizontalAxis)
                ? verticalAxis
                : horizontalAxis;

        PrimaryControlsVerticalAxis =
            ReferenceEquals(
                PrimaryAxis,
                VerticalAxis);
    }

    /// <summary>
    /// Creates a single-axis definition that uses the advanced axis assignment
    /// window without displaying a secondary axis section.
    /// </summary>
    public AxisPairDefinition(
        string pairId,
        string displayName,
        string plotTitle,
        DeviceAxisDefinition primaryAxis)
    {
        PairId = pairId;
        DisplayName = displayName;
        PlotTitle = plotTitle;

        PrimaryAxis = primaryAxis;

        // A single-axis definition uses the horizontal live-position plot.
        HorizontalAxis = primaryAxis;
        VerticalAxis = primaryAxis;

        _secondaryAxis = null;
        PrimaryControlsVerticalAxis = false;
    }

    public string PairId { get; }

    public string DisplayName { get; }

    public string PlotTitle { get; }

    public string WindowTitle =>
        HasSecondaryAxis
            ? "Assign Axis Pair"
            : "Assign " + DisplayName + " Axis";

    public DeviceAxisDefinition HorizontalAxis { get; }

    public DeviceAxisDefinition VerticalAxis { get; }

    public DeviceAxisDefinition PrimaryAxis { get; }

    public DeviceAxisDefinition SecondaryAxis =>
        _secondaryAxis
        ?? throw new InvalidOperationException(
            $"Axis definition '{PairId}' does not have a secondary axis.");

    public bool HasSecondaryAxis =>
        _secondaryAxis is not null;

    public string PrimaryLogicalAxisName =>
        PrimaryAxis.LogicalAxisName;

    public string SecondaryLogicalAxisName =>
        _secondaryAxis?.LogicalAxisName ?? "";

    public string PrimaryTitle =>
        PrimaryAxis.DisplayName + " axis";

    public string SecondaryTitle =>
        _secondaryAxis is null
            ? ""
            : _secondaryAxis.DisplayName + " axis";

    public string PrimaryMapButtonText =>
        "Map " + PrimaryAxis.DisplayName;

    public string SecondaryMapButtonText =>
        _secondaryAxis is null
            ? ""
            : "Map " + _secondaryAxis.DisplayName;

    public bool PrimaryControlsVerticalAxis { get; }

    public string LeftDirectionLabel =>
        HorizontalAxis.LeftLabel;

    public string RightDirectionLabel =>
        HorizontalAxis.RightLabel;

    public string BottomDirectionLabel =>
        HasSecondaryAxis
            ? VerticalAxis.LeftLabel
            : "";

    public string TopDirectionLabel =>
        HasSecondaryAxis
            ? VerticalAxis.RightLabel
            : "";
}