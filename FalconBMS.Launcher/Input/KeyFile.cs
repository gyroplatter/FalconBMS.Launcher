using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace FalconBMS.Launcher.Input;

/// <summary>
/// Parser and container for FalconBMS key files, including validation, regex-based line parsing, cloning, and callback lookup.
/// </summary>

public sealed class KeyFile : ICloneable
{
    public KeyAssgn[] keyAssign = Array.Empty<KeyAssgn>();
    public string[] categoryHeaderLabels = Array.Empty<string>();

    public KeyFile(string filename)
    {
        if (!File.Exists(filename))
            return;

        ValidateKeyfileLines(filename);

        var records = new List<KeyAssgn>(2000);
        var cats = new List<string>(12);

        using (var reader = File.OpenText(filename))
        {
            while (true)
            {
                string? line = reader.ReadLine();
                if (line is null) break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (RegexFactory.LineComment.IsMatch(line))
                    continue;

                // Ignore any DX button/hat bindings, we're only interested in keyboard bindings here
                if (RegexFactory.ButtonOrHatBindingLine.IsMatch(line))
                    continue;

                if (RegexFactory.CategoryHeaderLine.IsMatch(line))
                    cats.Add(ParseCategoryHeaderLabel(line)); // also falls through and becomes a KeyAssgn row

                if (RegexFactory.KeyBindingLine.IsMatch(line))
                {
                    KeyAssgn? keyAssgn = ParseKeyfileLine(line);
                    if (keyAssgn != null)
                        records.Add(keyAssgn);
                }
            }
        }

        keyAssign = records.ToArray();
        categoryHeaderLabels = cats.ToArray();
    }

    public static bool ValidateKeyfileLines(string filename)
    {
        if (!File.Exists(filename)) return false;

        var errors = new List<string>(50);

        using (var reader = File.OpenText(filename))
        {
            int lineNum = 0;
            while (true)
            {
                ++lineNum;

                string? line = reader.ReadLine();
                if (line is null) break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;
                if (RegexFactory.LineComment.IsMatch(line))
                    continue;
                if (RegexFactory.ButtonOrHatBindingLine.IsMatch(line))
                    continue;
                if (RegexFactory.KeyBindingLine.IsMatch(line))
                    continue;

                string err = $"Unrecognized line #{lineNum}: {line}";
                errors.Add(err);
            }
        }

        return errors.Count == 0;
    }

    private static string ParseCategoryHeaderLabel(string line)
    {
        Match m = RegexFactory.CategoryHeaderLine.Match(line);
        Debug.Assert(m.Success);

        return m.Groups["categoryHeaderDQ"].Value;
    }

    internal static KeyAssgn? ParseKeyfileLine(string line)
    {
        if (RegexFactory.ButtonOrHatBindingLine.IsMatch(line))
            return null;

        Match m = RegexFactory.KeyBindingLine.Match(line);
        if (!m.Success)
            throw new InvalidDataException("Unexpected line in keyfile: " + line);

        string callbackName = m.Groups["callbackName"].Value;
        string soundId = m.Groups["soundId"].Value;
        string keyScancodeHex = m.Groups["keyScancodeHex"].Value;
        string keyModifierFlags = m.Groups["keyModifierFlags"].Value;
        string chordScancodeHex = m.Groups["chordScancodeHex"].Value;
        string chordModifierFlags = m.Groups["chordModifierFlags"].Value;
        string displayFlags = m.Groups["displayFlags"].Value;
        string descriptionStringDQ = m.Groups["descriptionStringDQ"].Value;

        return new KeyAssgn(
            callbackName, soundId, "0", keyScancodeHex, keyModifierFlags, chordScancodeHex, chordModifierFlags, displayFlags, descriptionStringDQ
        );
    }

    public KeyAssgn? LookupCallback(string callbackName)
    {
        foreach (var ka in keyAssign)
            if (ka.GetCallback() == callbackName) return ka;
        return null;
    }

    public KeyFile(IReadOnlyList<KeyAssgn> keyAssign)
    {
        this.keyAssign = new KeyAssgn[keyAssign.Count];
        for (int i = 0; i < keyAssign.Count; i++)
            this.keyAssign[i] = keyAssign[i].Clone();
    }

    object ICloneable.Clone() => Clone();

    public KeyFile Clone() => new(keyAssign);

    internal static class RegexFactory
    {
        private readonly static Dictionary<string, string> _patternMap = new()
        {
            { "@DoubleQuote", @"\x22" },
            { "@NumberSign", @"\x23" },
            { "@MinusSymbol", @"\x2D" },
            { "@CallbackIdentifier", "[A-Za-z0-9_]+" },
            { "@HexIdentifier", "0([xX][0-9A-Fa-f]{1,8})?" },
        };

        public static Regex LineComment = Create(@"(?nsx)
            ^
                \s* @NumberSign .*
            $");

        public static Regex CategoryHeaderLine = Create(@"(?nsx)
            ^\s*
                SimDoNothing \s+ -1 \s+ 0 \s+ 0[xX]FFFFFFFF \s+ 0 \s+ 0 \s+ 0 \s+ -1 \s+ (?<categoryHeaderDQ> @DoubleQuote \d+\. \s [^@DoubleQuote]+ @DoubleQuote)
            \s*$");

        public static Regex ButtonOrHatBindingLine = Create(@"(?nsx)
            ^\s*
                @CallbackIdentifier \s+ @HexIdentifier \s+ -1 \s+ -2 \s+ \d+ \s+ 0x0 \s+ -?\d+
            \s*$");

        public static Regex KeyBindingLine = Create(@"(?nsx)
            ^\s*
                (?<callbackName> @CallbackIdentifier) \s+
                (?<soundId> -?\d+) \s+
                (?<unused> -?\d+) \s+
                (?<keyScancodeHex> 0[xX][0-9A-Fa-f]{1,8} | 0[xX]FFFFFFFF) \s+
                (?<keyModifierFlags> \d+) \s+
                (?<chordScancodeHex> @HexIdentifier) \s+
                (?<chordModifierFlags> \d+) \s+
                (?<displayFlags> -?\d+) \s+
                (?<descriptionStringDQ> @DoubleQuote [^@DoubleQuote]* @DoubleQuote)
            \s*$");

        private static Regex Create(string pattern)
        {
            foreach (var kvp in _patternMap)
                pattern = pattern.Replace(kvp.Key, kvp.Value);

            // ORIGINAL key files contain both 0x and 0X forms (example: 0XFFFFFFFF),
            // so key-line parsing must be case-insensitive to preserve all bindings.
            return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }
    }
}