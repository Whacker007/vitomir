// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Formats.L2;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Perception;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// Who sees on a floor built in memory: a neonate's charged
/// leap seen by witnesses of the enhanced module's kinds (SANDBOX, as its files say them) — near or at the edge of
/// sight, by night or by day, in a bush, behind a low wall crouched or standing —; the make-out time; the act table (a
/// burst of speed never alarms by itself); the reaction rule; a shout heard once within reach; a guard's run and
/// challenge; the ladder cooling back to the routine; an area's heat; the light and conceal areas read from a manifest;
/// the same digests from the same inputs; a witness read from the command line; and nothing allocated per tick.
/// </summary>
public sealed class PerceptionTests
{
    static readonly Locomotion Human = Locomotion.Sandbox with
    {
        StepHeight = 0.35f, SteepestGroundDegrees = 40f, JumpImpulseBudget = Locomotion.ImpulseFor(0.5f, 9.81f), FallTolerance = 8f,
    };

    /// <summary>A neonate: a 1 m jump (twice a human's), a 7 m/s sprint.</summary>
    static readonly Locomotion Neonate = Human with
    {
        RunSpeed = 3.5f, SprintSpeed = 7f, RestingSpeed = 7f, JumpImpulseBudget = Locomotion.ImpulseFor(1f, 9.81f), StandingReach = 2.35f,
        VaultMax = 1.6f, MantleMax = 2.4f,
    };

    static ObserverNumbers Kind(float sight, float cone, float eyes, float night, float attention, float bravery, float curiosity, float duty,
        float excitability, float voice, float notice, float makeOut, float conclude, float hold, float coolDown) => new()
    {
        SightRange = sight, ConeDegrees = cone, Eyesight = eyes, NightEyes = night, Attention = attention, Bravery = bravery, Curiosity = curiosity,
        Duty = duty, Excitability = excitability, Voice = voice,
        NoticeSeconds = notice, MakeOutSeconds = makeOut, ConcludeSeconds = conclude, HoldSeconds = hold, CoolDownSeconds = coolDown,
    };

    // The enhanced module's kinds (SANDBOX), as Data/perception/*.json says them (the ladder's waits and the calming down).
    internal static readonly ObserverNumbers Guard = Kind(30, 70, 1f, 0.3f, 0.9f, 0.9f, 0.5f, 1f, 0.5f, 1f, 0.4f, 1.5f, 2f, 30, 240);
    internal static readonly ObserverNumbers OldMan = Kind(20, 50, 0.55f, 0.1f, 0.5f, 0.2f, 0.3f, 0f, 0.3f, 0.5f, 1.2f, 4f, 5f, 20, 120);
    internal static readonly ObserverNumbers Townsman = Kind(25, 60, 0.8f, 0.2f, 0.7f, 0.4f, 0.3f, 0.1f, 0.5f, 0.8f, 0.6f, 2.5f, 3f, 20, 150);
    internal static readonly ObserverNumbers Monk = Kind(22, 55, 0.7f, 0.2f, 0.6f, 0.5f, 0.6f, 0.2f, 0.4f, 0.6f, 0.6f, 2.5f, 3f, 20, 150);
    internal static readonly ObserverNumbers Drunk = Kind(18, 45, 0.5f, 0.2f, 0.25f, 0.7f, 0.6f, 0f, 0.15f, 0.7f, 1.5f, 5f, 6f, 10, 60);

    internal sealed class Rig(World w, EntityId player)
    {
        public World W { get; } = w;
        public EntityId Player { get; } = player;
        public List<AwarenessChanged> Changes { get; } = [];
        public List<ShoutRaised> Shouts { get; } = [];
        public List<ReactionStarted> Reactions { get; } = [];
        public List<AreaHeatRose> Rose { get; } = [];
        public List<AreaHeatGone> Gone { get; } = [];
        public List<MasqueradeRisk> Risks { get; } = [];
        /// <summary>The last tick an act was in view (−1: none yet).</summary>
        public long LastActInView { get; set; } = -1;
        public PerceptionConfig Config => W.Service<PerceptionConfig>();
    }

    static LevelSpace Floor(Action<TestLevel>? more = null, LevelPlaces? places = null)
    {
        var t = new TestLevel().Floor(-40, -40, 40, 40);
        more?.Invoke(t);
        return new LevelSpace("floor", t.Build("floor").Collision) { Places = places ?? LevelPlaces.None };
    }

