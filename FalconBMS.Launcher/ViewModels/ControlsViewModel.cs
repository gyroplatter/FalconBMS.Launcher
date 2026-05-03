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
    private const string AllCategoriesLabel = "ALL KEYS & AXES";

    private readonly KeyControlsGridBuilderService _keyGridBuilder = new();
    private readonly AxisControlsGridBuilderService _axisGridBuilder = new();
    private readonly AxisDefinitionService _axisDefinitionService = new();

    private readonly List<ControlGridRowViewModel> _allRows = new();

    public ObservableCollection<BindingAircraftProfile> Profiles { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<ControlGridRowViewModel> Rows { get; } = new();

    public ObservableCollection<DeviceBindingProfile> DeviceColumns { get; } = new();

    public IReadOnlyList<BindingRow> SelectedProfileRows =>
        SelectedProfile?.Rows ?? Array.Empty<BindingRow>().ToList();

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
            SelectedCategory = AllCategoriesLabel;
            ApplyFilters();
        }
    }

    private string _selectedCategory = AllCategoriesLabel;
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!Set(ref _selectedCategory, value)) return;
            ApplyFilters();
        }
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

        foreach (var profile in bindingModel.AircraftProfiles)
            Profiles.Add(profile);

        foreach (var deviceProfile in bindingModel.DeviceProfiles.OrderBy(device => device.DiscoveryIndex))
            DeviceColumns.Add(deviceProfile);

        SelectedProfile = Profiles.FirstOrDefault(
            profile => string.Equals(profile.AircraftProfile, "F-16", StringComparison.OrdinalIgnoreCase))
            ?? Profiles.FirstOrDefault();

        RebuildRowsFromSelectedProfile();
        RebuildCategories();
        SelectedCategory = AllCategoriesLabel;
        ApplyFilters();
    }

    private void RebuildRowsFromSelectedProfile()
    {
        _allRows.Clear();

        foreach (var row in _keyGridBuilder.Build(SelectedProfile))
            _allRows.Add(row);

        foreach (var row in _axisGridBuilder.Build(DeviceColumns))
            _allRows.Add(row);
    }

    private void RebuildCategories()
    {
        Categories.Clear();
        Categories.Add(AllCategoriesLabel);

        foreach (string category in _allRows
                     .Where(row => row.IsCategoryHeader)
                     .Select(row => row.CategoryName)
                     .Where(category => !string.IsNullOrWhiteSpace(category))
                     .Distinct())
        {
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
            string.Equals(SelectedCategory, AllCategoriesLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
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
        SelectedCategory = AllCategoriesLabel;
    }
}