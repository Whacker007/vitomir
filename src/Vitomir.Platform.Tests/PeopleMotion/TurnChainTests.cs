// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// A person's turn as a chain that catches up (<see cref="PeopleTurn.StepChain"/>, <see cref="TurnChain"/>;
/// and the design note) and the crouch without the kneel, on the made-up person (no files needed): the head, the chest and the
/// hips each chase the view at their rates within their limits, in order, eased; a turn on the spot from 30°, sped up the further
/// behind, re-aimed, turned round when the view goes back, the legs pivoting past 75°; the turn clips' own lead taken out of the pose;
/// moving, the earlier build's legs; <c>--turn-chain-off</c> gives the earlier build's turn and <c>--crouch-kneel</c> the earlier build's kneel; a world without people (the arena's)
/// has no store of it.
/// </summary>
public sealed class TurnChainTests(ITestOutputHelper output)
{
    const float Deg = DetMath.DegToRad;
    static readonly LegTurnSettings S = new();

    /// <summary>One tick of a run: the view (the body's yaw), the legs', the chest's and the head's turns from it, and what played.</summary>
    readonly record struct Seen(float View, float Legs, float Chest, float Head, MotionRole Role, string Clip, float Time, float ChestDrawn, float HeadDrawn);

    /// <summary>
    /// The made-up person standing (or crouched) at the origin facing +Y for half a second, then its view at <paramref name="view"/>(t)
    /// degrees from there for <paramref name="seconds"/>: every tick as seen.
    /// </summary>
    static List<Seen> Run(Func<float, float> view, float seconds, LegTurnSettings? legs = null, AnimationSettings? animation = null, int crouched = 0, bool crouchKit = false,
                          Func<float, Vector3>? velocity = null)
    {
        var (assets, k, person) = PeopleDoubles.Kind(crouch: crouchKit);
        var (world, e) = Make(assets, k, Floor(), Vector3.Zero, legs: legs, animation: animation);
        for (int t = 0; t < World.TicksPerSecond / 2; t++) Step(world, e, Vector3.Zero, DetMath.HalfPi, true, crouched: crouched);
        var seen = new List<Seen>();
        var feet = Vector3.Zero;
        for (int t = 0; t < seconds * World.TicksPerSecond; t++)
        {
            float time = t * Tick;
            var v = velocity?.Invoke(time) ?? Vector3.Zero;
            feet += v * Tick;
            float yaw = DetMath.HalfPi + view(time) * Deg;
            Step(world, e, feet, yaw, true, velocity: v, crouched: crouched);
            var a = world.Get<CharacterAnimation>(e);
            var chain = world.TryStore<PersonTurn>(out var chains) && chains.Has(e) ? chains.Get(e) : default;
            TurnChain.Lines(world.Service<CharacterPoses>().Bones(world, e), person.Rig, out float chest, out float head);
            seen.Add(new Seen(yaw, world.Get<PoseRig>(e).LegYaw, chain.Chest, chain.Head, a.Role, a.Clip >= 0 ? assets.Clip(a.Clip).Name : "-", a.Time, chest, head));
            Assert.True(Finite(world, e));
        }
        return seen;
    }

    /// <summary>The turns begun in a run (a turn clip whose time went back; a turn re-aimed keeps its time), by name.</summary>
    static List<string> Begun(List<Seen> run)
    {
        var begun = new List<string>();
        for (int i = 0; i < run.Count; i++)
            if (run[i].Role == MotionRole.Turn && (i == 0 || run[i - 1].Role != MotionRole.Turn || run[i].Time < run[i - 1].Time)) begun.Add(run[i].Clip);
        return begun;
    }

