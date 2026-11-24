// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The disciplines a character has. `misc/game.ndd` says what each of the 73 *is* — its group, its
// blood cost per level, its recast, what the AI makes of it — and a character carries a list of the
// ones it has learnt, each with its own level and its own place (or not) in the six quick slots.
//
// The engine keeps that list on the actor, with the chosen one, and three things walk
// it: the AI's chooser, the status bar's slot refresh, and
// the save writer. Everything here is named from those three agreeing.
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **Whether a discipline may be cast at a thing** — one routine
/// that three separate open questions were waiting on: the two routes past the pointer gate
/// (`Ui.Highlight.Offers`), the window for doing something with a corpse, and
/// why you cannot feed on one.
///
/// The offsets check each other. The definition's flags word and its range — and the `.ndd`
/// column order is `flags type minDist maxDist`, so flags at `0x6c` puts `maxDist` at exactly
/// `0x78`, which is what the range test reads. Worth knowing because **the file's own header calls
/// `maxDist` "maximum distance to cast (for AI)"** and it is nothing of the sort: this test
/// enforces it for the player too.
/// </summary>
public static class DisciplineTarget
{
    /// <summary>
    /// The target test, in the engine's own order.
    ///
    /// <list type="number">
    /// <item>**Range first**, straight-line between the two things, against the discipline's
    /// `maxDist`. Nothing out of reach is a target of anything.</item>
    /// <item>An **item, weapon or armour** is a target only of a `TARGET INVENTORY` discipline, and
    /// nothing else about it is looked at.</item>
    /// <item>A **character** carrying `THING_AF_PICKUP` or `THING_AF_TALKTO` is never a target —
    /// a body you can carry and a person you can talk to are both out, before anything else.</item>
    /// <item>A **living** character: friendly needs `TARGET FRIENDLY`; otherwise it needs
    /// `TARGET ENEMY` **and** must not be the case below.</item>
    /// <item>**An `OFFENSIVE` discipline refuses a `NEUTRAL` target** — in single player only; the
    /// engine lets it through in multiplayer (on a host), which is a clause that can
    /// never fire here and is recorded rather than written. This is the same neutrality that keeps
    /// the pointer off a passer-by, doing the same job one layer down.</item>
    /// <item>A **dead** character needs `TARGET DEAD`, and then must be either
    /// `THING_TYPE_PLAYER` or still have its fade timer
    /// (<see cref="Actor.FadeAt"/>) — which is the 30-second window, not the 35, because the timer
    /// is zeroed the instant the fade *starts*. A friendly corpse then needs `TARGET FRIENDLY`;
    /// **an unfriendly one needs nothing further** — the engine returns a literal 1 rather than
    /// the `TARGET ENEMY` bit, so a `TARGET DEAD` discipline works on any enemy body whether or
    /// not it could have been cast on them alive.</item>
    /// <item>Everything else — a prop, a container, a region — is never a target.</item>
    /// </list>
    /// </summary>
    public static bool MayTarget(NddDiscipline discipline, WorldThing caster, WorldThing? target)
    {
        if (target is null) return false;
        if (System.Numerics.Vector3.Distance(caster.Position, target.Position) > discipline.MaxDistance)
            return false;

        switch (target.Type)
        {
            case ThingType.Item:
            case ThingType.Weapon:
            case ThingType.Armor:
                return discipline.Has(DisciplineFlags.TargetInventory);

            case ThingType.Actor:
            case ThingType.Player:
            {
                if (target.Actor is not { } who) return false;
                if (who.Has(Actor.Flag.Pickup) || who.Has(Actor.Flag.TalkTo)) return false;

                if (who.Has(Actor.Flag.Dead))
                {
                    if (!discipline.Has(DisciplineFlags.TargetDead)) return false;
                    if (target.Type != ThingType.Player && who.FadeAt == 0) return false;
                    return !Sight.Friendly(caster, target) || discipline.Has(DisciplineFlags.TargetFriendly);
                }

                if (Sight.Friendly(caster, target)) return discipline.Has(DisciplineFlags.TargetFriendly);
                // The multiplayer escape (a host) is the third arm of this test and
                // cannot fire in a single-player build.
                if (who.Has(Actor.Flag.Neutral) && discipline.Has(DisciplineFlags.Offensive)) return false;
                return discipline.Has(DisciplineFlags.TargetEnemy);
            }

            default:
                return false;
        }
    }
}

