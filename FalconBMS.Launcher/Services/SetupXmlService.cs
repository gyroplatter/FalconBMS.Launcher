using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// For reading, creating, copying, and updating FalconBMS Setup.v100.*.xml device config files.
/// </summary>
public sealed class SetupXmlService
{

    // ===== Snapshot + caching (performance) =====

    public sealed record AxisSnapshot(bool Invert, AxCurve Deadzone, AxCurve Saturation);

    public sealed record SetupXmlSnapshot(
        IReadOnlyDictionary<AxisFunction, AxisSnapshot> Axis,
        IReadOnlyDictionary<string, DetentPosition> DetentsBySafeDeviceName
    );

    private sealed record CacheEntry(DateTime MaxWriteUtc, SetupXmlSnapshot Snapshot);

    private static readonly object _snapLock = new();
    private static readonly Dictionary<string, CacheEntry> _snapCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a pre-parsed snapshot of all Setup.v100.*.xml files for an install.
    /// This is intended to avoid repeatedly loading/parsing XML during tab switches.
    /// </summary>
    public SetupXmlSnapshot ReadSnapshot(string baseDir)
    {
        var cfgDir = UserConfigDir(baseDir);
        if (!Directory.Exists(cfgDir))
        {
            return new SetupXmlSnapshot(
                Axis: new Dictionary<AxisFunction, AxisSnapshot>(),
                DetentsBySafeDeviceName: new Dictionary<string, DetentPosition>(StringComparer.OrdinalIgnoreCase)
            );
        }

        string cacheKey = baseDir;

        string[] files;
        try
        {
            files = Directory.GetFiles(cfgDir, "Setup.v100.*.xml", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            files = Array.Empty<string>();
        }

        DateTime maxWrite = DateTime.MinValue;
        foreach (var f in files)
        {
            try
            {
                var t = File.GetLastWriteTimeUtc(f);
                if (t > maxWrite) maxWrite = t;
            }
            catch { }
        }

        lock (_snapLock)
        {
            if (_snapCache.TryGetValue(cacheKey, out var hit) && hit.MaxWriteUtc == maxWrite)
                return hit.Snapshot;
        }

        // Reverse map: Setup AxisName -> AxisFunction
        var reverse = new Dictionary<string, AxisFunction>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in AxisCatalog.All)
        {
            var setupName = AxisFunctionToSetupAxisName(def.Function);
            if (!string.IsNullOrWhiteSpace(setupName))
                reverse[setupName!] = def.Function;
        }

        var axis = new Dictionary<AxisFunction, AxisSnapshot>();
        var detentsBySafeName = new Dictionary<string, DetentPosition>(StringComparer.OrdinalIgnoreCase);

        foreach (var xmlPath in files)
        {
            XDocument? doc = null;
            try { doc = XDocument.Load(xmlPath); } catch { }
            if (doc?.Root is null) continue;

            // Detents are per-device file. Use existing parsing logic.
            try
            {
                var safe = ExtractSafeDeviceNameFromSetupFilename(Path.GetFileName(xmlPath));
                if (!string.IsNullOrWhiteSpace(safe))
                {
                    var deviceName = safe; // safe already matches sanitized name

                    if (TryGetDetents(baseDir, deviceName!, out var dp))
                        detentsBySafeName[safe!] = dp;
                }
            }
            catch { }

            foreach (var ax in doc.Root.Descendants("AxAssgn"))
            {
                var axisName = (string?)ax.Element("AxisName");
                if (string.IsNullOrWhiteSpace(axisName)) continue;

                if (!reverse.TryGetValue(axisName!.Trim(), out var func))
                    continue;

                bool invert = false;
                AxCurve deadzone = AxCurve.None;
                AxCurve saturation = AxCurve.None;

                var invStr = (string?)ax.Element("Invert");
                if (!string.IsNullOrWhiteSpace(invStr))
                    bool.TryParse(invStr, out invert);

                var dzStr = (string?)ax.Element("Deadzone");
                if (!string.IsNullOrWhiteSpace(dzStr))
                    Enum.TryParse(dzStr, ignoreCase: true, out deadzone);

                var satStr = (string?)ax.Element("Saturation");
                if (!string.IsNullOrWhiteSpace(satStr))
                    Enum.TryParse(satStr, ignoreCase: true, out saturation);

                axis[func] = new AxisSnapshot(invert, deadzone, saturation);
            }
        }

        var snapshot = new SetupXmlSnapshot(axis, detentsBySafeName);

        lock (_snapLock)
        {
            _snapCache[cacheKey] = new CacheEntry(maxWrite, snapshot);
        }

        return snapshot;
    }

    public static string? SanitizeDeviceNameForLookup(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return null;
        return SanitizeFilePart(deviceName);
    }

