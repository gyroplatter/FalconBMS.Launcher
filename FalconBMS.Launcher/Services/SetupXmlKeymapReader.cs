using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Reads keymapping data back out of FalconBMS Setup.xml files.
/// </summary>

public sealed class SetupXmlKeymapReader
{
    private readonly AxisMappingDatReader _axisDat = new();
    private readonly DeviceSortingReader _sorting = new();
    private readonly SetupXmlService _setupXml = new();

    public sealed record Result(JoyAssgnLite[] Devices, int RollJoyId, int ThrottleJoyId);

    public Result Read(string baseDir)
    {
        var devices = _sorting.Read(baseDir);
        var joys = new List<JoyAssgnLite>(devices.Count);

        foreach (var d in devices)
        {
            // Setup files in this launcher use safe device names in the filename: Setup.v100.{SafeName} {GUID}.xml
            var safe = SetupXmlService.SanitizeDeviceNameForLookup(d.Name);
            if (string.IsNullOrWhiteSpace(safe))
            {
                joys.Add(new JoyAssgnLite(d.Name, MakeEmptyDx(), MakeEmptyPov()));
                continue;
            }

            string? xmlPath = FindSetupXmlBySafeName(baseDir, safe!);
            if (xmlPath is null || !File.Exists(xmlPath))
            {
                joys.Add(new JoyAssgnLite(d.Name, MakeEmptyDx(), MakeEmptyPov()));
                continue;
            }

            joys.Add(ParseSetupXml(d.Name, xmlPath));
        }

        (int rollJoy, int throttleJoy) = GetRollAndThrottleJoyIds(baseDir);

        return new Result(joys.ToArray(), rollJoy, throttleJoy);
    }

