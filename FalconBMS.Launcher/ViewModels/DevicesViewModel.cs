using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// View model for the visual Devices tab.
/// The Devices tab consumes the already-loaded in-memory binding model.
/// Visual maps only provide image coordinates for known device inputs.
/// </summary>
public sealed class DevicesViewModel : ViewModelBase
{
    private const string DefaultAircraftProfile = "F-16";

    private readonly DeviceVisualMapService _visualMapService = new();
    private readonly IReadOnlyList<DeviceVisualMap> _visualMaps;

    public ObservableCollection<DeviceVisualListItemViewModel> Devices { get; } = new();

    private DeviceVisualListItemViewModel? _selectedDevice;
    private DeviceVisualCalloutViewModel? _activeCallout;
    private BindingRow? _activeMappedBindingRow;
    private string? _activeButtonDeviceKey;
    private int? _activeButtonIndex;

    public DevicesViewModel()
    {
        _visualMaps = _visualMapService.LoadMaps();

        ModifyMappingCommand = new RelayCommand(
            () =>
            {
                if (ActiveMappedBindingRow is not null)
                    KeyMappingRequested?.Invoke(this, new DevicesKeyMappingRequestedEventArgs(ActiveMappedBindingRow));
            },
            () => ActiveMappedBindingRow is not null);
    }

    /// <summary>
    /// Devices does not edit mappings directly. It uses ControlsViewModel so Modify
    /// opens the same KeyMappingWindow and save path used by the Controls tab.
    /// </summary>
    public ControlsViewModel? ControlsViewModel { get; set; }

    public RelayCommand ModifyMappingCommand { get; }

    public event EventHandler<DevicesKeyMappingRequestedEventArgs>? KeyMappingRequested;

