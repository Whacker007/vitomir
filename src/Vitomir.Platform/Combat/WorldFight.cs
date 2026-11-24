// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;

namespace Vitomir.Platform.Combat;

/// <summary>
/// A person in the world who fights (ours; digested, plain data): the one it fights (<see cref="Foe"/>), when it
/// began, where it last had its foe within <see cref="PerceptionNumbers.GiveUpMetres"/> (<see cref="LastPlace"/>: it
/// searches there if it gives up), how long its foe has been farther than that (<see cref="FarSeconds"/>), and how long before
/// a route to its foe is asked again after one found none (<see cref="RouteWait"/>; the brain steers straight meanwhile). Put on with
/// the <see cref="DuelBrain"/> that drives it (<see cref="WorldFights.Begin"/>) and taken off with it
/// (<see cref="WorldFights.End"/>); a brain on a body without this component is someone else's (the arena's) and perception
/// never re-aims it.
/// </summary>
public struct WorldFighter
{
    public EntityId Foe;
    public long Began;
    public Vector3 LastPlace;
    public float FarSeconds;
    public float RouteWait;
    /// <summary>Follow-up: how long before the brain takes over (s; <see cref="PerceptionNumbers.FightReactSeconds"/> for one struck, 0 once it has, or for one who joined).</summary>
    public float ReactLeft;
    /// <summary>Follow-up: how long since it last saw its foe (s; perception's sight test, on its looks), and where it saw them last.</summary>
    public float Unseen;
    public Vector3 SeenPlace;
}

/// <summary>Why a person in the world began to fight: it was struck, it saw a blow struck (or was told of one) and joined.</summary>
public enum FightCause : byte
{
    Struck = 0,
    Joined = 1,
}

/// <summary>Why a fight ended: its foe is down (stopped), its foe was too far too long (it searches), it was Crippled and not brave enough or holds no blade any more (it flees), or something else took it off (its own stop, a test).</summary>
public enum FightEnd : byte
{
    FoeDown = 0,
    GaveUp = 1,
    Crippled = 2,
    Disarmed = 3,
    Other = 4,
}

/// <summary>A person in the world began to fight: who, whom, when, and why.</summary>
public readonly record struct FightBegun(EntityId Who, EntityId Foe, long Tick, FightCause Cause);

/// <summary>A person's fight ended: who, whom it fought, when, why, and how long it fought (s).</summary>
public readonly record struct FightEnded(EntityId Who, EntityId Foe, long Tick, FightEnd Why, float Seconds);

/// <summary>
/// What a fight needs that is configuration, not state (a service): the duelling brain's settings for each kind of
/// observer, added to the world's <see cref="DuelBrainBook"/> the first time anyone fights — every kind's at once, in the
/// kinds' order, then the brain's defaults — so the book does not grow with every fight and a brain's index
/// (<see cref="DuelBrain.Book"/>, digested) means the same settings whichever fight came first (a saved fight's brain finds
/// its settings again in a world built from the same modules); and each character kind's reach
/// (<see cref="DuelBrains.ReachOf"/>, worked out once).
/// </summary>
public sealed class WorldFightBook
{
    int[] bookOf = [];
    int defaults = -1;
    float[] reachOf = [];

    /// <summary>The <see cref="DuelBrainBook"/> index of observer kind <paramref name="kind"/>'s settings (its <see cref="ObserverKind.Duel"/>, or the brain's defaults); the brain's defaults' for a kind out of range.</summary>
    public int BookOf(World world, int kind)
    {
        if (defaults < 0)
        {
            var config = world.Service<PerceptionConfig>();
            var book = world.Service<DuelBrainBook>();
            bookOf = new int[config.Kinds.Count];
            for (int i = 0; i < bookOf.Length; i++) bookOf[i] = book.Add(config.Kinds[i].Duel ?? new DuelBrainSettings());
            defaults = book.Add(new DuelBrainSettings());
        }
        return (uint)kind < (uint)bookOf.Length ? bookOf[kind] : defaults;
    }

