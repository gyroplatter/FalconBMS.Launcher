using FalconBMS.Launcher.Models;
using System;
using System.IO;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Creates, reads, updates, and clears entries in axismapping.dat, including pitch/roll assignments
/// </summary>

public sealed class AxisMappingDatService
{
    // Falcon BMS uses a 504-byte axismapping.dat:
    // 24-byte header + 30 entries * 16 bytes each
    private const int HeaderSize = 24;
    private const int AxisCount = 30;
    private const int EntrySize = 16;
    private const int TotalSize = HeaderSize + AxisCount * EntrySize; // 504

    // In stock-style file, joy num stored is (device slot + 2)
    private const int JoyNumOffset = 2;

    // Batch edit support to avoid rewriting the file multiple times during bootstrap
    private byte[]? _batchBytes;
    private string? _batchPath;
    private bool _batchDirty;

    public string GetPath(string baseDir) =>
        Path.Combine(baseDir, "User", "Config", "axismapping.dat");

    public void BeginBatch(string baseDir)
    {
        var path = GetPath(baseDir);
        EnsureExists(baseDir);

        _batchPath = path;
        _batchBytes = File.ReadAllBytes(path);
        _batchDirty = false;
    }

    public void EndBatch()
    {
        if (_batchBytes is null || _batchPath is null)
            return;

        if (_batchDirty)
        {
            string before = DebugDiagnosticsService.GetFileSignature(_batchPath);
            DebugDiagnosticsService.Info("Overwriting AxisMapping.dat..");
            File.WriteAllBytes(_batchPath, _batchBytes);
            DebugDiagnosticsService.LogFileWriteResult("AxisMapping.dat", _batchPath, before, "AxisMappingDatService.EndBatch", "BatchFlush");
        }
        else
        {
            DebugDiagnosticsService.Info("FILE WRITE SKIPPED | File=AxisMapping.dat | Caller=AxisMappingDatService.EndBatch | Reason=BatchNotDirty");
        }

        _batchBytes = null;
        _batchPath = null;
        _batchDirty = false;
    }

