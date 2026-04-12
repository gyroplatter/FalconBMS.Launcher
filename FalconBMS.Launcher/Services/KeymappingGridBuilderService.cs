using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Models.Keymapping;
using FalconBMS.Launcher.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Builds the unified Keymapping grid model from a key file plus current axis bindings.
/// This preserves the current single-grid behavior while moving parsing and row creation
/// out of KeymappingViewModel.
/// </summary>
public sealed class KeymappingGridBuilderService
{
    private readonly AxisBindingsSnapshotService _axisSnapshot = new();
    private readonly AxisMappingDatService _axisDat = new();

    public BuildResult Build(string baseDir, KeyProfile profile, KeyFile keyFile)
    {
        var sections = new List<SectionGroup>();
        var keyRows = new List<KeyAssgn>();

        var placements = KeymappingAxisPlacementCatalog.GetPlacements(profile);
        var snapshot = _axisSnapshot.Build(baseDir, placements.Select(x => x.Function));
        SectionGroup? currentSection = null;

        foreach (var row in keyFile.keyAssign)
        {
            if (TryParseSectionHeader(row.GetKeyDescription(), out string sectionId, out string categoryName))
            {
                var headerRow = new KeymappingCategoryHeaderRowViewModel(
                    sectionId: sectionId,
                    categoryName: categoryName,
                    mapping: row.Mapping,
                    key: "",
                    visibility: row.Visibility,
                    searchText: NormalizeSearchText($"{sectionId} {categoryName}"),
                    keyRow: row);

                currentSection = new SectionGroup(sectionId, categoryName, headerRow);

                foreach (var placement in placements.Where(x => string.Equals(x.SectionId, sectionId, StringComparison.OrdinalIgnoreCase)))
                {
                    currentSection.AxisRows.Add(BuildAxisRow(baseDir, snapshot, placement, categoryName));
                }

                sections.Add(currentSection);
                continue;
            }

            if (currentSection is null)
                continue;

            var keyRow = new KeymappingKeyRowViewModel(
                sectionId: currentSection.SectionId,
                categoryName: currentSection.CategoryName,
                mapping: row.Mapping,
                key: row.Key,
                visibility: row.Visibility,
                searchText: BuildKeyRowSearchText(row),
                keyRow: row);

            currentSection.KeyRows.Add(keyRow);
            keyRows.Add(row);
        }

        return new BuildResult(sections, keyRows);
    }

    private KeymappingGridRowViewModel BuildAxisRow(
        string baseDir,
        AxisBindingsSnapshotService.AxisBindingsSnapshot snapshot,
        KeymappingAxisPlacementCatalog.AxisPlacement placement,
        string categoryName)
    {
        var def = AxisCatalog.Get(placement.Function);

        var axisVm = new AxisRowViewModel(
            def,
            canExecute: _ => false,
            assign: _ => { },
            clear: _ => { });

        int? assignedSlot = null;

        if (snapshot.Bindings.TryGetValue(placement.Function, out var binding) && binding.IsMapped)
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
            sectionId: placement.SectionId,
            categoryName: categoryName,
            mapping: mappingText,
            key: "",
            visibility: "White",
            searchText: NormalizeSearchText($"{placement.SectionId} {categoryName} {mappingText} {axisVm.BindingText}"),
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

    private static bool TryParseSectionHeader(string raw, out string sectionId, out string categoryName)
    {
        sectionId = "";
        categoryName = "";

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string text = raw.Trim().Trim('"');
        text = text.Replace("=", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();

        var match = Regex.Match(text, @"^(?<id>\d+\.\d+)\s+(?<label>.+)$");
        if (!match.Success)
            return false;

        sectionId = match.Groups["id"].Value.Trim();
        categoryName = match.Groups["label"].Value.Trim();
        return true;
    }

    public sealed class BuildResult
    {
        public IReadOnlyList<SectionGroup> Sections { get; }
        public IReadOnlyList<KeyAssgn> KeyRows { get; }

        public BuildResult(IReadOnlyList<SectionGroup> sections, IReadOnlyList<KeyAssgn> keyRows)
        {
            Sections = sections;
            KeyRows = keyRows;
        }
    }

    public sealed class SectionGroup
    {
        public string SectionId { get; }
        public string CategoryName { get; }
        public KeymappingGridRowViewModel HeaderRow { get; }
        public List<KeymappingGridRowViewModel> AxisRows { get; } = new();
        public List<KeymappingGridRowViewModel> KeyRows { get; } = new();

        public SectionGroup(string sectionId, string categoryName, KeymappingGridRowViewModel headerRow)
        {
            SectionId = sectionId;
            CategoryName = categoryName;
            HeaderRow = headerRow;
        }
    }
}