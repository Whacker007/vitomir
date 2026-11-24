// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The effect definitions: everything a discipline, a weapon, a poison or a wound does to a character.
//
// `misc/effects.ned` is the only `.ned` in the game and it documents itself — a 140-line comment
// header written by the original's own author (`08-14-99 YB Original definitions and coding`) gives
// the column format, all 36 effect types, the compose modes and the flags. Everything below is read
// out of that header and checked against the 187 shipped rows, every one of which has exactly
// seventeen whitespace-separated columns:
//
//   name  [icon|none]  row  [script|none]  type  subtype  compose  flags  tick  pc  dc  v1..v6
//
//   ef_disc_celerity1   celerityIcon  0  EffectDiscCelerity  17  0  6  0x200  0  0  0  1.5 2.0 ... 4.0
//   ef_wanted           none          0  none                22  0  6  0x80   0  0  0  1 2 3 4 5 6
//
// The game's reader is called once, for `effects.ned` alone — no
// other table goes through it. A line whose first character is `#` or `;`, or that starts `//`, is a
// comment; the shipped file uses `#`. A line that starts with white space is skipped whole. Two rows
// write their values with a leading plus (`+4 +8 +12`), which is why the values are parsed with
// `AllowLeadingSign` rather than as bare numbers.
//
// The header states the hit and damage formulas outright.
using System.Globalization;

namespace Vitomir.Formats;

/// <summary>
/// What an effect does. The engine's own numbering, from the file's header; three of them are
/// defined and never used by the shipped file (<see cref="BaseSoakInstant"/>,
/// <see cref="BaseSoakOverTime"/> and <see cref="ModelScale"/>).
/// </summary>
public enum EffectKind
{
    None = 0,
    /// <summary>Base stat, instant.</summary>
    BaseStatInstant = 1,
    /// <summary>Base stat, over time.</summary>
    BaseStatOverTime = 2,
    /// <summary>Modified stat.</summary>
    Stat = 3,
    /// <summary>Base soak, instant. Defined; no shipped effect uses it.</summary>
    BaseSoakInstant = 4,
    /// <summary>Base soak, over time. Defined; no shipped effect uses it.</summary>
    BaseSoakOverTime = 5,
    /// <summary>Modified soak.</summary>
    Soak = 6,
    /// <summary>Damage, instant.</summary>
    DamageInstant = 7,
    /// <summary>Damage, over time.</summary>
    DamageOverTime = 8,
    /// <summary>Heal, instant.</summary>
    HealInstant = 9,
    /// <summary>Heal, over time.</summary>
    HealOverTime = 10,
    Offense = 11,
    Defense = 12,
    Damage = 13,
    SoakBonus = 14,
    Potence = 15,
    Fortitude = 16,
    /// <summary>A multiplier on how fast the character moves. Celerity is this.</summary>
    MoveSpeed = 17,
    /// <summary>A multiplier on how fast the character attacks, and the only one wired to animation.</summary>
    AttackSpeed = 18,
    /// <summary>Adds to how hard the character is to notice.</summary>
    Hide = 19,
    /// <summary>Adds to how well the character notices others.</summary>
    Sense = 20,
    StakeProtection = 21,
    /// <summary>Marks the character as a criminal, which is what the guards' search style looks for.</summary>
    Wanted = 22,
    Mesmerized = 23,
    Frenzy = 24,
    Afraid = 25,
    Dazed = 26,
    Pet = 27,
    Possessing = 28,
    ModelAlpha = 29,
    /// <summary>Defined; no shipped effect uses it.</summary>
    ModelScale = 30,
    BloodCost = 31,
    Majesty = 32,
    Immobilize = 33,
    FeedProtection = 34,
    /// <summary>Makes the character read as being on the team the value names.</summary>
    TeamFriendly = 35,
    UserMult1 = 36, UserMult2 = 37, UserMult3 = 38, UserMult4 = 39, UserMult5 = 40,
}

/// <summary>How several effects of one type and subtype combine. Only the first one's mode is read.</summary>
public enum EffectCompose
{
    None = 0, First = 1, Last = 2, Min = 3, Max = 4, Add = 5, Multiply = 6, Average = 7,
}

/// <summary>The flags column, `%x`.</summary>
[Flags]
public enum EffectFlags : uint
{
    None = 0,
    /// <summary>Do not blink the icon.</summary>
    NoBlink = 0x1,
    /// <summary>Keep the value and show it.</summary>
    ShowValue = 0x2,
    /// <summary>
    /// Kills any effect of the same type and subtype whose value is less than or equal to this
    /// one's, and creates no instance of itself — so a negator can have no script.
    /// </summary>
    Negator = 0x4,
    /// <summary>Leave out of the rollover.</summary>
    NoRollover = 0x8,
    /// <summary>Remove when whoever created it dies.</summary>
    DiesWithCreator = 0x10,
    /// <summary>Show the value as a percentage.</summary>
    Percentage = 0x20,
    /// <summary>Synchronised only to the owner.</summary>
    OwnerOnly = 0x40,
    /// <summary>Not synchronised.</summary>
    NotSynced = 0x80,
    /// <summary>Not saved.</summary>
    NotSaved = 0x100,
    /// <summary>Can be cancelled.</summary>
    Cancellable = 0x200,
}