    [Fact]
    public void A_flick_has_the_head_the_chest_and_the_hips_catch_up_in_order_within_their_rates_and_limits()
    {
        // 180° to the left at 600°/s (his fastest flicks were 550–700°/s over a tenth of a second), then held.
        var run = Run(t => MathF.Min(t * 600f, 180f), 4f);
        float dt = Tick, eps = 1e-4f;
        int headAt = -1, chestAt = -1, legsAt = -1, pivoted = 0;
        for (int i = 0; i < run.Count; i++)
        {
            var r = run[i];
            Assert.InRange(r.Head - r.Chest, -S.NeckMost - eps, S.NeckMost + eps);     // the head never further than NeckMost (25°) ahead of the chest
            Assert.InRange(r.Chest - r.Legs, -S.SpineMost - eps, S.SpineMost + eps);   // the chest never further than 30° ahead of the hips
            Assert.InRange(MathF.Abs(r.Legs), 0f, DetMath.Pi + eps);
            if (i > 0)
            {
                var p = run[i - 1];
                float turned = DetMath.WrapAngle(r.View - p.View);
                float head = r.Head + turned - p.Head, chest = r.Chest + turned - p.Chest, legs = r.Legs + turned - p.Legs;   // each part's own turn in the world
                Assert.InRange(MathF.Abs(head), 0f, S.HeadRate * dt + eps);
                Assert.InRange(MathF.Abs(chest), 0f, MathF.Max(S.ChestRate * dt, MathF.Abs(legs)) + eps);   // pushed on only by the hips' limit
                if (MathF.Abs(p.Legs) > S.PivotFrom + S.PivotRate * dt && legs >= S.PivotRate * dt - eps) pivoted++;
                if (i == 1) Assert.InRange(head, 1e-5f, S.HeadRate * dt * 0.9f);     // eased off the mark: no snap
            }
            if (headAt < 0 && MathF.Abs(r.Head) < 10f * Deg && i > 20) headAt = i;
            if (chestAt < 0 && MathF.Abs(r.Chest) < 10f * Deg && i > 20) chestAt = i;
            if (legsAt < 0 && MathF.Abs(r.Legs) < 10f * Deg && i > 20) legsAt = i;
        }
        output.WriteLine($"180° at 600°/s: the head within 10° of the view after {headAt * dt:0.00} s, the chest {chestAt * dt:0.00} s, the hips {legsAt * dt:0.00} s; "
                         + $"pivoting {pivoted} ticks; the turns {string.Join(", ", Begun(run))}");
        Assert.True(headAt > 0 && headAt <= chestAt && chestAt <= legsAt, "the head first, then the chest, then the hips");
        Assert.True(pivoted > 0, "the legs pivot on their own past 75°");
        Assert.InRange(legsAt * dt, 0.3f, 2.5f);           // catching up: a moment, not at once and not seconds
        var end = run[^1];
        Assert.InRange(MathF.Abs(end.Legs), 0f, 1f * Deg);
        Assert.InRange(MathF.Abs(end.Chest), 0f, 1f * Deg);
        Assert.InRange(MathF.Abs(end.Head), 0f, 1f * Deg);
        Assert.Equal(MotionRole.Idle, end.Role);
    }

    [Fact]
    public void A_slow_turn_moves_the_upper_body_as_one_and_starts_the_legs_at_30_degrees()
    {
        // 60°/s (below the studies' 85°/s, the upper body as one block): the head a few degrees behind the view, the chest a few more.
        var run = Run(t => MathF.Min(t * 60f, 90f), 3f);
        float most = 0f;
        foreach (var r in run) most = MathF.Max(most, MathF.Abs(r.Head - r.Chest));
        output.WriteLine($"90° at 60°/s: the head at most {most / Deg:0.0}° ahead of the chest; the turns {string.Join(", ", Begun(run))}");
        Assert.InRange(most, 0f, 6f * Deg);
        int first = run.FindIndex(r => r.Role == MotionRole.Turn);
        Assert.True(first > 0);
        Assert.InRange(MathF.Abs(run[first - 1].Legs), 29f * Deg, 31f * Deg);   // begun at 30°, not the earlier build's 45°
    }

    [Fact]
    public void The_turn_plays_faster_the_further_the_hips_are_behind_between_1_and_its_most()
    {
        Assert.Equal(1f, PeopleTurn.Speedup(0f, S));
        Assert.Equal(1f, PeopleTurn.Speedup(-30f * Deg, S), 4);
        Assert.Equal(1.5f, PeopleTurn.Speedup(52.5f * Deg, S), 4);
        Assert.Equal(2f, PeopleTurn.Speedup(-75f * Deg, S), 4);
        Assert.Equal(2f, PeopleTurn.Speedup(170f * Deg, S), 4);
        Assert.Equal(3f, PeopleTurn.Speedup(80f * Deg, S with { TurnMostSpeedup = 3f }), 4);
        Assert.Equal(1f, PeopleTurn.Speedup(80f * Deg, S with { TurnMostSpeedup = 0.5f }));   // never slower than its own rate
        for (float d = 0f; d <= 180f; d += 1f) Assert.InRange(PeopleTurn.Speedup(d * Deg, S), 1f, S.TurnMostSpeedup);

        // In play: the view 60° off at once, a turn's clock runs Speedup(the hips' turn) times the tick.
        var run = Run(_ => 60f, 0.2f);
        int i = run.FindIndex(r => r.Role == MotionRole.Turn) + 1;
        Assert.Equal(PeopleTurn.Speedup(run[i - 1].Legs, S) * Tick, run[i].Time - run[i - 1].Time, 4);
        Assert.True(run[i].Time - run[i - 1].Time > 1.5f * Tick);
    }

