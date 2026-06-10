using FalconBMS.Launcher.Models;
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

        if (!importResult.HasSkippedItems)
            return;

        SkippedControlsPanel.Visibility =
            Visibility.Visible;

        SkippedControlsTextBox.Text =
            BuildSkippedControlsText(
                importResult.SkippedItems);
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
        DialogResult = true;
    }
}