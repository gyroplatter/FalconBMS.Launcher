using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
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

    private readonly List<ControlGridRowViewModel> _allRows = new();

    public ObservableCollection<BindingAircraftProfile> Profiles { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<ControlGridRowViewModel> Rows { get; } = new();

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
            : $"{SelectedProfile.AircraftProfile}: {Rows.Count} visible key rows";

    public RelayCommand ClearFilterCommand { get; }

    public ControlsViewModel()
    {
        ClearFilterCommand = new RelayCommand(ClearFilters, () => true);
    }

    public void LoadBindingModel(BindingModel bindingModel)
    {
        Profiles.Clear();

        foreach (var profile in bindingModel.AircraftProfiles)
            Profiles.Add(profile);

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

    private void ClearFilters()
    {
        FilterText = "";
        SelectedCategory = AllCategoriesLabel;
    }
}