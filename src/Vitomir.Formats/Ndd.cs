// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `misc/game.ndd` — the 73 disciplines, and the whole discipline economy in one table. Like
// `effects.ned` it is plain text with a comment header the original's own author wrote, and the
// header documents both the flag bits and the AI type numbers — so nothing here was inferred.
using System.Text;

namespace Vitomir.Formats;

/// <summary>
/// What the AI makes of a discipline (`game.ndd`'s `type` column, named in the file's own header).
/// The numbers are the categories the AI's chooser asks for, which is how the discipline ladder
/// and this table meet.
/// </summary>
public enum DisciplineType
{
    /// <summary>"AI cannot use, or use only in special conditions."</summary>
    None = 0,
    /// <summary>"Healing discipline (when damaged)."</summary>
    Heal = 1,
    /// <summary>"Buffing discipline (start of fight generally)."</summary>
    Buff = 2,
    /// <summary>"Summoning discipline (start of fight generally)."</summary>
    Summon = 3,
    /// <summary>"Hide, sneak, etc. (preferably at start of fight or when damaged too much)."</summary>
    Hide = 4,
    /// <summary>"Sense discipline (used by AI on losing its target)."</summary>
    Sense = 5,
    Feed = 6,
    /// <summary>"Damage discipline (in fight damage)."</summary>
    Damage = 10,
    /// <summary>"Mind/control (in fight mind control, fear, mesmerize, etc.)."</summary>
    Mind = 11,
    /// <summary>"Makes frenzy higher."</summary>
    Frenzy = 12,
}

/// <summary>How a discipline works (`game.ndd`'s `flags`, named in the file's own header).</summary>
[Flags]
public enum DisciplineFlags : uint
{
    None = 0,
    TargetNone = 0x1, TargetArea = 0x2, TargetInventory = 0x4, TargetEnemy = 0x8,
    TargetFriendly = 0x10, Offensive = 0x20, TargetDead = 0x40, TargetHead = 0x80,
    /// <summary>"Projectile, should try to avoid hitting friends in the back."</summary>
    Projectile = 0x2000,
    /// <summary>"Explosion, should try to avoid hitting friends (radius is in fData)."</summary>
    AreaOfEffect = 0x4000,
    /// <summary>"A shape change that cannot be combined with other shape changes."</summary>
    ShapeChange = 0x8000,
    /// <summary>"Should terrify/astonish mortals (peds) witnessing its use."</summary>
    Terrifying = 0x10000,
    /// <summary>"Counter to OFFENSIVE, does not notify party members of this 'attack'."</summary>
    NoPartyNotify = 0x20000,
}

/// <summary>One row of `game.ndd`.</summary>
public sealed record NddDiscipline(
    string Name, string Group, string Icon, string Script, string Sound,
    DisciplineFlags Flags, DisciplineType Type, float MinDistance, float MaxDistance,
    float Data, int CastAnimation, int Experience,
    int[] BloodCost, int[] RecastMs, (int Stat, float Value)[] Checks)
{
    /// <summary>How many levels a discipline has a cost and a recast for — the file's `c0..c4`.</summary>
    public const int Levels = 5;

    /// <summary>
    /// Blood to cast at a level. **Levels count from zero**, as they do for an effect
    /// (<see cref="NedEffect.At"/>), and the engine indexes the cost table by the level directly —
    /// **including at level −1**, a discipline known and not yet learnt, where the same arithmetic
    /// lands on the word before the table, which is <see cref="Experience"/>. The AI's
    /// chooser prices such a discipline that way, 250 to 1500 blood, which is why an
    /// NPC never reaches for one.
    /// </summary>
    public int CostAt(int level) => level == -1 ? Experience : BloodCost[Math.Clamp(level, 0, Levels - 1)];

    /// <summary>Milliseconds before it can be cast again, at a level (the recast table, indexed the same way).</summary>
    public int RecastAt(int level) => level == -1 ? BloodCost[Levels - 1] : RecastMs[Math.Clamp(level, 0, Levels - 1)];

    /// <summary>
    /// The name as the engine keeps it: **lower-cased** by the loader before it is stored
    /// (`_strlwr`), and so what `actorcast` hands a script.
    /// </summary>
    public string EngineName => Name.ToLowerInvariant();

    public bool Has(DisciplineFlags flag) => (Flags & flag) != 0;
}

/// <summary>`misc/game.ndd`, parsed. Names are matched without regard to case.</summary>
public sealed class NddFile
{
    /// <summary>The columns every row has: 28, per the header's own line format.</summary>
    public const int Columns = 28;

    /// <summary>The file's comment character. `.ndd`, `.ndg` and `.ncd` use it; `.ntt` uses `//`.</summary>
    public const char Comment = '#';

    public IReadOnlyList<NddDiscipline> All { get; }

    /// <summary>Rows the reader would not take, for a caller that wants to complain about them.</summary>
    public IReadOnlyList<string> Dropped { get; }

    readonly Dictionary<string, NddDiscipline> byName;

