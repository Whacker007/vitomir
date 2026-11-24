// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The .nai file: what a character's mind is made of.
//
// Plain text, one "key value" per line, whitespace-separated (the engine's delimiter set is
// " \r\t\n" exactly). A line whose first character is ';' or '#', or whose first two are '//', is a
// comment; so is a key the table below does not know. A key with no value after it is dropped
// whole. Keys are matched case-insensitively (_strcmpi), which is why the files write mindFlags and
// the engine writes mindflags.
//
// Every field's type, scale, clamp and default is the original game's, not a guess.
// The reader's conversions are:
//   atoi                      whole number as written
//   sscanf "%x"               hexadecimal, 0x-prefixed in every shipped file
//   atof                      float as written
//   atof, x1000, trunc        seconds on disk, milliseconds in memory, truncated toward zero
//   atof, clamped to 0..1     a fraction
// Nothing here is scaled by anything else, and nothing is validated beyond those clamps: a file
// that sets attackType to 99 gets 99.
using System.Globalization;
using System.Text;

namespace Vitomir.Formats;

/// <summary>One <c>discipline &lt;name&gt; &lt;level&gt;</c> line. The level is clamped to 0..4.</summary>
public readonly record struct NaiDiscipline(string Name, int Level);

/// <summary>
/// A parsed .nai file — the engine's aiClass, 0xc4 bytes of it. Field order here follows the
/// keyword table, not the struct.
/// </summary>
public sealed class NaiFile
{
    /// <summary>The engine's own clamp on every percentage field.</summary>
    public const float PercentMin = 0f, PercentMax = 1f;
    /// <summary>Seconds on disk become milliseconds in memory (times 1000).</summary>
    public const double RateScale = 1000.0;
    /// <summary>A discipline's level is clamped to this: a negative one becomes 0, 5 or more becomes 4.</summary>
    public const int MaxDisciplineLevel = 4;

    /// <summary>What the template called it, lower-cased and cut to 31 characters as the cache does.</summary>
    public string Name { get; private set; } = "";

    // --- general ---
    /// <summary>ATTACK_* — every shipped file says 5. Stored and read by nothing in 1.1.</summary>
    public int AttackType { get; set; }
    /// <summary>Attack flags. Every shipped file says 0x01.</summary>
    public uint AttackFlags { get; set; }
    /// <summary>How long between attacks, in milliseconds.</summary>
    public int AttackRate { get; set; } = 2000;
    /// <summary>How often the alternate attack is taken instead, 0..1. Not clamped by the engine.</summary>
    public float AttackAlt { get; set; } = 0.5f;
    /// <summary>MOVE_* — every shipped file says 4. Stored and read by nothing in 1.1.</summary>
    public int MoveType { get; set; }
    /// <summary>Move flags. Every shipped file says 0x20. Stored and read by nothing in 1.1.</summary>
    public uint MoveFlags { get; set; }
    /// <summary>Stored and read by nothing in 1.1.</summary>
    public float MoveDist { get; set; } = 100f;
    /// <summary>Milliseconds.</summary>
    public int MoveRate { get; set; } = 2000;
    public float ChaseDist { get; set; } = 200f;
    /// <summary>Milliseconds. Stored and read by nothing in 1.1.</summary>
    public int ChaseRate { get; set; } = 2000;
    public float SearchDist { get; set; } = 1000f;
    /// <summary>Milliseconds. Stored and read by nothing in 1.1: the searches wait fixed times.</summary>
    public int SearchRate { get; set; } = 1000;
    /// <summary>
    /// How far a fight may drag a **party member** from the player before it gives up and stops,
    /// scaled by the member's stance: 2.5 / 1.0 / 0.4 for attack, neutral and
    /// defense. No shipped file sets one, so everybody uses this default and the three come out as
    /// 1,250 / 500 / 200 units. It was recorded as unused until then; it is not — nothing
    /// *writes* it, which is a different thing.
    /// </summary>
    public float BreakDist { get; set; } = 500f;

