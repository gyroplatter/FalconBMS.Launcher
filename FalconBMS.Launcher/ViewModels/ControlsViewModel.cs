using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace FalconBMS.Launcher.ViewModels;

public sealed class ControlsViewModel : ViewModelBase
{
    private readonly KeyControlsGridBuilderService _keyGridBuilder = new();

    public ObservableCollection<BindingAircraftProfile> Profiles { get; } = new();
    public ObservableCollection<ControlGridRowViewModel> Rows { get; } = new();

    private BindingAircraftProfile? _selectedProfile;
    public BindingAircraftProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value)) return;
            RefreshRows();
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string SummaryText =>
        SelectedProfile is null
            ? "No binding profile loaded."
            : $"{SelectedProfile.AircraftProfile}: {Rows.Count} visible key rows";

    public void LoadBindingModel(BindingModel bindingModel)
    {
        Profiles.Clear();

        foreach (var profile in bindingModel.AircraftProfiles)
            Profiles.Add(profile);

        SelectedProfile = Profiles.FirstOrDefault(
            profile => string.Equals(profile.AircraftProfile, "F-16", StringComparison.OrdinalIgnoreCase))
            ?? Profiles.FirstOrDefault();

        RefreshRows();
        OnPropertyChanged(nameof(SummaryText));
    }

    private void RefreshRows()
    {
        Rows.Clear();

        foreach (var row in _keyGridBuilder.Build(SelectedProfile))
            Rows.Add(row);

        OnPropertyChanged(nameof(SummaryText));
    }
}