using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FalconBMS.Launcher.Services;

public sealed class LegacyAxisMappingDatWriterService
{
    private const int HeaderSize = 24;
    private const int AxisCount = 30;
    private const int EntrySize = 16;
    private const int TotalSize = HeaderSize + AxisCount * EntrySize;
    private const int JoyNumOffset = 2;

    public void Write(string baseDir, IReadOnlyList<DeviceBindingProfile> deviceProfiles)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("AXISDAT");

        string configDir = Path.Combine(baseDir, "User", "Config");
        Directory.CreateDirectory(configDir);

        string path = Path.Combine(configDir, "axismapping.dat");
        string beforeSignature = DebugDiagnosticsService.GetFileSignature(path);

        byte[] bytes = BuildAxisMappingBytes(deviceProfiles);

        if (File.Exists(path))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);

        if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes))
            File.WriteAllBytes(path, bytes);

        DebugDiagnosticsService.LogFileWriteResult(
            "axismapping.dat",
            path,
            beforeSignature,
            "LegacyAxisMappingDatWriterService.Write",
            $"DeviceCount={deviceProfiles.Count}",
            actionId);
    }

    private byte[] BuildAxisMappingBytes(IReadOnlyList<DeviceBindingProfile> deviceProfiles)
    {
        var bytes = new byte[TotalSize];

        // Match legacy unassigned header default.
        WriteInt32LE(bytes, 0, 1);
        WriteInt32LE(bytes, 20, deviceProfiles.Count);

        for (int i = 0; i < AxisCount; i++)
        {
            int offset = HeaderSize + i * EntrySize;

            WriteInt32LE(bytes, offset + 0, -1);
            WriteInt32LE(bytes, offset + 4, -1);
            WriteInt32LE(bytes, offset + 8, 100);
            WriteInt32LE(bytes, offset + 12, -1);
        }

        bool headerWritten = false;

        for (int deviceSlotIndex = 0; deviceSlotIndex < deviceProfiles.Count; deviceSlotIndex++)
        {
            DeviceBindingProfile profile = deviceProfiles[deviceSlotIndex];

            foreach (DeviceAxisBinding axis in profile.AxisBindings)
            {
                if (!axis.PhysicalAxisIndex.HasValue)
                    continue;

                if (!AxisDefinitionService.TryGetMappingIndex(axis.LogicalAxisName, out int mappingIndex))
                    continue;

                int offset = HeaderSize + mappingIndex * EntrySize;

                WriteInt32LE(bytes, offset + 0, deviceSlotIndex + JoyNumOffset);
                WriteInt32LE(bytes, offset + 4, axis.PhysicalAxisIndex.Value);
                WriteInt32LE(bytes, offset + 8, ConvertDeadzone(axis.Deadzone));
                WriteInt32LE(bytes, offset + 12, ConvertSaturation(axis.Saturation));

                if (!headerWritten || mappingIndex == 0)
                {
                    WriteInt32LE(bytes, 0, deviceSlotIndex + JoyNumOffset);
                    Buffer.BlockCopy(profile.InstanceGuid.ToByteArray(), 0, bytes, 4, 16);
                    headerWritten = true;
                }
            }
        }

        return bytes;
    }

    private static int ConvertDeadzone(string value)
    {
        return value switch
        {
            "Small" => 100,
            "Medium" => 500,
            "Large" => 1000,
            _ => 0
        };
    }

    private static int ConvertSaturation(string value)
    {
        return value switch
        {
            "Small" => 9500,
            "Medium" => 9000,
            "Large" => 8500,
            _ => -1
        };
    }

    private static void WriteInt32LE(byte[] bytes, int offset, int value)
    {
        unchecked
        {
            bytes[offset + 0] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
            bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}