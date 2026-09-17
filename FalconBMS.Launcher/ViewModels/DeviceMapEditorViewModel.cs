using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FalconBMS.Launcher.ViewModels;

public sealed class DeviceMapEditorViewModel : ViewModelBase
{
    private const string DeviceImageFilter =
        "Device images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";

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

            SaveCommand.RaiseCanExecuteChanged();
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

        SaveCommand =
            new RelayCommand(
                SaveAndClose,
                () => HasDeviceImage);

        CancelCommand =
            new RelayCommand(
                () => _closeWindow(false));

        BuildInputList();
        LoadCurrentImage();
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
         * Preview the selected image only.
         * It is not copied into Launcher-Backups until Save Map is clicked.
         */
        _pendingImageSourcePath =
            openDialog.FileName;

        DeviceImage =
            image;

        ImageFileName =
            Path.GetFileName(
                openDialog.FileName);
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
                _deviceMapStore.ImportUserImage(
                    _baseDir,
                    Device,
                    _pendingImageSourcePath);
            }

            _closeWindow(true);
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"Device map image save failed: {_pendingImageSourcePath ?? ImageFileName}");

            MessageBox.Show(
                _getOwnerWindow(),
                "The device map image could not be saved.\n\n" +
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