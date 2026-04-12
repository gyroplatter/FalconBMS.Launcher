using FalconBMS.Launcher.Services;
using System;
using System.Collections.Generic;
using Vortice.DirectInput;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// Wrapper around DirectInput device enumeration and polling for joysticks and keyboard input capture.
/// </summary>
public sealed class DirectInputManager : IDisposable
{
    private readonly IDirectInput8 _di;

    public DirectInputManager()
    {
        _di = DInput.DirectInput8Create();
    }

    public sealed record DeviceInfo(string Name, Guid InstanceGuid, Guid ProductGuid);

    public IReadOnlyList<DeviceInfo> EnumerateDevices()
    {
        var devices = _di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);

        var list = new List<DeviceInfo>(devices.Count);
        foreach (var d in devices)
        {
            var name =
                (!string.IsNullOrWhiteSpace(d.ProductName) ? d.ProductName :
                 !string.IsNullOrWhiteSpace(d.InstanceName) ? d.InstanceName :
                 "Unknown");

            var info = new DeviceInfo(name, d.InstanceGuid, d.ProductGuid);
            list.Add(info);

            DebugDiagnosticsService.Info($"Found device: {info.Name}; pidvid {info.ProductGuid}; instance {info.InstanceGuid}");
        }

        return list;
    }

    public JoystickSession Open(Guid instanceGuid, IntPtr hwnd)
    {
        DebugDiagnosticsService.Info($"Opening joystick device: {instanceGuid}");

        var device = _di.CreateDevice(instanceGuid);

        device.SetCooperativeLevel(hwnd, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
        device.SetDataFormat<RawJoystickState>();
        device.Acquire();

        return new JoystickSession(device);
    }

    public KeyboardSession OpenKeyboard(IntPtr hwnd)
    {
        var keyboards = _di.GetDevices(DeviceClass.Keyboard, DeviceEnumerationFlags.AttachedOnly);
        if (keyboards.Count == 0)
        {
            DebugDiagnosticsService.Warn("No DirectInput keyboard devices found.");
            throw new InvalidOperationException("No DirectInput keyboard devices found.");
        }

        DebugDiagnosticsService.Info($"Opening keyboard device: {keyboards[0].InstanceGuid}");

        var device = _di.CreateDevice(keyboards[0].InstanceGuid);

        device.SetCooperativeLevel(hwnd, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
        device.SetDataFormat<RawKeyboardState>();
        device.Acquire();

        return new KeyboardSession(device);
    }

    public static int[] ReadAxisVector(JoystickState s)
    {
        int slider0 = (s.Sliders is { Length: > 0 }) ? s.Sliders[0] : 0;
        int slider1 = (s.Sliders is { Length: > 1 }) ? s.Sliders[1] : 0;

        return new[]
        {
            s.X, s.Y, s.Z,
            s.RotationX, s.RotationY, s.RotationZ,
            slider0, slider1
        };
    }

    public void Dispose()
    {
        _di.Dispose();
    }
}

public sealed class JoystickSession : IDisposable
{
    private readonly IDirectInputDevice8 _device;

    internal JoystickSession(IDirectInputDevice8 device)
    {
        _device = device;
    }

    public JoystickState ReadState()
    {
        _device.Poll();
        return _device.GetCurrentJoystickState();
    }

    public void Dispose()
    {
        try { _device.Unacquire(); } catch { }
        _device.Dispose();
    }
}

public sealed class KeyboardSession : IDisposable
{
    private readonly IDirectInputDevice8 _device;

    internal KeyboardSession(IDirectInputDevice8 device)
    {
        _device = device;
    }

    public KeyboardState ReadState()
    {
        _device.Poll();
        return _device.GetCurrentKeyboardState();
    }

    public void Dispose()
    {
        try { _device.Unacquire(); } catch { }
        _device.Dispose();
    }
}