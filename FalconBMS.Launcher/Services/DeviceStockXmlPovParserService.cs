using FalconBMS.Launcher.Models;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Parses POV bindings from stock XML.
/// </summary>
public sealed class DeviceStockXmlPovParserService
{
    private const string DoNothingCallback = "SimDoNothing";

    public void ApplyPovs(DeviceBindingProfile profile)
    {
        if (profile.Source != DeviceBindingSource.StockXml)
            return;

        if (string.IsNullOrWhiteSpace(profile.StockXmlPath) || !File.Exists(profile.StockXmlPath))
            return;

        string actionId = DebugDiagnosticsService.CreateActionId("XMLPOV");

        DebugDiagnosticsService.Info(
            $"Stock XML POV parse begin | Device=\"{profile.ProductName}\" | File=\"{Path.GetFileName(profile.StockXmlPath)}\" | ActionId={actionId}");

        try
        {
            XDocument doc = XDocument.Load(profile.StockXmlPath);

            XElement? rootPov = doc.Root?.Element("pov");

            if (rootPov != null)
            {
                DeviceAircraftBindingProfile? f16Profile = profile.AircraftProfiles.FirstOrDefault(aircraft =>
                    string.Equals(aircraft.AircraftProfile, "F-16", StringComparison.OrdinalIgnoreCase));

                if (f16Profile is not null)
                {
                    // Legacy stock XML root POV assignments are the F-16/default set.
                    // Do not copy root POV assignments into F-15ABCD unless the XML
                    // explicitly contains a profileF15ABCD section.
                    ApplyPovSection(profile, f16Profile, rootPov, "root", actionId);
                }
            }

            ApplyAircraftSpecificPovSection(profile, doc, "profileDefaultF16", "F-16", actionId);
            ApplyAircraftSpecificPovSection(profile, doc, "profileF15ABCD", "F-15ABCD", actionId);

            int total = profile.AircraftProfiles.Sum(a => a.PovBindings.Count);

            DebugDiagnosticsService.Info(
                $"Stock XML POV parse complete | Device=\"{profile.ProductName}\" | TotalPovBindings={total} | ActionId={actionId}");
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"Stock XML POV parse failed | Device=\"{profile.ProductName}\" | ActionId={actionId}");
        }
    }

    private void ApplyAircraftSpecificPovSection(
    DeviceBindingProfile profile,
    XDocument document,
    string xmlProfileElementName,
    string aircraftProfileName,
    string actionId)
    {
        XElement? profileElement = document.Root?.Element(xmlProfileElementName);
        XElement? povElement = profileElement?.Element("pov");

        if (povElement is null)
            return;

        DeviceAircraftBindingProfile? aircraftProfile = profile.AircraftProfiles.FirstOrDefault(aircraft =>
            string.Equals(aircraft.AircraftProfile, aircraftProfileName, StringComparison.OrdinalIgnoreCase));

        if (aircraftProfile is null)
        {
            DebugDiagnosticsService.Warn(
                $"Stock XML POV profile skipped: aircraft profile missing | Device=\"{profile.ProductName}\" | XmlProfile={xmlProfileElementName} | Aircraft={aircraftProfileName} | ActionId={actionId}");
            return;
        }

        // Aircraft-specific sections override the baseline/root assignments.
        aircraftProfile.PovBindings.Clear();

        ApplyPovSection(profile, aircraftProfile, povElement, xmlProfileElementName, actionId);
    }


    private void ApplyPovSection(
        DeviceBindingProfile device,
        DeviceAircraftBindingProfile aircraft,
        XElement povRoot,
        string source,
        string actionId)
    {
        var povElements = povRoot.Elements("PovAssgn").Take(4).ToList();

        int added = 0;

        for (int povIndex = 0; povIndex < povElements.Count; povIndex++)
        {
            XElement povElement = povElements[povIndex];

            XElement? directionRoot = povElement.Element("direction");

            var directions = (directionRoot != null
                    ? directionRoot.Elements("DirAssgn")
                    : povElement.Elements("DirAssgn"))
                .Take(8)
                .ToList();

            for (int dir = 0; dir < directions.Count; dir++)
            {
                XElement dirAssgn = directions[dir];

                var callbackValues = dirAssgn
                    .Element("Callback")
                    ?.Elements("string")
                    .ToList();

                var soundValues = dirAssgn
                    .Element("SoundID")
                    ?.Elements("int")
                    .ToList();

                string unshiftedCallback = callbackValues?.ElementAtOrDefault(0)?.Value?.Trim() ?? DoNothingCallback;
                string shiftedCallback = callbackValues?.ElementAtOrDefault(1)?.Value?.Trim() ?? DoNothingCallback;

                int unshiftedSoundId = int.TryParse(soundValues?.ElementAtOrDefault(0)?.Value, out int s0) ? s0 : 0;
                int shiftedSoundId = int.TryParse(soundValues?.ElementAtOrDefault(1)?.Value, out int s1) ? s1 : 0;

                if (!string.IsNullOrWhiteSpace(unshiftedCallback) &&
                    !string.Equals(unshiftedCallback, DoNothingCallback, StringComparison.OrdinalIgnoreCase))
                {
                    aircraft.PovBindings.Add(new DevicePovBinding
                    {
                        PovIndex = povIndex,
                        Direction = dir,
                        CallbackName = unshiftedCallback,
                        Invoke = "Default",
                        SoundId = unshiftedSoundId
                    });

                    added++;

                    DebugDiagnosticsService.Info(
                        $"Stock XML POV mapped | Device=\"{device.ProductName}\" | Aircraft={aircraft.AircraftProfile} | POV={povIndex} | Dir={dir} | Shift=False | Callback={unshiftedCallback} | ActionId={actionId}");
                }

                if (!string.IsNullOrWhiteSpace(shiftedCallback) &&
                    !string.Equals(shiftedCallback, DoNothingCallback, StringComparison.OrdinalIgnoreCase))
                {
                    aircraft.PovBindings.Add(new DevicePovBinding
                    {
                        PovIndex = povIndex,
                        Direction = dir,
                        CallbackName = shiftedCallback,
                        Invoke = "Shift",
                        SoundId = shiftedSoundId
                    });

                    added++;

                    DebugDiagnosticsService.Info(
                        $"Stock XML POV mapped | Device=\"{device.ProductName}\" | Aircraft={aircraft.AircraftProfile} | POV={povIndex} | Dir={dir} | Shift=True | Callback={shiftedCallback} | ActionId={actionId}");
                }
            }
        }

        DebugDiagnosticsService.Info(
            $"Stock XML POV section parsed | Device=\"{device.ProductName}\" | Aircraft={aircraft.AircraftProfile} | Source={source} | PovBindingsAdded={added} | ActionId={actionId}");
    }
}