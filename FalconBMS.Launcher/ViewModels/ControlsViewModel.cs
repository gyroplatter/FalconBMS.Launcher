using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Services.Controls;
using FalconBMS.Launcher.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace FalconBMS.Launcher.ViewModels;

public sealed class ControlsViewModel : ViewModelBase
{
    private const string AllActionsLabel = "ALL";
    private const string AllAxesLabel = "ALL AXES";
    private const string AxisCategoryName = "AXIS";
    private const string UnassignedKeysLabel = "UNASSIGNED KEYS";

    private readonly KeyControlsGridBuilderService _keyGridBuilder = new();
    private readonly AxisControlsGridBuilderService _axisGridBuilder = new();
    private readonly BindingJsonImportExportService _bindingJsonImportExport = new();
    private readonly UnassignedKeyboardKeyService _unassignedKeyboardKeyService = new();

    private readonly List<ControlGridRowViewModel> _allRows = new();

    private Func<string?>? _getBaseDir;
    private Func<BindingModel>? _getBindingModel;
    private Func<Window?>? _getOwnerWindow;
    private Action? _reloadBindingModel;

    public ObservableCollection<BindingAircraftProfile> Profiles { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<ControlGridRowViewModel> Rows { get; } = new();

    public ObservableCollection<DeviceBindingProfile> DeviceColumns { get; } = new();

    // Separate left-nav list so the UI can show friendly device names
    // without changing the real DeviceColumns collection used by the table.
    public ObservableCollection<ControlsDeviceNavigationItem> DeviceNavigationItems { get; } = new();

    public IReadOnlyList<BindingRow> SelectedProfileRows =>
            SelectedProfile?.Rows ?? Array.Empty<BindingRow>().ToList();

    // Tracks whether any binding has been modified since the last PrepareForLaunch.
    // Used by MainViewModel to skip the on-close save if nothing changed.
    private bool _isDirty;
    public bool IsDirty => _isDirty;

    private ControlGridRowViewModel? _selectedRow;

    public ControlGridRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => Set(ref _selectedRow, value);
    }

