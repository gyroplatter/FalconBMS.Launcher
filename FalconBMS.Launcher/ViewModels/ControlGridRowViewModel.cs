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
    public string Key { get; init; } = "";

    public bool IsCategoryHeader => RowKind == BindingRowKind.CategoryHeader;
    public bool IsSectionHeader => RowKind == BindingRowKind.SectionHeader;
    public bool IsRemark => RowKind == BindingRowKind.Remark;

    public bool IsEditable => SourceRow?.IsEditable == true;
}