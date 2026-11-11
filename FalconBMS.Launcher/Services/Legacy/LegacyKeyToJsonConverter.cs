using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FalconBMS.Launcher.Services.Legacy;

/// <summary>
/// Converts a legacy BMS ".key" file into the (schema v2) JSON keyboard-binding format.
/// Only F-16 is supported, for this import path.. and only keyboard-binding lines are imported. 
/// </summary>
internal static class LegacyKeyToJsonConverter
{
    const string AircraftProfile = "F-16";

    /// <summary>
    /// Converts the .key file at <paramref name="sourcePath"/> to JSON, to a uniquely-named 
    /// file in the %temp% directory.  Returns the full path of that temp json file.
    /// </summary>
    public static string ConvertLegacyKeyToJson(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath))
            throw new ArgumentNullException("sourcePath");

        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string jsonPath = Path.Combine(Path.GetTempPath(),
            baseName + "_" + Guid.NewGuid().ToString("N") + ".json");
            
        var reader = File.OpenText(sourcePath);
        var writer = new StreamWriter(jsonPath, append:false, new UTF8Encoding(encoderShouldEmitUTF8Identifier:false));

        _WriteJsonHeader(writer);
        _ConvertLineByLine(reader, writer);
        _WriteJsonFooter(writer);

        reader.Close();
        writer.Close();

        return jsonPath;
    }

    //
    // Implementation

    static void _ConvertLineByLine( StreamReader reader, StreamWriter writer )
    {
        var regex = _CreateTemplatedRegex(s_keyBindingLine);

        while (true)
        {
            string key_line = reader.ReadLine();
            if (key_line == null) break;
            if (key_line == string.Empty) continue;

            var m = regex.Match(key_line);
            if (!m.Success) continue;

            int soundId, unused, keyScancode, keyMods, chordScancode, chordMods, visibility;
            int num_errs = 0;
            num_errs += TryParseDec(m.Groups["sound_id"].Value, out soundId);
            num_errs += TryParseDec(m.Groups["unused"].Value, out unused);
            num_errs += TryParseHex(m.Groups["key_scancode_hex"].Value, out keyScancode);
            num_errs += TryParseDec(m.Groups["key_modifier_flags"].Value, out keyMods);
            num_errs += TryParseHex(m.Groups["chord_scancode_hex"].Value, out chordScancode);
            num_errs += TryParseDec(m.Groups["chord_modifier_flags"].Value, out chordMods);
            num_errs += TryParseDec(m.Groups["visibility_flags"].Value, out visibility);
            if (num_errs > 0)
                continue; // eg. overflow in the 'unused' digits -- treat as non-conforming

            var rec = new KeyBindingRecord(
                callback_name: m.Groups["callback_name"].Value,
                description: m.Groups["description"].Value.Trim('\x22'), // trim doublequote delims
                category_name: "",
                section_name: "",
                sound_id: soundId,
                key_scancode: keyScancode,
                key_modifier_flags: keyMods,
                chord_scancode: chordScancode,
                chord_modifier_flags: chordMods,
                unused: unused,
                visibility: visibility,
                row_kind: "EditableCallback",
                is_modified: true);

            _WriteJsonElement(rec, writer);
        }
        return;
    }

    record KeyBindingRecord(
        string callback_name,
        string description,
        string category_name,
        string section_name,
        int sound_id,
        int key_scancode,
        int key_modifier_flags,
        int chord_scancode,
        int chord_modifier_flags,
        int unused,
        int visibility,
        string row_kind,
        bool is_modified
    );

    static readonly string s_keyBindingLine = @"(?nsx) #ExplicitCapture, Singleline, IgnorePatternWhitespace
        ^\s*
            (?<callback_name> @CallbackIdentifier )
        \s+
            (?<sound_id> @MinusSymbol?[0-9]+ )
        \s+
            (?<unused> [0-9]+ ) #note: typically 0 but some older keyfiles have digits here
        \s+
            (?<key_scancode_hex> @HexIdentifier )
        \s+
            (?<key_modifier_flags> [0-7] )
        \s+
            (?<chord_scancode_hex> @HexIdentifier )
        \s+
            (?<chord_modifier_flags> [0-7] )
        \s+
            (?<visibility_flags> [0-9] )
        \s+
            (?<description> @DoubleQuote [^@DoubleQuote]* @DoubleQuote )
            (
                \s*
                (?<trailing_comment> @NumberSign .* ) # optional trailing comment
            )?
        \s*$";

    static readonly Dictionary<string, string> s_patternMap = new Dictionary<string, string>() {
        { "@DoubleQuote", @"\x22" },
        { "@NumberSign", @"\x23" },
        { "@MinusSymbol", @"\x2D" },
        { "@CallbackIdentifier", "[A-Za-z0-9_]+" },
        { "@HexIdentifier", "0([xX][0-9A-Fa-f]{1,8})?" },
    };

    static Regex _CreateTemplatedRegex( string pattern )
    {
        foreach (KeyValuePair<string, string> kv in s_patternMap)
            pattern = pattern.Replace(kv.Key, kv.Value);

        return new Regex(pattern, RegexOptions.CultureInvariant);
    }

    static void _WriteJsonHeader( StreamWriter sw )
    {
        sw.WriteLine("{");
        sw.WriteLine("  \"schema_version\": 2,");
        sw.WriteLine("  \"binding_type\": \"keyboard\",");
        sw.WriteLine("  \"aircraft_profile\": " + EscapeJsonString(AircraftProfile) + ",");
        sw.WriteLine("  \"rows\": [");
    }

    static void _WriteJsonFooter( StreamWriter sw )
    {
        sw.WriteLine("  ]");
        sw.WriteLine("}");
    }

    static void _WriteJsonElement( KeyBindingRecord rec, StreamWriter sw )
    {
        sw.WriteLine("{");
        _WriteJsonValue(sw, "callback_name", rec.callback_name);
        _WriteJsonValue(sw, "description", rec.description);
        _WriteJsonValue(sw, "sound_id", rec.sound_id);
        _WriteJsonValue(sw, "key_scancode", rec.key_scancode > 0 ? rec.key_scancode : null);
        _WriteJsonValue(sw, "key_modifier_flags", rec.key_modifier_flags);
        _WriteJsonValue(sw, "chord_scancode", rec.chord_scancode);
        _WriteJsonValue(sw, "chord_modifier_flags", rec.chord_modifier_flags);
        _WriteJsonValue(sw, "unused", rec.unused);
        _WriteJsonValue(sw, "row_kind", rec.row_kind);
        _WriteJsonValue(sw, "is_modified", rec.is_modified);
        sw.WriteLine("},");
    }

    static void _WriteJsonValue( StreamWriter sw, string name, object? value )
    {
        if (value == null)
            sw.WriteLine($"\x22{name}\x22: null,");
        else if (value is bool)
            sw.WriteLine($"\x22{name}\x22: {value.ToString().ToLowerInvariant()},");
        else if (value is string)
            sw.WriteLine($"\x22{name}\x22: {EscapeJsonString(value.ToString())},");
        else if (value is int)
            sw.WriteLine($"\x22{name}\x22: {((int)value).ToString(CultureInfo.InvariantCulture)},");
        else
            throw new InvalidProgramException();
    }

    // "0" or "0x1C" (up to 8 hex digits, as matched by @HexIdentifier).
    static int TryParseHex(string s, out int value)
    {
        value = 0;
        if (s == "0") return 0;//ok
        if (s.ToLowerInvariant().StartsWith("0x"))
            s = s.Substring(2); //note AllowHexSpecifier doesn't actually allow the "0x" prefix
        bool ok = int.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        return ok ? 0 : 1;
    }

    static int TryParseDec( string s, out int value )
    {
        value = 0;
        if (s == "0") return 0;//ok
        bool ok = int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        return ok ? 0 : 1;
    }

    static string EscapeJsonString( string raw )
    {
        if (raw == null) 
            return @"null";

        string escaped = raw
            .Replace("\\", @"\\") //backslash (\x5C): Note, do this first!
            .Replace("'", @"\'") //single-quote (\x27)
            .Replace("\"", @"\""") //double-quote (\x22)
            .Replace("\x0D", @"\r") //carriage-return (\x0D)
            .Replace("\f", @"\f") //form-feed (\x0C)
            .Replace("\v", @"\v") //vertical-tab (\x0B)
            .Replace("\x0A", @"\n") //newline (\x0A)
            .Replace("\t", @"\t") //horizontal-tab (\x09)
            .Replace("\b", @"\b") //backspace (\x08)
        ;

        return "\x22" + escaped + "\x22";
    }
}
