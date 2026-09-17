using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace FalconBMS.Launcher.Views;

public partial class DeviceMapEditorWindow : Window
{
    private readonly SemaphoreSlim _captureStartGate =
        new(1, 1);

    private DirectInputCaptureHost? _captureHost;

    private CancellationTokenSource? _captureStartCancellation;

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
         * outside the visible part of the list. Anchor that button into view 
         * so the user can immediately see what was pressed.
         */
        DeviceInputsListBox.ScrollIntoView(
            DeviceInputsListBox.SelectedItem);
    }
}