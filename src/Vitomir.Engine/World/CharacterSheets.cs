// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A player-type thing's `.NPC` sheet, read as it is made (a comparison with 1.1's gap 1).
//
// 1.1 reads the sheet in the player's template-tag handler, case 0x3b (`ACTOR_RPGSTATS`), as
// the template is applied to **any** `THING_TYPE_PLAYER` thing (type 7) — not only the player it starts
// the game with. So Wilhem, met at the University, is made with `WilhemDa.NPC`'s stats and bounds, its
// health, its lifetime experience, its disciplines and its kit, and the kit's items take numbers before
// his own. Earlier, Vitomir read a sheet only for the starting player (`Program.cs`), a network player
// (`Net.Outfit`) and a 1.1 import, and a fresh Wilhem had 20 in everything, a 10,000 ceiling and an
// empty pack. Every character with a sheet is met this way: Wilhem, Serena, Samuel, Erik, Lily, Pink.
//
// The making is split in two here, as Vitomir builds a place: the kit's numbers are **held** while the
// scene is numbered (`Hold`, from `Location.NumberThings`, the chapter swap and a thing placed while
// the game runs), and the sheet is **given** once the place has the effect and discipline tables
// (`Give`, from `Location.Awaken` — or at once, for a place already awake).
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;
using System.Runtime.CompilerServices;

namespace Vitomir.Engine.World;

public static class CharacterSheets
{
    /// <summary>The name the reset writes before the sheet's own (`s_Fumpfblumen_0051a718`).</summary>
    public const string ResetName = "Fumpfblumen";

    /// <summary>Every stat's maximum after the reset (100)…</summary>
    public const float ResetMax = 100f;

    /// <summary>…but the two pools', blood's and mana's (stats 16 and 18: `0x447a0000`, 1,000).</summary>
    public const float ResetPoolMax = 1000f;

    /// <summary>
    /// The current values the reset writes (the current stat set with each stat): 20 (`0x41a00000`) but generation
    /// 13 (`0x41500000`), blood rating 1 (`0x3f800000`), and 100 (`0x42c80000`) for blood, both pools and
    /// mana — in 1.1's order, the pools before what they hold.
    /// </summary>
    static readonly (ActorStat Stat, float Value)[] ResetValues =
    [
        (ActorStat.Strength, 20f), (ActorStat.Dexterity, 20f), (ActorStat.Stamina, 20f),
        (ActorStat.Perception, 20f), (ActorStat.Intelligence, 20f), (ActorStat.Wits, 20f),
        (ActorStat.Humanity, 20f), (ActorStat.Frenzy, 20f), (ActorStat.Faith, 20f),
        (ActorStat.Generation, 13f), (ActorStat.Charisma, 20f), (ActorStat.Manipulation, 20f),
        (ActorStat.Appearance, 20f), (ActorStat.FrenzyRating, 20f), (ActorStat.BloodRating, 1f),
        (ActorStat.BloodPool, 100f), (ActorStat.Blood, 100f), (ActorStat.ManaPool, 100f), (ActorStat.Mana, 100f),
    ];

    /// <summary>The reset's values by stat number, the rules file's default (<see cref="Rules.StatRules.SheetResetValues"/>).</summary>
    public static float[] ResetDefaults()
    {
        var values = new float[Actor.Stats];
        foreach (var (stat, value) in ResetValues) values[(int)stat] = value;
        return values;
    }

    /// <summary>
    /// The order the recompute takes the nineteen stats from base and effects in, which the read ends
    /// with (the tag handler, after the file).
    /// </summary>
    static readonly int[] RecomputeOrder = [13, 6, 16, 15, 11, 1, 10, 8, 14, 9, 7, 4, 17, 18, 12, 3, 2, 0, 5];

