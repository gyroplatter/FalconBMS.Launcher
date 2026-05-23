using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FalconBMS.Launcher.Services;

public sealed class LegacyDeviceSortingWriterService
{
    private static readonly Regex OldNameSanitizeRx =
        new(@"[^A-Za-z0-9\~\`\[\]\{\}\-_\=\'\x20]", RegexOptions.Compiled);

    public void Write(string baseDir, IReadOnlyList<DeviceBindingProfile> deviceProfiles)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("DEVSORT");

        string configDir = Path.Combine(baseDir, "User", "Config");
        Directory.CreateDirectory(configDir);

        string path = Path.Combine(configDir, "DeviceSorting.txt");
        string beforeSignature = DebugDiagnosticsService.GetFileSignature(path);

        IReadOnlyList<DeviceBindingProfile> connectedProfiles = deviceProfiles
            .Where(profile => profile.IsConnected)
            .ToList();

        if (connectedProfiles.Count != deviceProfiles.Count)
        {
            DebugDiagnosticsService.Warn(
                $"DeviceSorting write is excluding offline devices. Connected={connectedProfiles.Count} Total={deviceProfiles.Count} | ActionId={actionId}");
        }

        string content = BuildContent(connectedProfiles);

        if (File.Exists(path))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);

        if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            File.WriteAllText(
                path,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        DebugDiagnosticsService.LogFileWriteResult(
            "DeviceSorting.txt",
            path,
            beforeSignature,
            "LegacyDeviceSortingWriterService.Write",
            $"DeviceCount={deviceProfiles.Count}",
            actionId);
    }

    private static string BuildContent(IReadOnlyList<DeviceBindingProfile> deviceProfiles)
    {
        var sb = new StringBuilder();

        foreach (DeviceBindingProfile profile in deviceProfiles)
        {
            string name = SanitizeDeviceName(profile.ProductName);
            sb.Append('{');
            sb.Append(profile.ProductGuid.ToString().ToUpperInvariant());
            sb.Append("} \"");
            sb.Append(name);
            sb.AppendLine("\"");
        }

        return sb.ToString();
    }

    private static string SanitizeDeviceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return OldNameSanitizeRx.Replace(value, "").Trim();
    }
}