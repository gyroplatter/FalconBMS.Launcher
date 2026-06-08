using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services.Controls;

/// <summary>
/// Defines the axes that use the advanced axis assignment window.
///
/// Two-axis definitions describe one physical X/Y control.
/// Single-axis definitions reuse the same window with the secondary editor hidden.
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
            verticalLogicalAxisName: "Cursor_Y"),

        CreateSingleAxis(
            pairId: "RudderYaw",
            displayName: "Rudder / Yaw",
            plotTitle: "Rudder Position",
            logicalAxisName: "Yaw")
    };

    public static IReadOnlyList<AxisPairDefinition> All =>
        Definitions;

    public static AxisPairDefinition? FindByLogicalAxisNames(
        string primaryLogicalAxisName,
        string secondaryLogicalAxisName)
    {
        return Definitions.FirstOrDefault(
            definition =>
                definition.HasSecondaryAxis &&
                string.Equals(
                    definition.PrimaryLogicalAxisName,
                    primaryLogicalAxisName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    definition.SecondaryLogicalAxisName,
                    secondaryLogicalAxisName,
                    StringComparison.OrdinalIgnoreCase));
    }

    public static AxisPairDefinition? FindByLogicalAxisName(
        string logicalAxisName)
    {
        if (string.IsNullOrWhiteSpace(logicalAxisName))
            return null;

        return Definitions.FirstOrDefault(
            definition =>
                string.Equals(
                    definition.PrimaryLogicalAxisName,
                    logicalAxisName,
                    StringComparison.OrdinalIgnoreCase) ||
                definition.HasSecondaryAxis &&
                string.Equals(
                    definition.SecondaryLogicalAxisName,
                    logicalAxisName,
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
            AxisDefinitionService.Find(
                horizontalLogicalAxisName)
            ?? throw new InvalidOperationException(
                $"Axis pair '{pairId}' references unknown horizontal axis " +
                $"'{horizontalLogicalAxisName}'.");

        DeviceAxisDefinition verticalAxis =
            AxisDefinitionService.Find(
                verticalLogicalAxisName)
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

    private static AxisPairDefinition CreateSingleAxis(
        string pairId,
        string displayName,
        string plotTitle,
        string logicalAxisName)
    {
        DeviceAxisDefinition primaryAxis =
            AxisDefinitionService.Find(
                logicalAxisName)
            ?? throw new InvalidOperationException(
                $"Advanced axis definition '{pairId}' references unknown axis " +
                $"'{logicalAxisName}'.");

        return new AxisPairDefinition(
            pairId,
            displayName,
            plotTitle,
            primaryAxis);
    }
}