    private static string? ExtractSafeDeviceNameFromSetupFilename(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        // Current launcher file name format:
        // Setup.v100.{SafeDeviceName} {GUID}.xml
        if (!fileName!.StartsWith("Setup.v100.", StringComparison.OrdinalIgnoreCase))
            return null;

        // Strip prefix + suffix
        var name = fileName;
        if (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        name = name["Setup.v100.".Length..];

        // name is now: "{SafeDeviceName} {GUID}". Split on " {" which begins the GUID.
        int guidStart = name.IndexOf(" {", StringComparison.Ordinal);
        if (guidStart <= 0) return null;
        var safeDeviceName = name[..guidStart].Trim();
        return string.IsNullOrWhiteSpace(safeDeviceName) ? null : safeDeviceName;
    }

    public string UserConfigDir(string baseDir) => Path.Combine(baseDir, "User", "Config");

    private string? FindExistingUserXmlPathByGuid(string baseDir, Guid instanceGuid)
    {
        var cfgDir = UserConfigDir(baseDir);
        if (!Directory.Exists(cfgDir)) return null;

        // The GUID portion is stable; the device-name portion can vary.
        string guidToken = "{" + instanceGuid.ToString().ToUpperInvariant() + "}";

        return Directory.GetFiles(cfgDir, "Setup.v100.*.xml", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => Path.GetFileName(f).IndexOf(guidToken, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public string BuildUserXmlPath(string baseDir, string deviceName, Guid instanceGuid)
    {
        // If a Setup xml already exists for this GUID, use it.
        // This avoids creating a second file when device name strings differ
        // (e.g., "HOTAS" vs "H.O.T.A.S.") which prevents BMS from seeing curves.
        var existing = FindExistingUserXmlPathByGuid(baseDir, instanceGuid);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing!;

        var safe = SanitizeFilePart(deviceName);
        var file = $"Setup.v100.{safe} {{{instanceGuid.ToString().ToUpperInvariant()}}}.xml";
        return Path.Combine(UserConfigDir(baseDir), file);
    }

    public void EnsureUserXmlExistsFromStock(string baseDir, string deviceName, Guid instanceGuid)
    {
        var userDir = UserConfigDir(baseDir);
        Directory.CreateDirectory(userDir);

        var userPath = BuildUserXmlPath(baseDir, deviceName, instanceGuid);
        if (File.Exists(userPath)) return;

        var stock = FindMatchingStockXml(baseDir, deviceName);

        // Match OLD launcher behavior: if there is no exact Stock template,
        // do NOT create a user XML at all.
        if (stock is null)
            return;

        File.Copy(stock, userPath, overwrite: false);
    }

    public void EnsureUserXmlExistsForWrite(string baseDir, string deviceName, Guid instanceGuid)
    {
        var userDir = UserConfigDir(baseDir);
        Directory.CreateDirectory(userDir);

        var userPath = BuildUserXmlPath(baseDir, deviceName, instanceGuid);
        if (File.Exists(userPath))
            return;

        // Prefer copying exact Stock template if available.
        var stock = FindMatchingStockXml(baseDir, deviceName);
        if (stock is not null)
        {
            File.Copy(stock, userPath, overwrite: false);
            return;
        }

        // Match OLD launcher "save creates XML" behavior:
        // If there's no stock template, we still need a file to write the axis assignment into.
        CreateMinimalUserXml(userPath);
    }

    public void ApplyAxisBinding(string baseDir, AxisFunction function, AxisSelectionResult sel)
    {
        var cfgDir = UserConfigDir(baseDir);
        Directory.CreateDirectory(cfgDir);

        // Ensure the target user XML exists
        EnsureUserXmlExistsForWrite(baseDir, sel.DeviceName, sel.DeviceInstanceGuid);

        // Clear existing binding across all user setup XMLs
        ClearAxisNameAcrossAll(cfgDir, function);

        // Apply to selected device xml (Setup.v100.*.xml)
        var userPath = BuildUserXmlPath(baseDir, sel.DeviceName, sel.DeviceInstanceGuid);
        SetAxisInDeviceXml(userPath, function, sel.PhysicalAxisIndex, sel.Invert, sel.Deadzone, sel.Saturation);

        // IMPORTANT: Falcon reads Deadzone/Saturation for in-game UI/behavior from axismapping.dat
        // Setup XML alone is not enough.
        UpdateAxisMappingDatCurves(baseDir, function, sel.Deadzone, sel.Saturation, resetToUnassignedBlock: false);

        // Optional: backup behavior can be added later (User\Config\Backup)
    }

    public void SaveAllDeviceXmlsFromJoyAssgns(string baseDir, JoyAssgnLite[] joys)
    {
        // Match original launcher behavior:
        // On ANY save (axis or key), write Setup XMLs for ALL devices.
        //
        // We rely on:
        // - DeviceSorting.txt order == joys[] order
        // - DirectInput enumeration gives InstanceGuid (needed for filename token)
        // - Existing user XML (or stock copy) provides axis/detent blocks that we preserve
        // - We rewrite DX/POV tables into a consistent “full” structure (288KB-ish)

        var sorting = new DeviceSortingReader().Read(baseDir);
        if (sorting.Count == 0 || joys.Length == 0)
            return;

        var byProduct = sorting.ToDictionary(d => d.ProductGuid, d => d.SlotIndex);

        IReadOnlyList<DirectInputManager.DeviceInfo> diDevices;
        using (var di = new DirectInputManager())
            diDevices = di.EnumerateDevices();

        var diByProduct = diDevices
            .GroupBy(d => d.ProductGuid)
            .ToDictionary(g => g.Key, g => g.First());

        string? previousProfile = joys[0].CurrentAvionicsProfile;

        try
        {
            for (int slot = 0; slot < sorting.Count; slot++)
            {
                if (slot < 0 || slot >= joys.Length)
                    continue;

                var entry = sorting[slot];

                if (!diByProduct.TryGetValue(entry.ProductGuid, out var diDev))
                    continue;

                string deviceName = !string.IsNullOrWhiteSpace(diDev.Name) ? diDev.Name : entry.Name;
                Guid instanceGuid = diDev.InstanceGuid;

                EnsureUserXmlExistsForWrite(baseDir, deviceName, instanceGuid);

                string userPath = BuildUserXmlPath(baseDir, deviceName, instanceGuid);

                // Ensure file is “full structure” (adds missing dx/pov/profile blocks if stock file is smaller)
                NormalizeJoyAssgnXmlToFullStructure(userPath);

                // Write DX/POV tables from JoyAssgnLite (both F16 and F15 profiles)
                WriteDxPovTables(userPath, joys[slot]);
            }
        }
        finally
        {
            // Restore whatever profile the UI currently had selected.
            for (int i = 0; i < joys.Length; i++)
                joys[i].SelectAvionicsProfile(previousProfile);
        }
    }

    private static void NormalizeJoyAssgnXmlToFullStructure(string xmlPath)
    {
        // If the file already has full blocks, keep it.
        // If it’s a smaller Stock-derived XML lacking full dx/pov/profile blocks,
        // we add missing nodes with SimDoNothing defaults.
        XDocument doc = XDocument.Load(xmlPath);
        var root = doc.Root;
        if (root is null)
            return;

        bool changed = false;

        // detentPosition
        if (root.Element("detentPosition") is null)
        {
            root.AddFirst(new XElement("detentPosition",
                new XElement("AB", "65536"),
                new XElement("IDLE", "0")
            ));
            changed = true;
        }

        // axis (8)
        if (root.Element("axis") is null)
        {
            root.Add(new XElement("axis",
                Enumerable.Range(0, 8).Select(_ =>
                    new XElement("AxAssgn",
                        new XElement("AxisName"),
                        new XElement("AssgnDate", "1998-12-12T12:00:00"),
                        new XElement("Invert", "false"),
                        new XElement("Saturation", "None"),
                        new XElement("Deadzone", "None")
                    )
                )
            ));
            changed = true;
        }

        // pov (4 hats x 8 dirs)
        if (root.Element("pov") is null)
        {
            root.Add(MakePovBlock());
            changed = true;
        }

        // dx (128 buttons x 4 assigns)
        if (root.Element("dx") is null)
        {
            root.Add(MakeDxBlock());
            changed = true;
        }

        // profileDefaultF16
        if (root.Element("profileDefaultF16") is null)
        {
            root.Add(new XElement("profileDefaultF16", MakePovBlock(), MakeDxBlock()));
            changed = true;
        }
        else
        {
            var p = root.Element("profileDefaultF16")!;
            if (p.Element("pov") is null)
            {
                p.Add(MakePovBlock());
                changed = true;
            }

            if (p.Element("dx") is null)
            {
                p.Add(MakeDxBlock());
                changed = true;
            }
        }

        // profileF15ABCD
        if (root.Element("profileF15ABCD") is null)
        {
            root.Add(new XElement("profileF15ABCD", MakePovBlock(), MakeDxBlock()));
            changed = true;
        }
        else
        {
            var p = root.Element("profileF15ABCD")!;
            if (p.Element("pov") is null)
            {
                p.Add(MakePovBlock());
                changed = true;
            }

            if (p.Element("dx") is null)
            {
                p.Add(MakeDxBlock());
                changed = true;
            }
        }

        // Do not rewrite the file if normalization found nothing to add.
        // This preserves current behavior while avoiding a no-op save pass.
        if (!changed)
            return;

        SaveXmlWithLauncherFormatting(xmlPath, doc);
    }

    private static void WriteDxPovTables(string xmlPath, JoyAssgnLite joy)
    {
        XDocument doc = XDocument.Load(xmlPath);
        var root = doc.Root;
        if (root is null)
            return;

        // Preserve axis + detents as-is.
        // Overwrite only dx/pov blocks (root + profiles).

        string? previous = joy.CurrentAvionicsProfile;

        try
        {
            // F16 = root tables
            joy.SelectAvionicsProfile(null);

            ReplacePov(root, joy.Pov);
            ReplaceDx(root, joy.Dx);

            var profF16 = root.Element("profileDefaultF16");
            if (profF16 is not null)
            {
                ReplacePov(profF16, joy.Pov);
                ReplaceDx(profF16, joy.Dx);
            }

            // F15 profile tables
            joy.SelectAvionicsProfile(JoyAssgnLite.F15ProfileTag);

            var profF15 = root.Element("profileF15ABCD");
            if (profF15 is not null)
            {
                ReplacePov(profF15, joy.Pov);
                ReplaceDx(profF15, joy.Dx);
            }

            SaveXmlWithLauncherFormatting(xmlPath, doc);
        }
        finally
        {
            joy.SelectAvionicsProfile(previous);
        }
    }

    private static void ReplaceDx(XElement parent, JoyAssgnLite.DxButton[] dxButtons)
    {
        var dxEl = parent.Element("dx");
        if (dxEl is null)
        {
            dxEl = new XElement("dx");
            parent.Add(dxEl);
        }

        dxEl.RemoveNodes();

        for (int b = 0; b < 128; b++)
        {
            JoyAssgnLite.DxButton btn = b < dxButtons.Length ? dxButtons[b] : MakeEmptyDxButton();

            dxEl.Add(
                new XElement("DxAssgn",
                    new XElement("assign",
                        Enumerable.Range(0, 4).Select(i =>
                        {
                            var a = btn.Assign[i];
                            return new XElement("Assgn",
                                new XElement("Callback", a.Callback),
                                new XElement("Invoke", a.Invoke),
                                new XElement("SoundID", a.SoundId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            );
                        })
                    )
                )
            );
        }
    }

    private static JoyAssgnLite.DxButton MakeEmptyDxButton()
    {
        return new JoyAssgnLite.DxButton(
            new JoyAssgnLite.DxAssgn("SimDoNothing", "Default", 0),
            new JoyAssgnLite.DxAssgn("SimDoNothing", "Default", 0),
            new JoyAssgnLite.DxAssgn("SimDoNothing", "Default", 0),
            new JoyAssgnLite.DxAssgn("SimDoNothing", "Default", 0)
        );
    }

    private static void ReplacePov(XElement parent, JoyAssgnLite.PovHat[] povHats)
    {
        var povEl = parent.Element("pov");
        if (povEl is null)
        {
            povEl = new XElement("pov");
            parent.Add(povEl);
        }

        povEl.RemoveNodes();

        for (int h = 0; h < 4; h++)
        {
            JoyAssgnLite.PovHat hat = h < povHats.Length ? povHats[h] : MakeEmptyPovHat();

            povEl.Add(
                new XElement("PovAssgn",
                    new XElement("direction",
                        Enumerable.Range(0, 8).Select(d =>
                        {
                            var dir = hat.Direction[d];
                            return new XElement("DirAssgn",
                                new XElement("Callback",
                                    new XElement("string", dir.CallbackUnshift),
                                    new XElement("string", dir.CallbackShift)
                                ),
                                new XElement("SoundID",
                                    new XElement("int", dir.SoundIdUnshift.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                                    new XElement("int", dir.SoundIdShift.ToString(System.Globalization.CultureInfo.InvariantCulture))
                                )
                            );
                        })
                    )
                )
            );
        }
    }

    private static JoyAssgnLite.PovHat MakeEmptyPovHat()
    {
        var dirs = new JoyAssgnLite.PovDir[8];
        for (int i = 0; i < 8; i++)
            dirs[i] = new JoyAssgnLite.PovDir("SimDoNothing", "SimDoNothing", 0, 0);
        return new JoyAssgnLite.PovHat(dirs);
    }

    private static XElement MakePovBlock()
    {
        XElement MakeDirAssgn()
            => new XElement("DirAssgn",
                new XElement("Callback",
                    new XElement("string", "SimDoNothing"),
                    new XElement("string", "SimDoNothing")
                ),
                new XElement("SoundID",
                    new XElement("int", "0"),
                    new XElement("int", "0")
                )
            );

        XElement MakePovAssgn()
            => new XElement("PovAssgn",
                new XElement("direction",
                    Enumerable.Range(0, 8).Select(_ => MakeDirAssgn())
                )
            );

        return new XElement("pov",
            Enumerable.Range(0, 4).Select(_ => MakePovAssgn())
        );
    }

    private static XElement MakeDxBlock()
    {
        XElement MakeAssgn()
            => new XElement("Assgn",
                new XElement("Callback", "SimDoNothing"),
                new XElement("Invoke", "Default"),
                new XElement("SoundID", "0")
            );

        XElement MakeDxAssgn()
            => new XElement("DxAssgn",
                new XElement("assign",
                    Enumerable.Range(0, 4).Select(_ => MakeAssgn())
                )
            );

        return new XElement("dx",
            Enumerable.Range(0, 128).Select(_ => MakeDxAssgn())
        );
    }

    private static void SaveXmlWithLauncherFormatting(string xmlPath, XDocument doc)
    {
        var settings = new System.Xml.XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = false,
            NewLineChars = "\r\n",
            NewLineHandling = System.Xml.NewLineHandling.Replace
        };

        Directory.CreateDirectory(Path.GetDirectoryName(xmlPath)!);

        using var writer = System.Xml.XmlWriter.Create(xmlPath, settings);
        doc.Save(writer);
    }

    public void ClearAxisBinding(string baseDir, AxisFunction function)
    {
        var cfgDir = UserConfigDir(baseDir);
        if (!Directory.Exists(cfgDir)) return;

        // Clear in Setup XMLs
        ClearAxisNameAcrossAll(cfgDir, function);

        // Reset curves in axismapping.dat to the stock "unassigned block"
        UpdateAxisMappingDatCurves(baseDir, function, AxCurve.None, AxCurve.None, resetToUnassignedBlock: true);
    }

    public bool TryFindAxisBinding(string baseDir, AxisFunction function, out Guid instanceGuid, out int physicalAxisIndex)
    {
        instanceGuid = Guid.Empty;
        physicalAxisIndex = -1;

        string? targetName = AxisFunctionToSetupAxisName(function);
        if (string.IsNullOrWhiteSpace(targetName))
            return false;

        var cfgDir = UserConfigDir(baseDir);
        if (!Directory.Exists(cfgDir))
            return false;

        string[] files;
        try
        {
            files = Directory.GetFiles(cfgDir, "Setup.v100.*.xml", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return false;
        }

        foreach (var f in files)
        {
            XDocument doc;
            try { doc = XDocument.Load(f); }
            catch { continue; }

            // AxAssgn order corresponds to physical axis index (0..n) for that device.
            int idx = 0;

            foreach (var ax in doc.Descendants().Where(e => e.Name.LocalName == "AxAssgn"))
            {
                var axisName = ax.Elements().FirstOrDefault(e => e.Name.LocalName == "AxisName")?.Value?.Trim();
                if (string.Equals(axisName, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    var g = ExtractInstanceGuidFromSetupFilename(Path.GetFileName(f));
                    if (g is null)
                        return false;

                    instanceGuid = g.Value;
                    physicalAxisIndex = idx;
                    return true;
                }

                idx++;
            }
        }

        return false;
    }

    private static Guid? ExtractInstanceGuidFromSetupFilename(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return null;

        int l = filename!.IndexOf('{');
        int r = filename.IndexOf('}');
        if (l < 0 || r <= l)
            return null;

        var inside = filename.Substring(l + 1, r - l - 1).Trim();
        return Guid.TryParse(inside, out var g) ? g : null;
    }

    public bool TryGetInvert(string baseDir, AxisFunction function, out bool invert)
    {
        invert = false;

        string? targetName = AxisFunctionToSetupAxisName(function);
        if (targetName is null) return false;

        var cfgDir = UserConfigDir(baseDir);
        if (!Directory.Exists(cfgDir))
            return false;

        var files = Directory.GetFiles(cfgDir, "Setup.v100.*.xml", SearchOption.TopDirectoryOnly);

        foreach (var f in files)
        {
            XDocument doc;
            try { doc = XDocument.Load(f); }
            catch { continue; }

            // Find any AxAssgn where AxisName matches, then read sibling Invert
            var axAssgnNodes = doc.Descendants().Where(e => e.Name.LocalName == "AxAssgn");
            foreach (var ax in axAssgnNodes)
            {
                var axisNameEl = ax.Elements().FirstOrDefault(e => e.Name.LocalName == "AxisName");
                if (axisNameEl is null) continue;

                if (!string.Equals(axisNameEl.Value, targetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var invEl = ax.Elements().FirstOrDefault(e => e.Name.LocalName == "Invert");
                if (invEl is null) return false;

                if (bool.TryParse(invEl.Value, out var parsed))
                {
                    invert = parsed;
                    return true;
                }

                // Some files might store invert as 0/1
                if (invEl.Value == "1") { invert = true; return true; }
                if (invEl.Value == "0") { invert = false; return true; }

                return false;
            }
        }

        return false;
    }
    public bool TryGetDeadzone(string baseDir, AxisFunction function, out AxCurve deadzone)
    {
        deadzone = AxCurve.None;

        string? targetName = AxisFunctionToSetupAxisName(function);
        if (targetName is null) return false;

        var cfgDir = UserConfigDir(baseDir);
        if (!Directory.Exists(cfgDir))
            return false;

        var files = Directory.GetFiles(cfgDir, "Setup.v100.*.xml", SearchOption.TopDirectoryOnly);

        foreach (var f in files)
        {
            XDocument doc;
            try { doc = XDocument.Load(f); }
            catch { continue; }

            var axAssgnNodes = doc.Descendants().Where(e => e.Name.LocalName == "AxAssgn");
            foreach (var ax in axAssgnNodes)
            {
                var axisName = ax.Elements().FirstOrDefault(e => e.Name.LocalName == "AxisName")?.Value?.Trim();
                if (!string.Equals(axisName, targetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var dzText = ax.Elements().FirstOrDefault(e => e.Name.LocalName == "Deadzone")?.Value?.Trim();
                if (Enum.TryParse<AxCurve>(dzText, ignoreCase: true, out var dz))
                {
                    deadzone = dz;
                    return true;
                }

                deadzone = AxCurve.None;
                return true;
            }
        }

        return false;
    }

    public bool TryGetSaturation(string baseDir, AxisFunction function, out AxCurve saturation)
    {
        saturation = AxCurve.None;

        string? targetName = AxisFunctionToSetupAxisName(function);
        if (targetName is null) return false;

        var cfgDir = UserConfigDir(baseDir);
        if (!Directory.Exists(cfgDir))
            return false;

        var files = Directory.GetFiles(cfgDir, "Setup.v100.*.xml", SearchOption.TopDirectoryOnly);

        foreach (var f in files)
        {
            XDocument doc;
            try { doc = XDocument.Load(f); }
            catch { continue; }

            var axAssgnNodes = doc.Descendants().Where(e => e.Name.LocalName == "AxAssgn");
            foreach (var ax in axAssgnNodes)
            {
                var axisName = ax.Elements().FirstOrDefault(e => e.Name.LocalName == "AxisName")?.Value?.Trim();
                if (!string.Equals(axisName, targetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var satText = ax.Elements().FirstOrDefault(e => e.Name.LocalName == "Saturation")?.Value?.Trim();
                if (Enum.TryParse<AxCurve>(satText, ignoreCase: true, out var sat))
                {
                    saturation = sat;
                    return true;
                }

                saturation = AxCurve.None;
                return true;
            }
        }

        return false;
    }

    public bool TryGetDetents(string baseDir, string deviceName, out DetentPosition detents)
    {
        detents = DetentPosition.Default;

        var cfgDir = UserConfigDir(baseDir);
        if (!Directory.Exists(cfgDir))
            return false;

        var safe = SanitizeFilePart(deviceName);
        var match = Directory.GetFiles(cfgDir, $"Setup.v100.{safe} *.xml", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();

        if (match is null)
            return false;

        try
        {
            var doc = XDocument.Load(match);
            var det = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "detentPosition");
            if (det is null) return false;

            var abEl = det.Elements().FirstOrDefault(e => e.Name.LocalName == "AB");
            var idleEl = det.Elements().FirstOrDefault(e => e.Name.LocalName == "IDLE");
            if (abEl is null || idleEl is null) return false;

            if (!int.TryParse(abEl.Value, out var ab)) return false;
            if (!int.TryParse(idleEl.Value, out var idle)) return false;

            detents = new DetentPosition(DetentPosition.Clamp(ab), DetentPosition.Clamp(idle));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void SetDetents(string baseDir, string deviceName, Guid instanceGuid, DetentPosition detents)
    {
        var cfgDir = UserConfigDir(baseDir);
        Directory.CreateDirectory(cfgDir);

        EnsureUserXmlExistsFromStock(baseDir, deviceName, instanceGuid);
        var userPath = BuildUserXmlPath(baseDir, deviceName, instanceGuid);

        XDocument doc = XDocument.Load(userPath);

        var root = doc.Root ?? throw new InvalidOperationException("Invalid Setup XML: missing root.");
        var ns = root.GetDefaultNamespace();

        var det = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "detentPosition");
        if (det is null)
        {
            det = new XElement(ns + "detentPosition",
                new XElement(ns + "AB", detents.AB),
                new XElement(ns + "IDLE", detents.IDLE)
            );

            // Stock files place detentPosition as first child under JoyAssgn; keep it near the top.
            root.AddFirst(det);
        }
        else
        {
            var abEl = det.Elements().FirstOrDefault(e => e.Name.LocalName == "AB");
            var idleEl = det.Elements().FirstOrDefault(e => e.Name.LocalName == "IDLE");

            if (abEl is null) det.Add(new XElement(ns + "AB", detents.AB));
            else abEl.Value = detents.AB.ToString();

            if (idleEl is null) det.Add(new XElement(ns + "IDLE", detents.IDLE));
            else idleEl.Value = detents.IDLE.ToString();
        }

        doc.Save(userPath);
    }

    private static void ClearAxisNameAcrossAll(string userConfigDir, AxisFunction function)
    {
        var targetName = AxisFunctionToSetupAxisName(function);
        if (targetName is null) return;
        var files = Directory.GetFiles(userConfigDir, "Setup.v100.*.xml", SearchOption.TopDirectoryOnly);

        foreach (var f in files)
        {
            XDocument doc;
            try { doc = XDocument.Load(f); }
            catch { continue; }

            bool changed = false;

            var axisNameElements = doc.Descendants().Where(e => e.Name.LocalName == "AxisName");
            foreach (var e in axisNameElements)
            {
                if (string.Equals((string?)e.Value, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    e.Value = "";
                    changed = true;
                }
            }

            if (changed)
                doc.Save(f);
        }
    }

    private static void SetAxisInDeviceXml(
        string xmlPath,
        AxisFunction function,
        int physicalAxisIndex,
        bool invert,
        AxCurve deadzone,
        AxCurve saturation)
    {
        var name = AxisFunctionToSetupAxisName(function)
            ?? throw new InvalidOperationException($"No Setup XML AxisName mapping for {function}.");

        XDocument doc = XDocument.Load(xmlPath);

        // Locate AxAssgn list (by LocalName to ignore namespaces)
        var axAssgn = doc.Descendants().Where(e => e.Name.LocalName == "AxAssgn").ToList();

        // If file doesn't have enough AxAssgn nodes, extend it
        while (axAssgn.Count <= physicalAxisIndex)
        {
            // Try to find an <axis> parent; otherwise attach to root
            var axisParent = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "axis")
                             ?? doc.Root
                             ?? throw new InvalidOperationException("Invalid Setup XML: missing root.");

            var ns = axisParent.GetDefaultNamespace();

            // Match launcher style timestamp format (ISO 8601 with offset)
            string nowIso = DateTimeOffset.Now.ToString("o");

            var newAx = new XElement(ns + "AxAssgn",
                new XElement(ns + "AxisName", ""),
                new XElement(ns + "AssgnDate", nowIso),
                new XElement(ns + "Invert", "false"),
                new XElement(ns + "Saturation", "None"),
                new XElement(ns + "Deadzone", "None")
            );

            axisParent.Add(newAx);
            axAssgn = doc.Descendants().Where(e => e.Name.LocalName == "AxAssgn").ToList();
        }

        var target = axAssgn[physicalAxisIndex];

        // Match launcher style timestamp format (ISO 8601 with offset)
        string nowIso2 = DateTimeOffset.Now.ToString("o");

        // Ensure AxisName exists + set it
        var axisNameEl = target.Elements().FirstOrDefault(e => e.Name.LocalName == "AxisName");
        if (axisNameEl is null)
        {
            axisNameEl = new XElement(target.GetDefaultNamespace() + "AxisName", "");
            target.AddFirst(axisNameEl);
        }
        axisNameEl.Value = name;

        // Ensure AssgnDate exists + set it
        var dateEl = target.Elements().FirstOrDefault(e => e.Name.LocalName == "AssgnDate");
        if (dateEl is null)
        {
            dateEl = new XElement(target.GetDefaultNamespace() + "AssgnDate", nowIso2);
            axisNameEl.AddAfterSelf(dateEl);
        }
        else
        {
            dateEl.Value = nowIso2;
        }

        // Ensure Invert exists + set it
        var invEl = target.Elements().FirstOrDefault(e => e.Name.LocalName == "Invert");
        if (invEl is null)
        {
            invEl = new XElement(target.GetDefaultNamespace() + "Invert", invert ? "true" : "false");
            target.Add(invEl);
        }
        else
        {
            invEl.Value = invert ? "true" : "false";
        }

        // Ensure Saturation exists + set it
        var satEl = target.Elements().FirstOrDefault(e => e.Name.LocalName == "Saturation");
        if (satEl is null)
        {
            satEl = new XElement(target.GetDefaultNamespace() + "Saturation", saturation.ToString());
            target.Add(satEl);
        }
        else
        {
            satEl.Value = saturation.ToString();
        }

        // Ensure Deadzone exists + set it
        var dzEl = target.Elements().FirstOrDefault(e => e.Name.LocalName == "Deadzone");
        if (dzEl is null)
        {
            dzEl = new XElement(target.GetDefaultNamespace() + "Deadzone", deadzone.ToString());
            target.Add(dzEl);
        }
        else
        {
            dzEl.Value = deadzone.ToString();
        }

        doc.Save(xmlPath);
    }

    private static readonly AxisFunction[] AxisMappingDatOrder =
{
    AxisFunction.Pitch,
    AxisFunction.Roll,
    AxisFunction.Yaw,
    AxisFunction.Throttle,
    AxisFunction.Throttle_Right,
    AxisFunction.Toe_Brake,
    AxisFunction.Toe_Brake_Right,
    AxisFunction.FOV,
    AxisFunction.Trim_Pitch,
    AxisFunction.Trim_Yaw,
    AxisFunction.Trim_Roll,
    AxisFunction.Radar_Antenna_Elevation,
    AxisFunction.Range_Knob,
    AxisFunction.Cursor_X,
    AxisFunction.Cursor_Y,
    AxisFunction.COMM_Channel_1,
    AxisFunction.COMM_Channel_2,
    AxisFunction.MSL_Volume,
    AxisFunction.Threat_Volume,
    AxisFunction.IntercomVolumeVolume,
    AxisFunction.AI_vs_IVC,
    AxisFunction.HUD_Brightness,
    AxisFunction.FLIR_Brightness,
    AxisFunction.HMS_Brightness,
    AxisFunction.Reticle_Depression,
    AxisFunction.Camera_Distance,
    AxisFunction.HSI_Course_Knob,
    AxisFunction.HSI_Heading_Knob,
    AxisFunction.Altimeter_Knob,
    AxisFunction.ILS_Volume_Knob
};

    private static void UpdateAxisMappingDatCurves(
        string baseDir,
        AxisFunction function,
        AxCurve deadzone,
        AxCurve saturation,
        bool resetToUnassignedBlock)
    {
        int idx = Array.IndexOf(AxisMappingDatOrder, function);
        if (idx < 0) return;

        string cfgDir = Path.Combine(baseDir, "User", "Config");
        string path = Path.Combine(cfgDir, "axismapping.dat");
        if (!File.Exists(path)) return;

        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch { return; }

        const int HeaderSize = 24;
        const int EntrySize = 16;
        const int ExpectedEntries = 30;
        int expectedLen = HeaderSize + (ExpectedEntries * EntrySize);

        if (bytes.Length < expectedLen) return;

        int off = HeaderSize + (idx * EntrySize);

        if (resetToUnassignedBlock)
        {
            // Stock "unassigned block" used by the original launcher:
            // device = 0xFFFFFFFF
            // axis   = 0xFFFFFFFF
            // deadz  = 0x00000064  (Small)
            // sat    = 0xFFFFFFFF  (None)
            bytes[off + 0] = 0xFF; bytes[off + 1] = 0xFF; bytes[off + 2] = 0xFF; bytes[off + 3] = 0xFF;
            bytes[off + 4] = 0xFF; bytes[off + 5] = 0xFF; bytes[off + 6] = 0xFF; bytes[off + 7] = 0xFF;

            bytes[off + 8] = 0x64; bytes[off + 9] = 0x00; bytes[off + 10] = 0x00; bytes[off + 11] = 0x00;

            bytes[off + 12] = 0xFF; bytes[off + 13] = 0xFF; bytes[off + 14] = 0xFF; bytes[off + 15] = 0xFF;
        }
        else
        {
            // Leave device/axis fields alone (first 8 bytes).
            // Just write the curve fields (deadzone + saturation).
            var dz = GetAxDeadZoneBytes(deadzone);
            var sat = GetAxSaturationBytes(saturation);

            bytes[off + 8] = dz[0]; bytes[off + 9] = dz[1]; bytes[off + 10] = dz[2]; bytes[off + 11] = dz[3];
            bytes[off + 12] = sat[0]; bytes[off + 13] = sat[1]; bytes[off + 14] = sat[2]; bytes[off + 15] = sat[3];
        }

        try { File.WriteAllBytes(path, bytes); }
        catch { }
    }

    private static byte[] GetAxDeadZoneBytes(AxCurve axCurve)
    {
        // Matches the original launcher mapping:
        // None   -> 0x00000000
        // Small  -> 0x00000064 (100)
        // Medium -> 0x000001F4 (500)
        // Large  -> 0x000003E8 (1000)
        return axCurve switch
        {
            AxCurve.None => new byte[] { 0x00, 0x00, 0x00, 0x00 },
            AxCurve.Small => new byte[] { 0x64, 0x00, 0x00, 0x00 },
            AxCurve.Medium => new byte[] { 0xF4, 0x01, 0x00, 0x00 },
            AxCurve.Large => new byte[] { 0xE8, 0x03, 0x00, 0x00 },
            _ => new byte[] { 0x00, 0x00, 0x00, 0x00 }
        };
    }

    private static byte[] GetAxSaturationBytes(AxCurve axCurve)
    {
        // Matches the original launcher mapping:
        // None   -> 0xFFFFFFFF
        // Small  -> 0x0000251C (9500)
        // Medium -> 0x00002328 (9000)
        // Large  -> 0x00002134 (8500)
        return axCurve switch
        {
            AxCurve.None => new byte[] { 0xFF, 0xFF, 0xFF, 0xFF },
            AxCurve.Small => new byte[] { 0x1C, 0x25, 0x00, 0x00 },
            AxCurve.Medium => new byte[] { 0x28, 0x23, 0x00, 0x00 },
            AxCurve.Large => new byte[] { 0x34, 0x21, 0x00, 0x00 },
            _ => new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }
        };
    }

    private static string? AxisFunctionToSetupAxisName(AxisFunction f) => f switch
    {
        AxisFunction.Pitch => "Pitch",
        AxisFunction.Roll => "Roll",
        AxisFunction.Yaw => "Yaw",
        AxisFunction.Throttle => "Throttle",
        AxisFunction.Throttle_Right => "Throttle_Right",
        AxisFunction.Toe_Brake => "Toe_Brake",
        AxisFunction.Toe_Brake_Right => "Toe_Brake_Right",
        AxisFunction.FOV => "FOV",
        AxisFunction.Trim_Pitch => "Trim_Pitch",
        AxisFunction.Trim_Yaw => "Trim_Yaw",
        AxisFunction.Trim_Roll => "Trim_Roll",
        AxisFunction.Radar_Antenna_Elevation => "Radar_Antenna_Elevation",
        AxisFunction.Range_Knob => "Range_Knob",
        AxisFunction.Cursor_X => "Cursor_X",
        AxisFunction.Cursor_Y => "Cursor_Y",
        AxisFunction.COMM_Channel_1 => "COMM_Channel_1",
        AxisFunction.COMM_Channel_2 => "COMM_Channel_2",
        AxisFunction.MSL_Volume => "MSL_Volume",
        AxisFunction.Threat_Volume => "Threat_Volume",
        AxisFunction.IntercomVolumeVolume => "IntercomVolume",
        AxisFunction.AI_vs_IVC => "AI_vs_IVC",
        AxisFunction.HUD_Brightness => "HUD_Brightness",
        AxisFunction.FLIR_Brightness => "FLIR_Brightness",
        AxisFunction.HMS_Brightness => "HMS_Brightness",
        AxisFunction.Reticle_Depression => "Reticle_Depression",
        AxisFunction.Camera_Distance => "Camera_Distance",
        AxisFunction.HSI_Course_Knob => "HSI_Course_Knob",
        AxisFunction.HSI_Heading_Knob => "HSI_Heading_Knob",
        AxisFunction.Altimeter_Knob => "Altimeter_Knob",
        AxisFunction.ILS_Volume_Knob => "ILS_Volume_Knob",
        _ => null
    };

    private static string? FindMatchingStockXml(string baseDir, string deviceName)
    {
        // Match OLD launcher behavior: only copy from Stock when the filename matches
        // the sanitized device name exactly. No fuzzy/token matching.

        string safe = SanitizeFilePart(deviceName);

        string appBase = AppContext.BaseDirectory;

        // Support both the old launcher locations and the repo-local Stock folder.
        string[] roots =
        {
        Path.Combine(appBase, "Stock"),
        Path.Combine(Directory.GetCurrentDirectory(), "Stock"),
        Path.Combine(Directory.GetCurrentDirectory(), "Launcher", "Stock"),
        Path.Combine(baseDir, "Launcher", "Stock"),
    };

        // Support both naming conventions:
        // - {Stock}.xml  (what you have)
        // - (Stock).xml  (seen in some distributions)
        string exactA = $"Setup.v100.{safe} {{Stock}}.xml";
        string exactB = $"Setup.v100.{safe} (Stock).xml";

        foreach (var r in roots)
        {
            if (!Directory.Exists(r)) continue;

            var pA = Path.Combine(r, exactA);
            if (File.Exists(pA)) return pA;

            var pB = Path.Combine(r, exactB);
            if (File.Exists(pB)) return pB;
        }

        return null;
    }

    private static void CreateMinimalUserXml(string xmlPath)
    {
        // Match CURRENT launcher “full” XML structure for devices that have no Stock template.
        //
        // Root contains:
        // - detentPosition (AB/IDLE)
        // - axis (8 AxAssgn)
        // - pov (4 PovAssgn, each 8 DirAssgn with 2 callbacks + 2 soundIDs)
        // - dx  (128 DxAssgn, each 4 Assgn)
        // - profileDefaultF16 (pov+dx)
        // - profileF15ABCD    (pov+dx)

        static XElement MakeAxisBlock()
            => new XElement("axis",
                Enumerable.Range(0, 8).Select(_ =>
                    new XElement("AxAssgn",
                        new XElement("AxisName"), // empty => <AxisName />
                        new XElement("AssgnDate", "1998-12-12T12:00:00"),
                        new XElement("Invert", "false"),
                        new XElement("Saturation", "None"),
                        new XElement("Deadzone", "None")
                    )
                )
            );

        static XElement MakeDirAssgn()
            => new XElement("DirAssgn",
                new XElement("Callback",
                    new XElement("string", "SimDoNothing"),
                    new XElement("string", "SimDoNothing")
                ),
                new XElement("SoundID",
                    new XElement("int", "0"),
                    new XElement("int", "0")
                )
            );

        static XElement MakePovAssgn()
            => new XElement("PovAssgn",
                new XElement("direction",
                    Enumerable.Range(0, 8).Select(_ => MakeDirAssgn())
                )
            );

        static XElement MakePovBlock()
            => new XElement("pov",
                Enumerable.Range(0, 4).Select(_ => MakePovAssgn())
            );

        static XElement MakeAssgn()
            => new XElement("Assgn",
                new XElement("Callback", "SimDoNothing"),
                new XElement("Invoke", "Default"),
                new XElement("SoundID", "0")
            );

        static XElement MakeDxAssgn()
            => new XElement("DxAssgn",
                new XElement("assign",
                    Enumerable.Range(0, 4).Select(_ => MakeAssgn())
                )
            );

        static XElement MakeDxBlock()
            => new XElement("dx",
                Enumerable.Range(0, 128).Select(_ => MakeDxAssgn())
            );

        var root = new XElement("JoyAssgn",
            // CURRENT launcher always includes these namespace attributes on the root.
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),

            new XElement("detentPosition",
                // CURRENT launcher defaults:
                // AB = AXISMAX (65536)
                // IDLE = AXISMIN (0)
                new XElement("AB", "65536"),
                new XElement("IDLE", "0")
            ),

            MakeAxisBlock(),
            MakePovBlock(),
            MakeDxBlock(),

            new XElement("profileDefaultF16",
                MakePovBlock(),
                MakeDxBlock()
            ),

            new XElement("profileF15ABCD",
                MakePovBlock(),
                MakeDxBlock()
            )
        );

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);

        var settings = new System.Xml.XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), // UTF-8 no BOM (matches CURRENT)
            Indent = true,
            OmitXmlDeclaration = false,
            NewLineChars = "\r\n",
            NewLineHandling = System.Xml.NewLineHandling.Replace
        };

        Directory.CreateDirectory(Path.GetDirectoryName(xmlPath)!);

        using var writer = System.Xml.XmlWriter.Create(xmlPath, settings);
        doc.Save(writer);
    }

    private static string SanitizeFilePart(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        // Match ORIGINAL launcher JoyAssgn.cs sanitization:
        // Regex.Replace(deviceInstance.ProductName,
        //   @"[^A-Za-z0-9\~\`\[\]\{\}\-_\=\'\x20]", String.Empty);
        // This strips '.' (periods), so "H.O.T.A.S." => "HOTAS".
        return Regex.Replace(s, @"[^A-Za-z0-9\~\`\[\]\{\}\-_\=\'\x20]", string.Empty).Trim();
    }
}