    [Fact]
    public void A_turn_is_turned_round_when_the_view_goes_back_the_other_way()
    {
        // 60° to the left at once, then after a quarter of a second 40° to the right of where it began.
        var run = Run(t => t < 0.25f ? 60f : -40f, 3f);
        var begun = Begun(run);
        output.WriteLine($"left then back right: {string.Join(", ", begun)}");
        Assert.True(begun.Count >= 2);
        Assert.Contains("turn_left", begun[0]);
        Assert.Contains("turn_right", begun[1]);
        // The right turn began while the left one was still playing (stopped, not played out to its end), from where the legs were.
        int at = run.FindIndex(r => r.Clip == begun[1]);
        Assert.Contains("turn_left", run[at - 1].Clip);
        Assert.True(run[at - 1].Time < 0.825f - 1e-3f, "turned round mid-turn");
        Assert.InRange(at * Tick, 0.25f, 0.3f);
        var end = run[^1];
        Assert.InRange(MathF.Abs(end.Legs), 0f, 1f * Deg);
        Assert.Equal(MotionRole.Idle, end.Role);
    }

    [Fact]
    public void A_turn_begun_for_the_view_as_it_was_ends_on_the_view_as_it_is()
    {
        // 60° at 120°/s: the turn begins at 30° and is re-aimed as the view goes on, so one turn brings the legs round.
        var run = Run(t => MathF.Min(t * 120f, 60f), 2.5f);
        var begun = Begun(run);
        int last = run.FindLastIndex(r => r.Role == MotionRole.Turn);
        output.WriteLine($"60° at 120°/s: {string.Join(", ", begun)}; the legs {run[last].Legs / Deg:0.0}° off at its end");
        Assert.Single(begun);
        Assert.InRange(MathF.Abs(run[last].Legs), 0f, 3f * Deg);
        // The earlier build's played the turn it began for (at 45°) and left the legs planted the rest of the way off.
        var old = Run(t => MathF.Min(t * 120f, 60f), 2.5f, LegTurnSettings.NoChain);
        Assert.InRange(MathF.Abs(old[old.FindLastIndex(r => r.Role == MotionRole.Turn)].Legs), 10f * Deg, 20f * Deg);
        // Re-aimed only within the same steps (the 45's 23–67°, the 90's 68–112°: the same pose, so the time goes on), the same way.
        var (assets, k, _) = PeopleDoubles.Kind();
        var turns = assets.Kind(k).Person!.Turns!;
        string Aimed(float degrees, float seconds, float rel)
        {
            int clip = turns.Clip(degrees * Deg);
            return assets.Clip(turns.Retarget(clip, (TurnClip)assets.Clip(clip), seconds, rel * Deg)).Name;
        }
        Assert.Equal("double:turn_left_45@67", Aimed(40f, 0f, -80f));      // stretched as far as the 45's steps go
        Assert.Equal("double:turn_left_45@50", Aimed(40f, 0f, -50f));
        Assert.Equal("double:turn_left_45@40", Aimed(40f, 0f, -41f));      // within 2°: kept
        Assert.Equal("double:turn_left_90@68", Aimed(80f, 0f, -30f));      // shrunk as far as the 90's go
        Assert.Equal("double:turn_left_45@40", Aimed(40f, 0f, 20f));       // the other way: not this turn's to do
        Assert.Equal("double:turn_right_45@-60", Aimed(-40f, 0f, 60f));
        Assert.Equal("double:turn_left_45@40", Aimed(40f, 0.81f, -80f));   // nearly over: kept
    }