/// <summary>
/// One discipline a character knows — the engine's 0x34-byte instance, one per character
/// per discipline, each with **its own** script object.
///
/// | # | field |
/// |---|---|
/// | 1 | <see cref="Owner"/> |
/// | 2 | <see cref="Script"/> — an object of the row's Java class, made for this instance |
/// | 3 | <see cref="Flags"/> |
/// | 4 | <see cref="CastCount"/> |
/// | 5 | <see cref="Level"/>, −1..4 |
/// | 6 | <see cref="Active"/> |
/// | 7 | <see cref="QuickSlot"/>, −1..5 |
/// | 8 | <see cref="RecastUntil"/> |
/// | 9 | <see cref="Snapshotted"/> |
/// | 10 | <see cref="ScrollGuid"/> |
/// | 11 | <see cref="Definition"/> |
/// </summary>
public sealed class Discipline
{
    /// <summary>Its row of `game.ndd`.</summary>
    public required NddDiscipline Definition { get; init; }

    /// <summary>Who knows it. The range of every cast is measured from here.</summary>
    public WorldThing? Owner { get; set; }

    /// <summary>
    /// How far it has been advanced, **−1 to 4**. Levels count from zero, as they
    /// do everywhere else in this engine: the blood cost table is indexed by the level directly.
    /// **−1 is known and not yet learnt** — "[Cannot use yet]" in the pane, refused by the cast
    /// (the cast test's step 7), hidden outside the advancement screen, and what a tome or a level-up of
    /// the discipline before it in its group leaves behind.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Which of the six quick slots it sits in, or **−1** for none. The status
    /// bar's refresh indexes its slot controls by exactly this, and the AI's chooser
    /// weights a discipline in a slot five times over.
    /// </summary>
    public int QuickSlot { get; set; } = -1;

    /// <summary>Instance flags: <see cref="Temporary"/>, <see cref="Free"/>, <see cref="NetAdvanced"/>, <see cref="Unannounced"/>, <see cref="FromScroll"/>.</summary>
    public uint Flags { get; set; }

    /// <summary>
    /// 0x1: made for one cast and thrown away after it — a scroll's — and exempt from the
    /// stat checks.
    /// </summary>
    public const uint Temporary = 0x1;
    /// <summary>
    /// 0x2: costs nothing to cast. The chooser tests it before looking the cost up at all,
    /// so it is a waiver rather than a discount.
    /// </summary>
    public const uint Free = 0x2;
    /// <summary>0x4: advanced in a multiplayer game. Nothing in single player sets it.</summary>
    public const uint NetAdvanced = 0x4;
    /// <summary>
    /// 0x8: new and not yet announced — set when the instance is made, cleared by any
    /// level other than −1 and by the discipline pane's announcement.
    /// </summary>
    public const uint Unannounced = 0x8;
    /// <summary>0x10: cast from a scroll — the scroll is used up, and `THING_AF2_SCROLLCAST` is set around the script's call.</summary>
    public const uint FromScroll = 0x10;

    /// <summary>
    /// How many times it has been cast, counted **before** `actorcast` is sent — so the first
    /// cast of a learnt discipline reports 1. A scroll's starts at 100 and a temporary one at
    /// 10,000,000, so a script can tell those apart by the number alone.
    /// </summary>
    public int CastCount { get; set; }

    /// <summary>When it may be cast again: armed at the order, after `cancast`.</summary>
    public int RecastUntil { get; set; }

    /// <summary>`Get/SetPlayerDisciplineState`'s byte. Nothing shipped reads or writes it.</summary>
    public bool Active { get; set; }

    /// <summary>The advancement screen's snapshot mark.</summary>
    public bool Snapshotted { get; set; }

    /// <summary>The scroll this was made from, for a <see cref="FromScroll"/> instance.</summary>
    public int ScrollGuid { get; set; }

    /// <summary>Its own script object: an object of <c>Definition.Script</c>, owned by the character.</summary>
    public object? Script { get; set; }

    public string Name => Definition.Name;
    public DisciplineType Type => Definition.Type;

    /// <summary>
    /// What casting it would cost, before the <c>BLOODCOST</c> multiplier — nothing for a
    /// <see cref="Free"/> one, and at level −1 the row's experience cost (see
    /// <see cref="NddDiscipline.CostAt"/>).
    /// </summary>
    public int Cost => (Flags & Free) != 0 ? 0 : Definition.CostAt(Level);
}

/// <summary>
/// A character's disciplines: the list, and the one it has selected.
/// **Newest first**: the engine prepends every new instance, so everything that walks
/// the list — the AI's chooser and its `now % n`, the status bar's slot refresh (where of two
/// claiming one slot the one later in the list, the older, is the one shown), the save — sees the
/// most recent first. This list appended until then.
/// </summary>
public sealed class DisciplineList
{
    readonly List<Discipline> known = [];

