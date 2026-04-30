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
                foreach (var aircraft in profile.AircraftProfiles)
                    ApplyPovSection(profile, aircraft, rootPov, "root", actionId);
            }

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

    private void ApplyPovSection(
        DeviceBindingProfile device,
        DeviceAircraftBindingProfile aircraft,
        XElement povRoot,
        string source,
        string actionId)
    {
        var povElements = povRoot.Elements("PovAssgn").ToList();

        int added = 0;

        for (int povIndex = 0; povIndex < povElements.Count; povIndex++)
        {
            XElement povElement = povElements[povIndex];
            XElement? assignRoot = povElement.Element("assign");

            if (assignRoot == null)
                continue;

            var directions = assignRoot.Elements("Assgn").ToList();

            for (int dir = 0; dir < directions.Count; dir++)
            {
                XElement assgn = directions[dir];

                string callback = assgn.Element("Callback")?.Value?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(callback) ||
                    string.Equals(callback, DoNothingCallback, StringComparison.OrdinalIgnoreCase))
                    continue;

                aircraft.PovBindings.Add(new DevicePovBinding
                {
                    PovIndex = povIndex,
                    Direction = dir,
                    CallbackName = callback,
                    Invoke = assgn.Element("Invoke")?.Value ?? "Default",
                    SoundId = int.TryParse(assgn.Element("SoundID")?.Value, out int s) ? s : 0
                });

                added++;

                DebugDiagnosticsService.Info(
                    $"Stock XML POV mapped | Device=\"{device.ProductName}\" | Aircraft={aircraft.AircraftProfile} | POV={povIndex} | Dir={dir} | Callback={callback} | ActionId={actionId}");
            }
        }

        DebugDiagnosticsService.Info(
            $"Stock XML POV section parsed | Device=\"{device.ProductName}\" | Aircraft={aircraft.AircraftProfile} | PovBindingsAdded={added} | ActionId={actionId}");
    }
}