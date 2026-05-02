using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services.Controls;

public sealed class KeyControlsGridBuilderService
{
    public List<ControlGridRowViewModel> Build(BindingAircraftProfile? profile)
    {
        if (profile is null)
            return new List<ControlGridRowViewModel>();

        return profile.Rows
            .Where(row => row.RowKind != BindingRowKind.HiddenCallback)
            .Where(row => row.RowKind != BindingRowKind.Other)
            .OrderBy(row => row.SourceLineNumber)
            .Select(CreateRow)
            .ToList();
    }

    private static ControlGridRowViewModel CreateRow(BindingRow row)
    {
        var viewModel = new ControlGridRowViewModel
        {
            SourceRow = row,
            RowKind = row.RowKind,
            SourceLineNumber = row.SourceLineNumber,
            CategoryName = row.CategoryName,
            SectionName = row.SectionName,
            Mapping = GetMappingText(row)
        };

        viewModel.RefreshFromSource();

        return viewModel;
    }

    private static string GetMappingText(BindingRow row)
    {
        if (row.RowKind == BindingRowKind.CategoryHeader)
            return row.CategoryName;

        if (row.RowKind == BindingRowKind.SectionHeader)
            return row.SectionName;

        return row.Description;
    }

    private static string GetKeyText(BindingRow row)
    {
        if (!row.IsCallback)
            return "";

        return KeyAssgn.GetKeyAssignmentStatus(
            row.KeyScancode,
            row.KeyModifierFlags,
            row.ChordScancode,
            row.ChordModifierFlags);
    }
}