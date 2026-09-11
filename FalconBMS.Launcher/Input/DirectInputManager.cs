using FalconBMS.Launcher.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Vortice.DirectInput;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// Provides low-level DirectInput device discovery and keyboard access.
/// Responsible only for interacting with hardware and exposing normalized
/// device information (GUIDs, names, PID/VID). Contains no binding logic.
/// </summary>
/// 
public enum DirectInputAxis
{
    X = 0,
    Y = 1,
    Z = 2,
    RotationX = 3,
    RotationY = 4,
    RotationZ = 5,
    Slider0 = 6,
    Slider1 = 7
}

public sealed class DirectInputManager : IDisposable
{
    private readonly IDirectInput8 _di;

    // Reflection cache (performance + stability)
    private static MethodInfo? _getObjectsMethod;
    private static Type? _deviceObjectEnumType;

    public DirectInputManager()
    {
        _di = DInput.DirectInput8Create();
    }

    public IReadOnlyList<InputDeviceInfo> DiscoverGameControllers()
    {
        var devices = _di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
        var discovered = new List<InputDeviceInfo>();

        for (int i = 0; i < devices.Count; i++)
        {
            DeviceInstance device = devices[i];

            string vendorIdHex = GetGuidWordHex(device.ProductGuid, 1);
            string productIdHex = GetGuidWordHex(device.ProductGuid, 0);

            discovered.Add(new InputDeviceInfo
            {
                DiscoveryIndex = i,
                InstanceGuid = device.InstanceGuid,
                ProductGuid = device.ProductGuid,
                InstanceName = device.InstanceName ?? "",
                ProductName = device.ProductName ?? "",
                VendorIdHex = vendorIdHex,
                ProductIdHex = productIdHex,
                Capabilities = ReadCapabilities(device.InstanceGuid)
            });
        }

        return ApplyDuplicatePidVidSequencing(discovered);
    }

    public KeyboardSession OpenKeyboard(IntPtr hwnd)
    {
        var keyboards =
            _di.GetDevices(
                DeviceClass.Keyboard,
                DeviceEnumerationFlags.AttachedOnly);

        if (keyboards.Count == 0)
            throw new InvalidOperationException(
                "No DirectInput keyboard devices found.");

        var device =
            _di.CreateDevice(
                keyboards[0].InstanceGuid);

        device.SetCooperativeLevel(
            hwnd,
            CooperativeLevel.NonExclusive |
            CooperativeLevel.Background);

        device.SetDataFormat<RawKeyboardState>();

        // DirectInput's buffer preserves input events in order until the
        // listener consumes them, including rapid keyboard transitions and
        // other bursty input. A buffer of 128 provides room for fast encoders
        // and similar bursts.
        //
        // This must be configured before the device is acquired.
        device.Properties.BufferSize = 128;

        return new KeyboardSession(device);
    }

    public JoystickSession OpenJoystick(
        Guid instanceGuid,
        IntPtr hwnd)
    {
        var device =
            _di.CreateDevice(instanceGuid);

        device.SetCooperativeLevel(
            hwnd,
            CooperativeLevel.NonExclusive |
            CooperativeLevel.Background);

        device.SetDataFormat<RawJoystickState>();

        // DirectInput buffers button transitions, POV changes and axis changes
        // in order until the listener consumes them. They all share this
        // device buffer.
        device.Properties.BufferSize = 128;

        return new JoystickSession(device);
    }

    public static int[] ReadAxisVector(JoystickState state)
    {
        int slider0 = state.Sliders is { Length: > 0 } ? state.Sliders[0] : 0;
        int slider1 = state.Sliders is { Length: > 1 } ? state.Sliders[1] : 0;

        return new[]
        {
            state.X,
            state.Y,
            state.Z,
            state.RotationX,
            state.RotationY,
            state.RotationZ,
            slider0,
            slider1
        };
    }

    public void Dispose()
    {
        _di.Dispose();
    }

    // Capability Reading
    private InputDeviceCapabilities ReadCapabilities(Guid instanceGuid)
    {
        try
        {
            using IDirectInputDevice8 device = _di.CreateDevice(instanceGuid);

            object? caps = GetCapabilitiesObject(device);
            if (caps is null)
                return InputDeviceCapabilities.Unknown;

            EnsureObjectEnumReflection(device);

            int axisCount =
                CountObjectsByFlag(device, "Axis") +
                CountObjectsByFlag(device, "Axes") +
                CountObjectsByFlag(device, "Slider") +
                CountObjectsByFlag(device, "Sliders");

            int buttonCount = ReadIntMember(caps, "ButtonsCount", "ButtonCount", "Buttons");
            int povCount = ReadIntMember(caps, "PovsCount", "POVsCount", "PovCount", "Povs", "POVs");

            return new InputDeviceCapabilities
            {
                AxisCount = axisCount,
                ButtonCount = buttonCount,
                PovCount = povCount,
                WasReadSuccessfully = true
            };
        }
        catch
        {
            return InputDeviceCapabilities.Unknown;
        }
    }