    public IReadOnlyList<Discipline> All => known;
    public int Count => known.Count;

    /// <summary>The one the player has picked, which is what a right click would cast.</summary>
    public Discipline? Current { get; set; }

    /// <summary>
    /// Puts an instance at the **head** of the list, as 1.1 does. For a caller that has its own
    /// reasons — a test, the `--disciplines` option; the game's own way in is
    /// <see cref="DisciplineBook.SetLevel"/>, which also unlocks the next of the group.
    /// </summary>
    public Discipline Add(NddDiscipline definition, int level = 0, int quickSlot = -1, WorldThing? owner = null)
    {
        var learnt = new Discipline { Definition = definition, Level = level, QuickSlot = quickSlot, Owner = owner };
        known.Insert(0, learnt);
        return learnt;
    }

    /// <summary>Puts a made instance at the head.</summary>
    internal void Prepend(Discipline d) => known.Insert(0, d);

    /// <summary>Takes one out of the list (the advancement reset).</summary>
    internal bool Remove(Discipline d)
    {
        if (ReferenceEquals(Current, d)) Current = null;
        return known.Remove(d);
    }

    /// <summary>Replaces the whole list (the advancement reset installs its copies).</summary>
    internal void Replace(IEnumerable<Discipline> list)
    {
        known.Clear();
        known.AddRange(list);
    }

    /// <summary>The first in list order sitting in a quick slot, or null. The six slots are `sbDiscipline0`..`5`.</summary>
    public Discipline? InSlot(int slot)
    {
        foreach (var d in known) if (d.QuickSlot == slot) return d;
        return null;
    }

    /// <summary>One by name, case-insensitively, in list order, or null. Finding never creates.</summary>
    public Discipline? this[string name]
    {
        get
        {
            foreach (var d in known)
                if (d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }
    }

    /// <summary>One by its row, or null.</summary>
    public Discipline? Of(NddDiscipline definition)
    {
        foreach (var d in known) if (ReferenceEquals(d.Definition, definition) || d.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase)) return d;
        return null;
    }

    /// <summary>The six quick slots the status bar declares.</summary>
    public const int QuickSlots = 6;
}

/// <summary>
/// The operations on a character's disciplines: making one, setting a
/// level, selecting. They are written against the thing rather than the list because each needs the
/// owner — for the script object, and to tell the interface when the steered character changes.
/// </summary>
public static class DisciplineBook
{
    /// <summary>
    /// The character's instance of a discipline, made when it has none. **It finds
    /// first**: a character that already knows the discipline gets the one it has. A new one is
    /// level −1, no slot, flagged <see cref="Discipline.Unannounced"/>, with its own script object —
    /// **and when the script layer is running and cannot make that object, no instance at all**
    /// (1.1 frees it and answers 0). A <paramref name="temporary"/> one is always new, flagged
    /// so, counts from 10,000,000 and is not put on the list.
    /// </summary>
    public static Discipline? Make(WorldThing owner, NddDiscipline definition, bool temporary = false)
    {
        if (owner.Actor is not { } actor) return null;
        if (!temporary && actor.Disciplines.Of(definition) is { } had) return had;
        object? script = null;
        if (owner.Place?.ScriptObjects is { } scripts)
        {
            script = scripts.Make(definition.Script, owner);
            if (script is null) return null;
        }
        var made = new Discipline
        {
            Definition = definition, Owner = owner, Level = -1, QuickSlot = -1, Script = script,
            Flags = (temporary ? Discipline.Temporary : 0u) | Discipline.Unannounced,
            CastCount = temporary ? 10_000_000 : 0,
        };
        if (!temporary) actor.Disciplines.Prepend(made);
        return made;
    }

    /// <summary>
    /// `SetDisciplineLevel`: find the discipline or make it, set its level (held to
    /// −1..4), and — for any level but −1 — clear its "new" flag and **unlock the next discipline of
    /// its group** (the next row of `game.ndd` in the same group) at −1, with its "new" flag cleared,
    /// when the character does not already know it. That is the whole of the unlock chain: learning
    /// Feed makes Blood Healing learnable, Blood Healing Blood Strength, and so on down the group.
    /// A <paramref name="temporary"/> one is made fresh and selected instead. Null when it could not
    /// be made.
    /// </summary>
    public static Discipline? SetLevel(WorldThing owner, DisciplineDefinitions defs, string name, int level, bool temporary = false)
    {
        if (owner.Actor is not { } actor || defs[name] is not { } def) return null;
        var d = temporary ? null : actor.Disciplines.Of(def);
        d ??= Make(owner, def, temporary);
        if (d is null) return null;
        d.Level = Math.Clamp(level, -1, NddDiscipline.Levels - 1);
        if (level != -1) d.Flags &= ~Discipline.Unannounced;
        if (temporary) { Select(owner, d); return d; }
        if (level != -1 && defs.NextInGroup(def) is { } next && actor.Disciplines.Of(next) is null
            && Make(owner, next) is { } unlocked)
            unlocked.Flags &= ~Discipline.Unannounced;
        return d;
    }

