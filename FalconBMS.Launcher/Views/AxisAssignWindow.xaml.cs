using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.ViewModels;
using System;
using System.Windows;
using System.Windows.Interop;

namespace FalconBMS.Launcher.Views;

public partial class AxisAssignWindow : Window
{
    public AxisAssignWindow(AxisFunction function) : this(function, null) { }

    public AxisAssignWindow(AxisFunction function, AxisExistingBinding? existing)
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            var vm = new AxisAssignViewModel(function, hwnd, existing);
            DataContext = vm;

            Loaded += (_, _) => vm.StartDetect(preserveExisting: existing is not null);

            Closed += (_, _) =>
            {
                try { vm.Dispose(); } catch { }
            };
        };
    }

    public AxisSelectionResult? Result { get; private set; }
    public bool WasCleared { get; private set; }
    public DetentPosition? Detents { get; private set; }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var vm = (AxisAssignViewModel)DataContext;

        WasCleared = vm.ClearRequested;
        Result = vm.BuildResult();

        // Only meaningful for throttle axes.
        Detents = vm.GetDetents();

        // Allow OK when cleared, even though Result is null
        DialogResult = WasCleared || Result is not null;
        Close();
    }
}