    public void EnsureExists(string baseDir)
    {
        var path = GetPath(baseDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (File.Exists(path)) return;

        var bytes = new byte[TotalSize];

        // Header:
        // [0..3] header joy num (slot+2) or -1
        // [4..19] instance GUID bytes (16)
        // [20..23] deviceCount
        // Match original launcher behavior for "Pitch not assigned" header:
        // int32(1), then 16 zero GUID bytes, then deviceCount
        WriteInt32LE(bytes, 0, 1);
        // guid bytes are already 0
        WriteInt32LE(bytes, 20, 0);

        // Entries (30):
        // [0..3] joy = -1
        // [4..7] axis = -1
        // [8..11] deadzone = 100 (stock default)
        // [12..15] saturation = -1 (stock "none")
        for (int i = 0; i < AxisCount; i++)
        {
            int off = HeaderSize + i * EntrySize;
            WriteInt32LE(bytes, off + 0, -1);
            WriteInt32LE(bytes, off + 4, -1);
            WriteInt32LE(bytes, off + 8, 100);
            WriteInt32LE(bytes, off + 12, -1);
        }

        File.WriteAllBytes(path, bytes);
    }
    public AxisMappingDatData ReadAll(string baseDir)
    {
        EnsureExists(baseDir);

        var path = GetPath(baseDir);
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length != TotalSize)
            throw new InvalidDataException($"axismapping.dat size {bytes.Length} unexpected (expected {TotalSize}).");

        int headerJoyNum = ReadInt32LE(bytes, 0);
        Guid headerGuid = ReadGuid(bytes, 4);
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
                Saturation = ReadInt32LE(bytes, off + 12)
            });
        }

        return new AxisMappingDatData
        {
            DeviceCount = deviceCount,
            HeaderJoyNum = headerJoyNum,
            HeaderInstanceGuid = headerGuid,
            Entries = entries
        };
    }

    // ---------- Generic 0..29 API ----------

    public void SetAxisMapping(
        string baseDir,
        int mappingIndex,
        int deviceSlotIndex,
        Guid primaryInstanceGuidForHeader,
        int physicalAxisIndex,
        int deviceCount,
        int? deadzone = null,
        int? saturation = null,
        bool updateHeaderPrimary = false)
    {
        DebugDiagnosticsService.Info($"[AxisMapping] WRITE mappingIndex={mappingIndex} slot={deviceSlotIndex} axis={physicalAxisIndex}");

        if (mappingIndex < 0 || mappingIndex >= AxisCount)
            throw new ArgumentOutOfRangeException(nameof(mappingIndex), $"Must be 0..{AxisCount - 1}");

        EnsureExists(baseDir);

        byte[] bytes;
        string path;

        // Use batch buffer if active, otherwise read from disk (original behavior)
        if (_batchBytes is not null && _batchPath is not null)
        {
            bytes = _batchBytes;
            path = _batchPath;
        }
        else
        {
            path = GetPath(baseDir);
            bytes = File.ReadAllBytes(path);
        }

        if (bytes.Length != TotalSize)
            throw new InvalidDataException($"axismapping.dat size {bytes.Length} unexpected (expected {TotalSize}).");

        WriteInt32LE(bytes, 20, deviceCount);

        bool headerEmptyGuid = true;
        for (int i = 4; i < 20; i++)
        {
            if (bytes[i] != 0) { headerEmptyGuid = false; break; }
        }

        if (updateHeaderPrimary || headerEmptyGuid)
        {
            WriteInt32LE(bytes, 0, deviceSlotIndex + JoyNumOffset);
            Buffer.BlockCopy(primaryInstanceGuidForHeader.ToByteArray(), 0, bytes, 4, 16);
        }

        int entryOff = HeaderSize + mappingIndex * EntrySize;

        int dz = deadzone ?? ReadInt32LE(bytes, entryOff + 8);
        int sat = saturation ?? ReadInt32LE(bytes, entryOff + 12);

        WriteInt32LE(bytes, entryOff + 0, deviceSlotIndex + JoyNumOffset);
        WriteInt32LE(bytes, entryOff + 4, physicalAxisIndex);
        WriteInt32LE(bytes, entryOff + 8, dz);
        WriteInt32LE(bytes, entryOff + 12, sat);

        if (_batchBytes is not null)
        {
            _batchDirty = true;
        }
        else
        {
            string before = DebugDiagnosticsService.GetFileSignature(path);
            DebugDiagnosticsService.Info("Overwriting AxisMapping.dat..");
            File.WriteAllBytes(path, bytes);
            DebugDiagnosticsService.LogFileWriteResult("AxisMapping.dat", path, before, "AxisMappingDatService.SetAxisMapping", $"MappingIndex={mappingIndex}");
        }
    }

    public void ClearAxisMapping(string baseDir, int mappingIndex)
    {
        DebugDiagnosticsService.Info($"[AxisMapping] CLEAR mappingIndex={mappingIndex}");
        if (mappingIndex < 0 || mappingIndex >= AxisCount)
            throw new ArgumentOutOfRangeException(nameof(mappingIndex), $"Must be 0..{AxisCount - 1}");

        EnsureExists(baseDir);

        var path = GetPath(baseDir);
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length != TotalSize)
            throw new InvalidDataException($"axismapping.dat size {bytes.Length} unexpected (expected {TotalSize}).");

        int entryOff = HeaderSize + mappingIndex * EntrySize;

        WriteInt32LE(bytes, entryOff + 0, -1);
        WriteInt32LE(bytes, entryOff + 4, -1);
        WriteInt32LE(bytes, entryOff + 8, 100);
        WriteInt32LE(bytes, entryOff + 12, -1);

        if (mappingIndex == 0)
        {
            WriteInt32LE(bytes, 0, 1);
            for (int i = 4; i < 20; i++) bytes[i] = 0;
        }

        string before = DebugDiagnosticsService.GetFileSignature(path);
        DebugDiagnosticsService.Info("Overwriting AxisMapping.dat..");
        File.WriteAllBytes(path, bytes);
        DebugDiagnosticsService.LogFileWriteResult("AxisMapping.dat", path, before, "AxisMappingDatService.ClearAxisMapping", $"MappingIndex={mappingIndex}");
    }

    public (int JoyNum, int AxisIndex, int Deadzone, int Saturation)? ReadAxisMapping(string baseDir, int mappingIndex)
    {
        if (mappingIndex < 0 || mappingIndex >= AxisCount)
            throw new ArgumentOutOfRangeException(nameof(mappingIndex), $"Must be 0..{AxisCount - 1}");

        var path = GetPath(baseDir);
        if (!File.Exists(path)) return null;

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != TotalSize) return null;

        int entryOff = HeaderSize + mappingIndex * EntrySize;

        int joy = ReadInt32LE(bytes, entryOff + 0);
        int axis = ReadInt32LE(bytes, entryOff + 4);
        int dz = ReadInt32LE(bytes, entryOff + 8);
        int sat = ReadInt32LE(bytes, entryOff + 12);

        if (joy < 0 || axis < 0) return null;
        return (joy, axis, dz, sat);
    }

    // ---------- Compatibility wrappers (keep your current Roll/Pitch UI working) ----------

    /// <summary>
    /// Writes Pitch or Roll mapping (Stock: Pitch=0, Roll=1).
    /// </summary>
    public void SetPitchOrRoll(
        string baseDir,
        bool isPitch,
        int deviceSlotIndex,
        Guid primaryInstanceGuidForHeader,
        int physicalAxisIndex,
        int deviceCount)
    {
        int mappingIndex = isPitch ? 0 : 1;

        SetAxisMapping(
            baseDir: baseDir,
            mappingIndex: mappingIndex,
            deviceSlotIndex: deviceSlotIndex,
            primaryInstanceGuidForHeader: primaryInstanceGuidForHeader,
            physicalAxisIndex: physicalAxisIndex,
            deviceCount: deviceCount,
            deadzone: null,
            saturation: null,
            updateHeaderPrimary: isPitch);
    }

    public void ClearPitchOrRoll(string baseDir, bool isPitch)
    {
        int mappingIndex = isPitch ? 0 : 1;
        ClearAxisMapping(baseDir, mappingIndex);
    }

    private static int ReadInt32LE(byte[] b, int offset) =>
        b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24);
    private static Guid ReadGuid(byte[] b, int offset)
    {
        var guidBytes = new byte[16];
        Buffer.BlockCopy(b, offset, guidBytes, 0, 16);
        return new Guid(guidBytes);
    }
    private static void WriteInt32LE(byte[] b, int offset, int value)
    {
        unchecked
        {
            b[offset + 0] = (byte)(value & 0xFF);
            b[offset + 1] = (byte)((value >> 8) & 0xFF);
            b[offset + 2] = (byte)((value >> 16) & 0xFF);
            b[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}