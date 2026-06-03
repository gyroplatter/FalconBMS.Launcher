using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services.Controls;

/// <summary>
/// Central list of logical BMS axis pairs that should appear as one physical X/Y control in the UI.
/// This keeps the Controls table display decision out of STOCK/XML/JSON loading.
/// </summary>
public static class AxisPairDefinitionService
{
    private static readonly AxisPairDefinition[] Definitions =
    {
        new()
        {
            PairId = "FlightStick",
            DisplayName = "STICK: Pitch & Roll",
            PrimaryLogicalAxisName = "Pitch",
            SecondaryLogicalAxisName = "Roll",
            PrimaryTitle = "Pitch axis",
            SecondaryTitle = "Roll axis",
            PrimaryMapButtonText = "Map Pitch",
            SecondaryMapButtonText = "Map Roll"
        }
    };

    public static IReadOnlyList<AxisPairDefinition> All => Definitions;

    public static AxisPairDefinition? FindByLogicalAxisNames(string primaryLogicalAxisName, string secondaryLogicalAxisName)
    {
        return Definitions.FirstOrDefault(definition =>
            string.Equals(definition.PrimaryLogicalAxisName, primaryLogicalAxisName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(definition.SecondaryLogicalAxisName, secondaryLogicalAxisName, StringComparison.OrdinalIgnoreCase));
    }
}