using FalconBMS.Launcher.ViewModels;
using System.Windows;

namespace FalconBMS.Launcher.Views;

public partial class AxisAssignWindow : Window
{
    public AxisAssignWindow()
    {
        InitializeComponent();
    }

    public AxisAssignWindow(ControlGridRowViewModel axisRow) : this()
    {
        Title = "Assign " + axisRow.Mapping + " Axis";
        TitleTextBlock.Text = Title;
        AxisNameTextBlock.Text = "Logical Axis: " + axisRow.AxisLogicalAxisName;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}