using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Vortice.DirectInput;
using DiKey = Vortice.DirectInput.Key;

namespace FalconBMS.Launcher.ViewModels;

public sealed class KeyMappingWindowViewModel : ViewModelBase, IDisposable
{
    private const string PovInvokeDefault = "Default";
    private const string PovInvokeShift = "Shift";

    private readonly BindingRow _row;
    private readonly List<BindingRow> _profileRows;
    private readonly List<DeviceBindingProfile> _deviceProfiles;
    private readonly string _aircraftProfileName;
    private readonly Action<BindingRow, string, int, string, int> _saveKeyboardBinding;
    private readonly Action<BindingRow, string?, int?, int?> _saveDeviceButtonBinding;
    private readonly Action<BindingRow, string?, int?, int?, string?> _saveDevicePovBinding;
    private readonly Action _closeWindow;
    private readonly DirectInputCaptureHost _captureHost = new();

    private string _tempKeyScancode;
    private int _tempModifierFlags;
    private string _tempChordScancode;
    private int _tempChordModifierFlags;
    private bool _isReservedKeyWarningActive;

    private readonly List<PendingDxButton> _pendingDxButtons = new();
    private readonly List<PendingDxPov> _pendingDxPovs = new();

    private sealed class PendingDxButton
    {
        public string DeviceKey { get; set; } = "";
        public int ButtonIndex { get; set; }
        public int AssignmentIndex { get; set; }
    }

    private sealed class PendingDxPov
    {
        public string DeviceKey { get; set; } = "";
        public int PovIndex { get; set; }
        public int Direction { get; set; }
        public string Invoke { get; set; } = PovInvokeDefault;
    }

    public string TitleText { get; }

    private string _keyboardAssignmentText;
    public string KeyboardAssignmentText
    {
        get => _keyboardAssignmentText;
        private set => Set(ref _keyboardAssignmentText, value);
    }

    private string _dxAssignmentText;
    public string DxAssignmentText
    {
        get => _dxAssignmentText;
        private set => Set(ref _dxAssignmentText, value);
    }

    private string _keyboardConflictText = "";
    public string KeyboardConflictText
    {
        get => _keyboardConflictText;
        private set => Set(ref _keyboardConflictText, value);
    }

    private string _dxConflictText = "";
    public string DxConflictText
    {
        get => _dxConflictText;
        private set => Set(ref _dxConflictText, value);
    }

    private bool _isUnshifted = true;
    public bool IsUnshifted
    {
        get => _isUnshifted;
        set
        {
            if (!Set(ref _isUnshifted, value))
                return;

            OnPropertyChanged(nameof(IsShifted));
        }
    }

    public bool IsShifted
    {
        get => !IsUnshifted;
        set => IsUnshifted = !value;
    }

    private bool _isOnPress = true;
    public bool IsOnPress
    {
        get => _isOnPress;
        set
        {
            if (!Set(ref _isOnPress, value))
                return;

            OnPropertyChanged(nameof(IsOnRelease));
        }
    }

    public bool IsOnRelease
    {
        get => !IsOnPress;
        set => IsOnPress = !value;
    }

    public bool IsDxOptionSelectionEnabled => !DeviceButtonBinding.IsDxShiftCallback(_row.CallbackName);

