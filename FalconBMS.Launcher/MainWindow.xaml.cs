using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace FalconBMS.Launcher;

public partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private int _modalOverlayDepth;

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

        #if DEBUG
                PreviewKeyDown += MainWindow_DebugPreviewKeyDown;
        #endif

        DebugDiagnosticsService.Info("MainWindow constructed.");
    }

    public static IDisposable BeginModalOverlay(Window? ownerWindow)
    {
        if (ownerWindow is MainWindow mainWindow)
        {
            mainWindow.ShowModalOverlay();
            return new ModalOverlayScope(mainWindow);
        }

        return EmptyDisposable.Instance;
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

    private void ShowModalOverlay()
    {
        _modalOverlayDepth++;

        ModalOverlay.Visibility =
            Visibility.Visible;
    }

    private void HideModalOverlay()
    {
        if (_modalOverlayDepth > 0)
            _modalOverlayDepth--;

        if (_modalOverlayDepth == 0)
        {
            ModalOverlay.Visibility =
                Visibility.Collapsed;
        }
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

#if DEBUG
    private void MainWindow_DebugPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // Debug shortcuts are intentionally active only while the Main tab is selected.
        if (viewModel.CurrentTab != Models.LauncherTab.Main)
            return;

        // A toggles directly between the effective Light and Dark themes.
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.None)
        {
            var newThemeMode = ThemeService.IsCurrentEffectiveThemeDark()
                ? Models.LauncherThemeModes.Light
                : Models.LauncherThemeModes.Dark;

            // Use the MainViewModel properties so its theme radio-button state stays synchronized.
            if (newThemeMode == Models.LauncherThemeModes.Light)
                viewModel.Main.LauncherThemeLight = true;
            else
                viewModel.Main.LauncherThemeDark = true;

            e.Handled = true;
            return;
        }

        // Ctrl+W closes the Launcher through the normal window Closing pipeline.
        if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            Close();
        }
    }
#endif

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.SaveOutputsForClose();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        ThemeService.EffectiveDarkThemeChanged -= ThemeService_EffectiveDarkThemeChanged;

    #if DEBUG
            PreviewKeyDown -= MainWindow_DebugPreviewKeyDown;
    #endif
    }

    private sealed class ModalOverlayScope : IDisposable
    {
        private MainWindow? _mainWindow;

        public ModalOverlayScope(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void Dispose()
        {
            if (_mainWindow is null)
                return;

            _mainWindow.HideModalOverlay();
            _mainWindow = null;
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        private EmptyDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}