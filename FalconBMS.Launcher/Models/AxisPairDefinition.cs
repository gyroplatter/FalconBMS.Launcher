using System;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Defines two logical BMS axes that represent one physical X/Y control.
///
/// Individual axis metadata remains owned by AxisDefinitionService.
/// This definition only describes the relationship between the horizontal
/// and vertical axes and how the combined control is presented.
/// </summary>
public sealed class AxisPairDefinition
{
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
    }

    public string PairId { get; }

    public string DisplayName { get; }

    public string PlotTitle { get; }

    public DeviceAxisDefinition HorizontalAxis { get; }

    public DeviceAxisDefinition VerticalAxis { get; }

    /// <summary>
    /// The first axis shown in the existing AxisPair window.
    ///
    /// AxisDefinitionService mapping order determines the display order.
    /// This preserves Pitch before Roll and Cursor X before Cursor Y.
    /// </summary>
    public DeviceAxisDefinition PrimaryAxis =>
        HorizontalAxis.MappingIndex <= VerticalAxis.MappingIndex
            ? HorizontalAxis
            : VerticalAxis;

    /// <summary>
    /// The second axis shown in the existing AxisPair window.
    /// </summary>
    public DeviceAxisDefinition SecondaryAxis =>
        ReferenceEquals(PrimaryAxis, HorizontalAxis)
            ? VerticalAxis
            : HorizontalAxis;

    public string PrimaryLogicalAxisName =>
        PrimaryAxis.LogicalAxisName;

    public string SecondaryLogicalAxisName =>
        SecondaryAxis.LogicalAxisName;

    public string PrimaryTitle =>
        PrimaryAxis.DisplayName + " axis";

    public string SecondaryTitle =>
        SecondaryAxis.DisplayName + " axis";

    public string PrimaryMapButtonText =>
        "Map " + PrimaryAxis.DisplayName;

    public string SecondaryMapButtonText =>
        "Map " + SecondaryAxis.DisplayName;

    public bool PrimaryControlsVerticalAxis =>
        ReferenceEquals(PrimaryAxis, VerticalAxis);

    public string LeftDirectionLabel =>
        HorizontalAxis.LeftLabel;

    public string RightDirectionLabel =>
        HorizontalAxis.RightLabel;

    public string BottomDirectionLabel =>
        VerticalAxis.LeftLabel;

    public string TopDirectionLabel =>
        VerticalAxis.RightLabel;
}