    private static object? GetCapabilitiesObject(IDirectInputDevice8 device)
    {
        Type deviceType = device.GetType();

        PropertyInfo? property = deviceType.GetProperty("Capabilities", BindingFlags.Instance | BindingFlags.Public);
        if (property != null)
            return property.GetValue(device);

        MethodInfo? method = deviceType.GetMethod("GetCapabilities", BindingFlags.Instance | BindingFlags.Public);
        return method?.Invoke(device, Array.Empty<object>());
    }

    // Axis Enumeration (correct DirectInput handling)
    private static void EnsureObjectEnumReflection(IDirectInputDevice8 device)
    {
        if (_getObjectsMethod != null)
            return;

        Type deviceType = device.GetType();

        _getObjectsMethod = deviceType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m =>
                (m.Name == "GetObjects" || m.Name == "EnumObjects") &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.IsEnum);

        if (_getObjectsMethod != null)
            _deviceObjectEnumType = _getObjectsMethod.GetParameters()[0].ParameterType;
    }

    private static int CountObjectsByFlag(IDirectInputDevice8 device, string flagName)
    {
        if (_getObjectsMethod == null || _deviceObjectEnumType == null)
            return 0;

        object flag;

        try
        {
            flag = Enum.Parse(_deviceObjectEnumType, flagName);
        }
        catch
        {
            return 0;
        }

        object? result = _getObjectsMethod.Invoke(device, new[] { flag });

        if (result is not IEnumerable enumerable)
            return 0;

        int count = 0;

        foreach (var _ in enumerable)
            count++;

        return count;
    }

    // Reflection helpers
    private static int ReadIntMember(object instance, params string[] memberNames)
    {
        Type type = instance.GetType();

        foreach (string name in memberNames)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null)
            {
                object? value = property.GetValue(instance);
                if (TryConvertToInt(value, out int intValue))
                    return intValue;
            }

            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                object? value = field.GetValue(instance);
                if (TryConvertToInt(value, out int intValue))
                    return intValue;
            }
        }

        return 0;
    }

    private static bool TryConvertToInt(object? value, out int result)
    {
        if (value is int i) { result = i; return true; }
        if (value is uint ui) { result = unchecked((int)ui); return true; }
        if (value is short s) { result = s; return true; }
        if (value is ushort us) { result = us; return true; }

        result = 0;
        return false;
    }

     // Helpers
    private static string GetGuidWordHex(Guid guid, int wordIndex)
    {
        byte[] bytes = guid.ToByteArray();
        ushort value = BitConverter.ToUInt16(bytes, wordIndex * 2);
        return value.ToString("X4");
    }

    private IReadOnlyList<InputDeviceInfo> ApplyDuplicatePidVidSequencing(IReadOnlyList<InputDeviceInfo> devices)
    {
        var duplicates = devices
            .GroupBy(d => d.PidVid, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        if (duplicates.Count == 0)
            return devices;

        var result = new List<InputDeviceInfo>();

        foreach (var device in devices)
        {
            if (!duplicates.TryGetValue(device.PidVid, out var group))
            {
                result.Add(device);
                continue;
            }

            int seq = group
                .OrderBy(d => d.DiscoveryIndex)
                .Select((d, i) => new { d, seq = i + 1 })
                .First(x => x.d.DiscoveryIndex == device.DiscoveryIndex)
                .seq;

            result.Add(new InputDeviceInfo
            {
                DiscoveryIndex = device.DiscoveryIndex,
                InstanceGuid = device.InstanceGuid,
                ProductGuid = device.ProductGuid,
                InstanceName = device.InstanceName,
                ProductName = device.ProductName,
                VendorIdHex = device.VendorIdHex,
                ProductIdHex = device.ProductIdHex,
                DuplicatePidVidSequenceNumber = seq,
                Capabilities = device.Capabilities
            });
        }

        return result;
    }
}


public sealed class KeyboardSession : IDisposable
{
    private const int ConfiguredBufferSize = 128;
    private const int MaximumBufferedEvents = ConfiguredBufferSize - 1;

    private readonly IDirectInputDevice8 _device;

    private readonly AutoResetEvent _dataAvailable =
        new(false);

    private readonly ManualResetEvent _stopRequested =
        new(false);