    private static string? FindSetupXmlBySafeName(string baseDir, string safeName)
    {
        string cfg = Path.Combine(baseDir, "User", "Config");
        if (!Directory.Exists(cfg)) return null;

        // Exact-ish match on the safe device segment. (Matches how filenames are constructed: "Setup.v100.{safe} {GUID}.xml")
        string prefix = "Setup.v100." + safeName + " {";
        return Directory.GetFiles(cfg, "Setup.v100.*.xml", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static JoyAssgnLite ParseSetupXml(string displayName, string xmlPath)
    {
        XDocument doc = XDocument.Load(xmlPath);
        XElement? root = doc.Root;

        // IMPORTANT (ORIGINAL parity):
        // Setup XML contains 3 DX/POV tables:
        //   1) root <dx>/<pov> (active)
        //   2) <profileDefaultF16>/<dx>/<pov>
        //   3) <profileF15ABCD>/<dx>/<pov>
        //
        // The ORIGINAL launcher loads devices once and swaps tables in memory when switching profiles.
        // So we must parse these tables explicitly (do NOT use Descendants() across the whole document).

        var dxActive = ReadDxButtons(root?.Element("dx")) ?? MakeEmptyDx();
        var povActive = ReadPovHats(root?.Element("pov")) ?? MakeEmptyPov();

        // IMPORTANT ORIGINAL PARITY RULE:
        // F16 active tables ALWAYS come from root <dx>/<pov>.
        // <profileDefaultF16> is only a storage snapshot used when switching away.
        // We must NOT preload F16 from profileDefaultF16.

        var profF15 = root?.Element("profileF15ABCD");
        var dxF15 = ReadDxButtons(profF15?.Element("dx")) ?? MakeEmptyDx();
        var povF15 = ReadPovHats(profF15?.Element("pov")) ?? MakeEmptyPov();

        return new JoyAssgnLite(
            productName: displayName,
            dxActive: dxActive,
            povActive: povActive,
            profileDefaultF16Dx: dxActive,   // F16 starts as root
            profileDefaultF16Pov: povActive, // F16 starts as root
            profileF15Dx: dxF15,
            profileF15Pov: povF15
        );
    }

    private static JoyAssgnLite.DxButton[]? ReadDxButtons(XElement? dxElement)
    {
        if (dxElement is null) return null;

        // XML structure (verified):
        // <dx>
        //   <DxAssgn>
        //     <assign>
        //       <Assgn>...</Assgn> x4
        //     </assign>
        //   </DxAssgn> x128
        // </dx>
        //
        // Some older variants may omit <assign> and place <Assgn> directly under <DxAssgn>.
        var dxButtons = dxElement.Elements("DxAssgn")
            .Select(dxAssgn =>
            {
                var assignContainer = dxAssgn.Element("assign");
                var assgns = (assignContainer is not null
                        ? assignContainer.Elements("Assgn")
                        : dxAssgn.Elements("Assgn"))
                    .Take(4)
                    .ToArray();

                string cb0 = (string?)assgns.ElementAtOrDefault(0)?.Element("Callback") ?? "SimDoNothing";
                string iv0 = (string?)assgns.ElementAtOrDefault(0)?.Element("Invoke") ?? "Default";
                int sd0 = (int?)assgns.ElementAtOrDefault(0)?.Element("SoundID") ?? 0;

                string cb1 = (string?)assgns.ElementAtOrDefault(1)?.Element("Callback") ?? "SimDoNothing";
                string iv1 = (string?)assgns.ElementAtOrDefault(1)?.Element("Invoke") ?? "Default";
                int sd1 = (int?)assgns.ElementAtOrDefault(1)?.Element("SoundID") ?? 0;

                string cb2 = (string?)assgns.ElementAtOrDefault(2)?.Element("Callback") ?? "SimDoNothing";
                string iv2 = (string?)assgns.ElementAtOrDefault(2)?.Element("Invoke") ?? "Default";
                int sd2 = (int?)assgns.ElementAtOrDefault(2)?.Element("SoundID") ?? 0;

                string cb3 = (string?)assgns.ElementAtOrDefault(3)?.Element("Callback") ?? "SimDoNothing";
                string iv3 = (string?)assgns.ElementAtOrDefault(3)?.Element("Invoke") ?? "Default";
                int sd3 = (int?)assgns.ElementAtOrDefault(3)?.Element("SoundID") ?? 0;

                return new JoyAssgnLite.DxButton(
                    new JoyAssgnLite.DxAssgn(cb0, iv0, sd0),
                    new JoyAssgnLite.DxAssgn(cb1, iv1, sd1),
                    new JoyAssgnLite.DxAssgn(cb2, iv2, sd2),
                    new JoyAssgnLite.DxAssgn(cb3, iv3, sd3)
                );
            })
            .ToArray();

        return dxButtons.Length == 0 ? null : dxButtons;
    }

    private static JoyAssgnLite.PovHat[]? ReadPovHats(XElement? povElement)
    {
        if (povElement is null) return null;

        // XML structure (verified):
        // <pov>
        //   <PovAssgn>
        //     <direction>
        //       <DirAssgn>...</DirAssgn> x8
        //     </direction>
        //   </PovAssgn> up to 4
        // </pov>
        //
        // Some older variants may omit <direction> and place <DirAssgn> directly under <PovAssgn>.
        var hats = povElement.Elements("PovAssgn")
            .Take(4)
            .Select(povAssgn =>
            {
                var dirContainer = povAssgn.Element("direction");
                var dirNodes = (dirContainer is not null
                        ? dirContainer.Elements("DirAssgn")
                        : povAssgn.Elements("DirAssgn"))
                    .Take(8)
                    .ToArray();

                var dirs = dirNodes.Select(dir =>
                {
                    var cbStrings = dir.Element("Callback")?.Elements("string").ToArray() ?? Array.Empty<XElement>();
                    string unshift = cbStrings.ElementAtOrDefault(0)?.Value ?? "SimDoNothing";
                    string shift = cbStrings.ElementAtOrDefault(1)?.Value ?? "SimDoNothing";

                    var sndInts = dir.Element("SoundID")?.Elements("int").ToArray() ?? Array.Empty<XElement>();
                    int unSnd = int.TryParse(sndInts.ElementAtOrDefault(0)?.Value, out var a) ? a : 0;
                    int shSnd = int.TryParse(sndInts.ElementAtOrDefault(1)?.Value, out var b) ? b : 0;

                    return new JoyAssgnLite.PovDir(unshift, shift, unSnd, shSnd);
                }).ToArray();

                if (dirs.Length != 8)
                {
                    var padded = new JoyAssgnLite.PovDir[8];
                    for (int i = 0; i < 8; i++)
                        padded[i] = (i < dirs.Length) ? dirs[i] : new JoyAssgnLite.PovDir("SimDoNothing", "SimDoNothing", 0, 0);
                    dirs = padded;
                }

                return new JoyAssgnLite.PovHat(dirs);
            })
            .ToArray();

        return hats.Length == 0 ? null : hats;
    }

    private (int rollJoy, int throttleJoy) GetRollAndThrottleJoyIds(string baseDir)
    {
        // ORIGINAL uses inGameAxis Roll/Throttle device numbers.
        // In this launcher, we can derive the same device numbers from axismapping.dat entries for Roll/Throttle indices.
        var dat = _axisDat.Read(baseDir);
        if (dat is null) return (-1, -1);

        int rollIndex = AxisCatalog.All.First(x => x.Function == AxisFunction.Roll).MappingIndex;
        int throttleIndex = AxisCatalog.All.First(x => x.Function == AxisFunction.Throttle).MappingIndex;

        int rollJoy = dat.Entries.First(e => e.Index == rollIndex).JoyNum;
        int throttleJoy = dat.Entries.First(e => e.Index == throttleIndex).JoyNum;

        return (rollJoy, throttleJoy);
    }

    private static JoyAssgnLite.DxButton[] MakeEmptyDx()
    {
        var dx = new JoyAssgnLite.DxButton[128];
        for (int i = 0; i < 128; i++)
        {
            dx[i] = new JoyAssgnLite.DxButton(
                new JoyAssgnLite.DxAssgn("SimDoNothing", "Default", 0),
                new JoyAssgnLite.DxAssgn("SimDoNothing", "Default", 0),
                new JoyAssgnLite.DxAssgn("SimDoNothing", "Default", 0),
                new JoyAssgnLite.DxAssgn("SimDoNothing", "Default", 0)
            );
        }
        return dx;
    }

    private static JoyAssgnLite.PovHat[] MakeEmptyPov()
    {
        var pov = new JoyAssgnLite.PovHat[4];
        for (int h = 0; h < 4; h++)
        {
            var dirs = new JoyAssgnLite.PovDir[8];
            for (int d = 0; d < 8; d++)
                dirs[d] = new JoyAssgnLite.PovDir("SimDoNothing", "SimDoNothing", 0, 0);
            pov[h] = new JoyAssgnLite.PovHat(dirs);
        }
        return pov;
    }
}