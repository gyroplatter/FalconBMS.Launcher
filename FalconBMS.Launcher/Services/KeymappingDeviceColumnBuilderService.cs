using FalconBMS.Launcher.Controls;
using FalconBMS.Launcher.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Builds the dynamic device columns for the Keymapping grid.
/// This preserves the existing column generation behavior while removing
/// UI construction logic from KeymappingView.xaml.cs.
/// </summary>
public sealed class KeymappingDeviceColumnBuilderService
{
    public DataGridTemplateColumn CreateDeviceColumn(int slotIndex, string header)
    {
        string textProperty = $"DeviceCells[{slotIndex}].Text";

        var column = new DataGridTemplateColumn
        {
            Header = header,
            MinWidth = 140,
            Width = new DataGridLength(160),
            CellTemplate = BuildDeviceCellTemplate(slotIndex),
            IsReadOnly = true,
            SortMemberPath = textProperty
        };

        return column;
    }

    private static DataTemplate BuildDeviceCellTemplate(int slotIndex)
    {
        string cellPath = $"DeviceCells[{slotIndex}]";
        string textPath = $"{cellPath}.Text";
        string axisPath = $"{cellPath}.AxisRow";

        var gridFactory = new FrameworkElementFactory(typeof(Grid));

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        textFactory.SetBinding(TextBlock.TextProperty, new Binding(textPath));

        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        textStyle.Triggers.Add(new DataTrigger
        {
            Binding = new Binding(axisPath),
            Value = null,
            Setters =
            {
                new Setter(UIElement.VisibilityProperty, Visibility.Visible)
            }
        });
        textFactory.SetValue(TextBlock.StyleProperty, textStyle);

        var axisFactory = new FrameworkElementFactory(typeof(AxisBar));
        axisFactory.SetValue(AxisBar.ShowLabelsProperty, false);
        axisFactory.SetValue(AxisBar.BarWidthProperty, 120.0);
        axisFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        axisFactory.SetBinding(AxisBar.ValueProperty, new Binding($"{axisPath}.AxisBarValue"));
        axisFactory.SetBinding(AxisBar.IsActiveProperty, new Binding($"{axisPath}.AxisBarEnabled"));
        axisFactory.SetBinding(AxisBar.FillBrushProperty, new Binding($"{axisPath}.AxisBarFillBrush"));
        axisFactory.SetBinding(AxisBar.OverlayTextProperty, new Binding($"{axisPath}.AxisBarOverlayText"));
        axisFactory.SetBinding(AxisBar.OverlayVisibilityProperty, new Binding($"{axisPath}.AxisBarOverlayVisibility"));
        axisFactory.SetBinding(AxisBar.ShowDetentMarkersProperty, new Binding($"{axisPath}.ShowDetentMarkers"));
        axisFactory.SetBinding(AxisBar.IdleDetentFractionProperty, new Binding($"{axisPath}.IdleDetentFraction"));
        axisFactory.SetBinding(AxisBar.AbDetentFractionProperty, new Binding($"{axisPath}.AbDetentFraction"));

        var axisStyle = new Style(typeof(AxisBar));
        axisStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        axisStyle.Triggers.Add(new DataTrigger
        {
            Binding = new Binding(axisPath),
            Value = null,
            Setters =
            {
                new Setter(UIElement.VisibilityProperty, Visibility.Collapsed)
            }
        });
        axisFactory.SetValue(FrameworkElement.StyleProperty, axisStyle);

        gridFactory.AppendChild(textFactory);
        gridFactory.AppendChild(axisFactory);

        return new DataTemplate
        {
            VisualTree = gridFactory
        };
    }
}