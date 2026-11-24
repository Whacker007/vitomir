// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The engine's settings file: plain INI, because a player or a modder should be able to open it in
// Notepad and change one line without a schema or a linter. The original's own settings
// (masquerade.ini, dxcfg.ini) are INI too.
namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// A read-only INI file flattened to <c>section.key</c> → value, case-insensitively. Comments start
/// with <c>;</c> or <c>#</c>; a key outside any section is stored under its bare name; a repeated
/// key wins on its last occurrence; a value may contain <c>=</c> and is trimmed.
/// </summary>
public sealed class IniFile
{
    readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Values => values;
    public int Count => values.Count;

    public static IniFile Parse(string text)
    {
        var ini = new IniFile();
        string section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (line[0] == '[')
            {
                int close = line.IndexOf(']');
                section = close > 1 ? line[1..close].Trim() : "";
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            if (key.Length == 0) continue;
            ini.values[section.Length == 0 ? key : section + "." + key] = line[(eq + 1)..].Trim();
        }
        return ini;
    }

    /// <summary>Reads a file, or an empty set when it is missing or unreadable.</summary>
    public static IniFile Read(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path)) : new IniFile(); }
        catch (IOException) { return new IniFile(); }
        catch (UnauthorizedAccessException) { return new IniFile(); }
    }

    public string? this[string key] => values.TryGetValue(key, out var v) ? v : null;

    /// <summary>Every key under a section, with the section prefix removed.</summary>
    public IEnumerable<KeyValuePair<string, string>> Section(string section)
    {
        var prefix = section + ".";
        foreach (var kv in values)
            if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                yield return new(kv.Key[prefix.Length..], kv.Value);
    }

    /// <summary>
    /// A float the way the game stores one — <c>GetPrivateProfileStructA</c>, which is what its ini
    /// reader calls: the value's four bytes as hex, low byte first, then a checksum byte
    /// that is their sum. <c>0000403F7F</c> is 0.75. A value that is not that shape, or whose
    /// checksum is wrong, is refused and the fallback used — which is the Windows call's own rule.
    /// </summary>
    public static float ProfileFloat(string? value, float fallback)
    {
        if (value is not { Length: 10 }) return fallback;
        var bytes = new byte[5];
        for (int i = 0; i < 5; i++)
            if (!byte.TryParse(value.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                return fallback;
        if ((byte)(bytes[0] + bytes[1] + bytes[2] + bytes[3]) != bytes[4]) return fallback;
        return BitConverter.ToSingle(bytes, 0);
    }

    /// <summary>
    /// A whole number the way the game reads one — <c>GetPrivateProfileIntA</c>, which its ini reader
    /// calls (from the options' read): a key with no value keeps
    /// <paramref name="fallback"/>; otherwise leading blanks, an optional sign, and the decimal digits
    /// up to the first that is not one — so <c>1abc</c> is 1 and a value with no digits, <c>yes</c>
    /// or <c>on</c>, is <b>0</b>, not the fallback. (A <c>0x</c> prefix, which the Windows call may read
    /// as hex, is read here as 0: unconfirmed, and nothing the game writes has one — it writes 0 or 1.)
    /// </summary>
    public static int ProfileInt(string? value, int fallback)
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        int i = 0;
        while (i < value.Length && (value[i] == ' ' || value[i] == '\t')) i++;
        bool negative = false;
        if (i < value.Length && (value[i] == '-' || value[i] == '+')) negative = value[i++] == '-';
        long n = 0;
        while (i < value.Length && value[i] is >= '0' and <= '9' && n <= uint.MaxValue) n = n * 10 + (value[i++] - '0');
        return unchecked((int)(negative ? -n : n));
    }

    /// <summary>INI's idea of true: <c>1</c>, <c>true</c>, <c>yes</c>, <c>on</c>.</summary>
    public static bool IsTrue(string? value) =>
        value is not null && (value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase));
}
