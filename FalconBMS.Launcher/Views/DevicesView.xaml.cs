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
     * The map editor gets exclusive capture while its modal window is open,
     * then this view resumes capture when the editor closes.
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
        SubscribeToViewModel();
        StartDirectInputCapture();
    }

    private void DevicesView_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        UnsubscribeFromViewModel();
        StopDirectInputCapture();
    }

    private void DevicesView_DataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DevicesViewModel oldViewModel)
        {
            oldViewModel.MapEditorRequested -=
                ViewModel_MapEditorRequested;

            oldViewModel.DeleteMapRequested -=
                ViewModel_DeleteMapRequested;
        }

        if (!IsLoaded)
            return;

        SubscribeToViewModel();
        StartDirectInputCapture();
    }

    private void SubscribeToViewModel()
    {
        if (DataContext is not DevicesViewModel viewModel)
            return;

        viewModel.MapEditorRequested -=
            ViewModel_MapEditorRequested;

        viewModel.MapEditorRequested +=
            ViewModel_MapEditorRequested;

        viewModel.DeleteMapRequested -=
            ViewModel_DeleteMapRequested;

        viewModel.DeleteMapRequested +=
            ViewModel_DeleteMapRequested;
    }

    private void UnsubscribeFromViewModel()
    {
        if (DataContext is not DevicesViewModel viewModel)
            return;

        viewModel.MapEditorRequested -=
            ViewModel_MapEditorRequested;

        viewModel.DeleteMapRequested -=
            ViewModel_DeleteMapRequested;
    }

    private void ViewModel_DeleteMapRequested(
        object? sender,
        EventArgs e)
    {
        if (sender is not DevicesViewModel viewModel)
            return;

        DeviceBindingProfile? device =
            viewModel.SelectedDevice;

        if (device is null)
            return;

        Window? owner =
            Window.GetWindow(this);

        MessageBoxResult result =
            MessageBox.Show(
                owner,
                $"Delete the user map for {device.ProductName}?\n\n" +
                "Launcher stock maps will not be deleted.",
                "Delete Device Map",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
            return;

        viewModel.DeleteSelectedUserMap();
    }

    private void ViewModel_MapEditorRequested(
        object? sender,
        DeviceMapEditorRequestEventArgs e)
    {
        if (sender is not DevicesViewModel devicesViewModel)
            return;

        StopDirectInputCapture();

        Window? owner =
            Window.GetWindow(this);

        var editorWindow =
            new DeviceMapEditorWindow
            {
                Owner =
                    owner
            };

        editorWindow.DataContext =
            new DeviceMapEditorViewModel(
                e.BindingModel,
                e.SelectedProfile,
                e.Device,
                e.BaseDir,
                e.IsEditMode,
                getOwnerWindow: () => editorWindow,
                closeWindow: result =>
                {
                    if (result.HasValue)
                    {
                        editorWindow.DialogResult =
                            result.Value;
                    }
                    else
                    {
                        editorWindow.Close();
                    }
                });

        bool? result =
            null;

        try
        {
            using (MainWindow.BeginModalOverlay(owner))
            {
                result =
                    editorWindow.ShowDialog();
            }
        }
        finally
        {
            if (result == true)
            {
                devicesViewModel.RefreshDeviceMapState();
            }

            StartDirectInputCapture();
        }
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