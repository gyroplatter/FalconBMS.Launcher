using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// Converts WPF keyboard input into the same KeyAssgn display text used by the Controls table.
/// This keeps display formatting centralized in KeyAssgn instead of duplicating key-name logic.
/// </summary>
public static class KeyboardSearchAssgn
{
    private const uint MapVkToScanCode = 0;

    public static string FromKeyEvent(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.None || IsModifierOnlyKey(key))
            return "";

        int scancode = ToDirectInputScancode(key);
        if (scancode <= 0)
            return "";

        int modifierFlags = GetModifierFlags();

        return KeyAssgn.GetKeyAssignmentStatus(
            "0x" + scancode.ToString("X"),
            modifierFlags,
            "0",
            0);
    }

    private static int ToDirectInputScancode(Key key)
    {
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
            return 0;

        uint scanCode = MapVirtualKey((uint)virtualKey, MapVkToScanCode);
        if (scanCode == 0)
            scanCode = GetFallbackScancode(key);

        if (scanCode == 0)
            return 0;

        int directInputScancode = (int)scanCode;

        // DirectInput represents extended keys by adding 0x80 to the base scancode.
        // Example: NumPad 8 is 0x48, but keyboard UpArrow is 0xC8.
        if (IsExtendedKey(key))
            directInputScancode |= 0x80;

        return directInputScancode;
    }

    private static uint GetFallbackScancode(Key key)
    {
        return key switch
        {
            Key.PrintScreen => 0x37,
            Key.Pause => 0x45,
            _ => 0
        };
    }

    private static int GetModifierFlags()
    {
        int flags = 0;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            flags += 1;

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            flags += 2;

        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            flags += 4;

        return flags;
    }

    private static bool IsModifierOnlyKey(Key key)
    {
        return key == Key.LeftShift ||
               key == Key.RightShift ||
               key == Key.LeftCtrl ||
               key == Key.RightCtrl ||
               key == Key.LeftAlt ||
               key == Key.RightAlt;
    }

    private static bool IsExtendedKey(Key key)
    {
        return key == Key.Insert ||
               key == Key.Delete ||
               key == Key.Home ||
               key == Key.End ||
               key == Key.PageUp ||
               key == Key.PageDown ||
               key == Key.Up ||
               key == Key.Down ||
               key == Key.Left ||
               key == Key.Right ||
               key == Key.NumLock ||
               key == Key.Divide;
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
}