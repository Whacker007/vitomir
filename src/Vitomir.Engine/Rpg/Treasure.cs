// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Treasure: `misc/game.ntt` and the roll that turns a class number into an item.
//
// Everything that hands out loot goes through one routine. A chest, a barrel, a corpse,
// a shop's `R` line and the two script natives all ask it for "one thing of class N", and it
// answers with a treasure record — the template to make, with its amount and effects — or with
// nothing. What the class *is* lives entirely in the data: nothing in the roll depends on who is
// looking, what chapter it is or how hard the game is, so progression is which class number a
// thing carries.
using System.Text;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// The engine's one treasure generator: a 32-bit linear congruential step,
/// <c>s = s × 0x19660d + 0x3c6ef35f</c>, and a unit value made by the float bit trick
/// — the low 23 bits dropped into the mantissa of 1.0, minus 1 — so a draw is exactly
/// <c>(s &amp; 0x7fffff) / 2²³</c>, never 1.
///
/// **One instance, shared**, as in the engine: the treasure roll, the shops' restock and (in the
/// original) the particles all step the same state, which is why no sequence in 1.1 repeats. It is
/// seeded from the wall clock when `game.ntt` loads and reseeded the same way every time a shop is
/// built (<see cref="Clock"/>); the seed is not saved. A test sets <see cref="State"/> itself.
/// </summary>
public sealed class TreasureRng
{
    public uint State { get; set; }

    public TreasureRng(uint seed) => State = seed;

    /// <summary>One step of the generator.</summary>
    public uint Step() => State = unchecked(State * 0x19660du + 0x3c6ef35fu);

    /// <summary>A step, as a value in [0, 1 − 2⁻²³].</summary>
    public float Unit() => (Step() & 0x7fffff) / 8388608f;

    /// <summary>
    /// A step, as a whole number in [<paramref name="lo"/>, <paramref name="hi"/>]:
    /// <c>lo + trunc(r × (hi − lo + 1))</c>, truncated toward zero as every such pick in this file is.
    /// </summary>
    public int Range(int lo, int hi) => lo + (int)(Unit() * (float)(hi - lo + 1));

    /// <summary>
    /// The seed both loaders take from the clock: milliseconds plus a thousand times the seconds
    /// into the hour (for `game.ntt` and for a shop), from local time.
    /// </summary>
    public static uint Clock(DateTime now) =>
        (uint)(now.Millisecond + 1000 * (now.Second + 60 * now.Minute));
}

/// <summary>A <c>GROUP:</c> line — a set of effects an alteration picks one of.</summary>
/// <param name="Effects">One slot per name on the line; a name the effect table does not know stays as a null.</param>
/// <param name="Found">How many of the slots are not null, which is what the pick is scaled by.</param>
public sealed record TreasureGroup(string Name, NedEffect?[] Effects, int Found);

/// <summary>
/// An <c>ALTER:</c> line — a random modifier: a new amount, or an effect from a group.
/// </summary>
/// <param name="Kind">1 for <c>D</c> (duration), 2 for <c>P</c> (permanent), 0 for anything else — by the first letter.</param>
public sealed record TreasureAlter(string Name, int Kind, TreasureGroup? Group, float Min, float Max,
                                   int DurationMin, int DurationMax, int AmountMin, int AmountMax)
{
    public const int Duration = 1, Permanent = 2;
}

/// <summary>
/// A <c>CATEG:</c> line — a list of templates, one of which a taken line makes. **A name may
/// appear more than once, and that is how the file weights a pick**: `baseloot` lists
/// `silvercoins` four times.
/// </summary>
/// <param name="Templates">One slot per name, resolved at load; a name that is not a template stays as a null.</param>
/// <param name="Found">How many resolved — the pick is scaled by this, not by the length.</param>
public sealed record TreasureCategory(string Name, string?[] Templates, int Found);

/// <summary>
/// A <c>CITEM:</c> line of a class: a category, its chance and up to three alterations, each
/// with its flags (<see cref="LevelRange"/>, <see cref="Negate"/>, <see cref="Inverse"/>).
/// </summary>
/// <param name="Chance">The percentage as the engine stores it: <c>(float)(atof(pct) × 0.01)</c>.</param>
public sealed record TreasureLine(TreasureCategory Category, float Chance,
                                  TreasureAlter?[] Alters, int[] AlterFlags)
{
    public const int LevelRange = 1, Negate = 2, Inverse = 4;
}

