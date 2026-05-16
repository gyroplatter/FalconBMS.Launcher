using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Writes Falcon BMS joystick calibration data to User\Config\joystick.cal.
/// 
/// joystick.cal is a generated compatibility artifact. It is not a source of truth.
/// The source of truth is the in-memory DeviceBindingProfile model, persisted to
/// DeviceBindings_*.json.
/// 
/// Falcon BMS 4.37+ uses:
/// - 30 axis calibration entries
/// - 24 bytes per entry
/// - assigned flag at byte 20
/// - invert flag at byte 21
/// - throttle detents at bytes 0-1 and 4-5 in a 0..15000 inverted scale
/// </summary>
public sealed class JoystickCalService
{
    private const int AxisCount = 30;
    private const int EntrySize = 24;
    private const int TotalSize = AxisCount * EntrySize;
    private const int JoystickCalDetentScale = 15000;

    /// <summary>
    /// BMS 4.37+ joystick.cal order.
    /// 
    /// Important: this order is not the same as axismapping.dat's mapping index order.
    /// It matches the stock Alternative Launcher calibration list.
    /// </summary>
    private static readonly string[] JoystickCalOrder =
        {
        "Pitch",
        "Roll",
        "Yaw",
        "Throttle",
        "Throttle_Right",
        "Trim_Pitch",
        "Trim_Yaw",
        "Trim_Roll",
        "Toe_Brake",
        "Toe_Brake_Right",
        "FOV",
        "Radar_Antenna_Elevation",
        "Cursor_X",
        "Cursor_Y",
        "Range_Knob",
        "COMM_Channel_1",
        "COMM_Channel_2",
        "MSL_Volume",
        "Threat_Volume",
        "HUD_Brightness",
        "Reticle_Depression",
        "Camera_Distance",
        "IntercomVolumeVolume",
        "HMS_Brightness",
        "AI_vs_IVC",
        "FLIR_Brightness",
        "HSI_Course_Knob",
        "HSI_Heading_Knob",
        "Altimeter_Knob",
        "ILS_Volume_Knob"
    };

    public void Write(string baseDir, IReadOnlyList<DeviceBindingProfile> deviceProfiles)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("JOYCAL");

        string configDir = Path.Combine(baseDir, "User", "Config");
        Directory.CreateDirectory(configDir);

        string path = Path.Combine(configDir, "joystick.cal");
        string beforeSignature = DebugDiagnosticsService.GetFileSignature(path);

        byte[] bytes = BuildJoystickCalBytes(deviceProfiles);

        if (File.Exists(path))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);

        if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes))
            File.WriteAllBytes(path, bytes);

        DebugDiagnosticsService.LogFileWriteResult(
            "joystick.cal",
            path,
            beforeSignature,
            "JoystickCalService.Write",
            $"DeviceCount={deviceProfiles.Count}",
            actionId);
    }

    private byte[] BuildJoystickCalBytes(IReadOnlyList<DeviceBindingProfile> deviceProfiles)
    {
        var bytes = new byte[TotalSize];

        for (int joystickCalIndex = 0; joystickCalIndex < AxisCount; joystickCalIndex++)
        {
            byte[] block = CreateDefaultBlock();

            string? logicalAxisName = JoystickCalOrder[joystickCalIndex];

            if (!string.IsNullOrWhiteSpace(logicalAxisName))
            {
                DeviceAxisBinding? binding = FindAssignedAxisBinding(deviceProfiles, logicalAxisName);

                if (binding is not null)
                {
                    ApplyAssignedAndInvertFlags(block, binding);

                    // Only the primary Throttle axis has detents in this launcher.
                    // Throttle_Right remains assignable, but does not expose or write detents.
                    if (string.Equals(binding.LogicalAxisName, "Throttle", StringComparison.OrdinalIgnoreCase))
                        ApplyThrottleDetents(block, binding);
                }
            }

            Buffer.BlockCopy(block, 0, bytes, joystickCalIndex * EntrySize, EntrySize);
        }

        return bytes;
    }

    private DeviceAxisBinding? FindAssignedAxisBinding(
        IReadOnlyList<DeviceBindingProfile> deviceProfiles,
        string logicalAxisName)
    {
        DeviceAxisDefinition? definition = AxisDefinitionService.Find(logicalAxisName);

        if (definition is null)
            return null;

        foreach (DeviceBindingProfile profile in deviceProfiles)
        {
            DeviceAxisBinding? binding = profile.AxisBindings.FirstOrDefault(axis =>
                string.Equals(axis.LogicalAxisName, definition.LogicalAxisName, StringComparison.OrdinalIgnoreCase) &&
                axis.PhysicalAxisIndex.HasValue);

            if (binding is not null)
                return binding;
        }

        return null;
    }

    private static byte[] CreateDefaultBlock()
    {
        // Matches the default bytes used by the stock launcher for BMS 4.37+.
        // The 0x98,0x3A bytes represent the default 15000 calibration value.
        return new byte[EntrySize]
        {
            0x00, 0x00, 0x00, 0x00,
            0x98, 0x3A, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };
    }

    private static void ApplyAssignedAndInvertFlags(byte[] block, DeviceAxisBinding binding)
    {
        block[20] = 0x01;
        block[21] = binding.Invert ? (byte)0x01 : (byte)0x00;
    }

    private static void ApplyThrottleDetents(byte[] block, DeviceAxisBinding binding)
    {
        int afterburnerDetent = binding.AfterburnerDetent ?? DetentPosition.DefaultAfterburnerDetent;
        int idleDetent = binding.IdleDetent ?? DetentPosition.DefaultIdleDetent;

        WriteDetentValue(block, 0, afterburnerDetent);
        WriteDetentValue(block, 4, idleDetent);
    }

    private static void WriteDetentValue(byte[] block, int offset, int falconAxisValue)
    {
        /*
        Stock launcher conversion:
        - launcher stores detents in Falcon/native 0..65535 axis space
        - joystick.cal stores them in a 0..15000 scale
        - joystick.cal direction is inverted
        - value is written little-endian at the requested offset
        */

        int clampedFalconValue = Math.Max(
            DetentPosition.MinAxisValue,
            Math.Min(DetentPosition.MaxAxisValue, falconAxisValue));

        int scaledValue = clampedFalconValue * JoystickCalDetentScale / DetentPosition.MaxAxisValue;
        int joystickCalValue = JoystickCalDetentScale - scaledValue;

        joystickCalValue = Math.Max(0, Math.Min(JoystickCalDetentScale, joystickCalValue));

        block[offset + 0] = (byte)(joystickCalValue & 0xFF);
        block[offset + 1] = (byte)((joystickCalValue >> 8) & 0xFF);
        block[offset + 2] = 0x00;
        block[offset + 3] = 0x00;
    }
}