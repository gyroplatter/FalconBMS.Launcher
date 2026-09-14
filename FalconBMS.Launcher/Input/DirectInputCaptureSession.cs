using FalconBMS.Launcher.Services;
using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Vortice.DirectInput;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// Owns one active V3 DirectInput capture session.
///
/// Device listeners receive buffered DirectInput data on their own worker
/// threads. This class provides the single WPF dispatcher boundary before
/// input reaches Controls or a mapping ViewModel.
/// </summary>
public sealed class DirectInputCaptureSession : IDisposable
{
    private readonly DirectInputManager _directInputManager;
    private readonly Dispatcher _dispatcher;
    private readonly IntPtr _hwnd;

    private KeyboardSession? _keyboardSession;

    private readonly Dictionary<string, JoystickSession>
        _joystickSessionsByDeviceKey =
            new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public event EventHandler<BufferedKeyboardInputEventArgs>? KeyboardInput;
    public event EventHandler<BufferedJoystickButtonEventArgs>? JoystickButtonInput;
    public event EventHandler<BufferedJoystickPovEventArgs>? JoystickPovInput;
    public event EventHandler<BufferedJoystickAxisEventArgs>? JoystickAxisInput;

    public DirectInputCaptureSession(
        DirectInputManager directInputManager,
        Dispatcher dispatcher,
        IntPtr hwnd)
    {
        _directInputManager =
            directInputManager ??
            throw new ArgumentNullException(nameof(directInputManager));

        _dispatcher =
            dispatcher ??
            throw new ArgumentNullException(nameof(dispatcher));

        _hwnd = hwnd;
    }

    public void OpenKeyboard()
    {
        ThrowIfDisposed();

        if (_keyboardSession is not null)
            return;

        KeyboardSession session =
            _directInputManager.OpenKeyboard(_hwnd);

        session.InputReceived += KeyboardSession_InputReceived;
        session.Faulted += KeyboardSession_Faulted;
        session.BufferCapacityReached += KeyboardSession_BufferCapacityReached;

        _keyboardSession = session;
    }

    public void OpenJoystick(
        string durableDeviceKey,
        Guid instanceGuid)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(durableDeviceKey))
            throw new ArgumentException(
                "Durable device key is required.",
                nameof(durableDeviceKey));

        if (_joystickSessionsByDeviceKey.ContainsKey(durableDeviceKey))
            return;

        JoystickSession session =
            _directInputManager.OpenJoystick(
                instanceGuid,
                _hwnd);

        session.ButtonChanged +=
            (buttonIndex, isPressed) =>
                JoystickSession_ButtonChanged(
                    durableDeviceKey,
                    buttonIndex,
                    isPressed);

        session.PovChanged +=
            (povIndex, value) =>
                JoystickSession_PovChanged(
                    durableDeviceKey,
                    povIndex,
                    value);

        session.AxisChanged +=
            (axis, value) =>
                JoystickSession_AxisChanged(
                    durableDeviceKey,
                    axis,
                    value);

        session.Faulted +=
            ex =>
                JoystickSession_Faulted(
                    durableDeviceKey,
                    instanceGuid,
                    ex);

        session.BufferCapacityReached +=
            count =>
                JoystickSession_BufferCapacityReached(
                    durableDeviceKey,
                    count);

        _joystickSessionsByDeviceKey.Add(
            durableDeviceKey,
            session);
    }

    public bool IsJoystickButtonPressed(
        string durableDeviceKey,
        int buttonIndex)
    {
        if (!_joystickSessionsByDeviceKey.TryGetValue(
                durableDeviceKey,
                out JoystickSession? session))
        {
            return false;
        }

        return session.IsButtonPressed(buttonIndex);
    }

    public bool TryGetJoystickAxisValues(
        string durableDeviceKey,
        out int[] axisValues)
    {
        if (_joystickSessionsByDeviceKey.TryGetValue(
                durableDeviceKey,
                out JoystickSession? session))
        {
            axisValues = session.GetAxisValues();
            return true;
        }

        axisValues = Array.Empty<int>();
        return false;
    }

    private void KeyboardSession_InputReceived(
        Key key,
        bool isPressed,
        int modifierFlags)
    {
        DispatchToUi(() =>
        {
            KeyboardInput?.Invoke(
                this,
                new BufferedKeyboardInputEventArgs(
                    key,
                    isPressed,
                    modifierFlags));
        });
    }

    private void JoystickSession_ButtonChanged(
        string durableDeviceKey,
        int buttonIndex,
        bool isPressed)
    {
        DispatchToUi(() =>
        {
            JoystickButtonInput?.Invoke(
                this,
                new BufferedJoystickButtonEventArgs(
                    durableDeviceKey,
                    buttonIndex,
                    isPressed));
        });
    }

    private void JoystickSession_PovChanged(
        string durableDeviceKey,
        int povIndex,
        int value)
    {
        DispatchToUi(() =>
        {
            JoystickPovInput?.Invoke(
                this,
                new BufferedJoystickPovEventArgs(
                    durableDeviceKey,
                    povIndex,
                    value));
        });
    }

    private void JoystickSession_AxisChanged(
        string durableDeviceKey,
        DirectInputAxis axis,
        int value)
    {
        DispatchToUi(() =>
        {
            JoystickAxisInput?.Invoke(
                this,
                new BufferedJoystickAxisEventArgs(
                    durableDeviceKey,
                    axis,
                    value));
        });
    }

    private static void KeyboardSession_Faulted(
        Exception ex)
    {
        DebugDiagnosticsService.Exception(
            ex,
            "Buffered DirectInput keyboard listener stopped");
    }

    private static void KeyboardSession_BufferCapacityReached(
        int count)
    {
        DebugDiagnosticsService.Warn(
            "Buffered DirectInput keyboard read reached the configured " +
            $"buffer capacity ({count} events). Input may have overflowed.");
    }

    private static void JoystickSession_Faulted(
        string durableDeviceKey,
        Guid instanceGuid,
        Exception ex)
    {
        DebugDiagnosticsService.Exception(
            ex,
            "Buffered DirectInput joystick listener stopped. " +
            $"DeviceKey={durableDeviceKey} InstanceGuid={instanceGuid}");
    }

    private static void JoystickSession_BufferCapacityReached(
        string durableDeviceKey,
        int count)
    {
        DebugDiagnosticsService.Warn(
            "Buffered DirectInput joystick read reached the configured " +
            $"buffer capacity ({count} events). Input may have overflowed. " +
            $"DeviceKey={durableDeviceKey}");
    }

    private void DispatchToUi(
        Action action)
    {
        if (_disposed)
            return;

        if (_dispatcher.CheckAccess())
        {
            if (!_disposed)
                action();

            return;
        }

        try
        {
            _dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    if (!_disposed)
                        action();
                }));
        }
        catch
        {
            // The Launcher may already be shutting down.
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(
                nameof(DirectInputCaptureSession));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_keyboardSession is not null)
        {
            _keyboardSession.InputReceived -=
                KeyboardSession_InputReceived;

            _keyboardSession.Faulted -=
                KeyboardSession_Faulted;

            _keyboardSession.BufferCapacityReached -=
                KeyboardSession_BufferCapacityReached;

            try
            {
                _keyboardSession.Dispose();
            }
            catch
            {
            }

            _keyboardSession = null;
        }

        foreach (JoystickSession session
                 in _joystickSessionsByDeviceKey.Values)
        {
            try
            {
                session.Dispose();
            }
            catch
            {
            }
        }

        _joystickSessionsByDeviceKey.Clear();
    }
}

