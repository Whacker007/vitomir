// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The fighting pace (Bodies/FightingPace.cs; the pace survey): a body whose blow is swinging or held ready
/// moves at most the cap whatever gait it asks for, and at its gait's own pace otherwise; the walk below the cap stays the walk;
/// an ended blow, the service off or a world without it change nothing; a pace scale multiplies the cap; the global combat
/// tempo's override is a plain record.
/// </summary>
public sealed class FightingPaceTests
{
    const int T = World.TicksPerSecond;

    static LevelSpace Floor() =>
        new("floor", new TestLevel().Floor(-20, -20, 20, 20, cell: 4f).Build("floor").Collision) { Places = LevelPlaces.None };

    /// <summary>A controlled human on a floor, its action channel set by <paramref name="action"/>, holding <paramref name="held"/> forward for a second: its level speed (m/s).</summary>
    static float Speed(FightingPace? pace, Action<World, EntityId>? action = null, Buttons held = Buttons.None, bool unpaced = false)
    {
        var b = new WorldBuilder(3);
        MovementServices.Register(b, Floor());
        b.Component<Combatant>().Component<CharacterAnimation>().Component<Dead>();
        if (unpaced) FightingPace.RegisterUnpaced(b);
        b.Event<BlockLowered>();
        PowerContract.Register(b);
        if (pace is not null) b.Service(pace);
        var w = b.Build();
        var p = MovementServices.SpawnControlled(w, new Vector3(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        for (int i = 0; i < 12; i++) MovementServices.Step(w, ActionFrame.Idle);
        action?.Invoke(w, p);
        for (int i = 0; i < T; i++) MovementServices.Step(w, new ActionFrame(new Vector2(0, 1), default, held, default));
        var v = w.Get<Body>(p).Velocity;
        return new Vector2(v.X, v.Y).Length();
    }

    static void Swinging(World w, EntityId p) => w.Set(p, new CharacterAnimation { Action = ActionRole.AttackQuick });

    [Fact]
    public void A_body_whose_blow_swings_or_is_held_ready_runs_and_sprints_at_no_more_than_the_fighting_pace()
    {
        var fp = new FightingPace();
        Assert.Equal(1.8f, fp.MaxSpeed);
        float run = Locomotion.Sandbox.RunSpeed, walk = Locomotion.Sandbox.WalkSpeed;
        Assert.Equal(run, Speed(fp), 0.01f);                                          // no blow: the run
        Assert.Equal(1.8f, Speed(fp, Swinging), 0.01f);                               // a blow swinging: the cap
        Assert.Equal(1.8f, Speed(fp, Swinging, Buttons.Sprint), 0.01f);               // a sprint asked for: the cap
        Assert.Equal(walk, Speed(fp, Swinging, Buttons.Walk), 0.01f);                 // a walk below the cap stays the walk
        // Held ready (the direction work's hold at its ready point): still a blow in hand.
        Assert.Equal(1.8f, Speed(fp, (w, p) => w.Set(p, new CharacterAnimation { Action = ActionRole.AttackStrong, ActionHolds = true, ActionHoldAt = 0.3f, ActionTime = 0.3f })), 0.01f);
        // A blow ended (fading out), a block, the service off or a world without it: the run.
        Assert.Equal(run, Speed(fp, (w, p) => w.Set(p, new CharacterAnimation { Action = ActionRole.AttackQuick, ActionEnding = true })), 0.01f);
        Assert.Equal(run, Speed(fp, (w, p) => w.Set(p, new CharacterAnimation { Action = ActionRole.Block })), 0.01f);
        Assert.Equal(run, Speed(fp with { On = false }, Swinging), 0.01f);
        Assert.Equal(run, Speed(null, Swinging), 0.01f);
        // Another cap.
        Assert.Equal(2.4f, Speed(fp with { MaxSpeed = 2.4f }, Swinging), 0.01f);
    }

    /// <summary>
    /// A body marked <see cref="RushUnpaced"/> (a rushing creature, with the module's <c>rush-unpaced</c>) keeps its own
    /// pace through its blow; one unmarked in the same world is held as ever; registering the mark alone changes no body's pace.
    /// </summary>
    [Fact]
    public void A_rushing_creature_marked_unpaced_keeps_its_run_through_its_blow()
    {
        var fp = new FightingPace();
        float run = Locomotion.Sandbox.RunSpeed;
        Assert.Equal(run, Speed(fp, (w, p) => { Swinging(w, p); w.Set(p, new RushUnpaced { Kind = 1 }); }, unpaced: true), 0.01f);
        Assert.Equal(1.8f, Speed(fp, Swinging, unpaced: true), 0.01f);                  // the mark registered, this body without it: held
        Assert.Equal(run, Speed(fp, unpaced: true), 0.01f);                             // no blow: the run, as ever
        Assert.False(FightingPace.Unpaced(null, EntityId.None));
    }

    [Fact]
    public void The_cap_makes_the_asked_gait_a_walk_at_the_lesser_speed_and_a_pace_scale_multiplies_it()
    {
        var fp = new FightingPace();
        var l = Locomotion.Sandbox;
        Assert.Equal(1.8f, fp.Capped(l, Gait.Run, 1f).WalkSpeed);
        Assert.Equal(1.8f, fp.Capped(l, Gait.Sprint, 1f).WalkSpeed);
        Assert.Equal(1.8f, fp.Capped(l, Gait.Supernatural, 1f).WalkSpeed);
        Assert.Equal(l.WalkSpeed, fp.Capped(l, Gait.Walk, 1f).WalkSpeed);
        Assert.Equal(l.RunSpeed, fp.Capped(l, Gait.Run, 1f).RunSpeed);                // only the walk's number is the cap's
        // Celerity's movement (a pace scale of 2 on the human: run 6 m/s) breaks the human pace in proportion: 3.6 m/s.
        var paced = BodyMotor.Paced(l, l, 2f);
        Assert.Equal(3.6f, fp.Capped(paced, Gait.Run, 2f).WalkSpeed, 0.001f);
        Assert.Equal(1.8f, fp.Capped(l, Gait.Run, 0.5f).WalkSpeed);                    // a factor below 1 is none
        Assert.True(FightingPace.InBlow(new CharacterAnimation { Action = ActionRole.AttackQuick }));
        Assert.False(FightingPace.InBlow(new CharacterAnimation { Action = ActionRole.Hurt }));
        Assert.False(FightingPace.InBlow(default));
    }

    [Fact]
    public void The_global_combat_tempo_s_override_is_the_program_s_number_within_its_range()
    {
        var o = new CombatTempoOverride(0.6f);
        Assert.Equal(0.6f, o.Tempo);
        Assert.Equal("combatTempo", CombatTempoOverride.TapeNote);
        Assert.True(CombatTempoOverride.Least < 0.6f && CombatTempoOverride.Most >= 1f);
    }
}
