using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FalconBMS.Launcher;

public partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;

        ThemeService.EffectiveDarkThemeChanged += ThemeService_EffectiveDarkThemeChanged;

        DebugDiagnosticsService.Info("MainWindow constructed.");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // The native title bar can only be themed after WPF has created the window handle.
        ApplyNativeTitleBarTheme(ThemeService.IsCurrentEffectiveThemeDark());
    }

    private void ThemeService_EffectiveDarkThemeChanged(bool isDarkTheme)
    {
        ApplyNativeTitleBarTheme(isDarkTheme);
    }

    private void ApplyNativeTitleBarTheme(bool useDarkTitleBar)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var value = useDarkTitleBar ? 1 : 0;

        var result = DwmSetWindowAttribute(
            hwnd,
            DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref value,
            sizeof(int));

        if (result != 0)
        {
            DebugDiagnosticsService.Warn($"Unable to apply native title bar theme. DwmSetWindowAttribute result={result}");
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.SaveOutputsForClose();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        ThemeService.EffectiveDarkThemeChanged -= ThemeService_EffectiveDarkThemeChanged;
    }
}