public sealed class BufferedKeyboardInputEventArgs : EventArgs
{
    public Key Key { get; }
    public bool IsPressed { get; }
    public int ModifierFlags { get; }

    public BufferedKeyboardInputEventArgs(
        Key key,
        bool isPressed,
        int modifierFlags)
    {
        Key = key;
        IsPressed = isPressed;
        ModifierFlags = modifierFlags;
    }
}

public sealed class BufferedJoystickButtonEventArgs : EventArgs
{
    public string DeviceKey { get; }
    public int ButtonIndex { get; }
    public bool IsPressed { get; }

    public BufferedJoystickButtonEventArgs(
        string deviceKey,
        int buttonIndex,
        bool isPressed)
    {
        DeviceKey = deviceKey;
        ButtonIndex = buttonIndex;
        IsPressed = isPressed;
    }
}

public sealed class BufferedJoystickPovEventArgs : EventArgs
{
    public string DeviceKey { get; }
    public int PovIndex { get; }
    public int Value { get; }
    public int? Direction { get; }

    public BufferedJoystickPovEventArgs(
        string deviceKey,
        int povIndex,
        int value)
    {
        DeviceKey = deviceKey;
        PovIndex = povIndex;
        Value = value;
        Direction = NormalizeDirectInputPovDirection(value);
    }

    private static int? NormalizeDirectInputPovDirection(
        int povValue)
    {
        // DirectInput POV values are hundredths of a degree:
        // 0=Up, 9000=Right, 18000=Down, 27000=Left, -1=centered
        // BMS stores POV directions in 8-way slots:
        // 0=Up, 2=Right, 4=Down, 6=Left, with odd numbers as diagonals
        if (povValue < 0)
            return null;

        int normalizedDegrees =
            ((povValue / 100) + 360) % 360;

        int eightWayDirection =
            (int)Math.Round(normalizedDegrees / 45.0) % 8;

        return eightWayDirection;
    }
}

public sealed class BufferedJoystickAxisEventArgs : EventArgs
{
    public string DeviceKey { get; }
    public DirectInputAxis Axis { get; }
    public int Value { get; }

    public BufferedJoystickAxisEventArgs(
        string deviceKey,
        DirectInputAxis axis,
        int value)
    {
        DeviceKey = deviceKey;
        Axis = axis;
        Value = value;
    }
}