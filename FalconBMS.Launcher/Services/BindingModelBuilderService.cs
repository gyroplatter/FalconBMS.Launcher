using FalconBMS.Launcher.Models;
using System.Collections.Generic;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Builds the editable BindingModel from read-only KeyCatalog data.
/// 
/// This is the bridge between:
/// - KeyCatalog: what exists in BMS
/// - BindingModel: what the launcher/user can edit in memory
/// 
/// This service does not read devices and does not write output files.
/// </summary>
public sealed class BindingModelBuilderService
{
    public BindingModel Build(IReadOnlyList<KeyCatalog> catalogs)
    {
        var model = new BindingModel();

        foreach (var catalog in catalogs)
        {
            var profile = new BindingAircraftProfile
            {
                AircraftProfile = catalog.AircraftProfile,
                SourceCatalogPath = catalog.SourcePath
            };

            foreach (var catalogRow in catalog.Rows)
                profile.Rows.Add(CreateBindingRow(catalogRow));

            model.AircraftProfiles.Add(profile);

            DebugDiagnosticsService.Info(
                $"Binding profile built | Aircraft={profile.AircraftProfile} | " +
                $"Rows={profile.TotalRows} | VisibleRows={profile.VisibleRows} | " +
                $"Callbacks={profile.CallbackRows} | Editable={profile.EditableRows} | " +
                $"Locked={profile.LockedRows} | Hidden={profile.HiddenRows} | " +
                $"Categories={profile.CategoryHeaders} | Sections={profile.SectionHeaders} | " +
                $"Remarks={profile.Remarks}");
        }

        DebugDiagnosticsService.Info(
            $"Binding model built | Profiles={model.ProfileCount} | " +
            $"Rows={model.TotalRows} | VisibleRows={model.VisibleRows} | " +
            $"Callbacks={model.CallbackRows} | Editable={model.EditableRows} | " +
            $"Locked={model.LockedRows} | Hidden={model.HiddenRows}");

        return model;
    }

    private static BindingRow CreateBindingRow(KeyCatalogRow catalogRow)
    {
        return new BindingRow
        {
            SourceLineNumber = catalogRow.LineNumber,
            SourceRawLine = catalogRow.RawLine,

            RowKind = MapRowKind(catalogRow.RowKind),

            CallbackName = catalogRow.CallbackName,
            SoundId = catalogRow.SoundId,
            Unused = catalogRow.Unused,

            KeyScancode = catalogRow.KeyScancode,
            KeyModifierFlags = catalogRow.KeyModifierFlags,
            ChordScancode = catalogRow.ChordScancode,
            ChordModifierFlags = catalogRow.ChordModifierFlags,

            Visibility = catalogRow.Visibility,
            Description = catalogRow.Description,

            CategoryName = catalogRow.CategoryName,
            SectionName = catalogRow.SectionName,

            IsModified = false
        };
    }

    private static BindingRowKind MapRowKind(KeyCatalogRowKind rowKind)
    {
        return rowKind switch
        {
            KeyCatalogRowKind.CategoryHeader => BindingRowKind.CategoryHeader,
            KeyCatalogRowKind.SectionHeader => BindingRowKind.SectionHeader,
            KeyCatalogRowKind.EditableCallback => BindingRowKind.EditableCallback,
            KeyCatalogRowKind.LockedCallback => BindingRowKind.LockedCallback,
            KeyCatalogRowKind.HiddenCallback => BindingRowKind.HiddenCallback,
            KeyCatalogRowKind.Remark => BindingRowKind.Remark,
            _ => BindingRowKind.Other
        };
    }
}