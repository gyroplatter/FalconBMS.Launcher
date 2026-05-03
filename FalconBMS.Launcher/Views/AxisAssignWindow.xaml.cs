using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using System.Windows;

namespace FalconBMS.Launcher.Views;

public partial class AxisAssignWindow : Window
{
    private readonly AxisDefinitionService _axisDefinitionService = new();

    private static readonly AxCurve[] AxisCurveOptions =
    {
        AxCurve.None,
        AxCurve.Small,
        AxCurve.Medium,
        AxCurve.Large
    };

    public AxisAssignWindow()
    {
        InitializeComponent();

        DeadzoneComboBox.ItemsSource = AxisCurveOptions;
        SaturationComboBox.ItemsSource = AxisCurveOptions;
    }

    public AxisAssignWindow(ControlGridRowViewModel axisRow) : this()
    {
        DeviceAxisDefinition? definition = _axisDefinitionService.Find(axisRow.AxisLogicalAxisName);

        if (definition is null)
        {
            ApplyFallbackLayout(axisRow);
            return;
        }

        ApplyDefinition(definition);
    }

    private void ApplyDefinition(DeviceAxisDefinition definition)
    {
        Title = "Assign " + definition.DisplayName + " Axis";
        TitleTextBlock.Text = Title;

        LeftLabelTextBlock.Text = definition.LeftLabel;
        RightLabelTextBlock.Text = definition.RightLabel;

        DeadzonePanel.Visibility = definition.SupportsDeadzone
            ? Visibility.Visible
            : Visibility.Collapsed;

        SaturationPanel.Visibility = definition.SupportsSaturation
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (definition.LayoutKind == DeviceAxisAssignmentLayoutKind.Throttle)
        {
            SaturationPanel.Margin = new Thickness(0, 0, 0, 0);
        }
        else
        {
            SaturationPanel.Margin = definition.SupportsDeadzone
                ? new Thickness(138, 0, 0, 0)
                : new Thickness(138, 0, 0, 0);
        }

        InvertCheckBox.Visibility = definition.SupportsInvert
            ? Visibility.Visible
            : Visibility.Collapsed;

        ThrottleDetentPanel.Visibility =
            definition.LayoutKind == DeviceAxisAssignmentLayoutKind.Throttle
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (definition.LayoutKind == DeviceAxisAssignmentLayoutKind.Throttle)
        {
            ThrottleDetentPanel.Margin = new Thickness(0, 0, 0, 0);
        }

        ThrottleMarkerCanvas.Visibility =
            definition.LayoutKind == DeviceAxisAssignmentLayoutKind.Throttle
                ? Visibility.Visible
                : Visibility.Collapsed;

        StatusTextBlock.Text = "Awaiting inputs";
    }

    private void ApplyFallbackLayout(ControlGridRowViewModel axisRow)
    {
        Title = "Assign " + axisRow.Mapping + " Axis";
        TitleTextBlock.Text = Title;

        LeftLabelTextBlock.Text = "";
        RightLabelTextBlock.Text = "";

        DeadzonePanel.Visibility = Visibility.Collapsed;
        SaturationPanel.Visibility = Visibility.Collapsed;
        InvertCheckBox.Visibility = Visibility.Collapsed;
        ThrottleDetentPanel.Visibility = Visibility.Collapsed;
        ThrottleMarkerCanvas.Visibility = Visibility.Collapsed;

        StatusTextBlock.Text = "Logical Axis: " + axisRow.AxisLogicalAxisName;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}