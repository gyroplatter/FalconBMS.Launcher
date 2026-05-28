namespace FalconBMS.Launcher.Models;

/// <summary>
/// Represents one DirectInput button-to-callback binding for a device.
/// ButtonIndex is zero-based; ButtonNumber is the user-facing DX number.
/// AssignmentIndex is the zero-based slot inside the XML button assignment list.
///
/// DX press/release and shifted state are four fixed slots:
/// 0 = Unshifted + Press
/// 1 = Shifted + Press
/// 2 = Unshifted + Release
/// 3 = Shifted + Release
/// </summary>
public sealed class DeviceButtonBinding
{
    public const string ShiftStateUnshifted = "Unshifted";
    public const string ShiftStateShifted = "Shifted";
    public const string TriggerPress = "Press";
    public const string TriggerRelease = "Release";

    public const string DxShiftCallbackName = "SimHotasShift";
    public const string DxPinkyShiftCallbackName = "SimHotasPinkyShift";

    public int ButtonIndex { get; init; }

    public int ButtonNumber => ButtonIndex + 1;

    public int AssignmentIndex { get; init; }

    public string CallbackName { get; set; } = "";

    public string Invoke { get; set; } = "Default";

    public int SoundId { get; set; }

    public static bool IsDxShiftCallback(string callbackName)
    {
        return string.Equals(callbackName, DxPinkyShiftCallbackName, System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(callbackName, DxShiftCallbackName, System.StringComparison.OrdinalIgnoreCase);
    }

    public static int NormalizeAssignmentIndexForCallback(string callbackName, int assignmentIndex)
    {
        // DX Shift callbacks are special launcher/system callbacks.
        return IsDxShiftCallback(callbackName)
            ? 0
            : assignmentIndex;
    }

    public static int GetAssignmentIndex(string shiftState, string trigger)
    {
        bool shifted = string.Equals(shiftState, ShiftStateShifted, System.StringComparison.OrdinalIgnoreCase);
        bool release = string.Equals(trigger, TriggerRelease, System.StringComparison.OrdinalIgnoreCase);

        if (release && shifted)
            return 3;

        if (release)
            return 2;

        if (shifted)
            return 1;

        return 0;
    }

    public static string GetShiftState(int assignmentIndex)
    {
        return assignmentIndex == 1 || assignmentIndex == 3
            ? ShiftStateShifted
            : ShiftStateUnshifted;
    }

    public static string GetTrigger(int assignmentIndex)
    {
        return assignmentIndex == 2 || assignmentIndex == 3
            ? TriggerRelease
            : TriggerPress;
    }

    public static string GetDefaultInvoke(int assignmentIndex)
    {
        return GetTrigger(assignmentIndex) == TriggerRelease
            ? "Down"
            : "Default";
    }

    public static string BuildDisplayText(int buttonIndex, int assignmentIndex)
    {
        string text = "DX" + (buttonIndex + 1);

        if (GetTrigger(assignmentIndex) == TriggerRelease)
            text += " RELEASE";

        if (GetShiftState(assignmentIndex) == ShiftStateShifted)
            text += " SHIFT";

        return text;
    }
}