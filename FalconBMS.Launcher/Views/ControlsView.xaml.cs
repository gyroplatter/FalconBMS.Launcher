using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
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
using System.Windows.Media;
using System.Windows.Threading;
using static FalconBMS.Launcher.Input.KeyboardSession;
using DiKey = Vortice.DirectInput.Key;

namespace FalconBMS.Launcher.Views;

public partial class ControlsView : UserControl
{
    private readonly DirectInputManager _di = new();
    private readonly LiveDeviceButtonPollingService _deviceButtonPolling = new();
    private KeyboardSession? _keyboard;
    private readonly Dictionary<string, JoystickSession> _joystickSessionsByDeviceKey = new();

    // Tracks which dynamic device column belongs to which DurableDeviceKey.
    // This keeps double-click mapping correct even after the user reorders columns.
    private readonly Dictionary<DataGridColumn, string> _deviceKeyByColumn = new();

    // Prevents saving while we are restoring the saved column order.
    private bool _isRestoringDeviceColumnOrder;

    private HashSet<DiKey> _previousPressedKeys = new();
    private DispatcherTimer? _timer;
    private ControlsViewModel? _subscribedViewModel;

    public ControlsView()
    {
        InitializeComponent();

        _deviceButtonPolling.ButtonStateChanged += DeviceButtonPolling_ButtonStateChanged;

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

        _deviceKeyByColumn.Clear();

        while (ControlsGrid.Columns.Count > fixedColumnCount)
            ControlsGrid.Columns.RemoveAt(fixedColumnCount);

        if (DataContext is not ControlsViewModel viewModel)
            return;

        foreach (DeviceBindingProfile deviceProfile in viewModel.DeviceColumns)
        {
            var column = new DataGridTemplateColumn
            {
                Header = GetDeviceColumnHeader(deviceProfile),
                CellTemplate = CreateDeviceCellTemplate(deviceProfile.DurableDeviceKey),
                Width = new DataGridLength(140),
                MinWidth = 140,
                IsReadOnly = true
            };

            ControlsGrid.Columns.Add(column);
            _deviceKeyByColumn[column] = deviceProfile.DurableDeviceKey;
        }

        RestoreSavedDeviceColumnOrder();

        // Save the current visible order after rebuild.
        // This keeps the setting current when new devices are discovered
        // and appends them after the user's saved device order.
        SaveDeviceColumnOrder();
    }

    private void RestoreSavedDeviceColumnOrder()
    {
        const int fixedColumnCount = 2;

        string savedOrder = Properties.Settings.Default.ControlsDeviceColumnOrder;

        if (string.IsNullOrWhiteSpace(savedOrder))
            return;

        string[] savedDeviceKeys = savedOrder
            .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

        if (savedDeviceKeys.Length == 0)
            return;

        Dictionary<string, int> savedIndexByDeviceKey = savedDeviceKeys
            .Select((deviceKey, index) => new { deviceKey, index })
            .GroupBy(item => item.deviceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().index,
                StringComparer.OrdinalIgnoreCase);

        List<DataGridColumn> deviceColumns = ControlsGrid.Columns
            .Where(column => _deviceKeyByColumn.ContainsKey(column))
            .OrderBy(column =>
            {
                string deviceKey = _deviceKeyByColumn[column];

                return savedIndexByDeviceKey.TryGetValue(deviceKey, out int savedIndex)
                    ? savedIndex
                    : int.MaxValue;
            })
            .ThenBy(column => ControlsGrid.Columns.IndexOf(column))
            .ToList();

        _isRestoringDeviceColumnOrder = true;

        try
        {
            for (int index = 0; index < deviceColumns.Count; index++)
                deviceColumns[index].DisplayIndex = fixedColumnCount + index;
        }
        finally
        {
            _isRestoringDeviceColumnOrder = false;
        }
    }

    private void ControlsGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
    {
        if (_isRestoringDeviceColumnOrder)
            return;

        SaveDeviceColumnOrder();
    }

    private void SaveDeviceColumnOrder()
    {
        string savedOrder = string.Join(
            "|",
            ControlsGrid.Columns
                .Where(column => _deviceKeyByColumn.ContainsKey(column))
                .OrderBy(column => column.DisplayIndex)
                .Select(column => _deviceKeyByColumn[column]));

        Properties.Settings.Default.ControlsDeviceColumnOrder = savedOrder;
        Properties.Settings.Default.Save();
    }

