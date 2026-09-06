using Vortice.DirectInput;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// Central list of key and key combinations that Falcon BMS or 
/// Windows reserves and that must not be reassigned 
/// through the Launcher.
/// </summary>
public static class ReservedKeyboardBindings
{
    private const int ModifierNone = 0;
    private const int ModifierShift = 1;
    private const int ModifierCtrl = 2;
    private const int ModifierCtrlShift = 3;
    private const int ModifierAlt = 4;
    private const int ModifierAltShift = 5;
    private const int ModifierCtrlAlt = 6;

    private const int EscapeScanCode = 0x01;
    private const int TabScanCode = 0x0F;
    private const int QScanCode = 0x10;
    private const int WScanCode = 0x11;
    private const int EScanCode = 0x12;
    private const int RScanCode = 0x13;
    private const int TScanCode = 0x14;
    private const int YScanCode = 0x15;
    private const int EnterScanCode = 0x1C;
    private const int PrintScreenScanCode = 0xB7;
    private const int PauseScanCode = 0xC5;

    public static bool TryGetDisplayText(
        Key key,
        int modifierFlags,
        out string displayText)
    {
        int scanCode = (int)key;

        if (modifierFlags == ModifierNone)
        {
            displayText = scanCode switch
            {
                QScanCode => "Q",
                WScanCode => "W",
                EScanCode => "E",
                RScanCode => "R",
                TScanCode => "T",
                YScanCode => "Y",
                EscapeScanCode => "Escape",
                PrintScreenScanCode => "PrtScn",
                PauseScanCode => "Pause/Break",
                _ => ""
            };

            return displayText.Length > 0;
        }

        if (scanCode == EnterScanCode &&
            modifierFlags == ModifierCtrl)
        {
            displayText = "Ctrl+Enter";
            return true;
        }

        if (scanCode == EnterScanCode &&
            modifierFlags == ModifierAlt)
        {
            displayText = "Alt+Enter";
            return true;
        }

        if (scanCode == TabScanCode &&
            modifierFlags == ModifierAlt)
        {
            displayText = "Alt+Tab";
            return true;
        }

        if (scanCode == TabScanCode &&
            modifierFlags == ModifierAltShift)
        {
            displayText = "Shift+Alt+Tab";
            return true;
        }

        if (scanCode == TabScanCode &&
            modifierFlags == ModifierCtrlAlt)
        {
            displayText = "Ctrl+Alt+Tab";
            return true;
        }

        if (scanCode == EscapeScanCode &&
            modifierFlags == ModifierCtrl)
        {
            displayText = "Ctrl+Escape";
            return true;
        }

        if (scanCode == EscapeScanCode &&
            modifierFlags == ModifierCtrlShift)
        {
            displayText = "Ctrl+Shift+Escape";
            return true;
        }

        if (scanCode == PrintScreenScanCode &&
            modifierFlags == ModifierAlt)
        {
            displayText = "Alt+PrtScn";
            return true;
        }

        displayText = "";
        return false;
    }
}