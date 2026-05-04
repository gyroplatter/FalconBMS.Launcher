using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using Vortice.DirectInput;
using DiKey = Vortice.DirectInput.Key;

namespace FalconBMS.Launcher.ViewModels;

public sealed class KeyMappingWindowViewModel : ViewModelBase, IDisposable
{
    private readonly BindingRow _row;
    private readonly List<BindingRow> _profileRows;
    private readonly List<DeviceBindingProfile> _deviceProfiles;
    private readonly string _aircraftProfileName;
    private readonly Action<BindingRow, string, int, string, int> _saveKeyboardBinding;
    private readonly Action<BindingRow, string?, int?> _saveDeviceButtonBinding;
    private readonly Action _closeWindow;
    private readonly DirectInputManager _di = new();

    private KeyboardSession? _keyboard;
    private readonly Dictionary<string, JoystickSession> _joystickSessionsByDeviceKey = new();
    private readonly Dictionary<string, bool[]> _previousButtonsByDeviceKey = new();
    private DispatcherTimer? _timer;
    private HashSet<DiKey> _previousPressedKeys = new();

    private string _tempKeyScancode;
    private int _tempModifierFlags;
    private string _tempChordScancode;
    private int _tempChordModifierFlags;

    private string? _tempDxDeviceKey;
    private int? _tempDxButtonIndex;

    public string TitleText { get; }

    private string _assignmentText;
    public string AssignmentText
    {
        get => _assignmentText;
        private set => Set(ref _assignmentText, value);
    }

    private string _conflictText = "";
    public string ConflictText
    {
        get => _conflictText;
        private set => Set(ref _conflictText, value);
    }

    private bool _isUnshifted = true;
    public bool IsUnshifted
    {
        get => _isUnshifted;
        set => Set(ref _isUnshifted, value);
    }

