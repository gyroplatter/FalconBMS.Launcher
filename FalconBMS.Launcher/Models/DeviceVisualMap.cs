using System.Collections.Generic;
using System.Runtime.Serialization;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Visual-only map for a supported physical device image.
/// This does not store BMS mappings. It only maps device inputs like DX1/DX2
/// to coordinates on the image shown in the Devices tab.
/// </summary>
[DataContract]
public sealed class DeviceVisualMap
{
    [DataMember(Name = "stockDeviceName")]
    public string StockDeviceName { get; set; } = "";

    [DataMember(Name = "imagePath")]
    public string ImagePath { get; set; } = "";

    [DataMember(Name = "canvasWidth")]
    public double CanvasWidth { get; set; }

    [DataMember(Name = "canvasHeight")]
    public double CanvasHeight { get; set; }

    [DataMember(Name = "calloutBox")]
    public DeviceVisualCalloutBoxMap CalloutBox { get; set; } = new();

    [DataMember(Name = "controls")]
    public List<DeviceVisualControlMap> Controls { get; set; } = new();
}

[DataContract]
public sealed class DeviceVisualCalloutBoxMap
{
    [DataMember(Name = "x")]
    public double X { get; set; }

    [DataMember(Name = "y")]
    public double Y { get; set; }

    [DataMember(Name = "width")]
    public double Width { get; set; }
}

[DataContract]
public sealed class DeviceVisualControlMap
{
    [DataMember(Name = "kind")]
    public string Kind { get; set; } = "";

    /// <summary>
    /// Zero-based DirectInput button index.
    /// DX1 = 0, DX2 = 1, etc.
    /// </summary>
    [DataMember(Name = "buttonIndex")]
    public int ButtonIndex { get; set; } = -1;

    [DataMember(Name = "inputId")]
    public string InputId { get; set; } = "";

    [DataMember(Name = "physicalName")]
    public string PhysicalName { get; set; } = "";

    /// <summary>
    /// Which hotspot should be used for the connector line when a control has
    /// multiple visible locations on a two-view image.
    /// </summary>
    [DataMember(Name = "preferredHotspotIndex")]
    public int PreferredHotspotIndex { get; set; }

    [DataMember(Name = "hotspots")]
    public List<DeviceVisualHotspotMap> Hotspots { get; set; } = new();
}

[DataContract]
public sealed class DeviceVisualHotspotMap
{
    [DataMember(Name = "shape")]
    public string Shape { get; set; } = "ellipse";

    [DataMember(Name = "x")]
    public double X { get; set; }

    [DataMember(Name = "y")]
    public double Y { get; set; }

    [DataMember(Name = "width")]
    public double Width { get; set; }

    [DataMember(Name = "height")]
    public double Height { get; set; }

    [DataMember(Name = "anchorX")]
    public double AnchorX { get; set; }

    [DataMember(Name = "anchorY")]
    public double AnchorY { get; set; }
}