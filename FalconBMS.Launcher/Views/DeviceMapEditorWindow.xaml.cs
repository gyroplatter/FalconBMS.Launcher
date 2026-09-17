using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using static FalconBMS.Launcher.ViewModels.DeviceMapHotspotViewModel;

namespace FalconBMS.Launcher.Views;

public partial class DeviceMapEditorWindow : Window
{
    private readonly SemaphoreSlim _captureStartGate =
        new(1, 1);

    private DirectInputCaptureHost? _captureHost;

    private CancellationTokenSource? _captureStartCancellation;

    private DeviceMapHotspotViewModel? _draggedHotspot;
    private FrameworkElement? _dragElement;
    private Point _dragOffset;

    private DeviceMapCalloutViewModel? _draggedCallout;
    private FrameworkElement? _draggedCalloutElement;
    private Point _calloutDragOffset;

    private DeviceMapHotspotViewModel? _resizingHotspot;
    private FrameworkElement? _resizeElement;
    private DeviceMapHotspotResizeCorner _resizeCorner;
    private Point _resizeStartPoint;
    private double _resizeStartLeft;
    private double _resizeStartTop;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    public DeviceMapEditorWindow()
    {
        InitializeComponent();

        Loaded +=
            DeviceMapEditorWindow_Loaded;

        Closed +=
            DeviceMapEditorWindow_Closed;
    }