    /// <summary>A world on <paramref name="level"/> with a neonate unbound at <paramref name="at"/> and the kinds guard, oldman, townsman, monk and drunk.</summary>
    internal static Rig Make(LevelSpace level, Vector3 at, LevelAreas? areas = null, bool night = true, Action<PerceptionConfig>? tune = null, ulong seed = 1,
        Action<WorldBuilder>? build = null)
    {
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        PerceptionServices.Register(b, areas);
        build?.Invoke(b);
        var w = b.Build();
        var profiles = w.Service<MovementProfiles>();
        profiles.Add(new MovementProfile(AssetId.Of("test", "movement/human"), "a human", Human, true), isHuman: true);
        int n = profiles.Add(new MovementProfile(AssetId.Of("test", "movement/neonate"), "a neonate", Neonate, true));
        var p = MovementServices.SpawnControlled(w, at, 0f, Human);
        profiles.Give(w, p, n);
        w.Get<MasqueradeState>(p).Mode = MasqueradeMode.Unbound;
        w.Get<Locomotion>(p) = profiles.Numbers(n, MasqueradeMode.Unbound);
        var config = w.Service<PerceptionConfig>();
        config.Night = night;
        foreach (var (name, numbers) in new[] { ("guard", Guard), ("oldman", OldMan), ("townsman", Townsman), ("monk", Monk), ("drunk", Drunk) })
            config.Add(new ObserverKind(AssetId.Of("test", "perception/" + name), name, [], numbers, true));
        tune?.Invoke(config);
        return new Rig(w, p);
    }

    /// <summary>A witness of <paramref name="kind"/> standing at (x, y) facing <paramref name="yawDegrees"/>.</summary>
    internal static EntityId Witness(Rig r, string kind, float x, float y, float yawDegrees, float z = 0.01f) =>
        PerceptionServices.Witness(r.W, new WitnessRequest(kind, new(x, y, z), yawDegrees), r.Config.IndexOf(kind), Human);

    /// <summary>A witness at (x, y) facing the player.</summary>
    internal static EntityId Facing(Rig r, string kind, float x, float y, float z = 0.01f)
    {
        var p = r.W.Get<Body>(r.Player).Feet;
        return Witness(r, kind, x, y, DetMath.Atan2(p.Y - y, p.X - x) / DetMath.DegToRad, z);
    }