    [Fact]
    public void A_turn_clips_own_lead_is_taken_out_and_the_chain_is_all_the_chest_and_head_do()
    {
        // The made-up turns lead with the chest (12°) and the head (8° more), as the set's do.
        var run = Run(_ => 70f, 1.5f);
        var old = Run(_ => 70f, 1.5f, LegTurnSettings.NoChain with { PersonTurnFrom = 30f * Deg });
        float most = 0f, mostOld = 0f;
        foreach (var r in run.Where(r => r.Role == MotionRole.Turn))
        {
            Assert.Equal(r.Chest, r.ChestDrawn, 0.1f * Deg);     // drawn where the chain has it
            Assert.Equal(r.Head, r.HeadDrawn, 0.1f * Deg);
            most = MathF.Max(most, MathF.Abs(r.ChestDrawn - r.Chest));
        }
        foreach (var r in old.Where(r => r.Role == MotionRole.Turn)) mostOld = MathF.Max(mostOld, MathF.Abs(r.ChestDrawn));
        output.WriteLine($"the chest drawn off the chain by at most {most / Deg:0.00}°; the planted turn's chest off the view by the clip's lead up to {mostOld / Deg:0.0}°");
        Assert.InRange(mostOld, 8f * Deg, 13f * Deg);
    }

    [Fact]
    public void Moving_the_legs_are_as_before_and_the_head_and_chest_still_chase()
    {
        // Walking ahead at 1.25 m/s, the view flicked 80° to the left at 600°/s.
        Func<float, Vector3> walk = t => new Vector3(0f, 1.25f, 0f);
        var run = Run(t => t < 0.5f ? 0f : MathF.Min((t - 0.5f) * 600f, 80f), 2f, velocity: walk);
        var old = Run(t => t < 0.5f ? 0f : MathF.Min((t - 0.5f) * 600f, 80f), 2f, LegTurnSettings.NoChain, velocity: walk);
        for (int i = 0; i < run.Count; i++) Assert.Equal(old[i].Legs, run[i].Legs, 5);
        float behind = run.Max(r => MathF.Abs(r.Head));
        output.WriteLine($"walking, an 80° flick: the head at most {behind / Deg:0.0}° behind the view (the planted turn's chest and head on it)");
        Assert.InRange(behind, 5f * Deg, 80f * Deg);
        Assert.Equal(0f, old.Max(r => MathF.Abs(r.Head)));
        Assert.InRange(MathF.Abs(run[^1].Head), 0f, 1f * Deg);
    }

    [Fact]
    public void The_turn_chain_off_switch_gives_the_planted_turn_and_no_store()
    {
        var (assets, k, _) = PeopleDoubles.Kind();
        var (world, _) = Make(assets, k, Floor(), Vector3.Zero, legs: LegTurnSettings.NoChain);
        Assert.False(world.TryStore<PersonTurn>(out _));
        var (chained, _) = Make(assets, k, Floor(), Vector3.Zero);
        Assert.True(chained.TryStore<PersonTurn>(out _));
        // A world with no people (1.1's, the arena's) has none either, chain or not: its stores and digests are as before.
        var none = new WorldBuilder(1).Animation(new CharacterAssets()).Build();
        Assert.False(none.TryStore<PersonTurn>(out _));

        // The earlier build's numbers: planted at 40° (it began at 45°), the legs held at the 90° twist by a view turned on, the chest on the view.
        Assert.DoesNotContain(Run(_ => 40f, 1f, LegTurnSettings.NoChain), r => r.Role == MotionRole.Turn);
        Assert.Contains(Run(_ => 40f, 1f), r => r.Role == MotionRole.Turn);
        var old = Run(t => MathF.Min(t * 600f, 180f), 1f, LegTurnSettings.NoChain);
        Assert.Equal(90f * Deg, old.Max(r => MathF.Abs(r.Legs)), 4);
        Assert.All(old, r => Assert.Equal(0f, r.Chest));
    }

    [Fact]
    public void The_turns_note_carries_its_numbers_and_none_is_the_unchained_turn()
    {
        var given = S with { HeadRate = 250f * Deg, NeckMost = 40f * Deg, PersonTurnFrom = 35f * Deg, TurnMostSpeedup = 1.5f, ChestLag = 0.08f };
        var back = new LegTurnSettings().WithNote(given.Note());
        Assert.Equal(given.Note(), back.Note());
        Assert.True(back.Chain);
        Assert.Equal(250f * Deg, back.HeadRate, 5);
        Assert.Equal(0.08f, back.ChestLag);
        Assert.StartsWith("chain head=300 headLag=0.03 neck=25 chest=200 chestLag=0.06 spine=30 from=30 on=20 speedup=2 pivotFrom=75 pivotRate=240", S.Note());
        var old = S.WithNote(null);
        Assert.False(old.Chain);
        Assert.Equal(45f * Deg, old.PersonTurnFrom, 5);
        Assert.Equal(LegTurnSettings.NoChain.Note(), old.Note());
        Assert.False(S.WithNote(LegTurnSettings.NoChain.Note()).Chain);
    }

