using System.Collections.Generic;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Persisted visual map for one physical device family.
///
/// Hotspot and callout geometry is stored as normalized image coordinates so
/// the map remains stable when the editor or Devices view is resized.
/// </summary>
public sealed class DeviceMapDefinition
{
    public int Version { get; set; } = 2;

    public string DeviceName { get; set; } = "";

    public string PidVid { get; set; } = "";

    public string ImageFileName { get; set; } = "";

    public List<DeviceMapHotspot> Hotspots { get; set; } =
        new();

    public List<DeviceMapCallout> Callouts { get; set; } =
        new();
}

public sealed class DeviceMapHotspot
{
    public string InputKind { get; set; } =
        "Button";

    public int ButtonIndex { get; set; } =
        -1;

    public int PovIndex { get; set; } =
        -1;

    public int PovDirection { get; set; } =
        -1;

    /// <summary>
    /// Normalized horizontal position from 0.0 to 1.0.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Normalized vertical position from 0.0 to 1.0.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Normalized width relative to the image width.
    /// </summary>
    public double Width { get; set; } =
        0.08;

    /// <summary>
    /// Normalized height relative to the image height.
    /// </summary>
    public double Height { get; set; } =
        0.08;
}

/// <summary>
/// One callout is shared by all hotspots belonging to the same logical input.
/// The text itself is not persisted because it is resolved from the current
/// aircraft profile when the map is displayed.
/// </summary>
public sealed class DeviceMapCallout
{
    public string InputKind { get; set; } =
        "Button";

    public int ButtonIndex { get; set; } =
        -1;

    public int PovIndex { get; set; } =
        -1;

    public int PovDirection { get; set; } =
        -1;

    /// <summary>
    /// Normalized horizontal position from 0.0 to 1.0.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Normalized vertical position from 0.0 to 1.0.
    /// </summary>
    public double Y { get; set; }
}