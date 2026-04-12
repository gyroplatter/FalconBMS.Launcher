using System.IO;
using System.Text;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Locates installed theaters and saves/populates the theater list used by the Launcher.
/// </summary>

public sealed class TheaterDiscoveryService
{
    public IReadOnlyList<string> PopulateAndSave(string installDir)
    {
        // Match legacy path exactly
        var theaterLstPath = Path.Combine(installDir, "Data", "Terrdata", "TheaterDefinition", "theater.lst");

        // Backup path (you observed User\Config\Backup exists)
        var backupDir = Path.Combine(installDir, "User", "Config", "Backup");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, "theater.lst");

        // Backup once (same behavior: only if backup doesn't exist yet)
        if (!File.Exists(backupPath) && File.Exists(theaterLstPath))
            File.Copy(theaterLstPath, backupPath, overwrite: false);

        // Clear read-only attribute if file exists
        if (File.Exists(theaterLstPath))
        {
            var attrs = File.GetAttributes(theaterLstPath);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(theaterLstPath, attrs & ~FileAttributes.ReadOnly);
        }
        else
        {
            // Ensure directory exists before we write
            Directory.CreateDirectory(Path.GetDirectoryName(theaterLstPath)!);
        }

        // Recursively scan all subdirectories under /Data for *.tdf
        var dataRoot = Path.Combine(installDir, "Data");
        var theaterFiles = Directory.GetFiles(dataRoot, "*.tdf", SearchOption.AllDirectories);

        // Korea KTO should be at the top
        Array.Sort(theaterFiles, (a, b) =>
        {
            if (a.EndsWith("\\Korea KTO.tdf", StringComparison.OrdinalIgnoreCase)) return -1;
            if (b.EndsWith("\\Korea KTO.tdf", StringComparison.OrdinalIgnoreCase)) return +1;
            return StringComparer.OrdinalIgnoreCase.Compare(a, b);
        });

        // Write relative paths for all TDFs to theater.lst (relative to /Data)
        var relPaths = theaterFiles
            .Select(t => t.Substring(dataRoot.Length).TrimStart('\\'))
            .ToArray();

        File.WriteAllLines(theaterLstPath, relPaths);

        // Extract "name " line from each TDF and return list (preserve order)
        var theaters = new List<string>(theaterFiles.Length);

        foreach (var tdf in theaterFiles)
        {
            // Hack: ignore output from F4Patch
            if (tdf.Contains("F4Patch", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var line in File.ReadLines(tdf, Encoding.UTF8))
            {
                if (line.StartsWith("name ", StringComparison.OrdinalIgnoreCase))
                {
                    theaters.Add(line.Replace("name ", "", StringComparison.OrdinalIgnoreCase).Trim());
                    break;
                }
            }
        }

        return theaters;
    }
}