/// <summary>
/// What a roll answers (a 0xb8-byte record): the template to make and the item as it
/// will be — its amount and its effects, which are the only two things an alteration can change.
/// Everything else about the item is the template's, <see cref="ItemFlags.Identified"/> included
/// (the roll does not force it; a shop does, separately).
/// </summary>
public sealed class TreasureRecord
{
    /// <summary>The record's number, from the counter a save keeps as tag 14.</summary>
    public int Id { get; set; }
    public required string Template { get; init; }
    /// <summary>The item the record describes. Not in any world, and belonging to nobody.</summary>
    public required Item Item { get; init; }
    public override string ToString() => $"{Template} x{Item.Amount}";
}

/// <summary>
/// `misc/game.ntt`, loaded, and the roll over it.
/// </summary>
public sealed class Treasure
{
    public const string Path = "misc/game.ntt";

    /// <summary>`MAX_TREASURECLASSES` — classes are numbered 1 to 255 in a table of 256.</summary>
    public const int Classes = 256;

    /// <summary>A line's chance is its file value times this (a double, as the file's number is read).</summary>
    public const double ChancePercent = 0.01;

    readonly Dictionary<string, TreasureGroup> groups = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, TreasureAlter> alters = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, TreasureCategory> categories = new(StringComparer.OrdinalIgnoreCase);
    readonly TreasureLine[]?[] classes = new TreasureLine[]?[Classes];

    public IReadOnlyDictionary<string, TreasureGroup> Groups => groups;
    public IReadOnlyDictionary<string, TreasureAlter> Alters => alters;
    public IReadOnlyDictionary<string, TreasureCategory> Categories => categories;

    /// <summary>A class's lines in file order, or empty when the file never defined it.</summary>
    public IReadOnlyList<TreasureLine> Class(int n) => n is >= 0 and < Classes && classes[n] is { } c ? c : [];

    /// <summary>How many classes the file defined.</summary>
    public int ClassCount => classes.Count(c => c is not null);

    /// <summary>The generator every roll steps (<see cref="TreasureRng"/>).</summary>
    public TreasureRng Random { get; }

    /// <summary>The record counter (initially 1), which a save writes as tag 14.</summary>
    public int Counter { get; set; } = 1;

    /// <summary>Whether the file was there to load; a roll with no tables answers nothing.</summary>
    public bool Loaded { get; private set; }

    readonly Func<string, IReadOnlyDictionary<uint, NodTag>?> template;
    readonly Func<string, NedEffect?> effect;

