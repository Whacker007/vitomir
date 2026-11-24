// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>
/// The side a fighter is on (ours; digested, plain data): one against several — the subject alone on its side, the
/// foes on theirs (<i>"the real danger comes from getting overwhelmed by too many foes"</i>). Put on every
/// fighter of a bout whose cast names sides (the arena's <c>"side"</c>); a world with no sides never registers it, so its digest is
/// as before. <see cref="Switches"/> counts the times <see cref="DuelSideSystem"/> gave the fighter's brain a new foe.
/// </summary>
public struct DuelSide
{
    /// <summary>The side's index (the order the cast first names it).</summary>
    public int Side;
    /// <summary>How many times the fighter's brain was given another foe (its first included when the cast's was on its own side).</summary>
    public int Switches;
}

/// <summary>
/// How a fighter with a side chooses its foe (<b>SANDBOX</b>, ours): the nearest fighter of another side who is still
/// up, chosen again when its foe goes down; and, while its own blow is not playing, a foe nearer than its own by more than
/// <see cref="SwitchMetres"/> taken instead (so two foes about as near do not make it turn back and forth every tick).
/// </summary>
public sealed record DuelSideSettings
{
    /// <summary>How much nearer another foe must be (m, feet to feet across the ground) before the brain turns to it: 1.0, SANDBOX.</summary>
    public float SwitchMetres { get; init; } = 1.0f;
}

/// <summary>The sides registered: the component, the settings and <c>Think/94 platform.duel-sides</c>.</summary>
public static class DuelSides
{
    /// <summary>The chooser's place: <c>Think/94</c>, just before the duelling brain (95), so the brain acts on the foe chosen this tick.</summary>
    public const int Order = 94;

    /// <summary>
    /// Registers <see cref="DuelSide"/> (digested), <see cref="DuelSideSettings"/> (unless given) and <c>Think/94
    /// platform.duel-sides</c>. Only for a world whose fighters have sides (the arena's cast with <c>"side"</c>): a world without
    /// it is the world earlier, digest for digest. After <see cref="DuelBrains.Register"/>.
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, DuelSideSettings? settings = null)
    {
        b.Component<DuelSide>();
        if (!b.Peek.TryService<DuelSideSettings>(out _)) b.Service(settings ?? new DuelSideSettings());
        b.System(Phase.Think, Order, "platform.duel-sides", new DuelSideSystem());
        return b;
    }

    /// <summary>Whether sides are registered in <paramref name="world"/>.</summary>
    public static bool Registered(World world) => world.TryStore<DuelSide>(out _);

    /// <summary>
    /// Whether <paramref name="e"/> is out of the fight: down (<see cref="WorldFights.Down"/>: gone, stopped, or playing a death) or
    /// its weapon hand cut off (<see cref="HeldItems.WeaponHandGone"/>: it can no longer attack; the arena ends its part, DISARMED).
    /// </summary>
    public static bool Out(World world, EntityId e) => WorldFights.Down(world, e) || HeldItems.WeaponHandGone(world, e);
}

/// <summary>
/// Chooses each sided fighter's foe (<see cref="Phase.Think"/>, before the duelling brain): for every duelling brain
/// on a fighter with a <see cref="DuelSide"/> that is not itself out — when its foe is out or on its own side, the nearest fighter
/// of another side still up; otherwise, while its own blow is not playing, a foe nearer than its own by more than
/// <see cref="DuelSideSettings.SwitchMetres"/>. A new foe gets the brain's foe, the foe's reach (<see cref="DuelBrains.ReachOf"/>),
/// and the foe's current swing as already answered (so a blow the new foe began before it was chosen draws no block or parry), the
/// parry and dodge pending let go. With no foe left the brain keeps the last (out) one, and stands as it does when its foe is down.
/// The brain answers only its own foe's blows: the others' come at it unguarded (one against several is meant to be dangerous).
/// Draws nothing; walks the stores in their order; allocates only when a new kind's reach is first worked out.
/// </summary>
public sealed class DuelSideSystem : ISystem
{
    float[] reachOf = [];