    /// <summary>
    /// Which mind it starts with. This is not the mode number: the engine maps it, and
    /// a THING_TYPE_PLAYER ignores it entirely. See <c>AiClass</c> in the engine.
    /// </summary>
    public int BehaviorClass { get; set; }
    /// <summary>
    /// Which group of the scene's interest spots this character may walk to. -1, the default, means
    /// none; the engine matches it against each spot's group number.
    /// </summary>
    public int InterestNum { get; set; } = -1;
    /// <summary>How far from where it stands a wanderer will pick a spot. 0 means no limit.</summary>
    public float WanderRadius { get; set; }
    /// <summary>Milliseconds.</summary>
    public int WakeupDelay { get; set; }
    /// <summary>Which of the two enemysearch behaviours it uses (0 or 1).</summary>
    public int SearchStyle { get; set; }

    // --- thresholds, each clamped to 0..1 ---
    /// <summary>
    /// The default is 0.30 and <see cref="LowHealth"/>'s is 0.15, which is the wrong way round —
    /// the engine's own constructor has them swapped. Every file that sets one sets both.
    /// </summary>
    public float VeryLowHealth { get; set; } = 0.3f;
    public float LowHealth { get; set; } = 0.15f;
    public float LowBlood { get; set; } = 0.3f;
    public float HighDamage { get; set; } = 0.2f;

    // --- discipline use ---
    /// <summary>Milliseconds.</summary>
    public int DiscHealRate { get; set; } = 2000;
    public int DiscBuffRate { get; set; } = 20000;
    public int DiscDamageRate { get; set; } = 4000;
    public int DiscFeedRate { get; set; } = 12000;
    public float DiscBuffPercent { get; set; } = 0.75f;
    public float DiscDamagePercent { get; set; } = 0.5f;
    public float DiscFeedPercent { get; set; } = 0.5f;

    // --- item use. No shipped file sets any of these; 1.1 reads only itemHealRate and
    // itemBloodRate, and the other five are read by nothing. ---
    public int ItemHealRate { get; set; } = 2000;
    public int ItemBuffRate { get; set; } = 20000;
    public int ItemDamageRate { get; set; } = 4000;
    public int ItemBloodRate { get; set; } = 2000;
    public float ItemBuffPercent { get; set; } = 0.75f;
    public float ItemDamagePercent { get; set; } = 0.5f;
    public float ItemBloodPercent { get; set; } = 0.75f;

    /// <summary>
    /// Mind flags (`AIMIND_FLAG_*`): 0x1 no fear, 0x2 no daze, 0x4 no mesmerise, 0x8 no possession
    /// and 0x10 no Majesty, each read by that mode's way in, and 0x1000, which makes a wanderer run and no file sets. 29 files set
    /// the word, 20 of them to 0x1f.
    /// </summary>
    public uint MindFlags { get; set; }
    /// <summary>Milliseconds.</summary>
    public int FeedDuration { get; set; } = 5000;

    /// <summary>What the character is granted when its AI is attached, in file order.</summary>
    public NaiDiscipline[] Disciplines { get; private set; } = [];

    /// <summary>Keys the engine would not have recognised either, in file order, for diagnostics.</summary>
    public string[] UnknownKeys { get; private set; } = [];
    /// <summary>Known keys written with no value, which the engine drops whole.</summary>
    public string[] ValuelessKeys { get; private set; } = [];

    /// <summary>
    /// The engine's keyword table, in the order the parser's switch indexes
    /// it. Order matters: it is the only thing that ties a key to its field.
    /// </summary>
    public static readonly string[] Keys =
    [
        "attackType", "attackFlags", "attackRate", "attackAlt", "moveType", "moveFlags", "moveDist",
        "moveRate", "chaseDist", "chaseRate", "searchDist", "searchRate", "breakDist",
        "behaviorClass", "interestNum", "wanderRadius", "wakeupDelay", "searchStyle",
        "verylowhealth", "lowhealth", "lowblood", "highdamage", "discipline", "discHealRate",
        "discBuffRate", "discDamageRate", "discBuffPercent", "discDamagePercent", "itemHealRate",
        "itemBuffRate", "itemDamageRate", "itemBuffPercent", "itemDamagePercent", "itemBloodRate",
        "itemBloodPercent", "mindflags", "discFeedRate", "discFeedPercent", "feedDuration",
    ];