    private void DeviceMapEditorWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        StartDirectInputCapture();
    }

    private void DeviceMapEditorWindow_Closed(
        object? sender,
        EventArgs e)
    {
        StopDirectInputCapture();
    }

    private async void StartDirectInputCapture()
    {
        StopDirectInputCapture();

        if (DataContext is not DeviceMapEditorViewModel viewModel)
            return;

        IntPtr hwnd =
            new WindowInteropHelper(this).Handle;

        if (hwnd == IntPtr.Zero)
            return;

        DirectInputCaptureDevice[] joystickDevices =
            viewModel.GetCaptureDevices()
                .Select(device =>
                    new DirectInputCaptureDevice(
                        device.DurableDeviceKey,
                        device.InstanceGuid))
                .ToArray();

        var cancellation =
            new CancellationTokenSource();

        _captureStartCancellation =
            cancellation;

        bool gateEntered =
            false;

        DirectInputCaptureHost? captureHost =
            null;

        try
        {
            await _captureStartGate.WaitAsync(
                cancellation.Token);

            gateEntered =
                true;

            captureHost =
                await Task.Run(() =>
                {
                    cancellation.Token
                        .ThrowIfCancellationRequested();

                    var host =
                        new DirectInputCaptureHost();

                    try
                    {
                        host.Start(
                            Dispatcher,
                            hwnd,
                            captureKeyboard: false,
                            joystickDevices: joystickDevices);

                        cancellation.Token
                            .ThrowIfCancellationRequested();

                        return host;
                    }
                    catch
                    {
                        host.Dispose();
                        throw;
                    }
                },
                cancellation.Token);

            if (cancellation.IsCancellationRequested ||
                !IsLoaded ||
                !ReferenceEquals(
                    DataContext,
                    viewModel))
            {
                return;
            }

            captureHost.JoystickButtonInput +=
                CaptureHost_JoystickButtonInput;

            captureHost.JoystickPovInput +=
                CaptureHost_JoystickPovInput;

            _captureHost =
                captureHost;

            captureHost =
                null;
        }
        catch (OperationCanceledException)
        {
            // Normal when the editor closes while capture is starting.
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "Device map editor DirectInput capture start failed.");
        }
        finally
        {
            captureHost?.Dispose();

            if (gateEntered)
            {
                _captureStartGate.Release();
            }

            if (ReferenceEquals(
                    _captureStartCancellation,
                    cancellation))
            {
                _captureStartCancellation =
                    null;
            }

            cancellation.Dispose();
        }
    }

    private void StopDirectInputCapture()
    {
        CancellationTokenSource? cancellation =
            _captureStartCancellation;

        _captureStartCancellation =
            null;

        if (cancellation is not null)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        DirectInputCaptureHost? captureHost =
            _captureHost;

        _captureHost =
            null;

        if (captureHost is null)
            return;

        captureHost.JoystickButtonInput -=
            CaptureHost_JoystickButtonInput;

        captureHost.JoystickPovInput -=
            CaptureHost_JoystickPovInput;

        captureHost.Dispose();
    }

    private void CaptureHost_JoystickButtonInput(
        object? sender,
        BufferedJoystickButtonEventArgs e)
    {
        if (!e.IsPressed)
            return;

        if (DataContext is not DeviceMapEditorViewModel viewModel)
            return;

        bool isShifted =
            viewModel.IsDxShiftActive(
                IsJoystickButtonPressed);

        viewModel.ShowButtonInput(
            e.DeviceKey,
            e.ButtonIndex,
            isShifted);
    }

    private void CaptureHost_JoystickPovInput(
        object? sender,
        BufferedJoystickPovEventArgs e)
    {
        if (!e.Direction.HasValue)
            return;

        if (DataContext is not DeviceMapEditorViewModel viewModel)
            return;

        bool isShifted =
            viewModel.IsDxShiftActive(
                IsJoystickButtonPressed);

        viewModel.ShowPovInput(
            e.DeviceKey,
            e.PovIndex,
            e.Direction.Value,
            isShifted);
    }

    private bool IsJoystickButtonPressed(
        string deviceKey,
        int buttonIndex)
    {
        return _captureHost?.IsJoystickButtonPressed(
                   deviceKey,
                   buttonIndex) ??
               false;
    }

    private void DeviceInputsListBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DeviceInputsListBox.SelectedItem is null)
            return;

        /*
         * Physical DirectInput presses can select an input that is currently
         * outside the visible part of the list. Anchor that input into view so
         * the user can immediately see what was pressed.
         */
        DeviceInputsListBox.ScrollIntoView(
            DeviceInputsListBox.SelectedItem);
    }

    private void Hotspot_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not DeviceMapHotspotViewModel hotspot)
        {
            return;
        }

        if (DataContext is DeviceMapEditorViewModel viewModel)
        {
            viewModel.SelectInputForHotspot(
                hotspot);
        }

        Point mousePosition =
            e.GetPosition(
                HotspotItemsControl);

        _draggedHotspot =
            hotspot;

        _dragElement =
            element;

        _dragOffset =
            new Point(
                mousePosition.X - hotspot.Left,
                mousePosition.Y - hotspot.Top);

        element.CaptureMouse();

        e.Handled =
            true;
    }

    private void Hotspot_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_draggedHotspot is null ||
            _dragElement is null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point mousePosition =
            e.GetPosition(
                HotspotItemsControl);

        _draggedHotspot.MoveTo(
            mousePosition.X - _dragOffset.X,
            mousePosition.Y - _dragOffset.Y);

        if (DataContext is DeviceMapEditorViewModel viewModel)
        {
            viewModel.RefreshVisibleConnectorMetrics();
        }

        e.Handled =
            true;
    }

    private void Hotspot_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_dragElement is not null)
        {
            _dragElement.ReleaseMouseCapture();
        }

        _draggedHotspot =
            null;

        _dragElement =
            null;

        e.Handled =
            true;
    }

    private void HotspotResize_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not DeviceMapHotspotViewModel hotspot ||
            element.Tag is not string cornerName ||
            !Enum.TryParse(
                cornerName,
                out DeviceMapHotspotResizeCorner corner))
        {
            return;
        }

        if (DataContext is DeviceMapEditorViewModel viewModel)
        {
            viewModel.SelectInputForHotspot(
                hotspot);
        }

        _resizingHotspot =
            hotspot;

        _resizeElement =
            element;

        _resizeCorner =
            corner;

        _resizeStartPoint =
            e.GetPosition(
                HotspotItemsControl);

        _resizeStartLeft =
            hotspot.Left;

        _resizeStartTop =
            hotspot.Top;

        _resizeStartWidth =
            hotspot.DisplayWidth;

        _resizeStartHeight =
            hotspot.DisplayHeight;

        element.CaptureMouse();

        e.Handled =
            true;
    }

    private void HotspotResize_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_resizingHotspot is null ||
            _resizeElement is null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point mousePosition =
            e.GetPosition(
                HotspotItemsControl);

        double deltaX =
            mousePosition.X -
            _resizeStartPoint.X;

        double deltaY =
            mousePosition.Y -
            _resizeStartPoint.Y;

        bool keepAspectRatio =
            (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        _resizingHotspot.ResizeFromCorner(
            _resizeCorner,
            _resizeStartLeft,
            _resizeStartTop,
            _resizeStartWidth,
            _resizeStartHeight,
            deltaX,
            deltaY,
            keepAspectRatio);

        if (DataContext is DeviceMapEditorViewModel viewModel)
        {
            viewModel.RefreshVisibleConnectorMetrics();
        }

        e.Handled =
            true;
    }

    private void HotspotResize_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_resizeElement is not null)
        {
            _resizeElement.ReleaseMouseCapture();
        }

        _resizingHotspot =
            null;

        _resizeElement =
            null;

        e.Handled =
            true;
    }

    private void Callout_MouseLeftButtonDown(
    object sender,
    MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not DeviceMapCalloutViewModel callout)
        {
            return;
        }

        Point mousePosition =
            e.GetPosition(
                HotspotItemsControl);

        _draggedCallout =
            callout;

        _draggedCalloutElement =
            element;

        _calloutDragOffset =
            new Point(
                mousePosition.X - callout.Left,
                mousePosition.Y - callout.Top);

        element.CaptureMouse();

        e.Handled =
            true;
    }

    private void Callout_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_draggedCallout is null ||
            _draggedCalloutElement is null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point mousePosition =
            e.GetPosition(
                HotspotItemsControl);

        _draggedCallout.MoveTo(
            mousePosition.X - _calloutDragOffset.X,
            mousePosition.Y - _calloutDragOffset.Y);

        if (DataContext is DeviceMapEditorViewModel viewModel)
        {
            viewModel.RefreshVisibleConnectorMetrics();
        }

        e.Handled =
            true;
    }

    private void Callout_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_draggedCalloutElement is not null)
        {
            _draggedCalloutElement.ReleaseMouseCapture();
        }

        _draggedCallout =
            null;

        _draggedCalloutElement =
            null;

        e.Handled =
            true;
    }

}