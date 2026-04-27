namespace FalconBMS.Launcher.Models;

/// <summary>
/// Defines the classification of a parsed key file row.
/// 
/// The row kind determines how the row is interpreted and displayed,
/// such as whether it is a header, an editable callback, a locked entry,
/// or a hidden row.
/// 
/// This classification is derived from key file visibility flags and
/// description formatting rules.
/// </summary>

public enum KeyCatalogRowKind
{
    CategoryHeader = 0,
    SectionHeader = 1,
    EditableCallback = 2,
    LockedCallback = 3,
    HiddenCallback = 4,
    Remark = 5,
    Other = 6
}