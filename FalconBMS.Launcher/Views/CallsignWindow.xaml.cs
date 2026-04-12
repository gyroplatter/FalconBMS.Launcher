using FalconBMS.Launcher.Services;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace FalconBMS.Launcher.Views;

public partial class CallsignWindow : Window
{
    private static readonly Regex AllowedCallsignRegex = new("[^A-Z|a-z|0-9|~|`|\\[|\\]|\\{|\\}|\\-|_|\\=|\\'|\\s]", RegexOptions.Compiled);

    private readonly string _installKeyName;
    private readonly string _baseDir;
    private readonly CallsignService _callsign = new();

    public CallsignWindow(string installKeyName, string baseDir)
    {
        _installKeyName = installKeyName;
        _baseDir = baseDir;

        InitializeComponent();
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