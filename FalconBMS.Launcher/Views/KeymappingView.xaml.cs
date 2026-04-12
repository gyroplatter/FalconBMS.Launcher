using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using FalconBMS.Launcher.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using DiKey = Vortice.DirectInput.Key;

namespace FalconBMS.Launcher.Views;

public partial class KeymappingView : UserControl
{
    private readonly DeviceSortingReader _sorting = new();
    private readonly DirectInputManager _di = new();
    private readonly AxisAssignmentWorkflowService _axisAssignmentWorkflow = new();
    private readonly KeymappingDeviceColumnBuilderService _deviceColumnBuilder = new();

    private readonly Dictionary<DataGridColumn, int> _columnSlotMap = new();

    private KeyboardSession? _kb;
    private readonly Dictionary<int, JoystickSession> _joySessions = new();
    private readonly Dictionary<int, bool[]> _prevButtons = new();
    private HashSet<DiKey> _prevPressedKeys = new();
    private DispatcherTimer? _timer;
    private bool _isShiftButtonPressed;
    private bool _skipNextDxPoll;

    public KeymappingView()
    {
        InitializeComponent();

        Loaded += KeymappingView_Loaded;
        Unloaded += KeymappingView_Unloaded;
    }

    private void KeymappingView_Loaded(object sender, RoutedEventArgs e)
    {
        RegenerateDeviceColumns();
        StartCapture();
    }

    private void KeymappingView_Unloaded(object sender, RoutedEventArgs e)
    {
        StopCapture();
    }

    public void RefreshAfterDeviceHotplug()
    {
        string actionId = DebugDiagnosticsService.CreateActionId("KEYHOT");
        DebugDiagnosticsService.Info($"REFRESH REQUEST | ActionId={actionId} | Source=KeymappingView.RefreshAfterDeviceHotplug | Scope=RegenerateColumns+RestartCapture");

        RegenerateDeviceColumns();
        StartCapture();
    }

