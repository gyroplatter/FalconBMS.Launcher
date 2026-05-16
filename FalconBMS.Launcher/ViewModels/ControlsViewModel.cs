using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Services.Controls;
using FalconBMS.Launcher.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FalconBMS.Launcher.ViewModels;

public sealed class ControlsViewModel : ViewModelBase
{
    private const string AllActionsLabel = "ALL";
    private const string AllAxesLabel = "All AXES";
    private const string AxisCategoryName = "AXIS";

    private readonly KeyControlsGridBuilderService _keyGridBuilder = new();
    private readonly AxisControlsGridBuilderService _axisGridBuilder = new();
    private readonly AxisDefinitionService _axisDefinitionService = new();

    private readonly List<ControlGridRowViewModel> _allRows = new();

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

    public string SummaryText =>
        SelectedProfile is null
            ? "No binding profile loaded."
            : $"{SelectedProfile.AircraftProfile}: {Rows.Count} visible rows";

    public RelayCommand ClearFilterCommand { get; }

    public ControlsViewModel()
    {
        ClearFilterCommand = new RelayCommand(ClearFilters, () => true);
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

        SelectedProfile = Profiles.FirstOrDefault(
            profile => string.Equals(profile.AircraftProfile, "F-16", StringComparison.OrdinalIgnoreCase))
            ?? Profiles.FirstOrDefault();

        RebuildRowsFromSelectedProfile();
        RebuildCategories();
        SelectedCategory = AllActionsLabel;
        ApplyFilters();
    }