    public void Run(World world)
    {
        var sides = world.Store<DuelSide>();
        if (sides.Count == 0 || !world.TryStore<DuelBrain>(out var brains)) return;
        var settings = world.Service<DuelSideSettings>();
        var bodies = world.Store<Body>();
        var anims = world.Store<CharacterAnimation>();
        var fighters = world.Store<Combatant>();
        var ents = sides.Entities;
        var data = sides.Data;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            ref var brain = ref brains.Find(e);
            if (Unsafe.IsNullRef(ref brain) || brain.Kind != BrainKind.Duel || DuelSides.Out(world, e)) continue;
            ref var body = ref bodies.Find(e);
            if (Unsafe.IsNullRef(ref body)) continue;
            int side = data[i].Side;

            // The nearest fighter of another side who is still up, and how far the present foe is.
            EntityId nearest = EntityId.None;
            float best = float.MaxValue, current = float.MaxValue;
            bool foeValid = false;
            for (int j = 0; j < ents.Length; j++)
            {
                if (j == i || data[j].Side == side) continue;
                var other = ents[j];
                ref var ob = ref bodies.Find(other);
                if (Unsafe.IsNullRef(ref ob) || DuelSides.Out(world, other)) continue;
                float d = Across(body.Feet, ob.Feet);
                if (other == brain.Foe) { foeValid = true; current = d; }
                if (d < best) { best = d; nearest = other; }
            }
            if (nearest.IsNone || nearest == brain.Foe) continue;
            if (foeValid)
            {
                // A foe as near as its own (within the margin), or its own blow playing: it keeps the one it has.
                if (best + settings.SwitchMetres >= current) continue;
                ref var own = ref anims.Find(e);
                if (!Unsafe.IsNullRef(ref own) && FightingPace.InBlow(own)) continue;
            }
            brain.Foe = nearest;
            brain.FoeReach = ReachOf(world, nearest);
            // The foe it now has and why (the nearest still up; the one it had, how far, or down), when decisions are asked for
            if (world.TryService<BrainDecisions>(out var decisions))
                decisions.Foe(new BrainDecided(e, world.Tick, BrainChoice.Target, BrainRule.NearestFoe, nearest, best, brain.Reach, brain.FoeReach, 0,
                    BrainFoePhase.None, 0, false, false, 0f, brain.PauseLeft), BrainRule.NearestFoe, foeValid ? current : -1f);
            ref var foeC = ref fighters.Find(nearest);
            brain.AnsweredSwing = Unsafe.IsNullRef(ref foeC) ? 0 : foeC.Swing;
            brain.ParryPending = false;
            brain.DodgePending = false;
            brain.HoldSeen = 0;
            data[i].Switches++;
        }
    }

    static float Across(Vector3 a, Vector3 b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>A body's reach (m): its character kind's (<see cref="DuelBrains.ReachOf"/>, worked out once a kind), or <see cref="DuelBrains.BareReach"/> for one not drawn.</summary>
    float ReachOf(World world, EntityId e)
    {
        ref var anim = ref world.Store<CharacterAnimation>().Find(e);
        if (Unsafe.IsNullRef(ref anim) || anim.Kind < 0 || !world.TryService<CharacterAssets>(out var assets)) return DuelBrains.BareReach;
        if (reachOf.Length <= anim.Kind)
        {
            int old = reachOf.Length;
            Array.Resize(ref reachOf, anim.Kind + 1);
            for (int k = old; k < reachOf.Length; k++) reachOf[k] = float.NaN;
        }
        if (float.IsNaN(reachOf[anim.Kind])) reachOf[anim.Kind] = DuelBrains.ReachOf(assets, anim.Kind);
        return reachOf[anim.Kind];
    }
}
