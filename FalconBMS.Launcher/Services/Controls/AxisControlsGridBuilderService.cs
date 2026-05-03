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
/// </summary>
public sealed class AxisControlsGridBuilderService
{
    private const string AxisCategoryName = "AXIS";

    private readonly AxisDefinitionService _axisDefinitionService = new();

    public List<ControlGridRowViewModel> Build(IEnumerable<DeviceBindingProfile> deviceProfiles)
    {
        var devices = deviceProfiles.ToList();

        var rows = new List<ControlGridRowViewModel>
        {
            new()
            {
                RowKind = BindingRowKind.CategoryHeader,
                CategoryName = AxisCategoryName,
                SectionName = AxisCategoryName,
                Mapping = AxisCategoryName
            }
        };

        rows.AddRange(_axisDefinitionService
            .GetDefinitions()
            .Select(axisDefinition => CreateAxisRow(axisDefinition, devices)));

        return rows;
    }

    private static ControlGridRowViewModel CreateAxisRow(
        DeviceAxisDefinition axisDefinition,
        IReadOnlyList<DeviceBindingProfile> deviceProfiles)
    {
        var deviceCellsByDeviceKey = new Dictionary<string, ControlGridDeviceCellViewModel>();

        foreach (DeviceBindingProfile deviceProfile in deviceProfiles)
        {
            DeviceAxisBinding? binding = deviceProfile.AxisBindings.FirstOrDefault(axis =>
                string.Equals(axis.LogicalAxisName, axisDefinition.LogicalAxisName, StringComparison.OrdinalIgnoreCase));

            bool hasAxisBinding = binding?.PhysicalAxisIndex is int;
            bool showDetents = hasAxisBinding &&
                               axisDefinition.LayoutKind == DeviceAxisAssignmentLayoutKind.Throttle;

            deviceCellsByDeviceKey[deviceProfile.DurableDeviceKey] = new ControlGridDeviceCellViewModel
            {
                DisplayText = binding?.PhysicalAxisIndex is int physicalAxisIndex
                    ? PhysicalAxisNameService.GetDisplayName(physicalAxisIndex)
                    : "",
                HasAxisBinding = hasAxisBinding,
                PhysicalAxisIndex = binding?.PhysicalAxisIndex ?? -1,
                Invert = binding?.Invert ?? false,
                ShowDetents = showDetents,
                IdleDetentFraction = (binding?.IdleDetent ?? DetentPosition.DefaultIdleDetent) / (double)DetentPosition.MaxAxisValue,
                AfterburnerDetentFraction = (binding?.AfterburnerDetent ?? DetentPosition.DefaultAfterburnerDetent) / (double)DetentPosition.MaxAxisValue
            };
        }

        return new ControlGridRowViewModel
        {
            SourceRow = null,
            RowKind = BindingRowKind.EditableCallback,
            CategoryName = AxisCategoryName,
            SectionName = AxisCategoryName,
            Mapping = axisDefinition.DisplayName,
            IsAxisRow = true,
            AxisLogicalAxisName = axisDefinition.LogicalAxisName,
            DeviceCellsByDeviceKey = deviceCellsByDeviceKey
        };
    }

}