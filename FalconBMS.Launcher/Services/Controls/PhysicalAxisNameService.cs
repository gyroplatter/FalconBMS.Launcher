namespace FalconBMS.Launcher.Services.Controls;

/// <summary>
/// Converts Falcon BMS physical axis indexes to display names.
/// These indexes match the Setup.v100 XML axis order.
/// </summary>
public static class PhysicalAxisNameService
{
    public static string GetDisplayName(int physicalAxisIndex)
    {
        return physicalAxisIndex switch
        {
            0 => "X",
            1 => "Y",
            2 => "Z",
            3 => "Rx",
            4 => "Ry",
            5 => "Rz",
            6 => "Slider 0",
            7 => "Slider 1",
            _ => $"Axis {physicalAxisIndex}"
        };
    }
}