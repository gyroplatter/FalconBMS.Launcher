using FalconBMS.Launcher.Services;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace FalconBMS.Launcher.Views;

public partial class CallsignWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private static readonly Regex AllowedCallsignRegex = new("[^A-Z|a-z|0-9|~|`|\\[|\\]|\\{|\\}|\\-|_|\\=|\\'|\\s]", RegexOptions.Compiled);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    private readonly string _installKeyName;
    private readonly string _baseDir;
    private readonly CallsignService _callsign = new();

    public CallsignWindow(string installKeyName, string baseDir)
    {
        _installKeyName = installKeyName;
        _baseDir = baseDir;

        InitializeComponent();

        ThemeService.EffectiveDarkThemeChanged += ThemeService_EffectiveDarkThemeChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // The native Windows title bar can only be themed after WPF creates the window handle.
        ApplyNativeTitleBarTheme(ThemeService.IsCurrentEffectiveThemeDark());
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.EffectiveDarkThemeChanged -= ThemeService_EffectiveDarkThemeChanged;

        base.OnClosed(e);
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
            DebugDiagnosticsService.Warn($"Unable to apply native title bar theme for CallsignWindow. DwmSetWindowAttribute result={result}");
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Label_Error_Callsign.Visibility = Visibility.Collapsed;
        Label_Error_PilotName.Visibility = Visibility.Collapsed;
    }

    private void Callsign_Changed(object sender, TextChangedEventArgs e)
    {
        if (TextBox_Callsign.Text != CallsignService.DefaultCallsign)
            Label_Error_Callsign.Visibility = Visibility.Collapsed;

        string filtered = AllowedCallsignRegex.Replace(TextBox_Callsign.Text, string.Empty);
        if (!string.Equals(filtered, TextBox_Callsign.Text, StringComparison.Ordinal))
        {
            int caret = Math.Min(TextBox_Callsign.SelectionStart, filtered.Length);
            TextBox_Callsign.Text = filtered;
            TextBox_Callsign.SelectionStart = caret;
        }

        if (TextBox_Callsign.Text.Length > 12)
        {
            TextBox_Callsign.Text = TextBox_Callsign.Text.Remove(12);
            TextBox_Callsign.SelectionStart = TextBox_Callsign.Text.Length;
        }
    }

    private void PilotName_Changed(object sender, TextChangedEventArgs e)
    {
        if (TextBox_PilotName.Text != CallsignService.DefaultPilotName)
            Label_Error_PilotName.Visibility = Visibility.Collapsed;

        if (TextBox_PilotName.Text.Length > 20)
        {
            TextBox_PilotName.Text = TextBox_PilotName.Text.Remove(20);
            TextBox_PilotName.SelectionStart = TextBox_PilotName.Text.Length;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Button_Register_Click(object sender, RoutedEventArgs e)
    {
        string pilotName = TextBox_PilotName.Text.Trim();
        string pilotCallsign = TextBox_Callsign.Text.Trim();

        bool ok = true;

        if (string.Equals(pilotCallsign, CallsignService.DefaultCallsign, StringComparison.OrdinalIgnoreCase))
        {
            Label_Error_Callsign.Visibility = Visibility.Visible;
            ok = false;
        }

        if (string.Equals(pilotName, CallsignService.DefaultPilotName, StringComparison.OrdinalIgnoreCase))
        {
            Label_Error_PilotName.Visibility = Visibility.Visible;
            ok = false;
        }

        if (!ok)
            return;

        _callsign.ChangeName(_installKeyName, pilotCallsign, pilotName);
        _callsign.CreateLogbookIfMissing(_baseDir, pilotCallsign, pilotName);

        DialogResult = true;
        Close();
    }
}