    /// <summary>
    /// The reset before the read: the name `Fumpfblumen`; every minimum 0
    /// (which lifts a current value below it); every maximum 100 but the blood and mana
    /// pools' 1,000 (which lowers one above it); and the current values of
    /// <see cref="ResetValues"/>. The template's 20s and a 10,000 ceiling are what the sheet is read
    /// over otherwise; every shipped sheet names every stat but mana and the mana pool, so those two
    /// bounds are what it changes. **Ours**: the current writes are clamped and stored
    /// (<see cref="Actor.ClampStat"/>) without the rest of the current stat set — the frenzy check and the bark
    /// — as nothing is in the world to hear them.
    /// </summary>
    public static void Reset(Actor actor)
    {
        actor.Name = ResetName;
        for (int i = 0; i < Actor.Stats; i++)
        {
            actor.StatMin[i] = 0f;
            if (actor.Stat[i] < 0f) actor.Stat[i] = 0f;
        }
        for (int i = 0; i < Actor.Stats; i++)
        {
            var maxima = Rules.GameRules.Current.Stats.SheetResetMax;
            float max = i is (int)ActorStat.BloodPool or (int)ActorStat.ManaPool ? maxima[1] : maxima[0];
            actor.StatMax[i] = max;
            if (actor.Stat[i] > max) actor.Stat[i] = max;
        }
        // In 1.1's order (the pools before what they hold), each stat's value the rules file's.
        var values = Rules.GameRules.Current.Stats.SheetResetValues;
        foreach (var (stat, _) in ResetValues) actor.Stat[(int)stat] = actor.ClampStat((int)stat, values[(int)stat]);
    }

    /// <summary>
    /// The reset and then the sheet's name, clan, experience, health, stats and bounds
    /// (<see cref="Actor.Apply"/>). Answers how many stats the sheet named.
    /// </summary>
    public static int Read(Actor actor, NpcFile npc)
    {
        Reset(actor);
        return actor.Apply(npc);
    }

    /// <summary>A sheet read at a thing's making and not yet given, with the numbers its kit holds.</summary>
    sealed class Pending
    {
        public required NpcFile Sheet { get; init; }
        public required string Path { get; init; }
        public Queue<int> Items { get; } = new();
        /// <summary>The number an equipped kit weapon's blade trail was made with, or 0.</summary>
        public int Trail { get; set; }
        /// <summary>The order the things were made in, which is the order they are given in.</summary>
        public long Order { get; init; }
    }

    static readonly ConditionalWeakTable<WorldThing, Pending> pending = new();
    static readonly ConditionalWeakTable<Location, StrongBox<int>> owed = new();
    static long made;

    /// <summary>
    /// Whether 1.1 reads a thing's sheet as it is made: a `THING_TYPE_PLAYER` whose template names an
    /// `ACTOR_RPGSTATS` file the install has. The three exceptions — a save's load, a
    /// place's `.nsw` read and a player's machine (network mode 1) — are the
    /// callers': a load numbers nothing (<see cref="ThingGuids.Loading"/>), and a player's machine is
    /// never awakened, so what it holds is never given.
    /// </summary>
    static NpcFile? SheetOf(ThingType type, string rpgStats, GameFiles files, out string path)
    {
        path = "";
        if (type != ThingType.Player || rpgStats.Length == 0) return null;
        path = NpcFile.PathFor(rpgStats);
        return files.TryRead(path, out var data) ? NpcFile.Read(data, path) : null;
    }

    /// <summary>
    /// The things a sheet's kit makes, in order: each `item` line whose template exists (the sheet's
    /// `item` case finds the template — none, and the line is passed over with no number —
    /// then the thing is made, which takes one), and after an equipped weapon that hangs a
    /// blade trail, its trail (true).
    /// </summary>
    public static IEnumerable<bool> KitNumbers(NpcFile npc, TemplateSet templates)
    {
        foreach (var line in npc.Items)
        {
            if (!templates.Has(line.Template)) continue;
            yield return false;
            if (line.Where == NpcPlacement.Equip
                && Weapon.From(line.Template, templates.Resolve(line.Template)) is { } w
                && w.Attacks.Length > 0 && w.Attacks[0].Is(WeaponFlag.BladeTrail) && w.Trail.Length > 0
                && templates.Has(w.Trail))
                yield return true;
        }
    }

