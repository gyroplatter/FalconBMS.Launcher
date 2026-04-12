using FalconBMS.Launcher.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Captures a snapshot of connected devices and detects whether the device list changed.
/// </summary>

public sealed class DeviceHotplugSnapshotService
{
    public sealed record Snapshot(IReadOnlyList<Guid> InstanceGuids);

    public Snapshot Capture()
    {
        using var di = new DirectInputManager();

        var ids = di.EnumerateDevices()
            .Select(x => x.InstanceGuid)
            .ToArray();

        return new Snapshot(ids);
    }

    public bool HasChanged(Snapshot? previous, Snapshot current)
    {
        if (previous is null)
            return true;

        if (previous.InstanceGuids.Count != current.InstanceGuids.Count)
            return true;

        for (int i = 0; i < current.InstanceGuids.Count; i++)
        {
            if (current.InstanceGuids[i] != previous.InstanceGuids[i])
                return true;
        }

        return false;
    }
}