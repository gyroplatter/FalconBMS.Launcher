using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    private const double MapSurfaceWidthValue =
        1000.0;

    private readonly DeviceMapStore _deviceMapStore =
        new();

    private readonly DeviceInputMappingResolver _inputMappingResolver =
        new();

    private BindingModel _bindingModel =
        new();

    private Func<string?>? _getBaseDir;

    public ObservableCollection<BindingAircraftProfile> Profiles { get; } =
        new();

    public ObservableCollection<DevicesDeviceListItemViewModel> ConnectedDevices { get; } =
        new();

    public ObservableCollection<DeviceMapHotspotViewModel> VisibleHotspots { get; } =
        new();

    public ObservableCollection<DeviceMapCalloutViewModel> VisibleCallouts { get; } =
        new();

    public ObservableCollection<DeviceMapConnectorViewModel> VisibleConnectors { get; } =
        new();

    private DeviceMapDefinition? _selectedDeviceMap;

    public double MapSurfaceWidth =>
        MapSurfaceWidthValue;

    private double _mapSurfaceHeight =
        MapSurfaceWidthValue;

    public double MapSurfaceHeight
    {
        get => _mapSurfaceHeight;

        private set => Set(
            ref _mapSurfaceHeight,
            value);
    }

    public RelayCommand CreateMapCommand { get; }

    public RelayCommand EditMapCommand { get; }

    public event EventHandler<DeviceMapEditorRequestEventArgs>? MapEditorRequested;

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

            UpdateMapSurfaceSize();
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
        CreateMapCommand =
            new RelayCommand(
                () => RequestMapEditor(
                    isEditMode: false),
                CanCreateMap);

        EditMapCommand =
            new RelayCommand(
                () => RequestMapEditor(
                    isEditMode: true),
                CanEditMap);
    }

    public void ConfigureMapImages(
        Func<string?> getBaseDir)
    {
        _getBaseDir =
            getBaseDir;

        RefreshDeviceMapState();
    }

    public void RefreshDeviceMapState()
    {
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
        DevicesDeviceListItemViewModel? deviceItem =
            ConnectedDevices.FirstOrDefault(item =>
                string.Equals(
                    item.Device.DurableDeviceKey,
                    deviceKey,
                    StringComparison.OrdinalIgnoreCase));

        if (deviceItem is null)
            return;

        /*
         * Follow the physical device that generated the input.
         *
         * Changing SelectedDeviceItem also refreshes the displayed image and
         * clears any stale input details that belonged to the previous device.
         */
        if (!ReferenceEquals(
                SelectedDeviceItem,
                deviceItem))
        {
            SelectedDeviceItem =
                deviceItem;
        }

        DeviceBindingProfile? selectedDevice =
            SelectedDevice;

        BindingAircraftProfile? selectedProfile =
            SelectedProfile;

        if (selectedDevice is null ||
            selectedProfile is null)
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

        ShowMapInput(
            inputKind: "Button",
            buttonIndex: buttonIndex,
            povIndex: -1,
            povDirection: -1,
            result: result);
    }

    public void ShowPovInput(
        string deviceKey,
        int povIndex,
        int direction,
        bool isShifted)
    {
        DevicesDeviceListItemViewModel? deviceItem =
            ConnectedDevices.FirstOrDefault(item =>
                string.Equals(
                    item.Device.DurableDeviceKey,
                    deviceKey,
                    StringComparison.OrdinalIgnoreCase));

        if (deviceItem is null)
            return;

        /*
         * POV input follows the same behavior as a DX button:
         * switch the Devices view to the hardware that generated the input.
         */
        if (!ReferenceEquals(
                SelectedDeviceItem,
                deviceItem))
        {
            SelectedDeviceItem =
                deviceItem;
        }

        DeviceBindingProfile? selectedDevice =
            SelectedDevice;

        BindingAircraftProfile? selectedProfile =
            SelectedProfile;

        if (selectedDevice is null ||
            selectedProfile is null)
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

        ShowMapInput(
            inputKind: "Pov",
            buttonIndex: -1,
            povIndex: povIndex,
            povDirection: direction,
            result: result);
    }

    public bool IsDxShiftActive(
        Func<string, int, bool> isButtonPressed)
    {
        BindingAircraftProfile? selectedProfile =
            SelectedProfile;

        if (selectedProfile is null)
            return false;

        return _inputMappingResolver.IsDxShiftActive(
            _bindingModel,
            selectedProfile,
            isButtonPressed);
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

        ClearVisibleMapInput();

        if (baseDir is null ||
            string.IsNullOrWhiteSpace(baseDir) ||
            device is null)
        {
            _selectedDeviceMap =
                null;

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

        _selectedDeviceMap =
            _deviceMapStore.LoadMap(
                baseDir,
                device);
    }

    private bool CanCreateMap()
    {
        return CanOpenMapEditor() &&
               !HasSelectedDeviceImage;
    }

    private bool CanEditMap()
    {
        return CanOpenMapEditor() &&
               HasSelectedDeviceImage;
    }

    private bool CanOpenMapEditor()
    {
        return SelectedDevice is not null &&
               SelectedProfile is not null &&
               !string.IsNullOrWhiteSpace(
                   _getBaseDir?.Invoke());
    }

    private void RequestMapEditor(
        bool isEditMode)
    {
        string? baseDir =
            _getBaseDir?.Invoke();

        DeviceBindingProfile? device =
            SelectedDevice;

        BindingAircraftProfile? profile =
            SelectedProfile;

        if (baseDir is null ||
            string.IsNullOrWhiteSpace(baseDir) ||
            device is null ||
            profile is null)
        {
            return;
        }

        MapEditorRequested?.Invoke(
            this,
            new DeviceMapEditorRequestEventArgs(
                _bindingModel,
                profile,
                device,
                baseDir,
                isEditMode));
    }

    private void RefreshMapCommandState()
    {
        CreateMapCommand.RaiseCanExecuteChanged();
        EditMapCommand.RaiseCanExecuteChanged();
    }

    private void ShowMapInput(
    string inputKind,
    int buttonIndex,
    int povIndex,
    int povDirection,
    DeviceInputMappingResult result)
    {
        /*
         * Devices only exposes the visual data belonging to the most recent
         * physical input. Release events do not call this method, so the last
         * pressed input remains visible.
         */
        ClearVisibleMapInput();

        DeviceMapDefinition? map =
            _selectedDeviceMap;

        if (map is null)
            return;

        foreach (DeviceMapHotspot hotspot in map.Hotspots)
        {
            if (!MapInputMatches(
                    hotspot.InputKind,
                    hotspot.ButtonIndex,
                    hotspot.PovIndex,
                    hotspot.PovDirection,
                    inputKind,
                    buttonIndex,
                    povIndex,
                    povDirection))
            {
                continue;
            }

            VisibleHotspots.Add(
                new DeviceMapHotspotViewModel(
                    hotspot,
                    () => MapSurfaceWidth,
                    () => MapSurfaceHeight));
        }

        DeviceMapCallout? calloutModel =
            map.Callouts.FirstOrDefault(callout =>
                MapInputMatches(
                    callout.InputKind,
                    callout.ButtonIndex,
                    callout.PovIndex,
                    callout.PovDirection,
                    inputKind,
                    buttonIndex,
                    povIndex,
                    povDirection));

        if (calloutModel is null)
            return;

        var callout =
            new DeviceMapCalloutViewModel(
                calloutModel,
                result.InputDisplay,
                result.MappingDisplay,
                () => MapSurfaceWidth,
                () => MapSurfaceHeight);

        VisibleCallouts.Add(
            callout);

        foreach (DeviceMapHotspotViewModel hotspot in VisibleHotspots)
        {
            VisibleConnectors.Add(
                new DeviceMapConnectorViewModel(
                    hotspot,
                    callout));
        }
    }

    private void ClearVisibleMapInput()
    {
        VisibleHotspots.Clear();
        VisibleCallouts.Clear();
        VisibleConnectors.Clear();
    }

    private static bool MapInputMatches(
        string storedInputKind,
        int storedButtonIndex,
        int storedPovIndex,
        int storedPovDirection,
        string inputKind,
        int buttonIndex,
        int povIndex,
        int povDirection)
    {
        if (!string.Equals(
                storedInputKind,
                inputKind,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(
                inputKind,
                "Button",
                StringComparison.OrdinalIgnoreCase))
        {
            return storedButtonIndex ==
                   buttonIndex;
        }

        return storedPovIndex == povIndex &&
               storedPovDirection == povDirection;
    }

    private void UpdateMapSurfaceSize()
    {
        if (SelectedDeviceImage is null ||
            SelectedDeviceImage.PixelWidth <= 0 ||
            SelectedDeviceImage.PixelHeight <= 0)
        {
            MapSurfaceHeight =
                MapSurfaceWidthValue;

            return;
        }

        MapSurfaceHeight =
            MapSurfaceWidthValue *
            SelectedDeviceImage.PixelHeight /
            SelectedDeviceImage.PixelWidth;
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

        ClearVisibleMapInput();
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

public sealed class DeviceMapEditorRequestEventArgs : EventArgs
{
    public BindingModel BindingModel { get; }

    public BindingAircraftProfile SelectedProfile { get; }

    public DeviceBindingProfile Device { get; }

    public string BaseDir { get; }

    public bool IsEditMode { get; }

    public DeviceMapEditorRequestEventArgs(
        BindingModel bindingModel,
        BindingAircraftProfile selectedProfile,
        DeviceBindingProfile device,
        string baseDir,
        bool isEditMode)
    {
        BindingModel =
            bindingModel;

        SelectedProfile =
            selectedProfile;

        Device =
            device;

        BaseDir =
            baseDir;

        IsEditMode =
            isEditMode;
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