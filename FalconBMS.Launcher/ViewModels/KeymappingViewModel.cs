using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Models.Keymapping;
using FalconBMS.Launcher.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Main keymapping screen view model for loading profiles, filtering rows, importing overrides,
/// and embedding axis rows into the same table.
/// </summary>
public sealed class KeymappingViewModel : ViewModelBase
{
    private readonly Func<BmsInstall?> _getSelectedInstall;
    private readonly SetupXmlKeymapReader _setupKeymap = new();
    private readonly KeymappingGridBuilderService _gridBuilder = new();
    private readonly AxisBindingsSnapshotService _axisSnapshot = new();
    private readonly AxisMappingDatService _axisDat = new();

    private readonly List<KeymappingGridBuilderService.SectionGroup> _sections = new();

    private string? _importedF16KeyPath;
    private string? _importedF15KeyPath;

    private KeyProfile _selectedProfile = KeyProfile.F16;
    public KeyProfile SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value)) return;
            RefreshFromDisk();
        }
    }

    private KeymappingCategoryOption? _selectedCategory;
    public KeymappingCategoryOption? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!Set(ref _selectedCategory, value)) return;
            ApplyRowFilter();
        }
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value)) return;
            ApplyRowFilter();
        }
    }

    private KeymappingGridRowViewModel? _selectedRow;
    public KeymappingGridRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!Set(ref _selectedRow, value)) return;

            if (value is null)
            {
                AssignmentText = "KEYSEARCH MODE";
                return;
            }

            if (value.IsAxisRow && value.AxisRow is not null)
            {
                AssignmentText = value.AxisRow.BindingText;
                return;
            }

            if (value.IsKeyRow && value.KeyRow is not null)
            {
                AssignmentText = value.Mapping.Trim();
                return;
            }

            AssignmentText = "KEYSEARCH MODE";
        }
    }

    private string _assignmentText = "KEYSEARCH MODE";
    public string AssignmentText
    {
        get => _assignmentText;
        set => Set(ref _assignmentText, value);
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public ObservableCollection<KeyProfile> Profiles { get; } =
        new(new[] { KeyProfile.F16, KeyProfile.F15ABCD });

    public ObservableCollection<KeymappingCategoryOption> Categories { get; } = new();

    public ObservableCollection<KeymappingGridRowViewModel> Rows { get; } = new();

    public ObservableCollection<KeyAssgn> KeyRows { get; } = new();

    public KeymappingViewModel(Func<BmsInstall?> getSelectedInstall)
    {
        _getSelectedInstall = getSelectedInstall;
    }

    public void ImportKeyFile(string keyFilePath)
    {
        if (SelectedProfile == KeyProfile.F15ABCD)
            _importedF15KeyPath = keyFilePath;
        else
            _importedF16KeyPath = keyFilePath;

        RefreshFromDisk();
    }

    public void ClearImportedOverride(KeyProfile profile)
    {
        if (profile == KeyProfile.F15ABCD)
            _importedF15KeyPath = null;
        else
            _importedF16KeyPath = null;
    }

    public string GetResolvedKeyPath(KeyProfile profile)
    {
        var install = _getSelectedInstall();
        if (install is null)
            return "";

        string? importedPath = GetImportedKeyPath(profile);
        if (!string.IsNullOrWhiteSpace(importedPath) && File.Exists(importedPath))
            return importedPath!;

        return GetDefaultKeyPath(install.BaseDir, profile);
    }

    public string GetActiveKeyPath(KeyProfile profile, bool requireExistingFile = true)
    {
        string path = GetResolvedKeyPath(profile);

        if (!requireExistingFile)
            return path;

        return File.Exists(path) ? path : "";
    }

    public void RefreshFromDisk()
    {
        var totalSw = Stopwatch.StartNew();
        DebugDiagnosticsService.Info($"KEYMAP REFRESH BEGIN | Profile={SelectedProfile}");

        Rows.Clear();
        KeyRows.Clear();
        Categories.Clear();
        _sections.Clear();

        SelectedRow = null;
        AssignmentText = "KEYSEARCH MODE";

        var install = _getSelectedInstall();
        if (install is null)
        {
            StatusText = "No install selected.";
            DebugDiagnosticsService.Info($"KEYMAP REFRESH END | Profile={SelectedProfile} | Result=NoInstall | ElapsedMs={totalSw.ElapsedMilliseconds}");
            return;
        }

        string baseDir = install.BaseDir;
        string keyPath = GetResolvedKeyPath(SelectedProfile);

        if (string.IsNullOrEmpty(keyPath))
        {
            StatusText = SelectedProfile == KeyProfile.F15ABCD
                ? "Missing key file: BMS - Full-F15ABCD.key"
                : "Missing key file: BMS - Full.key";
            DebugDiagnosticsService.Info($"KEYMAP REFRESH END | Profile={SelectedProfile} | Result=MissingKeyPath | ElapsedMs={totalSw.ElapsedMilliseconds}");
            return;
        }

        var setupReadSw = Stopwatch.StartNew();
        var km = _setupKeymap.Read(baseDir);
        setupReadSw.Stop();
        DebugDiagnosticsService.Info(
            $"KEYMAP REFRESH PHASE | Phase=SetupXmlRead | Profile={SelectedProfile} | ElapsedMs={setupReadSw.ElapsedMilliseconds} | DeviceCount={km.Devices.Length}");

        string? profileTag = SelectedProfile == KeyProfile.F15ABCD
            ? JoyAssgnLite.F15ProfileTag
            : null;

        for (int i = 0; i < km.Devices.Length; i++)
            km.Devices[i].SelectAvionicsProfile(profileTag);

        KeymappingContext.JoyAssgns = km.Devices;
        KeymappingContext.RollJoyId = km.RollJoyId;
        KeymappingContext.ThrottleJoyId = km.ThrottleJoyId;

        var keyFileReadSw = Stopwatch.StartNew();
        var keyFile = new KeyFile(keyPath);
        keyFileReadSw.Stop();
        DebugDiagnosticsService.Info(
            $"KEYMAP REFRESH PHASE | Phase=KeyFileRead | Profile={SelectedProfile} | ElapsedMs={keyFileReadSw.ElapsedMilliseconds} | KeyPath={Path.GetFileName(keyPath)} | KeyAssignCount={keyFile.keyAssign.Count()}");

        var buildSw = Stopwatch.StartNew();
        var buildResult = _gridBuilder.Build(baseDir, SelectedProfile, keyFile);
        buildSw.Stop();

        int builtAxisRowCount = buildResult.Sections.Sum(x => x.AxisRows.Count);
        int builtKeyRowCount = buildResult.Sections.Sum(x => x.KeyRows.Count);

        DebugDiagnosticsService.Info(
            $"KEYMAP REFRESH PHASE | Phase=GridBuild | Profile={SelectedProfile} | ElapsedMs={buildSw.ElapsedMilliseconds} | SectionCount={buildResult.Sections.Count} | AxisRowCount={builtAxisRowCount} | KeyRowCount={builtKeyRowCount}");

        _sections.AddRange(buildResult.Sections);

        foreach (var keyRow in buildResult.KeyRows)
            KeyRows.Add(keyRow);

        var categorySw = Stopwatch.StartNew();
        RebuildCategories(keyFile);
        EnsureSelectedCategoryStillValid();
        categorySw.Stop();
        DebugDiagnosticsService.Info(
            $"KEYMAP REFRESH PHASE | Phase=CategoryBuild | Profile={SelectedProfile} | ElapsedMs={categorySw.ElapsedMilliseconds} | CategoryCount={Categories.Count}");

        ApplyRowFilter();

        int rowCount = _sections.Sum(x => x.AxisRows.Count + x.KeyRows.Count);
        bool isImported = IsImportedPathForProfile(SelectedProfile, keyPath);

        StatusText = isImported
            ? $"Loaded {rowCount:N0} mappings from imported file: {Path.GetFileName(keyPath)}"
            : $"Loaded {rowCount:N0} mappings from {Path.GetFileName(keyPath)}";

        totalSw.Stop();
        DebugDiagnosticsService.Info(
            $"KEYMAP REFRESH END | Profile={SelectedProfile} | ElapsedMs={totalSw.ElapsedMilliseconds} | SectionCount={_sections.Count} | KeyRows={KeyRows.Count} | VisibleRows={Rows.Count} | Source={Path.GetFileName(keyPath)}");
    }

    public void RefreshKeyRowsInMemory()
    {
        var totalSw = Stopwatch.StartNew();
        string? selectedCallback = SelectedRow?.KeyRow?.GetCallback();

        var rowsByCallback = KeyRows
            .GroupBy(x => x.GetCallback(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        int replacedCount = 0;

        foreach (var section in _sections)
        {
            for (int i = 0; i < section.KeyRows.Count; i++)
            {
                var existingRow = section.KeyRows[i];
                if (existingRow.KeyRow is null)
                    continue;

                string callback = existingRow.KeyRow.GetCallback();
                if (!rowsByCallback.TryGetValue(callback, out var sourceRow))
                    continue;

                string newSearchText = BuildKeyRowSearchText(sourceRow);

                bool needsReplace =
                    !ReferenceEquals(existingRow.KeyRow, sourceRow) ||
                    !string.Equals(existingRow.Key, sourceRow.Key, StringComparison.Ordinal) ||
                    !string.Equals(existingRow.SearchText, newSearchText, StringComparison.Ordinal);

                if (!needsReplace)
                    continue;

                section.KeyRows[i] = CreateKeyRowViewModel(
                    sectionId: section.SectionId,
                    categoryName: section.CategoryName,
                    row: sourceRow);

                replacedCount++;
            }
        }

        ApplyRowFilter();

        if (!string.IsNullOrWhiteSpace(selectedCallback) &&
            TryFindKeyRowByCallback(selectedCallback!, out var selectedRow) &&
            selectedRow is not null &&
            IsRowVisible(selectedRow))
        {
            SelectedRow = selectedRow;
        }

        totalSw.Stop();
        DebugDiagnosticsService.Info(
            $"KEYMAP IN-MEMORY REFRESH | Type=KeyRows | Profile={SelectedProfile} | ElapsedMs={totalSw.ElapsedMilliseconds} | ReplacedRows={replacedCount} | VisibleRows={Rows.Count}");
    }

    public void RefreshSpecificKeyRowsInMemory(KeyAssgn originalSelectedRow, KeyAssgn savedSelectedRow, KeyAssgn? clearedDuplicateRow)
    {
        var totalSw = Stopwatch.StartNew();
        KeymappingGridRowViewModel? selectedRowAfterRefresh = null;
        int replacedCount = 0;
        var visibleRowReplacements = new Dictionary<KeymappingGridRowViewModel, KeymappingGridRowViewModel>();

        foreach (var section in _sections)
        {
            for (int i = 0; i < section.KeyRows.Count; i++)
            {
                var existingRow = section.KeyRows[i];
                if (existingRow.KeyRow is null)
                    continue;

                KeyAssgn? replacementSource = null;

                if (ReferenceEquals(existingRow.KeyRow, originalSelectedRow))
                {
                    replacementSource = savedSelectedRow;
                }
                else if (clearedDuplicateRow is not null && ReferenceEquals(existingRow.KeyRow, clearedDuplicateRow))
                {
                    replacementSource = clearedDuplicateRow;
                }

                if (replacementSource is null)
                    continue;

                var refreshedRow = CreateKeyRowViewModel(
                    sectionId: section.SectionId,
                    categoryName: section.CategoryName,
                    row: replacementSource);

                section.KeyRows[i] = refreshedRow;
                visibleRowReplacements[existingRow] = refreshedRow;

                if (ReferenceEquals(replacementSource, savedSelectedRow))
                    selectedRowAfterRefresh = refreshedRow;

                replacedCount++;
            }
        }

        bool usedFullRefilter = !string.IsNullOrWhiteSpace(SearchText);
        if (usedFullRefilter)
            ApplyRowFilter();
        else
            ReplaceVisibleRowsInPlace(visibleRowReplacements);

        if (selectedRowAfterRefresh is not null && IsRowVisible(selectedRowAfterRefresh))
            SelectedRow = selectedRowAfterRefresh;

        totalSw.Stop();
        DebugDiagnosticsService.Info(
            $"KEYMAP IN-MEMORY REFRESH | Type=SpecificKeyRows | Profile={SelectedProfile} | ElapsedMs={totalSw.ElapsedMilliseconds} | ReplacedRows={replacedCount} | VisibleRows={Rows.Count} | UsedFullRefilter={usedFullRefilter}");
    }

    public void RefreshAxisRowInMemory(AxisFunction function)
    {
        var totalSw = Stopwatch.StartNew();

        var install = _getSelectedInstall();
        if (install is null)
        {
            DebugDiagnosticsService.Info(
                $"KEYMAP IN-MEMORY REFRESH | Type=AxisRow | Profile={SelectedProfile} | Result=NoInstall | Function={function} | ElapsedMs={totalSw.ElapsedMilliseconds}");
            return;
        }

        string baseDir = install.BaseDir;
        var snapshot = _axisSnapshot.Build(baseDir, new[] { function });

        int replacedCount = 0;
        var visibleRowReplacements = new Dictionary<KeymappingGridRowViewModel, KeymappingGridRowViewModel>();

        foreach (var section in _sections)
        {
            for (int i = 0; i < section.AxisRows.Count; i++)
            {
                var existingRow = section.AxisRows[i];
                if (existingRow.AxisRow is null || existingRow.AxisRow.Function != function)
                    continue;

                var refreshedRow = CreateAxisRowViewModel(
                    baseDir: baseDir,
                    sectionId: section.SectionId,
                    categoryName: section.CategoryName,
                    function: function,
                    snapshot: snapshot);

                section.AxisRows[i] = refreshedRow;
                visibleRowReplacements[existingRow] = refreshedRow;

                replacedCount++;
            }
        }

        bool usedFullRefilter = !string.IsNullOrWhiteSpace(SearchText);
        if (usedFullRefilter)
            ApplyRowFilter();
        else
            ReplaceVisibleRowsInPlace(visibleRowReplacements);

        if (TryFindAxisRowByFunction(function, out var selectedRow) &&
            selectedRow is not null &&
            IsRowVisible(selectedRow))
        {
            SelectedRow = selectedRow;
        }

        totalSw.Stop();
        DebugDiagnosticsService.Info(
            $"KEYMAP IN-MEMORY REFRESH | Type=AxisRow | Profile={SelectedProfile} | Function={function} | ElapsedMs={totalSw.ElapsedMilliseconds} | ReplacedRows={replacedCount} | VisibleRows={Rows.Count} | UsedFullRefilter={usedFullRefilter}");
    }

    public void ApplyRowFilter()
    {
        var sw = Stopwatch.StartNew();

        Rows.Clear();

        KeymappingCategoryOption selected = SelectedCategory
            ?? Categories.FirstOrDefault(x => x.Kind == CategoryFilterKind.All)
            ?? KeymappingCategoryOption.CreateAll();

        int matchedSectionCount = 0;
        int visibleAxisRowCount = 0;
        int visibleKeyRowCount = 0;

        foreach (var section in _sections)
        {
            bool axisOnly = selected.Kind == CategoryFilterKind.Axis;
            bool categoryMatches = selected.Kind switch
            {
                CategoryFilterKind.All => true,
                CategoryFilterKind.Axis => true,
                CategoryFilterKind.MajorCategory => string.Equals(
                    selected.MajorNumber,
                    GetMajorCategoryNumber(section.SectionId),
                    StringComparison.OrdinalIgnoreCase),
                CategoryFilterKind.Section => string.Equals(
                    selected.SectionId,
                    section.SectionId,
                    StringComparison.OrdinalIgnoreCase),
                _ => true
            };

            if (!categoryMatches)
                continue;

            var visibleAxisRows = section.AxisRows.Where(MatchesSearch).ToList();
            var visibleKeyRows = axisOnly
                ? new List<KeymappingGridRowViewModel>()
                : section.KeyRows.Where(MatchesSearch).ToList();

            if (visibleAxisRows.Count == 0 && visibleKeyRows.Count == 0)
                continue;

            matchedSectionCount++;
            visibleAxisRowCount += visibleAxisRows.Count;
            visibleKeyRowCount += visibleKeyRows.Count;

            Rows.Add(section.HeaderRow);

            foreach (var axisRow in visibleAxisRows)
                Rows.Add(axisRow);

            foreach (var keyRow in visibleKeyRows)
                Rows.Add(keyRow);
        }

        sw.Stop();
        DebugDiagnosticsService.Info(
            $"KEYMAP FILTER | Profile={SelectedProfile} | ElapsedMs={sw.ElapsedMilliseconds} | SelectedCategory={selected.DisplayText} | SearchLength={(SearchText?.Length ?? 0)} | MatchedSections={matchedSectionCount} | VisibleAxisRows={visibleAxisRowCount} | VisibleKeyRows={visibleKeyRowCount} | VisibleRows={Rows.Count}");
    }

    public bool IsRowVisible(KeymappingGridRowViewModel row)
    {
        return Rows.Contains(row);
    }

    public bool TryFindKeyRowByCallback(string callbackName, out KeymappingGridRowViewModel? row)
    {
        row = _sections
            .SelectMany(x => x.KeyRows)
            .FirstOrDefault(x =>
                x.KeyRow is not null &&
                string.Equals(x.KeyRow.GetCallback(), callbackName, StringComparison.OrdinalIgnoreCase));

        return row is not null;
    }

    public bool TryFindKeyRowByKeyboardAssignment(string assignmentText, out KeymappingGridRowViewModel? row)
    {
        row = _sections
            .SelectMany(x => x.KeyRows)
            .FirstOrDefault(x =>
                x.KeyRow is not null &&
                string.Equals(x.KeyRow.GetKeyAssignmentStatus(), assignmentText, StringComparison.OrdinalIgnoreCase));

        return row is not null;
    }

    public IReadOnlyList<KeymappingGridRowViewModel> GetAllAxisRows()
    {
        return _sections.SelectMany(x => x.AxisRows).ToArray();
    }

    public bool TryFindAxisRowByFunction(AxisFunction function, out KeymappingGridRowViewModel? row)
    {
        row = _sections
            .SelectMany(x => x.AxisRows)
            .FirstOrDefault(x =>
                x.AxisRow is not null &&
                x.AxisRow.Function == function);

        return row is not null;
    }

    public string? GetSelectedCategoryKey()
    {
        return SelectedCategory?.Key;
    }

    public bool ContainsCategoryKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        return Categories.Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    public void SelectCategoryByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            SelectAllCategory();
            return;
        }

        var match = Categories.FirstOrDefault(x =>
            string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

        SelectedCategory = match
            ?? Categories.FirstOrDefault(x => x.Kind == CategoryFilterKind.All)
            ?? KeymappingCategoryOption.CreateAll();
    }

    public void SelectAllCategory()
    {
        SelectCategoryByKey(KeymappingCategoryOption.AllKey);
    }

    private void ReplaceVisibleRowsInPlace(Dictionary<KeymappingGridRowViewModel, KeymappingGridRowViewModel> replacements)
    {
        if (replacements.Count == 0)
            return;

        for (int i = 0; i < Rows.Count; i++)
        {
            var existingRow = Rows[i];
            if (replacements.TryGetValue(existingRow, out var refreshedRow))
                Rows[i] = refreshedRow;
        }
    }

    private KeymappingGridRowViewModel CreateKeyRowViewModel(
    string sectionId,
    string categoryName,
    KeyAssgn row)
    {
        return new KeymappingKeyRowViewModel(
            sectionId: sectionId,
            categoryName: categoryName,
            mapping: row.Mapping,
            key: row.Key,
            visibility: row.Visibility,
            searchText: BuildKeyRowSearchText(row),
            keyRow: row);
    }

    private KeymappingGridRowViewModel CreateAxisRowViewModel(
        string baseDir,
        string sectionId,
        string categoryName,
        AxisFunction function,
        AxisBindingsSnapshotService.AxisBindingsSnapshot snapshot)
    {
        var def = AxisCatalog.Get(function);

        var axisVm = new AxisRowViewModel(
            def,
            canExecute: (AxisFunction _) => false,
            assign: (AxisFunction _) => { },
            clear: (AxisFunction _) => { });

        int? assignedSlot = null;

        if (snapshot.Bindings.TryGetValue(function, out var binding) && binding.IsMapped)
        {
            axisVm.BindingText = binding.BindingText;
            axisVm.SetLiveSource(new AxisRowViewModel.LiveAxisSource(
                binding.DeviceName ?? "",
                binding.ProductGuid,
                binding.PhysicalAxisIndex,
                binding.Invert,
                binding.Detents));

            var existingMap = _axisDat.ReadAxisMapping(baseDir, def.MappingIndex);
            if (existingMap is not null)
                assignedSlot = existingMap.Value.JoyNum - 2;
        }
        else
        {
            axisVm.BindingText = "Not set";
            axisVm.SetLiveSource(null);
        }

        string mappingText = $"{def.DisplayName} axis";

        return new KeymappingAxisRowViewModel(
            sectionId: sectionId,
            categoryName: categoryName,
            mapping: mappingText,
            key: "",
            visibility: "White",
            searchText: NormalizeSearchText($"{sectionId} {categoryName} {mappingText} {axisVm.BindingText}"),
            axisRow: axisVm,
            assignedDeviceSlot: assignedSlot);
    }

    private static string BuildKeyRowSearchText(KeyAssgn row)
    {
        return NormalizeSearchText(string.Join("\n", new[]
        {
            row.GetKeyDescription(),
            row.Mapping,
            row.Key,
            row.GetCallback(),
            row.Z_Joy_0,
            row.Z_Joy_1,
            row.Z_Joy_2,
            row.Z_Joy_3,
            row.Z_Joy_4,
            row.Z_Joy_5,
            row.Z_Joy_6,
            row.Z_Joy_7,
            row.Z_Joy_8,
            row.Z_Joy_9,
            row.Z_Joy_10,
            row.Z_Joy_11,
            row.Z_Joy_12,
            row.Z_Joy_13,
            row.Z_Joy_14,
            row.Z_Joy_15
        }));
    }

    private static string NormalizeSearchText(string value)
    {
        return value.Replace("\"", "").Trim();
    }

    private void RebuildCategories(KeyFile keyFile)
    {
        string? selectedKeyBeforeRefresh = SelectedCategory?.Key;

        Categories.Clear();

        foreach (var option in BuildCategoryOptions(keyFile))
            Categories.Add(option);

        if (!string.IsNullOrWhiteSpace(selectedKeyBeforeRefresh))
        {
            var selected = Categories.FirstOrDefault(x =>
                string.Equals(x.Key, selectedKeyBeforeRefresh, StringComparison.OrdinalIgnoreCase));

            if (selected is not null)
            {
                _selectedCategory = selected;
                OnPropertyChanged(nameof(SelectedCategory));
                return;
            }
        }

        _selectedCategory = Categories.FirstOrDefault(x => x.Kind == CategoryFilterKind.All)
            ?? KeymappingCategoryOption.CreateAll();

        OnPropertyChanged(nameof(SelectedCategory));
    }

    private void EnsureSelectedCategoryStillValid()
    {
        if (SelectedCategory is null)
        {
            SelectAllCategory();
            return;
        }

        if (!ContainsCategoryKey(SelectedCategory.Key))
        {
            SelectAllCategory();
        }
    }

    /// <summary>
    /// Build a merged dropdown list:
    /// ALL
    /// AXIS
    /// 1. UI & 3RD PARTY SOFTWARE
    ///   UI FUNCTIONS
    ///   3RD PARTY SOFTWARE
    /// 2. LEFT CONSOLE
    ///   TEST PANEL
    ///   FLT CONTROL PANEL
    /// etc.
    ///
    /// The major categories come from the key file SimDoNothing category rows.
    /// The child sections come from the parsed 1.01 / 2.03 section headers already
    /// present in the current grid builder output.
    /// </summary>
    private IReadOnlyList<KeymappingCategoryOption> BuildCategoryOptions(KeyFile keyFile)
    {
        var options = new List<KeymappingCategoryOption>
        {
            KeymappingCategoryOption.CreateAll(),
            KeymappingCategoryOption.CreateAxis()
        };

        var addedSectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parents = ParseMajorCategoryOptions(keyFile.categoryHeaderLabels);

        foreach (var parent in parents)
        {
            options.Add(parent);

            foreach (var section in _sections.Where(x =>
                         string.Equals(GetMajorCategoryNumber(x.SectionId), parent.MajorNumber, StringComparison.OrdinalIgnoreCase)))
            {
                options.Add(KeymappingCategoryOption.CreateSection(
                    majorNumber: parent.MajorNumber ?? "",
                    sectionId: section.SectionId,
                    sectionName: section.CategoryName));

                addedSectionIds.Add(section.SectionId);
            }
        }

        // Fallback for any unexpected section that exists without a matching major category row.
        // This keeps the dropdown resilient if BMS ever changes key-file formatting.
        foreach (var section in _sections.Where(x => !addedSectionIds.Contains(x.SectionId)))
        {
            string majorNumber = GetMajorCategoryNumber(section.SectionId) ?? "";
            string orphanDisplay = string.IsNullOrWhiteSpace(majorNumber)
                ? section.CategoryName
                : $"{majorNumber}. {section.CategoryName}";

            options.Add(KeymappingCategoryOption.CreateSection(
                majorNumber: majorNumber,
                sectionId: section.SectionId,
                sectionName: orphanDisplay));
        }

        return options;
    }

    private static IReadOnlyList<KeymappingCategoryOption> ParseMajorCategoryOptions(IEnumerable<string> rawCategoryHeaders)
    {
        var result = new List<KeymappingCategoryOption>();
        var seenMajorNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in rawCategoryHeaders)
        {
            string clean = raw.Trim().Trim('"');

            var match = Regex.Match(clean, @"^(?<major>\d+)\.\s+(?<label>.+)$");
            if (!match.Success)
                continue;

            string majorNumber = match.Groups["major"].Value.Trim();
            string label = match.Groups["label"].Value.Trim();

            if (!seenMajorNumbers.Add(majorNumber))
                continue;

            result.Add(KeymappingCategoryOption.CreateMajorCategory(majorNumber, label));
        }

        return result;
    }

    private static string? GetMajorCategoryNumber(string? sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return null;

        int dotIndex = sectionId!.IndexOf('.');
        if (dotIndex <= 0)
            return null;

        return sectionId[..dotIndex].Trim();
    }

    private bool MatchesSearch(KeymappingGridRowViewModel row)
    {
        string filter = SearchText.Trim();
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return row.SearchText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private string? GetImportedKeyPath(KeyProfile profile)
    {
        return profile == KeyProfile.F15ABCD ? _importedF15KeyPath : _importedF16KeyPath;
    }

    private bool IsImportedPathForProfile(KeyProfile profile, string keyPath)
    {
        string? importedPath = GetImportedKeyPath(profile);
        if (string.IsNullOrWhiteSpace(importedPath))
            return false;

        return string.Equals(
            Path.GetFullPath(importedPath),
            Path.GetFullPath(keyPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDefaultKeyPath(string baseDir, KeyProfile profile)
    {
        string configDir = Path.Combine(baseDir, "User", "Config");

        if (profile == KeyProfile.F15ABCD)
        {
            string auto = Path.Combine(configDir, "BMS - Auto-F15ABCD.key");
            if (File.Exists(auto)) return auto;

            string full = Path.Combine(configDir, "BMS - Full-F15ABCD.key");
            if (File.Exists(full)) return full;

            string cwdFull = "BMS - Full-F15ABCD.key";
            if (File.Exists(cwdFull)) return cwdFull;

            return "";
        }
        else
        {
            string auto = Path.Combine(configDir, "BMS - Auto.key");
            if (File.Exists(auto)) return auto;

            string full = Path.Combine(configDir, "BMS - Full.key");
            if (File.Exists(full)) return full;

            string cwdFull = "BMS - Full.key";
            if (File.Exists(cwdFull)) return cwdFull;

            return "";
        }
    }

    public enum CategoryFilterKind
    {
        All,
        Axis,
        MajorCategory,
        Section
    }

    public sealed class KeymappingCategoryOption
    {
        public const string AllKey = "ALL";
        public const string AxisKey = "AXIS";

        public CategoryFilterKind Kind { get; }
        public string Key { get; }
        public string DisplayText { get; }
        public string? MajorNumber { get; }
        public string? SectionId { get; }

        private KeymappingCategoryOption(
            CategoryFilterKind kind,
            string key,
            string displayText,
            string? majorNumber,
            string? sectionId)
        {
            Kind = kind;
            Key = key;
            DisplayText = displayText;
            MajorNumber = majorNumber;
            SectionId = sectionId;
        }

        public static KeymappingCategoryOption CreateAll()
        {
            return new KeymappingCategoryOption(
                kind: CategoryFilterKind.All,
                key: AllKey,
                displayText: "ALL KEYS & AXES",
                majorNumber: null,
                sectionId: null);
        }

        public static KeymappingCategoryOption CreateAxis()
        {
            return new KeymappingCategoryOption(
                kind: CategoryFilterKind.Axis,
                key: AxisKey,
                displayText: "ALL AXES",
                majorNumber: null,
                sectionId: null);
        }

        public static KeymappingCategoryOption CreateMajorCategory(string majorNumber, string label)
        {
            return new KeymappingCategoryOption(
                kind: CategoryFilterKind.MajorCategory,
                key: $"MAJOR|{majorNumber}",
                displayText: $"{majorNumber}. {label}",
                majorNumber: majorNumber,
                sectionId: null);
        }

        public static KeymappingCategoryOption CreateSection(string majorNumber, string sectionId, string sectionName)
        {
            return new KeymappingCategoryOption(
                kind: CategoryFilterKind.Section,
                key: $"SECTION|{sectionId}",
                displayText: $"  {sectionName}",
                majorNumber: majorNumber,
                sectionId: sectionId);
        }

        public override string ToString()
        {
            return DisplayText;
        }
    }
}