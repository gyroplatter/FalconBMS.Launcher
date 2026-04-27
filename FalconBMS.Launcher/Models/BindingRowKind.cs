namespace FalconBMS.Launcher.Models;

/// <summary>
/// Defines the type of row used by the editable in-memory binding model.
/// 
/// These kinds mirror the read-only KeyCatalog row kinds, but belong to the
/// user-editable binding layer.
/// </summary>
public enum BindingRowKind
{
    CategoryHeader = 0,
    SectionHeader = 1,
    EditableCallback = 2,
    LockedCallback = 3,
    HiddenCallback = 4,
    Remark = 5,
    Other = 6
}