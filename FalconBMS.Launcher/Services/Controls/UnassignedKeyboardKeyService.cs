using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FalconBMS.Launcher.Services.Controls;

public sealed class UnassignedKeyboardKeyService
{
    public IReadOnlyList<UnassignedKeyboardKeyCandidate> BuildRows(
        IReadOnlyList<BindingRow> selectedProfileRows,
        string filterText)
    {
        var usedKeyboardAssignments =
            new HashSet<string>(
                selectedProfileRows
                    .Where(row => row.IsCallback)
                    .Where(row => !string.IsNullOrWhiteSpace(row.KeyScancode))
                    .Where(row => !string.Equals(row.KeyScancode, "0xFFFFFFFF", StringComparison.OrdinalIgnoreCase))
                    .Select(row =>
                        BuildKeyboardIdentity(
                            row.KeyScancode,
                            row.KeyModifierFlags,
                            row.ChordScancode,
                            row.ChordModifierFlags)),
                StringComparer.OrdinalIgnoreCase);

        return BuildUnassignedKeyCandidates()
            .Where(candidate => !usedKeyboardAssignments.Contains(candidate.Identity))
            .Where(candidate => PassesTextFilter(candidate, filterText))
            .ToList();
    }

    private static bool PassesTextFilter(
        UnassignedKeyboardKeyCandidate candidate,
        string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
            return true;

        return Contains(candidate.DisplayText, filterText) ||
               Contains(candidate.ModifierDisplayName, filterText) ||
               Contains(candidate.BaseKeyDisplayName, filterText);
    }

    private static bool Contains(string value, string filter)
    {
        return value?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string BuildKeyboardIdentity(
        string keyScancode,
        int keyModifierFlags,
        string chordScancode,
        int chordModifierFlags)
    {
        return NormalizeScancodeForIdentity(keyScancode) + "|" +
               keyModifierFlags + "|" +
               NormalizeScancodeForIdentity(chordScancode) + "|" +
               chordModifierFlags;
    }

    private static string NormalizeScancodeForIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "0";

        string trimmed = value.Trim();

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring(2);

        if (!int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsed))
            return value.Trim().ToUpperInvariant();