    private readonly Thread _listenerThread;

    private bool _leftShiftPressed;
    private bool _rightShiftPressed;
    private bool _leftControlPressed;
    private bool _rightControlPressed;
    private bool _leftAltPressed;
    private bool _rightAltPressed;

    private bool _disposed;

    public event Action<Key, bool, int>? InputReceived;
    public event Action<Exception>? Faulted;
    public event Action<int>? BufferCapacityReached;

    internal KeyboardSession(
        IDirectInputDevice8 device)
    {
        _device = device;

        _device.SetEventNotification(
            _dataAvailable);

        _device.Acquire();

        _listenerThread =
            new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "DirectInput Keyboard"
            };

        _listenerThread.Start();
    }

    private void ListenLoop()
    {
        WaitHandle[] waitHandles =
        {
            _stopRequested,
            _dataAvailable
        };

        while (!_disposed)
        {
            int signaled =
                WaitHandle.WaitAny(waitHandles);

            if (signaled == 0 || _disposed)
                return;

            try
            {
                DrainBufferedInput();
            }
            catch (Exception ex)
            {
                if (!_disposed)
                    Faulted?.Invoke(ex);

                return;
            }
        }
    }

    private void DrainBufferedInput()
    {
        KeyboardUpdate[] updates =
            _device.GetBufferedKeyboardData();

        if (updates.Length >= MaximumBufferedEvents)
            BufferCapacityReached?.Invoke(updates.Length);

        foreach (KeyboardUpdate update in updates)
        {
            Key key = update.Key;

            if (key == Key.Unknown)
                continue;

            UpdateModifierState(
                key,
                update.IsPressed);

            InputReceived?.Invoke(
                key,
                update.IsPressed,
                GetModifierFlags());
        }
    }

    private void UpdateModifierState(
        Key key,
        bool isPressed)
    {
        switch (key)
        {
            case Key.LeftShift:
                _leftShiftPressed = isPressed;
                break;

            case Key.RightShift:
                _rightShiftPressed = isPressed;
                break;

            case Key.LeftControl:
                _leftControlPressed = isPressed;
                break;

            case Key.RightControl:
                _rightControlPressed = isPressed;
                break;

            case Key.LeftAlt:
                _leftAltPressed = isPressed;
                break;

            case Key.RightAlt:
                _rightAltPressed = isPressed;
                break;
        }
    }

    private int GetModifierFlags()
    {
        int modifierFlags = 0;

        if (_leftShiftPressed ||
            _rightShiftPressed)
        {
            modifierFlags |= 1;
        }

        if (_leftControlPressed ||
            _rightControlPressed)
        {
            modifierFlags |= 2;
        }

        if (_leftAltPressed ||
            _rightAltPressed)
        {
            modifierFlags |= 4;
        }

        return modifierFlags;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _stopRequested.Set();

        try
        {
            _device.SetEventNotification(null);
        }
        catch
        {
        }

        if (Thread.CurrentThread != _listenerThread)
        {
            try
            {
                _listenerThread.Join();
            }
            catch
            {
            }
        }

        try
        {
            _device.Unacquire();
        }
        catch
        {
        }

        _device.Dispose();

        _dataAvailable.Dispose();
        _stopRequested.Dispose();
    }
}

public sealed class JoystickSession : IDisposable
{
    private const int ConfiguredBufferSize = 128;
    private const int MaximumBufferedEvents = ConfiguredBufferSize - 1;

    private const int Pov0Offset = 32;
    private const int Pov3Offset = 44;

    private const int Button0Offset = 48;
    private const int Button127Offset = 175;

    private readonly IDirectInputDevice8 _device;

    private readonly AutoResetEvent _dataAvailable =
        new(false);

    private readonly ManualResetEvent _stopRequested =
        new(false);

    private readonly Thread _listenerThread;

    private readonly object _stateSync =
        new();

    private readonly int[] _axisValues =
        new int[8];

    private readonly bool[] _buttonStates =
        new bool[128];

    private bool _disposed;

    public event Action<int, bool>? ButtonChanged;
    public event Action<int, int>? PovChanged;
    public event Action<DirectInputAxis, int>? AxisChanged;
    public event Action<Exception>? Faulted;
    public event Action<int>? BufferCapacityReached;

    internal JoystickSession(
        IDirectInputDevice8 device)
    {
        _device = device;

        _device.SetEventNotification(
            _dataAvailable);

        _device.Acquire();

        SeedInitialState();

        _listenerThread =
            new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "DirectInput Joystick"
            };