    NddFile(List<NddDiscipline> all, List<string> dropped)
    {
        All = all;
        Dropped = dropped;
        byName = new Dictionary<string, NddDiscipline>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in all) byName.TryAdd(d.Name, d);
    }

    /// <summary>A discipline by name, or null.</summary>
    public NddDiscipline? this[string name] => byName.GetValueOrDefault(name);

    public static NddFile Read(byte[] data, string where = "game.ndd")
    {
        var all = new List<NddDiscipline>();
        var dropped = new List<string>();
        var text = Encoding.Latin1.GetString(data).Replace("\r\n", "\n").Replace('\r', '\n');
        int line = 0;
        foreach (var raw in text.Split('\n'))
        {
            line++;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed[0] == Comment) continue;
            var c = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (c.Length != Columns)
            {
                dropped.Add($"{where}:{line}: {c.Length} columns, not {Columns}");
                continue;
            }
            // The first row of a name wins; a later one is skipped whole (the name is looked up
            // before anything is allocated).
            if (all.Any(d => d.Name.Equals(c[0], StringComparison.OrdinalIgnoreCase)))
            {
                dropped.Add($"{where}:{line}: a second {c[0]}, skipped");
                continue;
            }
            all.Add(new NddDiscipline(
                c[0], c[1], c[2], c[3], c[4],
                (DisciplineFlags)Hex(c[5]), (DisciplineType)Int(c[6]),
                Float(c[7]), Float(c[8]), Float(c[9]), Int(c[10]), Int(c[11]),
                [.. c[12..17].Select(Int)], [.. c[17..22].Select(Int)],
                Checks(c)));
        }
        return new NddFile(all, dropped);
    }

    /// <summary>
    /// The stat checks, **compacted** as the loader compacts them: a pair whose
    /// stat column begins with `-`, or whose stat is not one of the nineteen (`atoi` ≥ 19, which
    /// takes in a negative one read unsigned), is skipped and the next one moves up. Reading every
    /// pair instead would turn each `-` into "stat 0 at most 0" — Strength ≤ 0 — and refuse every
    /// player every discipline.
    /// </summary>
    static (int Stat, float Value)[] Checks(string[] c)
    {
        var kept = new List<(int, float)>(3);
        for (int i = 0; i < 3; i++)
        {
            string stat = c[22 + i * 2];
            if (stat.StartsWith('-')) continue;
            uint index = unchecked((uint)Atoi(stat));
            if (index >= 19) continue;
            kept.Add(((int)index, Float(c[23 + i * 2])));
        }
        return [.. kept];
    }

    /// <summary>C's `atoi`: leading digits, stopping at the first thing that is not one; nothing reads as 0.</summary>
    static int Atoi(string s)
    {
        int i = 0, sign = 1, v = 0;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) { if (s[i] == '-') sign = -1; i++; }
        for (; i < s.Length && char.IsAsciiDigit(s[i]); i++) v = v * 10 + (s[i] - '0');
        return sign * v;
    }

    /// <summary>A missing value is written `-` and reads as zero, which is what the shipped rows do.</summary>
    static int Int(string s) => int.TryParse(s, out int v) ? v : 0;
    static float Float(string s) => float.TryParse(s, System.Globalization.NumberStyles.Float,
                                                   System.Globalization.CultureInfo.InvariantCulture,
                                                   out float v) ? v : 0f;
    static uint Hex(string s) =>
        s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(s[2..], 16)
            : uint.TryParse(s, out uint v) ? v : 0u;
}

/// <summary>One row of `misc/game.ndg`: a discipline group — a tab in the discipline pane.</summary>
/// <param name="Name">`DGRP_…`, which is also its string-table key.</param>
/// <param name="Icon">The tab's icon.</param>
/// <param name="Experience">What learning the group's first discipline costs on top of the discipline's own (1000, 0 for `DGRP_Common`).</param>
/// <param name="Flags">The group's flags, the `%x` column.</param>
public sealed record NdgGroup(string Name, string Icon, int Experience, uint Flags);

/// <summary>
/// `misc/game.ndg`, the discipline groups, read as the game reads it: a line is
/// lower-cased, then `%s %s %u %x`, and taken only when all four convert. At most 32 are kept, in
/// file order, which is the order the discipline pane's tabs come in.
/// </summary>
public sealed class NdgFile
{
    /// <summary>The group table's size (the game asserts fewer than 32).</summary>
    public const int MaxGroups = 32;

    public IReadOnlyList<NdgGroup> All { get; }

    NdgFile(List<NdgGroup> all) => All = all;

    /// <summary>A group by name, case-insensitively, or null.</summary>
    public NdgGroup? this[string name] => All.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Where a group sits in file order, or −1.</summary>
    public int IndexOf(string name)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public static NdgFile Read(byte[] data)
    {
        var all = new List<NdgGroup>();
        var text = Encoding.Latin1.GetString(data).Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var raw in text.Split('\n'))
        {
            var t = raw.Trim();
            if (t.Length == 0 || t[0] == ';' || t[0] == '#' || t.StartsWith("//")) continue;
            var c = t.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (c.Length < 4 || !uint.TryParse(c[2], out uint xp)) continue;
            string flags = c[3].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? c[3][2..] : c[3];
            if (!uint.TryParse(flags, System.Globalization.NumberStyles.HexNumber, null, out uint f)) continue;
            if (all.Count >= MaxGroups) break;
            all.Add(new NdgGroup(c[0], c[1], (int)xp, f));
        }
        return new NdgFile(all);
    }
}