    public DeviceVisualListItemViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!Set(ref _selectedDevice, value)) return;

            ActiveCallout = null;
            ActiveMappedBindingRow = null;
            _activeButtonDeviceKey = null;
            _activeButtonIndex = null;

            OnPropertyChanged(nameof(SelectedDeviceName));
            OnPropertyChanged(nameof(SelectedDeviceSummary));
            OnPropertyChanged(nameof(VisualTemplateVisibility));
            OnPropertyChanged(nameof(GenericFallbackVisibility));
            OnPropertyChanged(nameof(VisualCanvasWidth));
            OnPropertyChanged(nameof(VisualCanvasHeight));
            OnPropertyChanged(nameof(VisualImageSource));
            OnPropertyChanged(nameof(HighlightedControl));
            OnPropertyChanged(nameof(HighlightedControlDescription));
            OnPropertyChanged(nameof(DeviceCapabilitiesText));
            OnPropertyChanged(nameof(MappedControlDetailsVisibility));
        }
    }

    private DeviceVisualCalloutViewModel? ActiveCallout
    {
        get => _activeCallout;
        set
        {
            if (!Set(ref _activeCallout, value)) return;

            OnPropertyChanged(nameof(ActiveCalloutVisibility));
            OnPropertyChanged(nameof(ActiveCalloutInputId));
            OnPropertyChanged(nameof(ActiveCalloutPhysicalName));
            OnPropertyChanged(nameof(ActiveHotspots));
            OnPropertyChanged(nameof(ActiveCalloutX));
            OnPropertyChanged(nameof(ActiveCalloutY));
            OnPropertyChanged(nameof(ActiveCalloutWidth));
            OnPropertyChanged(nameof(ActiveCalloutScale));
            OnPropertyChanged(nameof(ActiveConnectors));
            OnPropertyChanged(nameof(HighlightedControl));
            OnPropertyChanged(nameof(HighlightedControlDescription));
            OnPropertyChanged(nameof(MappedDxButtonText));
        }
    }

    private BindingRow? ActiveMappedBindingRow
    {
        get => _activeMappedBindingRow;
        set
        {
            if (!Set(ref _activeMappedBindingRow, value)) return;

            OnPropertyChanged(nameof(MappedControlDetailsVisibility));
            OnPropertyChanged(nameof(MappedControlDescription));
            OnPropertyChanged(nameof(MappedKeyboardText));
            OnPropertyChanged(nameof(MappedDxButtonText));
            ModifyMappingCommand.RaiseCanExecuteChanged();
        }
    }

    public string SelectedDeviceName => SelectedDevice?.DisplayName ?? "No device selected";

    public string SelectedDeviceSummary
    {
        get
        {
            if (SelectedDevice is null)
                return "No device selected.";

            if (SelectedDevice.HasVisualTemplate)
                return "Press a button on your device to show it's current mapped control.";

            return "No visual layout has been added for this device yet.";
        }
    }

    public Visibility VisualTemplateVisibility =>
        SelectedDevice?.HasVisualTemplate == true
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility GenericFallbackVisibility =>
        SelectedDevice?.HasVisualTemplate == true
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility ActiveCalloutVisibility =>
        SelectedDevice?.HasVisualTemplate == true && ActiveCallout is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public double VisualCanvasWidth => SelectedDevice?.VisualMap?.CanvasWidth ?? 1000;
    public double VisualCanvasHeight => SelectedDevice?.VisualMap?.CanvasHeight ?? 1000;

    public string VisualImageSource =>
        SelectedDevice?.VisualMap is null
            ? ""
            : BuildPackUri(SelectedDevice.VisualMap.ImagePath);

    public string HighlightedControl =>
        ActiveCallout?.PhysicalName
        ?? (SelectedDevice?.HasVisualTemplate == true ? "Waiting for input" : "None");

    public string HighlightedControlDescription =>
        ActiveCallout is not null
            ? ActiveCallout.InputId
            : "Press a button on this device.";

    public string DeviceCapabilitiesText
    {
        get
        {
            if (SelectedDevice?.DeviceProfile is null)
                return "";

            DeviceBindingProfile device = SelectedDevice.DeviceProfile;

            return FormatCount(device.ButtonCount, "button", "buttons") + ", " +
                   FormatCount(device.PovCount, "POV", "POVs") + ", " +
                   FormatCount(device.AxisCount, "axis", "axes");
        }
    }

    public string ActiveCalloutInputId => ActiveCallout?.InputId ?? "";
    public string ActiveCalloutPhysicalName => ActiveCallout?.PhysicalName ?? "";

    public Visibility MappedControlDetailsVisibility =>
        ActiveMappedBindingRow is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string MappedControlDescription => ActiveMappedBindingRow?.Description ?? "";

    public string MappedKeyboardText
    {
        get
        {
            if (ActiveMappedBindingRow is null)
                return "";

            string keyboardText = KeyAssgn.GetKeyAssignmentStatus(
                ActiveMappedBindingRow.KeyScancode,
                ActiveMappedBindingRow.KeyModifierFlags,
                ActiveMappedBindingRow.ChordScancode,
                ActiveMappedBindingRow.ChordModifierFlags);

            return string.IsNullOrWhiteSpace(keyboardText)
                ? "None"
                : keyboardText;
        }
    }

    public string MappedDxButtonText => ActiveCallout?.InputId ?? "";

    public IReadOnlyList<DeviceVisualHotspotViewModel> ActiveHotspots =>
        ActiveCallout?.Hotspots ?? Array.Empty<DeviceVisualHotspotViewModel>();

    public double ActiveCalloutX => ActiveCallout?.CalloutX ?? 0;
    public double ActiveCalloutY => ActiveCallout?.CalloutY ?? 0;
    public double ActiveCalloutWidth => ActiveCallout?.CalloutWidth ?? 0;

    // The callout lives inside the image Canvas so connector lines stay aligned,
    // but it is visually scaled up so the shared theme text styles remain readable.
    public double ActiveCalloutScale => ActiveCallout?.CalloutScale ?? 1.0;

    public IReadOnlyList<DeviceVisualConnectorViewModel> ActiveConnectors =>
        ActiveCallout?.Connectors ?? Array.Empty<DeviceVisualConnectorViewModel>();

    public void LoadBindingModel(BindingModel bindingModel)
    {
        string? previousSelectedDurableKey = SelectedDevice?.DeviceProfile?.DurableDeviceKey;

        Devices.Clear();

        foreach (DeviceBindingProfile deviceProfile in GetDeviceProfilesInControlsColumnOrder(bindingModel))
        {
            DeviceVisualMap? visualMap = _visualMapService.FindMapForDevice(deviceProfile, _visualMaps);

            Devices.Add(new DeviceVisualListItemViewModel(
                deviceProfile,
                visualMap));
        }

        SelectedDevice =
            Devices.FirstOrDefault(device =>
                string.Equals(device.DeviceProfile.DurableDeviceKey, previousSelectedDurableKey, StringComparison.OrdinalIgnoreCase))
            ?? Devices.FirstOrDefault();
    }

    /// <summary>
    /// Uses the same saved device order as the Controls table columns.
    /// Devices that are not found in the saved column order fall back to discovery order.
    /// </summary>
    private static IEnumerable<DeviceBindingProfile> GetDeviceProfilesInControlsColumnOrder(BindingModel bindingModel)
    {
        Dictionary<string, int> savedOrderByDurableKey = ParseSavedControlsDeviceColumnOrder();

        return bindingModel.DeviceProfiles
            .OrderBy(device =>
                savedOrderByDurableKey.TryGetValue(device.DurableDeviceKey, out int savedOrder)
                    ? savedOrder
                    : int.MaxValue)
            .ThenBy(device => device.DiscoveryIndex);
    }

    private static Dictionary<string, int> ParseSavedControlsDeviceColumnOrder()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        string savedOrder = Properties.Settings.Default.ControlsDeviceColumnOrder ?? "";

        string[] durableKeys = savedOrder
            // ControlsView device column separators
            .Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(key => key.Trim())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToArray();

        for (int index = 0; index < durableKeys.Length; index++)
        {
            if (!result.ContainsKey(durableKeys[index]))
                result.Add(durableKeys[index], index);
        }

        return result;
    }

    public bool TryShowVisualCalloutForButton(string durableDeviceKey, int buttonIndex)
    {
        if (SelectedDevice?.DeviceProfile is null || SelectedDevice.VisualMap is null)
            return false;

        DeviceBindingProfile deviceProfile = SelectedDevice.DeviceProfile;
        DeviceVisualMap visualMap = SelectedDevice.VisualMap;

        if (!string.Equals(deviceProfile.DurableDeviceKey, durableDeviceKey, StringComparison.OrdinalIgnoreCase))
            return false;

        // Available DX buttons come from the current in-memory device profile.
        // The visual map only says where supported inputs are drawn on the image.
        if (buttonIndex < 0 || buttonIndex >= deviceProfile.ButtonCount)
            return false;

        DeviceVisualControlMap? control = visualMap.Controls.FirstOrDefault(item =>
            string.Equals(item.Kind, "button", StringComparison.OrdinalIgnoreCase) &&
            item.ButtonIndex == buttonIndex);

        if (control is null)
            return false;

        ActiveCallout = DeviceVisualCalloutViewModel.FromMap(
            control,
            visualMap.CalloutBox);

        _activeButtonDeviceKey = durableDeviceKey;
        _activeButtonIndex = buttonIndex;
        ActiveMappedBindingRow = FindMappedBindingRow(deviceProfile, buttonIndex);

        return true;
    }

    public void RefreshActiveMappedControlDetails()
    {
        if (string.IsNullOrWhiteSpace(_activeButtonDeviceKey) || !_activeButtonIndex.HasValue)
            return;

        if (SelectedDevice?.DeviceProfile is not DeviceBindingProfile deviceProfile)
            return;

        if (!string.Equals(deviceProfile.DurableDeviceKey, _activeButtonDeviceKey, StringComparison.OrdinalIgnoreCase))
            return;

        ActiveMappedBindingRow = FindMappedBindingRow(deviceProfile, _activeButtonIndex.Value);
    }

    private BindingRow? FindMappedBindingRow(DeviceBindingProfile deviceProfile, int buttonIndex)
    {
        string aircraftProfileName = ControlsViewModel?.SelectedProfile?.AircraftProfile ?? DefaultAircraftProfile;

        DeviceAircraftBindingProfile? aircraftProfile = deviceProfile.AircraftProfiles.FirstOrDefault(profile =>
            string.Equals(profile.AircraftProfile, aircraftProfileName, StringComparison.OrdinalIgnoreCase));

        if (aircraftProfile is null)
            return null;

        DeviceButtonBinding? binding = aircraftProfile.ButtonBindings
            .Where(item =>
                item.ButtonIndex == buttonIndex &&
                !string.IsNullOrWhiteSpace(item.CallbackName))
            .OrderBy(item => item.AssignmentIndex)
            .FirstOrDefault();

        if (binding is null)
            return null;

        return ControlsViewModel?.SelectedProfileRows.FirstOrDefault(row =>
            row.IsEditable &&
            string.Equals(row.CallbackName, binding.CallbackName, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildPackUri(string resourcePath)
    {
        string normalizedPath = resourcePath.Replace('\\', '/');

        string escapedPath = string.Join(
            "/",
            normalizedPath
                .Split('/')
                .Select(Uri.EscapeDataString));

        return $"pack://application:,,,/{escapedPath}";
    }

    private static string FormatCount(int count, string singularLabel, string pluralLabel)
    {
        string label = count == 1
            ? singularLabel
            : pluralLabel;

        return $"{count} {label}";
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

public sealed class DevicesKeyMappingRequestedEventArgs : EventArgs
{
    public BindingRow Row { get; }

    public DevicesKeyMappingRequestedEventArgs(BindingRow row)
    {
        Row = row;
    }
}

/// <summary>
/// Left-pane item for a detected or saved device profile.
/// The profile comes from the in-memory BindingModel.
/// </summary>
public sealed class DeviceVisualListItemViewModel
{
    public DeviceBindingProfile DeviceProfile { get; }
    public DeviceVisualMap? VisualMap { get; }

    public bool HasVisualTemplate => VisualMap is not null;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DeviceProfile.ProductName))
                return DeviceProfile.ProductName;

            if (!string.IsNullOrWhiteSpace(DeviceProfile.InstanceName))
                return DeviceProfile.InstanceName;

            return DeviceProfile.DurableDeviceKey;
        }
    }

    public string TemplateStatus => HasVisualTemplate
        ? "Visual layout available"
        : "Generic fallback";

    public DeviceVisualListItemViewModel(DeviceBindingProfile deviceProfile, DeviceVisualMap? visualMap)
    {
        DeviceProfile = deviceProfile;
        VisualMap = visualMap;
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Represents the one active callout currently shown on the device image
/// </summary>
public sealed class DeviceVisualCalloutViewModel
{
    private const double DefaultCalloutScale = 1.7;

    public string InputId { get; }
    public string PhysicalName { get; }

    public IReadOnlyList<DeviceVisualHotspotViewModel> Hotspots { get; }
    public IReadOnlyList<DeviceVisualConnectorViewModel> Connectors { get; }

    public double CalloutX { get; }
    public double CalloutY { get; }
    public double CalloutWidth { get; }
    public double CalloutScale { get; }

    private DeviceVisualCalloutViewModel(
        string inputId,
        string physicalName,
        IReadOnlyList<DeviceVisualHotspotViewModel> hotspots,
        IReadOnlyList<DeviceVisualConnectorViewModel> connectors,
        double calloutX,
        double calloutY,
        double calloutWidth,
        double calloutScale)
    {
        InputId = inputId;
        PhysicalName = physicalName;
        Hotspots = hotspots;
        Connectors = connectors;
        CalloutX = calloutX;
        CalloutY = calloutY;
        CalloutWidth = calloutWidth;
        CalloutScale = calloutScale;
    }

    public static DeviceVisualCalloutViewModel FromMap(
        DeviceVisualControlMap control,
        DeviceVisualCalloutBoxMap calloutBox)
    {
        List<DeviceVisualHotspotViewModel> hotspots = control.Hotspots
            .Select(DeviceVisualHotspotViewModel.FromMap)
            .ToList();

        // The visible callout is scaled inside the Canvas. Use the scaled visual
        // width for connector endpoints so lines meet the actual visible box.
        double visualCalloutWidth = calloutBox.Width * DefaultCalloutScale;

        List<DeviceVisualConnectorViewModel> connectors = hotspots
            .Select(hotspot => DeviceVisualConnectorViewModel.FromHotspot(
                hotspot,
                calloutBox.X,
                calloutBox.Y,
                visualCalloutWidth))
            .ToList();

        return new DeviceVisualCalloutViewModel(
            control.InputId,
            control.PhysicalName,
            hotspots,
            connectors,
            calloutBox.X,
            calloutBox.Y,
            calloutBox.Width,
            DefaultCalloutScale);
    }
}

public sealed class DeviceVisualHotspotViewModel
{
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public double AnchorX { get; }
    public double AnchorY { get; }

    private DeviceVisualHotspotViewModel(
        double x,
        double y,
        double width,
        double height,
        double anchorX,
        double anchorY)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        AnchorX = anchorX;
        AnchorY = anchorY;
    }

    public static DeviceVisualHotspotViewModel FromMap(DeviceVisualHotspotMap hotspot)
    {
        return new DeviceVisualHotspotViewModel(
            hotspot.X,
            hotspot.Y,
            hotspot.Width,
            hotspot.Height,
            hotspot.AnchorX,
            hotspot.AnchorY);
    }
}

public sealed class DeviceVisualConnectorViewModel
{
    public Geometry Geometry { get; }

    private DeviceVisualConnectorViewModel(Geometry geometry)
    {
        Geometry = geometry;
    }

    public static DeviceVisualConnectorViewModel FromHotspot(
        DeviceVisualHotspotViewModel hotspot,
        double calloutX,
        double calloutY,
        double calloutWidth)
    {
        double calloutLeft = calloutX;
        double calloutRight = calloutX + calloutWidth;
        double targetX;

        if (hotspot.AnchorX < calloutLeft)
            targetX = calloutLeft;
        else if (hotspot.AnchorX > calloutRight)
            targetX = calloutRight;
        else
            targetX = calloutX + (calloutWidth / 2);

        double targetY = calloutY + 48;
        double control1X = hotspot.AnchorX + ((targetX - hotspot.AnchorX) * 0.45);
        double control2X = hotspot.AnchorX + ((targetX - hotspot.AnchorX) * 0.65);

        Geometry geometry = Geometry.Parse(
            $"M {Format(hotspot.AnchorX)},{Format(hotspot.AnchorY)} " +
            $"C {Format(control1X)},{Format(hotspot.AnchorY)} " +
            $"{Format(control2X)},{Format(targetY)} " +
            $"{Format(targetX)},{Format(targetY)}");

        return new DeviceVisualConnectorViewModel(geometry);
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}