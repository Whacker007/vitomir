// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `misc/game.ncd`, the clans — read the way the game reads it, because three things
// here turn on a clan's record rather than its name: a character's stat caps, which of
// its discipline groups are in-clan when it buys a level, and the Gangrel's Appearance loss on
// frenzy.
using System.Globalization;
using System.Text;
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// One clan (0x9c bytes in the engine). The file's own header names every column; the offsets are
/// the loader's.
/// </summary>
public sealed class Clan
{
    /// <summary>The clan's name, lower case — the key templates name it by.</summary>
    public required string Name { get; init; }
    /// <summary>Bit 0x1 is "not selectable in MP".</summary>
    public uint Flags { get; init; }
    /// <summary>The template a beckoning-like discipline summons, or empty.</summary>
    public string Beckon { get; init; } = "";
    /// <summary>The template a shape-change becomes, or empty.</summary>
    public string Shape { get; init; } = "";
    /// <summary>0x1 "all discipline groups" (Caitiff).</summary>
    public uint Advantages { get; init; }
    /// <summary>
    /// **0x1** loses Appearance on frenzy (Gangrel), **0x2** pays 1.25× for
    /// every discipline (Caitiff), **0x4** the header's "cannot increase socials" — which
    /// the advancement code applies to Appearance only (Nosferatu).
    /// </summary>
    public uint Weaknesses { get; init; }

    public const uint WeakFrenzyAppearance = 0x1, WeakDisciplineCost = 0x2, WeakSocials = 0x4;

    /// <summary>The clan's three discipline groups, by name; "none" and unknown ones are left out.</summary>
    public required string[] Groups { get; init; }

    /// <summary>
    /// A cap per stat, or <see cref="NoCap"/> for none. The loader fills the
    /// row with 99999.9 before the file overwrites it, so a clan's every stat is written — and the
    /// shipped file writes −1 in all but two: Cappadocian Appearance 25, Nosferatu Appearance 0.
    /// </summary>
    public required float[] Caps { get; init; }
    public const float NoCap = -1f;

    /// <summary>
    /// Whether a group is one of this clan's own — by name, case-insensitively. **A
    /// character with no clan has no in-clan groups**, which is the group test's first.
    /// </summary>
    public bool Owns(string group) => Groups.Any(g => g.Equals(group, StringComparison.OrdinalIgnoreCase));
}

/// <summary>`misc/game.ncd`, loaded once.</summary>
public sealed class Clans
{
    public const string Path = "misc/game.ncd";

    readonly Dictionary<string, Clan> byName = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Clan> listed = [];
    public IReadOnlyCollection<Clan> All => byName.Values;

    /// <summary>
    /// **The clan table in 1.1's own order**: the loader puts each clan it takes at the head of the list
    /// The new record links to the old head), so the table runs **backwards through the file** —
    /// Caitiff first, Brujah last of the selectable ones. What walks the table walks it so: the create screen's clan list,
    /// Caitiff its first row and chosen, as 1.1's screen shows it.
    /// </summary>
    public IReadOnlyList<Clan> Listed => listed;

    public Clans(GameFiles files)
    {
        if (files.TryRead(Path, out var data)) Read(data);
    }

    public Clans(byte[] data) => Read(data);

    /// <summary>
    /// A clan's number from its name: the fifteen ACTOR_CLAN_* names by
    /// `_strcmpi`, 1 (`brujah`) to 15 (`caitiff`); then **16, "user defined", for any other name the
    /// clan file has** — which in the shipped file is `human` — and 0 for a name it has not.
    /// `none` and `userdefined` are not special: neither is in the shipped file, so both answer 0.
    /// </summary>
    public static int IdOf(string name, bool inFile)
    {
        for (int i = 1; i <= Named; i++)
            if (string.Equals(World.Actor.Clans[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        return inFile ? UserDefined : 0;
    }

    /// <inheritdoc cref="IdOf(string, bool)"/>
    public int IdOf(string name) => IdOf(name, this[name] is not null);

    /// <summary>How many clans the id lookup knows by name (brujah … caitiff).</summary>
    public const int Named = 15;

    /// <summary>ACTOR_CLAN_USERDEFINED: what any other clan in the file answers (`& 0x10`).</summary>
    public const int UserDefined = 16;

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameFiles, Clans> cache = new();

    /// <summary>The install's clans, read once per install — the engine reads them once at start-up.</summary>
    public static Clans For(GameFiles files) => cache.GetValue(files, f => new Clans(f));

    /// <summary>A clan by name, or null (the hash lookup the template tag goes through).</summary>
    public Clan? this[string name] => name.Length == 0 ? null : byName.GetValueOrDefault(name);

    /// <summary>
    /// The loader: a line is lower-cased, then `%s %s %s %s %x %x %x %s %s %s` and nineteen
    /// `%f`, and taken only when all 29 convert; the first clan of a name wins.
    /// </summary>
    void Read(byte[] data)
    {
        var text = Encoding.Latin1.GetString(data).Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var raw in text.Split('\n'))
        {
            var t = raw.Trim();
            if (t.Length == 0 || t[0] == ';' || t[0] == '#' || t.StartsWith("//")) continue;
            var c = t.ToLowerInvariant().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (c.Length < 29) continue;
            if (!Hex(c[4], out uint flags) || !Hex(c[5], out uint adv) || !Hex(c[6], out uint weak)) continue;
            var caps = new float[World.Actor.Stats];
            bool ok = true;
            for (int i = 0; i < caps.Length && ok; i++)
                ok = float.TryParse(c[10 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out caps[i]);
            if (!ok || byName.ContainsKey(c[0])) continue;
            byName[c[0]] = new Clan
            {
                Name = c[0], Flags = flags, Advantages = adv, Weaknesses = weak,
                Beckon = c[2] == "none" ? "" : c[2],
                Shape = c[3] == "none" ? "" : c[3],
                Groups = [.. c[7..10].Where(g => g != "none")],
                Caps = caps,
            };
            listed.Insert(0, byName[c[0]]);                // at the head, as 1.1's loader links it
        }
    }

    static bool Hex(string s, out uint v) =>
        uint.TryParse(s.StartsWith("0x") ? s[2..] : s, NumberStyles.HexNumber, null, out v);
}
