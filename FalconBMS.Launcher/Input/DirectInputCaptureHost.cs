using FalconBMS.Launcher.Services;
using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Vortice.DirectInput;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// One capture window's subscriptions to persistent DirectInput.
///
/// The host never creates, acquires, or disposes hardware.
/// Stopping capture only releases its subscriptions.
/// </summary>
public sealed class DirectInputCaptureHost : IDisposable
{
    private readonly Dictionary<string, JoystickCapture>
        _joysticks =
            new(StringComparer.OrdinalIgnoreCase);

    private PersistentDirectInputManager.ListenerLease<KeyboardSession>?
        _keyboardLease;

    private Action<Key, bool, int>? _keyboardHandler;

    private Dispatcher? _dispatcher;

    private int _generation;

    private bool _disposed;

    public event EventHandler<BufferedKeyboardInputEventArgs>?
        KeyboardInput;

    public event EventHandler<BufferedJoystickButtonEventArgs>?
        JoystickButtonInput;

    public event EventHandler<BufferedJoystickPovEventArgs>?
        JoystickPovInput;

    public event EventHandler<BufferedJoystickAxisEventArgs>?
        JoystickAxisInput;

    /// <summary>
    /// Keeps one joystick and its exact event handlers together.
    /// This makes unsubscription deterministic when capture stops.
    /// </summary>
    private sealed class JoystickCapture : IDisposable
    {
        private readonly PersistentDirectInputManager
            .ListenerLease<JoystickSession> _lease;

        private readonly Action<int, bool> _buttonHandler;
        private readonly Action<int, int> _povHandler;
        private readonly Action<DirectInputAxis, int> _axisHandler;

        public JoystickSession Session => _lease.Session;

        public JoystickCapture(
            PersistentDirectInputManager
                .ListenerLease<JoystickSession> lease,
            Action<int, bool> buttonHandler,
            Action<int, int> povHandler,
            Action<DirectInputAxis, int> axisHandler)
        {
            _lease = lease;

            _buttonHandler = buttonHandler;
            _povHandler = povHandler;
            _axisHandler = axisHandler;

            Session.ButtonChanged += _buttonHandler;
            Session.PovChanged += _povHandler;
            Session.AxisChanged += _axisHandler;
        }

        public void Dispose()
        {
            Session.ButtonChanged -= _buttonHandler;
            Session.PovChanged -= _povHandler;
            Session.AxisChanged -= _axisHandler;

            _lease.Dispose();
        }
    }

    public void Start(
        Dispatcher dispatcher,
        bool captureKeyboard,
        IEnumerable<DirectInputCaptureDevice> joystickDevices)
    {
        ThrowIfDisposed();

        if (dispatcher is null)
            throw new ArgumentNullException(nameof(dispatcher));

        if (joystickDevices is null)
            throw new ArgumentNullException(nameof(joystickDevices));

        Stop();

        _dispatcher = dispatcher;

        int generation = ++_generation;

        PersistentDirectInputManager manager =
            PersistentDirectInputManager.Current;

        if (captureKeyboard)
        {
            try
            {
                _keyboardLease =
                    manager.AcquireKeyboard();

                _keyboardHandler =
                    (key, isPressed, modifierFlags) =>
                    {
                        DispatchToUi(
                            generation,
                            () =>
                            {
                                KeyboardInput?.Invoke(
                                    this,
                                    new BufferedKeyboardInputEventArgs(
                                        key,
                                        isPressed,
                                        modifierFlags));
                            });
                    };

                _keyboardLease.Session.InputReceived +=
                    _keyboardHandler;
            }
            catch (Exception ex)
            {
                DebugDiagnosticsService.Exception(
                    ex,
                    "Keyboard capture subscription failed.");
            }
        }

        foreach (DirectInputCaptureDevice device in joystickDevices)
        {
            if (string.IsNullOrWhiteSpace(device.DeviceKey) ||
                device.InstanceGuid == Guid.Empty ||
                _joysticks.ContainsKey(device.DeviceKey))
            {
                continue;
            }

            PersistentDirectInputManager
                .ListenerLease<JoystickSession>? lease = null;

            try
            {
                lease =
                    manager.AcquireJoystick(
                        device.InstanceGuid);

                string deviceKey = device.DeviceKey;

                Action<int, bool> buttonHandler =
                    (buttonIndex, isPressed) =>
                    {
                        DispatchToUi(
                            generation,
                            () =>
                            {
                                JoystickButtonInput?.Invoke(
                                    this,
                                    new BufferedJoystickButtonEventArgs(
                                        deviceKey,
                                        buttonIndex,
                                        isPressed));
                            });
                    };

                Action<int, int> povHandler =
                    (povIndex, value) =>
                    {
                        DispatchToUi(
                            generation,
                            () =>
                            {
                                JoystickPovInput?.Invoke(
                                    this,
                                    new BufferedJoystickPovEventArgs(
                                        deviceKey,
                                        povIndex,
                                        value));
                            });
                    };

                Action<DirectInputAxis, int> axisHandler =
                    (axis, value) =>
                    {
                        DispatchToUi(
                            generation,
                            () =>
                            {
                                JoystickAxisInput?.Invoke(
                                    this,
                                    new BufferedJoystickAxisEventArgs(
                                        deviceKey,
                                        axis,
                                        value));
                            });
                    };

                _joysticks.Add(
                    deviceKey,
                    new JoystickCapture(
                        lease,
                        buttonHandler,
                        povHandler,
                        axisHandler));
            }
            catch (Exception ex)
            {
                lease?.Dispose();

                DebugDiagnosticsService.Exception(
                    ex,
                    "Joystick capture subscription failed. " +
                    $"DeviceKey={device.DeviceKey} " +
                    $"InstanceGuid={device.InstanceGuid}");
            }
        }
    }

    public void Stop()
    {
        // Invalidate events already queued to the dispatcher.
        // They cannot reach a newly opened capture session.
        ++_generation;

        if (_keyboardLease is not null)
        {
            if (_keyboardHandler is not null)
            {
                _keyboardLease.Session.InputReceived -=
                    _keyboardHandler;
            }

            _keyboardLease.Dispose();

            _keyboardLease = null;
            _keyboardHandler = null;
        }

        foreach (JoystickCapture capture in _joysticks.Values)
        {
            capture.Dispose();
        }

        _joysticks.Clear();

        _dispatcher = null;
    }

    public bool IsJoystickButtonPressed(
        string durableDeviceKey,
        int buttonIndex)
    {
        return _joysticks.TryGetValue(
                   durableDeviceKey,
                   out JoystickCapture? capture) &&
               capture.Session.IsButtonPressed(buttonIndex);
    }

    public bool TryGetJoystickAxisValues(
        string durableDeviceKey,
        out int[] axisValues)
    {
        if (_joysticks.TryGetValue(
                durableDeviceKey,
                out JoystickCapture? capture))
        {
            axisValues =
                capture.Session.GetAxisValues();

            return true;
        }

        axisValues = Array.Empty<int>();

        return false;
    }

    private void DispatchToUi(
        int generation,
        Action action)
    {
        Dispatcher? dispatcher = _dispatcher;

        if (_disposed || dispatcher is null)
            return;

        if (dispatcher.CheckAccess())
        {
            if (!_disposed &&
                generation == _generation)
            {
                action();
            }

            return;
        }

        try
        {
            dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    if (!_disposed &&
                        generation == _generation)
                    {
                        action();
                    }
                }));
        }
        catch
        {
            // WPF may already be shutting down.
        }
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