using FalconBMS.Launcher.Models;
using System;
using System.IO;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Writes Falcon joystick calibration data.
/// Writes User\Config\joystick.cal in the same layout as the stock Alternative Launcher
/// for BMS 4.37+ (24 bytes per axis entry, 30 entries).
///
/// We only implement what we need right now:
/// - per-axis "assigned" and "invert" flags
/// - throttle detents (AB + IDLE) for Throttle and Throttle_Right
/// </summary>
public sealed class JoystickCalService
{
    // BMS 4.37+ joystick.cal axis order (matches stock launcher OverrideSettingFor437.getJoystickCalList()).
    // Slots that this rewrite does not model yet are left as null:
    // - index 10: FOV
    // - index 21: Camera Distance
    private static readonly AxisFunction?[] JoystickCalOrder =
    {
        AxisFunction.Pitch,
        AxisFunction.Roll,
        AxisFunction.Yaw,
        AxisFunction.Throttle,
        AxisFunction.Throttle_Right,
        AxisFunction.Trim_Pitch,
        AxisFunction.Trim_Yaw,
        AxisFunction.Trim_Roll,
        AxisFunction.Toe_Brake,
        AxisFunction.Toe_Brake_Right,
        null, // FOV (not modeled yet)
        AxisFunction.Radar_Antenna_Elevation,
        AxisFunction.Cursor_X,
        AxisFunction.Cursor_Y,
        AxisFunction.Range_Knob,
        AxisFunction.COMM_Channel_1,
        AxisFunction.COMM_Channel_2,
        AxisFunction.MSL_Volume,
        AxisFunction.Threat_Volume,
        AxisFunction.HUD_Brightness,
        AxisFunction.Reticle_Depression,
        null, // Camera Distance (not modeled yet)
        AxisFunction.IntercomVolumeVolume,
        AxisFunction.HMS_Brightness,
        AxisFunction.AI_vs_IVC,
        AxisFunction.FLIR_Brightness,
        AxisFunction.HSI_Course_Knob,
        AxisFunction.HSI_Heading_Knob,
        AxisFunction.Altimeter_Knob,
        AxisFunction.ILS_Volume_Knob,
    };

    public void Write(string baseDir, AxisMappingDatData axisDat, SetupXmlService setupXml, DeviceSortingService sorting)
    {
        var cfgDir = Path.Combine(baseDir, "User", "Config");
        Directory.CreateDirectory(cfgDir);

        var path = Path.Combine(cfgDir, "joystick.cal");

        // Resolve throttle detents from the assigned throttle device (if any).
        var throttleDetents = TryResolveDetentsForAxis(baseDir, AxisFunction.Throttle, axisDat, setupXml, sorting);

        // Right-throttle mirrors primary throttle detents.
        var throttleRightDetents =
            TryResolveDetentsForAxis(baseDir, AxisFunction.Throttle_Right, axisDat, setupXml, sorting)
            ?? throttleDetents;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);

        for (int i = 0; i < 30; i++)
        {
            byte[] block = CreateDefaultBlock();

            AxisFunction? f = (i >= 0 && i < JoystickCalOrder.Length) ? JoystickCalOrder[i] : null;
            if (f is null)
            {
                fs.Write(block, 0, block.Length);
                continue;
            }

            int mappingIndex = AxisCatalog.Get(f.Value).MappingIndex;
            var entry = axisDat.Entries.FirstOrDefault(e => e.Index == mappingIndex);

            bool assigned = entry is not null && entry.JoyNum != -1 && entry.AxisIndex != -1;
            bool invert = false;
            if (assigned)
                setupXml.TryGetInvert(baseDir, f.Value, out invert);

            // assigned + invert flags
            block[20] = assigned ? (byte)0x01 : (byte)0x00;
            block[21] = invert ? (byte)0x01 : (byte)0x00;

            // throttle detents
            if (assigned && f.Value == AxisFunction.Throttle && throttleDetents is not null)
                ApplyDetents(block, throttleDetents);
            else if (assigned && f.Value == AxisFunction.Throttle_Right && throttleRightDetents is not null)
                ApplyDetents(block, throttleRightDetents);

            fs.Write(block, 0, block.Length);
        }
    }

    private static byte[] CreateDefaultBlock()
    {
        // Matches the default bytes used by the stock launcher for 4.37+.
        // Keep the mid-block calibration constants (0x98, 0x3A).
        return new byte[24]
        {
            0x00,0x00,0x00,0x00,
            0x98,0x3A,0x00,0x00,
            0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00
        };
    }

    private static void ApplyDetents(byte[] block, DetentPosition detents)
    {
        // Copy of the stock launcher's conversion:
        // - detents are stored in Falcon-native 0..65535 logical space
        // - joystick.cal stores them in a 0..15000 scale with an inverted direction
        // - 16-bit LE at bytes 0-1 (AB) and 4-5 (IDLE)

        int fAB = detents.AB * 15000 / DetentPosition.AxisMax;
        int fIdle = detents.IDLE * 15000 / DetentPosition.AxisMax;

        fAB = 15000 - fAB;
        fIdle = 15000 - fIdle;

        if (fAB < 0) fAB = 0;
        if (fAB > 15000) fAB = 15000;
        if (fIdle < 0) fIdle = 0;
        if (fIdle > 15000) fIdle = 15000;

        // AB
        block[0] = (byte)(fAB & 0xFF);
        block[1] = (byte)((fAB >> 8) & 0xFF);
        block[2] = 0x00;
        block[3] = 0x00;

        // IDLE
        block[4] = (byte)(fIdle & 0xFF);
        block[5] = (byte)((fIdle >> 8) & 0xFF);
        block[6] = 0x00;
        block[7] = 0x00;
    }

    private static DetentPosition? TryResolveDetentsForAxis(
        string baseDir,
        AxisFunction axis,
        AxisMappingDatData axisDat,
        SetupXmlService setupXml,
        DeviceSortingService sorting)
    {
        int mappingIndex = AxisCatalog.Get(axis).MappingIndex;
        var entry = axisDat.Entries.FirstOrDefault(e => e.Index == mappingIndex);
        if (entry is null || entry.JoyNum == -1) return null;

        int slot = entry.JoyNum - 2;
        var devName = sorting.GetDeviceNameBySlot(baseDir, slot);
        if (string.IsNullOrWhiteSpace(devName)) return null;

        return setupXml.TryGetDetents(baseDir, devName, out var detents) ? detents : null;
    }
}