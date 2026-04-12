using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models.Keymapping;
using System.Collections.ObjectModel;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Base row type for the unified Keymapping grid.
/// Subclasses represent category headers, key rows, and axis rows.
/// </summary>
public abstract class KeymappingGridRowViewModel : ViewModelBase
{
    public abstract KeymappingGridRowType RowType { get; }

    public string SectionId { get; }
    public string CategoryName { get; }
    public string Mapping { get; }
    public string Key { get; }
    public string Visibility { get; }
    public string SearchText { get; }

    public virtual KeyAssgn? KeyRow => null;
    public virtual AxisRowViewModel? AxisRow => null;
    public virtual int? AssignedDeviceSlot => null;

    private ObservableCollection<KeymappingDeviceCellViewModel>? _deviceCells;

    /// <summary>
    /// Build device cells lazily so derived-row state is fully initialized first.
    /// This is required for axis rows, otherwise AxisRow is still null when the
    /// base constructor runs and live axis bars disappear.
    /// </summary>
    public ObservableCollection<KeymappingDeviceCellViewModel> DeviceCells =>
        _deviceCells ??= KeymappingDeviceCellViewModel.BuildForRow(this);

    public bool IsCategoryHeader => RowType == KeymappingGridRowType.CategoryHeader;
    public bool IsAxisRow => RowType == KeymappingGridRowType.AxisBinding;
    public bool IsKeyRow => RowType == KeymappingGridRowType.KeyBinding;

    protected KeymappingGridRowViewModel(
        string sectionId,
        string categoryName,
        string mapping,
        string key,
        string visibility,
        string searchText)
    {
        SectionId = sectionId;
        CategoryName = categoryName;
        Mapping = mapping;
        Key = key;
        Visibility = visibility;
        SearchText = searchText;
    }
}

public sealed class KeymappingCategoryHeaderRowViewModel : KeymappingGridRowViewModel
{
    private readonly KeyAssgn? _keyRow;

    public override KeymappingGridRowType RowType => KeymappingGridRowType.CategoryHeader;
    public override KeyAssgn? KeyRow => _keyRow;

    public KeymappingCategoryHeaderRowViewModel(
        string sectionId,
        string categoryName,
        string mapping,
        string key,
        string visibility,
        string searchText,
        KeyAssgn? keyRow)
        : base(sectionId, categoryName, mapping, key, visibility, searchText)
    {
        _keyRow = keyRow;
    }
}

public sealed class KeymappingKeyRowViewModel : KeymappingGridRowViewModel
{
    private readonly KeyAssgn _keyRow;

    public override KeymappingGridRowType RowType => KeymappingGridRowType.KeyBinding;
    public override KeyAssgn KeyRow => _keyRow;

    public KeymappingKeyRowViewModel(
        string sectionId,
        string categoryName,
        string mapping,
        string key,
        string visibility,
        string searchText,
        KeyAssgn keyRow)
        : base(sectionId, categoryName, mapping, key, visibility, searchText)
    {
        _keyRow = keyRow;
    }
}

public sealed class KeymappingAxisRowViewModel : KeymappingGridRowViewModel
{
    private readonly AxisRowViewModel _axisRow;
    private readonly int? _assignedDeviceSlot;

    public override KeymappingGridRowType RowType => KeymappingGridRowType.AxisBinding;
    public override AxisRowViewModel AxisRow => _axisRow;
    public override int? AssignedDeviceSlot => _assignedDeviceSlot;

    public KeymappingAxisRowViewModel(
        string sectionId,
        string categoryName,
        string mapping,
        string key,
        string visibility,
        string searchText,
        AxisRowViewModel axisRow,
        int? assignedDeviceSlot)
        : base(sectionId, categoryName, mapping, key, visibility, searchText)
    {
        _axisRow = axisRow;
        _assignedDeviceSlot = assignedDeviceSlot;
    }
}