        _listenerThread.Start();
    }

    private void SeedInitialState()
    {
        // Some DirectInput joystick-family devices require Poll() before
        // immediate state is current. This is the one allowed startup poll:
        // it seeds the persistent axis/button state and is never used for
        // periodic input detection.
        try
        {
            _device.Poll();
        }
        catch
        {
            // GetCurrentJoystickState may still succeed for devices that
            // do not require explicit polling.
        }

        JoystickState state =
            _device.GetCurrentJoystickState();

        int[] axisValues =
            DirectInputManager.ReadAxisVector(state);

        bool[] buttons =
            state.Buttons ??
            Array.Empty<bool>();

        lock (_stateSync)
        {
            for (int i = 0;
                 i < _axisValues.Length &&
                 i < axisValues.Length;
                 i++)
            {
                _axisValues[i] =
                    axisValues[i];
            }

            int buttonCount =
                Math.Min(
                    _buttonStates.Length,
                    buttons.Length);

            for (int i = 0;
                 i < buttonCount;
                 i++)
            {
                _buttonStates[i] =
                    buttons[i];
            }
        }

        // The startup snapshot intentionally emits no events.
    }

    private void ListenLoop()
    {
        WaitHandle[] waitHandles =
        {
            _stopRequested,
            _dataAvailable
        };

        while (!_disposed)
        {
            int signaled =
                WaitHandle.WaitAny(waitHandles);

            if (signaled == 0 || _disposed)
                return;

            try
            {
                DrainBufferedInput();
            }
            catch (Exception ex)
            {
                if (!_disposed)
                    Faulted?.Invoke(ex);

                return;
            }
        }
    }

    private void DrainBufferedInput()
    {
        JoystickUpdate[] updates =
            _device.GetBufferedJoystickData();

        if (updates.Length >= MaximumBufferedEvents)
            BufferCapacityReached?.Invoke(updates.Length);

        foreach (JoystickUpdate update in updates)
        {
            int rawOffset =
                update.RawOffset;

            if (TryGetAxis(
                    rawOffset,
                    out DirectInputAxis axis))
            {
                lock (_stateSync)
                {
                    _axisValues[(int)axis] =
                        update.Value;
                }

                AxisChanged?.Invoke(
                    axis,
                    update.Value);

                continue;
            }

            if (rawOffset >= Pov0Offset &&
                rawOffset <= Pov3Offset &&
                (rawOffset - Pov0Offset) % 4 == 0)
            {
                int povIndex =
                    (rawOffset - Pov0Offset) / 4;

                PovChanged?.Invoke(
                    povIndex,
                    update.Value);

                continue;
            }

            if (rawOffset >= Button0Offset &&
                rawOffset <= Button127Offset)
            {
                int buttonIndex =
                    rawOffset - Button0Offset;

                bool isPressed =
                    (update.Value & 0x80) != 0;

                lock (_stateSync)
                {
                    _buttonStates[buttonIndex] =
                        isPressed;
                }

                ButtonChanged?.Invoke(
                    buttonIndex,
                    isPressed);
            }
        }
    }

    private static bool TryGetAxis(
        int rawOffset,
        out DirectInputAxis axis)
    {
        switch (rawOffset)
        {
            case 0:
                axis = DirectInputAxis.X;
                return true;

            case 4:
                axis = DirectInputAxis.Y;
                return true;

            case 8:
                axis = DirectInputAxis.Z;
                return true;

            case 12:
                axis = DirectInputAxis.RotationX;
                return true;

            case 16:
                axis = DirectInputAxis.RotationY;
                return true;

            case 20:
                axis = DirectInputAxis.RotationZ;
                return true;

            case 24:
                axis = DirectInputAxis.Slider0;
                return true;

            case 28:
                axis = DirectInputAxis.Slider1;
                return true;

            default:
                axis = default;
                return false;
        }
    }

    public int[] GetAxisValues()
    {
        lock (_stateSync)
        {
            return (int[])_axisValues.Clone();
        }
    }

    public bool IsButtonPressed(
        int buttonIndex)
    {
        if (buttonIndex < 0 ||
            buttonIndex >= _buttonStates.Length)
        {
            return false;
        }

        lock (_stateSync)
        {
            return _buttonStates[buttonIndex];
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _stopRequested.Set();

        try
        {
            _device.SetEventNotification(null);
        }
        catch
        {
        }

        if (Thread.CurrentThread != _listenerThread)
        {
            try
            {
                _listenerThread.Join();
            }
            catch
            {
            }
        }

        try
        {
            _device.Unacquire();
        }
        catch
        {
        }

        _device.Dispose();

        _dataAvailable.Dispose();
        _stopRequested.Dispose();
    }
}