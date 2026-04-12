using FalconBMS.Launcher.Models;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Reads FalconBMS config files and assembles a combined config state object for the launcher.
/// </summary>

public sealed class BmsConfigRepository
{
    private readonly DeviceSortingReader _sorting = new();
    private readonly AxisMappingDatReader _axisDat = new();

    public BmsConfigState ReadState(string baseDir)
    {
        var order = _sorting.Read(baseDir);
        var axis = _axisDat.Read(baseDir) ?? new AxisMappingDatData
        {
            DeviceCount = 0,
            HeaderJoyNum = -1,
            HeaderInstanceGuid = System.Guid.Empty,
            Entries = System.Array.Empty<AxisMapEntry>()
        };

        return new BmsConfigState
        {
            DeviceOrder = order,
            AxisMappings = axis
        };
    }
}