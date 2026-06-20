using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Services.Controls;
using FalconBMS.Launcher.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
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
    private KeyboardSession? _keyboard;
    private readonly Dictionary<string, JoystickSession> _joystickSessionsByDeviceKey = new();
    private readonly Dictionary<string, bool[]> _previousButtonsByDeviceKey = new();
    private readonly Dictionary<string, int[]> _previousPovsByDeviceKey = new();

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
        {
            _subscribedViewModel.DeviceColumns.CollectionChanged -= DeviceColumns_CollectionChanged;
            _subscribedViewModel.PropertyChanged -= ControlsViewModel_PropertyChanged;
        }

        _subscribedViewModel = viewModel;

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.DeviceColumns.CollectionChanged += DeviceColumns_CollectionChanged;
            _subscribedViewModel.PropertyChanged += ControlsViewModel_PropertyChanged;
        }
    }

    private void ControlsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ControlsViewModel.IsUnassignedKeysCategory))
            RebuildDeviceColumns();
    }

    private void DeviceColumns_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildDeviceColumns();
    }

    private void RebuildDeviceColumns()
    {
        _deviceKeyByColumn.Clear();
        ControlsGrid.Columns.Clear();

        if (DataContext is not ControlsViewModel viewModel)
        {
            AddNormalFixedColumns();
            return;
        }

        if (viewModel.IsUnassignedKeysCategory)
        {
            ControlsGrid.FrozenColumnCount = 0;
            ControlsGrid.CanUserReorderColumns = false;

            ControlsGrid.Columns.Add(CreateUnassignedTextColumn(
                "Unassigned Key",
                nameof(ControlGridRowViewModel.UnassignedKey),
                nameof(ControlGridRowViewModel.UnassignedKeySortKey),
                180));

            ControlsGrid.Columns.Add(CreateUnassignedTextColumn(
                "Modifier",
                nameof(ControlGridRowViewModel.UnassignedModifier),
                nameof(ControlGridRowViewModel.UnassignedModifierSortKey),
                180));

            ControlsGrid.Columns.Add(CreateUnassignedTextColumn(
                "Key",
                nameof(ControlGridRowViewModel.UnassignedBaseKey),
                nameof(ControlGridRowViewModel.UnassignedBaseKeySortKey),
                180));

            return;
        }

        ControlsGrid.FrozenColumnCount = 2;
        ControlsGrid.CanUserReorderColumns = true;

        AddNormalFixedColumns();

        foreach (DeviceBindingProfile deviceProfile in viewModel.DeviceColumns)
        {
            var column = new DataGridTemplateColumn
            {
                Header = GetDeviceColumnHeader(deviceProfile),
                CellTemplate = CreateDeviceCellTemplate(deviceProfile.DurableDeviceKey),
                Width = new DataGridLength(140),
                MinWidth = 140,
                IsReadOnly = true,
                CanUserSort = false
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

    private void AddNormalFixedColumns()
    {
        ControlsGrid.Columns.Add(CreateMappingColumn());

        ControlsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Key",
            Binding = new Binding(nameof(ControlGridRowViewModel.Key)),
            ElementStyle = TryFindResource("ControlsTableTextBlockStyle") as Style,
            Width = new DataGridLength(140),
            MinWidth = 140,
            SortMemberPath = nameof(ControlGridRowViewModel.Key),
            CanUserSort = true,
            IsReadOnly = true
        });
    }

    private static DataGridTemplateColumn CreateMappingColumn()
    {
        var column = new DataGridTemplateColumn
        {
            Header = "Mapping",
            Width = new DataGridLength(380),
            MinWidth = 380,
            SortMemberPath = nameof(ControlGridRowViewModel.Mapping),
            CanUserSort = true,
            IsReadOnly = true
        };

        var template = new DataTemplate();

        var stackPanelFactory =
            new FrameworkElementFactory(typeof(StackPanel));

        stackPanelFactory.SetValue(
            StackPanel.OrientationProperty,
            Orientation.Horizontal);

        stackPanelFactory.SetValue(
            FrameworkElement.VerticalAlignmentProperty,
            VerticalAlignment.Center);

        var mappingTextFactory =
            new FrameworkElementFactory(typeof(TextBlock));

        mappingTextFactory.SetBinding(
            TextBlock.TextProperty,
            new Binding(nameof(ControlGridRowViewModel.Mapping)));

        mappingTextFactory.SetResourceReference(
            FrameworkElement.StyleProperty,
            "ControlsTableTextBlockStyle");

        var axisBadgeFactory =
            new FrameworkElementFactory(typeof(Border));

        axisBadgeFactory.SetResourceReference(
            FrameworkElement.StyleProperty,
            "ControlsTableAxisBadgeStyle");

        var axisBadgeTextFactory =
            new FrameworkElementFactory(typeof(TextBlock));

        axisBadgeTextFactory.SetResourceReference(
            FrameworkElement.StyleProperty,
            "ControlsTableAxisBadgeTextStyle");

        axisBadgeFactory.AppendChild(axisBadgeTextFactory);

        stackPanelFactory.AppendChild(mappingTextFactory);
        stackPanelFactory.AppendChild(axisBadgeFactory);

        template.VisualTree = stackPanelFactory;
        column.CellTemplate = template;

        return column;
    }

    private DataGridTextColumn CreateUnassignedTextColumn(
        string header,
        string bindingPath,
        string sortMemberPath,
        double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(bindingPath),
            ElementStyle = TryFindResource("ControlsTableTextBlockStyle") as Style,
            Width = new DataGridLength(width),
            MinWidth = 140,
            SortMemberPath = sortMemberPath,
            CanUserSort = true,
            IsReadOnly = true
        };
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
        if (DataContext is ControlsViewModel viewModel &&
            viewModel.IsUnassignedKeysCategory)
        {
            return;
        }

        if (_deviceKeyByColumn.Count == 0)
            return;

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

    private static DataTemplate CreateDeviceCellTemplate(
        string durableDeviceKey)
    {
        var template = new DataTemplate();

        var gridFactory =
            new FrameworkElementFactory(typeof(Grid));

        gridFactory.SetBinding(
            FrameworkElement.DataContextProperty,
            new Binding(
                $"{nameof(ControlGridRowViewModel.DeviceCellsByDeviceKey)}[{durableDeviceKey}]"));

        gridFactory.SetResourceReference(
            FrameworkElement.StyleProperty,
            "ControlsTableDeviceCellGridStyle");

        var panelFactory =
            new FrameworkElementFactory(typeof(StackPanel));

        panelFactory.SetValue(
            StackPanel.OrientationProperty,
            Orientation.Vertical);

        panelFactory.SetValue(
            FrameworkElement.VerticalAlignmentProperty,
            VerticalAlignment.Center);

        /*
         * Primary axis bar
         *
         * For normal axis rows, this is the only visible bar.
         * For AxisPair rows, this represents Pitch.
         */
        var primaryAxisBarStyle = new Style(typeof(AxisBar));

        primaryAxisBarStyle.Setters.Add(
            new Setter(
                UIElement.VisibilityProperty,
                Visibility.Collapsed));

        var showPrimaryAxisTrigger = new DataTrigger
        {
            Binding = new Binding(
                nameof(ControlGridDeviceCellViewModel.HasAxisBinding)),
            Value = true
        };

        showPrimaryAxisTrigger.Setters.Add(
            new Setter(
                UIElement.VisibilityProperty,
                Visibility.Visible));

        primaryAxisBarStyle.Triggers.Add(showPrimaryAxisTrigger);

        var primaryAxisBarFactory =
            new FrameworkElementFactory(typeof(AxisBar));

        primaryAxisBarFactory.SetValue(
            FrameworkElement.HeightProperty,
            18.0);

        primaryAxisBarFactory.SetValue(
            FrameworkElement.MarginProperty,
            new Thickness(4, 2, 4, 2));

        primaryAxisBarFactory.SetValue(
            FrameworkElement.StyleProperty,
            primaryAxisBarStyle);

        primaryAxisBarFactory.SetBinding(
            AxisBar.ValueProperty,
            new Binding(
                nameof(ControlGridDeviceCellViewModel.AxisBarValue))
            {
                Mode = BindingMode.OneWay
            });

        primaryAxisBarFactory.SetBinding(
            AxisBar.IsActiveProperty,
            new Binding(
                nameof(ControlGridDeviceCellViewModel.HasAxisBinding))
            {
                Mode = BindingMode.OneWay
            });

        primaryAxisBarFactory.SetBinding(
            AxisBar.TextProperty,
            new Binding(
                nameof(ControlGridDeviceCellViewModel.DisplayText))
            {
                Mode = BindingMode.OneWay
            });

        primaryAxisBarFactory.SetBinding(
            AxisBar.ShowDetentsProperty,
            new Binding(
                nameof(ControlGridDeviceCellViewModel.ShowDetents))
            {
                Mode = BindingMode.OneWay
            });

        primaryAxisBarFactory.SetBinding(
            AxisBar.IdleDetentFractionProperty,
            new Binding(
                nameof(ControlGridDeviceCellViewModel.IdleDetentFraction))
            {
                Mode = BindingMode.OneWay
            });

        primaryAxisBarFactory.SetBinding(
            AxisBar.AfterburnerDetentFractionProperty,
            new Binding(
                nameof(ControlGridDeviceCellViewModel.AfterburnerDetentFraction))
            {
                Mode = BindingMode.OneWay
            });

        /*
         * Secondary AxisPair bar
         *
         * This remains collapsed for every normal table row.
         * For the Pitch/Roll row, this represents Roll.
         */
        var secondaryAxisBarStyle = new Style(typeof(AxisBar));

        secondaryAxisBarStyle.Setters.Add(
            new Setter(
                UIElement.VisibilityProperty,
                Visibility.Collapsed));

        var showSecondaryAxisTrigger = new DataTrigger
        {
            Binding = new Binding(
                nameof(ControlGridDeviceCellViewModel.SecondaryHasAxisBinding)),
            Value = true
        };

        showSecondaryAxisTrigger.Setters.Add(
            new Setter(
                UIElement.VisibilityProperty,
                Visibility.Visible));

        secondaryAxisBarStyle.Triggers.Add(showSecondaryAxisTrigger);

        var secondaryAxisBarFactory =
            new FrameworkElementFactory(typeof(AxisBar));

        secondaryAxisBarFactory.SetValue(
            FrameworkElement.HeightProperty,
            18.0);

        secondaryAxisBarFactory.SetValue(
            FrameworkElement.MarginProperty,
            new Thickness(4, 2, 4, 2));

        secondaryAxisBarFactory.SetValue(
            FrameworkElement.StyleProperty,
            secondaryAxisBarStyle);

        secondaryAxisBarFactory.SetBinding(
            AxisBar.ValueProperty,
            new Binding(
                nameof(ControlGridDeviceCellViewModel.SecondaryAxisBarValue))
            {
                Mode = BindingMode.OneWay
            });

        secondaryAxisBarFactory.SetBinding(
            AxisBar.IsActiveProperty,
            new Binding(
                nameof(ControlGridDeviceCellViewModel.SecondaryHasAxisBinding))
            {
                Mode = BindingMode.OneWay
            });

        secondaryAxisBarFactory.SetBinding(
            AxisBar.TextProperty,
            new Binding(
                nameof(ControlGridDeviceCellViewModel.SecondaryDisplayText))
            {
                Mode = BindingMode.OneWay
            });

        /*
         * Normal text for buttons, keys, POVs, and completely unmapped cells.
         */
        var textStyle = new Style(typeof(TextBlock));

        textStyle.Setters.Add(
            new Setter(
                UIElement.VisibilityProperty,
                Visibility.Visible));

        var hideTextForPrimaryAxisTrigger = new DataTrigger
        {
            Binding = new Binding(
                nameof(ControlGridDeviceCellViewModel.HasAxisBinding)),
            Value = true
        };

        hideTextForPrimaryAxisTrigger.Setters.Add(
            new Setter(
                UIElement.VisibilityProperty,
                Visibility.Collapsed));

        textStyle.Triggers.Add(hideTextForPrimaryAxisTrigger);

        var hideTextForSecondaryAxisTrigger = new DataTrigger
        {
            Binding = new Binding(
                nameof(ControlGridDeviceCellViewModel.SecondaryHasAxisBinding)),
            Value = true
        };

        hideTextForSecondaryAxisTrigger.Setters.Add(
            new Setter(
                UIElement.VisibilityProperty,
                Visibility.Collapsed));

        textStyle.Triggers.Add(hideTextForSecondaryAxisTrigger);

        var textFactory =
            new FrameworkElementFactory(typeof(TextBlock));

        textFactory.SetValue(
            FrameworkElement.MarginProperty,
            new Thickness(4, 0, 4, 0));

        textFactory.SetValue(
            FrameworkElement.VerticalAlignmentProperty,
            VerticalAlignment.Center);

        textFactory.SetValue(
            TextBlock.TextTrimmingProperty,
            TextTrimming.CharacterEllipsis);

        textFactory.SetBinding(
            TextBlock.TextProperty,
            new Binding(
                nameof(ControlGridDeviceCellViewModel.DisplayText))
            {
                Mode = BindingMode.OneWay
            });

        textFactory.SetValue(
            FrameworkElement.StyleProperty,
            textStyle);

        panelFactory.AppendChild(primaryAxisBarFactory);
        panelFactory.AppendChild(secondaryAxisBarFactory);
        panelFactory.AppendChild(textFactory);

        gridFactory.AppendChild(panelFactory);

        template.VisualTree = gridFactory;
        return template;
    }

    private void ControlsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataContext is not ControlsViewModel viewModel)
            return;

        ControlGridRowViewModel? selectedRow =
            viewModel.SelectedRow;

        if (selectedRow is null)
            return;

        Window? popupOwnerWindow =
            Window.GetWindow(this);

        AxisPairDefinition? advancedAxisDefinition =
            selectedRow.IsAxisPairRow
                ? selectedRow.AxisPairDefinition
                : selectedRow.IsAxisRow
                    ? AxisPairDefinitionService.FindByLogicalAxisName(
                        selectedRow.AxisLogicalAxisName)
                    : null;

        if (advancedAxisDefinition is not null)
        {
            StopKeyboardSearchCapture();

            var axisPairWindow =
                new AxisPairAssignWindow
                {
                    Owner = popupOwnerWindow
                };

            string? clickedDeviceKey =
                GetClickedDeviceKey(
                    e.OriginalSource as DependencyObject,
                    viewModel);

            IntPtr hwnd =
                popupOwnerWindow is not null
                    ? new WindowInteropHelper(
                        popupOwnerWindow).Handle
                    : IntPtr.Zero;

            axisPairWindow.DataContext =
                new AxisPairAssignViewModel(
                    advancedAxisDefinition,
                    viewModel.DeviceColumns,
                    clickedDeviceKey,
                    hwnd,
                    viewModel.ApplyAxisPairMappingFromPopup,
                    () => axisPairWindow.Close());

            try
            {
                using (MainWindow.BeginModalOverlay(popupOwnerWindow))
                {
                    axisPairWindow.ShowDialog();
                }
            }
            finally
            {
                StartKeyboardSearchCapture();
            }

            return;
        }

        if (selectedRow.IsAxisRow)
        {
            StopKeyboardSearchCapture();

            var axisWindow =
                new AxisAssignWindow
                {
                    Owner = popupOwnerWindow
                };

            string? clickedDeviceKey =
                GetClickedDeviceKey(
                    e.OriginalSource as DependencyObject,
                    viewModel);

            IntPtr hwnd =
                popupOwnerWindow is not null
                    ? new WindowInteropHelper(
                        popupOwnerWindow).Handle
                    : IntPtr.Zero;

            axisWindow.DataContext =
                new AxisAssignViewModel(
                    selectedRow,
                    viewModel.DeviceColumns,
                    clickedDeviceKey,
                    hwnd,
                    viewModel.ApplyAxisMappingFromPopup,
                    () => axisWindow.Close());

            try
            {
                using (MainWindow.BeginModalOverlay(popupOwnerWindow))
                {
                    axisWindow.ShowDialog();
                }
            }
            finally
            {
                StartKeyboardSearchCapture();
            }

            return;
        }

        if (selectedRow.SourceRow is null)
            return;

        if (!selectedRow.IsEditable)
            return;

        StopKeyboardSearchCapture();

        var window =
            new KeyMappingWindow
            {
                Owner = popupOwnerWindow
            };

        if (viewModel.SelectedProfile is null)
        {
            StartKeyboardSearchCapture();
            return;
        }

        window.DataContext =
            new KeyMappingWindowViewModel(
                selectedRow.SourceRow,
                viewModel.SelectedProfileRows,
                viewModel.DeviceColumns,
                viewModel.SelectedProfile.AircraftProfile,
                viewModel.ApplyKeyboardMappingFromPopup,
                viewModel.ApplyDeviceButtonMappingFromPopup,
                viewModel.ApplyDevicePovMappingFromPopup,
                () => window.Close());

        try
        {
            using (MainWindow.BeginModalOverlay(popupOwnerWindow))
            {
                window.ShowDialog();
            }
        }
        finally
        {
            StartKeyboardSearchCapture();
        }
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
        _previousButtonsByDeviceKey.Clear();
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

        bool selectedMatch =
            viewModel.IsUnassignedKeysCategory
                ? viewModel.SelectFirstVisibleUnassignedKeyMatch(assignmentStatus)
                : viewModel.SelectFirstVisibleKeyMatch(assignmentStatus);

        if (!selectedMatch)
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

        List<DeviceBindingProfile> connectedDevices = viewModel.DeviceColumns
            .Where(device => device.IsConnected && (device.ButtonCount > 0 || device.PovCount > 0))
            .ToList();

        var currentButtonsByDeviceKey = new Dictionary<string, bool[]>();
        var currentPovsByDeviceKey = new Dictionary<string, int[]>();

        // Read every connected device first so DX shift state is based on the
        // full current controller state, not just the device currently being scanned.
        // POV hats are captured from the same state read so POV clicks can also
        // jump the Controls table to the currently mapped callback row.
        foreach (DeviceBindingProfile deviceProfile in connectedDevices)
        {
            JoystickSession? session = EnsureJoystickOpened(deviceProfile, hwnd);
            if (session is null)
                continue;

            try
            {
                var state = session.ReadState();

                currentButtonsByDeviceKey[deviceProfile.DurableDeviceKey] =
                    state.Buttons ?? Array.Empty<bool>();

                currentPovsByDeviceKey[deviceProfile.DurableDeviceKey] =
                    state.PointOfViewControllers ?? Array.Empty<int>();
            }
            catch
            {
                continue;
            }
        }

        bool isShifted = viewModel.IsDxShiftActive(currentButtonsByDeviceKey);

        foreach (DeviceBindingProfile deviceProfile in connectedDevices)
        {
            currentButtonsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out bool[]? buttons);
            currentPovsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out int[]? povs);

            buttons ??= Array.Empty<bool>();
            povs ??= Array.Empty<int>();

            bool hasPreviousButtons = _previousButtonsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out bool[]? previousButtons);
            bool hasPreviousPovs = _previousPovsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out int[]? previousPovs);

            if (!hasPreviousButtons && !hasPreviousPovs)
            {
                _previousButtonsByDeviceKey[deviceProfile.DurableDeviceKey] = (bool[])buttons.Clone();
                _previousPovsByDeviceKey[deviceProfile.DurableDeviceKey] = (int[])povs.Clone();
                continue;
            }

            previousButtons ??= Array.Empty<bool>();
            previousPovs ??= Array.Empty<int>();

            bool selectedMatch = false;

            int buttonLimit = Math.Min(buttons.Length, previousButtons.Length);

            for (int buttonIndex = 0; buttonIndex < buttonLimit; buttonIndex++)
            {
                bool wasPressed = previousButtons[buttonIndex];
                bool isPressed = buttons[buttonIndex];

                if (wasPressed == isPressed)
                    continue;

                bool isRelease = wasPressed && !isPressed;

                selectedMatch = viewModel.SelectFirstVisibleDxMatch(
                    deviceProfile.DurableDeviceKey,
                    buttonIndex,
                    isRelease,
                    isShifted);

                break;
            }

            int povLimit = Math.Min(povs.Length, previousPovs.Length);

            for (int povIndex = 0; !selectedMatch && povIndex < povLimit; povIndex++)
            {
                int previousDirectionValue = previousPovs[povIndex];
                int currentDirectionValue = povs[povIndex];

                if (previousDirectionValue == currentDirectionValue)
                    continue;

                int? direction = NormalizeDirectInputPovDirection(currentDirectionValue);

                if (!direction.HasValue)
                    continue;

                selectedMatch = viewModel.SelectFirstVisiblePovMatch(
                    deviceProfile.DurableDeviceKey,
                    povIndex,
                    direction.Value,
                    isShifted);

                break;
            }

            if (selectedMatch && viewModel.SelectedRow is not null)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ControlsGrid.UpdateLayout();
                    ControlsGrid.SelectedItem = viewModel.SelectedRow;
                    ControlsGrid.ScrollIntoView(viewModel.SelectedRow);
                }, DispatcherPriority.Background);
            }

            _previousButtonsByDeviceKey[deviceProfile.DurableDeviceKey] = (bool[])buttons.Clone();
            _previousPovsByDeviceKey[deviceProfile.DurableDeviceKey] = (int[])povs.Clone();
        }
    }

    private static int? NormalizeDirectInputPovDirection(int povValue)
    {
        // DirectInput POV values are hundredths of a degree:
        // 0=Up, 9000=Right, 18000=Down, 27000=Left, -1=centered.
        // BMS stock XML stores POV directions in 8-way slots:
        // 0=Up, 2=Right, 4=Down, 6=Left, with odd numbers as diagonals.
        if (povValue < 0)
            return null;

        int normalizedDegrees = ((povValue / 100) + 360) % 360;
        int eightWayDirection = (int)Math.Round(normalizedDegrees / 45.0) % 8;

        return eightWayDirection;
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

        foreach (DeviceBindingProfile deviceProfile in
                 viewModel.DeviceColumns.Where(device =>
                     device.IsConnected &&
                     device.AxisCount > 0))
        {
            JoystickSession? session =
                EnsureJoystickOpened(deviceProfile, hwnd);

            if (session is null)
                continue;

            int[] axisValues;

            try
            {
                axisValues =
                    DirectInputManager.ReadAxisVector(
                        session.ReadState());
            }
            catch
            {
                continue;
            }

            foreach (ControlGridRowViewModel row in
                     viewModel.Rows.Where(row =>
                         row.IsAxisRow ||
                         row.IsAxisPairRow))
            {
                if (!row.DeviceCellsByDeviceKey.TryGetValue(
                        deviceProfile.DurableDeviceKey,
                        out ControlGridDeviceCellViewModel? cell))
                {
                    continue;
                }

                if (row.IsAxisPairRow)
                {
                    AxisPairDefinition? pairDefinition =
                        row.AxisPairDefinition;

                    if (pairDefinition is null)
                        continue;

                    if (cell.HasAxisBinding &&
                        cell.PhysicalAxisIndex >= 0 &&
                        cell.PhysicalAxisIndex < axisValues.Length)
                    {
                        cell.AxisBarValue =
                            AxisAssignViewModel.NormalizeAxisValue(
                                axisValues[cell.PhysicalAxisIndex],
                                pairDefinition.PrimaryLogicalAxisName,
                                cell.Invert);
                    }

                    if (cell.SecondaryHasAxisBinding &&
                        cell.SecondaryPhysicalAxisIndex >= 0 &&
                        cell.SecondaryPhysicalAxisIndex <
                        axisValues.Length)
                    {
                        cell.SecondaryAxisBarValue =
                            AxisAssignViewModel.NormalizeAxisValue(
                                axisValues[
                                    cell.SecondaryPhysicalAxisIndex],
                                pairDefinition.SecondaryLogicalAxisName,
                                cell.SecondaryInvert);
                    }

                    continue;
                }

                if (!cell.HasAxisBinding)
                    continue;

                if (cell.PhysicalAxisIndex < 0 ||
                    cell.PhysicalAxisIndex >= axisValues.Length)
                {
                    continue;
                }

                cell.AxisBarValue =
                    AxisAssignViewModel.NormalizeAxisValue(
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

    private void CategoryListBox_PreviewMouseLeftButtonUp(
    object sender,
    MouseButtonEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ControlsGrid.Focus();
            Keyboard.Focus(ControlsGrid);
        }, DispatcherPriority.Background);
    }

    private void CategoryListBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (!IsCategoryTextSearchKey(e.Key))
            return;

        // Prevent WPF ListBox text-search from changing categories.
        // DirectInput polling still sees the key press and can jump the table row.
        e.Handled = true;
    }

    private static bool IsCategoryTextSearchKey(Key key)
    {
        return (key >= Key.A && key <= Key.Z) ||
               (key >= Key.D0 && key <= Key.D9) ||
               (key >= Key.NumPad0 && key <= Key.NumPad9);
    }

    private bool IsFilterControlFocused()
    {
        return FocusManager.GetFocusedElement(this) is TextBox or ComboBox;
    }
}