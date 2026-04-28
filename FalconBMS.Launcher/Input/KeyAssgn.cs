using System;
using Vortice.DirectInput;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// Keyboard assignment display helper retained in the original launcher location.
/// This binding-layer version keeps the original key-name/modifier display behavior,
/// but does not include joystick/device preview logic yet.
/// </summary>
public static class KeyAssgn
{
    public static string GetKeyAssignmentStatus(
        string keyboard,
        int modifier,
        string keycombo,
        int keycomboMod)
    {
        string assignmentStatus = "";

        if (keycombo != "0")
        {
            assignmentStatus += ModFlagsToText(keycomboMod.ToString());

            int scancode10 = Convert.ToInt32(keycombo, fromBase: 16);
            Key int2enum = (Key)scancode10;

            assignmentStatus += int2enum + "\t: ";
        }

        if (keyboard != "0xFFFFFFFF")
        {
            assignmentStatus += ModFlagsToText(modifier.ToString());

            int scancode10 = Convert.ToInt32(keyboard, fromBase: 16);
            Key int2enum = (Key)scancode10;

            if (int2enum.ToString() == "-1")
                return assignmentStatus;

            assignmentStatus += NormalizeKeyName(int2enum.ToString());
        }

        return assignmentStatus;
    }

    private static string NormalizeKeyName(string key)
    {
        return key switch
        {
            "Back" => "BackSpace",
            _ => key
        };
    }

    private static string ModFlagsToText(string mod)
    {
        return mod switch
        {
            "0" => "",
            "1" => "Shift ",
            "2" => "Ctrl ",
            "3" => "Ctrl+Shift ",
            "4" => "Alt ",
            "5" => "Alt+Shift ",
            "6" => "Ctrl+Alt ",
            "7" => "Ctrl+Shift+Alt ",
            _ => ""
        };
    }
}