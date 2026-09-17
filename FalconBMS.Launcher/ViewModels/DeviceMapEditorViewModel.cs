using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Utils;
using FalconBMS.Launcher.ViewModels;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FalconBMS.Launcher.ViewModels;

public sealed class DeviceMapEditorViewModel : ViewModelBase
{
    private const string DeviceImageFilter =
        "Device images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";

    private const double MapSurfaceWidthValue =
        1000.0;

    private readonly BindingModel _bindingModel;
    private readonly BindingAircraftProfile _selectedProfile;
    private readonly string _baseDir;
    private readonly Func<Window?> _getOwnerWindow;
    private readonly Action<bool?> _closeWindow;

    private readonly DeviceMapStore _deviceMapStore =
        new();

    private readonly DeviceInputMappingResolver _inputMappingResolver =
        new();

    private string? _pendingImageSourcePath;

    public string WindowTitle { get; }

    public DeviceBindingProfile Device { get; }

    public string DeviceName =>
        !string.IsNullOrWhiteSpace(Device.ProductName)
            ? Device.ProductName
            : Device.InstanceName;

    public string DeviceSummary =>
        $"{Device.ButtonCount} buttons, {Device.PovCount} POV, {Device.AxisCount} axes";

    public ObservableCollection<DeviceMapInputListItemViewModel> Inputs { get; } =
        new();

    public ObservableCollection<DeviceMapHotspotViewModel> Hotspots { get; } =
        new();

    public ObservableCollection<DeviceMapHotspotViewModel> VisibleHotspots { get; } =
        new();

    public ObservableCollection<DeviceMapCalloutViewModel> Callouts { get; } =
        new();

    public ObservableCollection<DeviceMapCalloutViewModel> VisibleCallouts { get; } =
        new();

    public ObservableCollection<DeviceMapConnectorViewModel> VisibleConnectors { get; } =
        new();

    private DeviceMapInputListItemViewModel? _selectedInput;

    public DeviceMapInputListItemViewModel? SelectedInput
    {
        get => _selectedInput;

        set
        {
            if (!Set(
                    ref _selectedInput,
                    value))
            {
                return;
            }

            ResolveSelectedInput(
                isShifted: false);

            RefreshVisibleHotspots();
            RefreshHotspotCommandState();
        }
    }

    private BitmapImage? _deviceImage;

    public BitmapImage? DeviceImage
    {
        get => _deviceImage;

        private set
        {
            if (!Set(
                    ref _deviceImage,
                    value))
            {
                return;
            }

            OnPropertyChanged(
                nameof(HasDeviceImage));

            UpdateMapSurfaceSize();
            SaveCommand.RaiseCanExecuteChanged();
            RefreshHotspotCommandState();
        }
    }

    public bool HasDeviceImage =>
        DeviceImage is not null;

    private string _imageFileName =
        "No image selected";

    public string ImageFileName
    {
        get => _imageFileName;

        private set => Set(
            ref _imageFileName,
            value);
    }

    public double MapSurfaceWidth =>
        MapSurfaceWidthValue;

    private double _mapSurfaceHeight =
        MapSurfaceWidthValue;

    public double MapSurfaceHeight
    {
        get => _mapSurfaceHeight;

        private set
        {
            if (!Set(
                    ref _mapSurfaceHeight,
                    value))
            {
                return;
            }

            foreach (DeviceMapHotspotViewModel hotspot in Hotspots)
            {
                hotspot.RefreshSurfaceMetrics();
            }

            foreach (DeviceMapCalloutViewModel callout in Callouts)
            {
                callout.RefreshSurfaceMetrics();
            }

            RefreshVisibleConnectorMetrics();
        }
    }

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

    private bool _hasCurrentInput;

    public bool HasCurrentInput
    {
        get => _hasCurrentInput;

        private set => Set(
            ref _hasCurrentInput,
            value);
    }

    public RelayCommand ChangeImageCommand { get; }

    public RelayCommand AddHotspotCommand { get; }

    public RelayCommand AddSecondaryHotspotCommand { get; }

    public RelayCommand ClearHotspotsCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    public DeviceMapEditorViewModel(
        BindingModel bindingModel,
        BindingAircraftProfile selectedProfile,
        DeviceBindingProfile device,
        string baseDir,
        bool isEditMode,
        Func<Window?> getOwnerWindow,
        Action<bool?> closeWindow)
    {
        _bindingModel =
            bindingModel;

        _selectedProfile =
            selectedProfile;

        Device =
            device;

        _baseDir =
            baseDir;

        _getOwnerWindow =
            getOwnerWindow;

        _closeWindow =
            closeWindow;

        WindowTitle =
            isEditMode
                ? "Edit Device Map"
                : "Create Device Map";

        ChangeImageCommand =
            new RelayCommand(
                ChooseImage);

        AddHotspotCommand =
            new RelayCommand(
                AddHotspot,
                CanAddFirstHotspot);

        AddSecondaryHotspotCommand =
            new RelayCommand(
                AddHotspot,
                CanAddSecondaryHotspot);

        ClearHotspotsCommand =
            new RelayCommand(
                ClearSelectedInputHotspots,
                CanClearSelectedInputHotspots);

        SaveCommand =
            new RelayCommand(
                SaveAndClose,
                () => HasDeviceImage);

        CancelCommand =
            new RelayCommand(
                () => _closeWindow(false));

        BuildInputList();
        LoadCurrentImage();
        LoadCurrentMap();
    }

