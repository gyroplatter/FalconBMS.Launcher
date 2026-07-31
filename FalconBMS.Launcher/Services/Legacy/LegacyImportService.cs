using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Models.Legacy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FalconBMS.Launcher.Services.Legacy;

public sealed class LegacyImportService
{
    private readonly KeyCatalogService
        _keyCatalogService = new();

    private readonly BindingModelBuilderService
        _bindingModelBuilder = new();

    private readonly DeviceDiscoveryService
        _deviceDiscovery = new();

    private readonly LegacyImportBackupService
        _backupService = new();

    private readonly LegacyAutoKeyImportService
        _autoKeyImporter = new();

    private readonly LegacyDeviceSortingImportService
        _deviceSortingImporter = new();

    private readonly LegacyDeviceXmlImportService
        _deviceXmlImporter = new();

    private readonly LegacyUserCfgImportService
        _userCfgImporter = new();

    private readonly JsonKeyboardBindingWriterService
        _keyboardJsonWriter = new();

    private readonly DeviceJsonWriterService
        _deviceJsonWriter = new();

    public bool HasLegacyControlFiles(
        string baseDir)
    {
        string configDirectory =
            GetConfigDirectory(baseDir);

        if (!Directory.Exists(configDirectory))
            return false;

        // Only actual legacy control files should trigger the automatic import.
        //
        // SearchOption.TopDirectoryOnly also makes it explicit that archived
        // files inside backup or comparison folders must not trigger an import.
        return
            File.Exists(
                Path.Combine(
                    configDirectory,
                    "BMS - Auto.key")) ||
            File.Exists(
                Path.Combine(
                    configDirectory,
                    "BMS - Auto-F15ABCD.key")) ||
            File.Exists(
                Path.Combine(
                    configDirectory,
                    "axismapping.dat")) ||
            File.Exists(
                Path.Combine(
                    configDirectory,
                    "joystick.cal")) ||
            File.Exists(
                Path.Combine(
                    configDirectory,
                    "DeviceSorting.txt")) ||
            Directory.EnumerateFiles(
                    configDirectory,
                    "Setup.v100.*.xml",
                    SearchOption.TopDirectoryOnly)
                .Any();
    }

    public LegacyImportScanResult Scan(
        string baseDir)
    {
        string configDirectory =
            GetConfigDirectory(baseDir);

        IReadOnlyList<StockDeviceSetupMatch>
            connectedMatches =
                _deviceDiscovery
                    .DiscoverAndMatchStockXml(baseDir);

        IReadOnlyList<LegacyDeviceXmlFile>
            legacyXmlFiles =
                _deviceXmlImporter
                    .FindLegacyXmlFiles(configDirectory);

        var result = new LegacyImportScanResult
        {
            ConfigDirectory = configDirectory,
            F16AutoKeyPath =
                GetExistingPath(
                    configDirectory,
                    "BMS - Auto.key"),
            F15AutoKeyPath =
                GetExistingPath(
                    configDirectory,
                    "BMS - Auto-F15ABCD.key"),
            DeviceSortingPath =
                GetExistingPath(
                    configDirectory,
                    "DeviceSorting.txt"),
            UserCfgPath =
                GetExistingPath(
                    configDirectory,
                    "Falcon BMS User.cfg")
        };

        foreach (LegacyDeviceXmlFile legacyXml in legacyXmlFiles)
        {
            bool readable =
                _deviceXmlImporter
                    .CanReadXml(legacyXml.Path);

            string? stockXmlPath =
                _deviceXmlImporter.FindStockXmlByName(
                    legacyXml.DeviceName,
                    connectedMatches);

            result.Devices.Add(
                new LegacyImportDeviceScanResult
                {
                    DeviceName =
                        legacyXml.DeviceName,
                    LegacyXmlPath =
                        legacyXml.Path,
                    LegacyXmlIsReadable =
                        readable,
                    HasMatchingStockXml =
                        !string.IsNullOrWhiteSpace(
                            stockXmlPath),
                    StockXmlPath =
                        stockXmlPath
                });
        }

        if (!result.HasAnyAutoKey)
        {
            result.Warnings.Add(
                "No Alternative Launcher AUTO key files were found.");
        }

        return result;
    }

