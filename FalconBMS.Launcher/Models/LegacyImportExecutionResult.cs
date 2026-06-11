using System.Collections.Generic;

namespace FalconBMS.Launcher.Models;

public sealed class LegacyImportExecutionResult
{
    public bool Succeeded { get; init; }

    public BindingModel? ImportedBindingModel { get; init; }

    public bool ExportRttTextures { get; init; }

    public int KeyboardAssignmentsImported { get; init; }

    public int DevicesImportedFromLegacyXml { get; init; }

    public int DevicesUsingStockFallback { get; init; }

    public int MissingCallbacksSkipped { get; init; }

    public string BackupDirectory { get; init; } = "";

    public int BackupFilesCopied { get; init; }

    public List<string> Warnings { get; } = new();

    public List<LegacyImportSkippedItem> SkippedItems { get; } = new();

    public bool HasSkippedItems =>
        SkippedItems.Count > 0;

    public string ErrorMessage { get; init; } = "";
}