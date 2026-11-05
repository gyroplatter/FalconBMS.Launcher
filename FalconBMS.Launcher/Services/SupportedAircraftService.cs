using System;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// An aircraft explicitly supported by the Launcher and BMS. Filenames and 
/// the XML section name are hardcoded (e.g. profileDefaultF16 vs profileF15ABCD)
/// </summary>
public sealed class SupportedAircraft
{
    public string AircraftProfile { get; }
    public string FullKeyFileName { get; }
    public string AutoKeyFileName { get; }

    /// <summary>BMS Setup.v100 XML element name that holds this aircraft's DX/POV assignments</summary>
    public string XmlProfileElementName { get; }

    /// <summary>Early beta keyboard JSON as a fallback (F-16 only)</summary>
    public string? LegacyKeyboardJsonFileName { get; }

    public string KeyboardJsonFileName =>
        $"KeyboardBindings_{AircraftProfile}.json";

    public SupportedAircraft(
        string aircraftProfile,
        string fullKeyFileName,
        string autoKeyFileName,
        string xmlProfileElementName,
        string? legacyKeyboardJsonFileName = null)
    {
        AircraftProfile = aircraftProfile;
        FullKeyFileName = fullKeyFileName;
        AutoKeyFileName = autoKeyFileName;
        XmlProfileElementName = xmlProfileElementName;
        LegacyKeyboardJsonFileName = legacyKeyboardJsonFileName;
    }
}

/// <summary>
/// Central registry of aircraft supported by the Launcher.
///
/// TO ADD AN AIRCRAFT: add one entry below. Registry order is also the display
/// and output order, so keep F-16 first.
///
/// Only registered FULL KEY files are recognized. The Controls tab shows an
/// aircraft only when its FULL KEY catalog loaded successfully.
///
/// NOT covered by the registry: axis grouping. An aircraft with no case in
/// AxisDefinitionService.BuildPlacementMap gets the F-16 axis layout. If the
/// new aircraft needs different grouping, add a case there too.
/// </summary>
public static class SupportedAircraftService
{
    private static readonly IReadOnlyList<SupportedAircraft> Aircraft =
        new List<SupportedAircraft>
        {
            new(
                "F-16", // Aircraft display name in Controls profile dropdown
                "BMS - Full.key", // FULL KEY filename in User\Config
                "BMS - Auto.key", // AUTO KEY filename generated in User\Config
                "profileDefaultF16", // Aircraft name in BMS Setup XML
                legacyKeyboardJsonFileName: "KeyboardBindings.json"), // Fallback for early beta build

            new(
                "F-15ABCD",
                "BMS - Full-F15ABCD.key",
                "BMS - Auto-F15ABCD.key",
                "profileF15ABCD")

        };

    public static IReadOnlyList<SupportedAircraft> GetAircraft()
    {
        return Aircraft;
    }

    public static SupportedAircraft? FindByFullKeyFileName(string fileName)
    {
        return Aircraft.FirstOrDefault(aircraft =>
            string.Equals(
                aircraft.FullKeyFileName,
                fileName,
                StringComparison.OrdinalIgnoreCase));
    }
}