    /// <summary>
    /// How many numbers a sheet's kit takes, for a place whose numbers are set aside before it is
    /// read (<see cref="Location.SceneGuidCount"/>, the preloads).
    /// </summary>
    public static int KitCount(string rpgStats, GameFiles files, TemplateSet templates) =>
        rpgStats.Length > 0 && files.TryRead(NpcFile.PathFor(rpgStats), out var data)
            ? KitNumbers(NpcFile.Read(data), templates).Count() : 0;

    /// <summary>
    /// The making's first half: a player-type thing's sheet is read, and the numbers its kit's items
    /// take are taken now from <paramref name="guids"/> — before its fallback weapon's and its own, as
    /// 1.1's making takes them (in a London save Wilhem is 2,055, his fallback weapon 2,054 and his nine
    /// items 2,045..2,053 in the sheet's order). Nothing for any other thing.
    /// </summary>
    public static void Hold(WorldThing thing, GameFiles files, TemplateSet templates, ThingGuids guids)
    {
        if (Held(thing.Type, thing.Actor?.RpgStats ?? "", files, templates, guids) is { } p) pending.AddOrUpdate(thing, p);
    }

    static Pending? Held(ThingType type, string rpgStats, GameFiles files, TemplateSet templates, ThingGuids guids)
    {
        if (guids.Loading || SheetOf(type, rpgStats, files, out var path) is not { } npc) return null;
        var p = new Pending { Sheet = npc, Path = path, Order = Interlocked.Increment(ref made) };
        foreach (bool trail in KitNumbers(npc, templates))
            if (trail) p.Trail = guids.Take();
            else p.Items.Enqueue(guids.Take());
        return p;
    }

    /// <summary>
    /// <see cref="Hold"/> for a thing about to be made from <paramref name="template"/> while the game
    /// runs (<see cref="Location.Place"/>), before it takes its number: answers the hold, for
    /// <see cref="Made"/> to put on the thing once it exists.
    /// </summary>
    internal static object? HoldFor(Location place, string template)
    {
        if (moving > 0 || place.Files is not { } files || place.Templates is not { } templates || !templates.Has(template)) return null;
        var tags = templates.Resolve(template);
        var type = tags.TryGetValue(Tags.Type, out var ty) ? (ThingType)ty.AsUInt() : ThingType.None;
        string sheet = tags.TryGetValue(Tags.ActorRpgStats, out var rs) ? rs.AsString() ?? "" : "";
        return Held(type, sheet, files, templates, place.Guids);
    }

    /// <summary>
    /// A thing made while the game runs, with what <see cref="HoldFor"/> held for it: given its sheet at
    /// once when the place is awake, at <see cref="Location.Awaken"/> otherwise.
    /// </summary>
    internal static void Made(Location place, WorldThing thing, object? hold)
    {
        if (hold is not Pending p) return;
        pending.AddOrUpdate(thing, p);
        if (place.Effects is not null) Give(place, thing);
    }

    /// <summary>
    /// A thing that is **moved** rather than made: while the scope is open, a thing placed from a
    /// template reads no sheet. Vitomir moves a character to another place by building a fresh body
    /// there and handing it the old one's <see cref="Actor"/> (the Storyteller's move of one character,
    /// <c>Net.HostPlaces.Carry</c>); in 1.1 the thing itself moves and nothing is made, so
    /// no kit is numbered and no cash paid again (ours, a stand-in's guard).
    /// </summary>
    public static Moved Moving()
    {
        moving++;
        return new Moved();
    }

    /// <summary>The end of <see cref="Moving"/>.</summary>
    public readonly struct Moved : IDisposable
    {
        public void Dispose() => moving--;
    }

    [ThreadStatic] static int moving;

    /// <summary>Whether <paramref name="thing"/> has a sheet held for it and not yet given.</summary>
    public static bool Waiting(WorldThing thing) => pending.TryGetValue(thing, out _);

    /// <summary>What giving <paramref name="thing"/> its sheet said, or null when it has not been given one here.</summary>
    public static string? Given(WorldThing thing) => given.TryGetValue(thing, out var said) ? said : null;

    static readonly ConditionalWeakTable<WorldThing, string> given = new();