    /// <summary>
    /// The editor captures all connected button/POV devices so DX Shift can
    /// still be detected when the shift button lives on another controller.
    /// Only input from the device being edited is selected in the editor.
    /// </summary>
    public IReadOnlyList<DeviceBindingProfile> GetCaptureDevices()
    {
        return _bindingModel.DeviceProfiles
            .Where(device =>
                device.IsConnected &&
                (device.ButtonCount > 0 ||
                 device.PovCount > 0))
            .ToList();
    }

    public bool IsDxShiftActive(
        Func<string, int, bool> isButtonPressed)
    {
        return _inputMappingResolver.IsDxShiftActive(
            _bindingModel,
            _selectedProfile,
            isButtonPressed);
    }

    public void ShowButtonInput(
        string deviceKey,
        int buttonIndex,
        bool isShifted)
    {
        if (!string.Equals(
                Device.DurableDeviceKey,
                deviceKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DeviceMapInputListItemViewModel? input =
            Inputs.FirstOrDefault(item =>
                item.Kind == DeviceMapInputKind.Button &&
                item.ButtonIndex == buttonIndex);

        SelectAndResolveInput(
            input,
            isShifted);
    }

    public void ShowPovInput(
        string deviceKey,
        int povIndex,
        int direction,
        bool isShifted)
    {
        if (!string.Equals(
                Device.DurableDeviceKey,
                deviceKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DeviceMapInputListItemViewModel? input =
            Inputs.FirstOrDefault(item =>
                item.Kind == DeviceMapInputKind.Pov &&
                item.PovIndex == povIndex &&
                item.PovDirection == direction);

        SelectAndResolveInput(
            input,
            isShifted);
    }

    public void SelectInputForHotspot(
        DeviceMapHotspotViewModel hotspot)
    {
        DeviceMapInputListItemViewModel? input =
            Inputs.FirstOrDefault(item =>
                HotspotMatchesInput(
                    hotspot.Model,
                    item));

        if (input is null)
            return;

        SelectedInput =
            input;
    }

    private void BuildInputList()
    {
        Inputs.Clear();

        for (int buttonIndex = 0;
             buttonIndex < Device.ButtonCount;
             buttonIndex++)
        {
            bool hasMapping =
                _inputMappingResolver.ResolveButton(
                    _bindingModel,
                    _selectedProfile,
                    Device,
                    buttonIndex,
                    isShifted: false).IsAssigned ||
                _inputMappingResolver.ResolveButton(
                    _bindingModel,
                    _selectedProfile,
                    Device,
                    buttonIndex,
                    isShifted: true).IsAssigned;

            Inputs.Add(
                DeviceMapInputListItemViewModel.CreateButton(
                    buttonIndex,
                    hasMapping));
        }

        for (int povIndex = 0;
             povIndex < Device.PovCount;
             povIndex++)
        {
            for (int direction = 0;
                 direction < 8;
                 direction++)
            {
                bool hasMapping =
                    _inputMappingResolver.ResolvePov(
                        _bindingModel,
                        _selectedProfile,
                        Device,
                        povIndex,
                        direction,
                        isShifted: false).IsAssigned ||
                    _inputMappingResolver.ResolvePov(
                        _bindingModel,
                        _selectedProfile,
                        Device,
                        povIndex,
                        direction,
                        isShifted: true).IsAssigned;

                Inputs.Add(
                    DeviceMapInputListItemViewModel.CreatePov(
                        povIndex,
                        direction,
                        GetPovDirectionName(direction),
                        hasMapping));
            }
        }
    }

    private void LoadCurrentImage()
    {
        string? imagePath =
            _deviceMapStore.FindImagePath(
                _baseDir,
                Device);

        DeviceImage =
            _deviceMapStore.LoadImage(
                imagePath);

        ImageFileName =
            string.IsNullOrWhiteSpace(imagePath)
                ? "No image selected"
                : Path.GetFileName(imagePath);
    }

    private void LoadCurrentMap()
    {
        DeviceMapDefinition? map =
            _deviceMapStore.LoadMap(
                _baseDir,
                Device);

        Hotspots.Clear();
        Callouts.Clear();

        if (map?.Hotspots is not null)
        {
            foreach (DeviceMapHotspot hotspot in map.Hotspots)
            {
                Hotspots.Add(
                    new DeviceMapHotspotViewModel(
                        hotspot,
                        () => MapSurfaceWidth,
                        () => MapSurfaceHeight));
            }
        }

        if (map?.Callouts is not null)
        {
            foreach (DeviceMapCallout callout in map.Callouts)
            {
                DeviceMapInputListItemViewModel? input =
                    Inputs.FirstOrDefault(item =>
                        CalloutMatchesInput(
                            callout,
                            item));

                if (input is null)
                    continue;

                Callouts.Add(
                    CreateCalloutViewModel(
                        callout,
                        input));
            }
        }

        /*
         * Version-1 maps had hotspots but no callouts.
         * Give those existing hotspots a callout automatically instead of
         * requiring users to recreate their maps.
         */
        foreach (DeviceMapInputListItemViewModel input in Inputs)
        {
            if (GetHotspotCountForInput(input) > 0 &&
                FindCalloutForInput(input) is null)
            {
                CreateDefaultCallout(
                    input);
            }
        }

        RefreshVisibleHotspots();
        RefreshHotspotCommandState();
    }

    private void ChooseImage()
    {
        var openDialog =
            new OpenFileDialog
            {
                Title =
                    HasDeviceImage
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
                _getOwnerWindow()) != true)
        {
            return;
        }

        BitmapImage? image =
            _deviceMapStore.LoadImage(
                openDialog.FileName);

        if (image is null)
            return;

        /*
         * Preview the selected image only. Existing normalized hotspot
         * coordinates are intentionally left unchanged when the image changes.
         * The new image is not copied into Launcher-Backups until Save Map.
         */
        _pendingImageSourcePath =
            openDialog.FileName;

        DeviceImage =
            image;

        ImageFileName =
            Path.GetFileName(
                openDialog.FileName);
    }

    private bool CanAddFirstHotspot()
    {
        return HasDeviceImage &&
               SelectedInput is not null &&
               GetHotspotCountForInput(
                   SelectedInput) == 0;
    }

    private bool CanAddSecondaryHotspot()
    {
        return HasDeviceImage &&
               SelectedInput is not null &&
               GetHotspotCountForInput(
                   SelectedInput) == 1;
    }

    private bool CanClearSelectedInputHotspots()
    {
        return SelectedInput is not null &&
               GetHotspotCountForInput(
                   SelectedInput) > 0;
    }

    private void AddHotspot()
    {
        DeviceMapInputListItemViewModel? input =
            SelectedInput;

        if (input is null ||
            !HasDeviceImage)
        {
            return;
        }

        int existingCount =
            Hotspots.Count(hotspot =>
                HotspotMatchesInput(
                    hotspot.Model,
                    input));

        double offset =
            Math.Min(
                existingCount * 0.025,
                0.15);

        /*
         * Use one display-space diameter for both dimensions.
         * Because Width and Height are normalized against different image
         * dimensions, calculating them separately is what guarantees the new
         * hotspot renders as a true circle.
         */
        const double defaultDiameter =
            80.0;

        double normalizedWidth =
            defaultDiameter /
            MapSurfaceWidth;

        double normalizedHeight =
            defaultDiameter /
            MapSurfaceHeight;

        var model =
            new DeviceMapHotspot
            {
                InputKind =
                    input.Kind == DeviceMapInputKind.Button
                        ? "Button"
                        : "Pov",

                ButtonIndex =
                    input.ButtonIndex,

                PovIndex =
                    input.PovIndex,

                PovDirection =
                    input.PovDirection,

                /*
                 * New hotspots begin centered horizontally in the lower third
                 * of the device image so they do not overlap the default
                 * callout position.
                 */
                X =
                    ((1.0 - normalizedWidth) / 2.0) +
                    offset,

                Y =
                    0.72 -
                    (normalizedHeight / 2.0) +
                    offset,

                Width =
                    normalizedWidth,

                Height =
                    normalizedHeight
            };

        model.X =
            Clamp(
                model.X,
                0.0,
                1.0 - model.Width);

        model.Y =
            Clamp(
                model.Y,
                0.0,
                1.0 - model.Height);

        Hotspots.Add(
            new DeviceMapHotspotViewModel(
                model,
                () => MapSurfaceWidth,
                () => MapSurfaceHeight));

        if (FindCalloutForInput(input) is null)
        {
            CreateDefaultCallout(
                input);
        }

        RefreshVisibleHotspots();
        RefreshHotspotCommandState();
    }

    private void ClearSelectedInputHotspots()
    {
        DeviceMapInputListItemViewModel? input =
            SelectedInput;

        if (input is null)
            return;

        List<DeviceMapHotspotViewModel> hotspotsToRemove =
            Hotspots
                .Where(hotspot =>
                    HotspotMatchesInput(
                        hotspot.Model,
                        input))
                .ToList();

        foreach (DeviceMapHotspotViewModel hotspot in hotspotsToRemove)
        {
            Hotspots.Remove(hotspot);
        }

        DeviceMapCalloutViewModel? callout =
            FindCalloutForInput(
                input);

        if (callout is not null)
        {
            Callouts.Remove(
                callout);
        }

        RefreshVisibleHotspots();
        RefreshHotspotCommandState();
    }

    private void RefreshVisibleHotspots()
    {
        VisibleHotspots.Clear();
        VisibleCallouts.Clear();
        VisibleConnectors.Clear();

        DeviceMapInputListItemViewModel? input =
            SelectedInput;

        if (input is null)
            return;

        foreach (DeviceMapHotspotViewModel hotspot in Hotspots)
        {
            if (HotspotMatchesInput(
                    hotspot.Model,
                    input))
            {
                VisibleHotspots.Add(
                    hotspot);
            }
        }

        DeviceMapCalloutViewModel? callout =
            FindCalloutForInput(
                input);

        if (callout is null)
            return;

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

    public void RefreshVisibleConnectorMetrics()
    {
        foreach (DeviceMapConnectorViewModel connector in VisibleConnectors)
        {
            connector.RefreshMetrics();
        }
    }

    private DeviceMapCalloutViewModel? FindCalloutForInput(
        DeviceMapInputListItemViewModel input)
    {
        return Callouts.FirstOrDefault(callout =>
            CalloutMatchesInput(
                callout.Model,
                input));
    }

    private void CreateDefaultCallout(
        DeviceMapInputListItemViewModel input)
    {
        DeviceInputMappingResult mapping =
            ResolveMappingForCallout(
                input);

        /*
         * Center a newly-created callout on the device-map surface.
         * The stored position remains normalized like the hotspot positions.
         */
        double calloutLeft =
            Math.Max(
                0.0,
                (MapSurfaceWidth -
                 DeviceMapCalloutViewModel.DisplayWidthValue) /
                2.0);

        /*
         * Start the callout centered horizontally in the upper third of the
         * image. This leaves clear space between the default callout and the
         * default hotspot position.
         */
        double calloutTop =
            Math.Max(
                0.0,
                (MapSurfaceHeight * 0.22) -
                (DeviceMapCalloutViewModel.DisplayHeightValue / 2.0));

        var model =
            new DeviceMapCallout
            {
                InputKind =
                    input.Kind == DeviceMapInputKind.Button
                        ? "Button"
                        : "Pov",

                ButtonIndex =
                    input.ButtonIndex,

                PovIndex =
                    input.PovIndex,

                PovDirection =
                    input.PovDirection,

                X =
                    calloutLeft /
                    MapSurfaceWidth,

                Y =
                    calloutTop /
                    MapSurfaceHeight
            };

        Callouts.Add(
            new DeviceMapCalloutViewModel(
                model,
                input.DisplayName,
                mapping.MappingDisplay,
                () => MapSurfaceWidth,
                () => MapSurfaceHeight));
    }

    private DeviceMapCalloutViewModel CreateCalloutViewModel(
        DeviceMapCallout model,
        DeviceMapInputListItemViewModel input)
    {
        DeviceInputMappingResult mapping =
            ResolveMappingForCallout(
                input);

        return new DeviceMapCalloutViewModel(
            model,
            input.DisplayName,
            mapping.MappingDisplay,
            () => MapSurfaceWidth,
            () => MapSurfaceHeight);
    }

    private DeviceInputMappingResult ResolveMappingForCallout(
        DeviceMapInputListItemViewModel input)
    {
        return input.Kind == DeviceMapInputKind.Button
            ? _inputMappingResolver.ResolveButton(
                _bindingModel,
                _selectedProfile,
                Device,
                input.ButtonIndex,
                isShifted: false)
            : _inputMappingResolver.ResolvePov(
                _bindingModel,
                _selectedProfile,
                Device,
                input.PovIndex,
                input.PovDirection,
                isShifted: false);
    }

    private int GetHotspotCountForInput(
    DeviceMapInputListItemViewModel input)
    {
        return Hotspots.Count(hotspot =>
            HotspotMatchesInput(
                hotspot.Model,
                input));
    }

    private bool HasHotspotForInput(
        DeviceMapInputListItemViewModel input)
    {
        return Hotspots.Any(hotspot =>
            HotspotMatchesInput(
                hotspot.Model,
                input));
    }

    private static bool CalloutMatchesInput(
    DeviceMapCallout callout,
    DeviceMapInputListItemViewModel input)
    {
        if (input.Kind == DeviceMapInputKind.Button)
        {
            return string.Equals(
                       callout.InputKind,
                       "Button",
                       StringComparison.OrdinalIgnoreCase) &&
                   callout.ButtonIndex == input.ButtonIndex;
        }

        return string.Equals(
                   callout.InputKind,
                   "Pov",
                   StringComparison.OrdinalIgnoreCase) &&
               callout.PovIndex == input.PovIndex &&
               callout.PovDirection == input.PovDirection;
    }

    private static bool HotspotMatchesInput(
        DeviceMapHotspot hotspot,
        DeviceMapInputListItemViewModel input)
    {
        if (input.Kind == DeviceMapInputKind.Button)
        {
            return string.Equals(
                       hotspot.InputKind,
                       "Button",
                       StringComparison.OrdinalIgnoreCase) &&
                   hotspot.ButtonIndex == input.ButtonIndex;
        }

        return string.Equals(
                   hotspot.InputKind,
                   "Pov",
                   StringComparison.OrdinalIgnoreCase) &&
               hotspot.PovIndex == input.PovIndex &&
               hotspot.PovDirection == input.PovDirection;
    }

    private void SaveAndClose()
    {
        if (!HasDeviceImage)
            return;

        try
        {
            if (_pendingImageSourcePath is not null &&
                !string.IsNullOrWhiteSpace(
                    _pendingImageSourcePath))
            {
                string savedImagePath =
                    _deviceMapStore.ImportUserImage(
                        _baseDir,
                        Device,
                        _pendingImageSourcePath);

                ImageFileName =
                    Path.GetFileName(
                        savedImagePath);
            }

            var map =
                new DeviceMapDefinition
                {
                    DeviceName =
                        DeviceName,

                    PidVid =
                        Device.PidVid,

                    ImageFileName =
                        ImageFileName,

                    Hotspots =
                        Hotspots
                            .Select(hotspot =>
                                hotspot.Model)
                            .ToList(),

                    Callouts =
                        Callouts
                            .Select(callout =>
                                callout.Model)
                            .ToList()
                };

            _deviceMapStore.SaveMap(
                _baseDir,
                Device,
                map);

            _closeWindow(true);
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"Device map save failed: {DeviceName}");

            MessageBox.Show(
                _getOwnerWindow(),
                "The device map could not be saved.\n\n" +
                ex.Message,
                "Device Map",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SelectAndResolveInput(
        DeviceMapInputListItemViewModel? input,
        bool isShifted)
    {
        if (input is null)
            return;

        /*
         * Physical DirectInput selection needs to update the ListBox without
         * first resolving the same item as an ordinary unshifted mouse click.
         */
        if (!ReferenceEquals(
                _selectedInput,
                input))
        {
            _selectedInput =
                input;

            OnPropertyChanged(
                nameof(SelectedInput));

            RefreshVisibleHotspots();
            RefreshHotspotCommandState();
        }

        ResolveInput(
            input,
            isShifted);
    }

    private void ResolveSelectedInput(
        bool isShifted)
    {
        if (SelectedInput is null)
        {
            ClearCurrentInput();
            return;
        }

        ResolveInput(
            SelectedInput,
            isShifted);
    }

    private void ResolveInput(
        DeviceMapInputListItemViewModel input,
        bool isShifted)
    {
        DeviceInputMappingResult result =
            input.Kind == DeviceMapInputKind.Button
                ? _inputMappingResolver.ResolveButton(
                    _bindingModel,
                    _selectedProfile,
                    Device,
                    input.ButtonIndex,
                    isShifted)
                : _inputMappingResolver.ResolvePov(
                    _bindingModel,
                    _selectedProfile,
                    Device,
                    input.PovIndex,
                    input.PovDirection,
                    isShifted);

        CurrentInputDisplay =
            result.InputDisplay;

        CurrentBmsMappingDisplay =
            result.MappingDisplay;

        HasCurrentInput =
            true;
    }

    private void ClearCurrentInput()
    {
        CurrentInputDisplay =
            "";

        CurrentBmsMappingDisplay =
            "";

        HasCurrentInput =
            false;
    }

    private void UpdateMapSurfaceSize()
    {
        if (DeviceImage is null ||
            DeviceImage.PixelWidth <= 0 ||
            DeviceImage.PixelHeight <= 0)
        {
            MapSurfaceHeight =
                MapSurfaceWidthValue;

            return;
        }

        MapSurfaceHeight =
            MapSurfaceWidthValue *
            DeviceImage.PixelHeight /
            DeviceImage.PixelWidth;
    }

    private void RefreshHotspotCommandState()
    {
        AddHotspotCommand.RaiseCanExecuteChanged();
        AddSecondaryHotspotCommand.RaiseCanExecuteChanged();
        ClearHotspotsCommand.RaiseCanExecuteChanged();
    }

    private static string GetPovDirectionName(
        int direction)
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

    private static double Clamp(
        double value,
        double minimum,
        double maximum)
    {
        if (value < minimum)
            return minimum;

        if (value > maximum)
            return maximum;

        return value;
    }
}

public enum DeviceMapInputKind
{
    Button,
    Pov
}

public sealed class DeviceMapInputListItemViewModel
{
    public DeviceMapInputKind Kind { get; private init; }

    public int ButtonIndex { get; private init; } =
        -1;

    public int PovIndex { get; private init; } =
        -1;

    public int PovDirection { get; private init; } =
        -1;

    public string DisplayName { get; private init; } =
        "";

    public bool HasMapping { get; private init; }

    public static DeviceMapInputListItemViewModel CreateButton(
        int buttonIndex,
        bool hasMapping)
    {
        return new DeviceMapInputListItemViewModel
        {
            Kind =
                DeviceMapInputKind.Button,

            ButtonIndex =
                buttonIndex,

            DisplayName =
                "DX" + (buttonIndex + 1),

            HasMapping =
                hasMapping
        };
    }

    public static DeviceMapInputListItemViewModel CreatePov(
        int povIndex,
        int direction,
        string directionName,
        bool hasMapping)
    {
        return new DeviceMapInputListItemViewModel
        {
            Kind =
                DeviceMapInputKind.Pov,

            PovIndex =
                povIndex,

            PovDirection =
                direction,

            DisplayName =
                "POV" +
                (povIndex + 1) +
                " " +
                directionName,

            HasMapping =
                hasMapping
        };
    }
}

public enum DeviceMapHotspotResizeCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public sealed class DeviceMapHotspotViewModel : ViewModelBase
{
    private const double MinimumDisplaySize =
        24.0;

    private readonly Func<double> _getSurfaceWidth;
    private readonly Func<double> _getSurfaceHeight;

    public DeviceMapHotspot Model { get; }

    public double Left =>
        Model.X * GetSurfaceWidth();

    public double Top =>
        Model.Y * GetSurfaceHeight();

    public double DisplayWidth =>
        Model.Width * GetSurfaceWidth();

    public double DisplayHeight =>
        Model.Height * GetSurfaceHeight();

    public DeviceMapHotspotViewModel(
        DeviceMapHotspot model,
        Func<double> getSurfaceWidth,
        Func<double> getSurfaceHeight)
    {
        Model =
            model;

        _getSurfaceWidth =
            getSurfaceWidth;

        _getSurfaceHeight =
            getSurfaceHeight;

        NormalizeModel();
    }

    public void MoveTo(
        double left,
        double top)
    {
        double surfaceWidth =
            GetSurfaceWidth();

        double surfaceHeight =
            GetSurfaceHeight();

        double width =
            DisplayWidth;

        double height =
            DisplayHeight;

        left =
            Clamp(
                left,
                0.0,
                Math.Max(
                    0.0,
                    surfaceWidth - width));

        top =
            Clamp(
                top,
                0.0,
                Math.Max(
                    0.0,
                    surfaceHeight - height));

        Model.X =
            surfaceWidth <= 0.0
                ? 0.0
                : left / surfaceWidth;

        Model.Y =
            surfaceHeight <= 0.0
                ? 0.0
                : top / surfaceHeight;

        OnPropertyChanged(
            nameof(Left));

        OnPropertyChanged(
            nameof(Top));
    }

    public void ResizeFromCorner(
        DeviceMapHotspotResizeCorner corner,
        double startLeft,
        double startTop,
        double startWidth,
        double startHeight,
        double deltaX,
        double deltaY,
        bool keepAspectRatio)
    {
        double surfaceWidth =
            GetSurfaceWidth();

        double surfaceHeight =
            GetSurfaceHeight();

        double startRight =
            startLeft + startWidth;

        double startBottom =
            startTop + startHeight;

        double left =
            startLeft;

        double top =
            startTop;

        double right =
            startRight;

        double bottom =
            startBottom;

        if (keepAspectRatio)
        {
            double aspectRatio =
                startWidth / startHeight;

            double proposedWidth =
                Math.Max(
                    MinimumDisplaySize,
                    startWidth +
                    (corner == DeviceMapHotspotResizeCorner.TopLeft ||
                     corner == DeviceMapHotspotResizeCorner.BottomLeft
                        ? -deltaX
                        : deltaX));

            double proposedHeight =
                proposedWidth / aspectRatio;

            switch (corner)
            {
                case DeviceMapHotspotResizeCorner.TopLeft:

                    left =
                        startRight - proposedWidth;

                    top =
                        startBottom - proposedHeight;

                    break;

                case DeviceMapHotspotResizeCorner.TopRight:

                    right =
                        startLeft + proposedWidth;

                    top =
                        startBottom - proposedHeight;

                    break;

                case DeviceMapHotspotResizeCorner.BottomLeft:

                    left =
                        startRight - proposedWidth;

                    bottom =
                        startTop + proposedHeight;

                    break;

                case DeviceMapHotspotResizeCorner.BottomRight:

                    right =
                        startLeft + proposedWidth;

                    bottom =
                        startTop + proposedHeight;

                    break;
            }

            // Clamp while preserving the original aspect ratio.
            if (left < 0.0)
            {
                left = 0.0;
                proposedWidth = startRight;
                proposedHeight = proposedWidth / aspectRatio;
                top = startBottom - proposedHeight;
            }

            if (right > surfaceWidth)
            {
                right = surfaceWidth;
                proposedWidth = surfaceWidth - startLeft;
                proposedHeight = proposedWidth / aspectRatio;

                if (corner == DeviceMapHotspotResizeCorner.TopRight)
                    top = startBottom - proposedHeight;
                else
                    bottom = startTop + proposedHeight;
            }

            if (top < 0.0)
            {
                top = 0.0;
                proposedHeight = startBottom;
                proposedWidth = proposedHeight * aspectRatio;

                if (corner == DeviceMapHotspotResizeCorner.TopLeft)
                    left = startRight - proposedWidth;
                else
                    right = startLeft + proposedWidth;
            }

            if (bottom > surfaceHeight)
            {
                bottom = surfaceHeight;
                proposedHeight = surfaceHeight - startTop;
                proposedWidth = proposedHeight * aspectRatio;

                if (corner == DeviceMapHotspotResizeCorner.BottomLeft)
                    left = startRight - proposedWidth;
                else
                    right = startLeft + proposedWidth;
            }
        }
        else
        {
            switch (corner)
            {
                case DeviceMapHotspotResizeCorner.TopLeft:

                    left =
                        Clamp(
                            startLeft + deltaX,
                            0.0,
                            startRight - MinimumDisplaySize);

                    top =
                        Clamp(
                            startTop + deltaY,
                            0.0,
                            startBottom - MinimumDisplaySize);

                    break;

                case DeviceMapHotspotResizeCorner.TopRight:

                    right =
                        Clamp(
                            startRight + deltaX,
                            startLeft + MinimumDisplaySize,
                            surfaceWidth);

                    top =
                        Clamp(
                            startTop + deltaY,
                            0.0,
                            startBottom - MinimumDisplaySize);

                    break;

                case DeviceMapHotspotResizeCorner.BottomLeft:

                    left =
                        Clamp(
                            startLeft + deltaX,
                            0.0,
                            startRight - MinimumDisplaySize);

                    bottom =
                        Clamp(
                            startBottom + deltaY,
                            startTop + MinimumDisplaySize,
                            surfaceHeight);

                    break;

                case DeviceMapHotspotResizeCorner.BottomRight:

                    right =
                        Clamp(
                            startRight + deltaX,
                            startLeft + MinimumDisplaySize,
                            surfaceWidth);

                    bottom =
                        Clamp(
                            startBottom + deltaY,
                            startTop + MinimumDisplaySize,
                            surfaceHeight);

                    break;
            }
        }

        Model.X =
            left / surfaceWidth;

        Model.Y =
            top / surfaceHeight;

        Model.Width =
            (right - left) / surfaceWidth;

        Model.Height =
            (bottom - top) / surfaceHeight;

        OnPropertyChanged(
            nameof(Left));

        OnPropertyChanged(
            nameof(Top));

        OnPropertyChanged(
            nameof(DisplayWidth));

        OnPropertyChanged(
            nameof(DisplayHeight));
    }

    public void RefreshSurfaceMetrics()
    {
        OnPropertyChanged(
            nameof(Left));

        OnPropertyChanged(
            nameof(Top));

        OnPropertyChanged(
            nameof(DisplayWidth));

        OnPropertyChanged(
            nameof(DisplayHeight));
    }

    private void NormalizeModel()
    {
        Model.Width =
            Clamp(
                Model.Width,
                0.01,
                1.0);

        Model.Height =
            Clamp(
                Model.Height,
                0.01,
                1.0);

        Model.X =
            Clamp(
                Model.X,
                0.0,
                Math.Max(
                    0.0,
                    1.0 - Model.Width));

        Model.Y =
            Clamp(
                Model.Y,
                0.0,
                Math.Max(
                    0.0,
                    1.0 - Model.Height));
    }

    private double GetSurfaceWidth()
    {
        return Math.Max(
            1.0,
            _getSurfaceWidth());
    }

    private double GetSurfaceHeight()
    {
        return Math.Max(
            1.0,
            _getSurfaceHeight());
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum)
    {
        if (value < minimum)
            return minimum;

        if (value > maximum)
            return maximum;

        return value;
    }
}

public sealed class DeviceMapCalloutViewModel : ViewModelBase
{
    public const double DisplayWidthValue =
        340.0;

    public const double DisplayHeightValue =
        94.0;

    private readonly Func<double> _getSurfaceWidth;
    private readonly Func<double> _getSurfaceHeight;

    public DeviceMapCallout Model { get; }

    public string InputDisplay { get; }

    public string MappingDisplay { get; }

    public double Left =>
        Model.X * GetSurfaceWidth();

    public double Top =>
        Model.Y * GetSurfaceHeight();

    public double DisplayWidth =>
        DisplayWidthValue;

    public double DisplayHeight =>
        DisplayHeightValue;

    public double CenterX =>
        Left + (DisplayWidth / 2.0);

    public double CenterY =>
        Top + (DisplayHeight / 2.0);

    public DeviceMapCalloutViewModel(
        DeviceMapCallout model,
        string inputDisplay,
        string mappingDisplay,
        Func<double> getSurfaceWidth,
        Func<double> getSurfaceHeight)
    {
        Model =
            model;

        InputDisplay =
            inputDisplay;

        MappingDisplay =
            mappingDisplay;

        _getSurfaceWidth =
            getSurfaceWidth;

        _getSurfaceHeight =
            getSurfaceHeight;

        NormalizeModel();
    }

    public void MoveTo(
        double left,
        double top)
    {
        double surfaceWidth =
            GetSurfaceWidth();

        double surfaceHeight =
            GetSurfaceHeight();

        left =
            Clamp(
                left,
                0.0,
                Math.Max(
                    0.0,
                    surfaceWidth - DisplayWidth));

        top =
            Clamp(
                top,
                0.0,
                Math.Max(
                    0.0,
                    surfaceHeight - DisplayHeight));

        Model.X =
            left / surfaceWidth;

        Model.Y =
            top / surfaceHeight;

        RefreshSurfaceMetrics();
    }

    public void RefreshSurfaceMetrics()
    {
        OnPropertyChanged(
            nameof(Left));

        OnPropertyChanged(
            nameof(Top));

        OnPropertyChanged(
            nameof(CenterX));

        OnPropertyChanged(
            nameof(CenterY));
    }

    private void NormalizeModel()
    {
        double maximumX =
            Math.Max(
                0.0,
                1.0 -
                (DisplayWidth / GetSurfaceWidth()));

        double maximumY =
            Math.Max(
                0.0,
                1.0 -
                (DisplayHeight / GetSurfaceHeight()));

        Model.X =
            Clamp(
                Model.X,
                0.0,
                maximumX);

        Model.Y =
            Clamp(
                Model.Y,
                0.0,
                maximumY);
    }

    private double GetSurfaceWidth()
    {
        return Math.Max(
            1.0,
            _getSurfaceWidth());
    }

    private double GetSurfaceHeight()
    {
        return Math.Max(
            1.0,
            _getSurfaceHeight());
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum)
    {
        if (value < minimum)
            return minimum;

        if (value > maximum)
            return maximum;

        return value;
    }
}

public sealed class DeviceMapConnectorViewModel : ViewModelBase
{
    private readonly DeviceMapHotspotViewModel _hotspot;
    private readonly DeviceMapCalloutViewModel _callout;

    public Geometry ConnectorGeometry =>
        BuildConnectorGeometry();

    public DeviceMapConnectorViewModel(
        DeviceMapHotspotViewModel hotspot,
        DeviceMapCalloutViewModel callout)
    {
        _hotspot =
            hotspot;

        _callout =
            callout;
    }

    public void RefreshMetrics()
    {
        OnPropertyChanged(
            nameof(ConnectorGeometry));
    }

    private Geometry BuildConnectorGeometry()
    {
        double startX =
            _hotspot.Left +
            (_hotspot.DisplayWidth / 2.0);

        double startY =
            _hotspot.Top +
            (_hotspot.DisplayHeight / 2.0);

        double calloutLeft =
            _callout.Left;

        double calloutRight =
            _callout.Left +
            _callout.DisplayWidth;

        /*
         * Connect to the nearest horizontal edge of the callout.
         * If the hotspot sits beneath the callout itself, connect toward
         * the callout's horizontal center.
         */
        double targetX =
            startX < calloutLeft
                ? calloutLeft
                : startX > calloutRight
                    ? calloutRight
                    : _callout.CenterX;

        double targetY =
            _callout.CenterY;

        /*
         * Match the old Devices-map connector behavior: the first control
         * point leaves the hotspot horizontally, while the second approaches
         * the label horizontally. This produces the soft S-shaped curve.
         */
        double control1X =
            startX +
            ((targetX - startX) * 0.45);

        double control2X =
            startX +
            ((targetX - startX) * 0.65);

        var figure =
            new PathFigure
            {
                StartPoint =
                    new Point(
                        startX,
                        startY),

                IsClosed =
                    false,

                IsFilled =
                    false
            };

        figure.Segments.Add(
            new BezierSegment(
                new Point(
                    control1X,
                    startY),

                new Point(
                    control2X,
                    targetY),

                new Point(
                    targetX,
                    targetY),

                isStroked: true));

        var geometry =
            new PathGeometry();

        geometry.Figures.Add(
            figure);

        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }
}