    private void RebuildRowsFromSelectedProfile()
    {
        _allRows.Clear();

        foreach (var row in _keyGridBuilder.Build(SelectedProfile, DeviceColumns))
            _allRows.Add(row);

        foreach (var row in _axisGridBuilder.Build(DeviceColumns))
            _allRows.Add(row);
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
            // The UI should show that as "All Axes" in the forced second position instead.
            if (string.Equals(category, AxisCategoryName, StringComparison.OrdinalIgnoreCase))
                continue;

            Categories.Add(category);
        }
    }

    private void ApplyFilters()
    {
        Rows.Clear();

        foreach (var row in _allRows.Where(PassesCategoryFilter).Where(PassesTextFilter))
            Rows.Add(row);

        OnPropertyChanged(nameof(SummaryText));
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
                   string.Equals(row.CategoryName, AxisCategoryName, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(row.CategoryName, SelectedCategory, StringComparison.OrdinalIgnoreCase);
    }

    private bool PassesTextFilter(ControlGridRowViewModel row)
    {
        if (string.IsNullOrWhiteSpace(FilterText))
            return true;

        return Contains(row.Mapping, FilterText) ||
               Contains(row.Key, FilterText) ||
               Contains(row.CategoryName, FilterText) ||
               Contains(row.SectionName, FilterText);
    }

    private static bool Contains(string value, string filter)
    {
        return value?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public bool SelectFirstVisibleKeyMatch(string keySearchText)
    {
        if (string.IsNullOrWhiteSpace(keySearchText))
            return false;

        ControlGridRowViewModel? match = Rows.FirstOrDefault(
            row => string.Equals(row.Key, keySearchText, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return false;

        SelectedRow = match;
        return true;
    }

    public bool SelectFirstVisibleDxMatch(string durableDeviceKey, int buttonIndex)
    {
        if (SelectedProfile is null)
            return false;

        DeviceBindingProfile? deviceProfile = DeviceColumns.FirstOrDefault(device =>
            string.Equals(device.DurableDeviceKey, durableDeviceKey, StringComparison.OrdinalIgnoreCase));

        DeviceAircraftBindingProfile? aircraftProfile = deviceProfile?.AircraftProfiles.FirstOrDefault(profile =>
            string.Equals(profile.AircraftProfile, SelectedProfile.AircraftProfile, StringComparison.OrdinalIgnoreCase));

        if (aircraftProfile is null)
            return false;

        DeviceButtonBinding? binding = aircraftProfile.ButtonBindings
            .Where(binding =>
                binding.ButtonIndex == buttonIndex &&
                !string.IsNullOrWhiteSpace(binding.CallbackName))
            .OrderBy(binding => binding.AssignmentIndex)
            .FirstOrDefault();

        if (binding is null)
            return false;

        ControlGridRowViewModel? match = Rows.FirstOrDefault(row =>
            row.SourceRow is not null &&
            string.Equals(row.SourceRow.CallbackName, binding.CallbackName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return false;

        SelectedRow = match;
        return true;
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
        int? selectedButtonIndex)
    {
        if (SelectedProfile is null)
            return;

        string aircraftProfileName = SelectedProfile.AircraftProfile;
        var affectedCallbackNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        selectedRow.CallbackName
    };

        // Null device/button means "clear all DX buttons for this callback."
        // The popup uses this first, then re-adds every pending DX button one at a time.
        if (string.IsNullOrWhiteSpace(selectedDeviceKey) || !selectedButtonIndex.HasValue)
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
            }

            RefreshDeviceCellsForCallback(selectedRow.CallbackName);
            _isDirty = true;
            OnPropertyChanged(nameof(SummaryText));
            return;
        }

        DeviceBindingProfile? selectedDevice = DeviceColumns.FirstOrDefault(device =>
            string.Equals(device.DurableDeviceKey, selectedDeviceKey, StringComparison.OrdinalIgnoreCase));

        DeviceAircraftBindingProfile? selectedAircraftProfile = selectedDevice?.AircraftProfiles.FirstOrDefault(profile =>
            string.Equals(profile.AircraftProfile, aircraftProfileName, StringComparison.OrdinalIgnoreCase));

        if (selectedDevice is null || selectedAircraftProfile is null)
            return;

        // One callback may have multiple DX buttons.
        // One physical DX button may still belong to only one callback.
        foreach (DeviceButtonBinding conflict in selectedAircraftProfile.ButtonBindings
                     .Where(binding =>
                         binding.ButtonIndex == selectedButtonIndex.Value &&
                         !string.Equals(binding.CallbackName, selectedRow.CallbackName, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            affectedCallbackNames.Add(conflict.CallbackName);
            selectedAircraftProfile.ButtonBindings.Remove(conflict);
        }

        bool alreadyAssigned = selectedAircraftProfile.ButtonBindings.Any(binding =>
            binding.ButtonIndex == selectedButtonIndex.Value &&
            string.Equals(binding.CallbackName, selectedRow.CallbackName, StringComparison.OrdinalIgnoreCase));

        if (!alreadyAssigned)
        {
            selectedAircraftProfile.ButtonBindings.Add(new DeviceButtonBinding
            {
                ButtonIndex = selectedButtonIndex.Value,
                AssignmentIndex = 0,
                CallbackName = selectedRow.CallbackName,
                Invoke = "Default",
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
                    .Select(binding => "DX" + binding.ButtonNumber)
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
            1 => "Right",
            2 => "Down",
            3 => "Left",
            _ => direction.ToString()
        };
    }


    public void ApplyAxisMappingFromPopup(AxisAssignViewModel popup)
    {
        string logicalAxisName = popup.LogicalAxisName;

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
                    changedLogicalAxisNames.Add(binding.LogicalAxisName);

                binding.PhysicalAxisIndex = null;
            }
        }

        if (!popup.IsCleared && !string.IsNullOrWhiteSpace(popup.SelectedDeviceKey) && popup.SelectedPhysicalAxisIndex.HasValue)
        {
            DeviceBindingProfile? selectedDevice = DeviceColumns.FirstOrDefault(device =>
                string.Equals(device.DurableDeviceKey, popup.SelectedDeviceKey, StringComparison.OrdinalIgnoreCase));

            if (selectedDevice is not null)
            {
                foreach (DeviceAxisBinding conflict in selectedDevice.AxisBindings.Where(binding =>
                             binding.PhysicalAxisIndex == popup.SelectedPhysicalAxisIndex.Value &&
                             !string.Equals(binding.LogicalAxisName, logicalAxisName, StringComparison.OrdinalIgnoreCase)))
                {
                    // Same device + same physical axis cannot be assigned to multiple logical BMS axes.
                    // Match keyboard behavior: assigning it here removes it from the previous row.
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
            }
        }

        foreach (string changedLogicalAxisName in changedLogicalAxisNames)
            RefreshAxisRows(changedLogicalAxisName);

        _isDirty = true;
        OnPropertyChanged(nameof(SummaryText));
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

    private void RefreshAxisRows(string logicalAxisName)
    {
        DeviceAxisDefinition? axisDefinition = _axisDefinitionService.Find(logicalAxisName);

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
            if (!string.IsNullOrWhiteSpace(deviceProfile.ProductName))
                return deviceProfile.ProductName;

            if (!string.IsNullOrWhiteSpace(deviceProfile.InstanceName))
                return deviceProfile.InstanceName;

            return deviceProfile.DurableDeviceKey;
        }
    }

}