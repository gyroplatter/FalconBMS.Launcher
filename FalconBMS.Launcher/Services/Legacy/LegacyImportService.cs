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

    public bool HasLegacyAutoKeyFiles(
        string baseDir)
    {
        string configDirectory =
            GetConfigDirectory(baseDir);

        return
            File.Exists(
                Path.Combine(
                    configDirectory,
                    "BMS - Auto.key")) ||
            File.Exists(
                Path.Combine(
                    configDirectory,
                    "BMS - Auto-F15ABCD.key"));
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
        try
        {
            LegacyImportBackupResult backupResult =
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
                    "The current BMS Full key files could not be loaded.");
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
                ex.Message);
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

        int nextOfflineDiscoveryIndex =
            connectedMatches.Count;

        foreach (LegacyDeviceXmlFile legacyXml in
                 legacyXmlFiles.Where(xml =>
                     !usedLegacyXmlPaths.Contains(
                         xml.Path)))
        {
            LegacySortedDevice? sortedDevice =
                sortedDevices.FirstOrDefault(device =>
                    LegacyDeviceXmlImportService.NamesMatch(
                        device.ProductName,
                        legacyXml.DeviceName));

            if (sortedDevice is null)
            {
                DebugDiagnosticsService.Warn(
                    $"Legacy offline device skipped because it was not found in DeviceSorting.txt | Device=\"{legacyXml.DeviceName}\"");

                skippedItems.Add(
                    new LegacyImportSkippedItem
                    {
                        SourceName =
                            legacyXml.DeviceName,
                        ControlName =
                            "Device profile",
                        Reason =
                            "This disconnected device could not be identified and was not imported."
                    });

                continue;
            }

            string? stockXmlPath =
                _deviceXmlImporter.FindStockXmlByName(
                    legacyXml.DeviceName,
                    connectedMatches);

            bool legacyXmlReadable =
                _deviceXmlImporter.CanReadXml(
                    legacyXml.Path);

            if (!legacyXmlReadable &&
                string.IsNullOrWhiteSpace(stockXmlPath))
            {
                DebugDiagnosticsService.Warn(
                    $"Legacy offline device skipped because neither its legacy XML nor a stock XML could be read | Device=\"{legacyXml.DeviceName}\"");

                skippedItems.Add(
                    new LegacyImportSkippedItem
                    {
                        SourceName =
                            legacyXml.DeviceName,
                        ControlName =
                            "Device profile",
                        Reason =
                            "The existing device file could not be read and no stock profile was available."
                    });

                continue;
            }

            if (!legacyXmlReadable)
            {
                stockFallbackCount++;

                skippedItems.Add(
                    new LegacyImportSkippedItem
                    {
                        SourceName =
                            legacyXml.DeviceName,
                        ControlName =
                            "Device profile",
                        Reason =
                            "The existing device file could not be read. The stock profile was used instead."
                    });
            }

            int? duplicateSequenceNumber =
                GetDuplicateSequenceNumber(
                    sortedDevices,
                    sortedDevice);

            DeviceBindingProfile offlineProfile =
                _deviceXmlImporter.BuildOfflineProfile(
                    legacyXml,
                    sortedDevice,
                    stockXmlPath,
                    nextOfflineDiscoveryIndex++,
                    duplicateSequenceNumber,
                    skippedItems);

            profiles.Add(
                offlineProfile);

            if (legacyXmlReadable)
                legacyDeviceCount++;
        }

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
        return legacyXmlFiles.FirstOrDefault(xml =>
            !usedPaths.Contains(xml.Path) &&
            (
                xml.InstanceGuid ==
                match.Device.InstanceGuid ||
                LegacyDeviceXmlImportService.NamesMatch(
                    xml.DeviceName,
                    match.Device.ProductName) ||
                LegacyDeviceXmlImportService.NamesMatch(
                    xml.DeviceName,
                    match.Device.InstanceName)
            ));
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

    private static int? GetDuplicateSequenceNumber(
        IReadOnlyList<LegacySortedDevice> sortedDevices,
        LegacySortedDevice target)
    {
        List<LegacySortedDevice> duplicates =
            sortedDevices
                .Where(device =>
                    device.ProductGuid ==
                    target.ProductGuid)
                .OrderBy(device => device.Order)
                .ToList();

        if (duplicates.Count <= 1)
            return null;

        int index =
            duplicates.FindIndex(device =>
                ReferenceEquals(device, target));

        return index >= 0
            ? index + 1
            : null;
    }

    private static LegacyImportExecutionResult Failure(
        string errorMessage)
    {
        return new LegacyImportExecutionResult
        {
            Succeeded = false,
            ErrorMessage = errorMessage
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