    public LegacyImportExecutionResult Import(
        string baseDir,
        LegacyImportScanResult scanResult)
    {
        LegacyImportBackupResult? backupResult =
            null;

        try
        {
            backupResult =
                _backupService.CreateBackup(
                    scanResult.ConfigDirectory);

            if (!backupResult.Succeeded)
            {
                return Failure(
                    "The old Launcher control files could not be backed up. " +
                    "Import was stopped before any changes were made.\n\n" +
                    backupResult.ErrorMessage);
            }

            IReadOnlyList<KeyCatalog> catalogs =
                _keyCatalogService.LoadForInstall(
                    baseDir);

            if (catalogs.Count == 0)
            {
                return Failure(
                    "The current BMS Full key files could not be loaded.",
                    backupResult.BackupDirectory,
                    backupResult.FilesCopied);
            }

            BindingModel bindingModel =
                _bindingModelBuilder.Build(
                    catalogs);

            int keyboardAssignmentsImported =
                0;

            var missingCallbacks =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            string f16AutoKeyPath =
                scanResult.F16AutoKeyPath ?? "";

            if (!string.IsNullOrWhiteSpace(
                    f16AutoKeyPath))
            {
                LegacyAutoKeyImportResult f16Result =
                    _autoKeyImporter.Apply(
                        f16AutoKeyPath,
                        "F-16",
                        bindingModel);

                keyboardAssignmentsImported +=
                    f16Result.AssignmentsImported;

                missingCallbacks.UnionWith(
                    f16Result.MissingCallbacks);
            }

            string f15AutoKeyPath =
                scanResult.F15AutoKeyPath ?? "";

            if (!string.IsNullOrWhiteSpace(
                    f15AutoKeyPath))
            {
                LegacyAutoKeyImportResult f15Result =
                    _autoKeyImporter.Apply(
                        f15AutoKeyPath,
                        "F-15ABCD",
                        bindingModel);

                keyboardAssignmentsImported +=
                    f15Result.AssignmentsImported;

                missingCallbacks.UnionWith(
                    f15Result.MissingCallbacks);
            }

            IReadOnlyList<StockDeviceSetupMatch> connectedMatches =
                _deviceDiscovery
                    .DiscoverAndMatchStockXml(
                        baseDir);

            IReadOnlyList<LegacyDeviceXmlFile> legacyXmlFiles =
                _deviceXmlImporter
                    .FindLegacyXmlFiles(
                        scanResult.ConfigDirectory);

            string deviceSortingPath =
                scanResult.DeviceSortingPath ?? "";

            IReadOnlyList<LegacySortedDevice> sortedDevices =
                !string.IsNullOrWhiteSpace(
                    deviceSortingPath)
                    ? _deviceSortingImporter.Read(
                        deviceSortingPath)
                    : Array.Empty<LegacySortedDevice>();

            int legacyDeviceCount =
                0;

            int stockFallbackCount =
                0;

            var skippedItems =
                new List<LegacyImportSkippedItem>();

            List<DeviceBindingProfile> deviceProfiles =
                BuildDeviceProfiles(
                    connectedMatches,
                    legacyXmlFiles,
                    sortedDevices,
                    skippedItems,
                    ref legacyDeviceCount,
                    ref stockFallbackCount);

            bindingModel.DeviceProfiles.Clear();

            bindingModel.DeviceProfiles.AddRange(
                deviceProfiles);

            string userCfgPath =
                scanResult.UserCfgPath ?? "";

            LegacyUserCfgImportResult userCfgResult =
                !string.IsNullOrWhiteSpace(
                    userCfgPath)
                    ? _userCfgImporter.Read(
                        userCfgPath)
                    : new LegacyUserCfgImportResult();

            _userCfgImporter.ApplyCurves(
                bindingModel,
                userCfgResult.AxisCurves);

            _keyboardJsonWriter.Write(
                baseDir,
                bindingModel);

            _deviceJsonWriter.Write(
                baseDir,
                bindingModel.DeviceProfiles);

            var result =
                new LegacyImportExecutionResult
                {
                    Succeeded =
                        true,
                    ImportedBindingModel =
                        bindingModel,
                    ExportRttTextures =
                        userCfgResult.ExportRttTexturesFound &&
                        userCfgResult.ExportRttTextures,
                    KeyboardAssignmentsImported =
                        keyboardAssignmentsImported,
                    DevicesImportedFromLegacyXml =
                        legacyDeviceCount,
                    DevicesUsingStockFallback =
                        stockFallbackCount,
                    MissingCallbacksSkipped =
                        missingCallbacks.Count,
                    BackupDirectory =
                        backupResult.BackupDirectory,
                    BackupFilesCopied =
                        backupResult.FilesCopied
                };

            result.SkippedItems.AddRange(
                skippedItems);

            foreach (string callbackName in
                     missingCallbacks.OrderBy(name => name))
            {
                result.SkippedItems.Add(
                    new LegacyImportSkippedItem
                    {
                        SourceName =
                            "Keyboard controls",
                        ControlName =
                            callbackName,
                        Reason =
                            "This control is not available in the current BMS key files."
                    });
            }

            if (missingCallbacks.Count > 0)
            {
                result.Warnings.Add(
                    $"{missingCallbacks.Count} controls were skipped because " +
                    "they are not available in the current BMS Full key files.");
            }

            return result;
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "Legacy control import failed.");

            return Failure(
                ex.Message,
                backupResult?.BackupDirectory ?? "",
                backupResult?.FilesCopied ?? 0);
        }
    }

    private List<DeviceBindingProfile> BuildDeviceProfiles(
        IReadOnlyList<StockDeviceSetupMatch> connectedMatches,
        IReadOnlyList<LegacyDeviceXmlFile> legacyXmlFiles,
        IReadOnlyList<LegacySortedDevice> sortedDevices,
        ICollection<LegacyImportSkippedItem> skippedItems,
        ref int legacyDeviceCount,
        ref int stockFallbackCount)
    {
        var profiles =
            new List<DeviceBindingProfile>();

        var usedLegacyXmlPaths =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (StockDeviceSetupMatch match in
                 connectedMatches)
        {
            LegacyDeviceXmlFile? legacyXml =
                FindBestLegacyXml(
                    match,
                    legacyXmlFiles,
                    usedLegacyXmlPaths);

            if (legacyXml is not null)
            {
                usedLegacyXmlPaths.Add(
                    legacyXml.Path);
            }

            bool readableLegacyXml =
                legacyXml is not null &&
                _deviceXmlImporter.CanReadXml(
                    legacyXml.Path);

            DeviceBindingProfile profile =
                _deviceXmlImporter.BuildConnectedProfile(
                    match,
                    readableLegacyXml
                        ? legacyXml!.Path
                        : null,
                    skippedItems);

            if (readableLegacyXml)
            {
                legacyDeviceCount++;
            }
            else if (legacyXml is not null &&
                     match.HasStockXml)
            {
                stockFallbackCount++;

                skippedItems.Add(
                    new LegacyImportSkippedItem
                    {
                        SourceName =
                            match.Device.ProductName,
                        ControlName =
                            "Device profile",
                        Reason =
                            "The existing device file could not be read. The stock profile was used instead."
                    });
            }
            else if (legacyXml is not null)
            {
                skippedItems.Add(
                    new LegacyImportSkippedItem
                    {
                        SourceName =
                            match.Device.ProductName,
                        ControlName =
                            "Device profile",
                        Reason =
                            "The existing device file could not be read and no stock profile was available."
                    });
            }

            profiles.Add(
                profile);
        }

        LogIgnoredDisconnectedLegacyXmlFiles(
            legacyXmlFiles,
            usedLegacyXmlPaths);

        return profiles
            .OrderBy(profile =>
                GetLegacyOrder(
                    profile,
                    sortedDevices))
            .ThenBy(profile =>
                profile.DiscoveryIndex)
            .ToList();
    }

    private static LegacyDeviceXmlFile? FindBestLegacyXml(
        StockDeviceSetupMatch match,
        IReadOnlyList<LegacyDeviceXmlFile> legacyXmlFiles,
        ISet<string> usedPaths)
    {
        List<LegacyDeviceXmlFile> unusedXmlFiles =
            legacyXmlFiles
                .Where(xml =>
                    !usedPaths.Contains(
                        xml.Path))
                .ToList();

        LegacyDeviceXmlFile? exactInstanceGuidMatch =
            unusedXmlFiles
                .Where(xml =>
                    xml.InstanceGuid ==
                    match.Device.InstanceGuid)
                .OrderByDescending(xml =>
                    GetLegacyXmlLastWriteTimeUtc(
                        xml.Path))
                .ThenBy(xml =>
                    xml.Path,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

        if (exactInstanceGuidMatch is not null)
        {
            int sameNameCandidateCount =
                CountSameNameCandidates(
                    match,
                    unusedXmlFiles);

            LogLegacyXmlSelection(
                match,
                exactInstanceGuidMatch,
                sameNameCandidateCount,
                "InstanceGuid");

            return exactInstanceGuidMatch;
        }

        List<LegacyDeviceXmlFile> sameNameCandidates =
            unusedXmlFiles
                .Where(xml =>
                    LegacyDeviceXmlImportService.NamesMatch(
                        xml.DeviceName,
                        match.Device.ProductName) ||
                    LegacyDeviceXmlImportService.NamesMatch(
                        xml.DeviceName,
                        match.Device.InstanceName))
                .ToList();

        if (sameNameCandidates.Count == 0)
        {
            DebugDiagnosticsService.Info(
                $"Legacy XML not found for connected device | Device=\"{match.Device.ProductName}\" | InstanceGuid={match.Device.InstanceGuid}");

            return null;
        }

        LegacyDeviceXmlFile newestSameNameXml =
            sameNameCandidates
                .OrderByDescending(xml =>
                    GetLegacyXmlLastWriteTimeUtc(
                        xml.Path))
                .ThenBy(xml =>
                    xml.Path,
                    StringComparer.OrdinalIgnoreCase)
                .First();

        LogLegacyXmlSelection(
            match,
            newestSameNameXml,
            sameNameCandidates.Count,
            "NewestSameName");

        return newestSameNameXml;
    }

    private static int CountSameNameCandidates(
        StockDeviceSetupMatch match,
        IEnumerable<LegacyDeviceXmlFile> legacyXmlFiles)
    {
        return legacyXmlFiles.Count(xml =>
            LegacyDeviceXmlImportService.NamesMatch(
                xml.DeviceName,
                match.Device.ProductName) ||
            LegacyDeviceXmlImportService.NamesMatch(
                xml.DeviceName,
                match.Device.InstanceName));
    }

    private static void LogLegacyXmlSelection(
        StockDeviceSetupMatch match,
        LegacyDeviceXmlFile selectedXml,
        int sameNameCandidateCount,
        string matchMethod)
    {
        DebugDiagnosticsService.Info(
            $"Legacy XML selected for connected device | MatchMethod={matchMethod} | Device=\"{match.Device.ProductName}\" | CurrentInstanceGuid={match.Device.InstanceGuid} | XmlDevice=\"{selectedXml.DeviceName}\" | XmlInstanceGuid={selectedXml.InstanceGuid} | SameNameCandidates={sameNameCandidateCount} | LastWriteUtc={GetLegacyXmlLastWriteTimeUtc(selectedXml.Path):O} | Xml=\"{selectedXml.Path}\"");

        if (sameNameCandidateCount <= 1)
            return;

        DebugDiagnosticsService.Info(
            $"Legacy duplicate same-name XMLs detected for connected device. Only the selected XML will be imported; remaining same-name XMLs will not create offline devices. | Device=\"{match.Device.ProductName}\" | SameNameCandidates={sameNameCandidateCount}");
    }

    private static void LogIgnoredDisconnectedLegacyXmlFiles(
        IReadOnlyList<LegacyDeviceXmlFile> legacyXmlFiles,
        ISet<string> usedPaths)
    {
        foreach (LegacyDeviceXmlFile ignoredXml in
                 legacyXmlFiles
                     .Where(xml =>
                         !usedPaths.Contains(
                             xml.Path))
                     .OrderBy(xml =>
                         xml.DeviceName,
                         StringComparer.OrdinalIgnoreCase)
                     .ThenByDescending(xml =>
                         GetLegacyXmlLastWriteTimeUtc(
                             xml.Path))
                     .ThenBy(xml =>
                         xml.Path,
                         StringComparer.OrdinalIgnoreCase))
        {
            DebugDiagnosticsService.Info(
                $"Legacy XML ignored because v2-to-v3 import only imports currently connected devices | Device=\"{ignoredXml.DeviceName}\" | XmlInstanceGuid={ignoredXml.InstanceGuid} | LastWriteUtc={GetLegacyXmlLastWriteTimeUtc(ignoredXml.Path):O} | Xml=\"{ignoredXml.Path}\"");
        }
    }

    private static DateTime GetLegacyXmlLastWriteTimeUtc(
        string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(
                path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static int GetLegacyOrder(
        DeviceBindingProfile profile,
        IReadOnlyList<LegacySortedDevice> sortedDevices)
    {
        LegacySortedDevice? sortedDevice =
            sortedDevices.FirstOrDefault(device =>
                device.ProductGuid ==
                profile.ProductGuid &&
                LegacyDeviceXmlImportService.NamesMatch(
                    device.ProductName,
                    profile.ProductName));

        return sortedDevice?.Order ??
               int.MaxValue;
    }

    private static LegacyImportExecutionResult Failure(
        string errorMessage,
        string backupDirectory = "",
        int backupFilesCopied = 0)
    {
        return new LegacyImportExecutionResult
        {
            Succeeded =
                false,
            ErrorMessage =
                errorMessage,
            BackupDirectory =
                backupDirectory,
            BackupFilesCopied =
                backupFilesCopied
        };
    }

    private static string GetConfigDirectory(
        string baseDir)
    {
        return Path.Combine(
            baseDir,
            "User",
            "Config");
    }

    private static string? GetExistingPath(
        string directory,
        string fileName)
    {
        string path =
            Path.Combine(
                directory,
                fileName);

        return File.Exists(path)
            ? path
            : null;
    }
}