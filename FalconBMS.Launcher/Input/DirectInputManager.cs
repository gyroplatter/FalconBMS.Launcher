using FalconBMS.Launcher.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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