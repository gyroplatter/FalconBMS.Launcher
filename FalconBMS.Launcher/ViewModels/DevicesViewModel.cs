using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// View model for the Devices tab.
///
/// Devices consumes the existing BindingModel for aircraft profiles and
/// discovered device state. It does not perform hardware discovery itself.
/// </summary>
public sealed class DevicesViewModel : ViewModelBase
{
    private BindingModel _bindingModel = new();

    public ObservableCollection<BindingAircraftProfile> Profiles { get; } = new();

    public ObservableCollection<DevicesDeviceListItemViewModel> ConnectedDevices { get; } = new();

    private BindingAircraftProfile? _selectedProfile;

    public BindingAircraftProfile? SelectedProfile
    {
        get => _selectedProfile;
        set => Set(ref _selectedProfile, value);
    }

    private DevicesDeviceListItemViewModel? _selectedDeviceItem;

    public DevicesDeviceListItemViewModel? SelectedDeviceItem
    {
        get => _selectedDeviceItem;
        set
        {
            if (!Set(ref _selectedDeviceItem, value))
                return;

            OnPropertyChanged(nameof(SelectedDevice));
        }
    }

    /// <summary>
    /// The actual runtime device profile represented by the selected list item.
    ///
    /// Future Devices features should use this DeviceBindingProfile rather than
    /// rediscovering or reconstructing device identity.
    /// </summary>
    public DeviceBindingProfile? SelectedDevice =>
        SelectedDeviceItem?.Device;

    public void LoadBindingModel(BindingModel bindingModel)
    {
        string? previousAircraftProfile =
            SelectedProfile?.AircraftProfile;

        string? previousDeviceKey =
            SelectedDevice?.DurableDeviceKey;

        _bindingModel = bindingModel;

        Profiles.Clear();

        foreach (BindingAircraftProfile profile in bindingModel.AircraftProfiles)
            Profiles.Add(profile);

        // Preserve the current profile across a model reload when possible.
        // On the first load, prefer F-16 to match the Controls tab behavior.
        _selectedProfile =
            Profiles.FirstOrDefault(profile =>
                !string.IsNullOrWhiteSpace(previousAircraftProfile) &&
                string.Equals(
                    profile.AircraftProfile,
                    previousAircraftProfile,
                    StringComparison.OrdinalIgnoreCase))
            ?? Profiles.FirstOrDefault(profile =>
                string.Equals(
                    profile.AircraftProfile,
                    "F-16",
                    StringComparison.OrdinalIgnoreCase))
            ?? Profiles.FirstOrDefault();

        OnPropertyChanged(nameof(SelectedProfile));

        RebuildConnectedDevices(previousDeviceKey);
    }

    /// <summary>
    /// Reapplies the current Controls device-column order.
    ///
    /// Controls saves its column order independently, so Devices refreshes
    /// from that setting whenever the user enters the Devices tab.
    /// </summary>
    public void RefreshDeviceOrder()
    {
        RebuildConnectedDevices(
            SelectedDevice?.DurableDeviceKey);
    }

    private void RebuildConnectedDevices(
        string? preferredDeviceKey)
    {
        Dictionary<string, int> savedOrderByDeviceKey =
            GetSavedControlsDeviceOrder();

        List<DeviceBindingProfile> orderedDevices =
            _bindingModel.DeviceProfiles
                .Where(device => device.IsConnected)
                .OrderBy(device =>
                    savedOrderByDeviceKey.TryGetValue(
                        device.DurableDeviceKey,
                        out int savedIndex)
                            ? savedIndex
                            : int.MaxValue)
                .ThenBy(device => device.DiscoveryIndex)
                .ToList();

        ConnectedDevices.Clear();

        foreach (DeviceBindingProfile device in orderedDevices)
            ConnectedDevices.Add(
                new DevicesDeviceListItemViewModel(device));

        _selectedDeviceItem =
            ConnectedDevices.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(preferredDeviceKey) &&
                string.Equals(
                    item.Device.DurableDeviceKey,
                    preferredDeviceKey,
                    StringComparison.OrdinalIgnoreCase))
            ?? ConnectedDevices.FirstOrDefault();

        OnPropertyChanged(nameof(SelectedDeviceItem));
        OnPropertyChanged(nameof(SelectedDevice));
    }

    private static Dictionary<string, int> GetSavedControlsDeviceOrder()
    {
        string savedOrder =
            FalconBMS.Launcher.Properties.Settings.Default.ControlsDeviceColumnOrder;

        if (string.IsNullOrWhiteSpace(savedOrder))
        {
            return new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
        }

        return savedOrder
            .Split(
                new[] { '|' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select((deviceKey, index) => new
            {
                DeviceKey = deviceKey,
                Index = index
            })
            .GroupBy(
                item => item.DeviceKey,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Index,
                StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Presentation wrapper for one connected runtime device.
///
/// The underlying DeviceBindingProfile remains authoritative. This wrapper
/// only supplies UI-friendly display information for the Devices list.
/// </summary>
public sealed class DevicesDeviceListItemViewModel
{
    public DeviceBindingProfile Device { get; }

    public string DisplayName { get; }

    public DevicesDeviceListItemViewModel(
        DeviceBindingProfile device)
    {
        Device = device;

        DisplayName =
            !string.IsNullOrWhiteSpace(device.ProductName)
                ? device.ProductName
                : !string.IsNullOrWhiteSpace(device.InstanceName)
                    ? device.InstanceName
                    : device.DurableDeviceKey;
    }
}