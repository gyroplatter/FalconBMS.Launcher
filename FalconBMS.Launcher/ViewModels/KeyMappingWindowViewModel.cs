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
    private readonly Action<BindingRow, string, int, string, int> _saveKeyboardBinding;
    private readonly Action _closeWindow;
    private readonly DirectInputManager _di = new();

    private KeyboardSession? _keyboard;
    private DispatcherTimer? _timer;
    private HashSet<DiKey> _previousPressedKeys = new();

    private string _tempKeyScancode;
    private int _tempModifierFlags;
    private string _tempChordScancode;
    private int _tempChordModifierFlags;

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
        Action<BindingRow, string, int, string, int> saveKeyboardBinding,
        Action closeWindow)
    {
        _row = row;
        _profileRows = profileRows.ToList();
        _saveKeyboardBinding = saveKeyboardBinding;
        _closeWindow = closeWindow;

        // Match original launcher behavior: show only description, no callback name.
        TitleText = row.Description;

        _tempKeyScancode = row.KeyScancode;
        _tempModifierFlags = row.KeyModifierFlags;
        _tempChordScancode = row.ChordScancode;
        _tempChordModifierFlags = row.ChordModifierFlags;

        _assignmentText = BuildAssignmentPreview();
        UpdateKeyboardConflict();

        ClearDxCommand = new RelayCommand(() =>
        {
            UpdateKeyboardConflict();
            AssignmentText = BuildAssignmentPreview();
        });

        ClearKeyCommand = new RelayCommand(() =>
        {
            _tempKeyScancode = "0xFFFFFFFF";
            _tempModifierFlags = 0;
            _tempChordScancode = "0";
            _tempChordModifierFlags = 0;

            UpdateKeyboardConflict();
            AssignmentText = BuildAssignmentPreview();
        });

        SaveCommand = new RelayCommand(() =>
        {
            // Phase 2: commit only to the in-memory keyboard model.
            // File output still happens through the normal Launch/Close flush pipeline.
            _saveKeyboardBinding(
                _row,
                _tempKeyScancode,
                _tempModifierFlags,
                _tempChordScancode,
                _tempChordModifierFlags);

            _closeWindow();
        });

        CancelCommand = new RelayCommand(() =>
        {
            _closeWindow();
        });
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

        _previousPressedKeys.Clear();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        PollKeyboard();
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
        UpdateKeyboardConflict();
    }

    private void UpdateKeyboardConflict()
    {
        string selectedAssignment = BuildAssignmentPreview();

        if (string.IsNullOrWhiteSpace(selectedAssignment) ||
            selectedAssignment == "Awaiting input: Press any key")
        {
            ConflictText = "";
            return;
        }

        BindingRow? conflict = _profileRows.FirstOrDefault(row =>
            !ReferenceEquals(row, _row) &&
            row.IsEditable &&
            string.Equals(
                GetAssignmentText(row),
                selectedAssignment,
                StringComparison.OrdinalIgnoreCase));

        ConflictText = conflict is null
            ? ""
            : "Keyboard input currently bound to: " + conflict.Description.Trim() + "\nClick \"Save\" to replace the existing assignment.";
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
        string keyText = KeyAssgn.GetKeyAssignmentStatus(
            _tempKeyScancode,
            _tempModifierFlags,
            _tempChordScancode,
            _tempChordModifierFlags);

        if (string.IsNullOrWhiteSpace(keyText))
            return "Awaiting input: Press any key";

        return keyText;
    }

    public void Dispose()
    {
        StopCapture();
        _di.Dispose();
    }
}