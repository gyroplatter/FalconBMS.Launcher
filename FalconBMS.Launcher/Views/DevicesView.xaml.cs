using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace FalconBMS.Launcher.Views;

public partial class DevicesView : UserControl
{
    /*
     * Devices owns capture only while this view is active.
     *
     * This follows the same ownership model as Controls:
     * DirectInputCaptureHost owns the shared buffered implementation,
     * while the active view owns only its capture lifetime.
     */
    private readonly SemaphoreSlim _captureStartGate =
        new(1, 1);

    private DirectInputCaptureHost? _captureHost;

    private CancellationTokenSource? _captureStartCancellation;

    public DevicesView()
    {
        InitializeComponent();

        Loaded +=
            DevicesView_Loaded;

        Unloaded +=
            DevicesView_Unloaded;

        DataContextChanged +=
            DevicesView_DataContextChanged;
    }

    private void DevicesView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        StartDirectInputCapture();
    }

    private void DevicesView_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        StopDirectInputCapture();
    }

    private void DevicesView_DataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        StartDirectInputCapture();
    }

    private async void StartDirectInputCapture()
    {
        StopDirectInputCapture();

        if (DataContext is not DevicesViewModel viewModel)
            return;

        Window? window =
            Window.GetWindow(this);

        if (window is null)
            return;

        IntPtr hwnd =
            new WindowInteropHelper(window).Handle;

        if (hwnd == IntPtr.Zero)
            return;

        /*
         * Capture all connected joystick-family devices.
         *
         * We need all of them rather than only the selected device because
         * DX Shift may physically live on another connected controller.
         */
        DirectInputCaptureDevice[] joystickDevices =
            viewModel.ConnectedDevices
                .Select(item =>
                    item.Device)
                .Where(device =>
                    device.IsConnected &&
                    (device.ButtonCount > 0 ||
                     device.PovCount > 0))
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
            // Normal when the Devices view unloads while capture is starting.
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "Devices buffered DirectInput capture start failed.");
        }
        finally
        {
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
        /*
         * Keep the last useful physical input visible.
         * Releasing the button should not immediately erase what the user saw.
         */
        if (!e.IsPressed)
            return;

        if (DataContext is not DevicesViewModel viewModel)
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
        /*
         * Direction == null means the POV returned to center.
         * Keep the previous direction visible instead of clearing it.
         */
        if (!e.Direction.HasValue)
            return;

        if (DataContext is not DevicesViewModel viewModel)
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
}