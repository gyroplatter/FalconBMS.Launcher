using System;
using System.Collections.Generic;
using Vortice.DirectInput;

namespace FalconBMS.Launcher.Input;

public sealed class DirectInputManager : IDisposable
{
    private readonly IDirectInput8 _di;

    public DirectInputManager()
    {
        _di = DInput.DirectInput8Create();
    }

    public KeyboardSession OpenKeyboard(IntPtr hwnd)
    {
        var keyboards = _di.GetDevices(DeviceClass.Keyboard, DeviceEnumerationFlags.AttachedOnly);
        if (keyboards.Count == 0)
            throw new InvalidOperationException("No DirectInput keyboard devices found.");

        var device = _di.CreateDevice(keyboards[0].InstanceGuid);

        device.SetCooperativeLevel(hwnd, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
        device.SetDataFormat<RawKeyboardState>();
        device.Acquire();

        return new KeyboardSession(device);
    }

    public void Dispose()
    {
        _di.Dispose();
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