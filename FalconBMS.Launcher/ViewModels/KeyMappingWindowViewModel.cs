using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Vortice.DirectInput;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Popup view model for capturing a new keyboard assignment from live key input.
/// </summary>
public sealed class KeyMappingWindowViewModel : ViewModelBase, IDisposable
{
    private readonly string _baseDir;
    private readonly KeyProfile _selectedProfile;
    private readonly KeyAssgn _selectedRow;
    private readonly IReadOnlyList<KeyAssgn> _allRows;
    private readonly string _activeF16KeyPath;
    private readonly string _activeF15KeyPath;
    private readonly Action _onSaveSucceeded;
    private readonly Action _closeWindow;

    private readonly DirectInputManager _di = new();
    private KeyboardSession? _kb;
    private readonly Dictionary<int, JoystickSession> _joySessions = new();
    private readonly Dictionary<int, bool[]> _prevButtons = new();
    private DispatcherTimer? _timer;
    private bool _skipNextDxPoll;

    private JoyAssgnLite[] _tmpJoys = Array.Empty<JoyAssgnLite>();
    private readonly KeyAssgn _tmpKey;

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

    public System.Windows.Input.ICommand ClearDxCommand { get; } = null!;
    public System.Windows.Input.ICommand ClearKeyCommand { get; } = null!;
    public System.Windows.Input.ICommand SaveCommand { get; } = null!;
    public System.Windows.Input.ICommand CancelCommand { get; } = null!;

    public KeyMappingWindowViewModel(
        string baseDir,
        KeyProfile selectedProfile,
        KeyAssgn selectedRow,
        IReadOnlyList<KeyAssgn> allRows,
        string activeF16KeyPath,
        string activeF15KeyPath,
        Action onSaveSucceeded,
        Action closeWindow)
    {
        _baseDir = baseDir;
        _selectedProfile = selectedProfile;
        _selectedRow = selectedRow;
        _allRows = allRows;
        _activeF16KeyPath = activeF16KeyPath;
        _activeF15KeyPath = activeF15KeyPath;
        _onSaveSucceeded = onSaveSucceeded;
        _closeWindow = closeWindow;

        TitleText = selectedRow.Mapping?.Trim() ?? "";

        _tmpKey = selectedRow.Clone();
        RebuildTempJoysFromLive();

        _assignmentText = BuildAssignmentPreview();

        ClearDxCommand = new RelayCommand(() =>
        {
            RebuildTempJoysFromLive();

            string callback = _selectedRow.GetCallback();
            foreach (var j in _tmpJoys)
                j.ClearCallbackEverywhere(callback);

            ConflictText = "";
            AssignmentText = BuildAssignmentPreview();
        });

        ClearKeyCommand = new RelayCommand(() =>
        {
            _tmpKey.ClearKeyboard(shiftedLayer: !IsUnshifted);
            ConflictText = "";
            AssignmentText = BuildAssignmentPreview();
        });

        SaveCommand = new RelayCommand(() =>
        {
            bool saveSucceeded = false;

            try
            {
                if (_allRows is System.Collections.Generic.IList<KeyAssgn> list)
                {
                    int idx = list.IndexOf(_selectedRow);
                    if (idx >= 0)
                        list[idx] = _tmpKey;

                    ClearDuplicateKeyboardAssignmentInLiveProfile(list, _tmpKey);
                }

                var setupXml = new SetupXmlService();
                JoyAssgnLite[] joysToSave = _tmpJoys;
                setupXml.SaveAllDeviceXmlsFromJoyAssgns(_baseDir, joysToSave);

                if (!string.IsNullOrWhiteSpace(_activeF16KeyPath) && !string.IsNullOrWhiteSpace(_activeF15KeyPath))
                {
                    var keyFileF16 = new KeyFile(_activeF16KeyPath);
                    var keyFileF15 = new KeyFile(_activeF15KeyPath);

                    if (_selectedProfile == KeyProfile.F15ABCD)
                        SyncProfileKeyAssignmentsFromRows(keyFileF15, _allRows);
                    else
                        SyncProfileKeyAssignmentsFromRows(keyFileF16, _allRows);

                    int rollJoyId = KeymappingContext.RollJoyId;
                    int throttleJoyId = KeymappingContext.ThrottleJoyId;

                    new KeyMappingOverrideWriter().SaveKeyMapping(
                        _baseDir,
                        keyFileF16,
                        keyFileF15,
                        joysToSave,
                        rollJoyId,
                        throttleJoyId
                    );

                    saveSucceeded = true;
                }
            }
            catch
            {
            }

            if (saveSucceeded)
                _onSaveSucceeded();

            _closeWindow();
        });

        CancelCommand = new RelayCommand(() =>
        {
            _closeWindow();
        });
    }

