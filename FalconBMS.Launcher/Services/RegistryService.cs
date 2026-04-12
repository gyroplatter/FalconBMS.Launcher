using Microsoft.Win32;
using System.Text;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// For reading and writing FalconBMS-related Windows registry values.
/// </summary>
public sealed class RegistryService
{
    private const string BaseKeyPath = @"SOFTWARE\WOW6432Node\Benchmark Sims";

    private static string InstallKeyPath(string installKeyName) => $@"{BaseKeyPath}\{installKeyName}";

    public string? ReadString(string installKeyName, string valueName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(InstallKeyPath(installKeyName), writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void WriteString(string installKeyName, string valueName, string value)
    {
        using var key = Registry.LocalMachine.OpenSubKey(InstallKeyPath(installKeyName), writable: true);
        if (key is null)
            throw new InvalidOperationException($"Registry key not found: HKLM\\{InstallKeyPath(installKeyName)}");

        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public byte[]? ReadBinary(string installKeyName, string valueName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(InstallKeyPath(installKeyName), writable: false);
        return key?.GetValue(valueName) as byte[];
    }

    public void WriteBinary(string installKeyName, string valueName, byte[] value)
    {
        using var key = Registry.LocalMachine.OpenSubKey(InstallKeyPath(installKeyName), writable: true);
        if (key is null)
            throw new InvalidOperationException($"Registry key not found: HKLM\\{InstallKeyPath(installKeyName)}");

        key.SetValue(valueName, value, RegistryValueKind.Binary);
    }

    public string ReadZeroPaddedAsciiBinary(string installKeyName, string valueName, string fallback)
    {
        byte[]? bits = ReadBinary(installKeyName, valueName);
        if (bits is null || bits.Length == 0 || bits[0] == 0x00)
            return fallback;

        int n = Array.IndexOf(bits, (byte)0x00);
        if (n == -1)
            n = bits.Length;

        return Encoding.ASCII.GetString(bits, 0, n);
    }

    public void WriteZeroPaddedAsciiBinary(string installKeyName, string valueName, string value, int length)
    {
        byte[] buffer = new byte[length];
        byte[] ascii = Encoding.ASCII.GetBytes(value);

        int n = Math.Min(ascii.Length, buffer.Length);
        Array.Copy(ascii, buffer, n);

        WriteBinary(installKeyName, valueName, buffer);
    }
}