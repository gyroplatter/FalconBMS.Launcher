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
    private readonly Action<BindingRow, string?, int?, int?> _saveDeviceButtonBinding;
    private readonly Action _closeWindow;
    private readonly DirectInputManager _di = new();

    private KeyboardSession? _keyboard;
    private readonly Dictionary<string, JoystickSession> _joystickSessionsByDeviceKey = new();
    private readonly Dictionary<string, bool[]> _previousButtonsByDeviceKey = new();
    private DispatcherTimer? _timer;
    private HashSet<DiKey> _previousPressedKeys = new();

    // A DirectInput session is opened when the popup opens, so use the first
    // few polling ticks to learn held/latching switch positions before capturing input.
    // Increase the DxNeutralWarmupPolls to increase this delay, but that also 
    // can make the window miss the first real DX press.
    private const int DxNeutralWarmupPolls = 6;
    private int _dxNeutralWarmupPollsRemaining;

    private string _tempKeyScancode;
    private int _tempModifierFlags;
    private string _tempChordScancode;
    private int _tempChordModifierFlags;

    private readonly List<PendingDxButton> _pendingDxButtons = new();

    private sealed class PendingDxButton
    {
        public string DeviceKey { get; set; } = "";
        public int ButtonIndex { get; set; }
        public int AssignmentIndex { get; set; }
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

        LoadExistingDxBindings(row.CallbackName);
        ForceBaseDxShiftStateIfNeeded();

        _keyboardAssignmentText = BuildKeyboardAssignmentPreview();
        _dxAssignmentText = BuildDxAssignmentPreview();
        UpdateConflict();

        ClearDxCommand = new RelayCommand(() =>
        {
            _pendingDxButtons.Clear();

            // After Clear DX, ignore anything currently held so latching switches
            // do not immediately add themselves back.
            StartDxNeutralWarmup();

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
            _saveDeviceButtonBinding(_row, null, null, null);

            foreach (PendingDxButton pendingDxButton in _pendingDxButtons)
            {
                _saveDeviceButtonBinding(
                    _row,
                    pendingDxButton.DeviceKey,
                    pendingDxButton.ButtonIndex,
                    pendingDxButton.AssignmentIndex);
            }

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

        foreach (DeviceBindingProfile deviceProfile in _deviceProfiles.Where(device => device.IsConnected && device.ButtonCount > 0))
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

        StartDxNeutralWarmup();

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

        UpdateAssignmentPreviewTexts();
        UpdateConflict();
    }

    private void StartDxNeutralWarmup()
    {
        _previousButtonsByDeviceKey.Clear();
        _dxNeutralWarmupPollsRemaining = DxNeutralWarmupPolls;
    }

    private bool IsDxNeutralWarmupActive()
    {
        if (_dxNeutralWarmupPollsRemaining <= 0)
            return false;

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

            // Keep replacing the baseline during warmup. The final warmup poll becomes
            // the neutral state used when real DX capture begins.
            _previousButtonsByDeviceKey[pair.Key] = (bool[])buttons.Clone();
        }

        _dxNeutralWarmupPollsRemaining--;
        return true;
    }

    private void PollJoystickButtons()
    {
        if (IsDxNeutralWarmupActive())
            return;

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

                AddPendingDxButton(pair.Key, buttonIndex);

                UpdateAssignmentPreviewTexts();
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

        KeyboardConflictText = string.IsNullOrWhiteSpace(keyboardConflict)
            ? ""
            : keyboardConflict + "\nClick \"Save\" to replace the existing assignment.";

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
                deviceName + " " + DeviceButtonBinding.BuildDisplayText(pendingDxButton.ButtonIndex, pendingDxButton.AssignmentIndex) +
                " currently bound to: " + (conflictRow?.Description.Trim() ?? conflict.CallbackName));
        }

        List<string> distinctDxConflicts = dxConflicts
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DxConflictText = distinctDxConflicts.Count == 0
            ? ""
            : string.Join("\n", distinctDxConflicts) + "\nClick \"Save\" to replace the existing assignment.";
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
            ? "Awaiting input: Press any key"
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

        return parts.Count == 0
            ? "Awaiting input: Press any DX button"
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
        var existingBindings = new List<DeviceButtonBinding>();

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

                existingBindings.Add(binding);
                AddPendingDxButton(device.DurableDeviceKey, binding.ButtonIndex, assignmentIndex);
            }
        }

        if (existingBindings.Count == 0)
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
        DeviceButtonBinding firstBinding = existingBindings[0];

        IsUnshifted = DeviceButtonBinding.GetShiftState(firstBinding.AssignmentIndex) == DeviceButtonBinding.ShiftStateUnshifted;
        IsOnPress = DeviceButtonBinding.GetTrigger(firstBinding.AssignmentIndex) == DeviceButtonBinding.TriggerPress;
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

    private void ForceBaseDxShiftStateIfNeeded()
    {
        if (!DeviceButtonBinding.IsDxShiftCallback(_row.CallbackName))
            return;

        IsUnshifted = true;
        IsOnPress = true;
    }

    public void Dispose()
    {
        StopCapture();
        _di.Dispose();
    }
}