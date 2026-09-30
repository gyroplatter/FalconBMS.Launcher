using System;
using Vortice.DirectInput;

namespace FalconBMS.Launcher.Input;

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

        Direction =
            NormalizeDirectInputPovDirection(value);
    }

    private static int? NormalizeDirectInputPovDirection(
        int povValue)
    {
        // DirectInput stores POV values in hundredths of a degree.
        // BMS uses eight directions numbered 0 through 7.
        if (povValue < 0)
            return null;

        int normalizedDegrees =
            ((povValue / 100) + 360) % 360;

        return
            (int)Math.Round(
                normalizedDegrees / 45.0) % 8;
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