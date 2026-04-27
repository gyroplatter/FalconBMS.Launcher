using System;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents one row in the editable in-memory binding model.
/// 
/// This row is created from a read-only KeyCatalogRow and is where user
/// binding state will live.
/// 
/// For this phase, binding values are copied from the Full key file defaults.
/// Later phases will overlay saved user bindings and device assignments.
/// </summary>
public sealed class BindingRow
{
    public int SourceLineNumber { get; init; }
    public string SourceRawLine { get; init; } = "";

    public BindingRowKind RowKind { get; init; }

    public string CallbackName { get; init; } = "";
    public int SoundId { get; set; }
    public int Unused { get; set; }

    public string KeyScancode { get; set; } = "";
    public int KeyModifierFlags { get; set; }

    public string ChordScancode { get; set; } = "";
    public int ChordModifierFlags { get; set; }

    public int Visibility { get; init; }
    public string Description { get; init; } = "";

    public string CategoryName { get; init; } = "";
    public string SectionName { get; init; } = "";

    public bool IsModified { get; set; }

    public bool IsHeader =>
        RowKind == BindingRowKind.CategoryHeader ||
        RowKind == BindingRowKind.SectionHeader;

    public bool IsCallback =>
        RowKind == BindingRowKind.EditableCallback ||
        RowKind == BindingRowKind.LockedCallback ||
        RowKind == BindingRowKind.HiddenCallback;

    public bool IsEditable =>
        RowKind == BindingRowKind.EditableCallback &&
        !string.Equals(CallbackName, "SimDoNothing", StringComparison.OrdinalIgnoreCase);
}