    [Fact]
    public void The_chase_eases_on_never_overshoots_and_keeps_to_its_rate()
    {
        float angle = 1f, speed = 0f, last = angle;
        for (int t = 0; t < 240; t++)
        {
            PeopleTurn.Chase(ref angle, ref speed, 0f, 0.06f, 200f * Deg, Tick);
            Assert.InRange(angle, 0f, 1f);                                 // critically damped: on to the target, never past it
            Assert.InRange(last - angle, 0f, 200f * Deg * Tick + 1e-6f);  // never faster than its rate
            if (t == 0) Assert.True(last - angle < 200f * Deg * Tick * 0.5f, "eased off the mark");
            last = angle;
        }
        Assert.InRange(angle, 0f, 1e-3f);
        float a = 1f, v = 0f;
        PeopleTurn.Chase(ref a, ref v, 0f, 0f, 120f * Deg, Tick);
        Assert.Equal(1f - 120f * Deg * Tick, a, 5);                         // no lag: straight at the rate
    }

    [Fact]
    public void Crouched_and_still_a_person_holds_the_crouch_with_no_kneel()
    {
        var (assets, k, _) = PeopleDoubles.Kind(crouch: true);
        var system = new AnimationSystem(assets);
        Assert.False(system.Settings.PersonKneel);
        var a = CharacterAnimation.For(assets, k);
        string Name() => assets.Clip(a.Clip).Name;
        var names = new HashSet<string>();
        void Go(MotionInput input, float seconds) { for (int t = 0; t < (int)MathF.Round(seconds * World.TicksPerSecond); t++) { system.Step(ref a, input, Tick); names.Add(Name()); } }
        MotionInput Still(bool crouched) => new() { Grounded = true, Crouched = crouched };
        MotionInput Going(float speed, bool crouched) => new() { Grounded = true, Crouched = crouched, GroundSpeed = speed, ForwardSpeed = speed };

        // From the stand: crouch_down, then the crouch held for as long as it is held.
        Go(Still(false), 0.5f);
        Go(Still(true), 0.1f);
        Assert.Equal(("crouch_down", MotionRole.Crouch), (Name(), a.Role));
        Go(Still(true), 3f);
        Assert.Equal("crouch", Name());
        // Up: crouch_up, the stand. From a stride stopping as it crouches: crouch_down (its bend, not the stride faded into the low pose).
        Go(Still(false), 0.1f);
        Assert.Equal("crouch_up", Name());
        Go(Still(false), 1f);
        Go(Going(1f, false), 1f);
        Assert.Equal("double:walk@0", Name());
        Go(Still(true), 0.05f);
        Assert.Equal("crouch_down", Name());
        // From the crouched walk, stopping: the crouch held, straight.
        Go(Going(0.8f, true), 1f);
        Go(Still(true), 0.05f);
        Assert.Equal("crouch", Name());
        Go(Still(true), 2f);
        Assert.Equal("crouch", Name());
        Assert.DoesNotContain(names, n => n is "crouch_settle" or "crouch_kneel" or "crouch_rise" or "crouch_kneel_down" or "crouch_kneel_up");

        // A crouched turn ends in the crouch held, and stays there.
        var run = Run(_ => 70f, 3f, crouched: 1, crouchKit: true);
        Assert.Contains(run, r => r.Clip.Contains("crouch_turn_left"));
        Assert.DoesNotContain(run, r => r.Clip is "crouch_settle" or "crouch_kneel" or "crouch_rise" or "crouch_kneel_down");
        Assert.Equal("crouch", run[^1].Clip);
        Assert.InRange(MathF.Abs(run[^1].Legs), 0f, 1f * Deg);

        // --crouch-kneel: the earlier build's settle into the kneel after half a second.
        var kneeling = new AnimationSystem(assets, new AnimationSettings { PersonKneel = true });
        var b = CharacterAnimation.For(assets, k);
        for (int t = 0; t < World.TicksPerSecond * 2; t++) kneeling.Step(ref b, Still(true), Tick);
        Assert.Equal("crouch_kneel", assets.Clip(b.Clip).Name);
    }
}