/// <summary>One row of `effects.ned`.</summary>
/// <param name="Name">Single word, and also the id of its localized string.</param>
/// <param name="Icon">The screen icon, or empty. 73 of the 187 have one.</param>
/// <param name="Row">Which row the icon sits on: 0 disciplines, 1 everything else.</param>
/// <param name="Script">The Codex class that gives it its look and sound, or empty. 69 have one.</param>
/// <param name="Subtype">Which stat, soak or damage type — the types that have one say which.</param>
/// <param name="TickMs">Milliseconds between ticks, for the over-time types.</param>
/// <param name="PermanentCost">What it adds to an item's price as a permanent effect.</param>
/// <param name="DurationCost">What it adds per second of duration per charge.</param>
/// <param name="Values">The value at each of the six levels.</param>
public sealed record NedEffect(string Name, string Icon, int Row, string Script, EffectKind Kind,
                               int Subtype, EffectCompose Compose, EffectFlags Flags, int TickMs,
                               float PermanentCost, float DurationCost, float[] Values)
{
    /// <summary>How many levels every effect carries a value for — the engine's EFFECT_NUMLEVELS.</summary>
    public const int Levels = 6;

    /// <summary>
    /// The value at a level. **Levels count from zero**: the game asserts
    /// `bByValue || (level &lt; EFFECT_NUMLEVELS)` against 5 and then indexes the array directly,
    /// with no adjustment. The file's own header labels the columns `v1`..`v6`, which reads as
    /// one-based and is the trap — every shipped item sets level **0** and takes the first column,
    /// and the jewellery ladder confirms it from the other end: `ringOnyx` is level 0 at 25 gold,
    /// `ringSilver` level 1 at 50, `ringGold` level 2 at 100, `ringDiamond` level 3 at 500, for
    /// +5, +10, +15 and +20 Appearance. A one-based reading makes the first two identical and the
    /// price ladder nonsense.
    /// </summary>
    public float At(int level) => Values[Math.Clamp(level, 0, Levels - 1)];

    public bool Has(EffectFlags flag) => (Flags & flag) != 0;
}

/// <summary>
/// `misc/effects.ned`, parsed the way 1.1's reader parses it. Names are
/// lowered as they are read (`_strlwr`) and matched without regard to case, as the
/// engine's own lookups are.
/// </summary>
public sealed class NedFile
{
    /// <summary>
    /// How many columns a row is read for: the reader's `sscanf` has seventeen conversions and must
    /// make all of them (seventeen fields converted). A row with fewer is dropped; what comes after the
    /// seventeenth is never looked at, so a row with more is read.
    /// </summary>
    public const int Columns = 17;

    /// <summary>The last stat a stat effect (types 1–3) may name, `ACTOR_STAT` 18 (compared unsigned, so a negative one is out of range too).</summary>
    public const uint LastStat = 18;

    /// <summary>The last damage type a soak or damage effect (types 4–8) may name (compared unsigned).</summary>
    public const uint LastDamageType = 9;

    /// <summary>The longest script name kept: the copy is `strncpy` of 32 with the 32nd byte zeroed.</summary>
    public const int ScriptLength = 31;

    public IReadOnlyList<NedEffect> All { get; }

    readonly Dictionary<string, NedEffect> byName;

    /// <summary>Rows the reader would not take, for a caller that wants to complain about them.</summary>
    public IReadOnlyList<string> Dropped { get; }