    private bool _isOnPress = true;
    public bool IsOnPress
    {
        get => _isOnPress;
        set => Set(ref _isOnPress, value);
    }

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
        Action<BindingRow, string?, int?> saveDeviceButtonBinding,
        Action closeWindow)
    {
        _row = row;
        _profileRows = profileRows.ToList();
        _deviceProfiles = deviceProfiles.ToList();
        _aircraftProfileName = aircraftProfileName;
        _saveKeyboardBinding = saveKeyboardBinding;
        _saveDeviceButtonBinding = saveDeviceButtonBinding;
        _closeWindow = closeWindow;

        TitleText = row.Description;

        _tempKeyScancode = row.KeyScancode;
        _tempModifierFlags = row.KeyModifierFlags;
        _tempChordScancode = row.ChordScancode;
        _tempChordModifierFlags = row.ChordModifierFlags;

        DeviceButtonBinding? existingDx = FindExistingDxBinding(row.CallbackName);
        if (existingDx is not null)
        {
            DeviceBindingProfile? existingDevice = FindDeviceForButtonBinding(existingDx);
            _tempDxDeviceKey = existingDevice?.DurableDeviceKey;
            _tempDxButtonIndex = existingDx.ButtonIndex;
        }

        _assignmentText = BuildAssignmentPreview();
        UpdateConflict();

        ClearDxCommand = new RelayCommand(() =>
        {
            _tempDxDeviceKey = null;
            _tempDxButtonIndex = null;

            UpdateConflict();
            AssignmentText = BuildAssignmentPreview();
        });

        ClearKeyCommand = new RelayCommand(() =>
        {
            _tempKeyScancode = "0xFFFFFFFF";
            _tempModifierFlags = 0;
            _tempChordScancode = "0";
            _tempChordModifierFlags = 0;

            UpdateConflict();
            AssignmentText = BuildAssignmentPreview();
        });

        SaveCommand = new RelayCommand(() =>
        {
            _saveKeyboardBinding(
                _row,
                _tempKeyScancode,
                _tempModifierFlags,
                _tempChordScancode,
                _tempChordModifierFlags);

            _saveDeviceButtonBinding(
                _row,
                _tempDxDeviceKey,
                _tempDxButtonIndex);

            _closeWindow();
        });

        CancelCommand = new RelayCommand(_closeWindow);
    }

    public void StartCapture(IntPtr hwnd)
    {
        StopCapture();

        try
        {
            _keyboard = _di.OpenKeyboard(hwnd);
        }
        catch
        {
            _keyboard = null;
        }

        foreach (DeviceBindingProfile deviceProfile in _deviceProfiles.Where(device => device.ButtonCount > 0))
        {
            try
            {
                _joystickSessionsByDeviceKey[deviceProfile.DurableDeviceKey] =
                    _di.OpenJoystick(deviceProfile.InstanceGuid, hwnd);
            }
            catch
            {
                // Keep keyboard capture working even if one controller cannot be opened.
            }
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };

        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    public void StopCapture()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
        }

        if (_keyboard is not null)
        {
            try { _keyboard.Dispose(); } catch { }
            _keyboard = null;
        }

        foreach (JoystickSession session in _joystickSessionsByDeviceKey.Values)
        {
            try { session.Dispose(); } catch { }
        }

        _joystickSessionsByDeviceKey.Clear();
        _previousButtonsByDeviceKey.Clear();
        _previousPressedKeys.Clear();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        PollKeyboard();
        PollJoystickButtons();
    }

    private void PollKeyboard()
    {
        if (_keyboard is null)
            return;

        KeyboardState state;

        try
        {
            state = _keyboard.ReadState();
        }
        catch
        {
            return;
        }

        var currentPressed = new HashSet<DiKey>();

        foreach (DiKey key in Enum.GetValues(typeof(DiKey)))
        {
            if (key == DiKey.Unknown)
                continue;

            if (state.IsPressed(key))
                currentPressed.Add(key);
        }

        var newlyPressed = currentPressed
            .Where(key => !_previousPressedKeys.Contains(key))
            .ToList();

        _previousPressedKeys = currentPressed;

        if (newlyPressed.Count == 0)
            return;

        bool shift = currentPressed.Contains(DiKey.LeftShift) || currentPressed.Contains(DiKey.RightShift);
        bool ctrl = currentPressed.Contains(DiKey.LeftControl) || currentPressed.Contains(DiKey.RightControl);
        bool alt = currentPressed.Contains(DiKey.LeftAlt) || currentPressed.Contains(DiKey.RightAlt);

        int modifierFlags = 0;
        if (shift) modifierFlags |= 1;
        if (ctrl) modifierFlags |= 2;
        if (alt) modifierFlags |= 4;

        DiKey caught = newlyPressed.FirstOrDefault(key =>
            key != DiKey.LeftShift && key != DiKey.RightShift &&
            key != DiKey.LeftControl && key != DiKey.RightControl &&
            key != DiKey.LeftAlt && key != DiKey.RightAlt);

        if (caught == DiKey.Unknown)
            return;

        _tempKeyScancode = "0x" + ((int)caught).ToString("X");
        _tempModifierFlags = modifierFlags;
        _tempChordScancode = "0";
        _tempChordModifierFlags = 0;

        AssignmentText = BuildAssignmentPreview();
        UpdateConflict();
    }

    private void PollJoystickButtons()
    {
        foreach (KeyValuePair<string, JoystickSession> pair in _joystickSessionsByDeviceKey)
        {
            JoystickState state;

            try
            {
                state = pair.Value.ReadState();
            }
            catch
            {
                continue;
            }

            bool[] buttons = state.Buttons ?? Array.Empty<bool>();

            if (!_previousButtonsByDeviceKey.TryGetValue(pair.Key, out bool[]? previousButtons))
            {
                _previousButtonsByDeviceKey[pair.Key] = (bool[])buttons.Clone();
                continue;
            }

            int buttonLimit = Math.Min(buttons.Length, previousButtons.Length);

            for (int buttonIndex = 0; buttonIndex < buttonLimit; buttonIndex++)
            {
                if (!buttons[buttonIndex] || previousButtons[buttonIndex])
                    continue;

                _tempDxDeviceKey = pair.Key;
                _tempDxButtonIndex = buttonIndex;

                AssignmentText = BuildAssignmentPreview();
                UpdateConflict();

                break;
            }

            _previousButtonsByDeviceKey[pair.Key] = (bool[])buttons.Clone();
        }
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
                keyboardConflict = "Keyboard input currently bound to: " + conflict.Description.Trim();
        }

        string dxConflict = "";

        if (!string.IsNullOrWhiteSpace(_tempDxDeviceKey) && _tempDxButtonIndex.HasValue)
        {
            DeviceBindingProfile? device = _deviceProfiles.FirstOrDefault(d =>
                string.Equals(d.DurableDeviceKey, _tempDxDeviceKey, StringComparison.OrdinalIgnoreCase));

            DeviceAircraftBindingProfile? aircraft = device?.AircraftProfiles.FirstOrDefault(profile =>
                string.Equals(profile.AircraftProfile, _aircraftProfileName, StringComparison.OrdinalIgnoreCase));

            DeviceButtonBinding? conflict = aircraft?.ButtonBindings.FirstOrDefault(binding =>
                binding.ButtonIndex == _tempDxButtonIndex.Value &&
                !string.Equals(binding.CallbackName, _row.CallbackName, StringComparison.OrdinalIgnoreCase));

            if (conflict is not null)
            {
                BindingRow? conflictRow = _profileRows.FirstOrDefault(row =>
                    string.Equals(row.CallbackName, conflict.CallbackName, StringComparison.OrdinalIgnoreCase));

                dxConflict = "DX input currently bound to: " + (conflictRow?.Description.Trim() ?? conflict.CallbackName);
            }
        }

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(keyboardConflict))
            parts.Add(keyboardConflict);

        if (!string.IsNullOrWhiteSpace(dxConflict))
            parts.Add(dxConflict);

        ConflictText = parts.Count == 0
            ? ""
            : string.Join("\n", parts) + "\nClick \"Save\" to replace the existing assignment.";
    }

    private static string GetAssignmentText(BindingRow row)
    {
        return KeyAssgn.GetKeyAssignmentStatus(
            row.KeyScancode,
            row.KeyModifierFlags,
            row.ChordScancode,
            row.ChordModifierFlags);
    }

    private string BuildAssignmentPreview()
    {
        var parts = new List<string>();

        string keyText = BuildKeyboardAssignmentText();
        if (!string.IsNullOrWhiteSpace(keyText))
            parts.Add(keyText);

        if (!string.IsNullOrWhiteSpace(_tempDxDeviceKey) && _tempDxButtonIndex.HasValue)
        {
            DeviceBindingProfile? device = _deviceProfiles.FirstOrDefault(d =>
                string.Equals(d.DurableDeviceKey, _tempDxDeviceKey, StringComparison.OrdinalIgnoreCase));

            string deviceName = device?.ProductName
                ?? device?.InstanceName
                ?? _tempDxDeviceKey
                ?? "Unknown device";

            parts.Add(deviceName + " DX" + (_tempDxButtonIndex.Value + 1));
        }

        return parts.Count == 0
            ? "Awaiting input: Press any key or DX button"
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

    private DeviceButtonBinding? FindExistingDxBinding(string callbackName)
    {
        foreach (DeviceBindingProfile device in _deviceProfiles)
        {
            DeviceAircraftBindingProfile? aircraft = device.AircraftProfiles.FirstOrDefault(profile =>
                string.Equals(profile.AircraftProfile, _aircraftProfileName, StringComparison.OrdinalIgnoreCase));

            DeviceButtonBinding? binding = aircraft?.ButtonBindings.FirstOrDefault(button =>
                string.Equals(button.CallbackName, callbackName, StringComparison.OrdinalIgnoreCase));

            if (binding is not null)
                return binding;
        }

        return null;
    }

    private DeviceBindingProfile? FindDeviceForButtonBinding(DeviceButtonBinding target)
    {
        return _deviceProfiles.FirstOrDefault(device =>
        {
            DeviceAircraftBindingProfile? aircraft = device.AircraftProfiles.FirstOrDefault(profile =>
                string.Equals(profile.AircraftProfile, _aircraftProfileName, StringComparison.OrdinalIgnoreCase));

            return aircraft?.ButtonBindings.Contains(target) == true;
        });
    }

    public void Dispose()
    {
        StopCapture();
        _di.Dispose();
    }
}