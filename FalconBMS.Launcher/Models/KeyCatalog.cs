using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents a fully parsed BMS key file as an ordered, read-only catalog of rows.
/// 
/// This catalog preserves the original file structure, including:
/// - category headers
/// - section headers
/// - callback rows
/// - locked and hidden rows
/// - remark rows
/// 
/// It is the authoritative source of "what exists" in the BMS input system,
/// but does not contain any user-editable binding state.
/// </summary>

public sealed class KeyCatalog
{
    public string AircraftProfile { get; init; } = "";
    public string SourcePath { get; init; } = "";

    public List<KeyCatalogRow> Rows { get; } = new();

    public int TotalLineCount { get; set; }
    public int ParsedRowCount => Rows.Count;
    public int SkippedLineCount => TotalLineCount - ParsedRowCount;

    public int CategoryHeaderCount => Rows.Count(x => x.RowKind == KeyCatalogRowKind.CategoryHeader);
    public int SectionHeaderCount => Rows.Count(x => x.RowKind == KeyCatalogRowKind.SectionHeader);
    public int EditableCallbackCount => Rows.Count(x => x.RowKind == KeyCatalogRowKind.EditableCallback);
    public int LockedCallbackCount => Rows.Count(x => x.RowKind == KeyCatalogRowKind.LockedCallback);
    public int HiddenCallbackCount => Rows.Count(x => x.RowKind == KeyCatalogRowKind.HiddenCallback);
    public int RemarkCount => Rows.Count(x => x.RowKind == KeyCatalogRowKind.Remark);

    public int CallbackRowCount => Rows.Count(x => x.IsCallback);
    public int VisibleGridRowCount => Rows.Count(x => x.RowKind != KeyCatalogRowKind.HiddenCallback);
}