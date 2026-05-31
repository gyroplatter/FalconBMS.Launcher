using FalconBMS.Launcher.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// View model for the visual Devices tab.
///
/// The device list is loaded from the real in-memory BindingModel.DeviceProfiles.
/// Do not hard-code connected devices here. The only prototype-specific logic is
/// the temporary visual template matcher and DX2 callout for the Warthog/Viper image.
/// </summary>
public sealed class DevicesViewModel : ViewModelBase
{
    public ObservableCollection<DeviceVisualListItemViewModel> Devices { get; } = new();

    private DeviceVisualListItemViewModel? _selectedDevice;

    private DeviceVisualCalloutViewModel? _activeCallout;

    public DeviceVisualListItemViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!Set(ref _selectedDevice, value)) return;

            // Option B behavior: the active callout represents the most recently
            // pressed control for the currently selected visual device. When the
            // user selects another device, clear the stale callout.
            ActiveCallout = null;

            OnPropertyChanged(nameof(SelectedDeviceName));
            OnPropertyChanged(nameof(SelectedDeviceSummary));
            OnPropertyChanged(nameof(VisualTemplateVisibility));
            OnPropertyChanged(nameof(GenericFallbackVisibility));
            OnPropertyChanged(nameof(HighlightedControl));
            OnPropertyChanged(nameof(HighlightedControlDescription));
            OnPropertyChanged(nameof(DeviceCapabilitiesText));
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
            OnPropertyChanged(nameof(ActiveCalloutMappingName));
            OnPropertyChanged(nameof(ActiveHighlightX));
            OnPropertyChanged(nameof(ActiveHighlightY));
            OnPropertyChanged(nameof(ActiveHighlightWidth));
            OnPropertyChanged(nameof(ActiveHighlightHeight));
            OnPropertyChanged(nameof(ActiveCalloutX));
            OnPropertyChanged(nameof(ActiveCalloutY));
            OnPropertyChanged(nameof(ActiveCalloutWidth));
            OnPropertyChanged(nameof(ActiveConnectorGeometry));

            // These right-panel values are computed from ActiveCallout, so they
            // also need to refresh when the most recent live input changes.
            OnPropertyChanged(nameof(HighlightedControl));
            OnPropertyChanged(nameof(HighlightedControlDescription));
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
                return "Prototype visual layout. Press a mapped control on the selected device to show the most recent control callout.";

            return "No visual template has been added for this detected device yet.";
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

    public string HighlightedControl =>
        ActiveCallout?.InputId
        ?? (SelectedDevice?.HasVisualTemplate == true ? "Waiting for input" : "None");

    public string HighlightedControlDescription =>
        ActiveCallout is not null
            ? $"{ActiveCallout.PhysicalName} / {ActiveCallout.MappingName}"
            : "Press a mapped control on the selected device to keep its callout visible.";

    public string DeviceCapabilitiesText
    {
        get
        {
            if (SelectedDevice?.DeviceProfile is null)
                return "";

            DeviceBindingProfile device = SelectedDevice.DeviceProfile;

            string capsStatus = device.CapabilitiesReadSuccessfully
                ? "caps read"
                : "caps unavailable";

            string connectionStatus = device.IsConnected
                ? "connected"
                : "offline saved profile";

            return $"{device.ButtonCount} buttons, {device.PovCount} POVs, {device.AxisCount} axes · {connectionStatus}, {capsStatus}";
        }
    }

    public string ActiveCalloutInputId => ActiveCallout?.InputId ?? "";
    public string ActiveCalloutPhysicalName => ActiveCallout?.PhysicalName ?? "";
    public string ActiveCalloutMappingName => ActiveCallout?.MappingName ?? "";

    public double ActiveHighlightX => ActiveCallout?.HighlightX ?? 0;
    public double ActiveHighlightY => ActiveCallout?.HighlightY ?? 0;
    public double ActiveHighlightWidth => ActiveCallout?.HighlightWidth ?? 0;
    public double ActiveHighlightHeight => ActiveCallout?.HighlightHeight ?? 0;

    public double ActiveCalloutX => ActiveCallout?.CalloutX ?? 0;
    public double ActiveCalloutY => ActiveCallout?.CalloutY ?? 0;
    public double ActiveCalloutWidth => ActiveCallout?.CalloutWidth ?? 0;

    public Geometry ActiveConnectorGeometry
    {
        get
        {
            if (ActiveCallout is null)
                return Geometry.Empty;

            return Geometry.Parse(
                $"M {ActiveCallout.AnchorX},{ActiveCallout.AnchorY} " +
                $"C {ActiveCallout.AnchorX - 90},{ActiveCallout.AnchorY + 20} " +
                $"{ActiveCallout.CalloutX + ActiveCallout.CalloutWidth},{ActiveCallout.CalloutY + 48} " +
                $"{ActiveCallout.CalloutX + ActiveCallout.CalloutWidth},{ActiveCallout.CalloutY + 48}");
        }
    }

    public void LoadBindingModel(BindingModel bindingModel)
    {
        string? previousSelectedDurableKey = SelectedDevice?.DeviceProfile?.DurableDeviceKey;

        Devices.Clear();

        foreach (DeviceBindingProfile deviceProfile in bindingModel.DeviceProfiles.OrderBy(device => device.DiscoveryIndex))
        {
            Devices.Add(new DeviceVisualListItemViewModel(
                deviceProfile,
                HasPrototypeVisualTemplate(deviceProfile)));
        }

        SelectedDevice =
            Devices.FirstOrDefault(device =>
                string.Equals(device.DeviceProfile.DurableDeviceKey, previousSelectedDurableKey, StringComparison.OrdinalIgnoreCase))
            ?? Devices.FirstOrDefault();
    }

    /// <summary>
    /// Shows the visual callout for a live physical button press.
    ///
    /// This is intentionally small for the first live prototype: only the Warthog
    /// visual template's DX2 red pickle button has coordinates. Later this should
    /// look up the pressed input in a data-driven visual template.
    /// </summary>
    public bool TryShowVisualCalloutForButton(string durableDeviceKey, int buttonIndex)
    {
        if (SelectedDevice?.HasVisualTemplate != true)
            return false;

        if (SelectedDevice.DeviceProfile is null)
            return false;

        if (!string.Equals(
                SelectedDevice.DeviceProfile.DurableDeviceKey,
                durableDeviceKey,
                StringComparison.OrdinalIgnoreCase))
            return false;

        // DirectInput button indexes are zero-based.
        // DX2 is button index 1.
        if (buttonIndex != 1)
            return false;

        ActiveCallout = new DeviceVisualCalloutViewModel(
            inputId: "DX2",
            physicalName: "Red pickle button",
            mappingName: "SimPickle",
            highlightX: 386,
            highlightY: 86,
            highlightWidth: 95,
            highlightHeight: 82,
            anchorX: 430,
            anchorY: 128,
            calloutX: 96,
            calloutY: 200,
            calloutWidth: 210);

        return true;
    }

    /// <summary>
    /// Temporary proof-of-concept template matcher.
    ///
    /// The device list itself is not hard-coded. This only decides whether a real
    /// detected/saved device should use the one prototype image currently available.
    /// Later this should become a data-driven DeviceVisualTemplate service.
    /// </summary>
    private static bool HasPrototypeVisualTemplate(DeviceBindingProfile deviceProfile)
    {
        return Contains(deviceProfile.ProductName, "HOTAS Warthog") ||
               Contains(deviceProfile.InstanceName, "HOTAS Warthog");
    }

    private static bool Contains(string value, string text)
    {
        return value?.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

/// <summary>
/// Lightweight list item used by the Devices tab left pane.
/// Wraps a real DeviceBindingProfile from the in-memory binding model.
/// </summary>
public sealed class DeviceVisualListItemViewModel
{
    public DeviceBindingProfile DeviceProfile { get; }

    public bool HasVisualTemplate { get; }

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
        ? "Visual template available"
        : "Generic fallback";

    public DeviceVisualListItemViewModel(DeviceBindingProfile deviceProfile, bool hasVisualTemplate)
    {
        DeviceProfile = deviceProfile;
        HasVisualTemplate = hasVisualTemplate;
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Represents the one active callout currently shown on the device image.
///
/// Only one of these should be active at a time. That keeps the visual map readable
/// even when every button on a HOTAS is mapped.
/// </summary>
public sealed class DeviceVisualCalloutViewModel
{
    public string InputId { get; }
    public string PhysicalName { get; }
    public string MappingName { get; }

    public double HighlightX { get; }
    public double HighlightY { get; }
    public double HighlightWidth { get; }
    public double HighlightHeight { get; }

    public double AnchorX { get; }
    public double AnchorY { get; }

    public double CalloutX { get; }
    public double CalloutY { get; }
    public double CalloutWidth { get; }

    public DeviceVisualCalloutViewModel(
        string inputId,
        string physicalName,
        string mappingName,
        double highlightX,
        double highlightY,
        double highlightWidth,
        double highlightHeight,
        double anchorX,
        double anchorY,
        double calloutX,
        double calloutY,
        double calloutWidth)
    {
        InputId = inputId;
        PhysicalName = physicalName;
        MappingName = mappingName;
        HighlightX = highlightX;
        HighlightY = highlightY;
        HighlightWidth = highlightWidth;
        HighlightHeight = highlightHeight;
        AnchorX = anchorX;
        AnchorY = anchorY;
        CalloutX = calloutX;
        CalloutY = calloutY;
        CalloutWidth = calloutWidth;
    }
}