using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents the editable binding rows for one aircraft profile.
/// 
/// Examples:
/// - F-16
/// - F-15ABCD
/// 
/// Rows preserve the same order as the source KeyCatalog so the UI can
/// rebuild the familiar keymapping structure while editing BindingRow state.
/// </summary>
public sealed class BindingAircraftProfile
{
    public string AircraftProfile { get; init; } = "";
    public string SourceCatalogPath { get; init; } = "";

    public List<BindingRow> Rows { get; } = new();

    public int TotalRows => Rows.Count;
    public int VisibleRows => Rows.Count(x => x.RowKind != BindingRowKind.HiddenCallback);
    public int CallbackRows => Rows.Count(x => x.IsCallback);
    public int EditableRows => Rows.Count(x => x.RowKind == BindingRowKind.EditableCallback);
    public int LockedRows => Rows.Count(x => x.RowKind == BindingRowKind.LockedCallback);
    public int HiddenRows => Rows.Count(x => x.RowKind == BindingRowKind.HiddenCallback);
    public int CategoryHeaders => Rows.Count(x => x.RowKind == BindingRowKind.CategoryHeader);
    public int SectionHeaders => Rows.Count(x => x.RowKind == BindingRowKind.SectionHeader);
    public int Remarks => Rows.Count(x => x.RowKind == BindingRowKind.Remark);
}