    public ICommand ClearDxCommand { get; }
    public ICommand ClearKeyCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public KeyMappingWindowViewModel(
        BindingRow row,
        IEnumerable<BindingRow> profileRows,
        IEnumerable<DeviceBindingProfile> deviceProfiles,
        string aircraftProfileName,
        Action<BindingRow, string, int, string, int> saveKeyboardBinding,
        Action<BindingRow, string?, int?, int?> saveDeviceButtonBinding,
        Action<BindingRow, string?, int?, int?, string?> saveDevicePovBinding,
        Action closeWindow)
    {
        _row = row;
        _profileRows = profileRows.ToList();
        _deviceProfiles = deviceProfiles.ToList();
        _aircraftProfileName = aircraftProfileName;
        _saveKeyboardBinding = saveKeyboardBinding;
        _saveDeviceButtonBinding = saveDeviceButtonBinding;
        _saveDevicePovBinding = saveDevicePovBinding;
        _closeWindow = closeWindow;

        _captureHost.KeyboardInput +=
            CaptureSession_KeyboardInput;

        _captureHost.JoystickButtonInput +=
            CaptureSession_JoystickButtonInput;

        _captureHost.JoystickPovInput +=
            CaptureSession_JoystickPovInput;

        TitleText = row.Description;

        _tempKeyScancode = row.KeyScancode;
        _tempModifierFlags = row.KeyModifierFlags;
        _tempChordScancode = row.ChordScancode;
        _tempChordModifierFlags = row.ChordModifierFlags;

        LoadExistingDxBindings(row.CallbackName);
        ForceBaseDxShiftStateIfNeeded();

        _keyboardAssignmentText = BuildKeyboardAssignmentPreview();
        _dxAssignmentText = BuildDxAssignmentPreview();
        UpdateConflict();

        ClearDxCommand = new RelayCommand(() =>
        {
            _pendingDxButtons.Clear();
            _pendingDxPovs.Clear();

            // Buffered DirectInput only reports changes. A button or POV that
            // was already held when Clear DX was clicked does not generate a
            // second DOWN event, so no polling warmup/baseline reset is needed
            UpdateAssignmentPreviewTexts();
            UpdateConflict();
        });

        ClearKeyCommand = new RelayCommand(() =>
        {
            _tempKeyScancode = "0xFFFFFFFF";
            _tempModifierFlags = 0;
            _tempChordScancode = "0";
            _tempChordModifierFlags = 0;

            UpdateAssignmentPreviewTexts();
            UpdateConflict();
        });

        SaveCommand = new RelayCommand(() =>
        {
            _saveKeyboardBinding(
                _row,
                _tempKeyScancode,
                _tempModifierFlags,
                _tempChordScancode,
                _tempChordModifierFlags);

            // Replace this callback's DX list with the current pending list.
            // Treats DX as button OR POV, so this clear removes both.
            _saveDeviceButtonBinding(_row, null, null, null);

            foreach (PendingDxButton pendingDxButton in _pendingDxButtons)
            {
                _saveDeviceButtonBinding(
                    _row,
                    pendingDxButton.DeviceKey,
                    pendingDxButton.ButtonIndex,
                    pendingDxButton.AssignmentIndex);
            }

            foreach (PendingDxPov pendingDxPov in _pendingDxPovs)
            {
                _saveDevicePovBinding(
                    _row,
                    pendingDxPov.DeviceKey,
                    pendingDxPov.PovIndex,
                    pendingDxPov.Direction,
                    pendingDxPov.Invoke);
            }

            _closeWindow();
        });

        CancelCommand = new RelayCommand(_closeWindow);
    }

    public void StartCapture(IntPtr hwnd)
    {
        StopCapture();

        IEnumerable<DirectInputCaptureDevice> joystickDevices =
            _deviceProfiles
                .Where(device =>
                    device.IsConnected &&
                    (device.ButtonCount > 0 ||
                     device.PovCount > 0))
                .Select(device =>
                    new DirectInputCaptureDevice(
                        device.DurableDeviceKey,
                        device.InstanceGuid));

        _captureHost.Start(
            Application.Current.Dispatcher,
            hwnd,
            captureKeyboard: true,
            joystickDevices: joystickDevices);
    }

    public void StopCapture()
    {
        _captureHost.Stop();
    }