    /// <summary>How much of a line the engine's fgets buffer holds (0x1ff bytes, one for the NUL).</summary>
    public const int Buffer = 0x1fe;

    /// <summary>The engine's token delimiters, exactly.</summary>
    static readonly char[] Delimiters = [' ', '\r', '\t', '\n'];

    public static NaiFile Read(byte[] data, string name = "<nai>")
    {
        var text = Encoding.Latin1.GetString(data);
        var ai = new NaiFile { Name = CacheName(name) };
        var discs = new List<NaiDiscipline>();
        var unknown = new List<string>();
        var valueless = new List<string>();

        // The engine reads with fgets into a 0x1ff-byte buffer and decides the three comment forms
        // on the first two characters of whatever landed there. A line longer than the buffer comes
        // back as two records, and the second one starts mid-line, so the split is part of the
        // format: no shipped file is anywhere near that long, but the reader does it the same way.
        foreach (var raw in text.Split('\n'))
        {
            for (int at = 0; at == 0 || at < raw.Length; at += Buffer)
            {
                var line = raw.AsSpan(at, Math.Min(Buffer, raw.Length - at));
                if (line.Length > 0 && (line[0] == ';' || line[0] == '#')) continue;
                if (line.Length > 1 && line[0] == '/' && line[1] == '/') continue;

                var fields = line.ToString().Split(Delimiters, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length == 0) continue;
                int key = IndexOf(fields[0]);
                if (key < 0) { unknown.Add(fields[0]); continue; }
                if (fields.Length < 2) { valueless.Add(fields[0]); continue; }
                ai.Set(key, fields, discs);
            }
        }

        ai.Disciplines = [.. discs];
        ai.UnknownKeys = [.. unknown];
        ai.ValuelessKeys = [.. valueless];
        return ai;
    }

    /// <summary>
    /// What the template's name becomes in the engine's cache: lower case, cut to 31 characters
    /// (a 32-byte buffer ending in a zero). Two AI files
    /// whose names agree in the first 31 characters would be the same entry; none do.
    /// </summary>
    public static string CacheName(string name)
    {
        var s = name.Length > 31 ? name[..31] : name;
        return s.ToLowerInvariant();
    }

