using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FalconBMS.Launcher.Services;

public static class DebugDiagnosticsService
{
    private const int MaxLogSessions = 5; //Maximum number of launch sessions to keep in the log
    private const string SessionStartMessage = "===== Launcher session start =====";

    private static readonly object _sync = new();
    private static readonly List<string> _buffer = new();

    // Tracks which BMS version-specific logs have already been initialized during
    // this Launcher process. If the user switches BMS versions and later switches
    // back, we resume that same session instead of creating another one.
    private static readonly HashSet<string> _initializedLogPaths =
        new(StringComparer.OrdinalIgnoreCase);

    // The diagnostics service is first touched at Launcher startup, so this
    // gives each Launcher process a consistent session-start timestamp
    private static readonly DateTime _sessionStartedAt = DateTime.Now;

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
            if (string.Equals(
                    _currentLogPath,
                    logPath,
                    StringComparison.OrdinalIgnoreCase) &&
                _writer is not null)
            {
                return;
            }

            _writer?.Dispose();
            _writer = null;

            _currentLogPath = logPath;

            bool firstUseOfLogThisProcess =
                _initializedLogPaths.Add(logPath);

            if (firstUseOfLogThisProcess)
            {
                // Leave room for the new session so the finished log contains
                // no more than MaxLogSessions sessions.
                RetainMostRecentSessions(
                    logPath,
                    MaxLogSessions - 1);
            }

            bool logAlreadyHasContent =
                File.Exists(logPath) &&
                new FileInfo(logPath).Length > 0;

            _writer = new StreamWriter(
                logPath,
                append: true,
                Encoding.UTF8)
            {
                AutoFlush = true
            };

            string launcherVersion = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
                ?? Assembly.GetExecutingAssembly()
                    .GetName()
                    .Version?
                    .ToString()
                ?? "unknown";

            if (firstUseOfLogThisProcess)
            {
                if (logAlreadyHasContent)
                    _writer.WriteLine();

                WriteLineLocked(
                    _sessionStartedAt,
                    "INFO",
                    SessionStartMessage);

                WriteLineLocked(
                    _sessionStartedAt,
                    "INFO",
                    $"Falcon BMS Launcher {launcherVersion}");
            }

            // Early startup messages are buffered before the selected BMS
            // install is known. Write those before the log initialized line
            // so the timestamps remain in chronological order.
            if (_buffer.Count > 0)
            {
                foreach (string line in _buffer)
                    _writer.WriteLine(line);

                _buffer.Clear();
            }

            if (firstUseOfLogThisProcess)
            {
                WriteLineLocked(
                    "INFO",
                    $"Launcher log initialized: {logPath}");
            }
            else
            {
                WriteLineLocked(
                    "INFO",
                    $"Launcher log resumed: {logPath}");
            }
        }
    }

    public static void Info(string message) =>
        Write("INFO", message);

    public static void Warn(string message) =>
        Write("WARN", message);

    public static void Error(string message) =>
        Write("ERROR", message);

    public static void Exception(
        Exception ex,
        string context)
    {
        if (ex is null)
        {
            Write(
                "ERROR",
                $"{context}: <null exception>");

            return;
        }

        Write(
            "ERROR",
            $"{context}: {ex}");
    }

    public static string CreateActionId(
        string prefix)
    {
        string safePrefix =
            string.IsNullOrWhiteSpace(prefix)
                ? "ACT"
                : prefix.Trim().ToUpperInvariant();

        int id =
            Interlocked.Increment(
                ref _actionSeed);

        return $"{safePrefix}-{id:D5}";
    }

    public static string GetFileSignature(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "path=<null>";

        if (!File.Exists(path))
            return "missing";

        try
        {
            var fi = new FileInfo(path);

            using var stream =
                File.OpenRead(path);

            using var sha1 =
                SHA1.Create();

            byte[] hash =
                sha1.ComputeHash(stream);

            // .NET Framework 4.8 does not have Convert.ToHexString().
            string hashText =
                BitConverter
                    .ToString(hash)
                    .Replace("-", string.Empty);

            return
                $"exists len={fi.Length} sha1={hashText}";
        }
        catch (Exception ex)
        {
            return
                $"error={ex.GetType().Name}";
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
        string afterSignature =
            GetFileSignature(path);

        bool changed =
            !string.Equals(
                beforeSignature,
                afterSignature,
                StringComparison.Ordinal);

        Info(
            $"FILE WRITE | ActionId={actionId ?? "-"} | File={fileLabel} | Caller={caller} | Reason={reason} | Changed={changed} | Before={beforeSignature} | After={afterSignature} | Path={path}");
    }

    public static void Close()
    {
        lock (_sync)
        {
            if (_writer is not null)
            {
                WriteLineLocked(
                    "INFO",
                    "Process exiting - closing logfile.");

                _writer.Dispose();
                _writer = null;
            }
        }
    }

    private static void Write(
        string level,
        string message)
    {
        lock (_sync)
        {
            WriteLineLocked(
                level,
                message);
        }
    }

    private static void WriteLineLocked(
        string level,
        string message)
    {
        WriteLineLocked(
            DateTime.Now,
            level,
            message);
    }

    private static void WriteLineLocked(
        DateTime timestamp,
        string level,
        string message)
    {
        string line =
            $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

        Debug.WriteLine(line);

        if (_writer is not null)
        {
            _writer.WriteLine(line);
            return;
        }

        _buffer.Add(line);
    }

    private static void RetainMostRecentSessions(
        string logPath,
        int sessionsToKeep)
    {
        if (sessionsToKeep < 1)
            return;

        if (!File.Exists(logPath))
            return;

        try
        {
            string[] lines =
                File.ReadAllLines(
                    logPath,
                    Encoding.UTF8);

            if (lines.Length == 0)
                return;

            var sessionStarts =
                new List<int>();

            int firstSessionMarker = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (!IsSessionStartLine(lines[i]))
                    continue;

                if (firstSessionMarker < 0)
                    firstSessionMarker = i;

                sessionStarts.Add(i);
            }

            // A log created by an older Launcher version has no explicit
            // session marker. Preserve everything before the first new marker
            // as one legacy session until it naturally ages out.
            if (firstSessionMarker < 0)
                return;

            bool hasLegacySession = false;

            for (int i = 0; i < firstSessionMarker; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                hasLegacySession = true;
                break;
            }

            if (hasLegacySession)
                sessionStarts.Insert(0, 0);

            if (sessionStarts.Count <= sessionsToKeep)
                return;

            int firstSessionToKeep =
                sessionStarts[
                    sessionStarts.Count -
                    sessionsToKeep];

            int keptLineCount =
                lines.Length -
                firstSessionToKeep;

            var keptLines =
                new string[keptLineCount];

            Array.Copy(
                lines,
                firstSessionToKeep,
                keptLines,
                0,
                keptLineCount);

            File.WriteAllLines(
                logPath,
                keptLines,
                Encoding.UTF8);
        }
        catch (Exception ex)
        {
            // Logging must never prevent the Launcher from starting
            Debug.WriteLine(
                $"Launcher log retention cleanup failed: {ex}");
        }
    }

    private static bool IsSessionStartLine(
        string line)
    {
        return line.IndexOf(
                   $"[INFO] {SessionStartMessage}",
                   StringComparison.Ordinal) >= 0;
    }
}