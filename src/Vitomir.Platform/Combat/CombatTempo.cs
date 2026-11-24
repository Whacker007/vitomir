// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>
/// How quickly a person's body fights (ours): the rate its blows swing at, the rate it recovers from a blow, and the
/// rate its block rises at, each over 1.1's own timing (1 is 1.1's). A rules module sets it from what the person has
/// learned (the enhanced module: Melee with a weapon in hand, Brawl bare-handed; <i>"Melee
/// and Brawl make a swing come and recover faster and a block rise sooner"</i>); the platform only plays it:
/// <list type="bullet">
/// <item><see cref="Swing"/>: the attack's motion plays at its rate times this, so its active window, its <c>Strike</c>
/// mark and its contact come sooner in real time (the windows are clip time, so they follow);</item>
/// <item><see cref="Recovery"/>: the busy time after the blow's <c>Strike</c> mark (1.1's <c>RELOADTIME</c> less the time
/// to the mark) is divided by this, so the next blow or the block can come sooner;</item>
/// <item><see cref="Rise"/>: the block's motion plays at its rate times this, and its fade-in over the locomotion (and the
/// cross-fade into it) runs this much faster.</item>
/// </list>
/// <b>Absent means 1</b>, and so does a rate of 0 or less (<see cref="Of"/>): a world whose people have none is the same
/// world, digest for digest, as earlier. The component is registered only by a module that sets it
/// (<see cref="CombatTempos.Register"/>), since a registered store enters the world's digest even when empty. Plain data,
/// digested.
/// </summary>
public struct CombatTempo
{
    public float Swing;
    public float Recovery;
    public float Rise;

    /// <summary>1.1's own timing.</summary>
    public static CombatTempo Normal => new() { Swing = 1f, Recovery = 1f, Rise = 1f };

    public CombatTempo(float swing, float recovery, float rise)
    {
        Swing = swing;
        Recovery = recovery;
        Rise = rise;
    }

    /// <summary>The swing rate, 1 when unset.</summary>
    public readonly float SwingRate => Of(Swing);

    /// <summary>The recovery rate, 1 when unset.</summary>
    public readonly float RecoveryRate => Of(Recovery);

    /// <summary>The block's rise rate, 1 when unset.</summary>
    public readonly float RiseRate => Of(Rise);

    /// <summary>A rate as played: itself when above nothing, else 1 (an unset component's zeros are 1.1's timing).</summary>
    public static float Of(float rate) => rate > 0f ? rate : 1f;

    /// <summary>
    /// The busy time of a blow begun at this tempo (real seconds): the part of <paramref name="motion"/>'s busy time before
    /// its <c>Strike</c> mark (the mark's clip seconds over the motion's rate) divided by the swing rate, the rest by the
    /// recovery rate. A motion with no mark is all swing. At 1.1's timing it is the motion's busy time exactly.
    /// </summary>
    public readonly float BusyFor(in ActionMotion motion)
    {
        float swing = SwingRate, recovery = RecoveryRate, busy = motion.BusySeconds;
        if (swing == 1f && recovery == 1f) return busy;
        float before = motion.StrikeSeconds >= 0f && motion.Rate > 0f ? MathF.Min(busy, motion.StrikeSeconds / motion.Rate) : busy;
        return before / swing + (busy - before) / recovery;
    }
}

/// <summary>The tempo's registration and look-up.</summary>
public static class CombatTempos
{
    /// <summary>Registers the <see cref="CombatTempo"/> component unless it is already (a module that sets it calls this).</summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        if (!Registered(b.Peek)) b.Component<CombatTempo>();
        return b;
    }

    /// <summary>Whether the world has the component (a world without it plays everyone at 1.1's timing).</summary>
    public static bool Registered(World world) => world.TryStore<CombatTempo>(out _);   // no exception thrown and caught each tick (the world work found ~400 bytes a tick)

    /// <summary>A person's tempo from the store (null: not registered), <see cref="CombatTempo.Normal"/> when it has none.</summary>
    public static CombatTempo Of(Store<CombatTempo>? store, EntityId e)
    {
        if (store is null) return CombatTempo.Normal;
        ref var t = ref store.Find(e);
        return Unsafe.IsNullRef(ref t) ? CombatTempo.Normal : t;
    }
}

/// <summary>
/// The program's override of a rules module's <b>global combat tempo</b> — the one number a module may multiply
/// into every fighter's <see cref="CombatTempo.Swing"/> and <see cref="CombatTempo.Recovery"/> (the enhanced module's
/// <c>Data/combat/pace.json</c>, <c>tempo.global</c>: humans moving at human speeds). Given by
/// <c>--combat-tempo &lt;x&gt;</c> or an arena spec's <c>combat_tempo</c> and carried in a recording's head
/// (<see cref="TapeNote"/>); a world service, registered before the modules and only when given, which the module reads as it
/// registers and plays in place of its own number. Within <see cref="Least"/> to <see cref="Most"/>.
/// </summary>
public sealed record CombatTempoOverride(float Tempo)
{
    /// <summary>The least and the most tempo the program takes (1 is 1.1's own timing).</summary>
    public const float Least = 0.25f, Most = 4f;

    /// <summary>The recording's note.</summary>
    public const string TapeNote = "combatTempo";
}

/// <summary>
/// One fighter's own combat tempo, in place of the global (<see cref="CombatTempoOverride"/>, the module's
/// <c>pace.json</c> <c>tempo.global</c>) for that fighter alone — a scenario's <c>overrides.actorTempo</c>. The rules module multiplies it
/// as it multiplies the global: into the swing and the recovery, under Melee's and Celerity's rates, never the block's rise. The
/// component is registered only when a fighter has one (<see cref="ActorTempos.Register"/>, before the modules, so the module sees it as
/// it registers), so a world without it is unchanged, digest for digest. Plain data, digested.
/// </summary>
public struct ActorTempo
{
    /// <summary>The tempo, within <see cref="CombatTempoOverride.Least"/> to <see cref="CombatTempoOverride.Most"/> (1 is 1.1's own timing).</summary>
    public float Tempo;
}

/// <summary>The fighters' own tempos' registration and look-up.</summary>
public static class ActorTempos
{
    /// <summary>Registers <see cref="ActorTempo"/> unless it is already.</summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        if (!b.Peek.TryStore<ActorTempo>(out _)) b.Component<ActorTempo>();
        return b;
    }

    /// <summary>Whether the world has the component.</summary>
    public static bool Registered(World world) => world.TryStore<ActorTempo>(out _);

    /// <summary>A fighter's own tempo, or <paramref name="global"/> when it has none (or the store is null).</summary>
    public static float Of(Store<ActorTempo>? store, EntityId e, float global)
    {
        if (store is null) return global;
        ref var t = ref store.Find(e);
        return Unsafe.IsNullRef(ref t) || !(t.Tempo > 0f) ? global : t.Tempo;
    }
}