    private void ImportKeyFile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KeymappingViewModel kvm)
            return;

        var mw = FindAncestorMainWindow();
        if (mw?.DataContext is not MainWindowViewModel mwvm)
            return;

        var install = mwvm.Main.SelectedInstall;
        if (install is null)
            return;

        MessageBoxResult mbr = MessageBox.Show(
            mw,
            "WARNING -- selecting a new key file will erase and replace all keyboard bindings, in the currently selected profile.\r\n\r\nProceed with caution!",
            "Import Key File - WARNING",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (mbr != MessageBoxResult.OK)
            return;

        string configDir = Path.Combine(install.BaseDir, "User", "Config");

        var ofd = new OpenFileDialog
        {
            InitialDirectory = configDir,
            Filter = "Key files (*.key)|*.key|All files (*.*)|*.*"
        };

        bool? ans = ofd.ShowDialog(mw);
        if (ans != true)
            return;

        string newKeyfilePath = ofd.FileName;

        if (!File.Exists(newKeyfilePath))
        {
            MessageBox.Show(mw, "File not found: " + newKeyfilePath, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!KeyFile.ValidateKeyfileLines(newKeyfilePath))
        {
            MessageBox.Show(
                mw,
                "Key file contains one or more incorrectly formed lines.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        kvm.ImportKeyFile(newKeyfilePath);
        RegenerateDeviceColumns();
        StartCapture();
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KeymappingViewModel kvm)
            return;

        kvm.SearchText = "";
        kvm.SelectAllCategory();
        SearchTextBox.Focus();
    }

    private void KeyMappingGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not KeymappingViewModel kvm)
            return;

        if (KeyMappingGrid.SelectedItem is not KeymappingGridRowViewModel selected)
            return;

        if (selected.IsAxisRow && selected.AxisRow is not null)
        {
            ShowAxisAssignDialog(kvm, selected);
            return;
        }

        if (!selected.IsKeyRow || selected.KeyRow is null)
            return;

        var keyRow = selected.KeyRow;

        if (!string.Equals(keyRow.Visibility, "White", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(keyRow.GetCallback(), "SimDoNothing", StringComparison.OrdinalIgnoreCase))
            return;

        int joySlot = KeymappingContext.RollJoyId;

        var col = KeyMappingGrid.CurrentCell.Column;
        if (col is not null && _columnSlotMap.TryGetValue(col, out int slotFromColumn))
            joySlot = slotFromColumn;

        var mw = FindAncestorMainWindow();
        if (mw?.DataContext is not MainWindowViewModel mwvm)
            return;

        var install = mwvm.Main.SelectedInstall;
        if (install is null)
            return;

        var joys = KeymappingContext.JoyAssgns;
        if (joys is null || joys.Length == 0)
            return;

        joySlot = Math.Clamp(joySlot, 0, joys.Length - 1);

        string f16ActiveKeyPath = kvm.GetActiveKeyPath(KeyProfile.F16);
        string f15ActiveKeyPath = kvm.GetActiveKeyPath(KeyProfile.F15ABCD);

        var win = new KeyMappingWindow
        {
            Owner = mw
        };

        win.DataContext = new KeyMappingWindowViewModel(
            baseDir: install.BaseDir,
            selectedProfile: kvm.SelectedProfile,
            selectedRow: keyRow,
            allRows: kvm.KeyRows,
            activeF16KeyPath: f16ActiveKeyPath,
            activeF15KeyPath: f15ActiveKeyPath,
            onSaveSucceeded: () => kvm.ClearImportedOverride(kvm.SelectedProfile),
            closeWindow: () => win.Close());

        win.ShowDialog();

        string? selectedCategoryKeyBeforeRefresh = kvm.GetSelectedCategoryKey();
        string searchTextBeforeRefresh = kvm.SearchText;

        mwvm.RefreshDeviceState();

        if (kvm.ContainsCategoryKey(selectedCategoryKeyBeforeRefresh))
            kvm.SelectCategoryByKey(selectedCategoryKeyBeforeRefresh);

        kvm.SearchText = searchTextBeforeRefresh;

        RegenerateDeviceColumns();
        StartCapture();
        DebugDiagnosticsService.Info("[KeymappingView] Post-dialog refresh complete. Capture restarted.");
    }

    private void ShowAxisAssignDialog(KeymappingViewModel kvm, KeymappingGridRowViewModel selected)
    {
        if (selected.AxisRow is null)
            return;

        var mw = FindAncestorMainWindow();
        if (mw?.DataContext is not MainWindowViewModel mwvm)
            return;

        var install = mwvm.Main.SelectedInstall;
        if (install is null)
            return;

        bool changed = _axisAssignmentWorkflow.ShowDialogAndApply(
            owner: mw,
            baseDir: install.BaseDir,
            function: selected.AxisRow.Function);

        if (!changed)
            return;

        string? selectedCategoryKeyBeforeRefresh = kvm.GetSelectedCategoryKey();
        string searchTextBeforeRefresh = kvm.SearchText;

        mwvm.RefreshDeviceState();

        if (kvm.ContainsCategoryKey(selectedCategoryKeyBeforeRefresh))
            kvm.SelectCategoryByKey(selectedCategoryKeyBeforeRefresh);

        kvm.SearchText = searchTextBeforeRefresh;

        RegenerateDeviceColumns();
        StartCapture();
    }

    private void StartCapture()
    {
        string actionId = DebugDiagnosticsService.CreateActionId("KEYCAP");
        DebugDiagnosticsService.Info($"[KeymappingView] StartCapture CALLED | ActionId={actionId}");

        if (!IsLoaded)
            return;

        var mw = FindAncestorMainWindow();
        if (mw is null)
            return;

        IntPtr hwnd = new WindowInteropHelper(mw).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        StopCapture();
        DebugDiagnosticsService.Info("[KeymappingView] StartCapture | Action=StopPreviousCaptureFirst");

        try
        {
            _kb = _di.OpenKeyboard(hwnd);
        }
        catch
        {
            _kb = null;
        }

        OpenJoystickSessions(hwnd);
        _skipNextDxPoll = true;

        _timer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };

        _timer.Tick -= Timer_Tick;
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void StopCapture()
    {
        DebugDiagnosticsService.Info($"[KeymappingView] StopCapture CALLED | ExistingJoystickSessions={_joySessions.Count}");

        if (_timer is not null)
            _timer.Stop();

        if (_kb is not null)
        {
            try { _kb.Dispose(); } catch { }
            _kb = null;
        }

        foreach (var session in _joySessions.Values)
        {
            try { session.Dispose(); } catch { }
        }

        _joySessions.Clear();
        _prevButtons.Clear();
        _prevPressedKeys.Clear();
        _isShiftButtonPressed = false;

        DebugDiagnosticsService.Info("[KeymappingView] StopCapture COMPLETE | ExistingJoystickSessions=0");
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible)
            return;

        var window = FindAncestorMainWindow();
        if (window is null || !window.IsActive)
            return;

        PollKeyboard();
        PollDxButtons();
        PollAxisBars();
    }

    private void OpenJoystickSessions(IntPtr hwnd)
    {
        DebugDiagnosticsService.Info("[KeymappingView] OpenJoystickSessions CALLED");

        var mw = FindAncestorMainWindow();
        if (mw?.DataContext is not MainWindowViewModel mwvm)
            return;

        var install = mwvm.Main.SelectedInstall;
        if (install is null)
            return;

        var sorting = _sorting.Read(install.BaseDir);
        if (sorting.Count == 0)
        {
            DebugDiagnosticsService.Info("[KeymappingView] OpenJoystickSessions skipped because DeviceSorting is empty.");
            return;
        }

        var byProduct = sorting.ToDictionary(d => d.ProductGuid, d => d.SlotIndex);

        var diDevices = _di.EnumerateDevices();
        DebugDiagnosticsService.Info($"ENUM DEVICES | Source=KeymappingView.OpenJoystickSessions | Reason=CaptureStart | Count={diDevices.Count}");

        foreach (var dev in diDevices)
        {
            if (!byProduct.TryGetValue(dev.ProductGuid, out int slot))
                continue;

            var session = _di.Open(dev.InstanceGuid, hwnd);
            _joySessions[slot] = session;

            var state = session.ReadState();
            _prevButtons[slot] = (state.Buttons ?? Array.Empty<bool>()).ToArray();
        }
    }

    private void PollKeyboard()
    {
        if (_kb is null)
            return;

        if (SearchTextBox.IsFocused || SearchTextBox.IsKeyboardFocused ||
            CategoryComboBox.IsFocused || CategoryComboBox.IsKeyboardFocused ||
            CategoryComboBox.IsDropDownOpen)
        {
            return;
        }

        Vortice.DirectInput.KeyboardState state;
        try
        {
            state = _kb.ReadState();
        }
        catch
        {
            return;
        }

        var currentPressed = new HashSet<DiKey>();
        foreach (DiKey key in Enum.GetValues<DiKey>())
        {
            if (key == DiKey.Unknown)
                continue;

            if (state.IsPressed(key))
                currentPressed.Add(key);
        }

        var newlyPressed = currentPressed.Where(x => !_prevPressedKeys.Contains(x)).ToList();
        _prevPressedKeys = currentPressed;

        if (newlyPressed.Count == 0)
            return;

        bool shift = currentPressed.Contains(DiKey.LeftShift) || currentPressed.Contains(DiKey.RightShift);
        bool ctrl = currentPressed.Contains(DiKey.LeftControl) || currentPressed.Contains(DiKey.RightControl);
        bool alt = currentPressed.Contains(DiKey.LeftAlt) || currentPressed.Contains(DiKey.RightAlt);

        int modFlags = 0;
        if (shift) modFlags |= 1;
        if (ctrl) modFlags |= 2;
        if (alt) modFlags |= 4;

        var caught = newlyPressed.FirstOrDefault(x =>
            x != DiKey.LeftShift && x != DiKey.RightShift &&
            x != DiKey.LeftControl && x != DiKey.RightControl &&
            x != DiKey.LeftAlt && x != DiKey.RightAlt);

        if (caught == DiKey.Unknown)
            return;

        if (modFlags == 0 && (caught == DiKey.Q || caught == DiKey.W || caught == DiKey.E || caught == DiKey.R || caught == DiKey.T || caught == DiKey.Y))
            return;

        if (DataContext is not KeymappingViewModel kvm)
            return;

        var temp = KeyFile.ParseKeyfileLine("SimDoNothing -1 0 0xFFFFFFFF 0 0 0 -1 \"nothing\"");
        if (temp is null)
            return;

        temp.SetKeyboard(caught, modFlags, shiftedLayer: false);

        string assignmentStatus = temp.GetKeyAssignmentStatus();
        if (string.IsNullOrWhiteSpace(assignmentStatus))
            return;

        string label = "INPUT " + assignmentStatus;

        if (kvm.TryFindKeyRowByKeyboardAssignment(assignmentStatus, out KeymappingGridRowViewModel? row) && row is not null)
        {
            label += " / " + row.Mapping.Trim();
            SelectAndRevealRow(kvm, row);
        }

        kvm.AssignmentText = label;
    }

    private void PollDxButtons()
    {
        var joys = KeymappingContext.JoyAssgns;
        if (joys is null || joys.Length == 0)
            return;

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

            Vortice.DirectInput.JoystickState state;
            try
            {
                state = session.ReadState();
            }
            catch
            {
                continue;
            }

            var buttons = state.Buttons ?? Array.Empty<bool>();

            if (!_prevButtons.TryGetValue(slot, out bool[]? prev))
            {
                _prevButtons[slot] = buttons.ToArray();
                continue;
            }

            int count = Math.Min(prev.Length, buttons.Length);

            for (int i = 0; i < count; i++)
            {
                bool wasDown = prev[i];
                bool isDown = buttons[i];

                if (wasDown == isDown)
                    continue;

                HandleDxChanged(slot, i, isDown);
            }

            _prevButtons[slot] = buttons.ToArray();
        }
    }

    private void PollAxisBars()
    {
        if (DataContext is not KeymappingViewModel kvm)
            return;

        var stateBySlot = new Dictionary<int, int[]>();

        foreach (var row in kvm.GetAllAxisRows())
        {
            if (!row.IsAxisRow || row.AxisRow is null || row.AssignedDeviceSlot is null)
                continue;

            int slot = row.AssignedDeviceSlot.Value;

            if (!_joySessions.TryGetValue(slot, out var session))
                continue;

            if (!stateBySlot.TryGetValue(slot, out var axisVector))
            {
                try
                {
                    var state = session.ReadState();
                    axisVector = DirectInputManager.ReadAxisVector(state);
                    stateBySlot[slot] = axisVector;
                }
                catch
                {
                    continue;
                }
            }

            var live = row.AxisRow.GetLiveSource();
            if (live is null)
                continue;

            if ((uint)live.PhysicalAxisIndex >= (uint)axisVector.Length)
                continue;

            row.AxisRow.UpdateFromRawAxisValue(axisVector[live.PhysicalAxisIndex]);
        }
    }

    private void HandleDxChanged(int joySlot, int buttonIndex0Based, bool isDown)
    {
        if (DataContext is not KeymappingViewModel kvm)
            return;

        var joys = KeymappingContext.JoyAssgns;
        if (joys is null || joySlot < 0 || joySlot >= joys.Length)
            return;

        var joy = joys[joySlot];
        string deviceName = joy.ProductName;

        if (isDown)
        {
            int assignIndex = _isShiftButtonPressed ? 1 : 0;
            string? target = joy.GetDxCallback(buttonIndex0Based, assignIndex);

            if (string.Equals(target, "SimHotasPinkyShift", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(target, "SimHotasShift", StringComparison.OrdinalIgnoreCase))
            {
                _isShiftButtonPressed = true;
            }

            string label = $"DX{buttonIndex0Based + 1} ({deviceName})";

            if (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, "SimDoNothing", StringComparison.OrdinalIgnoreCase))
            {
                if (kvm.TryFindKeyRowByCallback(target, out KeymappingGridRowViewModel? row) && row is not null)
                {
                    label += " / " + row.Mapping.Trim();
                    SelectAndRevealRow(kvm, row, joySlot);
                }
                else
                {
                    label += " / " + target;
                }
            }

            kvm.AssignmentText = label;
            return;
        }

        int releaseIndex = _isShiftButtonPressed ? 3 : 2;
        string? releaseTarget = joy.GetDxCallback(buttonIndex0Based, releaseIndex);

        if (!string.IsNullOrWhiteSpace(releaseTarget) && !string.Equals(releaseTarget, "SimDoNothing", StringComparison.OrdinalIgnoreCase))
        {
            string label = $"DX{buttonIndex0Based + 1}.RELEASE ({deviceName})";

            if (kvm.TryFindKeyRowByCallback(releaseTarget, out KeymappingGridRowViewModel? releaseRow) && releaseRow is not null)
            {
                label += " / " + releaseRow.Mapping.Trim();
                SelectAndRevealRow(kvm, releaseRow, joySlot);
            }
            else
            {
                label += " / " + releaseTarget;
            }

            kvm.AssignmentText = label;
        }

        string? pressTarget = joy.GetDxCallback(buttonIndex0Based, 0);
        if (string.Equals(pressTarget, "SimHotasPinkyShift", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pressTarget, "SimHotasShift", StringComparison.OrdinalIgnoreCase))
        {
            _isShiftButtonPressed = false;
        }
    }

    private void SelectAndRevealRow(KeymappingViewModel kvm, KeymappingGridRowViewModel row, int? joySlot = null)
    {
        if (row.IsKeyRow && row.KeyRow is not null &&
            !string.Equals(row.KeyRow.Visibility, "White", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!kvm.IsRowVisible(row))
        {
            kvm.SelectAllCategory();
            kvm.SearchText = "";
        }

        kvm.SelectedRow = row;

        Dispatcher.BeginInvoke(() =>
        {
            KeyMappingGrid.UpdateLayout();
            KeyMappingGrid.SelectedItem = row;

            DataGridColumn? targetColumn = null;

            if (joySlot.HasValue)
            {
                targetColumn = _columnSlotMap
                    .FirstOrDefault(x => x.Value == joySlot.Value)
                    .Key;
            }

            if (targetColumn is not null)
                KeyMappingGrid.ScrollIntoView(row, targetColumn);
            else
                KeyMappingGrid.ScrollIntoView(row);
        }, DispatcherPriority.Background);
    }

    private void RegenerateDeviceColumns()
    {
        KeyMappingGrid.Columns.Clear();
        _columnSlotMap.Clear();

        var mappingColumn = new DataGridTextColumn
        {
            Header = "Mapping",
            Binding = new Binding(nameof(KeymappingGridRowViewModel.Mapping)),
            MinWidth = 300,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            IsReadOnly = true
        };

        var keyColumn = new DataGridTextColumn
        {
            Header = "Key",
            Binding = new Binding(nameof(KeymappingGridRowViewModel.Key)),
            MinWidth = 160,
            Width = new DataGridLength(180),
            IsReadOnly = true
        };

        KeyMappingGrid.Columns.Add(mappingColumn);
        KeyMappingGrid.Columns.Add(keyColumn);

        var mw = FindAncestorMainWindow();
        if (mw?.DataContext is not MainWindowViewModel mwvm)
            return;

        var install = mwvm.Main.SelectedInstall;
        if (install is null)
            return;

        var devices = _sorting.Read(install.BaseDir)
            .OrderBy(x => x.SlotIndex)
            .ToList();

        foreach (var device in devices)
        {
            var column = CreateDeviceColumn(device.SlotIndex, device.Name);
            KeyMappingGrid.Columns.Add(column);
            _columnSlotMap[column] = device.SlotIndex;
        }
    }

    private DataGridTemplateColumn CreateDeviceColumn(int slotIndex, string header)
    {
        return _deviceColumnBuilder.CreateDeviceColumn(slotIndex, header);
    }

    private Window? FindAncestorMainWindow()
        => Window.GetWindow(this);
}