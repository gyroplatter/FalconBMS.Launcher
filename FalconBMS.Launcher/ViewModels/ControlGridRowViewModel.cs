using FalconBMS.Launcher.Models;

namespace FalconBMS.Launcher.ViewModels;

public sealed class ControlGridRowViewModel : ViewModelBase
{
    public BindingRowKind RowKind { get; init; }

    public int SourceLineNumber { get; init; }

    public string Mapping { get; init; } = "";
    public string Key { get; init; } = "";

    public bool IsCategoryHeader => RowKind == BindingRowKind.CategoryHeader;
    public bool IsSectionHeader => RowKind == BindingRowKind.SectionHeader;
    public bool IsRemark => RowKind == BindingRowKind.Remark;
}