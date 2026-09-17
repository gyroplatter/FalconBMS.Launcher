using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// View model for the Devices tab.
///
/// Devices consumes the existing BindingModel for aircraft profiles and
/// discovered device state. It does not perform hardware discovery itself.
/// </summary>
public sealed class DevicesViewModel : ViewModelBase
{
    private const string DeviceImageFilter =
        "Device images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";

    private readonly DeviceMapStore _deviceMapStore =
        new();

    private readonly DeviceInputMappingResolver _inputMappingResolver =
        new();

    private BindingModel _bindingModel =
        new();

    private Func<string?>? _getBaseDir;

    private Func<Window?>? _getOwnerWindow;

    public ObservableCollection<BindingAircraftProfile> Profiles { get; } =
        new();

    public ObservableCollection<DevicesDeviceListItemViewModel> ConnectedDevices { get; } =
        new();

    public RelayCommand CreateMapCommand { get; }

    public RelayCommand EditMapCommand { get; }

    private BindingAircraftProfile? _selectedProfile;

    public BindingAircraftProfile? SelectedProfile
    {
        get => _selectedProfile;

        set
        {
            if (!Set(
                    ref _selectedProfile,
                    value))
            {
                return;
            }

            ClearCurrentInput();
        }
    }

    private DevicesDeviceListItemViewModel? _selectedDeviceItem;