        return parsed.ToString("X", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<UnassignedKeyboardKeyCandidate> BuildUnassignedKeyCandidates()
    {
        var candidates = new List<UnassignedKeyboardKeyCandidate>();

        IReadOnlyList<UnassignedBaseKey> baseKeys = BuildUnassignedBaseKeys();
        IReadOnlyList<UnassignedModifierGroup> modifierGroups = BuildUnassignedModifierGroups();

        foreach (UnassignedBaseKey baseKey in baseKeys)
        {
            foreach (UnassignedModifierGroup modifierGroup in modifierGroups)
            {
                string displayText =
                    string.IsNullOrWhiteSpace(modifierGroup.DisplayPrefix)
                        ? baseKey.DisplayName
                        : modifierGroup.DisplayPrefix + " " + baseKey.DisplayName;

                string identity =
                    BuildKeyboardIdentity(
                        "0x" + baseKey.Scancode.ToString("X", CultureInfo.InvariantCulture),
                        modifierGroup.ModifierFlags,
                        "0",
                        0);

                string keySortKey =
                    baseKey.SortOrder.ToString("D4", CultureInfo.InvariantCulture) + "_" +
                    modifierGroup.SortOrder.ToString("D2", CultureInfo.InvariantCulture) + "_" +
                    displayText;

                string modifierSortKey =
                    modifierGroup.SortOrder.ToString("D2", CultureInfo.InvariantCulture) + "_" +
                    baseKey.SortOrder.ToString("D4", CultureInfo.InvariantCulture) + "_" +
                    displayText;

                string baseKeySortKey =
                    baseKey.SortOrder.ToString("D4", CultureInfo.InvariantCulture) + "_" +
                    modifierGroup.SortOrder.ToString("D2", CultureInfo.InvariantCulture) + "_" +
                    displayText;

                candidates.Add(new UnassignedKeyboardKeyCandidate
                {
                    DisplayText = displayText,
                    ModifierDisplayName = modifierGroup.DisplayName,
                    BaseKeyDisplayName = baseKey.DisplayName,
                    Identity = identity,
                    KeySortKey = keySortKey,
                    ModifierSortKey = modifierSortKey,
                    BaseKeySortKey = baseKeySortKey
                });
            }
        }

        return candidates;
    }

    private static IReadOnlyList<UnassignedModifierGroup> BuildUnassignedModifierGroups()
    {
        return new[]
        {
            new UnassignedModifierGroup
            {
                DisplayName = "None",
                DisplayPrefix = "",
                ModifierFlags = 0,
                SortOrder = 0
            },
            new UnassignedModifierGroup
            {
                DisplayName = "Shift",
                DisplayPrefix = "Shift",
                ModifierFlags = 1,
                SortOrder = 1
            },
            new UnassignedModifierGroup
            {
                DisplayName = "Ctrl",
                DisplayPrefix = "Ctrl",
                ModifierFlags = 2,
                SortOrder = 2
            },
            new UnassignedModifierGroup
            {
                DisplayName = "Alt",
                DisplayPrefix = "Alt",
                ModifierFlags = 4,
                SortOrder = 3
            }
        };
    }

    private static IReadOnlyList<UnassignedBaseKey> BuildUnassignedBaseKeys()
    {
        var keys = new List<UnassignedBaseKey>();

        int order = 0;

        void Add(string displayName, int scancode)
        {
            keys.Add(new UnassignedBaseKey
            {
                DisplayName = displayName,
                Scancode = scancode,
                SortOrder = order++
            });
        }

        Add("A", 0x1E);
        Add("B", 0x30);
        Add("C", 0x2E);
        Add("D", 0x20);
        Add("E", 0x12);
        Add("F", 0x21);
        Add("G", 0x22);
        Add("H", 0x23);
        Add("I", 0x17);
        Add("J", 0x24);
        Add("K", 0x25);
        Add("L", 0x26);
        Add("M", 0x32);
        Add("N", 0x31);
        Add("O", 0x18);
        Add("P", 0x19);
        Add("Q", 0x10);
        Add("R", 0x13);
        Add("S", 0x1F);
        Add("T", 0x14);
        Add("U", 0x16);
        Add("V", 0x2F);
        Add("W", 0x11);
        Add("X", 0x2D);
        Add("Y", 0x15);
        Add("Z", 0x2C);

        Add("0", 0x0B);
        Add("1", 0x02);
        Add("2", 0x03);
        Add("3", 0x04);
        Add("4", 0x05);
        Add("5", 0x06);
        Add("6", 0x07);
        Add("7", 0x08);
        Add("8", 0x09);
        Add("9", 0x0A);

        Add("F1", 0x3B);
        Add("F2", 0x3C);
        Add("F3", 0x3D);
        Add("F4", 0x3E);
        Add("F5", 0x3F);
        Add("F6", 0x40);
        Add("F7", 0x41);
        Add("F8", 0x42);
        Add("F9", 0x43);
        Add("F10", 0x44);
        Add("F11", 0x57);
        Add("F12", 0x58);

        Add("Insert", 0xD2);
        Add("Delete", 0xD3);
        Add("Home", 0xC7);
        Add("End", 0xCF);
        Add("PageUp", 0xC9);
        Add("PageDown", 0xD1);
        Add("Up", 0xC8);
        Add("Down", 0xD0);
        Add("Left", 0xCB);
        Add("Right", 0xCD);

        Add("BackSpace", 0x0E);
        Add("Enter", 0x1C);
        Add("Space", 0x39);
        Add("Tab", 0x0F);

        Add("Grave", 0x29);
        Add("Minus", 0x0C);
        Add("Equals", 0x0D);
        Add("LeftBracket", 0x1A);
        Add("RightBracket", 0x1B);
        Add("Backslash", 0x2B);
        Add("Semicolon", 0x27);
        Add("Apostrophe", 0x28);
        Add("Comma", 0x33);
        Add("Period", 0x34);
        Add("Slash", 0x35);

        Add("Numpad 0", 0x52);
        Add("Numpad 1", 0x4F);
        Add("Numpad 2", 0x50);
        Add("Numpad 3", 0x51);
        Add("Numpad 4", 0x4B);
        Add("Numpad 5", 0x4C);
        Add("Numpad 6", 0x4D);
        Add("Numpad 7", 0x47);
        Add("Numpad 8", 0x48);
        Add("Numpad 9", 0x49);
        Add("Numpad Add", 0x4E);
        Add("Numpad Subtract", 0x4A);
        Add("Numpad Multiply", 0x37);
        Add("Numpad Divide", 0xB5);
        Add("Numpad Decimal", 0x53);
        Add("Numpad Enter", 0x9C);

        return keys;
    }

    private sealed class UnassignedModifierGroup
    {
        public string DisplayName { get; init; } = "";
        public string DisplayPrefix { get; init; } = "";
        public int ModifierFlags { get; init; }
        public int SortOrder { get; init; }
    }

    private sealed class UnassignedBaseKey
    {
        public string DisplayName { get; init; } = "";
        public int Scancode { get; init; }
        public int SortOrder { get; init; }
    }
}

public sealed class UnassignedKeyboardKeyCandidate
{
    public string DisplayText { get; init; } = "";
    public string ModifierDisplayName { get; init; } = "";
    public string BaseKeyDisplayName { get; init; } = "";
    public string Identity { get; init; } = "";
    public string KeySortKey { get; init; } = "";
    public string ModifierSortKey { get; init; } = "";
    public string BaseKeySortKey { get; init; } = "";
}