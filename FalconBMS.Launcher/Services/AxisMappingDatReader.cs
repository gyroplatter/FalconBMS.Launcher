using System;
using System.Collections.Generic;
using System.IO;
using FalconBMS.Launcher.Models;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Reads FalconBMS's axismapping.dat file and converts it into structured model data.
/// </summary>

public sealed class AxisMappingDatReader
{
    private const int HeaderSize = 24;
    private const int AxisCount = 30;
    private const int EntrySize = 16;
    private const int TotalSize = HeaderSize + AxisCount * EntrySize; // 504

    public string GetPath(string baseDir) =>
        Path.Combine(baseDir, "User", "Config", "axismapping.dat");

    public AxisMappingDatData? Read(string baseDir)
    {
        var path = GetPath(baseDir);
        if (!File.Exists(path)) return null;

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != TotalSize) return null; // if BMS changes format later, we'll version it

        int headerJoy = ReadInt32LE(bytes, 0);

        // .NET Framework 4.8 Guid does not accept Span<byte>, so copy the 16-byte GUID slice first.
        var headerGuidBytes = new byte[16];
        Buffer.BlockCopy(bytes, 4, headerGuidBytes, 0, 16);
        var headerGuid = new Guid(headerGuidBytes);

        int deviceCount = ReadInt32LE(bytes, 20);

        var entries = new List<AxisMapEntry>(AxisCount);
        for (int i = 0; i < AxisCount; i++)
        {
            int off = HeaderSize + i * EntrySize;
            entries.Add(new AxisMapEntry
            {
                Index = i,
                JoyNum = ReadInt32LE(bytes, off + 0),
                AxisIndex = ReadInt32LE(bytes, off + 4),
                Deadzone = ReadInt32LE(bytes, off + 8),
                Saturation = ReadInt32LE(bytes, off + 12),
            });
        }

        return new AxisMappingDatData
        {
            HeaderJoyNum = headerJoy,
            HeaderInstanceGuid = headerGuid,
            DeviceCount = deviceCount,
            Entries = entries
        };
    }

    private static int ReadInt32LE(byte[] b, int offset) =>
        b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24);
}