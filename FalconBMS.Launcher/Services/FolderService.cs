using System.Diagnostics;
using System.IO;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Opens folders in Windows Explorer
/// </summary>

public sealed class FolderService
{
    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Folder not found: {path}");

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}