using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FalconBMS.Launcher.Services.Legacy;

public sealed class LegacyImportBackupService
{
    private const string BackupParentFolderName =
        "Launcher-Backups";

    private const string BackupFolderPrefix =
        "V2-to-V3-Import";

    private static readonly string[] BackupIncludePatterns =
    {
        "axismapping.dat",
        "joystick.cal",
        "DeviceSorting.txt",
        "Falcon BMS User.cfg",

        "BMS - Auto.key",
        "BMS - Auto-F15ABCD.key",

        "*.ini",
        "*.lbk",
        "*.plc",
        "*.pop",

        "Setup.v100.*.xml"
    };

    private static readonly string[] BackupExcludeFileNames =
    {
        "3DUISettings.ini",
        "EWS_Def.ini",
        "Feedback.ini",
        "HARM_Def.ini",
        "IFF_Def.ini",
        "MFD_Def.ini"
    };

    public LegacyImportBackupResult CreateBackup(
        string configDirectory)
    {
        if (string.IsNullOrWhiteSpace(configDirectory))
        {
            return LegacyImportBackupResult.Failed(
                "The BMS control folder could not be found.");
        }

        if (!Directory.Exists(configDirectory))
        {
            return LegacyImportBackupResult.Failed(
                "The BMS control folder does not exist.");
        }

        try
        {
            string backupDirectory =
                CreateBackupDirectory(
                    configDirectory);

            List<string> sourceFiles =
                FindFilesToBackup(
                    configDirectory);

            int filesCopied =
                0;

            foreach (string sourcePath in sourceFiles)
            {
                string fileName =
                    Path.GetFileName(
                        sourcePath);

                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                string destinationPath =
                    Path.Combine(
                        backupDirectory,
                        fileName);

                File.Copy(
                    sourcePath,
                    destinationPath,
                    overwrite: false);

                filesCopied++;
            }

            DebugDiagnosticsService.Info(
                $"Legacy import backup created. FilesCopied={filesCopied} Path={backupDirectory}");

            return LegacyImportBackupResult.Success(
                backupDirectory,
                filesCopied);
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "Legacy import backup failed.");

            return LegacyImportBackupResult.Failed(
                ex.Message);
        }
    }

    private static string CreateBackupDirectory(
        string configDirectory)
    {
        string timestamp =
            DateTime.Now.ToString(
                "yyyy-MM-dd_HH-mm-ss");

        string backupDirectory =
            Path.Combine(
                configDirectory,
                BackupParentFolderName,
                $"{BackupFolderPrefix}-{timestamp}");

        Directory.CreateDirectory(
            backupDirectory);

        return backupDirectory;
    }

    private static List<string> FindFilesToBackup(
        string configDirectory)
    {
        return BackupIncludePatterns
            .SelectMany(pattern =>
                Directory.GetFiles(
                    configDirectory,
                    pattern,
                    SearchOption.TopDirectoryOnly))
            .Where(path =>
                !ShouldExcludeFile(path))
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(path =>
                Path.GetFileName(path),
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ShouldExcludeFile(
        string path)
    {
        string fileName =
            Path.GetFileName(
                path);

        return BackupExcludeFileNames.Contains(
            fileName,
            StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class LegacyImportBackupResult
{
    public bool Succeeded { get; init; }

    public string BackupDirectory { get; init; } = "";

    public int FilesCopied { get; init; }

    public string ErrorMessage { get; init; } = "";

    public static LegacyImportBackupResult Success(
        string backupDirectory,
        int filesCopied)
    {
        return new LegacyImportBackupResult
        {
            Succeeded =
                true,
            BackupDirectory =
                backupDirectory,
            FilesCopied =
                filesCopied
        };
    }

    public static LegacyImportBackupResult Failed(
        string errorMessage)
    {
        return new LegacyImportBackupResult
        {
            Succeeded =
                false,
            ErrorMessage =
                errorMessage
        };
    }
}