using FalconBMS.Launcher.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// Application-wide owner of persistent DirectInput listeners.
///
/// Capture windows receive temporary access to existing sessions.
/// Starting or stopping capture never reacquires hardware.
///
/// Device synchronization is performed outside the WPF dispatcher.
/// </summary>
public sealed class PersistentDirectInputManager : IDisposable
{
    private static readonly object InstanceSync = new();

    private static PersistentDirectInputManager? _current;

    private readonly object _sync = new();

    private readonly Dictionary<Guid, ListenerEntry<JoystickSession>>
        _joysticks = new();

    private readonly HashSet<Guid> _connectedJoysticks = new();

    private readonly IntPtr _windowHandle;

    private ListenerEntry<KeyboardSession>? _keyboard;

    private bool _disposed;

    public static PersistentDirectInputManager Current
    {
        get
        {
            lock (InstanceSync)
            {
                return _current ??
                    throw new InvalidOperationException(
                        "Persistent DirectInput has not been initialized.");
            }
        }
    }

    /// <summary>
    /// Owns one hardware session and its underlying DirectInput object.
    /// The reference count represents active capture-window leases.
    /// </summary>
    private sealed class ListenerEntry<T> : IDisposable
        where T : IDisposable
    {
        public DirectInputManager Manager { get; }

        public T Session { get; }

        public int ReferenceCount { get; set; }

        public volatile bool Faulted;

        public ListenerEntry(
            DirectInputManager manager,
            T session)
        {
            Manager = manager;
            Session = session;
        }

        public void Dispose()
        {
            try
            {
                Session.Dispose();
            }
            finally
            {
                Manager.Dispose();
            }
        }
    }

    /// <summary>
    /// Temporary access to a persistent hardware session.
    ///
    /// Disposing this object releases the subscriber's reference,
    /// not the underlying hardware session.
    /// </summary>
    public sealed class ListenerLease<T> : IDisposable
        where T : IDisposable
    {
        private Action? _release;

        public T Session { get; }

        internal ListenerLease(
            T session,
            Action release)
        {
            Session = session;
            _release = release;
        }

        public void Dispose()
        {
            Interlocked.Exchange(
                ref _release,
                null)?.Invoke();
        }
    }

    private PersistentDirectInputManager(
        IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    /// <summary>
    /// Initializes the single application-wide owner.
    /// Requires the main window's stable native handle.
    /// </summary>
    public static void Initialize(
        IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException(
                "A valid main window handle is required.",
                nameof(windowHandle));
        }

        PersistentDirectInputManager manager;

        lock (InstanceSync)
        {
            if (_current is not null)
                return;

            manager =
                new PersistentDirectInputManager(
                    windowHandle);

            _current = manager;
        }

        DebugDiagnosticsService.Info(
            "Persistent DirectInput manager initialized.");

        // The keyboard begins tracking input before the first
        // capture window is opened.
        try
        {
            lock (manager._sync)
            {
                manager.OpenKeyboard();
            }
        }
        catch (Exception ex)
        {
            // Joystick capture must remain available even when
            // keyboard initialization fails.
            DebugDiagnosticsService.Exception(
                ex,
                "Persistent keyboard initialization failed.");
        }
    }

    private ListenerEntry<KeyboardSession> OpenKeyboard()
    {
        if (_keyboard is not null)
        {
            if (!_keyboard.Faulted)
                return _keyboard;

            if (_keyboard.ReferenceCount != 0)
            {
                throw new InvalidOperationException(
                    "The keyboard listener has faulted while " +
                    "capture subscriptions are still active.");
            }

            DisposeEntry(
                _keyboard,
                "keyboard");

            _keyboard = null;
        }

        var manager = new DirectInputManager();

        KeyboardSession session;

        try
        {
            session =
                manager.OpenKeyboard(
                    _windowHandle);
        }
        catch
        {
            manager.Dispose();
            throw;
        }

        var entry =
            new ListenerEntry<KeyboardSession>(
                manager,
                session);

        session.Faulted += ex =>
        {
            entry.Faulted = true;

            DebugDiagnosticsService.Exception(
                ex,
                "Persistent keyboard listener faulted.");
        };

        session.BufferCapacityReached += count =>
        {
            DebugDiagnosticsService.Warn(
                "Keyboard DirectInput buffer reached capacity. " +
                $"Events={count}");
        };

        try
        {
            session.Start();
        }
        catch
        {
            entry.Dispose();
            throw;
        }

        _keyboard = entry;

        DebugDiagnosticsService.Info(
            "Persistent keyboard listener opened.");

        return entry;
    }

    private ListenerEntry<JoystickSession> OpenJoystick(
        Guid instanceGuid)
    {
        if (_joysticks.TryGetValue(
                instanceGuid,
                out ListenerEntry<JoystickSession>? existing))
        {
            if (!existing.Faulted)
                return existing;

            if (existing.ReferenceCount != 0)
            {
                throw new InvalidOperationException(
                    "The joystick listener has faulted while " +
                    "capture subscriptions are still active.");
            }

            _joysticks.Remove(instanceGuid);

            DisposeEntry(
                existing,
                $"joystick {instanceGuid}");
        }

        var manager = new DirectInputManager();

        JoystickSession session;

        try
        {
            session =
                manager.OpenJoystick(
                    instanceGuid,
                    _windowHandle);
        }
        catch
        {
            manager.Dispose();
            throw;
        }

        var entry =
            new ListenerEntry<JoystickSession>(
                manager,
                session);

        session.Faulted += ex =>
        {
            entry.Faulted = true;

            DebugDiagnosticsService.Exception(
                ex,
                "Persistent joystick listener faulted. " +
                $"InstanceGuid={instanceGuid}");
        };

        session.BufferCapacityReached += count =>
        {
            DebugDiagnosticsService.Warn(
                "Joystick DirectInput buffer reached capacity. " +
                $"InstanceGuid={instanceGuid} Events={count}");
        };

        try
        {
            session.Start();
        }
        catch
        {
            entry.Dispose();
            throw;
        }

        _joysticks.Add(
            instanceGuid,
            entry);

        DebugDiagnosticsService.Info(
            "Persistent joystick listener opened. " +
            $"InstanceGuid={instanceGuid}");

        return entry;
    }

