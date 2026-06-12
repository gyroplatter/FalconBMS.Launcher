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

        if (!importResult.Succeeded)
        {
            ConfigureFailureText(
                importResult);
        }

        BackupFolderRun.Text =
            BuildBackupFolderText(
                importResult.BackupDirectory);

        string importMessages =
            BuildImportMessagesText(
                importResult);

        if (string.IsNullOrWhiteSpace(
                importMessages))
        {
            ImportMessagesPanel.Visibility =
                Visibility.Collapsed;

            Height =
                300;

            MinHeight =
                300;
        }
        else
        {
            ImportMessagesTextBox.Text =
                importMessages;

            Height =
                440;

            MinHeight =
                440;
        }
    }

    private void ConfigureFailureText(
        LegacyImportExecutionResult importResult)
    {
        Title =
            "Import Failed";

        HeaderTextBlock.Text =
            "Import could not finish";

        IntroTextBlock.Text =
            "We found your old Launcher control setup, but the import could not be completed. " +
            "Please review the message below and restart the Launcher after fixing the issue.";

        BackupIntroRun.Text =
            "A backup was created before the import stopped here:";

        BackupTextBlock.Visibility =
            string.IsNullOrWhiteSpace(
                importResult.BackupDirectory)
                ? Visibility.Collapsed
                : Visibility.Visible;

        ImportMessagesHeaderTextBlock.Text =
            "Import Error";

        ContinueButton.Content =
            "Close";
    }

    private static string BuildBackupFolderText(
        string backupDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                backupDirectory))
        {
            return "User\\Config\\Launcher-Import-Backup";
        }

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

    private static string BuildImportMessagesText(
        LegacyImportExecutionResult importResult)
    {
        var builder =
            new StringBuilder();

        if (!importResult.Succeeded)
        {
            if (!string.IsNullOrWhiteSpace(
                    importResult.ErrorMessage))
            {
                builder.AppendLine(
                    importResult.ErrorMessage.Trim());
            }
            else
            {
                builder.AppendLine(
                    "No detailed error message was reported.");
            }

            return builder
                .ToString()
                .TrimEnd();
        }

        if (importResult.Warnings.Count == 0 &&
            !importResult.HasSkippedItems)
        {
            return "";
        }

        if (importResult.Warnings.Count > 0)
        {
            builder.AppendLine(
                "Warnings");

            foreach (string warning in importResult.Warnings)
            {
                builder.AppendLine(
                    $"- {warning}");
            }

            builder.AppendLine();
        }

        if (importResult.HasSkippedItems)
        {
            builder.AppendLine(
                "These controls were not imported correctly, please remap them manaully:");

            builder.AppendLine();

            builder.Append(
                BuildSkippedControlsText(
                    importResult.SkippedItems));
        }

        return builder
            .ToString()
            .TrimEnd();
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