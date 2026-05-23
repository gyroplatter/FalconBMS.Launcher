using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services.Controls;

/// <summary>
/// Builds logical BMS axis rows for the Controls grid.
/// These rows come from AxisDefinitionService, not from the .key files.
///
/// Each axis row carries a SectionName so the combined grid builder can
/// interleave axis rows into the correct section cards alongside regular key rows,
/// rather than appending them as a separate flat AXIS block at the bottom.
///
/// Axes whose definition has no section placement (HasSectionPlacement == false)
/// are given the UnplacedSectionName constant so the combined builder can collect
/// them into a single fallback group on the bottom so they always display.
/// </summary>
public sealed class AxisControlsGridBuilderService
{
    /// <summary>
    /// SectionName assigned to axes that have no placement entry for the current
    /// aircraft profile. The combined grid builder puts these at the bottom of the
    /// Controls grid so they remain visible and configurable.
    /// </summary>
    public const string UnplacedSectionName = "Unplaced Axes";

    /// <summary>
    /// Builds one ControlGridRowViewModel per axis definition for the given aircraft
    /// profile, with device cell data populated from the supplied device profiles.
    /// </summary>
    public List<ControlGridRowViewModel> Build(
        string aircraftProfile,
        IEnumerable<DeviceBindingProfile> deviceProfiles)
    {
        var devices = deviceProfiles.ToList();

        return AxisDefinitionService
            .GetDefinitions(aircraftProfile)
            .Select(axisDefinition => CreateAxisRow(axisDefinition, devices))
            .ToList();
    }

    private static ControlGridRowViewModel CreateAxisRow(
        DeviceAxisDefinition axisDefinition,
        IReadOnlyList<DeviceBindingProfile> deviceProfiles)
    {
        // Use the axis's declared section, or the fallback if none is defined.
        string sectionName = axisDefinition.HasSectionPlacement
            ? axisDefinition.SectionName
            : UnplacedSectionName;

        var deviceCellsByDeviceKey = new Dictionary<string, ControlGridDeviceCellViewModel>();

        foreach (DeviceBindingProfile deviceProfile in deviceProfiles)
        {
            DeviceAxisBinding? binding = deviceProfile.AxisBindings.FirstOrDefault(axis =>
                string.Equals(axis.LogicalAxisName, axisDefinition.LogicalAxisName,
                    StringComparison.OrdinalIgnoreCase));

            bool hasAxisBinding = binding?.PhysicalAxisIndex is int;

            // Detent controls only make sense when a throttle axis is actually bound.
            bool showDetents = hasAxisBinding &&
                               axisDefinition.LayoutKind == DeviceAxisAssignmentLayoutKind.Throttle;

            deviceCellsByDeviceKey[deviceProfile.DurableDeviceKey] = new ControlGridDeviceCellViewModel
            {
                IsDeviceConnected = deviceProfile.IsConnected,
                DisplayText = binding?.PhysicalAxisIndex is int physicalAxisIndex
                    ? PhysicalAxisNameService.GetDisplayName(physicalAxisIndex)
                    : "",
                HasAxisBinding = hasAxisBinding,
                PhysicalAxisIndex = binding?.PhysicalAxisIndex ?? -1,
                Invert = binding?.Invert ?? false,
                ShowDetents = showDetents,
                IdleDetentFraction = (binding?.IdleDetent ?? DetentPosition.DefaultIdleDetent)
                                               / (double)DetentPosition.MaxAxisValue,
                AfterburnerDetentFraction = (binding?.AfterburnerDetent ?? DetentPosition.DefaultAfterburnerDetent)
                                               / (double)DetentPosition.MaxAxisValue
            };
        }

        return new ControlGridRowViewModel
        {
            SourceRow = null,
            RowKind = BindingRowKind.EditableCallback,
            // CategoryName is intentionally omitted — the combined grid builder
            // derives it from SectionName when grouping rows into section cards.
            SectionName = sectionName,
            Mapping = axisDefinition.DisplayName,
            IsAxisRow = true,
            AxisLogicalAxisName = axisDefinition.LogicalAxisName,
            DeviceCellsByDeviceKey = deviceCellsByDeviceKey
        };
    }
}