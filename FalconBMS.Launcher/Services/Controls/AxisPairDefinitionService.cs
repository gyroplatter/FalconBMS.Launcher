using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services.Controls;

/// <summary>
/// Defines which individual logical axes represent one physical X/Y control.
///
/// AxisDefinitionService remains the source of truth for each individual axis.
/// This service only defines the relationship between the two axes.
/// </summary>
public static class AxisPairDefinitionService
{
    private static readonly AxisPairDefinition[] Definitions =
    {
        CreatePair(
            pairId: "FlightStick",
            displayName: "STICK: Pitch & Roll",
            plotTitle: "Stick Position",
            horizontalLogicalAxisName: "Roll",
            verticalLogicalAxisName: "Pitch"),

        CreatePair(
            pairId: "ThrottleCursor",
            displayName: "TQS: Cursor X & Y",
            plotTitle: "Cursor Position",
            horizontalLogicalAxisName: "Cursor_X",
            verticalLogicalAxisName: "Cursor_Y")
    };

    public static IReadOnlyList<AxisPairDefinition> All =>
        Definitions;

    public static AxisPairDefinition? FindByLogicalAxisNames(
        string primaryLogicalAxisName,
        string secondaryLogicalAxisName)
    {
        return Definitions.FirstOrDefault(definition =>
            string.Equals(
                definition.PrimaryLogicalAxisName,
                primaryLogicalAxisName,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                definition.SecondaryLogicalAxisName,
                secondaryLogicalAxisName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static AxisPairDefinition CreatePair(
        string pairId,
        string displayName,
        string plotTitle,
        string horizontalLogicalAxisName,
        string verticalLogicalAxisName)
    {
        DeviceAxisDefinition horizontalAxis =
            AxisDefinitionService.Find(horizontalLogicalAxisName)
            ?? throw new InvalidOperationException(
                $"Axis pair '{pairId}' references unknown horizontal axis " +
                $"'{horizontalLogicalAxisName}'.");

        DeviceAxisDefinition verticalAxis =
            AxisDefinitionService.Find(verticalLogicalAxisName)
            ?? throw new InvalidOperationException(
                $"Axis pair '{pairId}' references unknown vertical axis " +
                $"'{verticalLogicalAxisName}'.");

        return new AxisPairDefinition(
            pairId,
            displayName,
            plotTitle,
            horizontalAxis,
            verticalAxis);
    }
}