    internal static void Run(Rig r, int ticks, ActionFrame frame)
    {
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(r.W, frame);
            r.Changes.AddRange(r.W.Events<AwarenessChanged>().ThisTick);
            r.Shouts.AddRange(r.W.Events<ShoutRaised>().ThisTick);
            r.Reactions.AddRange(r.W.Events<ReactionStarted>().ThisTick);
            r.Rose.AddRange(r.W.Events<AreaHeatRose>().ThisTick);
            r.Gone.AddRange(r.W.Events<AreaHeatGone>().ThisTick);
            r.Risks.AddRange(r.W.Events<MasqueradeRisk>().ThisTick);
            if (r.W.Service<PerceptionState>().Acts.Length > 0) r.LastActInView = r.W.Tick;
        }
    }

    /// <summary>Steps until <paramref name="who"/> comes to <paramref name="rung"/> (at most <paramref name="seconds"/>), or fails.</summary>
    internal static void Until(Rig r, EntityId who, Awareness rung, float seconds = 20f)
    {
        for (int i = 0; i < seconds * World.TicksPerSecond; i++)
        {
            Run(r, 1, ActionFrame.Idle);
            if (r.W.Get<Observer>(who).Level == rung) return;
        }
        Assert.Fail($"it never came to {rung}: {string.Join(", ", r.Changes.Where(c => c.Observer == who))}");
    }

    /// <summary>The player put at (x, y), standing.</summary>
    internal static void Put(Rig r, float x, float y) => r.W.Get<Body>(r.Player) = Body.At(new(x, y, 0.01f), 0f);

    /// <summary>A charge held to its full (ticks).</summary>
    static readonly int Full = (int)((BodyMotor.HoldSeconds + Neonate.ChargeSeconds) * World.TicksPerSecond) + 12;

    /// <summary>The neonate settles, charges its jump to the full and leaps (1 m, twice a human's), then stands <paramref name="after"/> seconds.</summary>
    internal static void Leap(Rig r, float after = 3f, bool risk = true)
    {
        Run(r, 30, ActionFrame.Idle);
        Run(r, Full, ActionFrame.Idle with { Held = Buttons.Jump });
        Run(r, (int)(after * World.TicksPerSecond), ActionFrame.Idle);
        if (risk) Assert.Contains(r.Risks, k => k.Act == MasqueradeAct.Jump && k.Entity == r.Player);
        else Assert.Empty(r.Risks);
    }

    internal static Awareness Highest(Rig r, EntityId who) =>
        r.Changes.Where(c => c.Observer == who).Select(c => c.To).DefaultIfEmpty(Awareness.Unaware).Max();

    internal static long First(Rig r, EntityId who, Awareness atLeast) =>
        r.Changes.Where(c => c.Observer == who && c.To >= atLeast).Select(c => c.Tick).DefaultIfEmpty(-1).First();

    // ---- seeing ----

    [Fact]
    public void A_leap_in_full_view_of_a_guard_8_m_off_at_night_alarms_it_and_one_at_the_edge_of_its_sight_is_aware_at_most()
    {
        var r = Make(Floor(), new(0, 0, 0.01f));
        var near = Facing(r, "guard", 8, 0);
        var edge = Facing(r, "guard", -15f, 0.5f);   // at take-off (4.4 m/s up: as a sprint) it sees to 15.8 m, then to 12.2 m
        Leap(r, after: 6f);
        Assert.Equal(Awareness.Alarmed, Highest(r, near));
        Assert.True(Highest(r, edge) <= Awareness.Aware, $"the guard at the edge of its sight came to {Highest(r, edge)}");
    }

    [Fact]
    public void By_day_a_guard_20_m_off_sees_a_leap_that_at_night_it_cannot()
    {
        foreach (bool night in new[] { false, true })
        {
            var r = Make(Floor(), new(0, 0, 0.01f), night: night);
            var g = Facing(r, "guard", 20, 0);
            Leap(r, after: 4f);
            if (night) Assert.Equal(Awareness.Unaware, Highest(r, g));
            else Assert.True(Highest(r, g) >= Awareness.Alerted, $"by day it came to {Highest(r, g)}");
        }
    }

    [Fact]
    public void Behind_a_wall_or_with_its_back_turned_a_guard_sees_nothing()
    {
        var r = Make(Floor(t => t.Box(new(3, -3, 0), new(3.4f, 3, 3))), new(0, 0, 0.01f));
        var behind = Facing(r, "guard", 7, 0);
        var away = Witness(r, "guard", -6, 0, 180);
        Leap(r);
        Assert.Equal(Awareness.Unaware, Highest(r, behind));
        Assert.Equal(Awareness.Unaware, Highest(r, away));
    }

    [Fact]
    public void At_night_in_a_bush_a_leap_is_hidden_from_a_guard_5_m_off_and_seen_from_1_5_m()
    {
        var bush = LevelAreas.Of([], conceals: [(new TurnedBox(new(-1.5f, -1.5f, -1), 0, new(3, 3, 4)), 0.2f)]);
        foreach (float d in new[] { 5f, 1.5f })
        {
            var r = Make(Floor(), new(0, 0, 0.01f), bush);
            var g = Facing(r, "guard", d, 0);
            Leap(r);
            if (d > 2) Assert.Equal(Awareness.Unaware, Highest(r, g));
            else Assert.True(Highest(r, g) >= Awareness.Aware, $"the guard at {d} m came to {Highest(r, g)}");
        }
    }

    [Fact]
    public void Behind_a_low_wall_a_crouched_body_shows_less_than_a_standing_one_and_behind_a_high_wall_none()
    {
        float Seen(float wall, bool crouched)
        {
            var r = Make(Floor(t => t.Box(new(2, -2, 0), new(2.3f, 2, wall))), new(0, 0, 0.01f), night: false);
            var g = Facing(r, "guard", 6, 0);
            Run(r, 30, crouched ? ActionFrame.Idle with { Held = Buttons.Crouch } : ActionFrame.Idle);
            ref readonly var body = ref r.W.Get<Body>(g);
            var eyes = body.Feet + new Vector3(0, 0, r.W.Get<Locomotion>(g).EyeHeight);
            var (s, c) = DetMath.SinCos(body.Yaw);
            return PerceptionSystem.Clarity(r.Config, LevelAreas.None, r.W.Service<LevelSpace>().Collision, r.W.Store<Body>(), r.W.Store<Locomotion>(),
                r.Config.IndexOf("guard"), eyes, new(c, s, 0), r.Player, g);
        }
        float open = Seen(0.05f, false), standing = Seen(1.2f, false), crouched = Seen(1.2f, true), high = Seen(2.6f, false);
        Assert.True(open > standing && standing > crouched && crouched > 0f, $"open {open:0.###}, standing {standing:0.###}, crouched {crouched:0.###}");
        Assert.Equal(0f, high);
    }

    [Fact]
    public void Each_step_up_the_ladder_waits_the_kind_s_own_time_on_the_rung_below()
    {
        // An example: a guard sees a clear leap 8 m off at night — aware at about 0.4 s, alerted at about 1.9 s,
        // alarmed at about 3.9 s. An old man close by takes his own, slower, times.
        foreach (var (kind, at, k) in new[] { ("guard", 8f, Guard), ("oldman", 1.5f, OldMan) })
        {
            var r = Make(Floor(), new(0, 0, 0.01f));
            var o = Facing(r, kind, at, 0);
            Leap(r, after: k.NoticeSeconds + k.MakeOutSeconds + k.ConcludeSeconds + 2f);
            long risk = r.Risks.First(x => x.Act == MasqueradeAct.Jump).Tick;
            long aware = First(r, o, Awareness.Aware), alerted = First(r, o, Awareness.Alerted), alarmed = First(r, o, Awareness.Alarmed);
            string ladder = $"{kind}: {string.Join(", ", r.Changes.Where(c => c.Observer == o))}";
            Assert.True(aware >= 0 && alerted > aware && alarmed > alerted, ladder);
            static long T(float s) => (long)MathF.Round(s * World.TicksPerSecond);
            // Its first look comes within a look's time (0.15 s, up to half more), then the notice.
            Assert.InRange(aware - risk, T(k.NoticeSeconds) - 1, T(k.NoticeSeconds + 0.25f));
            Assert.InRange(alerted - aware, T(k.MakeOutSeconds) - 1, T(k.MakeOutSeconds) + 1);
            Assert.InRange(alarmed - alerted, T(k.ConcludeSeconds) - 1, T(k.ConcludeSeconds) + 1);
            Assert.All(r.Changes.Where(c => c.Observer == o), c => Assert.True(c.To > c.From, ladder));   // one step at a time, none back
            Assert.Equal(3, r.Changes.Count(c => c.Observer == o));
        }
    }

    [Fact]
    public void What_was_seen_is_still_taken_in_after_the_act_has_left_view()
    {
        // A single leap (in view about a second) still carries a guard 8 m off to alarmed some seconds later.
        var r = Make(Floor(), new(0, 0, 0.01f));
        var g = Facing(r, "guard", 8, 0);
        Leap(r, after: 6f);
        long alarmed = First(r, g, Awareness.Alarmed), risk = r.Risks.First(x => x.Act == MasqueradeAct.Jump).Tick;
        Assert.True(alarmed > 0, string.Join(", ", r.Changes));
        Assert.InRange(alarmed - risk, (long)(3.8f * World.TicksPerSecond), (long)(4.2f * World.TicksPerSecond));
        Assert.True(r.LastActInView < alarmed - 2 * World.TicksPerSecond, $"the act was in view until {r.LastActInView}, alarmed at {alarmed}");
    }

    // ---- acts and the reaction rule ----

    /// <summary>A world where a test system raises a risk of <paramref name="act"/> by the player at tick 30 (a body standing still), seen by a guard 3 m off by day.</summary>
    static Awareness Seen(MasqueradeAct act)
    {
        var player = EntityId.None;
        // Raised after the risk check, before perception reads it, as the risk system's own would be.
        var r = Make(Floor(), new(0, 0, 0.01f), night: false, build: b => b.System(Phase.Perceive, MasqueradeServices.RiskOrder + 5, "test.raise", w =>
        {
            if (w.Tick == 30) w.Events<MasqueradeRisk>().Publish(new MasqueradeRisk(player, w.Tick, act, TraversalKind.None, w.Get<Body>(player).Feet, 9f, 5.5f));
        }));
        player = r.Player;
        var g = Facing(r, "guard", 3, 0);
        Run(r, 6 * World.TicksPerSecond, ActionFrame.Idle);
        return Highest(r, g);
    }

    [Fact]
    public void Each_act_is_taken_as_the_act_table_says_a_burst_of_speed_never_alarming_by_itself()
    {
        Assert.Equal(Awareness.Alerted, Seen(MasqueradeAct.Speed));
        Assert.Equal(Awareness.Alarmed, Seen(MasqueradeAct.Fall));
    }

    [Fact]
    public void The_reaction_rule_sends_each_kind_its_way_by_its_traits_and_the_act()
    {
        var n = PerceptionNumbers.Sandbox;
        var acts = PerceptionNumbers.SandboxActs;
        ActResponse jump = acts[(int)MasqueradeAct.Jump], speed = acts[(int)MasqueradeAct.Speed];
        Reaction D(Awareness level, in ObserverNumbers k, in ActResponse a, Reaction doing = Reaction.Routine) => ReactionSystem.Decide(level, k, a, n, doing);
        Assert.Equal(Reaction.Routine, D(Awareness.Unaware, Townsman, jump, Reaction.Look));
        Assert.Equal(Reaction.Look, D(Awareness.Aware, Guard, jump));
        Assert.Equal(Reaction.BackOff, D(Awareness.Alerted, Townsman, jump));         // fear 0.3 over pull 0.27
        Assert.Equal(Reaction.EdgeCloser, D(Awareness.Alerted, Monk, jump));          // a curious monk goes to look
        Assert.Equal(Reaction.Watch, D(Awareness.Alerted, Townsman, speed));          // a burst of speed: a curious look
        Assert.Equal(Reaction.EdgeCloser, D(Awareness.Alerted, Drunk, jump));         // bold with drink
        Assert.Equal(Reaction.GoLook, D(Awareness.Alerted, Guard, speed));            // duty: always goes to look
        Assert.Equal(Reaction.Search, D(Awareness.Alerted, Guard, jump, Reaction.Challenge));
        Assert.Equal(Reaction.RunTo, D(Awareness.Alarmed, Guard, jump));              // duty: never flees
        Assert.Equal(Reaction.Flee, D(Awareness.Alarmed, Townsman, jump));
        Assert.Equal(Reaction.Flee, D(Awareness.Alarmed, OldMan, jump));
        Assert.Equal(Reaction.Watch, D(Awareness.Alarmed, Guard with { Duty = 0f, Bravery = 1f }, jump));   // brave, no duty: it stares
        Assert.Equal(Reaction.Watch, D(Awareness.Alerted, Townsman, jump, Reaction.Flee));                  // cooled from fleeing
        // A keeper of the peace alarmed while going to look runs; searching where they were last seen it keeps searching,
        // until it finds them in sight again (and not at once: one out of reach is not run at each tick).
        Assert.Equal(Reaction.RunTo, D(Awareness.Alarmed, Guard, jump, Reaction.GoLook));
        Assert.Equal(Reaction.Search, ReactionSystem.Decide(Awareness.Alarmed, Guard, jump, n, Reaction.Search, suspectUnseen: 5f, doingSeconds: 3f));
        Assert.Equal(Reaction.Search, ReactionSystem.Decide(Awareness.Alarmed, Guard, jump, n, Reaction.Search, suspectUnseen: 0.1f, doingSeconds: 0.2f));
        Assert.Equal(Reaction.RunTo, ReactionSystem.Decide(Awareness.Alarmed, Guard, jump, n, Reaction.Search, suspectUnseen: 0.1f, doingSeconds: 3f));
        // Searching when it concludes, with them in sight, it runs at them at once; out of sight it goes on searching.
        Assert.Equal(Reaction.RunTo, ReactionSystem.Decide(Awareness.Alarmed, Guard, jump, n, Reaction.Search, suspectUnseen: 0.1f, doingSeconds: 0.2f, fresh: true));
        Assert.Equal(Reaction.Search, ReactionSystem.Decide(Awareness.Alarmed, Guard, jump, n, Reaction.Search, suspectUnseen: 5f, doingSeconds: 0.2f, fresh: true));
    }

    // ---- reactions, shouts, cooling, heat ----

    [Fact]
    public void An_alarmed_townsman_shouts_once_and_those_within_its_voice_are_alerted_about_the_place_and_no_one_further()
    {
        var r = Make(Floor(), new(0, 0, 0.01f));
        var t = Facing(r, "townsman", 2.5f, 0);
        var guard = Witness(r, "guard", 12, 0, 0);       // 9.5 m beyond the townsman, its back turned
        var far = Witness(r, "oldman", 22, 0, 0);        // beyond a townsman's voice (0.8 × 15 = 12 m), though it backs off 4 m before it is alarmed
        Leap(r, after: 8f);
        Assert.Equal(Awareness.Alarmed, Highest(r, t));
        var shout = Assert.Single(r.Shouts);
        Assert.Equal(t, shout.Who);
        Assert.Equal(12f, shout.Radius, 3);
        Assert.Equal(1, shout.Heard);
        var heard = Assert.Single(r.Changes, c => c.Observer == guard && c.Cause == AwarenessCause.Heard);
        Assert.Equal(Awareness.Unaware, heard.From);
        Assert.Equal(Awareness.Alerted, heard.To);                  // told: straight on alerted, no wait
        Assert.Equal(shout.Tick, heard.Tick);
        Assert.True(Vector2.Distance(new(heard.Where.X, heard.Where.Y), Vector2.Zero) < 0.5f, $"heard about {heard.Where}");
        Assert.Contains(r.Reactions, x => x.Who == guard && x.Doing == Reaction.GoLook);
        Assert.Contains(r.Reactions, x => x.Who == t && x.Doing == Reaction.Flee);
        Assert.Equal(Awareness.Unaware, Highest(r, far));
    }

    [Fact]
    public void An_alarmed_guard_runs_at_the_one_it_saw_and_challenges_them()
    {
        var r = Make(Floor(), new(0, 0, 0.01f));
        var g = Facing(r, "guard", 2, 0);
        Leap(r, after: 6f);
        var mine = r.Reactions.Where(x => x.Who == g).Select(x => x.Doing).ToList();
        Assert.True(mine.IndexOf(Reaction.RunTo) >= 0 && mine.IndexOf(Reaction.Challenge) > mine.IndexOf(Reaction.RunTo), string.Join(", ", mine));
        Assert.Equal(1, r.W.Get<Observer>(g).Challenged);
    }

    [Fact]
    public void After_the_last_sight_the_evidence_holds_then_cools_over_minutes_down_the_ladder_and_back_to_the_routine()
    {
        // The guard's own numbers: once it has lost sight of the player it holds 30 s, then cools from the ceiling over
        // 240 s — alarmed until about 110 s, alerted until about 206 s, aware until about 254 s; then its routine.
        // (The alarm's heat, which lowers the thresholds while it lasts, is left out here: with it the guard stays alarmed
        // until about 131 s.)
        var r = Make(Floor(), new(0, 0, 0.01f), tune: c => c.Set(c.Numbers with { HeatWariness = 0f }, true));
        var g = Facing(r, "guard", 6, 0);
        Leap(r, after: 0.5f);
        Until(r, g, Awareness.Alarmed);
        Assert.Equal(PerceptionNumbers.Sandbox.Ceiling, r.W.Get<Observer>(g).Value, 3);
        Put(r, -35, -35);                                    // gone, out of its sight
        long lost = -1;
        float held = 0f;
        for (int i = 0; i < 280 * World.TicksPerSecond; i++)
        {
            Run(r, 1, ActionFrame.Idle);
            ref readonly var o = ref r.W.Get<Observer>(g);
            if (o.Unseen == 0f) lost = r.W.Tick;
            else if (r.W.Tick == lost + 29 * World.TicksPerSecond) held = o.Value;
        }
        Assert.Equal(PerceptionNumbers.Sandbox.Ceiling, held, 3);    // 29 s after the last sight, nothing yet lost
        var down = r.Changes.Where(c => c.Observer == g && c.Cause == AwarenessCause.Cooled).ToList();
        Assert.Equal([(Awareness.Alarmed, Awareness.Alerted), (Awareness.Alerted, Awareness.Aware), (Awareness.Aware, Awareness.Unaware)],
            down.Select(c => (c.From, c.To)));
        float After(int k) => (down[k].Tick - lost) / (float)World.TicksPerSecond;
        Assert.InRange(After(0), 109f, 111f);
        Assert.InRange(After(1), 205f, 207f);
        Assert.InRange(After(2), 253f, 255f);
        Assert.Equal(Reaction.Routine, r.Reactions.Last(x => x.Who == g).Doing);
        Assert.Equal(0, r.W.Get<RouteWalker>(g).Errand);
    }

    [Fact]
    public void A_rung_is_kept_its_least_time_before_it_steps_down()
    {
        // A kind that forgets at once (no hold, a second's cool-down) still keeps each rung ten seconds on the way down.
        var r = Make(Floor(), new(0, 0, 0.01f), tune: c => c.Add(new ObserverKind(AssetId.Of("test", "perception/quick"), "quick", [],
            Guard with { HoldSeconds = 0f, CoolDownSeconds = 1f }, true)));
        var g = Facing(r, "quick", 6, 0);
        Leap(r, after: 0.5f);
        Until(r, g, Awareness.Alarmed);
        long top = r.W.Tick;
        Put(r, -35, -35);
        Run(r, 35 * World.TicksPerSecond, ActionFrame.Idle);
        var down = r.Changes.Where(c => c.Observer == g && c.Cause == AwarenessCause.Cooled).Select(c => c.Tick).ToList();
        Assert.Equal(3, down.Count);
        long least = (long)(PerceptionNumbers.Sandbox.MinRungSeconds * World.TicksPerSecond);
        Assert.InRange(down[0] - top, least - 1, least + 1);
        Assert.InRange(down[1] - down[0], least - 1, least + 1);
        Assert.InRange(down[2] - down[1], least - 1, least + 1);
    }

    [Fact]
    public void A_challenging_guard_follows_at_a_walk_and_runs_again_only_beyond_10_m_or_out_of_sight()
    {
        // On a floor with no routes it is sent walking but stands (the proving ground's witness check sees it walk).
        var r = Make(Floor(), new(0, 0, 0.01f), night: false);
        var g = Facing(r, "guard", 4, 0);
        Leap(r, after: 0.5f);
        Until(r, g, Awareness.Alarmed);
        Run(r, 2 * World.TicksPerSecond, ActionFrame.Idle);
        Assert.Equal(Reaction.Challenge, r.W.Get<Observer>(g).Doing);
        Challenged(r, g, 6f);
    }

    /// <summary>
    /// With guard <paramref name="g"/> challenging, the player is put about it within <paramref name="near"/> m for half a
    /// minute — it keeps challenging, sent walking after them when they are beyond its reach —, then 8 m off — it still
    /// challenges —, then 16 m off — it runs. Answers how far it walked meanwhile.
    /// </summary>
    internal static float Challenged(Rig r, EntityId g, float near, Func<Vector2, Vector2>? keep = null, Vector2[]? corners = null)
    {
        int before = r.Reactions.Count;
        float walked = 0f;
        int sent = 0;
        var last = r.W.Get<Body>(g).Feet;
        for (int k = 0; k < 15; k++)
        {
            var at = r.W.Get<Body>(g).Feet;
            float a = k * 0.9f;
            var spot = new Vector2(at.X + (near - 1f) * MathF.Cos(a), at.Y + (near - 1f) * MathF.Sin(a));
            spot = keep?.Invoke(spot) ?? spot;
            Put(r, spot.X, spot.Y);
            for (int i = 0; i < 2 * World.TicksPerSecond; i++)
            {
                Run(r, 1, ActionFrame.Idle);
                ref readonly var body = ref r.W.Get<Body>(g);
                walked += Vector3.Distance(body.Feet, last);
                last = body.Feet;
                Assert.True(new Vector2(body.Velocity.X, body.Velocity.Y).Length() < 2.2f, "it ran");
                if (r.W.Get<RouteWalker>(g).Errand == RouteWalker.WalkErrand) sent++;
            }
        }
        Assert.DoesNotContain(r.Reactions.Skip(before), x => x.Who == g);
        Assert.Equal(Reaction.Challenge, r.W.Get<Observer>(g).Doing);
        Assert.True(sent > 0, "it was never sent after them");
        var here = r.W.Get<Body>(g).Feet;
        var p = r.W.Get<Body>(r.Player).Feet;
        var toward = Vector2.Normalize(new Vector2(p.X - here.X, p.Y - here.Y));
        var eight = new Vector2(here.X + 8f * toward.X, here.Y + 8f * toward.Y);
        eight = keep?.Invoke(eight) ?? eight;
        Put(r, eight.X, eight.Y);
        Run(r, 1 * World.TicksPerSecond, ActionFrame.Idle);
        Assert.Equal(Reaction.Challenge, r.W.Get<Observer>(g).Doing);
        here = r.W.Get<Body>(g).Feet;
        toward = Vector2.Normalize(new Vector2(eight.X - here.X, eight.Y - here.Y));
        var far = new Vector2(here.X + 16f * toward.X, here.Y + 16f * toward.Y);
        // Where the ground is bounded, its corner farthest from the guard.
        if (corners is not null) far = corners.MaxBy(c => Vector2.Distance(c, new Vector2(here.X, here.Y)));
        Assert.True(Vector2.Distance(far, new Vector2(here.X, here.Y)) > 12f);
        Put(r, far.X, far.Y);
        Run(r, World.TicksPerSecond * 3 / 2, ActionFrame.Idle);   // it turns to them (at most 0.75 s) and looks
        Assert.Contains(r.Reactions.Skip(before), x => x.Who == g && x.Doing == Reaction.RunTo);
        return walked;
    }

    [Fact]
    public void An_alarm_heats_its_area_which_cools_faster_once_the_player_has_left_it_and_lowers_the_thresholds_only_there()
    {
        var areas = LevelAreas.Of([("west", new TurnedBox(new(-40, -40, -5), 0, new(50, 80, 20))), ("east", new TurnedBox(new(10, -40, -5), 0, new(30, 80, 20)))]);
        float HotFor(bool leave)
        {
            var r = Make(Floor(), new(0, 0, 0.01f), areas, tune: c => c.Set(c.Numbers with { HeatCoolSeconds = 6f }, true));
            Facing(r, "guard", 5, 0);
            Leap(r, after: 4.5f);   // alarmed about 4 s after the leap
            var rose = Assert.Single(r.Rose);
            Assert.Equal("west", areas.Name(rose.Area));
            if (leave) r.W.Get<Body>(r.Player) = Body.At(new(20, 0, 0.01f), 0f);
            Run(r, 8 * World.TicksPerSecond, ActionFrame.Idle);
            return Assert.Single(r.Gone).Seconds;
        }
        Assert.InRange(HotFor(leave: false), 5.5f, 6.5f);
        Assert.InRange(HotFor(leave: true), 2.0f, 3.2f);    // three times faster once the player is gone (half a second after the alarm)
        var n = PerceptionNumbers.Sandbox;
        Assert.Equal(Awareness.Aware, PerceptionSystem.Rung(0.25f, n));
        Assert.Equal(Awareness.Alerted, PerceptionSystem.Rung(0.25f, n, 1f - n.HeatWariness));   // a full heat halves the thresholds
    }

    [Fact]
    public void In_a_free_place_a_leap_raises_no_risk_and_so_is_seen_by_no_one()
    {
        var free = LevelPlaces.Of([new LevelPlace("yard", new TurnedBox(new(-10, -10, -2), 0f, new(20, 20, 10)), PlaceKind.Free)]);
        var r = Make(Floor(places: free), new(0, 0, 0.01f));
        var g = Facing(r, "guard", 4, 0);
        Run(r, 30, ActionFrame.Idle);
        Run(r, Full, ActionFrame.Idle with { Held = Buttons.Jump });
        Run(r, 3 * World.TicksPerSecond, ActionFrame.Idle);
        Assert.Empty(r.Risks);
        Assert.Equal(Awareness.Unaware, Highest(r, g));
    }

    // ---- data, determinism, allocation ----

    [Fact]
    public void Light_and_conceal_areas_are_read_from_a_manifest_the_smallest_deciding_and_a_bad_value_said()
    {
        static L2Area Area(string name, float x, float size, string? key = null, string? value = null) => new()
        {
            Name = name,
            Box = new L2AreaBox { Origin = [x, 0, 0], Size = [size, size, size] },
            Unknown = key is null ? null : new Dictionary<string, JsonElement> { [key] = JsonDocument.Parse(value!).RootElement.Clone() },
        };
        var manifest = new L2Manifest
        {
            Areas =
            [
                Area("square", 0, 20, "light", "0.6"), Area("alley", 2, 4, "light", "0.1"), Area("hedge", 10, 2, "conceal", "0.2"),
                Area("district", 0, 30), Area("bad", 25, 2, "conceal", "\"thick\""),
            ],
        };
        var areas = LevelAreas.From(manifest);
        Assert.Equal(0.1f, areas.LightAt(new(3, 1, 1), 0.3f));      // the alley inside the square
        Assert.Equal(0.6f, areas.LightAt(new(15, 1, 1), 0.3f));
        Assert.Equal(0.3f, areas.LightAt(new(-5, 1, 1), 0.3f));
        Assert.Equal(0.2f, areas.ConcealAt(new(11, 1, 1)));
        Assert.Equal(1f, areas.ConcealAt(new(3, 1, 1)));
        Assert.Equal(2, areas.Count);                               // the level and the district: the spots are not places
        Assert.Equal("district", areas.Name(areas.At(new(15, 1, 1))));
        Assert.Contains(areas.Problems, p => p.Contains("bad") && p.Contains("conceal"));
    }

    [Fact]
    public void The_overlay_outlines_each_light_and_conceal_area_on_the_ground_and_no_place_of_heat()
    {
        var areas = LevelAreas.Of([("square", new TurnedBox(new(-20, -20, -5), 0, new(40, 40, 20)))],
            lights: [(new TurnedBox(new(4, 4, -1), 0, new(6, 6, 8)), 0.1f)], conceals: [(new TurnedBox(new(-3, -3, -1), 0, new(3, 3, 4)), 0.2f)]);
        var r = Make(Floor(), new(0, 0, 0.01f), areas);
        var edges = new List<Presentation.AwarenessEdge>();
        Presentation.AwarenessView.Areas(r.W, edges);
        Assert.Equal(4 * 6, edges.Count(x => !x.Conceal));                 // the dark corner's four 6 m sides, a metre a piece
        Assert.Equal(4 * 3, edges.Count(x => x.Conceal));                  // the bush's
        Assert.All(edges, x => Assert.InRange(x.From.Z, 0.01f, 0.1f));    // on the floor, not at the boxes' feet
        Assert.All(edges, x => Assert.True(MathF.Abs(x.From.X) <= 10.01f && MathF.Abs(x.From.Y) <= 10.01f, "the square drawn"));
    }

    [Fact]
    public void The_same_inputs_give_the_same_digests_with_witnesses()
    {
        List<ulong> Digests()
        {
            var r = Make(Floor(), new(0, 0, 0.01f));
            Facing(r, "guard", 3, 0); Facing(r, "townsman", -3, 1); Facing(r, "drunk", 0, 4);
            var d = new List<ulong>();
            for (int i = 0; i < 6 * World.TicksPerSecond; i++)
            {
                Run(r, 1, i >= 30 && i < 30 + Full ? ActionFrame.Idle with { Held = Buttons.Jump } : ActionFrame.Idle);
                if (i % 30 == 0) d.Add(r.W.Digest());
            }
            Assert.NotEmpty(r.Changes);
            return d;
        }
        Assert.Equal(Digests(), Digests());
    }

    [Fact]
    public void A_witness_is_read_as_kind_at_x_y_z_and_yaw_and_refused_otherwise()
    {
        Assert.True(WitnessRequest.TryParse("guard@12.5,-3,0.2,180", out var w, out _));
        Assert.Equal(new WitnessRequest("guard", new(12.5f, -3, 0.2f), 180f), w);
        Assert.Equal("guard@12.5,-3,0.2,180", w.ToString());
        Assert.True(WitnessRequest.TryParse("oldman@1,2,3", out var o, out _));
        Assert.Equal(0f, o.YawDegrees);
        foreach (var bad in new[] { "guard", "@1,2,3", "guard@1,2", "guard@1,2,x", "guard@1,2,3,4,5" })
            Assert.False(WitnessRequest.TryParse(bad, out _, out string error), bad);
    }

    [Fact]
    public void A_tick_of_perception_and_reactions_with_an_act_in_view_allocates_nothing()
    {
        Allocations.None("a leap seen by three witnesses", () =>
        {
            var r = Make(Floor(), new(0, 0, 0.01f));
            Facing(r, "guard", 5, 0); Facing(r, "townsman", -4, 1); Facing(r, "monk", 0, 5);
            Run(r, 30, ActionFrame.Idle);
            Run(r, Full, ActionFrame.Idle with { Held = Buttons.Jump });   // the risk raised: the act in view
            Allocations.Warm(r.W);
            MovementServices.Step(r.W, ActionFrame.Idle);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < 100; t++) MovementServices.Step(r.W, ActionFrame.Idle);
            long spent = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.NotEmpty(r.W.Service<PerceptionState>().Acts.ToArray());
            return spent;
        });
    }
}
