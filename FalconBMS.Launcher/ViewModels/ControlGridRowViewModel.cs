using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;

namespace FalconBMS.Launcher.ViewModels;

public sealed class ControlGridRowViewModel : ViewModelBase
{
    public BindingRow? SourceRow { get; init; }

    public BindingRowKind RowKind { get; init; }

    public int SourceLineNumber { get; init; }

    public string CategoryName { get; init; } = "";
    public string SectionName { get; init; } = "";

    public string Mapping { get; init; } = "";

    private string _key = "";
    public string Key
    {
        get => _key;
        private set => Set(ref _key, value);
    }

    public bool IsCategoryHeader => RowKind == BindingRowKind.CategoryHeader;
    public bool IsSectionHeader => RowKind == BindingRowKind.SectionHeader;
    public bool IsRemark => RowKind == BindingRowKind.Remark;

    public bool IsEditable => SourceRow?.IsEditable == true;

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