    /// <summary>
    /// Selects a discipline as the one a right click casts. Already selected: nothing
    /// (false). Otherwise a temporary one being replaced is thrown away — and cleared from the
    /// pending cast if it was waiting there — and when the character is the one being steered, the
    /// discipline pane and the status bar are told (<see cref="Selected"/>).
    /// </summary>
    public static bool Select(WorldThing owner, Discipline? d)
    {
        if (owner.Actor is not { } actor) return false;
        var was = actor.Disciplines.Current;
        if (ReferenceEquals(was, d)) return false;
        if (was is not null && (was.Flags & Discipline.Temporary) != 0)
        {
            if (ReferenceEquals(actor.PendingDiscipline, was)) actor.PendingDiscipline = null;
            Discard(owner, was);
        }
        actor.Disciplines.Current = d;
        if (ReferenceEquals(owner.Place?.Steered, owner)) Selected?.Invoke(owner);
        return true;
    }

    /// <summary>The interface's ear for a selection on the steered character (the pane, then the bar).</summary>
    public static Action<WorldThing>? Selected { get; set; }

    /// <summary>
    /// The discipline in a quick slot selected, when there is one and it is not already
    /// the current one — keys 1 to 6.
    /// </summary>
    public static bool SelectSlot(WorldThing owner, int slot) =>
        owner.Actor?.Disciplines.InSlot(slot) is { } d && Select(owner, d);

    /// <summary>
    /// A discipline let go: the interface told first (<see cref="LetGo"/>),
    /// then its script freed. Every way 1.1 lets one go — the selection replacing a temporary
    /// one, the cast's fire and its cancel and a character's
    /// removal — comes through here.
    /// </summary>
    internal static void Discard(WorldThing owner, Discipline d)
    {
        LetGo?.Invoke(owner, d);
        if (d.Script is { } script) owner.Place?.ScriptObjects?.Free(script);
        d.Script = null;
    }

    /// <summary>
    /// The interface's ear for a discipline let go: the pane's rows and the bar's slots
    /// that hold it are emptied, and **when its icon is on the cursor the cursor is itself again**
    /// — so a drag carrying it drops nothing (<see cref="Ui.DisciplineDrag.LetGo"/>).
    /// </summary>
    public static event Action<WorldThing, Discipline>? LetGo;
}

/// <summary>
/// `misc/game.ndd` and `misc/game.ndg`, loaded once at start-up (`game.ndg` then
/// `game.ndd`), the way `EffectDefinitions` loads `effects.ned`.
/// </summary>
public sealed class DisciplineDefinitions
{
    public const string Path = "misc/game.ndd";
    public const string GroupsPath = "misc/game.ndg";

    public NddFile? File { get; }
    public NdgFile? Groups { get; }

    public DisciplineDefinitions(GameFiles files)
    {
        if (files.TryRead(GroupsPath, out var g)) Groups = NdgFile.Read(g);
        if (files.TryRead(Path, out var data)) File = NddFile.Read(data, Path);
    }

    public NddDiscipline? this[string name] => File?[name];

    /// <summary>Every discipline of one AI type, which is what the chooser asks for.</summary>
    public IEnumerable<NddDiscipline> OfType(DisciplineType type) =>
        File?.All.Where(d => d.Type == type) ?? [];

    /// <summary>
    /// The disciplines of a group, in `game.ndd` row order — the order the loader chains them in
    /// (appended at the tail) and the order the discipline pane lists them.
    /// </summary>
    public IEnumerable<NddDiscipline> InGroup(string group) =>
        File?.All.Where(d => d.Group.Equals(group, StringComparison.OrdinalIgnoreCase)) ?? [];

    /// <summary>The next discipline of the same group, or null for the last.</summary>
    public NddDiscipline? NextInGroup(NddDiscipline def)
    {
        bool seen = false;
        foreach (var d in InGroup(def.Group))
        {
            if (seen) return d;
            if (ReferenceEquals(d, def) || d.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase)) seen = true;
        }
        return null;
    }

    /// <summary>A group's first discipline, or null.</summary>
    public NddDiscipline? FirstInGroup(string group) => InGroup(group).FirstOrDefault();

    /// <summary>A group's row, or null.</summary>
    public NdgGroup? Group(string name) => Groups?[name];
}
