using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services.Controls;

public sealed class KeyControlsGridBuilderService
{
    public List<ControlGridRowViewModel> Build(
        BindingAircraftProfile? profile,
        IEnumerable<DeviceBindingProfile> deviceProfiles)
    {
        if (profile is null)
            return new List<ControlGridRowViewModel>();

        var devices = deviceProfiles.ToList();

        return GetDisplayRows(profile.Rows)
                    .Select(row => CreateRow(row, profile.AircraftProfile, devices))
                    .ToList();
    }

    private static IEnumerable<BindingRow> GetDisplayRows(IEnumerable<BindingRow> rows)
    {
        bool skippedFirstCategoryHeader = false;

        foreach (BindingRow row in rows)
        {
            if (row.RowKind == BindingRowKind.HiddenCallback)
                continue;

            if (row.RowKind == BindingRowKind.Other)
                continue;

            // The first category-style row in the .key file is the file title.
            // Example: "BMS - Full"
            // Skip only the first parsed CategoryHeader row.
            if (!skippedFirstCategoryHeader &&
                row.RowKind == BindingRowKind.CategoryHeader)
            {
                skippedFirstCategoryHeader = true;
                continue;
            }

            yield return row;
        }
    }

    private static ControlGridRowViewModel CreateRow(
        BindingRow row,
        string aircraftProfileName,
        IReadOnlyList<DeviceBindingProfile> deviceProfiles)
    {
        var viewModel = new ControlGridRowViewModel
        {
            SourceRow = row,
            RowKind = row.RowKind,
            SourceLineNumber = row.SourceLineNumber,
            CategoryName = row.CategoryName,
            SectionName = row.SectionName,
            Mapping = GetMappingText(row),
            DeviceCellsByDeviceKey = BuildDeviceCells(row, aircraftProfileName, deviceProfiles)
        };

        viewModel.RefreshFromSource();

        return viewModel;
    }

    private static Dictionary<string, ControlGridDeviceCellViewModel> BuildDeviceCells(
        BindingRow row,
        string aircraftProfileName,
        IReadOnlyList<DeviceBindingProfile> deviceProfiles)
    {
        var cells = new Dictionary<string, ControlGridDeviceCellViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (DeviceBindingProfile deviceProfile in deviceProfiles)
        {
            string displayText = "";

            if (row.IsCallback && !string.IsNullOrWhiteSpace(row.CallbackName))
            {
                DeviceAircraftBindingProfile? aircraftProfile = deviceProfile.AircraftProfiles.FirstOrDefault(profile =>
                    string.Equals(profile.AircraftProfile, aircraftProfileName, StringComparison.OrdinalIgnoreCase));

                if (aircraftProfile is not null)
                    displayText = BuildDxDisplayText(aircraftProfile, row.CallbackName);
            }

            cells[deviceProfile.DurableDeviceKey] = new ControlGridDeviceCellViewModel
            {
                IsDeviceConnected = deviceProfile.IsConnected,
                DisplayText = displayText,
                HasAxisBinding = false,
                PhysicalAxisIndex = -1
            };
        }

        return cells;
    }

    private static string BuildDxDisplayText(
        DeviceAircraftBindingProfile aircraftProfile,
        string callbackName)
    {
        var parts = new List<string>();

        foreach (DeviceButtonBinding button in aircraftProfile.ButtonBindings.Where(binding =>
                     string.Equals(binding.CallbackName, callbackName, StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add("DX" + button.ButtonNumber);
        }

        foreach (DevicePovBinding pov in aircraftProfile.PovBindings.Where(binding =>
                     string.Equals(binding.CallbackName, callbackName, StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add("POV" + (pov.PovIndex + 1) + " " + GetPovDirectionName(pov.Direction));
        }

        return string.Join(", ", parts);
    }

    private static string GetPovDirectionName(int direction)
    {
        return direction switch
        {
            0 => "Up",
            1 => "Right",
            2 => "Down",
            3 => "Left",
            _ => direction.ToString()
        };
    }

    private static string GetMappingText(BindingRow row)
    {
        if (row.RowKind == BindingRowKind.CategoryHeader)
            return row.CategoryName;

        if (row.RowKind == BindingRowKind.SectionHeader)
            return row.SectionName;

        return row.Description;
    }
}