    public ListenerLease<KeyboardSession> AcquireKeyboard()
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            ListenerEntry<KeyboardSession> entry =
                OpenKeyboard();

            entry.ReferenceCount++;

            return new ListenerLease<KeyboardSession>(
                entry.Session,
                () => ReleaseKeyboard(entry));
        }
    }

    public ListenerLease<JoystickSession> AcquireJoystick(
        Guid instanceGuid)
    {
        if (instanceGuid == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid joystick instance GUID is required.",
                nameof(instanceGuid));
        }

        lock (_sync)
        {
            ThrowIfDisposed();

            ListenerEntry<JoystickSession> entry =
                OpenJoystick(instanceGuid);

            entry.ReferenceCount++;

            return new ListenerLease<JoystickSession>(
                entry.Session,
                () => ReleaseJoystick(
                    instanceGuid,
                    entry));
        }
    }

    private void ReleaseKeyboard(
        ListenerEntry<KeyboardSession> entry)
    {
        lock (_sync)
        {
            if (_disposed ||
                !ReferenceEquals(_keyboard, entry))
            {
                return;
            }

            if (entry.ReferenceCount > 0)
                entry.ReferenceCount--;

            if (entry.ReferenceCount == 0 &&
                entry.Faulted)
            {
                _keyboard = null;

                DisposeEntry(
                    entry,
                    "keyboard");
            }
        }
    }

    private void ReleaseJoystick(
        Guid instanceGuid,
        ListenerEntry<JoystickSession> entry)
    {
        lock (_sync)
        {
            if (_disposed ||
                !_joysticks.TryGetValue(
                    instanceGuid,
                    out ListenerEntry<JoystickSession>? current) ||
                !ReferenceEquals(current, entry))
            {
                return;
            }

            if (entry.ReferenceCount > 0)
                entry.ReferenceCount--;

            // A disconnected listener is retired immediately after
            // its last capture window releases it.
            if (entry.ReferenceCount == 0 &&
                (entry.Faulted ||
                 !_connectedJoysticks.Contains(instanceGuid)))
            {
                _joysticks.Remove(instanceGuid);

                DisposeEntry(
                    entry,
                    $"joystick {instanceGuid}");
            }
        }
    }

    /// <summary>
    /// Reconciles persistent listeners with the connected devices.
    ///
    /// Unchanged listeners retain their current input states.
    /// A disconnected listener with an active lease is retained
    /// until that lease is released.
    /// </summary>
    public void Synchronize(
        IEnumerable<Guid> connectedInstanceGuids)
    {
        if (connectedInstanceGuids is null)
        {
            throw new ArgumentNullException(
                nameof(connectedInstanceGuids));
        }

        Guid[] connected =
            connectedInstanceGuids
                .Where(guid => guid != Guid.Empty)
                .Distinct()
                .ToArray();

        lock (_sync)
        {
            if (_disposed)
                return;

            _connectedJoysticks.Clear();

            foreach (Guid guid in connected)
                _connectedJoysticks.Add(guid);

            foreach (Guid guid in _joysticks.Keys.ToArray())
            {
                if (_connectedJoysticks.Contains(guid))
                    continue;

                ListenerEntry<JoystickSession> entry =
                    _joysticks[guid];

                if (entry.ReferenceCount != 0)
                {
                    DebugDiagnosticsService.Warn(
                        "Disconnected joystick retained until " +
                        "active capture subscriptions are released. " +
                        $"InstanceGuid={guid}");

                    continue;
                }

                _joysticks.Remove(guid);

                DisposeEntry(
                    entry,
                    $"joystick {guid}");

                DebugDiagnosticsService.Info(
                    "Persistent joystick listener removed. " +
                    $"InstanceGuid={guid}");
            }

            foreach (Guid guid in connected)
            {
                try
                {
                    OpenJoystick(guid);
                }
                catch (Exception ex)
                {
                    // One unavailable device must not prevent
                    // other connected devices from initializing.
                    DebugDiagnosticsService.Exception(
                        ex,
                        "Persistent joystick initialization failed. " +
                        $"InstanceGuid={guid}");
                }
            }
        }
    }

    private static void DisposeEntry<T>(
        ListenerEntry<T> entry,
        string description)
        where T : IDisposable
    {
        try
        {
            entry.Dispose();
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "Persistent listener disposal failed. " +
                $"Device={description}");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(PersistentDirectInputManager));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_keyboard is not null)
            {
                DisposeEntry(
                    _keyboard,
                    "keyboard");

                _keyboard = null;
            }

            foreach (var pair in _joysticks)
            {
                DisposeEntry(
                    pair.Value,
                    $"joystick {pair.Key}");
            }

            _joysticks.Clear();
            _connectedJoysticks.Clear();
        }

        DebugDiagnosticsService.Info(
            "Persistent DirectInput manager stopped.");
    }

    public static void Shutdown()
    {
        PersistentDirectInputManager? manager;

        lock (InstanceSync)
        {
            manager = _current;
            _current = null;
        }

        manager?.Dispose();
    }
}