    NedFile(List<NedEffect> all, List<string> dropped)
    {
        All = all;
        Dropped = dropped;
        byName = new Dictionary<string, NedEffect>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in all) byName[e.Name] = e;
    }

    /// <summary>An effect by name, or null.</summary>
    public NedEffect? this[string name] =>
        byName.TryGetValue(name, out var e) ? e : null;

    public static NedFile Read(byte[] data, string name = "effects.ned") =>
        Read(System.Text.Encoding.Latin1.GetString(data), name);

    /// <summary>
    /// The reader, line by line:
    /// <list type="number">
    /// <item>a line whose first character is `;` or `#`, or whose first two are `//`, is a comment,
    /// and one whose first character is white space — space, tab, CR or LF, `strspn` against
    /// `" \r\t\n"` — is skipped whole, so an empty line goes and so does an indented
    /// row;</item>
    /// <item>the seventeen columns must all be read (<see cref="Columns"/>), or the line is dropped;</item>
    /// <item>the name is lowered and **a name already read drops the line** — the
    /// first line of a name wins (the table lookup before anything is kept);</item>
    /// <item>a stat effect (types 1–3) naming a stat past <see cref="LastStat"/>, or a soak or damage
    /// effect (types 4–8) naming a damage type past <see cref="LastDamageType"/>, is dropped — and
    /// being dropped it does not take its name, so a later line of the same name is read. The
    /// comparisons are unsigned, so a negative subtype is out of range too; no other type's subtype
    /// is checked;</item>
    /// <item>the script is cut to <see cref="ScriptLength"/> characters, and a row past 1 is made 0.
    /// </item>
    /// </list>
    /// No shipped line meets any of these but the comments: all 187 rows are read either way.
    ///
    /// Ours, where 1.1's `sscanf` would go on differently: a column that does not parse whole drops
    /// the line (1.1 would read the number's head and carry the rest into the next column), and
    /// the name, icon and script are not held to 1.1's 32-byte buffers. The `fgets` of 511 characters
    /// that would split a longer line is not modelled; the longest shipped line is well
    /// short of it. And the list the reader builds runs backwards (each row is put at the head),
    /// which nothing but the teardown walks, so <see cref="All"/> keeps file order.
    /// </summary>
    public static NedFile Read(string text, string name = "effects.ned")
    {
        var all = new List<NedEffect>();
        var dropped = new List<string>();
        var first = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            if (raw.Length == 0) continue;
            char c = raw[0];
            if (c is ';' or '#' || (c == '/' && raw.Length > 1 && raw[1] == '/')) continue;
            if (c is ' ' or '\t' or '\r')
            {
                var rest = raw.TrimStart();
                if (rest.Length > 0 && rest[0] is not ('#' or ';') && !rest.StartsWith("//", StringComparison.Ordinal))
                    dropped.Add($"{name}:{number}: starts with white space, which the reader skips");
                continue;
            }
            var f = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < Columns) { dropped.Add($"{name}:{number}: {f.Length} columns, not {Columns}"); continue; }
            try
            {
                var values = new float[NedEffect.Levels];
                for (int i = 0; i < values.Length; i++) values[i] = Number(f[11 + i]);
                var effect = new NedEffect(
                    Lower(f[0]),
                    Word(f[1]), Unsigned(f[2]) > 1 ? 0 : (int)Unsigned(f[2]), Script(Word(f[3])),
                    (EffectKind)Unsigned(f[4]), (int)Unsigned(f[5]),
                    (EffectCompose)Unsigned(f[6]), (EffectFlags)Hex(f[7]),
                    (int)Unsigned(f[8]), Number(f[9]), Number(f[10]), values);
                if (first.TryGetValue(effect.Name, out int earlier))
                {
                    dropped.Add($"{name}:{number}: {effect.Name} was read already, at line {earlier}; the first one stands");
                    continue;
                }
                uint subtype = Unsigned(f[5]);
                if (effect.Kind is EffectKind.BaseStatInstant or EffectKind.BaseStatOverTime or EffectKind.Stat
                    && subtype > LastStat
                    || effect.Kind is >= EffectKind.BaseSoakInstant and <= EffectKind.DamageOverTime
                    && subtype > LastDamageType)
                {
                    dropped.Add($"{name}:{number}: {effect.Name}'s subtype {subtype} is out of range for type {(int)effect.Kind}");
                    continue;
                }
                first[effect.Name] = number;
                all.Add(effect);
            }
            catch (FormatException) { dropped.Add($"{name}:{number}: a column will not parse"); }
            catch (OverflowException) { dropped.Add($"{name}:{number}: a column will not parse"); }
        }
        return new NedFile(all, dropped);
    }

    /// <summary>`_strlwr` in the C locale: A to Z and nothing else.</summary>
    static string Lower(string s) =>
        string.Create(s.Length, s, static (span, from) =>
        {
            for (int i = 0; i < from.Length; i++) span[i] = from[i] is >= 'A' and <= 'Z' ? (char)(from[i] + 32) : from[i];
        });

    /// <summary>The script name as the reader keeps it: its first <see cref="ScriptLength"/> characters.</summary>
    static string Script(string s) => s.Length > ScriptLength ? s[..ScriptLength] : s;

    /// <summary>
    /// A `%u` column: a sign is allowed, and a negative number wraps round, as the C library's does.
    /// </summary>
    static uint Unsigned(string s) =>
        unchecked((uint)long.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));

    /// <summary>`none` is how the file spells an empty column.</summary>
    static string Word(string s) => s.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : s;

    /// <summary>Two shipped rows write their values `+4 +8 +12`, so a leading sign is allowed.</summary>
    static float Number(string s) =>
        float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    static uint Hex(string s) =>
        s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : uint.Parse(s, CultureInfo.InvariantCulture);
}