    private void CaptureSession_KeyboardInput(
        object? sender,
        BufferedKeyboardInputEventArgs e)
    {
        // Ignore capture input while the reserved-key warning is showing, 
        // so repeated key events don't display more warning windows
        if (_isReservedKeyWarningActive)
            return;

        // Existing Key Mapping behavior creates an assignment on the
        // non-modifier key DOWN event. Modifier-only presses do not map.
        if (!e.IsPressed)
            return;

        DiKey caught =
            e.Key;

        if (caught == DiKey.Unknown ||
            caught == DiKey.LeftShift ||
            caught == DiKey.RightShift ||
            caught == DiKey.LeftControl ||
            caught == DiKey.RightControl ||
            caught == DiKey.LeftAlt ||
            caught == DiKey.RightAlt)
        {
            return;
        }

        int modifierFlags =
            e.ModifierFlags;

        // Reserve keys for BMS and Windows
        if (ReservedKeyboardBindings.TryGetDisplayText(
                caught,
                modifierFlags,
                out string reservedBinding))
        {
            _isReservedKeyWarningActive = true;

            try
            {
                MessageBox.Show(
                    reservedBinding +
                    " is reserved and cannot be reassigned.",
                    "Reserved Key",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _isReservedKeyWarningActive = false;
            }

            return;
        }

        _tempKeyScancode =
            "0x" +
            ((int)caught).ToString("X");

        _tempModifierFlags =
            modifierFlags;

        _tempChordScancode =
            "0";

        _tempChordModifierFlags =
            0;

        UpdateAssignmentPreviewTexts();
        UpdateConflict();
    }

    private void CaptureSession_JoystickButtonInput(
        object? sender,
        BufferedJoystickButtonEventArgs e)
    {
        // The physical event identifies the DX button. Shifted/Unshifted
        // and Press/Release remain manual choices in the popup.
        if (!e.IsPressed)
            return;

        AddPendingDxButton(
            e.DeviceKey,
            e.ButtonIndex);

        UpdateAssignmentPreviewTexts();
        UpdateConflict();
    }

    private void CaptureSession_JoystickPovInput(
        object? sender,
        BufferedJoystickPovEventArgs e)
    {
        int? direction =
            e.Direction;

        // Ignore the centered/released POV event. A new directional event
        // will arrive the next time the hat is moved.
        if (!direction.HasValue)
            return;

        AddPendingDxPov(
            e.DeviceKey,
            e.PovIndex,
            direction.Value);

        UpdateAssignmentPreviewTexts();
        UpdateConflict();
    }

    private void UpdateConflict()
    {
        string selectedAssignment = BuildKeyboardAssignmentText();

        string keyboardConflict = "";

        if (!string.IsNullOrWhiteSpace(selectedAssignment))
        {
            BindingRow? conflict = _profileRows.FirstOrDefault(row =>
                !ReferenceEquals(row, _row) &&
                row.IsEditable &&
                string.Equals(GetAssignmentText(row), selectedAssignment, StringComparison.OrdinalIgnoreCase));

            if (conflict is not null)
                keyboardConflict = "\'" + selectedAssignment + "\' is currently being used by: " + conflict.Description.Trim() + "";
        }

        KeyboardConflictText = string.IsNullOrWhiteSpace(keyboardConflict)
            ? ""
            : keyboardConflict + "\nClick \"Save\" to replace this anyway.";

        var dxConflicts = new List<string>();

        foreach (PendingDxButton pendingDxButton in _pendingDxButtons)
        {
            DeviceBindingProfile? device = _deviceProfiles.FirstOrDefault(d =>
                string.Equals(d.DurableDeviceKey, pendingDxButton.DeviceKey, StringComparison.OrdinalIgnoreCase));

            DeviceAircraftBindingProfile? aircraft = device?.AircraftProfiles.FirstOrDefault(profile =>
                string.Equals(profile.AircraftProfile, _aircraftProfileName, StringComparison.OrdinalIgnoreCase));

            DeviceButtonBinding? conflict = aircraft?.ButtonBindings.FirstOrDefault(binding =>
                binding.ButtonIndex == pendingDxButton.ButtonIndex &&
                binding.AssignmentIndex == pendingDxButton.AssignmentIndex &&
                !string.Equals(binding.CallbackName, _row.CallbackName, StringComparison.OrdinalIgnoreCase));

            if (conflict is null)
                continue;

            BindingRow? conflictRow = _profileRows.FirstOrDefault(row =>
                string.Equals(row.CallbackName, conflict.CallbackName, StringComparison.OrdinalIgnoreCase));

            string deviceName = device?.ProductName
                ?? device?.InstanceName
                ?? pendingDxButton.DeviceKey;

            dxConflicts.Add(
                deviceName +
                " " +
                DeviceButtonBinding.BuildDisplayText(
                    pendingDxButton.ButtonIndex,
                    pendingDxButton.AssignmentIndex) +
                " is currently being used by: " +
                (conflictRow?.Description.Trim() ?? conflict.CallbackName));
        }

        foreach (PendingDxPov pendingDxPov in _pendingDxPovs)
        {
            DeviceBindingProfile? device = _deviceProfiles.FirstOrDefault(d =>
                string.Equals(d.DurableDeviceKey, pendingDxPov.DeviceKey, StringComparison.OrdinalIgnoreCase));

            DeviceAircraftBindingProfile? aircraft = device?.AircraftProfiles.FirstOrDefault(profile =>
                string.Equals(profile.AircraftProfile, _aircraftProfileName, StringComparison.OrdinalIgnoreCase));

            DevicePovBinding? conflict = aircraft?.PovBindings.FirstOrDefault(binding =>
                binding.PovIndex == pendingDxPov.PovIndex &&
                binding.Direction == pendingDxPov.Direction &&
                string.Equals(NormalizePovInvoke(binding.Invoke), pendingDxPov.Invoke, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(binding.CallbackName, _row.CallbackName, StringComparison.OrdinalIgnoreCase));

            if (conflict is null)
                continue;

            BindingRow? conflictRow = _profileRows.FirstOrDefault(row =>
                string.Equals(row.CallbackName, conflict.CallbackName, StringComparison.OrdinalIgnoreCase));

            string deviceName = device?.ProductName
                ?? device?.InstanceName
                ?? pendingDxPov.DeviceKey;

            dxConflicts.Add(
                deviceName +
                " " +
                BuildPovDisplayText(
                    pendingDxPov.PovIndex,
                    pendingDxPov.Direction,
                    pendingDxPov.Invoke) +
                " is currently being used by: " +
                (conflictRow?.Description.Trim() ?? conflict.CallbackName));
        }

        List<string> distinctDxConflicts = dxConflicts
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DxConflictText = distinctDxConflicts.Count == 0
            ? ""
            : string.Join("\n", distinctDxConflicts) + "\nClick \"Save\" to replace this anyway.";
    }

    private static string GetAssignmentText(BindingRow row)
    {
        return KeyAssgn.GetKeyAssignmentStatus(
            row.KeyScancode,
            row.KeyModifierFlags,
            row.ChordScancode,
            row.ChordModifierFlags);
    }

    private void UpdateAssignmentPreviewTexts()
    {
        KeyboardAssignmentText = BuildKeyboardAssignmentPreview();
        DxAssignmentText = BuildDxAssignmentPreview();
    }

    private string BuildKeyboardAssignmentPreview()
    {
        string keyText = BuildKeyboardAssignmentText();

        return string.IsNullOrWhiteSpace(keyText)
            ? "Press any key to assign"
            : keyText;
    }

    private string BuildDxAssignmentPreview()
    {
        var parts = new List<string>();

        foreach (PendingDxButton pendingDxButton in _pendingDxButtons)
        {
            DeviceBindingProfile? device = _deviceProfiles.FirstOrDefault(d =>
                string.Equals(d.DurableDeviceKey, pendingDxButton.DeviceKey, StringComparison.OrdinalIgnoreCase));

            string deviceName = device?.ProductName
                ?? device?.InstanceName
                ?? pendingDxButton.DeviceKey;

            parts.Add(deviceName + " " + DeviceButtonBinding.BuildDisplayText(pendingDxButton.ButtonIndex, pendingDxButton.AssignmentIndex));
        }

        foreach (PendingDxPov pendingDxPov in _pendingDxPovs)
        {
            DeviceBindingProfile? device = _deviceProfiles.FirstOrDefault(d =>
                string.Equals(d.DurableDeviceKey, pendingDxPov.DeviceKey, StringComparison.OrdinalIgnoreCase));

            string deviceName = device?.ProductName
                ?? device?.InstanceName
                ?? pendingDxPov.DeviceKey;

            parts.Add(deviceName + " " + BuildPovDisplayText(pendingDxPov.PovIndex, pendingDxPov.Direction, pendingDxPov.Invoke));
        }

        return parts.Count == 0
            ? "Press any button to assign"
            : string.Join(" / ", parts);
    }

    private string BuildKeyboardAssignmentText()
    {
        return KeyAssgn.GetKeyAssignmentStatus(
            _tempKeyScancode,
            _tempModifierFlags,
            _tempChordScancode,
            _tempChordModifierFlags);
    }

    private void LoadExistingDxBindings(string callbackName)
    {
        var existingButtonBindings = new List<DeviceButtonBinding>();
        var existingPovBindings = new List<DevicePovBinding>();

        foreach (DeviceBindingProfile device in _deviceProfiles)
        {
            DeviceAircraftBindingProfile? aircraft = device.AircraftProfiles.FirstOrDefault(profile =>
                string.Equals(profile.AircraftProfile, _aircraftProfileName, StringComparison.OrdinalIgnoreCase));

            if (aircraft is null)
                continue;

            foreach (DeviceButtonBinding binding in aircraft.ButtonBindings
                         .Where(button => string.Equals(button.CallbackName, callbackName, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(button => button.ButtonIndex)
                         .ThenBy(button => button.AssignmentIndex))
            {
                int assignmentIndex = DeviceButtonBinding.NormalizeAssignmentIndexForCallback(callbackName, binding.AssignmentIndex);

                existingButtonBindings.Add(binding);
                AddPendingDxButton(device.DurableDeviceKey, binding.ButtonIndex, assignmentIndex);
            }

            foreach (DevicePovBinding binding in aircraft.PovBindings
                         .Where(pov => string.Equals(pov.CallbackName, callbackName, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(pov => pov.PovIndex)
                         .ThenBy(pov => pov.Direction)
                         .ThenBy(pov => pov.Invoke))
            {
                string invoke = NormalizePovInvoke(binding.Invoke);

                existingPovBindings.Add(binding);
                AddPendingDxPov(device.DurableDeviceKey, binding.PovIndex, binding.Direction, invoke);
            }
        }

        if (existingButtonBindings.Count == 0 && existingPovBindings.Count == 0)
            return;

        if (DeviceButtonBinding.IsDxShiftCallback(callbackName))
        {
            ForceBaseDxShiftStateIfNeeded();
            return;
        }

        // Use the first existing DX binding to initialize the radio buttons.
        // Most rows only have one DX binding. If a row has multiple DX bindings,
        // the preview still shows all of them, and this simply sets the default
        // radio state for the next DX input the user captures.
        if (existingButtonBindings.Count > 0)
        {
            DeviceButtonBinding firstBinding = existingButtonBindings[0];

            IsUnshifted = DeviceButtonBinding.GetShiftState(firstBinding.AssignmentIndex) == DeviceButtonBinding.ShiftStateUnshifted;
            IsOnPress = DeviceButtonBinding.GetTrigger(firstBinding.AssignmentIndex) == DeviceButtonBinding.TriggerPress;
            return;
        }

        DevicePovBinding firstPovBinding = existingPovBindings[0];

        IsUnshifted = !string.Equals(
            NormalizePovInvoke(firstPovBinding.Invoke),
            PovInvokeShift,
            StringComparison.OrdinalIgnoreCase);

        // POV bindings are directional press-style DX inputs. There is no separate POV release slot.
        IsOnPress = true;
    }

    private void AddPendingDxButton(string deviceKey, int buttonIndex)
    {
        if (DeviceButtonBinding.IsDxShiftCallback(_row.CallbackName))
        {
            AddPendingDxButton(deviceKey, buttonIndex, 0);
            return;
        }

        string shiftState = IsUnshifted
            ? DeviceButtonBinding.ShiftStateUnshifted
            : DeviceButtonBinding.ShiftStateShifted;

        string trigger = IsOnPress
            ? DeviceButtonBinding.TriggerPress
            : DeviceButtonBinding.TriggerRelease;

        AddPendingDxButton(deviceKey, buttonIndex, DeviceButtonBinding.GetAssignmentIndex(shiftState, trigger));
    }

    private void AddPendingDxButton(string deviceKey, int buttonIndex, int assignmentIndex)
    {
        assignmentIndex = DeviceButtonBinding.NormalizeAssignmentIndexForCallback(_row.CallbackName, assignmentIndex);

        bool alreadyPending = _pendingDxButtons.Any(button =>
            string.Equals(button.DeviceKey, deviceKey, StringComparison.OrdinalIgnoreCase) &&
            button.ButtonIndex == buttonIndex &&
            button.AssignmentIndex == assignmentIndex);

        if (alreadyPending)
            return;

        // Keep the list stable and readable in the popup preview.
        _pendingDxButtons.Add(new PendingDxButton
        {
            DeviceKey = deviceKey,
            ButtonIndex = buttonIndex,
            AssignmentIndex = assignmentIndex
        });
    }

    private void AddPendingDxPov(string deviceKey, int povIndex, int direction)
    {
        string invoke = IsUnshifted || DeviceButtonBinding.IsDxShiftCallback(_row.CallbackName)
            ? PovInvokeDefault
            : PovInvokeShift;

        AddPendingDxPov(deviceKey, povIndex, direction, invoke);
    }

    private void AddPendingDxPov(string deviceKey, int povIndex, int direction, string invoke)
    {
        invoke = NormalizePovInvoke(invoke);

        bool alreadyPending = _pendingDxPovs.Any(pov =>
            string.Equals(pov.DeviceKey, deviceKey, StringComparison.OrdinalIgnoreCase) &&
            pov.PovIndex == povIndex &&
            pov.Direction == direction &&
            string.Equals(pov.Invoke, invoke, StringComparison.OrdinalIgnoreCase));

        if (alreadyPending)
            return;

        // Keep the list stable and readable in the popup preview
        _pendingDxPovs.Add(new PendingDxPov
        {
            DeviceKey = deviceKey,
            PovIndex = povIndex,
            Direction = direction,
            Invoke = invoke
        });
    }

    private void ForceBaseDxShiftStateIfNeeded()
    {
        if (!DeviceButtonBinding.IsDxShiftCallback(_row.CallbackName))
            return;

        IsUnshifted = true;
        IsOnPress = true;
    }

    private static string NormalizePovInvoke(string? invoke)
    {
        return string.Equals(invoke, PovInvokeShift, StringComparison.OrdinalIgnoreCase)
            ? PovInvokeShift
            : PovInvokeDefault;
    }

    private static string BuildPovDisplayText(int povIndex, int direction, string invoke)
    {
        string text = "POV" + (povIndex + 1) + "." + GetPovDirectionName(direction).ToUpperInvariant();

        if (string.Equals(NormalizePovInvoke(invoke), PovInvokeShift, StringComparison.OrdinalIgnoreCase))
            text += " SHIFT";

        return text;
    }

    private static string GetPovDirectionName(int direction)
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

    public void Dispose()
    {
        _captureHost.Dispose();
    }
}