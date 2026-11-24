// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>
/// A body that gets up after its zero-Health stop (ours; digested): <b>a sandbox convenience, ENGINE_SANDBOX, not a
/// rule</b> — the rules package's torpor and incapacity are later. Given to the player in play by the session (never in
/// the arena), with the seconds it lies stopped before it gets up (<see cref="AfterSeconds"/>, the session's setting),
/// whether it is getting up now (<see cref="Rising"/>), and whether its stop was a severed neck or trunk
/// (<see cref="Severed"/>, <see cref="ActorStop.SeveredStop"/>: it stays down — a body without its head does not get up).
/// </summary>
public struct Recovers
{
    public float AfterSeconds;
    public int Rising;
    public int Severed;
}

/// <summary>A stopped body got up (<see cref="RecoverySystem"/>; ENGINE_SANDBOX): who, when, the tick it stopped, and the Health it was given back (of its maximum; 0 and 0 when no module keeps Health, or when the module refused).</summary>
public readonly record struct ActorRecovered(EntityId Who, long Tick, long StoppedTick, int Health, int HealthMax);

/// <summary>The recovery's numbers (ENGINE_SANDBOX; a service): how fast the stop is played back as the getting up.</summary>
public sealed class RecoveryRule
{
    /// <summary>The rate the stop's clip plays backwards as the body gets up (1: as fast as it fell). A stand-in until a getting-up motion (1.1's has none on the people's channel).</summary>
    public float RiseRate { get; set; } = 1f;
}

/// <summary>
/// The sandbox recovery (ours; <see cref="Phase.Resolve"/>, after the death): a body with <see cref="Recovers"/> that has
/// lain stopped (<see cref="Dead"/>) for its <see cref="Recovers.AfterSeconds"/> is given its Health back — its effective
/// maximum (<see cref="Powers.Vitals.HealthCap"/> when set: severed parts lower it; else its maximum) through the module's
/// sheets (<see cref="IPersonSheets.TrySet"/> of <c>"health"</c>) — where it lies, and gets up: its stop's clip played
/// backwards (<see cref="RecoveryRule.RiseRate"/>; a stand-in) to its first, standing frame, then faded out as any action.
/// While it gets up it is still stopped (no move, no blow, no power; brains and fighters take it as down). Standing, its
/// <see cref="Dead"/> is taken away and <see cref="ActorRecovered"/> raised. A severed neck or trunk
/// (<see cref="ActorStop.SeveredStop"/>) is not got up from. The arena never gives <see cref="Recovers"/>.
/// Allocates nothing per tick.
/// </summary>
public sealed class RecoverySystem : ISystem
{
    public void Run(World world)
    {
        var recovers = world.Store<Recovers>();
        if (recovers.Count == 0) return;
        var dead = world.Store<Dead>();
        if (dead.Count == 0) return;
        var anims = world.Store<CharacterAnimation>();
        var rule = world.Service<RecoveryRule>();
        // A stop that is a severed neck or trunk (SeveredStop) is not got up from (last tick's and this tick's stops).
        if (world.TryService<EventQueue<ActorDied>>(out var died))
        {
            Severed(recovers, died.LastTick);
            Severed(recovers, died.ThisTick);
        }
        for (int i = recovers.Count - 1; i >= 0; i--)
        {
            var e = recovers.Entities[i];
            ref var r = ref recovers.Data[i];
            ref var d = ref dead.Find(e);
            if (Unsafe.IsNullRef(ref d)) { r.Rising = 0; r.Severed = 0; continue; }
            if (r.Severed != 0) continue;
            ref var anim = ref anims.Find(e);
            bool dying = !Unsafe.IsNullRef(ref anim) && ActionRoles.IsDeath(anim.Action);
            if (r.Rising == 0)
            {
                if (r.AfterSeconds <= 0f || world.Tick - d.Tick < (long)MathF.Round(r.AfterSeconds * World.TicksPerSecond)) continue;
                r.Rising = 1;
                Restore(world, e);
                if (dying)
                {
                    anim.ActionRate = -MathF.Max(0.05f, rule.RiseRate);
                    anim.ActionEnding = false;
                    anim.ActionHolds = false;
                }
                continue;
            }
            if (dying && anim.ActionTime > 0f && !anim.ActionEnding) continue;
            // Standing: the stop's clip back at its first frame fades out, and the body is up.
            if (dying)
            {
                anim.ActionTime = 0f;
                anim.ActionRate = 0f;
                anim.ActionEnding = true;
            }
            long stopped = d.Tick;
            dead.Remove(e);
            r.Rising = 0;
            var (health, max) = Health(world, e);
            if (world.TryService<EventQueue<ActorRecovered>>(out var q)) q.Publish(new ActorRecovered(e, world.Tick, stopped, health, max));
        }
    }

    static void Severed(Store<Recovers> recovers, IReadOnlyList<ActorDied> list)
    {
        for (int k = 0; k < list.Count; k++)
        {
            if (list[k].Stop != ActorStop.SeveredStop) continue;
            ref var r = ref recovers.Find(list[k].Who);
            if (!Unsafe.IsNullRef(ref r)) r.Severed = 1;
        }
    }

    /// <summary>The Health given back: the effective maximum (<see cref="Powers.Vitals.HealthCap"/> when set, else the maximum), through the module's sheets; nothing without them.</summary>
    static void Restore(World world, EntityId e)
    {
        if (!world.TryService<IPersonSheets>(out var sheets) || !world.TryStore<Powers.Vitals>(out var vitals)) return;
        ref var v = ref vitals.Find(e);
        if (Unsafe.IsNullRef(ref v) || v.HealthMax <= 0f) return;
        float cap = v.HealthCap > 0f ? MathF.Min(v.HealthCap, v.HealthMax) : v.HealthMax;
        sheets.TrySet(e, "health", (int)MathF.Floor(cap), out _);
    }

    /// <summary>The Health the module now gives (its sheets' <c>"health"</c>) and the maximum (the vitals'); 0 and 0 without them.</summary>
    static (int Health, int Max) Health(World world, EntityId e)
    {
        if (!world.TryService<IPersonSheets>(out var sheets) || !world.TryStore<Powers.Vitals>(out var vitals)) return (0, 0);
        ref var v = ref vitals.Find(e);
        if (Unsafe.IsNullRef(ref v)) return (0, 0);
        return (sheets.TryRead(e, "health", out int h) ? h : (int)v.Health, (int)v.HealthMax);
    }
}

/// <summary>The sandbox recovery registered in one call.</summary>
public static class RecoveryServices
{
    /// <summary>After the death (<see cref="DeathServices.DeathOrder"/>) in <see cref="Phase.Resolve"/>.</summary>
    public const int Order = DeathServices.DeathOrder + 10;

    /// <summary>Registers the <see cref="Recovers"/> component, the <see cref="ActorRecovered"/> event, the <see cref="RecoveryRule"/> service and <c>Resolve/910 platform.recovery</c>. After <see cref="DeathServices.Register"/>.</summary>
    public static WorldBuilder Register(WorldBuilder b, RecoveryRule? rule = null)
    {
        b.Component<Recovers>();
        b.Event<ActorRecovered>();
        b.Service(rule ?? new RecoveryRule());
        b.System(Phase.Resolve, Order, "platform.recovery", new RecoverySystem());
        return b;
    }

    /// <summary>Makes <paramref name="e"/> get up <paramref name="afterSeconds"/> after its zero-Health stop (ENGINE_SANDBOX).</summary>
    public static void Recover(World world, EntityId e, float afterSeconds) => world.Set(e, new Recovers { AfterSeconds = afterSeconds });
}