    /// <summary>
    /// The making's second half, for every thing of <paramref name="place"/> whose sheet is held, in
    /// the order they were made: see <see cref="Give(Location, WorldThing)"/>.
    /// </summary>
    public static void Give(Location place)
    {
        var waiting = new List<(WorldThing Thing, long Order)>();
        foreach (var t in place.Things)
            if (pending.TryGetValue(t, out var p)) waiting.Add((t, p.Order));
        foreach (var (t, _) in waiting.OrderBy(w => w.Order)) Give(place, t);
    }

    /// <summary>
    /// The sheet given: the reset and the stats
    /// (<see cref="Read"/>); the sheet's `cash` **added to the party's purse** — the purse is set
    /// to itself plus the line's figure, whoever the sheet is for (Lily, Samuel and Pink
    /// carry 50, 50 and 500); each `discipline` learnt at its level with its quick slot;
    /// each `item` made on the number held for it and put where the line says
    /// (<see cref="StartingKit"/>), an equipped weapon's trail on its own; and then every stat
    /// recomputed from base and effects and every effect folded again. Answers a
    /// line for the log, or null when nothing was held.
    /// </summary>
    public static string? Give(Location place, WorldThing thing)
    {
        if (!pending.TryGetValue(thing, out var p) || thing.Actor is not { } actor) return null;
        pending.Remove(thing);
        var npc = p.Sheet;
        int stats = Read(actor, npc);
        if (npc.Cash != 0) Pay(place, npc.Cash);
        int learnt = 0;
        if (place.Powers is { } powers)
            foreach (var line in npc.Disciplines)
            {
                if (powers[line.Name] is not { } def || DisciplineBook.Make(thing, def) is not { } inst) continue;
                DisciplineBook.SetLevel(thing, powers, line.Name, line.Level);
                if (line.Quick >= 0) inst.QuickSlot = line.Quick;
                learnt++;
            }
        int items = 0;
        if (npc.Items.Count > 0 && place.Templates is { } templates && place.Effects is { } effects)
        {
            items = StartingKit.Give(thing, npc, templates, effects, place.Milliseconds,
                                     numbers: () => p.Items.Count > 0 ? p.Items.Dequeue() : 0).Count(g => g.Placed);
            // The trail its equipped weapon hung as it was made takes the number held for it; Vitomir
            // hangs it a step later (Particles.BladeTrails), which reads it here.
            if (p.Trail != 0 && actor.Weapon is { } held && held.Attacks.Length > 0
                && held.Attacks[0].Is(WeaponFlag.BladeTrail) && held.TrailGuid == 0)
                held.TrailGuid = p.Trail;
        }
        foreach (int i in RecomputeOrder) EffectRuntime.Recompute(thing, EffectKind.Stat, i);
        Save.PostLoad.Refold(thing);
        string said = $"{npc.Name} from {p.Path}: {stats} stats, {items} items, {learnt} disciplines"
                    + (npc.Cash != 0 ? $", {npc.Cash} to the purse" : "");
        Diag.For("Vitomir.World").LogInformation("sheet: {Said}", said);
        given.AddOrUpdate(thing, said);
        return said;
    }

    /// <summary>
    /// Cash a sheet gave the purse (<see cref="Session.Gold"/>): at once when the place has its session,
    /// kept until it is given one otherwise (<see cref="Owed"/>, paid by <see cref="Paid"/>) — ours, as
    /// Vitomir wakes a place before it hands it the session.
    /// </summary>
    static void Pay(Location place, int cash)
    {
        if (place.Session is { } session) { session.Gold += cash; return; }
        owed.GetOrCreateValue(place).Value += cash;
    }

    /// <summary>The sheets' cash a place holds for a purse it has not been given yet.</summary>
    public static int Owed(Location place) => owed.TryGetValue(place, out var box) ? box.Value : 0;

    /// <summary>The place has its session: the cash its sheets gave goes into the purse.</summary>
    internal static void Paid(Location place)
    {
        if (place.Session is not { } session || !owed.TryGetValue(place, out var box) || box.Value == 0) return;
        session.Gold += box.Value;
        box.Value = 0;
    }
}