    public void StartCapture(IntPtr hwnd)
    {
        RebuildTempJoysFromLive();

        _kb = _di.OpenKeyboard(hwnd);

        OpenJoystickSessions(hwnd);
        _skipNextDxPoll = true;

        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            _timer.Tick += (_, _) => PollInputs();
        }

        _timer.Start();
    }

    private void RebuildTempJoysFromLive()
    {
        _tmpJoys = (KeymappingContext.JoyAssgns ?? Array.Empty<JoyAssgnLite>())
            .Select(j => j.CloneDeep())
            .ToArray();
    }

    public void StopCapture()
    {
        if (_timer is not null)
            _timer.Stop();

        if (_kb is not null)
        {
            try { _kb.Dispose(); } catch { }
            _kb = null;
        }

        foreach (var s in _joySessions.Values)
        {
            try { s.Dispose(); } catch { }
        }

        _joySessions.Clear();
        _prevButtons.Clear();
    }

    private void OpenJoystickSessions(IntPtr hwnd)
    {
        _joySessions.Clear();
        _prevButtons.Clear();

        var sorting = new DeviceSortingReader().Read(_baseDir);
        if (sorting.Count == 0)
            return;

        var byProduct = sorting.ToDictionary(d => d.ProductGuid, d => d.SlotIndex);

        var diDevices = _di.EnumerateDevices();

        foreach (var dev in diDevices)
        {
            if (!byProduct.TryGetValue(dev.ProductGuid, out int slot))
                continue;

            if (slot < 0 || slot >= _tmpJoys.Length)
                continue;

            var session = _di.Open(dev.InstanceGuid, hwnd);
            _joySessions[slot] = session;

            var state = session.ReadState();
            var buttons = state.Buttons ?? Array.Empty<bool>();
            _prevButtons[slot] = buttons.ToArray();
        }
    }

    private void PollInputs()
    {
        PollKeyboard();
        PollDxButtons();
    }

    private void PollKeyboard()
    {
        if (_kb is null)
            return;

        KeyboardState ks;
        try
        {
            ks = _kb.ReadState();
        }
        catch
        {
            return;
        }

        var pressed = new List<Key>();

        foreach (Key k in Enum.GetValues(typeof(Key)))
        {
            if (k == Key.Unknown)
                continue;

            if (ks.IsPressed(k))
                pressed.Add(k);
        }

        if (pressed.Count == 0)
            return;

        bool shift = pressed.Contains(Key.LeftShift) || pressed.Contains(Key.RightShift);
        bool ctrl = pressed.Contains(Key.LeftControl) || pressed.Contains(Key.RightControl);
        bool alt = pressed.Contains(Key.LeftAlt) || pressed.Contains(Key.RightAlt);

        int modFlags = 0;
        if (shift) modFlags |= 1;
        if (ctrl) modFlags |= 2;
        if (alt) modFlags |= 4;

        var caught = pressed.FirstOrDefault(k =>
            k != Key.LeftShift && k != Key.RightShift &&
            k != Key.LeftControl && k != Key.RightControl &&
            k != Key.LeftAlt && k != Key.RightAlt);

        if (caught == Key.Unknown)
            return;

        if (modFlags == 0 && (caught == Key.Q || caught == Key.W || caught == Key.E || caught == Key.R || caught == Key.T || caught == Key.Y))
            return;

        bool shiftedLayer = !IsUnshifted;

        _tmpKey.SetKeyboard(caught, modFlags, shiftedLayer);

        var kbText = _tmpKey.GetKeyAssignmentStatus();
        var conflict = FindKeyboardConflict(kbText);
        ConflictText = conflict ?? "";

        AssignmentText = BuildAssignmentPreview();
    }

    private string? FindKeyboardConflict(string keyAssignmentText)
    {
        if (string.IsNullOrWhiteSpace(keyAssignmentText))
            return null;

        foreach (var row in _allRows)
        {
            if (ReferenceEquals(row, _selectedRow))
                continue;

            if (string.Equals(row.GetKeyAssignmentStatus(), keyAssignmentText, StringComparison.OrdinalIgnoreCase))
                return "Keyboard input currently bound to: " + row.Mapping.Trim() + "\nClick \"Save\" replace the existing assignment.";
        }

        return null;
    }

    private void PollDxButtons()
    {
        if (_skipNextDxPoll)
        {
            foreach (var kvp in _joySessions)
            {
                try
                {
                    var state = kvp.Value.ReadState();
                    _prevButtons[kvp.Key] = (state.Buttons ?? Array.Empty<bool>()).ToArray();
                }
                catch
                {
                }
            }

            _skipNextDxPoll = false;
            return;
        }

        foreach (var kvp in _joySessions)
        {
            int slot = kvp.Key;
            var session = kvp.Value;

            JoystickState state;
            try
            {
                state = session.ReadState();
            }
            catch
            {
                continue;
            }

            var buttons = state.Buttons ?? Array.Empty<bool>();

            if (!_prevButtons.TryGetValue(slot, out var prev))
            {
                _prevButtons[slot] = buttons.ToArray();
                continue;
            }

            int count = Math.Min(prev.Length, buttons.Length);

            for (int i = 0; i < count; i++)
            {
                bool wasDown = prev[i];
                bool isDown = buttons[i];

                if (!wasDown && isDown)
                {
                    HandleDxPressed(slot, buttonIndex0Based: i);
                    break;
                }
            }

            _prevButtons[slot] = buttons.ToArray();
        }
    }

    private void HandleDxPressed(int joySlot, int buttonIndex0Based)
    {
        if (joySlot < 0 || joySlot >= _tmpJoys.Length)
            return;

        int idx;
        if (IsOnPress)
            idx = IsUnshifted ? 0 : 1;
        else
            idx = IsUnshifted ? 2 : 3;

        var joy = _tmpJoys[joySlot];

        var existing = joy.GetDxCallback(buttonIndex0Based, idx);
        if (!string.IsNullOrWhiteSpace(existing) && !string.Equals(existing, "SimDoNothing", StringComparison.OrdinalIgnoreCase))
        {
            var other = _allRows.FirstOrDefault(r => string.Equals(r.GetCallback(), existing, StringComparison.OrdinalIgnoreCase));
            if (other is not null)
                ConflictText = "DX input currently bound to: " + other.Mapping.Trim() + "\nClick \"Save\" replace the existing assignment.";
            else
                ConflictText = "DX input currently bound to: " + existing;
        }
        else
        {
            ConflictText = "";
        }

        joy.SetDxAssignment(buttonIndex0Based, idx, _selectedRow.GetCallback(), invoke: "Default", soundId: _selectedRow.GetSoundID());

        AssignmentText = BuildAssignmentPreview();
    }

    private string BuildAssignmentPreview()
    {
        var parts = new List<string>();

        var keyText = _tmpKey.GetKeyAssignmentStatus();
        if (!string.IsNullOrWhiteSpace(keyText))
            parts.Add(keyText);

        for (int slot = 0; slot < _tmpJoys.Length; slot++)
        {
            var joy = _tmpJoys[slot];
            var dx = joy.KeyMappingPreviewDX(_selectedRow);
            if (!string.IsNullOrWhiteSpace(dx))
            {
                var lines = dx.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                    parts.Add("JOY " + slot + line.Trim());
            }
        }

        if (parts.Count == 0)
            return "Awaiting input: Press any key";

        return string.Join("; ", parts);
    }

    public void Dispose()
    {
        StopCapture();
        _di.Dispose();
    }

    private static void SyncProfileKeyAssignmentsFromRows(KeyFile keyFile, IReadOnlyList<KeyAssgn> rows)
    {
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            KeyAssgn sourceRow = rows[rowIndex];
            string callback = sourceRow.GetCallback();

            for (int keyIndex = 0; keyIndex < keyFile.keyAssign.Length; keyIndex++)
            {
                if (string.Equals(keyFile.keyAssign[keyIndex].GetCallback(), callback, StringComparison.OrdinalIgnoreCase))
                {
                    keyFile.keyAssign[keyIndex] = sourceRow;
                    break;
                }
            }
        }
    }

    private static void ClearDuplicateKeyboardAssignmentInLiveProfile(System.Collections.Generic.IList<KeyAssgn> rows, KeyAssgn selectedRow)
    {
        string selectedAssignment = selectedRow.GetKeyAssignmentStatus();
        if (string.IsNullOrWhiteSpace(selectedAssignment))
            return;

        for (int i = 0; i < rows.Count; i++)
        {
            KeyAssgn row = rows[i];

            if (ReferenceEquals(row, selectedRow))
                continue;

            if (string.Equals(row.GetKeyAssignmentStatus(), selectedAssignment, StringComparison.OrdinalIgnoreCase))
            {
                row.ClearKeyboard(shiftedLayer: false);
                row.ClearKeyboard(shiftedLayer: true);
                return;
            }
        }
    }
}