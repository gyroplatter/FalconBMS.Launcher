using FalconBMS.Launcher.Models;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Parses DX button assignments from a stock Setup.v100 XML file into existing
/// DeviceAircraftBindingProfile containers. This service only parses buttons;
/// axes and POV hats are handled by separate parsers.
/// </summary>
public sealed class DeviceStockXmlButtonParserService
{
    private const string DoNothingCallback = "SimDoNothing";

    public void ApplyButtons(DeviceBindingProfile profile)
    {
        if (profile.Source != DeviceBindingSource.StockXml)
            return;

        if (string.IsNullOrWhiteSpace(profile.StockXmlPath) || !File.Exists(profile.StockXmlPath))
            return;

        string actionId = DebugDiagnosticsService.CreateActionId("XMLDX");

        DebugDiagnosticsService.Info(
            $"Stock XML DX parse begin | Device=\"{profile.ProductName}\" | File=\"{Path.GetFileName(profile.StockXmlPath)}\" | ActionId={actionId}");

        try
        {
            XDocument document = XDocument.Load(profile.StockXmlPath);

            XElement? rootDx = document.Root?.Element("dx");
            if (rootDx is not null)
            {
                // Root-level DX assignments are the stock/default assignment set.
                // Until aircraft-specific XML sections are parsed, apply them to all
                // aircraft containers so both profiles have the same baseline.
                foreach (DeviceAircraftBindingProfile aircraftProfile in profile.AircraftProfiles)
                    ApplyDxSection(profile, aircraftProfile, rootDx, "root", actionId);
            }

            ApplyAircraftSpecificDxSection(profile, document, "profileDefaultF16", "F-16", actionId);
            ApplyAircraftSpecificDxSection(profile, document, "profileF15ABCD", "F-15ABCD", actionId);

            int totalButtons = profile.AircraftProfiles.Sum(aircraft => aircraft.ButtonBindings.Count);

            DebugDiagnosticsService.Info(
                $"Stock XML DX parse complete | Device=\"{profile.ProductName}\" | AircraftProfiles={profile.AircraftProfiles.Count} | TotalButtonBindings={totalButtons} | ActionId={actionId}");
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"Stock XML DX parse failed | Device=\"{profile.ProductName}\" | File=\"{Path.GetFileName(profile.StockXmlPath)}\" | ActionId={actionId}");
        }
    }

    private void ApplyAircraftSpecificDxSection(
        DeviceBindingProfile profile,
        XDocument document,
        string xmlProfileElementName,
        string aircraftProfileName,
        string actionId)
    {
        XElement? profileElement = document.Root?.Element(xmlProfileElementName);
        XElement? dxElement = profileElement?.Element("dx");

        if (dxElement is null)
            return;

        DeviceAircraftBindingProfile? aircraftProfile = profile.AircraftProfiles.FirstOrDefault(aircraft =>
            string.Equals(aircraft.AircraftProfile, aircraftProfileName, StringComparison.OrdinalIgnoreCase));

        if (aircraftProfile is null)
        {
            DebugDiagnosticsService.Warn(
                $"Stock XML DX profile skipped: aircraft profile missing | Device=\"{profile.ProductName}\" | XmlProfile={xmlProfileElementName} | Aircraft={aircraftProfileName} | ActionId={actionId}");
            return;
        }

        // Aircraft-specific sections override the baseline/root assignments.
        aircraftProfile.ButtonBindings.Clear();

        ApplyDxSection(profile, aircraftProfile, dxElement, xmlProfileElementName, actionId);
    }

    private void ApplyDxSection(
        DeviceBindingProfile deviceProfile,
        DeviceAircraftBindingProfile aircraftProfile,
        XElement dxRoot,
        string sourceSection,
        string actionId)
    {
        var buttonElements = dxRoot.Elements("DxAssgn").ToList();

        int added = 0;

        for (int buttonIndex = 0; buttonIndex < buttonElements.Count; buttonIndex++)
        {
            XElement buttonElement = buttonElements[buttonIndex];
            XElement? assignRoot = buttonElement.Element("assign");

            if (assignRoot is null)
                continue;

            var assignmentElements = assignRoot.Elements("Assgn").ToList();

            for (int assignmentIndex = 0; assignmentIndex < assignmentElements.Count; assignmentIndex++)
            {
                XElement assignmentElement = assignmentElements[assignmentIndex];

                string callbackName = ReadString(assignmentElement, "Callback");
                if (string.IsNullOrWhiteSpace(callbackName) ||
                    string.Equals(callbackName, DoNothingCallback, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                aircraftProfile.ButtonBindings.Add(new DeviceButtonBinding
                {
                    ButtonIndex = buttonIndex,
                    AssignmentIndex = assignmentIndex,
                    CallbackName = callbackName,
                    Invoke = ReadString(assignmentElement, "Invoke", "Default"),
                    SoundId = ReadInt(assignmentElement, "SoundID")
                });

                added++;

                DebugDiagnosticsService.Info(
                    $"Stock XML DX mapped | Device=\"{deviceProfile.ProductName}\" | Aircraft={aircraftProfile.AircraftProfile} | Source={sourceSection} | DX={buttonIndex + 1} | Slot={assignmentIndex} | Callback={callbackName} | Invoke={ReadString(assignmentElement, "Invoke", "Default")} | SoundID={ReadInt(assignmentElement, "SoundID")} | ActionId={actionId}");
            }
        }

        DebugDiagnosticsService.Info(
            $"Stock XML DX section parsed | Device=\"{deviceProfile.ProductName}\" | Aircraft={aircraftProfile.AircraftProfile} | Source={sourceSection} | ButtonsInXml={buttonElements.Count} | ButtonBindingsAdded={added} | ActionId={actionId}");
    }

    private static string ReadString(XElement parent, string elementName, string fallback = "")
    {
        return parent.Element(elementName)?.Value?.Trim() ?? fallback;
    }

    private static int ReadInt(XElement parent, string elementName)
    {
        string value = ReadString(parent, elementName);

        return int.TryParse(value, out int result)
            ? result
            : 0;
    }
}