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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DiKey = Vortice.DirectInput.Key;

namespace FalconBMS.Launcher.Views;

public partial class ControlsView : UserControl
{
    // Controls creates DirectInput capture only when this view is active.
    // Startup is performed on a worker thread so native device acquisition
    // does not block WPF from rendering the Controls tab.
    private readonly SemaphoreSlim _captureStartGate = new(1, 1);

    private DirectInputCaptureHost? _captureHost;
    private CancellationTokenSource? _captureStartCancellation;

    // Last Input axis detection uses the same basic jitter protections as axis
    // assignment, but with a lower movement threshold
    private const int LastInputAxisSettleMs = 600;
    private const int LastInputAxisStableHitsRequired = 3;
    private const double LastInputAxisDominantRatio = 1.5;

    private static readonly int LastInputAxisMovementThreshold =
        (DetentPosition.MaxAxisValue - DetentPosition.MinAxisValue) / 16;

    private readonly Dictionary<string, int[]> _lastInputAxisBaselineByDeviceKey = new();
    private readonly Dictionary<string, int> _lastInputAxisStableHitsByCandidate = new();

    private DateTime _lastInputAxisCaptureStartedUtc;

    private sealed class LastInputAxisCandidate
    {
        public required DeviceBindingProfile Device { get; init; }
        public required int AxisIndex { get; init; }
        public required int CurrentValue { get; init; }
        public required int Delta { get; init; }

        public string CandidateKey =>
            Device.DurableDeviceKey + ":" + AxisIndex;
    }

    // Tracks which dynamic device column belongs to which DurableDeviceKey.
    // This keeps double-click mapping correct even after the user reorders columns.
    private readonly Dictionary<DataGridColumn, string> _deviceKeyByColumn = new();

    // Prevents saving while we are restoring the saved column order.
    private bool _isRestoringDeviceColumnOrder;

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

