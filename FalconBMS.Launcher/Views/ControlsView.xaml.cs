using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using static FalconBMS.Launcher.Input.KeyboardSession;
using DiKey = Vortice.DirectInput.Key;

namespace FalconBMS.Launcher.Views;

public partial class ControlsView : UserControl
{
    private readonly DirectInputManager _di = new();
    private KeyboardSession? _keyboard;
    private readonly Dictionary<string, JoystickSession> _joystickSessionsByDeviceKey = new();
    private HashSet<DiKey> _previousPressedKeys = new();
    private DispatcherTimer? _timer;
    private ControlsViewModel? _subscribedViewModel;

    public ControlsView()
    {
        InitializeComponent();

        Loaded += ControlsView_Loaded;
        Unloaded += ControlsView_Unloaded;
        DataContextChanged += ControlsView_DataContextChanged;
    }

    private void ControlsView_Loaded(object sender, RoutedEventArgs e)
    {
        SubscribeToViewModel(DataContext as ControlsViewModel);
        RebuildDeviceColumns();
        StartKeyboardSearchCapture();
    }

    private void ControlsView_Unloaded(object sender, RoutedEventArgs e)
    {
        StopKeyboardSearchCapture();
        SubscribeToViewModel(null);
    }

    private void ControlsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SubscribeToViewModel(e.NewValue as ControlsViewModel);
        RebuildDeviceColumns();
    }

    private void SubscribeToViewModel(ControlsViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
            return;

        if (_subscribedViewModel is not null)
            _subscribedViewModel.DeviceColumns.CollectionChanged -= DeviceColumns_CollectionChanged;

        _subscribedViewModel = viewModel;

        if (_subscribedViewModel is not null)
            _subscribedViewModel.DeviceColumns.CollectionChanged += DeviceColumns_CollectionChanged;
    }

    private void DeviceColumns_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildDeviceColumns();
    }

    private void RebuildDeviceColumns()
    {
        const int fixedColumnCount = 2;

        while (ControlsGrid.Columns.Count > fixedColumnCount)
            ControlsGrid.Columns.RemoveAt(fixedColumnCount);

        if (DataContext is not ControlsViewModel viewModel)
            return;

        foreach (DeviceBindingProfile deviceProfile in viewModel.DeviceColumns)
        {
            var template = new DataTemplate(typeof(ControlGridDeviceCellViewModel));

            var gridFactory = new FrameworkElementFactory(typeof(Grid));

            var textFactory = new FrameworkElementFactory(typeof(TextBlock));
            textFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(ControlGridDeviceCellViewModel.DisplayText)));
            textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            textFactory.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 4, 0));

            var progressFactory = new FrameworkElementFactory(typeof(ProgressBar));
            progressFactory.SetValue(ProgressBar.MinimumProperty, 0.0);
            progressFactory.SetValue(ProgressBar.MaximumProperty, 1.0);
            progressFactory.SetValue(ProgressBar.HeightProperty, 14.0);
            progressFactory.SetValue(ProgressBar.MarginProperty, new Thickness(4, 1, 4, 1));
            progressFactory.SetBinding(ProgressBar.ValueProperty, new Binding(nameof(ControlGridDeviceCellViewModel.AxisBarValue)));

            gridFactory.AppendChild(progressFactory);
            gridFactory.AppendChild(textFactory);
            template.VisualTree = gridFactory;

            ControlsGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = GetDeviceColumnHeader(deviceProfile),
                CellTemplate = CreateDeviceCellTemplate(deviceProfile.DurableDeviceKey),
                Width = new DataGridLength(140),
                MinWidth = 140,
                IsReadOnly = true
            });
        }
    }

    private static string GetDeviceColumnHeader(DeviceBindingProfile deviceProfile)
    {
        if (!string.IsNullOrWhiteSpace(deviceProfile.ProductName))
            return deviceProfile.ProductName;

        if (!string.IsNullOrWhiteSpace(deviceProfile.InstanceName))
            return deviceProfile.InstanceName;

        return deviceProfile.DurableDeviceKey;
    }

    private static DataTemplate CreateDeviceCellTemplate(string durableDeviceKey)
    {
        var template = new DataTemplate();

        var gridFactory = new FrameworkElementFactory(typeof(Grid));
        gridFactory.SetBinding(
            FrameworkElement.DataContextProperty,
            new Binding($"{nameof(ControlGridRowViewModel.DeviceCellsByDeviceKey)}[{durableDeviceKey}]"));

        var progressStyle = new Style(typeof(ProgressBar));
        progressStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));

        var showWhenMappedTrigger = new DataTrigger
        {
            Binding = new Binding(nameof(ControlGridDeviceCellViewModel.HasAxisBinding)),
            Value = true
        };
        showWhenMappedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        progressStyle.Triggers.Add(showWhenMappedTrigger);

        var progressFactory = new FrameworkElementFactory(typeof(ProgressBar));
        progressFactory.SetValue(ProgressBar.MinimumProperty, 0.0);
        progressFactory.SetValue(ProgressBar.MaximumProperty, 1.0);
        progressFactory.SetValue(ProgressBar.HeightProperty, 14.0);
        progressFactory.SetValue(ProgressBar.MarginProperty, new Thickness(4, 1, 4, 1));
        progressFactory.SetValue(FrameworkElement.StyleProperty, progressStyle);
        progressFactory.SetBinding(ProgressBar.ValueProperty, new Binding(nameof(ControlGridDeviceCellViewModel.AxisBarValue)));

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(ControlGridDeviceCellViewModel.DisplayText)));
        textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        textFactory.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 4, 0));

        gridFactory.AppendChild(progressFactory);
        gridFactory.AppendChild(textFactory);

        template.VisualTree = gridFactory;
        return template;
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
            viewModel.ApplyKeyboardMappingFromPopup,
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

        foreach (JoystickSession session in _joystickSessionsByDeviceKey.Values)
            session.Dispose();

        _joystickSessionsByDeviceKey.Clear();
        _previousPressedKeys.Clear();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        PollKeyboardSearch();
        PollLiveAxes();
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

    private void PollLiveAxes()
    {
        if (DataContext is not ControlsViewModel viewModel)
            return;

        Window? window = Window.GetWindow(this);
        if (window is null)
            return;

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        foreach (DeviceBindingProfile deviceProfile in viewModel.DeviceColumns.Where(device => device.AxisCount > 0))
        {
            if (!_joystickSessionsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out JoystickSession session))
            {
                try
                {
                    session = _di.OpenJoystick(deviceProfile.InstanceGuid, hwnd);
                    _joystickSessionsByDeviceKey[deviceProfile.DurableDeviceKey] = session;
                }
                catch
                {
                    continue;
                }
            }

            int[] axisValues;

            try
            {
                axisValues = DirectInputManager.ReadAxisVector(session.ReadState());
            }
            catch
            {
                continue;
            }

            foreach (ControlGridRowViewModel row in viewModel.Rows.Where(row => row.IsAxisRow))
            {
                if (!row.DeviceCellsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out ControlGridDeviceCellViewModel? cell))
                    continue;

                if (!cell.HasAxisBinding)
                    continue;

                if (cell.PhysicalAxisIndex < 0 || cell.PhysicalAxisIndex >= axisValues.Length)
                    continue;

                cell.AxisBarValue = NormalizeAxisValue(axisValues[cell.PhysicalAxisIndex]);
            }
        }
    }

    private static double NormalizeAxisValue(int rawValue)
    {
        if (rawValue <= 0)
            return 0;

        if (rawValue >= 65535)
            return 1;

        return rawValue / 65535.0;
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