    private static object GetDeviceColumnHeader(DeviceBindingProfile deviceProfile)
    {
        string displayName;

        if (!string.IsNullOrWhiteSpace(deviceProfile.ProductName))
            displayName = deviceProfile.ProductName;
        else if (!string.IsNullOrWhiteSpace(deviceProfile.InstanceName))
            displayName = deviceProfile.InstanceName;
        else
            displayName = deviceProfile.DurableDeviceKey;

        if (deviceProfile.IsConnected)
            return displayName;

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical
        };

        panel.Children.Add(new TextBlock
        {
            Text = displayName
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Offline",
            FontStyle = FontStyles.Italic,
        });

        return panel;
    }

    private static DataTemplate CreateDeviceCellTemplate(string durableDeviceKey)
    {
        var template = new DataTemplate();

        var gridFactory = new FrameworkElementFactory(typeof(Grid));
        gridFactory.SetBinding(
            FrameworkElement.DataContextProperty,
            new Binding($"{nameof(ControlGridRowViewModel.DeviceCellsByDeviceKey)}[{durableDeviceKey}]"));

        gridFactory.SetResourceReference(FrameworkElement.StyleProperty, "ControlsTableDeviceCellGridStyle");

        var axisBarStyle = new Style(typeof(AxisBar));
        axisBarStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));

        var showAxisWhenMappedTrigger = new DataTrigger
        {
            Binding = new Binding(nameof(ControlGridDeviceCellViewModel.HasAxisBinding)),
            Value = true
        };
        showAxisWhenMappedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        axisBarStyle.Triggers.Add(showAxisWhenMappedTrigger);

        var axisBarFactory = new FrameworkElementFactory(typeof(AxisBar));
        axisBarFactory.SetValue(FrameworkElement.HeightProperty, 14.0);
        axisBarFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 1, 4, 1));
        axisBarFactory.SetValue(FrameworkElement.StyleProperty, axisBarStyle);

        axisBarFactory.SetBinding(AxisBar.ValueProperty, new Binding(nameof(ControlGridDeviceCellViewModel.AxisBarValue))
        {
            Mode = BindingMode.OneWay
        });

        axisBarFactory.SetBinding(AxisBar.IsActiveProperty, new Binding(nameof(ControlGridDeviceCellViewModel.HasAxisBinding))
        {
            Mode = BindingMode.OneWay
        });

        axisBarFactory.SetBinding(AxisBar.TextProperty, new Binding(nameof(ControlGridDeviceCellViewModel.DisplayText))
        {
            Mode = BindingMode.OneWay
        });

        axisBarFactory.SetBinding(AxisBar.ShowDetentsProperty, new Binding(nameof(ControlGridDeviceCellViewModel.ShowDetents))
        {
            Mode = BindingMode.OneWay
        });

        axisBarFactory.SetBinding(AxisBar.IdleDetentFractionProperty, new Binding(nameof(ControlGridDeviceCellViewModel.IdleDetentFraction))
        {
            Mode = BindingMode.OneWay
        });

        axisBarFactory.SetBinding(AxisBar.AfterburnerDetentFractionProperty, new Binding(nameof(ControlGridDeviceCellViewModel.AfterburnerDetentFraction))
        {
            Mode = BindingMode.OneWay
        });

        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));

        var hideTextWhenAxisTrigger = new DataTrigger
        {
            Binding = new Binding(nameof(ControlGridDeviceCellViewModel.HasAxisBinding)),
            Value = true
        };
        hideTextWhenAxisTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        textStyle.Triggers.Add(hideTextWhenAxisTrigger);

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 4, 0));
        textFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        textFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        textFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(ControlGridDeviceCellViewModel.DisplayText))
        {
            Mode = BindingMode.OneWay
        });
        textFactory.SetValue(FrameworkElement.StyleProperty, textStyle);

        gridFactory.AppendChild(axisBarFactory);
        gridFactory.AppendChild(textFactory);

        template.VisualTree = gridFactory;
        return template;
    }

    private void ControlsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ControlsViewModel viewModel)
            return;

        ControlGridRowViewModel? selectedRow = viewModel.SelectedRow;

        if (selectedRow is null)
            return;

        if (selectedRow.IsAxisRow)
        {
            StopKeyboardSearchCapture();

            Window? ownerWindow = Window.GetWindow(this);

            var axisWindow = new AxisAssignWindow
            {
                Owner = ownerWindow
            };

            string? clickedDeviceKey = GetClickedDeviceKey(e.OriginalSource as DependencyObject, viewModel);

            // The owner should normally be the main launcher window, but use IntPtr.Zero as a safe fallback
            // so nullable analysis and unusual design/runtime states do not break the axis popup flow.
            IntPtr hwnd = ownerWindow is not null
                ? new WindowInteropHelper(ownerWindow).Handle
                : IntPtr.Zero;

            axisWindow.DataContext = new AxisAssignViewModel(
                selectedRow,
                viewModel.DeviceColumns,
                clickedDeviceKey,
                hwnd,
                viewModel.ApplyAxisMappingFromPopup,
                () => axisWindow.Close());

            axisWindow.ShowDialog();

            StartKeyboardSearchCapture();
            return;
        }

        if (selectedRow.SourceRow is null)
            return;

        if (!selectedRow.IsEditable)
            return;

        StopKeyboardSearchCapture();

        var window = new KeyMappingWindow
        {
            Owner = Window.GetWindow(this)
        };

        if (viewModel.SelectedProfile is null)
            return;

        window.DataContext = new KeyMappingWindowViewModel(
            selectedRow.SourceRow,
            viewModel.SelectedProfileRows,
            viewModel.DeviceColumns,
            viewModel.SelectedProfile.AircraftProfile,
            viewModel.ApplyKeyboardMappingFromPopup,
            viewModel.ApplyDeviceButtonMappingFromPopup,
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

        // Button transition polling is shared with DevicesView.
        // Reset it whenever Controls polling stops so stale held-button state
        // does not carry into the next polling session.
        _deviceButtonPolling.Reset();

        _previousPressedKeys.Clear();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        PollKeyboardSearch();
        PollDeviceButtonSearch();
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

    private void PollDeviceButtonSearch()
    {
        if (IsFilterControlFocused())
            return;

        if (DataContext is not ControlsViewModel viewModel)
            return;

        Window? window = Window.GetWindow(this);
        if (window is null)
            return;

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        _deviceButtonPolling.Poll(viewModel.DeviceColumns, hwnd);
    }

    private void DeviceButtonPolling_ButtonStateChanged(object? sender, LiveDeviceButtonStateChangedEventArgs e)
    {
        if (IsFilterControlFocused())
            return;

        if (DataContext is not ControlsViewModel viewModel)
            return;

        bool isShifted = viewModel.IsDxShiftActive(e.CurrentButtonsByDeviceKey);

        if (!viewModel.SelectFirstVisibleDxMatch(e.DurableDeviceKey, e.ButtonIndex, e.IsRelease, isShifted))
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

        foreach (DeviceBindingProfile deviceProfile in viewModel.DeviceColumns.Where(device => device.IsConnected && device.AxisCount > 0))
        {
            JoystickSession? session = EnsureJoystickOpened(deviceProfile, hwnd);
            if (session is null)
                continue;

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

                cell.AxisBarValue = AxisAssignViewModel.NormalizeAxisValue(
                    axisValues[cell.PhysicalAxisIndex],
                    row.AxisLogicalAxisName,
                    cell.Invert);
            }
        }
    }

    private JoystickSession? EnsureJoystickOpened(DeviceBindingProfile deviceProfile, IntPtr hwnd)
    {
        if (!deviceProfile.IsConnected)
            return null;

        if (_joystickSessionsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out JoystickSession session))
            return session;

        try
        {
            session = _di.OpenJoystick(deviceProfile.InstanceGuid, hwnd);
            _joystickSessionsByDeviceKey[deviceProfile.DurableDeviceKey] = session;
            return session;
        }
        catch
        {
            return null;
        }
    }

    private string? GetClickedDeviceKey(DependencyObject? originalSource, ControlsViewModel viewModel)
    {
        DataGridCell? cell = FindVisualParent<DataGridCell>(originalSource);
        if (cell is null)
            return null;

        // Do not calculate this from DisplayIndex.
        // DisplayIndex changes when the user reorders columns, but the device collection order does not.
        return _deviceKeyByColumn.TryGetValue(cell.Column, out string? durableDeviceKey)
            ? durableDeviceKey
            : null;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T typedParent)
                return typedParent;

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
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