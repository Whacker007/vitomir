// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The half of the effect subsystem that changes a character: the recompute and the
/// per-frame tick. Every test here uses a **real row of
/// `misc/effects.ned`** rather than a made-up one, because the kinds and the tick rates are the
/// thing under test and inventing them would test nothing.
/// </summary>
public class EffectRuntimeTests
{
    static (Location Place, WorldThing Who, Actor Me, EffectDefinitions Defs) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var who = place.Player!.Thing;
        return (place, who, who.Actor!, new EffectDefinitions(files));
    }

    /// <summary>
    /// An instant base-stat effect is folded into the **base** row once and then thrown away. The
    /// dance is worth pinning: it counts for exactly one fold, the current row follows the base,
    /// and the instance is gone by the end of the next tick.
    /// </summary>
    [Fact]
    public void An_instant_stat_effect_lands_in_the_base_row_once()
    {
        using var files = Install.Open();
        var (_, who, me, defs) = Open(files);
        var increase = defs.File!["ef_increaseblood"]!;
        Assert.Equal(EffectKind.BaseStatInstant, increase.Kind);
        int blood = increase.Subtype;

        me.BaseStat[blood] = 5f;
        me.Stat[blood] = 5f;
        float step = increase.At(0);

        EffectRuntime.Apply(who, increase, 0, now: 1000);
        Assert.Equal(5f + step, me.BaseStat[blood], 3);
        Assert.Equal(5f + step, me.Stat[blood], 3);

        // It does not keep paying out, and it does not survive the tick.
        EffectRuntime.Tick(who, 1100, new Rng(1));
        Assert.Equal(5f + step, me.BaseStat[blood], 3);
        // That tick marks it removed; the one after reaps it (the tick reaps before it walks).
        Assert.All(who.Effects.All, e => Assert.True(e.Spent));
        EffectRuntime.Tick(who, 1200, new Rng(1));
        Assert.Empty(who.Effects.All);
    }

    /// <summary>
    /// A modified stat effect is the opposite: it never touches the base, it is folded on demand,
    /// and the current row drops back the moment it ends.
    /// </summary>
    [Fact]
    public void A_modified_stat_effect_lifts_the_current_row_and_puts_it_back()
    {
        using var files = Install.Open();
        var (_, who, me, defs) = Open(files);
        var stat = defs.File!.All.First(e => e.Kind == EffectKind.Stat && e.Subtype < Actor.Stats);
        int which = stat.Subtype;

        me.BaseStat[which] = 20f;
        me.Stat[which] = 20f;

        EffectRuntime.Apply(who, stat, 0, now: 1000, durationMs: 5000);
        Assert.Equal(20f, me.BaseStat[which], 3);
        Assert.Equal(20f + stat.At(0), me.Stat[which], 3);

        // Time runs out; the tick reaps it and the recompute puts the row back.
        EffectRuntime.Tick(who, 6001, new Rng(1));
        EffectRuntime.Recompute(who, EffectKind.Stat, which);
        Assert.Equal(20f, me.Stat[which], 3);
    }

    /// <summary>
    /// An over-time effect pays out every `tickMs` and not before, and each payout goes into the
    /// base row. `ef_increasebloodot` is the shipped example: one point a tick.
    /// </summary>
    [Fact]
    public void An_over_time_effect_pays_out_on_its_own_clock()
    {
        using var files = Install.Open();
        var (_, who, me, defs) = Open(files);
        var over = defs.File!["ef_increasebloodot"]!;
        Assert.Equal(EffectKind.BaseStatOverTime, over.Kind);
        Assert.True(over.TickMs > 0, "the shipped row has a tick rate");
        int blood = over.Subtype;
        me.BaseStat[blood] = 0f;

        EffectRuntime.Apply(who, over, 0, now: 1000, durationMs: 100_000);
        // Nothing on the frame it is applied: the engine starts the clock at now + 1.
        EffectRuntime.Tick(who, 1000, new Rng(1));
        Assert.Equal(0f, me.BaseStat[blood], 3);

        EffectRuntime.Tick(who, 1001, new Rng(1));
        Assert.Equal(over.At(0), me.BaseStat[blood], 3);

        // And not again until its rate has passed.
        EffectRuntime.Tick(who, 1001 + over.TickMs - 1, new Rng(1));
        Assert.Equal(over.At(0), me.BaseStat[blood], 3);
        EffectRuntime.Tick(who, 1001 + over.TickMs, new Rng(1));
        Assert.Equal(over.At(0) * 2f, me.BaseStat[blood], 3);
    }

    /// <summary>
    /// Healing over time is the one an item would use, and it stops at the character's maximum
    /// rather than pushing past it.
    /// </summary>
    [Fact]
    public void A_health_potion_heals_over_time_and_stops_at_the_maximum()
    {
        using var files = Install.Open();
        var (_, who, me, defs) = Open(files);
        var potion = defs.File!["ef_item_healthpotion"]!;
        Assert.Equal(EffectKind.HealOverTime, potion.Kind);

        me.MaxHealth = 100f;
        me.Health = 98f;
        EffectRuntime.Apply(who, potion, 0, now: 0, durationMs: 100_000);

        int at = 1;
        for (int i = 0; i < 20; i++, at += potion.TickMs) EffectRuntime.Tick(who, at, new Rng(1));
        Assert.Equal(100f, me.Health, 3);
    }

    /// <summary>
    /// MOVESPEED is where the limp comes from: the fold is written to the character and the
    /// `THING_AF2_LIMPING` bit is set or cleared by comparing it against **0.8** — which is the
    /// answer to where the MOVESPEED product ends up, a question this tree has carried since the
    /// gait work.
    /// </summary>
    [Fact]
    public void Move_speed_drives_the_limp_flag()
    {
        using var files = Install.Open();
        var (_, who, me, defs) = Open(files);
        // `ef_dmg_movespeed` is the shipped one that does this: [1, 1, 0.8, 0.6, 0.4, 0.1] by level,
        // so a badly wounded character drops to the threshold and limps. Level 2 is exactly 0.8,
        // and **that limps**: the recompute sets the bit when the product is at or under 0.8
        // (0.8 itself included) and clears it only above — the second wound level already
        // drags a leg.
        var slow = defs.File!["ef_dmg_movespeed"]!;
        Assert.Equal(EffectKind.MoveSpeed, slow.Kind);
        Assert.Equal(Actor.Flag2.LimpBelow, slow.At(2), 3);

        Assert.False(me.Has2(Actor.Flag2.Limping));
        var edge = EffectRuntime.Apply(who, slow, 2, now: 0, durationMs: 5000);
        Assert.True(me.Has2(Actor.Flag2.Limping));
        EffectRuntime.Remove(who, edge!);
        Assert.False(me.Has2(Actor.Flag2.Limping));

        var put = EffectRuntime.Apply(who, slow, 3, now: 0, durationMs: 5000);
        Assert.Equal(slow.At(3), me.MoveSpeedScale, 3);
        Assert.True(me.Has2(Actor.Flag2.Limping));

        who.Effects.Remove(put!);
        EffectRuntime.Recompute(who, EffectKind.MoveSpeed, slow.Subtype);
        // With nothing live the fold gives the neutral 1.0, which is above the threshold.
        Assert.Equal(1f, me.MoveSpeedScale, 3);
        Assert.False(me.Has2(Actor.Flag2.Limping));
    }

    /// <summary>The simulation runs the tick for everybody, every frame, without anyone asking.</summary>
    [Fact]
    public void The_simulation_ticks_effects_on_its_own()
    {
        using var files = Install.Open();
        var (place, who, me, defs) = Open(files);
        var over = defs.File!["ef_increasebloodot"]!;
        int blood = over.Subtype;
        me.BaseStat[blood] = 0f;

        var sim = new Simulation(place);
        EffectRuntime.Apply(who, over, 0, sim.Milliseconds, durationMs: 100_000);
        sim.Advance(3f, 1f / 30f);

        Assert.True(me.BaseStat[blood] > 0f, "the simulation never ticked the effect");
    }
}
