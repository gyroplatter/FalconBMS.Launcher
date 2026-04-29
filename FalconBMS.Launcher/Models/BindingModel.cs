using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Root editable in-memory model for launcher bindings.
/// 
/// This is the layer users will eventually edit. It is built from the
/// read-only KeyCatalog layer, then later overlaid with saved user bindings,
/// device assignments, axis mappings, and output-file state.
/// 
/// This model does not write files by itself.
/// </summary>
public sealed class BindingModel
{
    public List<BindingAircraftProfile> AircraftProfiles { get; } = new();

    public List<DeviceBindingProfile> DeviceProfiles { get; } = new();

    public int ProfileCount => AircraftProfiles.Count;
    public int TotalRows => AircraftProfiles.Sum(x => x.TotalRows);
    public int VisibleRows => AircraftProfiles.Sum(x => x.VisibleRows);
    public int CallbackRows => AircraftProfiles.Sum(x => x.CallbackRows);
    public int EditableRows => AircraftProfiles.Sum(x => x.EditableRows);
    public int LockedRows => AircraftProfiles.Sum(x => x.LockedRows);
    public int HiddenRows => AircraftProfiles.Sum(x => x.HiddenRows);

    public int DeviceProfileCount => DeviceProfiles.Count;
}