    private BindingAircraftProfile? _selectedProfile;
    public BindingAircraftProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value)) return;

            RebuildRowsFromSelectedProfile();
            RebuildCategories();
            SelectedCategory = AllActionsLabel;
            ApplyFilters();
        }
    }

    private string _selectedCategory = AllActionsLabel;
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!Set(ref _selectedCategory, value)) return;

            OnPropertyChanged(nameof(IsUnassignedKeysCategory));
            OnPropertyChanged(nameof(HelperText));

            ApplyFilters();
        }
    }

    private ControlsDeviceNavigationItem? _selectedDeviceNavigationItem;
    public ControlsDeviceNavigationItem? SelectedDeviceNavigationItem
    {
        get => _selectedDeviceNavigationItem;
        set => Set(ref _selectedDeviceNavigationItem, value);
    }

    private string _filterText = "";
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!Set(ref _filterText, value ?? "")) return;
            ApplyFilters();
        }
    }

    public string SummaryText
    {
        get
        {
            if (SelectedProfile is null)
                return "No binding profile loaded.";

            if (IsUnassignedKeysCategory)
                return $"{SelectedProfile.AircraftProfile}: {Rows.Count} unassigned keys";

            return $"{SelectedProfile.AircraftProfile}: {Rows.Count} visible rows";
        }
    }

    public bool IsUnassignedKeysCategory =>
        string.Equals(SelectedCategory, UnassignedKeysLabel, StringComparison.OrdinalIgnoreCase);

    public string HelperText =>
        IsUnassignedKeysCategory
            ? $"These {Rows.Count} keyboard combinations are not currently assigned to this aircraft."
            : "Double-click a row to map that control to a key, button, axis, or POV hat. Press a key, button, or hat direction to jump to its current assignment. Italicized rows are not editable";

    public RelayCommand ClearFilterCommand { get; }
    public RelayCommand ImportBindingsCommand { get; }
    public RelayCommand ExportBindingsCommand { get; }

    public ControlsViewModel()
    {
        ClearFilterCommand = new RelayCommand(ClearFilters, () => true);
        ImportBindingsCommand = new RelayCommand(ImportBindings, CanImportOrExportBindings);
        ExportBindingsCommand = new RelayCommand(ExportBindings, CanImportOrExportBindings);
    }

    public void ConfigureImportExport(
        Func<string?> getBaseDir,
        Func<BindingModel> getBindingModel,
        Func<Window?> getOwnerWindow,
        Action reloadBindingModel)
    {
        _getBaseDir = getBaseDir;
        _getBindingModel = getBindingModel;
        _getOwnerWindow = getOwnerWindow;
        _reloadBindingModel = reloadBindingModel;

        ImportBindingsCommand.RaiseCanExecuteChanged();
        ExportBindingsCommand.RaiseCanExecuteChanged();
    }

    private bool CanImportOrExportBindings()
    {
        return !string.IsNullOrWhiteSpace(_getBaseDir?.Invoke()) &&
               _getBindingModel is not null;
    }

    private void ImportBindings()
    {
        string? baseDir = _getBaseDir?.Invoke();
        BindingModel? bindingModel = _getBindingModel?.Invoke();

        if (baseDir is null || string.IsNullOrWhiteSpace(baseDir) || bindingModel is null)
            return;

        bool imported =
            _bindingJsonImportExport.Import(
                baseDir,
                bindingModel,
                _getOwnerWindow?.Invoke());

        if (!imported)
            return;

        _reloadBindingModel?.Invoke();
    }

    private void ExportBindings()
    {
        string? baseDir = _getBaseDir?.Invoke();
        BindingModel? bindingModel = _getBindingModel?.Invoke();

        if (baseDir is null || string.IsNullOrWhiteSpace(baseDir) || bindingModel is null)
            return;

        _bindingJsonImportExport.Export(
            baseDir,
            bindingModel,
            _getOwnerWindow?.Invoke());
    }

    public void LoadBindingModel(BindingModel bindingModel)
    {
        Profiles.Clear();
        DeviceColumns.Clear();
        DeviceNavigationItems.Clear();
        SelectedDeviceNavigationItem = null;

        foreach (var profile in bindingModel.AircraftProfiles)
            Profiles.Add(profile);

        foreach (var deviceProfile in bindingModel.DeviceProfiles.OrderBy(device => device.DiscoveryIndex))
        {
            DeviceColumns.Add(deviceProfile);
            DeviceNavigationItems.Add(new ControlsDeviceNavigationItem(deviceProfile));
        }

        // Set the backing field directly during full model reload so the SelectedProfile
        // setter does not rebuild/filter the grid once here and then again below.
        _selectedProfile = Profiles.FirstOrDefault(
            profile => string.Equals(profile.AircraftProfile, "F-16", StringComparison.OrdinalIgnoreCase))
            ?? Profiles.FirstOrDefault();

        OnPropertyChanged(nameof(SelectedProfile));

        RebuildRowsFromSelectedProfile();
        RebuildCategories();
        SelectedCategory = AllActionsLabel;
        ApplyFilters();
    }

    private void RebuildRowsFromSelectedProfile()
    {
        _allRows.Clear();

        string aircraftProfile = SelectedProfile?.AircraftProfile ?? "";

        List<ControlGridRowViewModel> keyRows = _keyGridBuilder.Build(SelectedProfile, DeviceColumns);
        List<ControlGridRowViewModel> axisRows = _axisGridBuilder.Build(aircraftProfile, DeviceColumns);

        // Axis rows are generated from AxisDefinitionService, not from the .key file.
        // That means the raw axis row knows its BMS section placement, but it does not
        // naturally know the parent .key category used by the left-side category filter.
        //
        // Example:
        //   SectionName = "2.19 THROTTLE QUADRANT SYSTEM"
        //   CategoryName = ""
        //
        // Build a lookup from the normal .key rows so axis rows placed in that section
        // inherit the same category as the surrounding key rows.
        Dictionary<string, string> categoryBySection = BuildCategoryNameBySection(keyRows);

        // Axis display names are plain names like "Throttle" and "Cursor X".
        // Normal .key rows in the same section usually include a short prefix such as:
        //   "TQS: COMMS Switch Up - UHF"
        //
        // Build a lookup from section name to prefix so axis rows can display/search as:
        //   "TQS: Throttle"
        //   "TQS: Cursor X"
        //
        // This makes text filtering for "TQS", "ICP", "HUD", etc. behave the same
        // for axis rows as it does for normal key rows.
        Dictionary<string, string> mappingPrefixBySection = BuildMappingPrefixBySection(keyRows);

        axisRows = axisRows
            .Select(row => ApplyAxisGridContext(row, categoryBySection, mappingPrefixBySection))
            .ToList();

        // Axis pairs are still two logical BMS axis bindings underneath, but the Controls table
        // can show them as one physical X/Y control row.
        axisRows = CombineAxisPairRows(axisRows);

        // Group axis rows by SectionName so we can look them up when iterating key rows.
        // Axes with no section placement (HasSectionPlacement == false) go to the fallback list.
        Dictionary<string, List<ControlGridRowViewModel>> axisBySection = axisRows
            .Where(row => !string.IsNullOrWhiteSpace(row.SectionName) &&
                          !string.Equals(row.SectionName, AxisControlsGridBuilderService.UnplacedSectionName,
                              StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => row.SectionName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        List<ControlGridRowViewModel> unplacedAxisRows = axisRows
            .Where(row => string.IsNullOrWhiteSpace(row.SectionName) ||
                          string.Equals(row.SectionName, AxisControlsGridBuilderService.UnplacedSectionName,
                              StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Walk key rows in order. When we hit a SectionHeader row, immediately inject
        // any axis rows that belong to that section before the regular key rows follow.
        foreach (ControlGridRowViewModel keyRow in keyRows)
        {
            _allRows.Add(keyRow);

            if (keyRow.RowKind == BindingRowKind.SectionHeader &&
                axisBySection.TryGetValue(keyRow.SectionName, out List<ControlGridRowViewModel>? sectionAxes))
            {
                foreach (ControlGridRowViewModel axisRow in sectionAxes)
                    _allRows.Add(axisRow);
            }
        }

        // Append any unplaced axes at the bottom so they are never silently lost.
        foreach (ControlGridRowViewModel axisRow in unplacedAxisRows)
            _allRows.Add(axisRow);
    }

    private static Dictionary<string, string> BuildCategoryNameBySection(
    IEnumerable<ControlGridRowViewModel> keyRows)
    {
        // The .key parser already assigns CategoryName and SectionName to normal key rows.
        // This creates a section -> category lookup so generated axis rows can participate
        // in the same left-side category filtering as the .key rows around them.
        //
        // Example:
        //   "2.19 THROTTLE QUADRANT SYSTEM" -> "2. LEFT CONSOLE"
        return keyRows
            .Where(row => !string.IsNullOrWhiteSpace(row.SectionName) &&
                          !string.IsNullOrWhiteSpace(row.CategoryName))
            .GroupBy(row => row.SectionName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().CategoryName,
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildMappingPrefixBySection(
        IEnumerable<ControlGridRowViewModel> keyRows)
    {
        // Axis definitions only provide clean display names such as "Throttle".
        // The surrounding .key rows usually include short section prefixes in their
        // descriptions, such as "TQS: COMMS Switch Up - UHF".
        //
        // This creates a section to prefix lookup by reading the first usable prefix
        // from the normal editable rows in that section.
        //
        // Example:
        //   "2.19 THROTTLE QUADRANT SYSTEM" -> "TQS"
        return keyRows
            .Where(row => !row.IsCategoryHeader &&
                          !row.IsSectionHeader &&
                          !row.IsRemark &&
                          !string.IsNullOrWhiteSpace(row.SectionName))
            .Select(row => new
            {
                row.SectionName,
                Prefix = ExtractMappingPrefix(row.Mapping)
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.Prefix))
            .GroupBy(row => row.SectionName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Prefix,
                StringComparer.OrdinalIgnoreCase);
    }

    private static ControlGridRowViewModel ApplyAxisGridContext(
        ControlGridRowViewModel row,
        IReadOnlyDictionary<string, string> categoryBySection,
        IReadOnlyDictionary<string, string> mappingPrefixBySection)
    {
        if (!row.IsAxisRow)
            return row;

        string categoryName = row.CategoryName;

        // Generated axis rows already know their section, but not their parent category.
        // Fill that in from the .key rows so clicking a category like "2. LEFT CONSOLE"
        // keeps the axes from that category visible.
        if (!string.IsNullOrWhiteSpace(row.SectionName) &&
            categoryBySection.TryGetValue(row.SectionName, out string? sectionCategoryName))
        {
            categoryName = sectionCategoryName;
        }

        string mapping = row.Mapping;

        // Prefix generated axis display names with the same short label used by nearby
        // .key rows. This is mostly for filtering/search, but it also makes the grid
        // visually consistent:
        //
        //   Before: "Throttle"
        //   After:  "TQS: Throttle"
        //
        // The colon check prevents double-prefixing if an axis row ever becomes
        // explicitly prefixed later.
        if (!string.IsNullOrWhiteSpace(row.SectionName) &&
            mappingPrefixBySection.TryGetValue(row.SectionName, out string? prefix) &&
            !string.IsNullOrWhiteSpace(prefix) &&
            mapping.IndexOf(':') < 0)
        {
            mapping = prefix + ": " + mapping;
        }

        return new ControlGridRowViewModel
        {
            SourceRow = row.SourceRow,
            RowKind = row.RowKind,
            SourceLineNumber = row.SourceLineNumber,
            CategoryName = categoryName,
            SectionName = row.SectionName,
            Mapping = mapping,
            IsAxisRow = row.IsAxisRow,
            AxisLogicalAxisName = row.AxisLogicalAxisName,
            IsAxisPairRow = row.IsAxisPairRow,
            AxisPairDefinition = row.AxisPairDefinition,
            DeviceCellsByDeviceKey = row.DeviceCellsByDeviceKey
        };
    }

    private static List<ControlGridRowViewModel> CombineAxisPairRows(
        List<ControlGridRowViewModel> axisRows)
    {
        var combinedRows =
            new List<ControlGridRowViewModel>();

        var consumedRows =
            new HashSet<ControlGridRowViewModel>();

        foreach (ControlGridRowViewModel row in axisRows)
        {
            if (consumedRows.Contains(row))
                continue;

            AxisPairDefinition? pairDefinition =
                AxisPairDefinitionService.All
                    .FirstOrDefault(
                        definition =>
                            definition.HasSecondaryAxis &&
                            string.Equals(
                                definition.PrimaryLogicalAxisName,
                                row.AxisLogicalAxisName,
                                StringComparison.OrdinalIgnoreCase));

            if (pairDefinition is null)
            {
                combinedRows.Add(row);
                continue;
            }

            ControlGridRowViewModel? secondaryRow =
                axisRows.FirstOrDefault(
                    candidate =>
                        !ReferenceEquals(candidate, row) &&
                        string.Equals(
                            candidate.AxisLogicalAxisName,
                            pairDefinition.SecondaryLogicalAxisName,
                            StringComparison.OrdinalIgnoreCase));

            if (secondaryRow is null)
            {
                combinedRows.Add(row);
                continue;
            }

            combinedRows.Add(
                CreateAxisPairRow(
                    row,
                    secondaryRow,
                    pairDefinition));

            consumedRows.Add(row);
            consumedRows.Add(secondaryRow);
        }

        return combinedRows;
    }

    private static ControlGridRowViewModel CreateAxisPairRow(
        ControlGridRowViewModel primaryRow,
        ControlGridRowViewModel secondaryRow,
        AxisPairDefinition pairDefinition)
    {
        var deviceCellsByDeviceKey =
            new Dictionary<string, ControlGridDeviceCellViewModel>(
                StringComparer.OrdinalIgnoreCase);

        foreach (string deviceKey in primaryRow.DeviceCellsByDeviceKey.Keys.Union(
                     secondaryRow.DeviceCellsByDeviceKey.Keys,
                     StringComparer.OrdinalIgnoreCase))
        {
            primaryRow.DeviceCellsByDeviceKey.TryGetValue(
                deviceKey,
                out ControlGridDeviceCellViewModel? primaryCell);

            secondaryRow.DeviceCellsByDeviceKey.TryGetValue(
                deviceKey,
                out ControlGridDeviceCellViewModel? secondaryCell);

            deviceCellsByDeviceKey[deviceKey] =
                new ControlGridDeviceCellViewModel
                {
                    IsDeviceConnected =
                        primaryCell?.IsDeviceConnected ??
                        secondaryCell?.IsDeviceConnected ??
                        true,

                    // The existing properties represent the primary axis.
                    DisplayText =
                        primaryCell?.HasAxisBinding == true
                            ? primaryCell.DisplayText
                            : "",

                    HasAxisBinding =
                        primaryCell?.HasAxisBinding == true,

                    PhysicalAxisIndex =
                        primaryCell?.PhysicalAxisIndex ?? -1,

                    Invert =
                        primaryCell?.Invert ?? false,

                    AxisBarValue = 0.5,

                    // The secondary properties represent the second half
                    // of the combined AxisPair row.
                    SecondaryDisplayText =
                        secondaryCell?.HasAxisBinding == true
                            ? secondaryCell.DisplayText
                            : "",

                    SecondaryHasAxisBinding =
                        secondaryCell?.HasAxisBinding == true,

                    SecondaryPhysicalAxisIndex =
                        secondaryCell?.PhysicalAxisIndex ?? -1,

                    SecondaryInvert =
                        secondaryCell?.Invert ?? false,

                    SecondaryAxisBarValue = 0.5
                };
        }

        return new ControlGridRowViewModel
        {
            SourceRow = null,
            RowKind = BindingRowKind.EditableCallback,
            CategoryName = primaryRow.CategoryName,
            SectionName = primaryRow.SectionName,
            Mapping = pairDefinition.DisplayName,
            IsAxisRow = false,
            IsAxisPairRow = true,
            AxisPairDefinition = pairDefinition,
            DeviceCellsByDeviceKey = deviceCellsByDeviceKey
        };
    }

    private static string BuildAxisPairDisplayText(string primaryText, string secondaryText)
    {
        if (!string.IsNullOrWhiteSpace(primaryText) && !string.IsNullOrWhiteSpace(secondaryText))
            return primaryText + " / " + secondaryText;

        if (!string.IsNullOrWhiteSpace(primaryText))
            return primaryText;

        if (!string.IsNullOrWhiteSpace(secondaryText))
            return secondaryText;

        return "";
    }

    private static string ExtractMappingPrefix(string mapping)
    {
        if (string.IsNullOrWhiteSpace(mapping))
            return "";

        int colonIndex = mapping.IndexOf(':');

        if (colonIndex <= 0)
            return "";

        string prefix = mapping.Substring(0, colonIndex).Trim();

        // BMS prefixes are short labels like TQS, ICP, HUD, AF, SIM, etc.
        // Keep this conservative so a longer sentence with a colon is not treated
        // as a real mapping prefix.
        if (prefix.Length > 12)
            return "";

        return prefix;
    }

    private void RebuildCategories()
    {
        Categories.Clear();

        // Force the two top-level navigation entries to always appear first.
        Categories.Add(AllActionsLabel);
        Categories.Add(AllAxesLabel);

        foreach (string category in _allRows
                     .Where(row => row.IsCategoryHeader)
                     .Select(row => row.CategoryName)
                     .Where(category => !string.IsNullOrWhiteSpace(category))
                     .Distinct())
        {
            // The axis builder creates an AXIS category internally.
            // The UI should show always show  "ALL AXES" in the second position.
            if (string.Equals(category, AxisCategoryName, StringComparison.OrdinalIgnoreCase))
                continue;

            Categories.Add(category);
        }

        // This is a generated helper category, not a real .key category.
        // Keep it last so it does not interrupt the normal BMS category order.
        Categories.Add(UnassignedKeysLabel);
    }

    private void ApplyFilters()
    {
        if (IsUnassignedKeysCategory)
        {
            RebuildUnassignedKeyRows();
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(HelperText));
            return;
        }

        Rows.Clear();

        bool isFiltering = !string.IsNullOrWhiteSpace(FilterText) ||
                           (!string.IsNullOrWhiteSpace(SelectedCategory) &&
                            !string.Equals(SelectedCategory, AllActionsLabel, StringComparison.OrdinalIgnoreCase));

        if (!isFiltering)
        {
            // No filter active: show everything as-is, headers included
            foreach (var row in _allRows)
                Rows.Add(row);
        }
        else
        {
            // Collect the data rows that pass both filters
            var matchingRows = _allRows
                .Where(row => !row.IsCategoryHeader && !row.IsSectionHeader)
                .Where(PassesCategoryFilter)
                .Where(PassesTextFilter)
                .ToHashSet();

            // Walk _allRows in order. For each header, check whether any row
            // that follows it (before the next same-level header) is in the match set.
            // If so, emit the header so results always have section context.
            ControlGridRowViewModel? pendingCategoryHeader = null;
            ControlGridRowViewModel? pendingSectionHeader = null;

            foreach (ControlGridRowViewModel row in _allRows)
            {
                if (row.IsCategoryHeader)
                {
                    // Hold the category header: emit it only when a match appears beneath it
                    pendingCategoryHeader = row;
                    pendingSectionHeader = null;
                    continue;
                }

                if (row.IsSectionHeader)
                {
                    // Hold the section header: emit it only when a match appears beneath it
                    pendingSectionHeader = row;
                    continue;
                }

                if (!matchingRows.Contains(row))
                    continue;

                // This row matched: flush any pending headers before adding it
                if (pendingCategoryHeader is not null)
                {
                    Rows.Add(pendingCategoryHeader);
                    pendingCategoryHeader = null;
                }

                if (pendingSectionHeader is not null)
                {
                    Rows.Add(pendingSectionHeader);
                    pendingSectionHeader = null;
                }

                Rows.Add(row);
            }
        }

        OnPropertyChanged(nameof(SummaryText));
    }

    private void RebuildUnassignedKeyRows()
    {
        Rows.Clear();

        foreach (UnassignedKeyboardKeyCandidate candidate in
                 _unassignedKeyboardKeyService.BuildRows(SelectedProfileRows, FilterText))
        {
            Rows.Add(new ControlGridRowViewModel
            {
                RowKind = BindingRowKind.Remark,
                IsUnassignedKeyRow = true,
                UnassignedKey = candidate.DisplayText,
                UnassignedModifier = candidate.ModifierDisplayName,
                UnassignedBaseKey = candidate.BaseKeyDisplayName,
                UnassignedKeySortKey = candidate.KeySortKey,
                UnassignedModifierSortKey = candidate.ModifierSortKey,
                UnassignedBaseKeySortKey = candidate.BaseKeySortKey
            });
        }
    }

    private bool PassesCategoryFilter(ControlGridRowViewModel row)
    {
        if (string.IsNullOrWhiteSpace(SelectedCategory) ||
            string.Equals(SelectedCategory, AllActionsLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(SelectedCategory, AllAxesLabel, StringComparison.OrdinalIgnoreCase))
        {
            return row.IsAxisRow ||
                   row.IsAxisPairRow ||
                   string.Equals(row.CategoryName, AxisCategoryName, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(row.CategoryName, SelectedCategory, StringComparison.OrdinalIgnoreCase);
    }

    private bool PassesTextFilter(ControlGridRowViewModel row)
    {
        if (string.IsNullOrWhiteSpace(FilterText))
            return true;

        return ContainsIgnoreCase(row.Mapping, FilterText) ||
               ContainsIgnoreCase(row.Key, FilterText) ||
               ContainsIgnoreCase(row.CategoryName, FilterText) ||
               ContainsIgnoreCase(row.SectionName, FilterText);
    }

    private static bool ContainsIgnoreCase(string? value, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return (value ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public bool SelectFirstVisibleKeyMatch(string keySearchText)
    {
        if (string.IsNullOrWhiteSpace(keySearchText))
            return false;

        ControlGridRowViewModel? match = Rows.FirstOrDefault(
            row => !row.IsUnassignedKeyRow &&
                   string.Equals(row.Key, keySearchText, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return false;

        SelectedRow = match;
        return true;
    }

    public bool SelectFirstVisibleUnassignedKeyMatch(string keySearchText)
    {
        if (string.IsNullOrWhiteSpace(keySearchText))
            return false;

        ControlGridRowViewModel? match = Rows.FirstOrDefault(
            row => row.IsUnassignedKeyRow &&
                   string.Equals(row.UnassignedKey, keySearchText, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return false;

        SelectedRow = match;
        return true;
    }

    public bool SelectFirstVisibleDxMatch(string durableDeviceKey, int buttonIndex, bool isRelease, bool isShifted)
    {
        if (SelectedProfile is null)
            return false;

        DeviceBindingProfile? deviceProfile = DeviceColumns.FirstOrDefault(device =>
            string.Equals(device.DurableDeviceKey, durableDeviceKey, StringComparison.OrdinalIgnoreCase));

        DeviceAircraftBindingProfile? aircraftProfile = deviceProfile?.AircraftProfiles.FirstOrDefault(profile =>
            string.Equals(profile.AircraftProfile, SelectedProfile.AircraftProfile, StringComparison.OrdinalIgnoreCase));

        if (aircraftProfile is null)
            return false;

        string shiftState = isShifted
            ? DeviceButtonBinding.ShiftStateShifted
            : DeviceButtonBinding.ShiftStateUnshifted;

        string trigger = isRelease
            ? DeviceButtonBinding.TriggerRelease
            : DeviceButtonBinding.TriggerPress;

        int assignmentIndex = DeviceButtonBinding.GetAssignmentIndex(shiftState, trigger);

        DeviceButtonBinding? binding = aircraftProfile.ButtonBindings
            .Where(binding =>
                binding.ButtonIndex == buttonIndex &&
                binding.AssignmentIndex == assignmentIndex &&
                !string.IsNullOrWhiteSpace(binding.CallbackName))
            .FirstOrDefault();

        if (binding is null)
            return false;

        return SelectFirstVisibleCallbackMatch(binding.CallbackName);
    }

    public bool SelectFirstVisiblePovMatch(string durableDeviceKey, int povIndex, int direction, bool isShifted)
    {
        if (SelectedProfile is null)
            return false;

        DeviceBindingProfile? deviceProfile = DeviceColumns.FirstOrDefault(device =>
            string.Equals(device.DurableDeviceKey, durableDeviceKey, StringComparison.OrdinalIgnoreCase));

        DeviceAircraftBindingProfile? aircraftProfile = deviceProfile?.AircraftProfiles.FirstOrDefault(profile =>
            string.Equals(profile.AircraftProfile, SelectedProfile.AircraftProfile, StringComparison.OrdinalIgnoreCase));

        if (aircraftProfile is null)
            return false;

        string invoke = isShifted
            ? "Shift"
            : "Default";

        DevicePovBinding? binding = aircraftProfile.PovBindings
            .Where(binding =>
                binding.PovIndex == povIndex &&
                binding.Direction == direction &&
                string.Equals(binding.Invoke, invoke, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(binding.CallbackName))
            .FirstOrDefault();

        if (binding is null)
            return false;

        return SelectFirstVisibleCallbackMatch(binding.CallbackName);
    }

    private bool SelectFirstVisibleCallbackMatch(string callbackName)
    {
        ControlGridRowViewModel? match = Rows.FirstOrDefault(row =>
            row.SourceRow is not null &&
            string.Equals(row.SourceRow.CallbackName, callbackName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return false;

        SelectedRow = match;
        return true;
    }

    public bool IsDxShiftActive(IReadOnlyDictionary<string, bool[]> currentButtonsByDeviceKey)
    {
        if (SelectedProfile is null)
            return false;

        foreach (DeviceBindingProfile deviceProfile in DeviceColumns)
        {
            if (!currentButtonsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out bool[]? buttons))
                continue;

            DeviceAircraftBindingProfile? aircraftProfile = deviceProfile.AircraftProfiles.FirstOrDefault(profile =>
                string.Equals(profile.AircraftProfile, SelectedProfile.AircraftProfile, StringComparison.OrdinalIgnoreCase));

            if (aircraftProfile is null)
                continue;

            foreach (DeviceButtonBinding binding in aircraftProfile.ButtonBindings)
            {
                if (!IsDxShiftCallback(binding.CallbackName))
                    continue;

                if (binding.ButtonIndex < 0 || binding.ButtonIndex >= buttons.Length)
                    continue;

                if (buttons[binding.ButtonIndex])
                    return true;
            }
        }

        return false;
    }

    private static bool IsDxShiftCallback(string callbackName)
    {
        return DeviceButtonBinding.IsDxShiftCallback(callbackName);
    }

    public void ApplyKeyboardMappingFromPopup(
    BindingRow selectedRow,
    string keyScancode,
    int keyModifierFlags,
    string chordScancode,
    int chordModifierFlags)
    {
        BindingRow? duplicateRow = FindDuplicateKeyboardAssignment(
            selectedRow,
            keyScancode,
            keyModifierFlags,
            chordScancode,
            chordModifierFlags);

        if (duplicateRow is not null)
        {
            ClearKeyboardAssignment(duplicateRow);
            RefreshGridRowForSource(duplicateRow);
        }

        selectedRow.KeyScancode = keyScancode;
        selectedRow.KeyModifierFlags = keyModifierFlags;
        selectedRow.ChordScancode = chordScancode;
        selectedRow.ChordModifierFlags = chordModifierFlags;
        selectedRow.IsModified = true;

        RefreshGridRowForSource(selectedRow);
        _isDirty = true;
        OnPropertyChanged(nameof(SummaryText));
    }

    public void ApplyDeviceButtonMappingFromPopup(
        BindingRow selectedRow,
        string? selectedDeviceKey,
        int? selectedButtonIndex,
        int? selectedAssignmentIndex)
    {
        if (SelectedProfile is null)
            return;

        string aircraftProfileName = SelectedProfile.AircraftProfile;
        var affectedCallbackNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        selectedRow.CallbackName
    };

        // Null device/button means "clear all DX inputs for this callback."
        // v2 treats DX as button OR POV, so Clear DX removes both.
        // The popup uses this first, then re-adds every pending DX input one at a time.
        if (string.IsNullOrWhiteSpace(selectedDeviceKey) || !selectedButtonIndex.HasValue || !selectedAssignmentIndex.HasValue)
        {
            foreach (DeviceBindingProfile deviceProfile in DeviceColumns)
            {
                DeviceAircraftBindingProfile? aircraftProfile = deviceProfile.AircraftProfiles.FirstOrDefault(profile =>
                    string.Equals(profile.AircraftProfile, aircraftProfileName, StringComparison.OrdinalIgnoreCase));

                if (aircraftProfile is null)
                    continue;

                foreach (DeviceButtonBinding existing in aircraftProfile.ButtonBindings
                             .Where(binding => string.Equals(binding.CallbackName, selectedRow.CallbackName, StringComparison.OrdinalIgnoreCase))
                             .ToList())
                {
                    aircraftProfile.ButtonBindings.Remove(existing);
                }

                foreach (DevicePovBinding existing in aircraftProfile.PovBindings
                             .Where(binding => string.Equals(binding.CallbackName, selectedRow.CallbackName, StringComparison.OrdinalIgnoreCase))
                             .ToList())
                {
                    aircraftProfile.PovBindings.Remove(existing);
                }
            }

            RefreshDeviceCellsForCallback(selectedRow.CallbackName);
            _isDirty = true;
            OnPropertyChanged(nameof(SummaryText));
            return;
        }

        int normalizedAssignmentIndex = DeviceButtonBinding.NormalizeAssignmentIndexForCallback(
            selectedRow.CallbackName,
            selectedAssignmentIndex.Value);

        DeviceBindingProfile? selectedDevice = DeviceColumns.FirstOrDefault(device =>
            string.Equals(device.DurableDeviceKey, selectedDeviceKey, StringComparison.OrdinalIgnoreCase));

        DeviceAircraftBindingProfile? selectedAircraftProfile = selectedDevice?.AircraftProfiles.FirstOrDefault(profile =>
            string.Equals(profile.AircraftProfile, aircraftProfileName, StringComparison.OrdinalIgnoreCase));

        if (selectedDevice is null || selectedAircraftProfile is null)
            return;

        // One callback may have multiple DX buttons.
        // v2 treats press/release and shifted/unshifted as four separate slots.
        // Only the exact same physical button + slot belongs to one callback.
        foreach (DeviceButtonBinding conflict in selectedAircraftProfile.ButtonBindings
                     .Where(binding =>
                         binding.ButtonIndex == selectedButtonIndex.Value &&
                         binding.AssignmentIndex == normalizedAssignmentIndex &&
                         !string.Equals(binding.CallbackName, selectedRow.CallbackName, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            affectedCallbackNames.Add(conflict.CallbackName);
            selectedAircraftProfile.ButtonBindings.Remove(conflict);
        }

        bool alreadyAssigned = selectedAircraftProfile.ButtonBindings.Any(binding =>
            binding.ButtonIndex == selectedButtonIndex.Value &&
            binding.AssignmentIndex == normalizedAssignmentIndex &&
            string.Equals(binding.CallbackName, selectedRow.CallbackName, StringComparison.OrdinalIgnoreCase));

        if (!alreadyAssigned)
        {
            selectedAircraftProfile.ButtonBindings.Add(new DeviceButtonBinding
            {
                ButtonIndex = selectedButtonIndex.Value,
                AssignmentIndex = normalizedAssignmentIndex,
                CallbackName = selectedRow.CallbackName,
                Invoke = DeviceButtonBinding.GetDefaultInvoke(normalizedAssignmentIndex),
                SoundId = selectedRow.SoundId
            });
        }

        foreach (string callbackName in affectedCallbackNames)
            RefreshDeviceCellsForCallback(callbackName);

        _isDirty = true;
        OnPropertyChanged(nameof(SummaryText));
    }

    public void ApplyDevicePovMappingFromPopup(
        BindingRow selectedRow,
        string? selectedDeviceKey,
        int? selectedPovIndex,
        int? selectedDirection,
        string? selectedInvoke)
    {
        if (SelectedProfile is null)
            return;

        if (string.IsNullOrWhiteSpace(selectedDeviceKey) || !selectedPovIndex.HasValue || !selectedDirection.HasValue)
            return;

        string aircraftProfileName = SelectedProfile.AircraftProfile;
        string invoke = string.Equals(selectedInvoke, "Shift", StringComparison.OrdinalIgnoreCase)
            ? "Shift"
            : "Default";

        var affectedCallbackNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        selectedRow.CallbackName
    };

        DeviceBindingProfile? selectedDevice = DeviceColumns.FirstOrDefault(device =>
            string.Equals(device.DurableDeviceKey, selectedDeviceKey, StringComparison.OrdinalIgnoreCase));

        DeviceAircraftBindingProfile? selectedAircraftProfile = selectedDevice?.AircraftProfiles.FirstOrDefault(profile =>
            string.Equals(profile.AircraftProfile, aircraftProfileName, StringComparison.OrdinalIgnoreCase));

        if (selectedDevice is null || selectedAircraftProfile is null)
            return;

        // POV bindings do not have release slots. They only support normal and shifted invoke.
        // The popup passes the selected shift state by storing shifted POVs with Invoke="Shift".
        // For now, a newly added POV from the popup defaults to normal invoke unless an existing
        // shifted POV is being re-added through the ViewModel save path.
        //
        // The KeyMappingWindowViewModel stores the shifted state in its pending POV list,
        // but this apply method only receives the physical POV information. If shifted POV
        // assignment is needed later, extend this method signature to include invoke.
        //

        foreach (DevicePovBinding conflict in selectedAircraftProfile.PovBindings
                     .Where(binding =>
                         binding.PovIndex == selectedPovIndex.Value &&
                         binding.Direction == selectedDirection.Value &&
                         string.Equals(binding.Invoke, invoke, StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(binding.CallbackName, selectedRow.CallbackName, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            affectedCallbackNames.Add(conflict.CallbackName);
            selectedAircraftProfile.PovBindings.Remove(conflict);
        }

        bool alreadyAssigned = selectedAircraftProfile.PovBindings.Any(binding =>
            binding.PovIndex == selectedPovIndex.Value &&
            binding.Direction == selectedDirection.Value &&
            string.Equals(binding.Invoke, invoke, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(binding.CallbackName, selectedRow.CallbackName, StringComparison.OrdinalIgnoreCase));

        if (!alreadyAssigned)
        {
            selectedAircraftProfile.PovBindings.Add(new DevicePovBinding
            {
                PovIndex = selectedPovIndex.Value,
                Direction = selectedDirection.Value,
                CallbackName = selectedRow.CallbackName,
                Invoke = invoke,
                SoundId = selectedRow.SoundId
            });
        }

        foreach (string callbackName in affectedCallbackNames)
            RefreshDeviceCellsForCallback(callbackName);

        _isDirty = true;
        OnPropertyChanged(nameof(SummaryText));
    }

    private BindingRow? FindDuplicateKeyboardAssignment(
        BindingRow selectedRow,
        string keyScancode,
        int keyModifierFlags,
        string chordScancode,
        int chordModifierFlags)
    {
        string newAssignment = KeyAssgn.GetKeyAssignmentStatus(
            keyScancode,
            keyModifierFlags,
            chordScancode,
            chordModifierFlags);

        if (string.IsNullOrWhiteSpace(newAssignment))
            return null;

        return SelectedProfileRows.FirstOrDefault(row =>
            !ReferenceEquals(row, selectedRow) &&
            row.IsEditable &&
            string.Equals(
                KeyAssgn.GetKeyAssignmentStatus(
                    row.KeyScancode,
                    row.KeyModifierFlags,
                    row.ChordScancode,
                    row.ChordModifierFlags),
                newAssignment,
                StringComparison.OrdinalIgnoreCase));
    }

    private static void ClearKeyboardAssignment(BindingRow row)
    {
        row.KeyScancode = "0xFFFFFFFF";
        row.KeyModifierFlags = 0;
        row.ChordScancode = "0";
        row.ChordModifierFlags = 0;
        row.IsModified = true;
    }

    private void RefreshGridRowForSource(BindingRow sourceRow)
    {
        foreach (ControlGridRowViewModel row in _allRows.Where(row => ReferenceEquals(row.SourceRow, sourceRow)))
            row.RefreshFromSource();
    }

    private void RefreshDeviceCellsForCallback(string callbackName)
    {
        if (SelectedProfile is null)
            return;

        foreach (ControlGridRowViewModel row in _allRows.Where(row =>
                     row.SourceRow is not null &&
                     string.Equals(row.SourceRow.CallbackName, callbackName, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (DeviceBindingProfile deviceProfile in DeviceColumns)
            {
                if (!row.DeviceCellsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out ControlGridDeviceCellViewModel? cell))
                    continue;

                DeviceAircraftBindingProfile? aircraftProfile = deviceProfile.AircraftProfiles.FirstOrDefault(profile =>
                    string.Equals(profile.AircraftProfile, SelectedProfile.AircraftProfile, StringComparison.OrdinalIgnoreCase));

                if (aircraftProfile is null)
                {
                    cell.DisplayText = "";
                    continue;
                }

                List<string> parts = aircraftProfile.ButtonBindings
                    .Where(binding => string.Equals(binding.CallbackName, callbackName, StringComparison.OrdinalIgnoreCase))
                    .Select(binding => DeviceButtonBinding.BuildDisplayText(binding.ButtonIndex, binding.AssignmentIndex))
                    .ToList();

                parts.AddRange(aircraftProfile.PovBindings
                    .Where(binding => string.Equals(binding.CallbackName, callbackName, StringComparison.OrdinalIgnoreCase))
                    .Select(binding => "POV" + (binding.PovIndex + 1) + " " + GetPovDirectionName(binding.Direction)));

                cell.DisplayText = string.Join(", ", parts);
            }
        }
    }

    private static string GetPovDirectionName(int direction)
    {
        return direction switch
        {
            0 => "Up",
            1 => "Up-Right",
            2 => "Right",
            3 => "Down-Right",
            4 => "Down",
            5 => "Down-Left",
            6 => "Left",
            7 => "Up-Left",
            _ => direction.ToString()
        };
    }


    public void ApplyAxisPairMappingFromPopup(
        AxisPairAssignViewModel popup)
    {
        string actionId =
            DebugDiagnosticsService.CreateActionId(
                "AXISPAIRAPPLY");

        AxisPairDefinition pairDefinition =
            popup.PairDefinition;

        DebugDiagnosticsService.Info(
            $"Apply advanced axis mapping begin. | " +
            $"ActionId={actionId} | " +
            $"DefinitionId={pairDefinition.PairId} | " +
            $"HasSecondaryAxis={pairDefinition.HasSecondaryAxis} | " +
            $"PrimaryLogicalAxis={pairDefinition.PrimaryLogicalAxisName} | " +
            $"PrimaryDeviceKey={popup.Primary.SelectedDeviceKey ?? "<null>"} | " +
            $"PrimaryPhysicalAxis={FormatPhysicalAxis(popup.Primary.SelectedPhysicalAxisIndex)} | " +
            $"PrimaryCleared={popup.Primary.IsCleared}");

        if (pairDefinition.HasSecondaryAxis)
        {
            DebugDiagnosticsService.Info(
                $"Apply advanced secondary axis mapping. | " +
                $"ActionId={actionId} | " +
                $"DefinitionId={pairDefinition.PairId} | " +
                $"SecondaryLogicalAxis={pairDefinition.SecondaryLogicalAxisName} | " +
                $"SecondaryDeviceKey={popup.Secondary.SelectedDeviceKey ?? "<null>"} | " +
                $"SecondaryPhysicalAxis={FormatPhysicalAxis(popup.Secondary.SelectedPhysicalAxisIndex)} | " +
                $"SecondaryCleared={popup.Secondary.IsCleared}");
        }

        string beforeState =
            BuildAxisBindingDirtyStateSignature();

        var changedLogicalAxisNames =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        ApplyAxisPairEdit(
            actionId,
            popup.Primary,
            changedLogicalAxisNames);

        if (pairDefinition.HasSecondaryAxis)
        {
            ApplyAxisPairEdit(
                actionId,
                popup.Secondary,
                changedLogicalAxisNames);
        }

        string afterState =
            BuildAxisBindingDirtyStateSignature();

        bool bindingsChanged =
            !string.Equals(
                beforeState,
                afterState,
                StringComparison.Ordinal);

        if (bindingsChanged)
        {
            foreach (string changedLogicalAxisName in
                     changedLogicalAxisNames)
            {
                DebugDiagnosticsService.Info(
                    $"Refreshing axis rows after advanced axis assignment. | " +
                    $"ActionId={actionId} | " +
                    $"LogicalAxis={changedLogicalAxisName}");

                RefreshAxisRows(
                    changedLogicalAxisName);
            }

            if (pairDefinition.HasSecondaryAxis)
            {
                RefreshAxisPairRows(
                    pairDefinition);
            }

            _isDirty = true;

            OnPropertyChanged(
                nameof(SummaryText));
        }
        else
        {
            DebugDiagnosticsService.Info(
                $"Advanced axis assignment unchanged; dirty state not set. | " +
                $"ActionId={actionId} | " +
                $"DefinitionId={pairDefinition.PairId}");
        }

        DebugDiagnosticsService.Info(
            $"Apply advanced axis mapping end. | " +
            $"ActionId={actionId} | " +
            $"DefinitionId={pairDefinition.PairId} | " +
            $"ChangedLogicalAxes={string.Join(",", changedLogicalAxisNames)} | " +
            $"BindingsChanged={bindingsChanged} | " +
            $"IsDirty={_isDirty}");
    }

    private string BuildAxisBindingDirtyStateSignature()
    {
        return string.Join(
            "\n",
            DeviceColumns
                .OrderBy(device => device.DurableDeviceKey, StringComparer.OrdinalIgnoreCase)
                .SelectMany(device =>
                    device.AxisBindings
                        .OrderBy(axis => axis.LogicalAxisName, StringComparer.OrdinalIgnoreCase)
                        .Select(axis => string.Join(
                            "|",
                            device.DurableDeviceKey ?? "",
                            axis.LogicalAxisName ?? "",
                            axis.PhysicalAxisIndex.HasValue
                                ? axis.PhysicalAxisIndex.Value.ToString()
                                : "",
                            axis.Deadzone ?? "",
                            axis.Saturation ?? "",
                            axis.Curve.ToString(),
                            axis.Invert.ToString(),
                            axis.IdleDetent.ToString(),
                            axis.AfterburnerDetent.ToString()))));
    }

    private void ApplyAxisPairEdit(
        string actionId,
        AxisPairAssignViewModel.AxisEditViewModel axisEdit,
        HashSet<string> changedLogicalAxisNames)
    {
        string logicalAxisName =
            axisEdit.LogicalAxisName;

        changedLogicalAxisNames.Add(
            logicalAxisName);

        // A logical BMS axis should resolve to one physical device axis total.
        // Clear this logical axis from every device before applying the edited
        // assignment.
        foreach (DeviceBindingProfile deviceProfile in
                 DeviceColumns)
        {
            foreach (DeviceAxisBinding binding in
                     deviceProfile.AxisBindings.Where(
                         binding =>
                             string.Equals(
                                 binding.LogicalAxisName,
                                 logicalAxisName,
                                 StringComparison.OrdinalIgnoreCase)))
            {
                if (binding.PhysicalAxisIndex.HasValue)
                {
                    DebugDiagnosticsService.Info(
                        $"Clearing previous axis pair logical axis assignment. | " +
                        $"ActionId={actionId} | " +
                        $"LogicalAxis={logicalAxisName} | " +
                        $"Device={GetDeviceDisplayName(deviceProfile)} | " +
                        $"DeviceKey={deviceProfile.DurableDeviceKey} | " +
                        $"PreviousPhysicalAxis={FormatPhysicalAxis(binding.PhysicalAxisIndex)}");

                    changedLogicalAxisNames.Add(
                        binding.LogicalAxisName);
                }

                binding.PhysicalAxisIndex = null;

                // Clear means the entire logical-axis record returns to its
                // default state, not only that its physical assignment is removed.
                if (axisEdit.IsCleared)
                {
                    binding.Deadzone =
                        AxCurve.None.ToString();

                    binding.Saturation =
                        AxCurve.None.ToString();

                    binding.Curve = 1;
                    binding.Invert = false;
                    binding.IdleDetent = null;
                    binding.AfterburnerDetent = null;

                    changedLogicalAxisNames.Add(
                        binding.LogicalAxisName);

                    DebugDiagnosticsService.Info(
                        $"Reset cleared axis pair tuning values. | " +
                        $"ActionId={actionId} | " +
                        $"LogicalAxis={logicalAxisName} | " +
                        $"Device={GetDeviceDisplayName(deviceProfile)} | " +
                        $"DeviceKey={deviceProfile.DurableDeviceKey} | " +
                        $"Deadzone={binding.Deadzone} | " +
                        $"Saturation={binding.Saturation} | " +
                        $"Curve={binding.Curve} | " +
                        $"Invert={binding.Invert}");
                }
            }
        }

        if (axisEdit.IsCleared)
        {
            DebugDiagnosticsService.Info(
                $"Axis pair edit cleared. | " +
                $"ActionId={actionId} | " +
                $"LogicalAxis={logicalAxisName}");

            return;
        }

        if (string.IsNullOrWhiteSpace(
                axisEdit.SelectedDeviceKey) ||
            !axisEdit.SelectedPhysicalAxisIndex.HasValue)
        {
            DebugDiagnosticsService.Info(
                $"Axis pair edit has no selected physical axis to apply. | " +
                $"ActionId={actionId} | " +
                $"LogicalAxis={logicalAxisName} | " +
                $"IsCleared={axisEdit.IsCleared} | " +
                $"SelectedDeviceKey={axisEdit.SelectedDeviceKey ?? "<null>"} | " +
                $"SelectedPhysicalAxis={FormatPhysicalAxis(axisEdit.SelectedPhysicalAxisIndex)}");

            return;
        }

        DeviceBindingProfile? selectedDevice =
            DeviceColumns.FirstOrDefault(
                device =>
                    string.Equals(
                        device.DurableDeviceKey,
                        axisEdit.SelectedDeviceKey,
                        StringComparison.OrdinalIgnoreCase));

        if (selectedDevice is null)
        {
            DebugDiagnosticsService.Warn(
                $"Apply axis pair mapping skipped; selected device key was not found. | " +
                $"ActionId={actionId} | " +
                $"LogicalAxis={logicalAxisName} | " +
                $"MissingDeviceKey={axisEdit.SelectedDeviceKey}");

            return;
        }

        foreach (DeviceAxisBinding conflict in
                 selectedDevice.AxisBindings.Where(
                     binding =>
                         binding.PhysicalAxisIndex ==
                         axisEdit.SelectedPhysicalAxisIndex.Value &&
                         !string.Equals(
                             binding.LogicalAxisName,
                             logicalAxisName,
                             StringComparison.OrdinalIgnoreCase)))
        {
            // Same device + same physical axis cannot be assigned to multiple
            // logical BMS axes. The axis pair popup uses the same overwrite
            // behavior as the single-axis popup.
            DebugDiagnosticsService.Warn(
                $"Removing conflicting axis pair assignment. | " +
                $"ActionId={actionId} | " +
                $"NewLogicalAxis={logicalAxisName} | " +
                $"PreviousLogicalAxis={conflict.LogicalAxisName} | " +
                $"Device={GetDeviceDisplayName(selectedDevice)} | " +
                $"DeviceKey={selectedDevice.DurableDeviceKey} | " +
                $"PhysicalAxis={FormatPhysicalAxis(conflict.PhysicalAxisIndex)}");

            changedLogicalAxisNames.Add(
                conflict.LogicalAxisName);

            conflict.PhysicalAxisIndex = null;
        }

        DeviceAxisBinding selectedBinding =
            selectedDevice.AxisBindings.FirstOrDefault(
                binding =>
                    string.Equals(
                        binding.LogicalAxisName,
                        logicalAxisName,
                        StringComparison.OrdinalIgnoreCase))
            ?? CreateAxisBinding(
                selectedDevice,
                logicalAxisName);

        selectedBinding.PhysicalAxisIndex =
            axisEdit.SelectedPhysicalAxisIndex.Value;

        selectedBinding.Deadzone =
            axisEdit.DeadzoneCurve.ToString();

        selectedBinding.Saturation =
            axisEdit.SaturationCurve.ToString();

        selectedBinding.Curve =
            axisEdit.CurveValue;

        selectedBinding.Invert =
            axisEdit.Invert;

        changedLogicalAxisNames.Add(
            selectedBinding.LogicalAxisName);

        DebugDiagnosticsService.Info(
            $"Applied axis pair assignment to in-memory profile. | " +
            $"ActionId={actionId} | " +
            $"LogicalAxis={logicalAxisName} | " +
            $"Device={GetDeviceDisplayName(selectedDevice)} | " +
            $"DeviceKey={selectedDevice.DurableDeviceKey} | " +
            $"PhysicalAxis={FormatPhysicalAxis(selectedBinding.PhysicalAxisIndex)} | " +
            $"Deadzone={selectedBinding.Deadzone} | " +
            $"Saturation={selectedBinding.Saturation} | " +
            $"Curve={selectedBinding.Curve} | " +
            $"Invert={selectedBinding.Invert}");
    }


    public void ApplyAxisMappingFromPopup(AxisAssignViewModel popup)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("AXISAPPLY");
        string logicalAxisName = popup.LogicalAxisName;

        DebugDiagnosticsService.Info(
            $"Apply axis mapping begin. | ActionId={actionId} | LogicalAxis={logicalAxisName} | PopupIsCleared={popup.IsCleared} | PopupSelectedDeviceKey={popup.SelectedDeviceKey ?? "<null>"} | PopupSelectedPhysicalAxis={FormatPhysicalAxis(popup.SelectedPhysicalAxisIndex)} | Deadzone={popup.DeadzoneCurve} | Saturation={popup.SaturationCurve} | Invert={popup.Invert} | IdleDetent={popup.IdleDetent} | AfterburnerDetent={popup.AfterburnerDetent}");

        var changedLogicalAxisNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            logicalAxisName
        };

        // A logical BMS axis should resolve to one physical device axis total.
        // Clear this logical axis from every device before applying the new assignment.
        foreach (DeviceBindingProfile deviceProfile in DeviceColumns)
        {
            foreach (DeviceAxisBinding binding in deviceProfile.AxisBindings.Where(binding =>
                         string.Equals(binding.LogicalAxisName, logicalAxisName, StringComparison.OrdinalIgnoreCase)))
            {
                if (binding.PhysicalAxisIndex.HasValue)
                {
                    DebugDiagnosticsService.Info(
                        $"Clearing previous logical axis assignment. | ActionId={actionId} | LogicalAxis={logicalAxisName} | Device={GetDeviceDisplayName(deviceProfile)} | DeviceKey={deviceProfile.DurableDeviceKey} | PreviousPhysicalAxis={FormatPhysicalAxis(binding.PhysicalAxisIndex)}");

                    changedLogicalAxisNames.Add(binding.LogicalAxisName);
                }

                binding.PhysicalAxisIndex = null;
            }
        }

        if (!popup.IsCleared && !string.IsNullOrWhiteSpace(popup.SelectedDeviceKey) && popup.SelectedPhysicalAxisIndex.HasValue)
        {
            DeviceBindingProfile? selectedDevice = DeviceColumns.FirstOrDefault(device =>
                string.Equals(device.DurableDeviceKey, popup.SelectedDeviceKey, StringComparison.OrdinalIgnoreCase));

            if (selectedDevice is not null)
            {
                DebugDiagnosticsService.Info(
                    $"Selected axis device found. | ActionId={actionId} | LogicalAxis={logicalAxisName} | Device={GetDeviceDisplayName(selectedDevice)} | DeviceKey={selectedDevice.DurableDeviceKey} | PhysicalAxis={FormatPhysicalAxis(popup.SelectedPhysicalAxisIndex)}");

                foreach (DeviceAxisBinding conflict in selectedDevice.AxisBindings.Where(binding =>
                             binding.PhysicalAxisIndex == popup.SelectedPhysicalAxisIndex.Value &&
                             !string.Equals(binding.LogicalAxisName, logicalAxisName, StringComparison.OrdinalIgnoreCase)))
                {
                    // Same device + same physical axis cannot be assigned to multiple logical BMS axes.
                    // Match keyboard behavior: assigning it here removes it from the previous row.
                    DebugDiagnosticsService.Warn(
                        $"Removing conflicting axis assignment. | ActionId={actionId} | NewLogicalAxis={logicalAxisName} | PreviousLogicalAxis={conflict.LogicalAxisName} | Device={GetDeviceDisplayName(selectedDevice)} | DeviceKey={selectedDevice.DurableDeviceKey} | PhysicalAxis={FormatPhysicalAxis(conflict.PhysicalAxisIndex)}");

                    changedLogicalAxisNames.Add(conflict.LogicalAxisName);
                    conflict.PhysicalAxisIndex = null;
                }

                DeviceAxisBinding selectedBinding = selectedDevice.AxisBindings.FirstOrDefault(binding =>
                    string.Equals(binding.LogicalAxisName, logicalAxisName, StringComparison.OrdinalIgnoreCase))
                    ?? CreateAxisBinding(selectedDevice, logicalAxisName);

                selectedBinding.PhysicalAxisIndex = popup.SelectedPhysicalAxisIndex.Value;
                selectedBinding.Deadzone = popup.DeadzoneCurve.ToString();
                selectedBinding.Saturation = popup.SaturationCurve.ToString();
                selectedBinding.Invert = popup.Invert;
                selectedBinding.IdleDetent = popup.IdleDetent;
                selectedBinding.AfterburnerDetent = popup.AfterburnerDetent;

                changedLogicalAxisNames.Add(selectedBinding.LogicalAxisName);

                DebugDiagnosticsService.Info(
                    $"Applied axis assignment to in-memory profile. | ActionId={actionId} | LogicalAxis={logicalAxisName} | Device={GetDeviceDisplayName(selectedDevice)} | DeviceKey={selectedDevice.DurableDeviceKey} | PhysicalAxis={FormatPhysicalAxis(selectedBinding.PhysicalAxisIndex)} | Deadzone={selectedBinding.Deadzone} | Saturation={selectedBinding.Saturation} | Invert={selectedBinding.Invert} | IdleDetent={selectedBinding.IdleDetent} | AfterburnerDetent={selectedBinding.AfterburnerDetent}");

                DeviceAxisBinding? readback = selectedDevice.AxisBindings.FirstOrDefault(binding =>
                    string.Equals(binding.LogicalAxisName, logicalAxisName, StringComparison.OrdinalIgnoreCase));

                DebugDiagnosticsService.Info(
                    $"Axis assignment readback. | ActionId={actionId} | LogicalAxis={logicalAxisName} | Found={readback is not null} | Device={GetDeviceDisplayName(selectedDevice)} | DeviceKey={selectedDevice.DurableDeviceKey} | PhysicalAxis={FormatPhysicalAxis(readback?.PhysicalAxisIndex)}");
            }
            else
            {
                DebugDiagnosticsService.Warn(
                    $"Apply axis mapping skipped; selected device key was not found in DeviceColumns. | ActionId={actionId} | LogicalAxis={logicalAxisName} | MissingDeviceKey={popup.SelectedDeviceKey}");
            }
        }
        else
        {
            DebugDiagnosticsService.Info(
                $"Apply axis mapping has no selected physical axis to apply. | ActionId={actionId} | LogicalAxis={logicalAxisName} | PopupIsCleared={popup.IsCleared} | PopupSelectedDeviceKey={popup.SelectedDeviceKey ?? "<null>"} | PopupSelectedPhysicalAxis={FormatPhysicalAxis(popup.SelectedPhysicalAxisIndex)}");
        }

        foreach (string changedLogicalAxisName in changedLogicalAxisNames)
        {
            DebugDiagnosticsService.Info(
                $"Refreshing axis rows after assignment. | ActionId={actionId} | LogicalAxis={changedLogicalAxisName}");

            RefreshAxisRows(changedLogicalAxisName);
        }

        _isDirty = true;
        OnPropertyChanged(nameof(SummaryText));

        DebugDiagnosticsService.Info(
            $"Apply axis mapping end. | ActionId={actionId} | LogicalAxis={logicalAxisName} | ChangedLogicalAxes={string.Join(",", changedLogicalAxisNames)} | IsDirty={_isDirty}");
    }

    private static DeviceAxisBinding CreateAxisBinding(DeviceBindingProfile deviceProfile, string logicalAxisName)
    {
        var binding = new DeviceAxisBinding
        {
            LogicalAxisName = logicalAxisName
        };

        deviceProfile.AxisBindings.Add(binding);
        return binding;
    }

    private static string FormatPhysicalAxis(int? physicalAxisIndex)
    {
        return physicalAxisIndex.HasValue
            ? PhysicalAxisNameService.GetDisplayName(physicalAxisIndex.Value) + $"({physicalAxisIndex.Value})"
            : "<null>";
    }

    private static string GetDeviceDisplayName(DeviceBindingProfile deviceProfile)
    {
        if (!string.IsNullOrWhiteSpace(deviceProfile.ProductName))
            return deviceProfile.ProductName;

        if (!string.IsNullOrWhiteSpace(deviceProfile.InstanceName))
            return deviceProfile.InstanceName;

        return deviceProfile.DurableDeviceKey;
    }

    private void RefreshAxisRows(string logicalAxisName)
    {
        DeviceAxisDefinition? axisDefinition = AxisDefinitionService.Find(logicalAxisName);

        foreach (ControlGridRowViewModel row in _allRows.Where(row =>
                     row.IsAxisRow &&
                     string.Equals(row.AxisLogicalAxisName, logicalAxisName, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (DeviceBindingProfile deviceProfile in DeviceColumns)
            {
                if (!row.DeviceCellsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out ControlGridDeviceCellViewModel? cell))
                    continue;

                DeviceAxisBinding? binding = deviceProfile.AxisBindings.FirstOrDefault(axis =>
                    string.Equals(axis.LogicalAxisName, logicalAxisName, StringComparison.OrdinalIgnoreCase));

                bool hasAxisBinding = binding?.PhysicalAxisIndex is int;
                bool showDetents = hasAxisBinding &&
                                   axisDefinition?.LayoutKind == DeviceAxisAssignmentLayoutKind.Throttle;

                cell.PhysicalAxisIndex = binding?.PhysicalAxisIndex ?? -1;
                cell.HasAxisBinding = hasAxisBinding;
                cell.DisplayText = binding?.PhysicalAxisIndex is int physicalAxisIndex
                    ? PhysicalAxisNameService.GetDisplayName(physicalAxisIndex)
                    : "";
                cell.Invert = binding?.Invert ?? false;

                cell.ShowDetents = showDetents;
                cell.IdleDetentFraction = (binding?.IdleDetent ?? DetentPosition.DefaultIdleDetent) / (double)DetentPosition.MaxAxisValue;
                cell.AfterburnerDetentFraction = (binding?.AfterburnerDetent ?? DetentPosition.DefaultAfterburnerDetent) / (double)DetentPosition.MaxAxisValue;

                // Reset to neutral until the next live polling tick updates the assigned physical axis.
                cell.AxisBarValue = 0.5;

                DebugDiagnosticsService.Info(
                    $"Axis row refreshed. | LogicalAxis={logicalAxisName} | Device={GetDeviceDisplayName(deviceProfile)} | DeviceKey={deviceProfile.DurableDeviceKey} | HasAxisBinding={cell.HasAxisBinding} | PhysicalAxisIndex={cell.PhysicalAxisIndex} | DisplayText={cell.DisplayText}");
            }
        }
    }

    private void RefreshAxisPairRows(AxisPairDefinition pairDefinition)
    {
        foreach (ControlGridRowViewModel row in _allRows.Where(row =>
                     row.IsAxisPairRow &&
                     row.AxisPairDefinition is not null &&
                     string.Equals(
                         row.AxisPairDefinition.PairId,
                         pairDefinition.PairId,
                         StringComparison.OrdinalIgnoreCase)))
        {
            foreach (DeviceBindingProfile deviceProfile in DeviceColumns)
            {
                if (!row.DeviceCellsByDeviceKey.TryGetValue(
                        deviceProfile.DurableDeviceKey,
                        out ControlGridDeviceCellViewModel? cell))
                {
                    continue;
                }

                DeviceAxisBinding? primaryBinding =
                    deviceProfile.AxisBindings.FirstOrDefault(axis =>
                        string.Equals(
                            axis.LogicalAxisName,
                            pairDefinition.PrimaryLogicalAxisName,
                            StringComparison.OrdinalIgnoreCase));

                DeviceAxisBinding? secondaryBinding =
                    deviceProfile.AxisBindings.FirstOrDefault(axis =>
                        string.Equals(
                            axis.LogicalAxisName,
                            pairDefinition.SecondaryLogicalAxisName,
                            StringComparison.OrdinalIgnoreCase));

                bool hasPrimaryBinding =
                    primaryBinding?.PhysicalAxisIndex.HasValue == true;

                bool hasSecondaryBinding =
                    secondaryBinding?.PhysicalAxisIndex.HasValue == true;

                cell.HasAxisBinding = hasPrimaryBinding;
                cell.PhysicalAxisIndex =
                    primaryBinding?.PhysicalAxisIndex ?? -1;
                cell.DisplayText =
                    primaryBinding?.PhysicalAxisIndex is int primaryAxisIndex
                        ? PhysicalAxisNameService.GetDisplayName(primaryAxisIndex)
                        : "";
                cell.Invert = primaryBinding?.Invert ?? false;
                cell.AxisBarValue = 0.5;

                cell.SecondaryHasAxisBinding = hasSecondaryBinding;
                cell.SecondaryPhysicalAxisIndex =
                    secondaryBinding?.PhysicalAxisIndex ?? -1;
                cell.SecondaryDisplayText =
                    secondaryBinding?.PhysicalAxisIndex is int secondaryAxisIndex
                        ? PhysicalAxisNameService.GetDisplayName(secondaryAxisIndex)
                        : "";
                cell.SecondaryInvert = secondaryBinding?.Invert ?? false;
                cell.SecondaryAxisBarValue = 0.5;

                DebugDiagnosticsService.Info(
                    $"Axis pair row refreshed. | PairId={pairDefinition.PairId} | PrimaryLogicalAxis={pairDefinition.PrimaryLogicalAxisName} | PrimaryAxis={cell.DisplayText} | SecondaryLogicalAxis={pairDefinition.SecondaryLogicalAxisName} | SecondaryAxis={cell.SecondaryDisplayText} | Device={GetDeviceDisplayName(deviceProfile)} | DeviceKey={deviceProfile.DurableDeviceKey}");
            }
        }
    }


    private void ClearFilters()
    {
        FilterText = "";
        SelectedCategory = AllActionsLabel;
    }

    /// <summary>
    /// Resets the dirty flag after a successful PrepareForLaunch write.
    /// Call this from MainViewModel after each successful save.
    /// </summary>
    public void ResetDirty()
    {
        _isDirty = false;
    }


    public sealed class ControlsDeviceNavigationItem
    {
        public ControlsDeviceNavigationItem(DeviceBindingProfile deviceProfile)
        {
            DeviceProfile = deviceProfile;
            DisplayName = GetDisplayName(deviceProfile);
        }

        public DeviceBindingProfile DeviceProfile { get; }

        public string DisplayName { get; }

        private static string GetDisplayName(DeviceBindingProfile deviceProfile)
        {
            string displayName;

            if (!string.IsNullOrWhiteSpace(deviceProfile.ProductName))
                displayName = deviceProfile.ProductName;
            else if (!string.IsNullOrWhiteSpace(deviceProfile.InstanceName))
                displayName = deviceProfile.InstanceName;
            else
                displayName = deviceProfile.DurableDeviceKey;

            return deviceProfile.IsConnected
                ? displayName
                : displayName + " — Offline";
        }
    }

}