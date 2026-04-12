using System;
using System.Text;
using Vortice.DirectInput;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// Represents a single FalconBMS key assignment entry and exposes helpers for callback name, scancode, modifiers, and display text.
/// </summary>

public sealed class KeyAssgn : ICloneable
{
    private string callback = "SimDoNothing";
    private string soundID = "-1";
    private string none = "0";
    private string keyboard = "0xFFFFFFFF";
    private string modifier = "0";
    private string keycombo = "0";
    private string keycomboMod = "0";
    private string visibility = "-0";
    private string description = "\"\"";

    private int numericScancode;
    private int numericModFlags;

    public string Visibility { get; set; } = "Green";
    public string Mapping => " " + description.Replace("\"", "");
    public string Key => GetKeyAssignmentStatus();

    public string GetCallback() => callback;
    public string GetKeycombo() => keycombo;
    public string GetKeycomboMod() => keycomboMod;
    public string GetKeyDescription() => description;
    public int GetSoundID() => int.Parse(soundID);
    public int GetScancode() => numericScancode;
    public int GetModFlags() => numericModFlags;

    /// <summary>
    /// Returns a single key binding line in the .key file format (ORIGINAL launcher behavior).
    /// </summary>
    public string GetKeyLine()
    {
        string line = "";

        if (Visibility == "Blue")
            line += "#=======================================" +
                    "============================================\n";

        line += callback;
        line += " " + soundID;
        line += " " + none;
        line += " " + keyboard;
        line += " " + modifier;
        line += " " + keycombo;
        line += " " + keycomboMod;

        if (Visibility == "Hidden")
            line += " -2";
        else if (Visibility == "Blue")
            line += " -1";
        else if (Visibility == "Green")
            line += " -0";
        else if (Visibility == "White")
            line += " 1";
        else
            line += " -0";

        // Also accept raw numeric strings if present
        if (Visibility == "-2")
            line += " -2";
        if (Visibility == "-1")
            line += " -1";
        if (Visibility == "-0")
            line += " -0";
        if (Visibility == "1")
            line += " 1";

        line += " " + description;
        line += "\n";
        return line;
    }

    // ORIGINAL-compatible constructor (KeyFile.ParseKeyfileLine relies on this signature)
    public KeyAssgn(params string[] stringParams)
    {
        callback = stringParams[0];
        soundID = stringParams[1];
        none = stringParams[2];
        keyboard = stringParams[3];
        numericScancode = Convert.ToInt32(keyboard, fromBase: 16);
        modifier = stringParams[4];
        numericModFlags = Convert.ToInt32(modifier, fromBase: 10);
        keycombo = stringParams[5];
        keycomboMod = stringParams[6];
        visibility = stringParams[7];

        if (visibility == "-2")
            Visibility = "Hidden";
        else if (visibility == "-1")
            Visibility = "Blue";
        else if (visibility == "-0")
            Visibility = "Green";
        else if (visibility == "1")
            Visibility = "White";
        else
            Visibility = "Green";

        description = "";
        if (stringParams.Length >= 9)
            description = stringParams[8];
        if (stringParams.Length > 9)
            for (int i = 9; i < stringParams.Length; i++)
                description += " " + stringParams[i];

        if (callback == "SimHotasPinkyShift" || callback == "SimHotasShift")
            Visibility = "White";
    }

    // ORIGINAL: GetKeyAssignmentStatus()
    public string GetKeyAssignmentStatus()
    {
        string assignmentStatus = "";

        if (keycombo != "0")
        {
            assignmentStatus += ModFlagsToText(keycomboMod);

            int scancode10 = Convert.ToInt32(keycombo, fromBase: 16);
            Key int2enum = (Key)scancode10;

            assignmentStatus += int2enum + "\t: ";
        }

        if (keyboard != "0xFFFFFFFF")
        {
            assignmentStatus += ModFlagsToText(modifier);

            int scancode10 = Convert.ToInt32(keyboard, fromBase: 16);
            Key int2enum = (Key)scancode10;

            if (int2enum.ToString() == "-1")
                return assignmentStatus;

            assignmentStatus += int2enum.ToString();
        }

        return assignmentStatus;
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

    // Z_Joy_* columns (ORIGINAL pattern)
    public string Z_Joy_0 => ReadJoyAssignment(0);
    public string Z_Joy_1 => ReadJoyAssignment(1);
    public string Z_Joy_2 => ReadJoyAssignment(2);
    public string Z_Joy_3 => ReadJoyAssignment(3);
    public string Z_Joy_4 => ReadJoyAssignment(4);
    public string Z_Joy_5 => ReadJoyAssignment(5);
    public string Z_Joy_6 => ReadJoyAssignment(6);
    public string Z_Joy_7 => ReadJoyAssignment(7);
    public string Z_Joy_8 => ReadJoyAssignment(8);
    public string Z_Joy_9 => ReadJoyAssignment(9);
    public string Z_Joy_10 => ReadJoyAssignment(10);
    public string Z_Joy_11 => ReadJoyAssignment(11);
    public string Z_Joy_12 => ReadJoyAssignment(12);
    public string Z_Joy_13 => ReadJoyAssignment(13);
    public string Z_Joy_14 => ReadJoyAssignment(14);
    public string Z_Joy_15 => ReadJoyAssignment(15);

    // ORIGINAL: ReadJoyAssignment(int joyId) — using KeymappingContext global state 
    private string ReadJoyAssignment(int joyId)
    {
        var joys = KeymappingContext.JoyAssgns;
        if (joyId < 0 || joyId >= joys.Length)
            return "";

        var sb = new StringBuilder();
        sb.Append(joys[joyId].KeyMappingPreviewDX(this));

        // ORIGINAL: POV only for Roll and/or Throttle devices
        if (KeymappingContext.RollJoyId == joyId || KeymappingContext.ThrottleJoyId == joyId)
        {
            string tmp = joys[joyId].KeyMappingPreviewPOV(this);
            if (!string.IsNullOrEmpty(tmp))
                sb.Append("\n" + tmp);
        }

        return sb.ToString();
    }

    public void ClearKeyboard(bool shiftedLayer)
    {
        if (shiftedLayer)
        {
            keycombo = "0";
            keycomboMod = "0";
        }
        else
        {
            keyboard = "0xFFFFFFFF";
            modifier = "0";
        }
    }

    public void SetKeyboard(Vortice.DirectInput.Key diKey, int modFlags, bool shiftedLayer)
    {
        string hex = "0x" + ((int)diKey).ToString("X");

        if (shiftedLayer)
        {
            keycombo = hex;
            keycomboMod = modFlags.ToString();
        }
        else
        {
            keyboard = hex;
            modifier = modFlags.ToString();
        }
    }

    object ICloneable.Clone() => Clone();

    public KeyAssgn Clone() => (KeyAssgn)MemberwiseClone();
}