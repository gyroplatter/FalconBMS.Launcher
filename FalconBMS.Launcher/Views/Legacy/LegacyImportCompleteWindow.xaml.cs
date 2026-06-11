using FalconBMS.Launcher.Models.Legacy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace FalconBMS.Launcher.Views;

public partial class LegacyImportCompleteWindow : Window
{
    public LegacyImportCompleteWindow(
        LegacyImportExecutionResult importResult)
    {
        InitializeComponent();

        BackupFolderRun.Text =
            BuildBackupFolderText(
                importResult.BackupDirectory);

        if (!importResult.HasSkippedItems)
            return;

        SkippedControlsPanel.Visibility =
            Visibility.Visible;

        SkippedControlsTextBox.Text =
            BuildSkippedControlsText(
                importResult.SkippedItems);
    }

    private static string BuildBackupFolderText(
        string backupDirectory)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
            return "User\\Config\\Launcher-Import-Backup";

        string normalizedPath =
            backupDirectory
                .Replace(
                    '/',
                    '\\');

        const string marker =
            "\\User\\Config\\";

        int markerIndex =
            normalizedPath.IndexOf(
                marker,
                StringComparison.OrdinalIgnoreCase);

        if (markerIndex >= 0)
        {
            return normalizedPath.Substring(
                markerIndex + 1);
        }

        return normalizedPath;
    }

    private static string BuildSkippedControlsText(
        IReadOnlyList<LegacyImportSkippedItem> skippedItems)
    {
        var builder =
            new StringBuilder();

        List<IGrouping<string, LegacyImportSkippedItem>> groups =
            skippedItems
                .GroupBy(
                    item => item.SourceName,
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(group =>
                    group.Key)
                .ToList();

        for (
            int groupIndex = 0;
            groupIndex < groups.Count;
            groupIndex++)
        {
            IGrouping<string, LegacyImportSkippedItem> group =
                groups[groupIndex];

            builder.AppendLine(
                group.Key);

            foreach (LegacyImportSkippedItem item in
                     group.OrderBy(item =>
                         item.ControlName))
            {
                builder.Append("  ");

                builder.AppendLine(
                    item.ControlName);
            }

            if (groupIndex < groups.Count - 1)
                builder.AppendLine();
        }

        return builder
            .ToString()
            .TrimEnd();
    }

    private void ContinueButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult =
            true;
    }
}