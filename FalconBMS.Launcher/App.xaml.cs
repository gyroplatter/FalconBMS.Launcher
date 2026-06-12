using FalconBMS.Launcher.Services;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FalconBMS.Launcher
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private static EventWaitHandle? _showWindowEvent;
        private static RegisteredWaitHandle? _showWindowRegistration;

        private const string MutexName = "FalconBMS_AlternativeLauncher_SingleInstance";
        private const string ShowWindowEventName = "FalconBMS_AlternativeLauncher_ShowWindow";

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            DebugDiagnosticsService.Info("Application Initialization starting.");

            DispatcherUnhandledException += App_DispatcherUnhandledException;

            bool createdNew;

            _mutex = new Mutex(true, MutexName, out createdNew);
            _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);

            if (!createdNew)
            {
                DebugDiagnosticsService.Info("Second launcher instance detected - requesting existing window foreground.");
                _showWindowEvent.Set();
                Shutdown();
                return;
            }

            _showWindowRegistration = ThreadPool.RegisterWaitForSingleObject(
                _showWindowEvent,
                OnShowWindowRequested,
                null,
                Timeout.Infinite,
                false);

            // Apply the saved launcher theme before the main window is created.
            ThemeService.ApplySavedThemeOnStartup();

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _showWindowRegistration?.Unregister(null);
                _showWindowRegistration = null;

                _showWindowEvent?.Dispose();
                _showWindowEvent = null;

                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
                _mutex = null;
            }
            finally
            {
                DebugDiagnosticsService.Close();
                base.OnExit(e);
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            DebugDiagnosticsService.Exception(e.Exception, "Unhandled dispatcher exception");
        }

        private void OnShowWindowRequested(object? state, bool timedOut)
        {
            DebugDiagnosticsService.Info("Single-instance foreground request received.");
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(BringMainWindowToFront));
        }

        private void BringMainWindowToFront()
        {
            if (MainWindow == null)
            {
                DebugDiagnosticsService.Warn(
                    "BringMainWindowToFront requested, but MainWindow is null.");

                return;
            }

            if (!MainWindow.IsVisible)
            {
                DebugDiagnosticsService.Warn(
                    "BringMainWindowToFront requested, but MainWindow is not visible yet.");

                return;
            }

            if (MainWindow.WindowState == WindowState.Minimized)
            {
                MainWindow.WindowState =
                    WindowState.Normal;
            }

            MainWindow.Activate();

            var helper =
                new System.Windows.Interop.WindowInteropHelper(
                    MainWindow);

            if (helper.Handle != IntPtr.Zero)
            {
                ShowWindow(
                    helper.Handle,
                    SW_RESTORE);

                SetForegroundWindow(
                    helper.Handle);
            }

            MainWindow.Topmost =
                true;

            MainWindow.Topmost =
                false;

            MainWindow.Focus();

            DebugDiagnosticsService.Info(
                "Main window brought to foreground.");
        }
    }
}