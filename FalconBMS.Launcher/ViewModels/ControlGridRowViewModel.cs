using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using System.Collections.Generic;

namespace FalconBMS.Launcher.ViewModels;

public sealed class ControlGridRowViewModel : ViewModelBase
{
    public BindingRow? SourceRow { get; init; }

    public BindingRowKind RowKind { get; init; }

    public int SourceLineNumber { get; init; }

    public string CategoryName { get; init; } = "";
    public string SectionName { get; init; } = "";

    public string Mapping { get; init; } = "";

    public bool IsAxisRow { get; init; }
    public string AxisLogicalAxisName { get; init; } = "";

    // Axis pair rows are user-facing grouping rows.
    // They display one physical X/Y control in the Controls table while still saving
    // as two separate logical BMS axis bindings underneath.
    public bool IsAxisPairRow { get; init; }
    public AxisPairDefinition? AxisPairDefinition { get; init; }

    private string _key = "";
    public string Key
    {
        get => _key;
        private set => Set(ref _key, value);
    }

    public Dictionary<string, ControlGridDeviceCellViewModel> DeviceCellsByDeviceKey { get; init; } = new();

    public bool IsCategoryHeader => RowKind == BindingRowKind.CategoryHeader;
    public bool IsSectionHeader => RowKind == BindingRowKind.SectionHeader;
    public bool IsRemark => RowKind == BindingRowKind.Remark;

    // Axis rows and axis pair rows are editable even though they do not come from a .key BindingRow.
    public bool IsEditable => IsAxisRow || IsAxisPairRow || SourceRow?.IsEditable == true;

    public void RefreshFromSource()
    {
        if (SourceRow is null || !SourceRow.IsCallback)
        {
            Key = "";
            return;
        }

        Key = KeyAssgn.GetKeyAssignmentStatus(
            SourceRow.KeyScancode,
            SourceRow.KeyModifierFlags,
            SourceRow.ChordScancode,
            SourceRow.ChordModifierFlags);
    }
}