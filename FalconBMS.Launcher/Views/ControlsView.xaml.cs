using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DiKey = Vortice.DirectInput.Key;

namespace FalconBMS.Launcher.Views;

public partial class ControlsView : UserControl
{
    private readonly DirectInputManager _di = new();
    private KeyboardSession? _keyboard;
    private HashSet<DiKey> _previousPressedKeys = new();
    private DispatcherTimer? _timer;

    public ControlsView()
    {
        InitializeComponent();

        Loaded += ControlsView_Loaded;
        Unloaded += ControlsView_Unloaded;
    }

    private void ControlsView_Loaded(object sender, RoutedEventArgs e)
    {
        StartKeyboardSearchCapture();
    }

    private void ControlsView_Unloaded(object sender, RoutedEventArgs e)
    {
        StopKeyboardSearchCapture();
    }

    private void ControlsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ControlsViewModel viewModel)
            return;

        if (viewModel.SelectedRow?.SourceRow is null)
            return;

        if (!viewModel.SelectedRow.IsEditable)
            return;

        StopKeyboardSearchCapture();

        var window = new KeyMappingWindow
        {
            Owner = Window.GetWindow(this)
        };

        window.DataContext = new KeyMappingWindowViewModel(
            viewModel.SelectedRow.SourceRow,
            viewModel.SelectedProfileRows,
            () => window.Close());

        window.ShowDialog();

        StartKeyboardSearchCapture();
    }

    private void StartKeyboardSearchCapture()
    {
        StopKeyboardSearchCapture();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };

        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void StopKeyboardSearchCapture()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
        }

        if (_keyboard is not null)
        {
            _keyboard.Dispose();
            _keyboard = null;
        }

        _previousPressedKeys.Clear();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        PollKeyboardSearch();
    }

    private void PollKeyboardSearch()
    {
        if (IsFilterControlFocused())
            return;

        EnsureKeyboardOpened();

        if (_keyboard is null)
            return;

        Vortice.DirectInput.KeyboardState state;

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

        string assignmentStatus = KeyAssgn.GetKeyAssignmentStatus(
            "0x" + ((int)caught).ToString("X"),
            modifierFlags,
            "0",
            0);

        if (string.IsNullOrWhiteSpace(assignmentStatus))
            return;

        if (DataContext is not ControlsViewModel viewModel)
            return;

        if (!viewModel.SelectFirstVisibleKeyMatch(assignmentStatus))
            return;

        if (viewModel.SelectedRow is null)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            ControlsGrid.UpdateLayout();
            ControlsGrid.SelectedItem = viewModel.SelectedRow;
            ControlsGrid.ScrollIntoView(viewModel.SelectedRow);
        }, DispatcherPriority.Background);
    }

    private void EnsureKeyboardOpened()
    {
        if (_keyboard is not null)
            return;

        Window? window = Window.GetWindow(this);
        if (window is null)
            return;

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        try
        {
            _keyboard = _di.OpenKeyboard(hwnd);
        }
        catch
        {
            _keyboard = null;
        }
    }

    private bool IsFilterControlFocused()
    {
        return FocusManager.GetFocusedElement(this) is TextBox or ComboBox;
    }
}