    /// <summary>Body <paramref name="e"/>'s reach (m): its character kind's (<see cref="DuelBrains.ReachOf"/>), or <see cref="DuelBrains.BareReach"/> for a body not drawn.</summary>
    public float ReachOf(World world, EntityId e)
    {
        ref var anim = ref world.Store<CharacterAnimation>().Find(e);
        if (Unsafe.IsNullRef(ref anim) || !world.TryService<CharacterAssets>(out var assets) || anim.Kind < 0) return DuelBrains.BareReach;
        if (reachOf.Length <= anim.Kind)
        {
            int old = reachOf.Length;
            Array.Resize(ref reachOf, anim.Kind + 1);
            for (int i = old; i < reachOf.Length; i++) reachOf[i] = float.NaN;
        }
        if (float.IsNaN(reachOf[anim.Kind])) reachOf[anim.Kind] = DuelBrains.ReachOf(assets, anim.Kind);
        return reachOf[anim.Kind];
    }
}

/// <summary>
/// The duelling brain on a person in the world (ours; a play log: <i>the guard was struck five times and
/// never answered</i>). Perception's reactions decide who fights whom (<see cref="ReactionSystem"/>,
/// <see cref="Reaction.Fight"/>); this puts the arena's own brain on them (<see cref="DuelBrain.For"/>, as
/// <c>Session.Arena.cs</c> does: the kind's settings from its file, its reach and its foe's) and takes it off. The brain
/// drives the fighter as a player's buttons would (no shortcut to a hit); its routes stand aside
/// (<see cref="Navigation.RouteWalker.AsideErrand"/>) while it fights near its foe, and take it to its foe by a route when far.
/// <see cref="WorldFightSystem"/> takes the brain off a fighter that stopped or stopped observing.
/// </summary>
public static class WorldFights
{
    /// <summary>The cleanup's place: <c>Think/85</c>, before the reactions (90) and the brain (95).</summary>
    public const int Order = 85;

