using System;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// Static shared context used to hold the currently loaded joystick assignment data for keymapping workflows.
/// </summary>

public static class KeymappingContext
{
    public static JoyAssgnLite[] JoyAssgns { get; set; } = Array.Empty<JoyAssgnLite>();
    public static int RollJoyId { get; set; } = -1;
    public static int ThrottleJoyId { get; set; } = -1;
}