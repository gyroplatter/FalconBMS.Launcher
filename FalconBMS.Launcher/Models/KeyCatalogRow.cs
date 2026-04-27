using System;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents a single parsed row from a BMS key file.
/// 
/// Each row corresponds to one line in the source file and retains:
/// - original raw text
/// - callback identifiers and metadata
/// - visibility flags
/// - category and section context
/// 
/// Rows may represent headers, callbacks, locked entries, hidden entries,
/// or remarks, and are used to reconstruct the original structure in the UI.
/// 
/// This model does not store user binding choices; it is purely descriptive.
/// </summary>

public sealed class KeyCatalogRow
{
    public int LineNumber { get; init; }
    public string RawLine { get; init; } = "";

    public KeyCatalogRowKind RowKind { get; init; }

    public string CallbackName { get; init; } = "";
    public int SoundId { get; init; }
    public int Unused { get; init; }

    public string KeyScancode { get; init; } = "";
    public int KeyModifierFlags { get; init; }

    public string ChordScancode { get; init; } = "";
    public int ChordModifierFlags { get; init; }

    public int Visibility { get; init; }
    public string Description { get; init; } = "";

    public string CategoryName { get; init; } = "";
    public string SectionName { get; init; } = "";

    public bool IsHeader =>
        RowKind == KeyCatalogRowKind.CategoryHeader ||
        RowKind == KeyCatalogRowKind.SectionHeader;

    public bool IsCallback =>
        RowKind == KeyCatalogRowKind.EditableCallback ||
        RowKind == KeyCatalogRowKind.LockedCallback ||
        RowKind == KeyCatalogRowKind.HiddenCallback;

    public bool IsEditable =>
        RowKind == KeyCatalogRowKind.EditableCallback &&
        !string.Equals(CallbackName, "SimDoNothing", StringComparison.OrdinalIgnoreCase);
}