    static int IndexOf(string key)
    {
        for (int i = 0; i < Keys.Length; i++)
            if (string.Equals(Keys[i], key, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    void Set(int key, string[] fields, List<NaiDiscipline> discs)
    {
        string value = fields[1];
        switch (key)
        {
            case 0: AttackType = Int(value); break;
            case 1: AttackFlags = Hex(value) ?? AttackFlags; break;
            case 2: AttackRate = Rate(value); break;
            case 3: AttackAlt = Float(value); break;
            case 4: MoveType = Int(value); break;
            case 5: MoveFlags = Hex(value) ?? MoveFlags; break;
            case 6: MoveDist = Float(value); break;
            case 7: MoveRate = Rate(value); break;
            case 8: ChaseDist = Float(value); break;
            case 9: ChaseRate = Rate(value); break;
            case 10: SearchDist = Float(value); break;
            case 11: SearchRate = Rate(value); break;
            case 12: BreakDist = Float(value); break;
            case 13: BehaviorClass = Int(value); break;
            case 14: InterestNum = Truncate(value); break;
            case 15: WanderRadius = Float(value); break;
            case 16: WakeupDelay = Rate(value); break;
            case 17: SearchStyle = Int(value); break;
            case 18: VeryLowHealth = Percent(value); break;
            case 19: LowHealth = Percent(value); break;
            case 20: LowBlood = Percent(value); break;
            case 21: HighDamage = Percent(value); break;
            case 22: discs.Add(Discipline(fields)); break;
            case 23: DiscHealRate = Rate(value); break;
            case 24: DiscBuffRate = Rate(value); break;
            case 25: DiscDamageRate = Rate(value); break;
            case 26: DiscBuffPercent = Percent(value); break;
            case 27: DiscDamagePercent = Percent(value); break;
            case 28: ItemHealRate = Rate(value); break;
            case 29: ItemBuffRate = Rate(value); break;
            case 30: ItemDamageRate = Rate(value); break;
            case 31: ItemBuffPercent = Percent(value); break;
            case 32: ItemDamagePercent = Percent(value); break;
            case 33: ItemBloodRate = Rate(value); break;
            case 34: ItemBloodPercent = Percent(value); break;
            case 35: MindFlags = Hex(value) ?? MindFlags; break;
            case 36: DiscFeedRate = Rate(value); break;
            case 37: DiscFeedPercent = Percent(value); break;
            case 38: FeedDuration = Rate(value); break;
        }
    }

    /// <summary>
    /// A <c>discipline</c> line. The level is a second token on the same line; without one, or with
    /// a negative one, it is 0, and 5 or more becomes 4. The engine reads
    /// the name through the discipline registry and drops the line if there is no such discipline;
    /// Vitomir keeps the name as written and lets the registry decide later.
    /// </summary>
    static NaiDiscipline Discipline(string[] fields)
    {
        if (fields.Length < 3) return new NaiDiscipline(fields[1], 0);
        int level = Int(fields[2]);
        if (level < 0) level = 0;
        else if (level >= 5) level = MaxDisciplineLevel;
        return new NaiDiscipline(fields[1], level);
    }

    /// <summary>C <c>atoi</c>: leading space, an optional sign, then digits until something else.</summary>
    public static int Int(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        bool neg = i < s.Length && (s[i] == '-' || s[i] == '+') && s[i++] == '-';
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            v = v * 10 + (s[i++] - '0');
            if (v > int.MaxValue) v = int.MaxValue;      // atoi's overflow is undefined; saturate.
        }
        return (int)(neg ? -v : v);
    }

    /// <summary>C <c>atof</c>: as much of a float as the front of the string spells.</summary>
    public static float Float(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsAsciiDigit(s[i])) i++; }
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int e = i++;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            if (i < s.Length && char.IsAsciiDigit(s[i])) { while (i < s.Length && char.IsAsciiDigit(s[i])) i++; }
            else i = e;
        }
        return start == i ? 0f
            : float.TryParse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }

    /// <summary>Seconds on disk, milliseconds in memory: <c>trunc(atof(s) * 1000)</c>, toward zero.</summary>
    public static int Rate(string s) => Truncate((double)Float(s) * RateScale);

    /// <summary>A count written as a float: <c>trunc(atof(s))</c>, toward zero.</summary>
    public static int Truncate(string s) => Truncate((double)Float(s));

    static int Truncate(double d) =>
        d >= int.MaxValue ? int.MaxValue : d <= int.MinValue ? int.MinValue : (int)d;

    /// <summary>A fraction, clamped to 0..1 by the engine before it is stored.</summary>
    public static float Percent(string s)
    {
        float v = Float(s);
        return v < PercentMin ? PercentMin : v > PercentMax ? PercentMax : v;
    }

    /// <summary>
    /// MSVC's <c>sscanf(s, "%x", &amp;field)</c>: leading space, an optional 0x, then hex digits.
    /// When nothing converts, sscanf writes nothing at all, so the field keeps its default — which
    /// is why this answers null rather than zero.
    /// </summary>
    public static uint? Hex(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\v' || s[i] == '\f')) i++;
        if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X') && Uri.IsHexDigit(s[i + 2 < s.Length ? i + 2 : i]))
            i += 2;
        int start = i;
        uint v = 0;
        while (i < s.Length && Uri.IsHexDigit(s[i]))
        {
            char c = s[i++];
            v = v * 16 + (uint)(c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10);
        }
        return i == start ? null : v;
    }
}