    /// <summary>
    /// Registers the <see cref="WorldFighter"/> component, the <see cref="WorldFightBook"/> service and <c>Think/85
    /// platform.world-fight</c>, and the duelling brain (<see cref="DuelBrains.Register"/>) unless the world has it. After
    /// <see cref="CombatServices.Register"/> and <see cref="PerceptionServices.Register"/>. A world without it (the arena)
    /// has no fights of perception's: its reactions never put a brain on anyone.
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        if (!b.Peek.TryService<DuelBrainBook>(out _)) DuelBrains.Register(b);
        b.Component<WorldFighter>();
        b.Event<FightBegun>().Event<FightEnded>();
        b.Service(new WorldFightBook());
        b.System(Phase.Think, Order, "platform.world-fight", new WorldFightSystem());
        return b;
    }

    /// <summary>Whether perception's fights are registered in <paramref name="world"/>.</summary>
    public static bool Registered(World world) => world.TryService<WorldFightBook>(out _);

    /// <summary>
    /// Whether <paramref name="e"/> can fight: it can attack (<see cref="Combatant"/>, and its character's set has a blow:
    /// <see cref="ActionSet.HasAttack"/> — the beggar's torch is held as a blade, but he has no swing), holds a blade
    /// (<see cref="HeldItems.Blade"/>) and is not stopped.
    /// </summary>
    public static bool CanFight(World world, EntityId e)
    {
        if (!world.Has<Combatant>(e) || world.Has<Dead>(e) || !world.TryService<CharacterAssets>(out var assets)) return false;
        ref var anim = ref world.Store<CharacterAnimation>().Find(e);
        if (Unsafe.IsNullRef(ref anim) || assets.Kind(anim.Kind).Actions is not { HasAttack: true }) return false;
        return HeldItems.Blade(world, e, out _);
    }

    /// <summary>
    /// Whether <paramref name="e"/> is out of a fight: gone, without a body, stopped (<see cref="Dead"/>), or its action channel
    /// playing a death (its stop on its way, or the stop it is getting up from).
    /// </summary>
    public static bool Down(World world, EntityId e)
    {
        if (e.IsNone || !world.Alive(e) || !world.Has<Body>(e) || world.Has<Dead>(e)) return true;
        ref var anim = ref world.Store<CharacterAnimation>().Find(e);
        return !Unsafe.IsNullRef(ref anim) && ActionRoles.IsDeath(anim.Action);
    }

    /// <summary>Whether <paramref name="e"/> fights in the world (has a <see cref="WorldFighter"/>), and whom.</summary>
    public static bool Fighting(World world, EntityId e, out EntityId foe)
    {
        foe = EntityId.None;
        if (!world.TryStore<WorldFighter>(out var s)) return false;
        ref var f = ref s.Find(e);
        if (Unsafe.IsNullRef(ref f)) return false;
        foe = f.Foe;
        return true;
    }

    /// <summary>
    /// Puts the duelling brain on <paramref name="e"/> (an observer of kind <paramref name="kind"/>) against
    /// <paramref name="foe"/>: the kind's settings, its reach and the foe's, and its <see cref="WorldFighter"/>; raises
    /// <see cref="FightBegun"/> with <paramref name="cause"/>. With <paramref name="reactSeconds"/> above nothing (one struck,
    /// the follow-up) the brain is put on only after them (<see cref="Engage"/>, from its fight step). False (and
    /// nothing done) when it already has a brain that is not perception's — an arena fighter is never re-aimed.
    /// </summary>
    public static bool Begin(World world, EntityId e, EntityId foe, int kind, FightCause cause = FightCause.Struck, float reactSeconds = 0f)
    {
        var fighters = world.Store<WorldFighter>();
        if (world.Has<DuelBrain>(e) && !fighters.Has(e)) return false;
        var place = world.Has<Body>(foe) ? world.Get<Body>(foe).Feet : Vector3.Zero;
        fighters.Set(e, new WorldFighter { Foe = foe, Began = world.Tick, LastPlace = place, SeenPlace = place, ReactLeft = MathF.Max(0f, reactSeconds) });
        if (reactSeconds <= 0f) Engage(world, e, kind);
        world.Events<FightBegun>().Publish(new FightBegun(e, foe, world.Tick, cause));
        return true;
    }

    /// <summary>
    /// Follow-up: puts the duelling brain on a fighter whose reaction has run out (<see cref="WorldFighter.ReactLeft"/>):
    /// the kind's settings, its reach and its foe's. Nothing when it has no <see cref="WorldFighter"/>.
    /// </summary>
    public static void Engage(World world, EntityId e, int kind)
    {
        if (!world.TryStore<WorldFighter>(out var fighters)) return;
        ref var f = ref fighters.Find(e);
        if (Unsafe.IsNullRef(ref f)) return;
        f.ReactLeft = 0f;
        var book = world.Service<WorldFightBook>();
        world.Set(e, DuelBrain.For(BrainKind.Duel, f.Foe, book.BookOf(world, kind), book.ReachOf(world, e), book.ReachOf(world, f.Foe)));
    }

    /// <summary>
    /// Takes the brain off <paramref name="e"/> (its <see cref="DuelBrain"/> and <see cref="WorldFighter"/>) and lets go what it
    /// held (attack, block, the aim, a quick slot), so nothing it pressed stays pressed; raises <see cref="FightEnded"/> with
    /// <paramref name="why"/>. Its routes come back with the errand
    /// its next reaction gives. Nothing for a body perception did not put a brain on.
    /// </summary>
    public static void End(World world, EntityId e, FightEnd why = FightEnd.Other)
    {
        if (!world.TryStore<WorldFighter>(out var fighters) || !fighters.Has(e)) return;
        var f = fighters.Get(e);
        world.Events<FightEnded>().Publish(new FightEnded(e, f.Foe, world.Tick, why, (world.Tick - f.Began) / (float)World.TicksPerSecond));
        fighters.Remove(e);
        if (world.Has<DuelBrain>(e)) world.Remove<DuelBrain>(e);
        ref var intent = ref world.Store<CombatIntent>().Find(e);
        if (!Unsafe.IsNullRef(ref intent))
        {
            intent.Attack = false;
            intent.Block = false;
            intent.Aims = false;
            intent.AimPitch = 0f;
        }
        if (world.TryStore<Powers.PowerIntent>(out var powers))
        {
            ref var pi = ref powers.Find(e);
            if (!Unsafe.IsNullRef(ref pi)) pi.Slot = 0;
        }
    }
}

/// <summary>
/// Takes the brain off a fighter that can fight no more by itself (<see cref="Phase.Think"/>, before the reactions):
/// one stopped (<see cref="Dead"/>: the death took its <see cref="Observer"/>, so its reactions no longer see it) or no
/// longer an observer. A stopped body's brain would stand idle anyway; this leaves nothing of the fight on it. Allocates
/// nothing.
/// </summary>
public sealed class WorldFightSystem : ISystem
{
    public void Run(World world)
    {
        var fighters = world.Store<WorldFighter>();
        if (fighters.Count == 0) return;
        var observers = world.Store<Observer>();
        var dead = world.Store<Dead>();
        // Backwards: End removes from the store being walked.
        for (int i = fighters.Count - 1; i >= 0; i--)
        {
            var e = fighters.Entities[i];
            if (dead.Has(e) || !observers.Has(e) || !world.Alive(e)) WorldFights.End(world, e);
        }
    }
}
