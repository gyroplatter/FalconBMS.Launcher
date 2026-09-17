using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using System;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Resolves one physical device input to the current BMS callback,
/// description, and keyboard assignment for the selected aircraft profile.
/// </summary>
public sealed class DeviceInputMappingResolver
{
    public DeviceInputMappingResult ResolveButton(
        BindingModel bindingModel,
        BindingAircraftProfile selectedProfile,
        DeviceBindingProfile device,
        int buttonIndex,
        bool isShifted)
    {
        DeviceAircraftBindingProfile? aircraftProfile =
            device.AircraftProfiles.FirstOrDefault(profile =>
                string.Equals(
                    profile.AircraftProfile,
                    selectedProfile.AircraftProfile,
                    StringComparison.OrdinalIgnoreCase));

        if (aircraftProfile is null)
        {
            return DeviceInputMappingResult.Unassigned(
                "DX" + (buttonIndex + 1));
        }

        /*
         * DX Shift itself is always stored in the base assignment slot.
         * Check for that special callback first so pressing the shift button
         * does not incorrectly try to resolve its shifted slot.
         */
        DeviceButtonBinding? shiftBinding =
            aircraftProfile.ButtonBindings.FirstOrDefault(binding =>
                binding.ButtonIndex == buttonIndex &&
                DeviceButtonBinding.IsDxShiftCallback(
                    binding.CallbackName));

        if (shiftBinding is not null)
        {
            return BuildResult(
                bindingModel,
                selectedProfile,
                shiftBinding.CallbackName,
                "DX" + (buttonIndex + 1));
        }

        string shiftState =
            isShifted
                ? DeviceButtonBinding.ShiftStateShifted
                : DeviceButtonBinding.ShiftStateUnshifted;

        int assignmentIndex =
            DeviceButtonBinding.GetAssignmentIndex(
                shiftState,
                DeviceButtonBinding.TriggerPress);

        DeviceButtonBinding? binding =
            aircraftProfile.ButtonBindings.FirstOrDefault(binding =>
                binding.ButtonIndex == buttonIndex &&
                binding.AssignmentIndex == assignmentIndex &&
                !string.IsNullOrWhiteSpace(
                    binding.CallbackName));

        if (binding is null)
        {
            return DeviceInputMappingResult.Unassigned(
                "DX" + (buttonIndex + 1));
        }

        return BuildResult(
            bindingModel,
            selectedProfile,
            binding.CallbackName,
            "DX" + (buttonIndex + 1));
    }

    public DeviceInputMappingResult ResolvePov(
        BindingModel bindingModel,
        BindingAircraftProfile selectedProfile,
        DeviceBindingProfile device,
        int povIndex,
        int direction,
        bool isShifted)
    {
        string inputDisplay =
            "POV" +
            (povIndex + 1) +
            " " +
            GetPovDirectionName(direction);

        DeviceAircraftBindingProfile? aircraftProfile =
            device.AircraftProfiles.FirstOrDefault(profile =>
                string.Equals(
                    profile.AircraftProfile,
                    selectedProfile.AircraftProfile,
                    StringComparison.OrdinalIgnoreCase));

        if (aircraftProfile is null)
        {
            return DeviceInputMappingResult.Unassigned(
                inputDisplay);
        }

        string invoke =
            isShifted
                ? "Shift"
                : "Default";

        DevicePovBinding? binding =
            aircraftProfile.PovBindings.FirstOrDefault(binding =>
                binding.PovIndex == povIndex &&
                binding.Direction == direction &&
                string.Equals(
                    binding.Invoke,
                    invoke,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(
                    binding.CallbackName));

        if (binding is null)
        {
            return DeviceInputMappingResult.Unassigned(
                inputDisplay);
        }

        return BuildResult(
            bindingModel,
            selectedProfile,
            binding.CallbackName,
            inputDisplay);
    }

    private static DeviceInputMappingResult BuildResult(
        BindingModel bindingModel,
        BindingAircraftProfile selectedProfile,
        string callbackName,
        string inputDisplay)
    {
        BindingRow? row =
            selectedProfile.Rows.FirstOrDefault(row =>
                row.IsCallback &&
                string.Equals(
                    row.CallbackName,
                    callbackName,
                    StringComparison.OrdinalIgnoreCase));

        string description =
            row is not null &&
            !string.IsNullOrWhiteSpace(row.Description)
                ? row.Description.Trim()
                : callbackName;

        string keyboardMapping =
            row is null
                ? ""
                : KeyAssgn.GetKeyAssignmentStatus(
                    row.KeyScancode,
                    row.KeyModifierFlags,
                    row.ChordScancode,
                    row.ChordModifierFlags);

        return new DeviceInputMappingResult
        {
            InputDisplay = inputDisplay,
            IsAssigned = true,
            CallbackName = callbackName,
            MappingDisplay = description,
            KeyboardDisplay =
                string.IsNullOrWhiteSpace(keyboardMapping)
                    ? "Unassigned"
                    : keyboardMapping.Trim()
        };
    }

    private static string GetPovDirectionName(
        int direction)
    {
        return direction switch
        {
            0 => "Up",
            1 => "Up-Right",
            2 => "Right",
            3 => "Down-Right",
            4 => "Down",
            5 => "Down-Left",
            6 => "Left",
            7 => "Up-Left",
            _ => direction.ToString()
        };
    }
}

public sealed class DeviceInputMappingResult
{
    public string InputDisplay { get; init; } = "";

    public bool IsAssigned { get; init; }

    public string CallbackName { get; init; } = "";

    public string MappingDisplay { get; init; } = "";

    public string KeyboardDisplay { get; init; } = "";

    public static DeviceInputMappingResult Unassigned(
        string inputDisplay)
    {
        return new DeviceInputMappingResult
        {
            InputDisplay = inputDisplay,
            IsAssigned = false,
            CallbackName = "",
            MappingDisplay = "Unassigned",
            KeyboardDisplay = "Unassigned"
        };
    }
}