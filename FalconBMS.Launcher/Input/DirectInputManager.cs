using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using Vortice.DirectInput;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// Provides low-level DirectInput device discovery and keyboard access.
/// Responsible only for interacting with hardware and exposing normalized
/// device information (GUIDs, names, PID/VID). Contains no binding logic.
/// </summary>

public sealed class DirectInputManager : IDisposable
{
    private readonly IDirectInput8 _di;

    public DirectInputManager()
    {
        _di = DInput.DirectInput8Create();
    }

    public IReadOnlyList<InputDeviceInfo> DiscoverGameControllers()
    {
        var devices = _di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
        var results = new List<InputDeviceInfo>();

        for (int i = 0; i < devices.Count; i++)
        {
            DeviceInstance device = devices[i];

            string vendorIdHex = GetGuidWordHex(device.ProductGuid, wordIndex: 1);
            string productIdHex = GetGuidWordHex(device.ProductGuid, wordIndex: 0);

            results.Add(new InputDeviceInfo
            {
                DiscoveryIndex = i,
                InstanceGuid = device.InstanceGuid,
                ProductGuid = device.ProductGuid,
                InstanceName = device.InstanceName ?? "",
                ProductName = device.ProductName ?? "",
                VendorIdHex = vendorIdHex,
                ProductIdHex = productIdHex
            });
        }

        return results;
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

    private static string GetGuidWordHex(Guid guid, int wordIndex)
    {
        byte[] bytes = guid.ToByteArray();

        int offset = wordIndex * 2;
        ushort value = BitConverter.ToUInt16(bytes, offset);

        return value.ToString("X4");
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