using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.ViewModels;
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

    public List<ControlGridRowViewModel> Build()
    {
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
            .Select(CreateAxisRow));

        return rows;
    }

    private static ControlGridRowViewModel CreateAxisRow(DeviceAxisDefinition axisDefinition)
    {
        return new ControlGridRowViewModel
        {
            SourceRow = null,
            RowKind = BindingRowKind.Callback,
            CategoryName = AxisCategoryName,
            SectionName = AxisCategoryName,
            Mapping = GetDisplayName(axisDefinition.LogicalAxisName),
            IsAxisRow = true,
            AxisLogicalAxisName = axisDefinition.LogicalAxisName
        };
    }

    private static string GetDisplayName(string logicalAxisName)
    {
        return logicalAxisName.Replace("_", " ");
    }
}