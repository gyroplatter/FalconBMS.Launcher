using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FalconBMS.Launcher.Services;

public static class DebugDiagnosticsService
{
    private static readonly object _sync = new();
    private static readonly List<string> _buffer = new();

    private static string? _currentLogPath;
    private static StreamWriter? _writer;
    private static int _actionSeed;

    public static string? CurrentLogPath
    {
        get
        {
            lock (_sync)
                return _currentLogPath;
        }
    }

    public static void InitializeForInstall(string baseDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            return;

        string configDir = Path.Combine(baseDir, "User", "Config");
        Directory.CreateDirectory(configDir);

        string logPath = Path.Combine(configDir, "Launcher_Log.txt");

        lock (_sync)
        {
            if (string.Equals(_currentLogPath, logPath, StringComparison.OrdinalIgnoreCase) && _writer is not null)
                return;

            _writer?.Dispose();
            _writer = null;

            _currentLogPath = logPath;
            _writer = new StreamWriter(logPath, append: false, Encoding.UTF8)
            {
                AutoFlush = true
            };

            WriteLineLocked("INFO", $"Launcher log initialized: {logPath}");

            if (_buffer.Count > 0)
            {
                foreach (string line in _buffer)
                    _writer.WriteLine(line);

                _buffer.Clear();
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Exception(Exception ex, string context)
    {
        if (ex is null)
        {
            Write("ERROR", $"{context}: <null exception>");
            return;
        }

        Write("ERROR", $"{context}: {ex}");
    }

    public static string CreateActionId(string prefix)
    {
        string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "ACT" : prefix.Trim().ToUpperInvariant();
        int id = Interlocked.Increment(ref _actionSeed);
        return $"{safePrefix}-{id:D5}";
    }

    public static string GetFileSignature(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "path=<null>";

        if (!File.Exists(path))
            return "missing";

        try
        {
            var fi = new FileInfo(path);
            using var stream = File.OpenRead(path);
            using var sha1 = SHA1.Create();
            byte[] hash = sha1.ComputeHash(stream);
            string hashText = Convert.ToHexString(hash);
            return $"exists len={fi.Length} sha1={hashText}";
        }
        catch (Exception ex)
        {
            return $"error={ex.GetType().Name}";
        }
    }

    public static void LogFileWriteResult(
        string fileLabel,
        string path,
        string beforeSignature,
        string caller,
        string reason,
        string? actionId = null)
    {
        string afterSignature = GetFileSignature(path);
        bool changed = !string.Equals(beforeSignature, afterSignature, StringComparison.Ordinal);

        Info(
            $"FILE WRITE | ActionId={actionId ?? "-"} | File={fileLabel} | Caller={caller} | Reason={reason} | Changed={changed} | Before={beforeSignature} | After={afterSignature} | Path={path}");
    }

    public static void Close()
    {
        lock (_sync)
        {
            if (_writer is not null)
            {
                WriteLineLocked("INFO", "Process exiting - closing logfile.");
                _writer.Dispose();
                _writer = null;
            }
        }
    }

    private static void Write(string level, string message)
    {
        lock (_sync)
        {
            WriteLineLocked(level, message);
        }
    }

    private static void WriteLineLocked(string level, string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

        Debug.WriteLine(line);

        if (_writer is not null)
        {
            _writer.WriteLine(line);
            return;
        }

        _buffer.Add(line);
    }
}