    public DevicesDeviceListItemViewModel? SelectedDeviceItem
    {
        get => _selectedDeviceItem;

        set
        {
            if (!Set(
                    ref _selectedDeviceItem,
                    value))
            {
                return;
            }

            OnPropertyChanged(
                nameof(SelectedDevice));

            // Input details belong to the previously selected device,
            // so clear them immediately when the user switches devices
            ClearCurrentInput();

            RefreshSelectedDeviceImage();
            RefreshMapCommandState();
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

    private BitmapImage? _selectedDeviceImage;

    public BitmapImage? SelectedDeviceImage
    {
        get => _selectedDeviceImage;

        private set
        {
            if (!Set(
                    ref _selectedDeviceImage,
                    value))
            {
                return;
            }

            OnPropertyChanged(
                nameof(HasSelectedDeviceImage));
        }
    }

    public bool HasSelectedDeviceImage =>
        SelectedDeviceImage is not null;

    private string _currentInputDisplay =
        "";

    public string CurrentInputDisplay
    {
        get => _currentInputDisplay;

        private set => Set(
            ref _currentInputDisplay,
            value);
    }

    private string _currentBmsMappingDisplay =
        "";

    public string CurrentBmsMappingDisplay
    {
        get => _currentBmsMappingDisplay;

        private set => Set(
            ref _currentBmsMappingDisplay,
            value);
    }

    private string _currentKeyboardDisplay =
        "";

    public string CurrentKeyboardDisplay
    {
        get => _currentKeyboardDisplay;

        private set => Set(
            ref _currentKeyboardDisplay,
            value);
    }

    private bool _hasCurrentInput;

    public bool HasCurrentInput
    {
        get => _hasCurrentInput;

        private set => Set(
            ref _hasCurrentInput,
            value);
    }

    public DevicesViewModel()
    {
        /*
         * Phase 1 behavior:
         *
         * Create Map selects the first image for a device.
         * Edit Map replaces the current image.
         *
         * When the real map editor is added, these commands can open
         * that editor instead while continuing to use DeviceMapStore.
         */

        CreateMapCommand =
            new RelayCommand(
                ChooseDeviceImage,
                CanCreateMap);

        EditMapCommand =
            new RelayCommand(
                ChooseDeviceImage,
                CanEditMap);
    }

    public void ConfigureMapImages(
        Func<string?> getBaseDir,
        Func<Window?> getOwnerWindow)
    {
        _getBaseDir =
            getBaseDir;

        _getOwnerWindow =
            getOwnerWindow;

        RefreshAllDeviceImageStates();
        RefreshSelectedDeviceImage();
        RefreshMapCommandState();
    }

    public void LoadBindingModel(
        BindingModel bindingModel)
    {
        string? previousAircraftProfile =
            SelectedProfile?.AircraftProfile;

        string? previousDeviceKey =
            SelectedDevice?.DurableDeviceKey;

        _bindingModel =
            bindingModel;

        Profiles.Clear();

        foreach (BindingAircraftProfile profile in bindingModel.AircraftProfiles)
        {
            Profiles.Add(profile);
        }

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

        OnPropertyChanged(
            nameof(SelectedDevice));

        ClearCurrentInput();
        RefreshSelectedDeviceImage();
        RefreshMapCommandState();
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

    public void ShowButtonInput(
        string deviceKey,
        int buttonIndex,
        bool isShifted)
    {
        DeviceBindingProfile? selectedDevice =
            SelectedDevice;

        BindingAircraftProfile? selectedProfile =
            SelectedProfile;

        if (selectedDevice is null ||
            selectedProfile is null)
        {
            return;
        }

        if (!string.Equals(
                selectedDevice.DurableDeviceKey,
                deviceKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DeviceInputMappingResult result =
            _inputMappingResolver.ResolveButton(
                _bindingModel,
                selectedProfile,
                selectedDevice,
                buttonIndex,
                isShifted);

        ApplyCurrentInput(
            result);
    }

    public void ShowPovInput(
        string deviceKey,
        int povIndex,
        int direction,
        bool isShifted)
    {
        DeviceBindingProfile? selectedDevice =
            SelectedDevice;

        BindingAircraftProfile? selectedProfile =
            SelectedProfile;

        if (selectedDevice is null ||
            selectedProfile is null)
        {
            return;
        }

        if (!string.Equals(
                selectedDevice.DurableDeviceKey,
                deviceKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DeviceInputMappingResult result =
            _inputMappingResolver.ResolvePov(
                _bindingModel,
                selectedProfile,
                selectedDevice,
                povIndex,
                direction,
                isShifted);

        ApplyCurrentInput(
            result);
    }

    public bool IsDxShiftActive(
        Func<string, int, bool> isButtonPressed)
    {
        BindingAircraftProfile? selectedProfile =
            SelectedProfile;

        if (selectedProfile is null)
            return false;

        foreach (DeviceBindingProfile device in
                 _bindingModel.DeviceProfiles.Where(device =>
                     device.IsConnected))
        {
            DeviceAircraftBindingProfile? aircraftProfile =
                device.AircraftProfiles.FirstOrDefault(profile =>
                    string.Equals(
                        profile.AircraftProfile,
                        selectedProfile.AircraftProfile,
                        StringComparison.OrdinalIgnoreCase));

            if (aircraftProfile is null)
                continue;

            foreach (DeviceButtonBinding binding in
                     aircraftProfile.ButtonBindings)
            {
                if (!DeviceButtonBinding.IsDxShiftCallback(
                        binding.CallbackName))
                {
                    continue;
                }

                if (binding.ButtonIndex < 0 ||
                    binding.ButtonIndex >= device.ButtonCount)
                {
                    continue;
                }

                if (isButtonPressed(
                        device.DurableDeviceKey,
                        binding.ButtonIndex))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void RebuildConnectedDevices(
        string? preferredDeviceKey)
    {
        Dictionary<string, int> savedOrderByDeviceKey =
            GetSavedControlsDeviceOrder();

        List<DeviceBindingProfile> orderedDevices =
            _bindingModel.DeviceProfiles
                .Where(device =>
                    device.IsConnected)
                .OrderBy(device =>
                    savedOrderByDeviceKey.TryGetValue(
                        device.DurableDeviceKey,
                        out int savedIndex)
                            ? savedIndex
                            : int.MaxValue)
                .ThenBy(device =>
                    device.DiscoveryIndex)
                .ToList();

        ConnectedDevices.Clear();

        foreach (DeviceBindingProfile device in orderedDevices)
        {
            ConnectedDevices.Add(
                new DevicesDeviceListItemViewModel(
                    device));
        }

        RefreshAllDeviceImageStates();

        _selectedDeviceItem =
            ConnectedDevices.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(preferredDeviceKey) &&
                string.Equals(
                    item.Device.DurableDeviceKey,
                    preferredDeviceKey,
                    StringComparison.OrdinalIgnoreCase))
            ?? ConnectedDevices.FirstOrDefault();

        OnPropertyChanged(
            nameof(SelectedDeviceItem));

        OnPropertyChanged(
            nameof(SelectedDevice));

        RefreshSelectedDeviceImage();
        RefreshMapCommandState();
    }

    private void RefreshAllDeviceImageStates()
    {
        string? baseDir =
            _getBaseDir?.Invoke();

        if (baseDir is null ||
            string.IsNullOrWhiteSpace(baseDir))
        {
            foreach (DevicesDeviceListItemViewModel item in ConnectedDevices)
            {
                item.SetHasVisualLayout(false);
            }

            return;
        }

        foreach (DevicesDeviceListItemViewModel item in ConnectedDevices)
        {
            bool hasVisualLayout =
                !string.IsNullOrWhiteSpace(
                    _deviceMapStore.FindImagePath(
                        baseDir,
                        item.Device));

            item.SetHasVisualLayout(
                hasVisualLayout);
        }
    }

    private void RefreshSelectedDeviceImage()
    {
        string? baseDir =
            _getBaseDir?.Invoke();

        DeviceBindingProfile? device =
            SelectedDevice;

        if (baseDir is null ||
            string.IsNullOrWhiteSpace(baseDir) ||
            device is null)
        {
            SelectedDeviceImage =
                null;

            return;
        }

        string? imagePath =
            _deviceMapStore.FindImagePath(
                baseDir,
                device);

        SelectedDeviceImage =
            _deviceMapStore.LoadImage(
                imagePath);
    }

    private bool CanCreateMap()
    {
        return CanChooseDeviceImage() &&
               !HasSelectedDeviceImage;
    }

    private bool CanEditMap()
    {
        return CanChooseDeviceImage() &&
               HasSelectedDeviceImage;
    }

    private bool CanChooseDeviceImage()
    {
        return SelectedDevice is not null &&
               !string.IsNullOrWhiteSpace(
                   _getBaseDir?.Invoke());
    }

    private void ChooseDeviceImage()
    {
        string? baseDir =
            _getBaseDir?.Invoke();

        DeviceBindingProfile? device =
            SelectedDevice;

        if (baseDir is null ||
            string.IsNullOrWhiteSpace(baseDir) ||
            device is null)
        {
            return;
        }

        var openDialog =
            new OpenFileDialog
            {
                Title =
                    HasSelectedDeviceImage
                        ? "Change Device Image"
                        : "Choose Device Image",

                Filter =
                    DeviceImageFilter,

                CheckFileExists =
                    true,

                Multiselect =
                    false
            };

        if (openDialog.ShowDialog(
                _getOwnerWindow?.Invoke()) != true)
        {
            return;
        }

        try
        {
            _deviceMapStore.ImportUserImage(
                baseDir,
                device,
                openDialog.FileName);

            RefreshAllDeviceImageStates();
            RefreshSelectedDeviceImage();
            RefreshMapCommandState();
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"Device map image import failed: {openDialog.FileName}");

            MessageBox.Show(
                _getOwnerWindow?.Invoke(),
                "The selected device image could not be saved.\n\n" +
                ex.Message,
                "Device Image",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RefreshMapCommandState()
    {
        CreateMapCommand.RaiseCanExecuteChanged();
        EditMapCommand.RaiseCanExecuteChanged();
    }

    private void ApplyCurrentInput(
        DeviceInputMappingResult result)
    {
        CurrentInputDisplay =
            result.InputDisplay;

        CurrentBmsMappingDisplay =
            result.MappingDisplay;

        CurrentKeyboardDisplay =
            result.KeyboardDisplay;

        HasCurrentInput =
            true;
    }

    private void ClearCurrentInput()
    {
        CurrentInputDisplay =
            "";

        CurrentBmsMappingDisplay =
            "";

        CurrentKeyboardDisplay =
            "";

        HasCurrentInput =
            false;
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
public sealed class DevicesDeviceListItemViewModel : ViewModelBase
{
    public DeviceBindingProfile Device { get; }

    public string DisplayName { get; }

    private bool _hasVisualLayout;

    public bool HasVisualLayout
    {
        get => _hasVisualLayout;

        private set
        {
            if (!Set(
                    ref _hasVisualLayout,
                    value))
            {
                return;
            }

            OnPropertyChanged(
                nameof(VisualLayoutStatus));
        }
    }

    public string VisualLayoutStatus =>
        HasVisualLayout
            ? "Visual layout available"
            : "No visual layout";

    public DevicesDeviceListItemViewModel(
        DeviceBindingProfile device)
    {
        Device =
            device;

        DisplayName =
            !string.IsNullOrWhiteSpace(device.ProductName)
                ? device.ProductName
                : !string.IsNullOrWhiteSpace(device.InstanceName)
                    ? device.InstanceName
                    : device.DurableDeviceKey;
    }

    public void SetHasVisualLayout(
        bool hasVisualLayout)
    {
        HasVisualLayout =
            hasVisualLayout;
    }
}