    /// <param name="template">A template's flattened tags by name, or null when there is none.</param>
    /// <param name="effect">An effect definition by name, or null.</param>
    /// <param name="seed">The generator's first state; null takes the clock, as the loader does.</param>
    public Treasure(byte[]? data, Func<string, IReadOnlyDictionary<uint, NodTag>?> template,
                    Func<string, NedEffect?> effect, uint? seed = null)
    {
        this.template = template;
        this.effect = effect;
        Random = new TreasureRng(seed ?? Ai.Chance.TreasureClock(DateTime.Now));   // the clock, or a repeat run's seed
        if (data is not null) Read(data);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Vfs.GameFiles, Treasure> cache = new();

    /// <summary>
    /// The install's tables, loaded once — the engine loads them once at start-up (under
    /// a `bStartup` guard) and every place shares the one generator.
    /// </summary>
    public static Treasure For(Vfs.GameFiles files, World.TemplateSet templates, EffectDefinitions? effects) =>
        cache.GetValue(files, f => new Treasure(f.TryRead(Path, out var d) ? d : null,
                                                name => templates.Has(name) ? templates.Resolve(name) : null,
                                                name => effects?[name]));

    // --- the loader ------------------------------------------------------------------

    /// <summary>
    /// A line at a time: comments (<c>#</c>, <c>//</c>) and blank lines are skipped, a comment after
    /// data is **more tokens**, not a comment, and each keyword is taken only with exactly the
    /// number of fields it wants — a line that fails its count is dropped
    /// without a word. A name defined twice keeps its first definition, because the hash insert
    /// refuses a duplicate.
    /// </summary>
    void Read(byte[] data)
    {
        Loaded = true;
        int current = -1;
        var gathered = new List<TreasureLine>();
        foreach (var raw in Encoding.Latin1.GetString(data).Split('\n'))
        {
            // fgets into 0x400 bytes; the rest of a longer line is read and thrown away.
            var line = raw.Length > 1023 ? raw[..1023] : raw;
            int start = 0;
            while (start < line.Length && IsSpace(line[start])) start++;
            if (start >= line.Length) continue;
            char c0 = line[start];
            if (c0 is '\r' or '\n' or '#') continue;
            if (c0 == '/' && start + 1 < line.Length && line[start + 1] == '/') continue;
            var f = Tokens(line[start..]);
            if (f.Count == 0) continue;
            switch (f[0].ToLowerInvariant())
            {
                case "group:" when f.Count >= 3: Group(f); break;
                case "alter:" when f.Count == 10: Alter(f); break;
                case "categ:" when f.Count >= 3: Category(f); break;
                case "class:" when f.Count == 2:
                    // 1..255 and not already filled. Taking one files the lines gathered
                    // so far under the class before it. **A refused number files nothing**: the
                    // engine leaves the gathered lines in its buffer and ignores CITEMs until the
                    // next good CLASS, which then files that buffer into slot −1 — so the class
                    // before the bad line ends up empty. The empty class is reproduced; the write
                    // to slot −1 is memory corruption and is not. No shipped line does either.
                    int n = Atoi(f[1]);
                    bool good = n is >= 1 and < Classes && classes[n] is null;
                    if (good && current > 0) classes[current] = [.. gathered];
                    gathered.Clear();
                    current = good ? n : -1;
                    break;
                case "citem:" when f.Count == 9 && current > 0:
                    if (Line(f) is { } l) gathered.Add(l);      // no bound: class 199 has eighteen
                    break;
            }
        }
        if (current > 0) classes[current] = [.. gathered];   // the flush at the end
    }

    // A GROUP: line.
    void Group(List<string> f)
    {
        var slots = f.Skip(2).Select(n => effect(n.ToLowerInvariant())).ToArray();
        groups.TryAdd(Name(f[1]), new TreasureGroup(Name(f[1]), slots, slots.Count(s => s is not null)));
    }

    // An ALTER: line.
    void Alter(List<string> f)
    {
        string kind = f[2].ToLowerInvariant();
        alters.TryAdd(Name(f[1]), new TreasureAlter(
            Name(f[1]), kind.StartsWith('d') ? TreasureAlter.Duration : kind.StartsWith('p') ? TreasureAlter.Permanent : 0,
            f[3] == "-" ? null : groups.GetValueOrDefault(Name(f[3])),
            Atof(f[4]), Atof(f[5]), Atoi(f[6]), Atoi(f[7]), Atoi(f[8]), Atoi(f[9])));
    }

    // A CATEG: line.
    void Category(List<string> f)
    {
        var slots = f.Skip(2).Select(n => template(Name(n).ToLowerInvariant()) is null ? null : Name(n).ToLowerInvariant()).ToArray();
        categories.TryAdd(Name(f[1]), new TreasureCategory(Name(f[1]), slots, slots.Count(s => s is not null)));
    }

    TreasureLine? Line(List<string> f)
    {
        // An unknown category drops the line; an unknown alteration is simply none.
        if (!categories.TryGetValue(Name(f[1]), out var cat)) return null;
        var picks = new TreasureAlter?[3];
        var flags = new int[3];
        for (int k = 0; k < 3; k++)
        {
            var a = f[3 + 2 * k];
            picks[k] = a == "-" ? null : alters.GetValueOrDefault(Name(a));
            flags[k] = Atoi(f[4 + 2 * k]);
        }
        return new TreasureLine(cat, (float)(Atof64(f[2]) * Rules.GameRules.Current.Items.TreasureChancePercent), picks, flags);
    }

    // --- the roll ---------------------------------------------------------------------

    /// <summary>
    /// One roll of class <paramref name="cls"/>: the record to make, or null for nothing.
    ///
    /// **A new draw for every line** — not one draw walked down a ladder of cumulative numbers:
    /// line <i>i</i> is taken when its own draw is at or under its chance (a float compare,
    /// equal counting), so a later line's real odds are its chance times the misses above it, and
    /// a class whose chances do not reach 100 can give nothing. Then one template, evenly, from the
    /// line's category; then its alterations. <paramref name="force"/> — the two natives' flag —
    /// replaces every chance with 1, which still costs a draw, and so always takes the first line.
    /// </summary>
    public TreasureRecord? Roll(int cls, bool force = false)
    {
        if (!Loaded || cls is < 0 or >= Classes) return null;
        if (classes[cls] is not { Length: > 0 } lines) return null;   // an undefined class draws nothing
        TreasureLine? taken = null;
        foreach (var line in lines)
        {
            float r = Random.Unit();
            if (r <= (force ? 1f : line.Chance)) { taken = line; break; }
        }
        if (taken is null || taken.Category.Found == 0) return null;

        int idx = (int)(Random.Unit() * (float)taken.Category.Found);
        // The pick indexes the slots as they were written, nulls and all, so a category with an
        // unresolved name early in it can land on the gap. The engine then copies from a null;
        // here it is nothing. No shipped category has a gap.
        if (idx >= taken.Category.Templates.Length || taken.Category.Templates[idx] is not { } name) return null;
        var record = Build(name);
        if (record is null) return null;

        var item = record.Item;
        if ((item.Flags & ItemFlags.NoRandomMod) != 0) return record;
        for (int k = 0; k < 3; k++)
        {
            if (taken.Alters[k] is not { } a) continue;
            // An amount only for a thing that counts: charges, or cash (flags 0x81). A
            // later alteration's amount replaces an earlier one's.
            if ((item.Flags & (ItemFlags.DestroyedAtZeroCharges | ItemFlags.InstantCash)) != 0)
                item.Amount = Random.Range(a.AmountMin, a.AmountMax);
            if (a.Group is not null) AddEffect(item, a, taken.AlterFlags[k]);
        }
        return record;
    }

    /// <summary>
    /// The template to a record: null unless it is an item, a weapon or armour. Takes
    /// the next record number.
    /// </summary>
    public TreasureRecord? Build(string name)
    {
        if (template(name) is not { } tags || Item.From(name, tags) is not { } item) return null;
        return new TreasureRecord { Id = Counter++, Template = name, Item = item };
    }

    /// <summary>
    /// An alteration's effect. **Nothing shipped reaches this** — only the
    /// `cashN` alterations are on any line, and they have no group — but it is the engine's and a
    /// mod may use it: one effect from the group, skipped if the item already has one of that name;
    /// its value either a random number in [min, max] (negated, then inverted, then truncated
    /// unless the effect keeps its value) or the effect's own value at a random level; and a random
    /// duration for a <c>D</c> alteration. It goes on the **front** of the item's list.
    /// </summary>
    void AddEffect(Item item, TreasureAlter a, int flags)
    {
        var group = a.Group!;
        int pick = (int)(Random.Unit() * (float)group.Found);
        if (pick >= group.Effects.Length || group.Effects[pick] is not { } eff) return;
        if (item.Effects.Any(e => e.Name.Equals(eff.Name, StringComparison.OrdinalIgnoreCase))) return;
        int level;
        float value;
        if ((flags & TreasureLine.LevelRange) == 0)
        {
            level = -1;
            value = Random.Unit() * ((a.Max + 1f) - a.Min) + a.Min;
            if ((flags & TreasureLine.Negate) != 0) value = -value;
            if ((flags & TreasureLine.Inverse) != 0 && (value > 0.01f || value < -0.01f)) value = 1f / value;
            if (!eff.Has(EffectFlags.ShowValue)) value = MathF.Truncate(value);
        }
        else
        {
            int lo = (int)a.Min, hi = (int)a.Max;
            level = Random.Range(lo, hi);
            if ((uint)level >= NedEffect.Levels) level = NedEffect.Levels - 1;
            value = eff.At(level);
        }
        int time = a.Kind == TreasureAlter.Duration ? Random.Range(a.DurationMin, a.DurationMax) : 0;
        item.Effects = [new ItemEffect(eff.Name, level, value, time), .. item.Effects];
    }

    /// <summary>
    /// A record made real: a new thing from the template, not in any world, with the
    /// record's amount and effects in place of the template's. The caller puts it somewhere.
    /// </summary>
    public static World.WorldThing? Make(World.Location place, TreasureRecord record)
    {
        if (place.Make(record.Template) is not { Item: { } item } thing) return null;
        item.Amount = record.Item.Amount;
        item.Effects = record.Item.Effects;
        return thing;
    }

    // --- tokens ------------------------------------------------------------------------------------

    /// <summary>`isspace` in the C runtime's table: space, tab, and \n \v \f \r.</summary>
    static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    /// <summary>The tokenizer: runs of non-space, each cut at 31 characters.</summary>
    static List<string> Tokens(string line)
    {
        var list = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && IsSpace(line[i])) i++;
            int s = i;
            while (i < line.Length && !IsSpace(line[i])) i++;
            if (i > s) list.Add(line[s..Math.Min(i, s + 31)]);
        }
        return list;
    }

    static string Name(string s) => s.Length > 31 ? s[..31] : s;

    /// <summary>`atoi`: optional sign, leading digits, anything after them ignored.</summary>
    static int Atoi(string s)
    {
        int i = 0, sign = 1;
        long v = 0;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) { if (s[i] == '-') sign = -1; i++; }
        while (i < s.Length && char.IsAsciiDigit(s[i])) { v = v * 10 + (s[i] - '0'); if (v > int.MaxValue) break; i++; }
        return (int)(sign * v);
    }

    static float Atof(string s) => (float)Atof64(s);

    /// <summary>`atof`: the longest leading run that is a number, 0 when there is none.</summary>
    static double Atof64(string s)
    {
        for (int n = s.Length; n > 0; n--)
            if (double.TryParse(s.AsSpan(0, n), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
        return 0;
    }
}
