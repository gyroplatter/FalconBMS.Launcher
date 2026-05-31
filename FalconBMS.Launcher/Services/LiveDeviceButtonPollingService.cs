using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Shared live DirectInput button polling service.
///
/// This service only reports physical button transitions. It does not know about
/// Controls rows, Devices visuals, mappings, or UI behavior.
/// </summary>
public sealed class LiveDeviceButtonPollingService : IDisposable
{
    private readonly DirectInputManager _di = new();
    private readonly Dictionary<string, JoystickSession> _joystickSessionsByDeviceKey = new();
    private readonly Dictionary<string, bool[]> _previousButtonsByDeviceKey = new();

    public event EventHandler<LiveDeviceButtonStateChangedEventArgs>? ButtonStateChanged;

    public void Poll(IEnumerable<DeviceBindingProfile> deviceProfiles, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        List<DeviceBindingProfile> connectedDevices = deviceProfiles
            .Where(device => device.IsConnected && device.ButtonCount > 0)
            .ToList();

        RemoveSessionsForMissingDevices(connectedDevices);

        Dictionary<string, bool[]> currentButtonsByDeviceKey = new();

        // Read all requested devices first so consumers can evaluate the complete
        // current button snapshot, including DX-shift state.
        foreach (DeviceBindingProfile deviceProfile in connectedDevices)
        {
            JoystickSession? session = EnsureJoystickOpened(deviceProfile, hwnd);
            if (session is null)
                continue;

            try
            {
                bool[] buttons = session.ReadState().Buttons ?? Array.Empty<bool>();
                currentButtonsByDeviceKey[deviceProfile.DurableDeviceKey] = buttons;
            }
            catch
            {
                continue;
            }
        }

        foreach (DeviceBindingProfile deviceProfile in connectedDevices)
        {
            if (!currentButtonsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out bool[]? buttons))
                continue;

            if (!_previousButtonsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out bool[]? previousButtons))
            {
                _previousButtonsByDeviceKey[deviceProfile.DurableDeviceKey] = (bool[])buttons.Clone();
                continue;
            }

            int buttonLimit = Math.Min(buttons.Length, previousButtons.Length);

            for (int buttonIndex = 0; buttonIndex < buttonLimit; buttonIndex++)
            {
                bool wasPressed = previousButtons[buttonIndex];
                bool isPressed = buttons[buttonIndex];

                if (wasPressed == isPressed)
                    continue;

                ButtonStateChanged?.Invoke(
                    this,
                    new LiveDeviceButtonStateChangedEventArgs(
                        deviceProfile.DurableDeviceKey,
                        buttonIndex,
                        isPressed,
                        wasPressed && !isPressed,
                        currentButtonsByDeviceKey));

                // Keep the same behavior as the current Controls polling:
                // react to the first changed button per device per polling tick.
                break;
            }

            _previousButtonsByDeviceKey[deviceProfile.DurableDeviceKey] = (bool[])buttons.Clone();
        }
    }

    public void Reset()
    {
        foreach (JoystickSession session in _joystickSessionsByDeviceKey.Values)
            session.Dispose();

        _joystickSessionsByDeviceKey.Clear();
        _previousButtonsByDeviceKey.Clear();
    }

    public void Dispose()
    {
        Reset();
        _di.Dispose();
    }

    private JoystickSession? EnsureJoystickOpened(DeviceBindingProfile deviceProfile, IntPtr hwnd)
    {
        if (!deviceProfile.IsConnected)
            return null;

        if (_joystickSessionsByDeviceKey.TryGetValue(deviceProfile.DurableDeviceKey, out JoystickSession session))
            return session;

        try
        {
            session = _di.OpenJoystick(deviceProfile.InstanceGuid, hwnd);
            _joystickSessionsByDeviceKey[deviceProfile.DurableDeviceKey] = session;
            return session;
        }
        catch
        {
            return null;
        }
    }

    private void RemoveSessionsForMissingDevices(IReadOnlyCollection<DeviceBindingProfile> connectedDevices)
    {
        HashSet<string> connectedDeviceKeys = connectedDevices
            .Select(device => device.DurableDeviceKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> staleDeviceKeys = _joystickSessionsByDeviceKey.Keys
            .Where(deviceKey => !connectedDeviceKeys.Contains(deviceKey))
            .ToList();

        foreach (string staleDeviceKey in staleDeviceKeys)
        {
            if (_joystickSessionsByDeviceKey.TryGetValue(staleDeviceKey, out JoystickSession session))
                session.Dispose();

            _joystickSessionsByDeviceKey.Remove(staleDeviceKey);
            _previousButtonsByDeviceKey.Remove(staleDeviceKey);
        }
    }
}

public sealed class LiveDeviceButtonStateChangedEventArgs : EventArgs
{
    public string DurableDeviceKey { get; }
    public int ButtonIndex { get; }
    public bool IsPressed { get; }
    public bool IsRelease { get; }
    public IReadOnlyDictionary<string, bool[]> CurrentButtonsByDeviceKey { get; }

    public LiveDeviceButtonStateChangedEventArgs(
        string durableDeviceKey,
        int buttonIndex,
        bool isPressed,
        bool isRelease,
        IReadOnlyDictionary<string, bool[]> currentButtonsByDeviceKey)
    {
        DurableDeviceKey = durableDeviceKey;
        ButtonIndex = buttonIndex;
        IsPressed = isPressed;
        IsRelease = isRelease;
        CurrentButtonsByDeviceKey = currentButtonsByDeviceKey;
    }
}