        // During initial view creation, DataContext is assigned before Loaded.
        // Let Loaded perform that first column build so it is not done twice.
        if (IsLoaded)
            RebuildDeviceColumns();
    }

    private void SubscribeToViewModel(ControlsViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
            return;

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.DeviceColumns.CollectionChanged -=
                DeviceColumns_CollectionChanged;

            _subscribedViewModel.PropertyChanged -=
                ControlsViewModel_PropertyChanged;

            _subscribedViewModel.BindingModelLoaded -=
                ControlsViewModel_BindingModelLoaded;
        }

        _subscribedViewModel = viewModel;

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.DeviceColumns.CollectionChanged +=
                DeviceColumns_CollectionChanged;

            _subscribedViewModel.PropertyChanged +=
                ControlsViewModel_PropertyChanged;

            _subscribedViewModel.BindingModelLoaded +=
                ControlsViewModel_BindingModelLoaded;
        }
    }

    private void ControlsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ControlsViewModel.IsUnassignedKeysCategory))
            RebuildDeviceColumns();
    }

    private void ControlsViewModel_BindingModelLoaded(
    object? sender,
    EventArgs e)
    {
        if (!IsLoaded)
            return;

        // DeviceColumns already raised collection notifications while the model
        // was rebuilding. Rebuild once more against the completed collection,
        // then restart Controls own capture with the new connected devices.
        RebuildDeviceColumns();
        StartKeyboardSearchCapture();
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
        string displayName = GetDeviceDisplayName(deviceProfile);

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

    private static string GetDeviceDisplayName(DeviceBindingProfile deviceProfile)
    {
        if (!string.IsNullOrWhiteSpace(deviceProfile.ProductName))
            return deviceProfile.ProductName;

        if (!string.IsNullOrWhiteSpace(deviceProfile.InstanceName))
            return deviceProfile.InstanceName;

        return deviceProfile.DurableDeviceKey;
    }

    private void UpdateLastInput(string deviceName, string inputName)
    {
        // Avoid unnecessary WPF property changes while an input remains active
        if (LastInputDeviceText.Text == deviceName &&
            LastInputValueText.Text == inputName)
        {
            return;
        }

        LastInputDeviceText.Text = deviceName;
        LastInputValueText.Text = inputName;
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
                                viewModel.SelectedProfile?.AircraftProfile ?? "",
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
                                viewModel.SelectedProfile?.AircraftProfile ?? "",
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

    private async void StartKeyboardSearchCapture()
    {
        StopKeyboardSearchCapture();

        if (DataContext is not ControlsViewModel viewModel)
            return;

        Window? window = Window.GetWindow(this);
        if (window is null)
            return;

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        // Materialize the device list while we are still on the UI thread.
        // The actual DirectInput manager/device creation and acquisition below
        // happens on a worker thread.
        DirectInputCaptureDevice[] joystickDevices =
            viewModel.DeviceColumns
                .Where(device =>
                    device.IsConnected &&
                    (device.AxisCount > 0 ||
                     device.ButtonCount > 0 ||
                     device.PovCount > 0))
                .Select(device =>
                    new DirectInputCaptureDevice(
                        device.DurableDeviceKey,
                        device.InstanceGuid))
                .ToArray();

        var cancellation =
            new CancellationTokenSource();

        _captureStartCancellation = cancellation;

        bool gateEntered = false;
        DirectInputCaptureHost? captureHost = null;

        try
        {
            await _captureStartGate.WaitAsync(
                cancellation.Token);

            gateEntered = true;

            captureHost = await Task.Run(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();

                var host =
                    new DirectInputCaptureHost();

                try
                {
                    host.Start(
                        Dispatcher,
                        hwnd,
                        captureKeyboard: true,
                        joystickDevices: joystickDevices);

                    cancellation.Token.ThrowIfCancellationRequested();

                    return host;
                }
                catch
                {
                    host.Dispose();
                    throw;
                }
            }, cancellation.Token);

            // Controls may have been unloaded or switched to another DataContext
            // while native DirectInput startup was still completing
            if (cancellation.IsCancellationRequested ||
                !IsLoaded ||
                !ReferenceEquals(DataContext, viewModel))
            {
                return;
            }

            captureHost.KeyboardInput +=
                CaptureSession_KeyboardInput;

            captureHost.JoystickButtonInput +=
                CaptureSession_JoystickButtonInput;

            captureHost.JoystickPovInput +=
                CaptureSession_JoystickPovInput;

            _captureHost = captureHost;
            captureHost = null;

            // Establish fresh axis baselines only after buffered capture is ready.
            // The 30 ms timer remains algorithm/UI-only; it never polls hardware.
            _lastInputAxisCaptureStartedUtc = DateTime.UtcNow;
            _lastInputAxisBaselineByDeviceKey.Clear();
            _lastInputAxisStableHitsByCandidate.Clear();

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };

            _timer.Tick += Timer_Tick;
            _timer.Start();
        }
        catch (OperationCanceledException)
        {
            // Normal when Controls unloads or another capture owner takes over
            // while this asynchronous startup is still in progress.
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "Controls buffered DirectInput capture start failed.");
        }
        finally
        {
            // If this host was never promoted to _captureHost, it belongs only
            // to this attempted startup and must be cleaned up here.
            if (captureHost is not null)
            {
                captureHost.Dispose();
            }

            if (gateEntered)
            {
                _captureStartGate.Release();
            }

            if (ReferenceEquals(
                    _captureStartCancellation,
                    cancellation))
            {
                _captureStartCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void StopKeyboardSearchCapture()
    {
        // Cancel a startup that may still be inside native DirectInput work.
        // DirectInput Start itself is not cancellable, so the local host will be
        // disposed as soon as that worker operation returns.
        CancellationTokenSource? captureStartCancellation =
            _captureStartCancellation;

        _captureStartCancellation = null;

        if (captureStartCancellation is not null)
        {
            try
            {
                captureStartCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
        }

        DirectInputCaptureHost? captureHost =
            _captureHost;

        _captureHost = null;

        if (captureHost is null)
            return;

        captureHost.KeyboardInput -=
            CaptureSession_KeyboardInput;

        captureHost.JoystickButtonInput -=
            CaptureSession_JoystickButtonInput;

        captureHost.JoystickPovInput -=
            CaptureSession_JoystickPovInput;

        captureHost.Dispose();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        // The timer remains only for the live axis bars and Last Input axis
        // stability algorithm. It no longer polls DirectInput hardware.
        PollLiveAxes();
    }

    private void CaptureSession_KeyboardInput(
        object? sender,
        BufferedKeyboardInputEventArgs e)
    {
        if (IsFilterControlFocused())
            return;

        if (!e.IsPressed)
            return;

        DiKey caught = e.Key;

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

        bool shift = (e.ModifierFlags & 1) != 0;
        bool ctrl = (e.ModifierFlags & 2) != 0;
        bool alt = (e.ModifierFlags & 4) != 0;

        string keyDisplayName = caught.ToString();

        if (shift)
            keyDisplayName = "Shift + " + keyDisplayName;

        if (ctrl)
            keyDisplayName = "Ctrl + " + keyDisplayName;

        if (alt)
            keyDisplayName = "Alt + " + keyDisplayName;

        UpdateLastInput("Keyboard", keyDisplayName);

        string assignmentStatus = KeyAssgn.GetKeyAssignmentStatus(
            "0x" + ((int)caught).ToString("X"),
            e.ModifierFlags,
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

        ScrollSelectedRowIntoView(
            viewModel,
            selectedMatch);
    }

    private void CaptureSession_JoystickButtonInput(
        object? sender,
        BufferedJoystickButtonEventArgs e)
    {
        if (IsFilterControlFocused())
            return;

        if (DataContext is not ControlsViewModel viewModel)
            return;

        DeviceBindingProfile? deviceProfile =
            viewModel.DeviceColumns.FirstOrDefault(device =>
                string.Equals(
                    device.DurableDeviceKey,
                    e.DeviceKey,
                    StringComparison.OrdinalIgnoreCase));

        if (deviceProfile is null)
            return;

        // Last Input reports the physical press. Releasing a button should
        // not replace the useful information the user just saw.
        if (e.IsPressed)
        {
            UpdateLastInput(
                GetDeviceDisplayName(deviceProfile),
                "DX" + (e.ButtonIndex + 1));
        }

        bool isShifted =
            viewModel.IsDxShiftActive(
                BuildCurrentButtonsByDeviceKey(viewModel));

        bool selectedMatch =
            viewModel.SelectFirstVisibleDxMatch(
                e.DeviceKey,
                e.ButtonIndex,
                isRelease: !e.IsPressed,
                isShifted);

        ScrollSelectedRowIntoView(
            viewModel,
            selectedMatch);
    }

    private void CaptureSession_JoystickPovInput(
        object? sender,
        BufferedJoystickPovEventArgs e)
    {
        if (IsFilterControlFocused())
            return;

        int? direction =
            e.Direction;

        // Returning a POV to center is not a mapping input
        if (!direction.HasValue)
            return;

        if (DataContext is not ControlsViewModel viewModel)
            return;

        DeviceBindingProfile? deviceProfile =
            viewModel.DeviceColumns.FirstOrDefault(device =>
                string.Equals(
                    device.DurableDeviceKey,
                    e.DeviceKey,
                    StringComparison.OrdinalIgnoreCase));

        if (deviceProfile is null)
            return;

        UpdateLastInput(
            GetDeviceDisplayName(deviceProfile),
            "POV" + (e.PovIndex + 1) + " " +
            ControlsViewModel.GetPovDirectionName(direction.Value));

        bool isShifted =
            viewModel.IsDxShiftActive(
                BuildCurrentButtonsByDeviceKey(viewModel));

        bool selectedMatch =
            viewModel.SelectFirstVisiblePovMatch(
                e.DeviceKey,
                e.PovIndex,
                direction.Value,
                isShifted);

        ScrollSelectedRowIntoView(
            viewModel,
            selectedMatch);
    }

    private Dictionary<string, bool[]> BuildCurrentButtonsByDeviceKey(
        ControlsViewModel viewModel)
    {
        var result =
            new Dictionary<string, bool[]>(
                StringComparer.OrdinalIgnoreCase);

        DirectInputCaptureHost? captureHost =
            _captureHost;

        if (captureHost is null)
            return result;

        foreach (DeviceBindingProfile deviceProfile in
                 viewModel.DeviceColumns.Where(device =>
                     device.IsConnected &&
                     device.ButtonCount > 0))
        {
            bool[] buttons =
                new bool[deviceProfile.ButtonCount];

            for (int buttonIndex = 0;
                 buttonIndex < buttons.Length;
                 buttonIndex++)
            {
                buttons[buttonIndex] =
                    captureHost.IsJoystickButtonPressed(
                        deviceProfile.DurableDeviceKey,
                        buttonIndex);
            }

            result[deviceProfile.DurableDeviceKey] =
                buttons;
        }

        return result;
    }

    private void ScrollSelectedRowIntoView(
        ControlsViewModel viewModel,
        bool selectedMatch)
    {
        if (!selectedMatch)
            return;

        ControlGridRowViewModel? selectedRow =
            viewModel.SelectedRow;

        if (selectedRow is null)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            // Filtering can change the visible rows before this deferred scroll runs.
            if (!viewModel.Rows.Contains(selectedRow))
                return;

            ControlsGrid.UpdateLayout();
            ControlsGrid.SelectedItem = selectedRow;
            ControlsGrid.ScrollIntoView(selectedRow);
        }, DispatcherPriority.Background);
    }

    private void PollLiveAxes()
    {
        if (DataContext is not ControlsViewModel viewModel)
            return;

        DirectInputCaptureHost? captureHost =
            _captureHost;

        if (captureHost is null)
            return;

        var lastInputCandidates =
            new List<LastInputAxisCandidate>();

        foreach (DeviceBindingProfile deviceProfile in
                 viewModel.DeviceColumns.Where(device =>
                     device.IsConnected &&
                     device.AxisCount > 0))
        {
            if (!captureHost.TryGetJoystickAxisValues(
                    deviceProfile.DurableDeviceKey,
                    out int[] axisValues))
            {
                continue;
            }

            // Reuse the capture session's current axis cache for both the live
            // mapping bars and Last Input. No DirectInput Poll() occurs here.
            AddLastInputAxisCandidates(
                deviceProfile,
                axisValues,
                lastInputCandidates);

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

        UpdateLastInputFromAxisCandidates(
            lastInputCandidates);
    }

    private void AddLastInputAxisCandidates(
        DeviceBindingProfile deviceProfile,
        int[] axisValues,
        List<LastInputAxisCandidate> candidates)
    {
        // For the first short period after polling starts, continually refresh
        // the baseline. This prevents startup state/noise from being mistaken
        // for intentional movement.
        if ((DateTime.UtcNow - _lastInputAxisCaptureStartedUtc).TotalMilliseconds <
            LastInputAxisSettleMs)
        {
            _lastInputAxisBaselineByDeviceKey[deviceProfile.DurableDeviceKey] =
                (int[])axisValues.Clone();

            return;
        }

        if (!_lastInputAxisBaselineByDeviceKey.TryGetValue(
                deviceProfile.DurableDeviceKey,
                out int[]? baseline))
        {
            _lastInputAxisBaselineByDeviceKey[deviceProfile.DurableDeviceKey] =
                (int[])axisValues.Clone();

            return;
        }

        int axisLimit = Math.Min(
            Math.Min(axisValues.Length, baseline.Length),
            Math.Max(0, deviceProfile.AxisCount));

        for (int axisIndex = 0;
             axisIndex < axisLimit;
             axisIndex++)
        {
            int delta = Math.Abs(
                axisValues[axisIndex] -
                baseline[axisIndex]);

            if (delta < LastInputAxisMovementThreshold)
                continue;

            candidates.Add(
                new LastInputAxisCandidate
                {
                    Device = deviceProfile,
                    AxisIndex = axisIndex,
                    CurrentValue = axisValues[axisIndex],
                    Delta = delta
                });
        }
    }

    private void UpdateLastInputFromAxisCandidates(
        List<LastInputAxisCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            _lastInputAxisStableHitsByCandidate.Clear();
            return;
        }

        LastInputAxisCandidate best =
            candidates
                .OrderByDescending(candidate => candidate.Delta)
                .First();

        LastInputAxisCandidate? secondBest =
            candidates
                .Where(candidate =>
                    !string.Equals(
                        candidate.CandidateKey,
                        best.CandidateKey,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.Delta)
                .FirstOrDefault();

        // Same principle used by axis assignment:
        // if two axes are moving by similar amounts, do not guess which one
        // the user intended to move.
        if (secondBest is not null &&
            best.Delta <
            secondBest.Delta * LastInputAxisDominantRatio)
        {
            _lastInputAxisStableHitsByCandidate.Clear();
            return;
        }

        foreach (string key in
                 _lastInputAxisStableHitsByCandidate.Keys.ToList())
        {
            if (!string.Equals(
                    key,
                    best.CandidateKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                _lastInputAxisStableHitsByCandidate[key] = 0;
            }
        }

        int stableHits =
            _lastInputAxisStableHitsByCandidate.TryGetValue(
                best.CandidateKey,
                out int currentHits)
                ? currentHits + 1
                : 1;

        _lastInputAxisStableHitsByCandidate[best.CandidateKey] =
            stableHits;

        if (stableHits < LastInputAxisStableHitsRequired)
            return;

        UpdateLastInput(
            GetDeviceDisplayName(best.Device),
            PhysicalAxisNameService.GetDisplayName(best.AxisIndex) +
            " Axis");

        // The movement has now been recognized. Move this axis reference
        // position to its current location so holding the control there does
        // not repeatedly trigger the same Last Input event.
        if (_lastInputAxisBaselineByDeviceKey.TryGetValue(
                best.Device.DurableDeviceKey,
                out int[]? baseline) &&
            best.AxisIndex >= 0 &&
            best.AxisIndex < baseline.Length)
        {
            baseline[best.AxisIndex] =
                best.CurrentValue;
        }

        _lastInputAxisStableHitsByCandidate.Clear();
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

        // Prevent WPF's ListBox text search from changing categories.
        // Buffered DirectInput capture still sees the key press and can jump
        // the table row.
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