using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// Reusable owner for one buffered DirectInput capture lifecycle.
/// The DirectInput manager lives for the lifetime of the host, while each
/// start/stop cycle creates and disposes only the active capture session.
/// </summary>
public sealed class DirectInputCaptureHost : IDisposable
{
    private readonly DirectInputManager _directInputManager = new();

    private DirectInputCaptureSession? _captureSession;
    private bool _disposed;

    public event EventHandler<BufferedKeyboardInputEventArgs>? KeyboardInput;
    public event EventHandler<BufferedJoystickButtonEventArgs>? JoystickButtonInput;
    public event EventHandler<BufferedJoystickPovEventArgs>? JoystickPovInput;
    public event EventHandler<BufferedJoystickAxisEventArgs>? JoystickAxisInput;

    public void Start(
        Dispatcher dispatcher,
        IntPtr hwnd,
        bool captureKeyboard,
        IEnumerable<DirectInputCaptureDevice> joystickDevices)
    {
        ThrowIfDisposed();

        if (dispatcher is null)
            throw new ArgumentNullException(nameof(dispatcher));

        if (joystickDevices is null)
            throw new ArgumentNullException(nameof(joystickDevices));

        Stop();

        var captureSession =
            new DirectInputCaptureSession(
                _directInputManager,
                dispatcher,
                hwnd);

        captureSession.KeyboardInput +=
            CaptureSession_KeyboardInput;

        captureSession.JoystickButtonInput +=
            CaptureSession_JoystickButtonInput;

        captureSession.JoystickPovInput +=
            CaptureSession_JoystickPovInput;

        captureSession.JoystickAxisInput +=
            CaptureSession_JoystickAxisInput;

        _captureSession = captureSession;

        if (captureKeyboard)
        {
            try
            {
                captureSession.OpenKeyboard();
            }
            catch
            {
                // Keyboard failure must not prevent joystick capture.
            }
        }

        foreach (DirectInputCaptureDevice device in joystickDevices)
        {
            try
            {
                captureSession.OpenJoystick(
                    device.DeviceKey,
                    device.InstanceGuid);
            }
            catch
            {
                // One controller failing to open must not prevent capture
                // from the remaining devices.
            }
        }
    }

    public void Stop()
    {
        if (_captureSession is null)
            return;

        DirectInputCaptureSession captureSession =
            _captureSession;

        _captureSession = null;

        captureSession.KeyboardInput -=
            CaptureSession_KeyboardInput;

        captureSession.JoystickButtonInput -=
            CaptureSession_JoystickButtonInput;

        captureSession.JoystickPovInput -=
            CaptureSession_JoystickPovInput;

        captureSession.JoystickAxisInput -=
            CaptureSession_JoystickAxisInput;

        try
        {
            captureSession.Dispose();
        }
        catch
        {
        }
    }

    public bool IsJoystickButtonPressed(
        string durableDeviceKey,
        int buttonIndex)
    {
        return _captureSession?.IsJoystickButtonPressed(
                   durableDeviceKey,
                   buttonIndex) ??
               false;
    }

    public bool TryGetJoystickAxisValues(
        string durableDeviceKey,
        out int[] axisValues)
    {
        if (_captureSession is not null)
        {
            return _captureSession.TryGetJoystickAxisValues(
                durableDeviceKey,
                out axisValues);
        }

        axisValues = Array.Empty<int>();
        return false;
    }

    private void CaptureSession_KeyboardInput(
        object? sender,
        BufferedKeyboardInputEventArgs e)
    {
        KeyboardInput?.Invoke(
            this,
            e);
    }

    private void CaptureSession_JoystickButtonInput(
        object? sender,
        BufferedJoystickButtonEventArgs e)
    {
        JoystickButtonInput?.Invoke(
            this,
            e);
    }

    private void CaptureSession_JoystickPovInput(
        object? sender,
        BufferedJoystickPovEventArgs e)
    {
        JoystickPovInput?.Invoke(
            this,
            e);
    }

    private void CaptureSession_JoystickAxisInput(
        object? sender,
        BufferedJoystickAxisEventArgs e)
    {
        JoystickAxisInput?.Invoke(
            this,
            e);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(DirectInputCaptureHost));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _directInputManager.Dispose();
        _disposed = true;
    }
}

public sealed class DirectInputCaptureDevice
{
    public string DeviceKey { get; }
    public Guid InstanceGuid { get; }

    public DirectInputCaptureDevice(
        string deviceKey,
        Guid instanceGuid)
    {
        DeviceKey = deviceKey;
        InstanceGuid = instanceGuid;
    }
}