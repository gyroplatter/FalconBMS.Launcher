using FalconBMS.Launcher.Models;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Parses axis assignments from a stock Setup.v100 XML file into an existing
/// DeviceBindingProfile. This service only updates pre-created logical axis
/// bindings; it does not create new logical axes and does not parse buttons/POVs.
/// </summary>
public sealed class DeviceStockXmlAxisParserService
{
    private readonly AxisDefinitionService _axisDefinitions = new();

    public void ApplyAxes(DeviceBindingProfile profile)
    {
        if (profile.Source != DeviceBindingSource.StockXml)
            return;

        if (string.IsNullOrWhiteSpace(profile.StockXmlPath) || !File.Exists(profile.StockXmlPath))
            return;

        string actionId = DebugDiagnosticsService.CreateActionId("XMLAXIS");

        DebugDiagnosticsService.Info(
            $"Stock XML axis parse begin | Device=\"{profile.ProductName}\" | File=\"{Path.GetFileName(profile.StockXmlPath)}\" | ActionId={actionId}");

        try
        {
            XDocument document = XDocument.Load(profile.StockXmlPath);

            ApplyAxisAssignments(profile, document, actionId);
            ApplyThrottleDetents(profile, document, actionId);

            int assignedAxes = profile.AxisBindings.Count(axis => axis.PhysicalAxisIndex.HasValue);

            DebugDiagnosticsService.Info(
                $"Stock XML axis parse complete | Device=\"{profile.ProductName}\" | AssignedAxes={assignedAxes} | TotalLogicalAxes={profile.AxisBindings.Count} | ActionId={actionId}");
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"Stock XML axis parse failed | Device=\"{profile.ProductName}\" | File=\"{Path.GetFileName(profile.StockXmlPath)}\" | ActionId={actionId}");
        }
    }

    private void ApplyAxisAssignments(DeviceBindingProfile profile, XDocument document, string actionId)
    {
        XElement? axisRoot = document.Root?.Element("axis");
        if (axisRoot is null)
            return;

        var axisElements = axisRoot.Elements("AxAssgn").ToList();

        for (int physicalAxisIndex = 0; physicalAxisIndex < axisElements.Count; physicalAxisIndex++)
        {
            XElement axisElement = axisElements[physicalAxisIndex];

            string logicalAxisName = ReadString(axisElement, "AxisName");
            if (string.IsNullOrWhiteSpace(logicalAxisName))
                continue;

            DeviceAxisDefinition? definition = _axisDefinitions.Find(logicalAxisName);
            if (definition is null)
            {
                DebugDiagnosticsService.Warn(
                    $"Stock XML axis skipped: unknown logical axis | Device=\"{profile.ProductName}\" | AxisName=\"{logicalAxisName}\" | PhysicalAxis={physicalAxisIndex} | ActionId={actionId}");
                continue;
            }

            DeviceAxisBinding? binding = profile.AxisBindings.FirstOrDefault(axis =>
                string.Equals(axis.LogicalAxisName, logicalAxisName, StringComparison.OrdinalIgnoreCase));

            if (binding is null)
            {
                DebugDiagnosticsService.Warn(
                    $"Stock XML axis skipped: logical axis not present in profile | Device=\"{profile.ProductName}\" | AxisName=\"{logicalAxisName}\" | PhysicalAxis={physicalAxisIndex} | ActionId={actionId}");
                continue;
            }

            binding.PhysicalAxisIndex = physicalAxisIndex;

            if (definition.SupportsSaturation)
                binding.Saturation = ReadString(axisElement, "Saturation", "None");

            if (definition.SupportsDeadzone)
                binding.Deadzone = ReadString(axisElement, "Deadzone", "None");
            else
                binding.Deadzone = "None";

            if (definition.SupportsInvert)
                binding.Invert = ReadBool(axisElement, "Invert");

            DebugDiagnosticsService.Info(
                $"Stock XML axis mapped | Device=\"{profile.ProductName}\" | AxisName=\"{logicalAxisName}\" | PhysicalAxis={physicalAxisIndex} | Saturation={binding.Saturation} | Deadzone={binding.Deadzone} | Invert={binding.Invert} | ActionId={actionId}");
        }
    }

    private void ApplyThrottleDetents(DeviceBindingProfile profile, XDocument document, string actionId)
    {
        XElement? detentRoot = document.Root?.Element("detentPosition");
        if (detentRoot is null)
            return;

        DeviceAxisDefinition? throttleDefinition = _axisDefinitions.Find("Throttle");
        if (throttleDefinition is null)
            return;

        DeviceAxisBinding? throttleBinding = profile.AxisBindings.FirstOrDefault(axis =>
            string.Equals(axis.LogicalAxisName, "Throttle", StringComparison.OrdinalIgnoreCase));

        if (throttleBinding is null)
            return;

        if (throttleDefinition.SupportsAfterburnerDetent)
            throttleBinding.AfterburnerDetent = ReadNullableInt(detentRoot, "AB");

        if (throttleDefinition.SupportsIdleDetent)
            throttleBinding.IdleDetent = ReadNullableInt(detentRoot, "IDLE");

        DebugDiagnosticsService.Info(
            $"Stock XML throttle detents parsed | Device=\"{profile.ProductName}\" | AB={FormatNullable(throttleBinding.AfterburnerDetent)} | IDLE={FormatNullable(throttleBinding.IdleDetent)} | ActionId={actionId}");
    }

    private static string ReadString(XElement parent, string elementName, string fallback = "")
    {
        return parent.Element(elementName)?.Value?.Trim() ?? fallback;
    }

    private static bool ReadBool(XElement parent, string elementName)
    {
        string value = ReadString(parent, elementName);

        return bool.TryParse(value, out bool result) && result;
    }

    private static int? ReadNullableInt(XElement parent, string elementName)
    {
        string value = ReadString(parent, elementName);

        if (int.TryParse(value, out int result))
            return result;

        return null;
    }

    private static string FormatNullable(int? value)
    {
        return value.HasValue ? value.Value.ToString() : "";
    }
}