// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Perception;
using Vitomir.Platform.Powers;
using Vitomir.Platform.Space;
using Xunit;

namespace Vitomir.Platform.Tests;

/// <summary>
/// People fight back (a play log: the
/// guard was struck five times and never answered): a guard struck draws on the one who struck it and swings; a townsman
/// struck runs; a guard who sees a blow struck at another joins; a fighter stands down when its foe is stopped, gives up and
/// searches when its foe is too far too long, and flees when Crippled and not brave enough; an arena's brain is never
/// re-aimed; the player stopped gets up after its seconds with its Health back (a sandbox convenience) and those fighting it
/// stand down; deterministic, the fight's state in digested components, nothing allocated per tick. On 1.1's own characters
/// (skipped without the install), on a floor, with contact, death, perception and the fights registered as the program does.
/// </summary>
public sealed class WorldFightTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);
    static readonly Dictionary<(string, string?), OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        lock (Loaded)
        {
            if (!Loaded.TryGetValue((name, weapon), out var c))
                Loaded[(name, weapon)] = c = Library.Value!.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    /// <summary>A world as the program makes it (contact, death, perception, the fights, the recovery) on a floor, and what it raised.</summary>
    sealed class Yard
    {
        public required World World { get; init; }
        public required Dictionary<string, int> Kinds { get; init; }
        public CharacterAssets Assets => World.Service<CharacterAssets>();
        public PerceptionConfig Config => World.Service<PerceptionConfig>();
        public readonly List<ContactMade> Contacts = [];
        public readonly List<AttackBegun> Begun = [];
        public readonly List<ReactionStarted> Reactions = [];
        public readonly List<AwarenessChanged> Awareness = [];
        public readonly List<ActorRecovered> Recovered = [];
        public readonly List<FightBegun> Begins = [];
        public readonly List<FightEnded> Ends = [];

        public void Step(ActionFrame frame, int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, frame);
                Contacts.AddRange(World.Events<ContactMade>().ThisTick);
                Begun.AddRange(World.Events<AttackBegun>().ThisTick);
                Reactions.AddRange(World.Events<ReactionStarted>().ThisTick);
                Awareness.AddRange(World.Events<AwarenessChanged>().ThisTick);
                if (World.TryService<EventQueue<ActorRecovered>>(out var r)) Recovered.AddRange(r.ThisTick);
                if (World.TryService<EventQueue<FightBegun>>(out var fb)) Begins.AddRange(fb.ThisTick);
                if (World.TryService<EventQueue<FightEnded>>(out var fe)) Ends.AddRange(fe.ThisTick);
            }
        }

        public void Idle(int ticks) => Step(ActionFrame.Idle, ticks);

        /// <summary>A character of <paramref name="kind"/> at (x, y) facing <paramref name="yawDegrees"/>, armed; the player when <paramref name="player"/>.</summary>
        public EntityId Put(string kind, float x, float y, float yawDegrees, bool player = false)
        {
            float yaw = yawDegrees * DetMath.DegToRad;
            var e = player ? MovementServices.SpawnControlled(World, new(x, y, 0.01f), yaw, Locomotion.Sandbox)
                           : MovementServices.SpawnBody(World, new(x, y, 0.01f), yaw, Locomotion.Sandbox);
            Draw(e, kind);
            return e;
        }

        /// <summary>A witness of perception kind <paramref name="kind"/> posted at (x, y), drawn as <paramref name="character"/> (armed as its 1.1 character is).</summary>
        public EntityId Witness(string kind, string character, float x, float y, float yawDegrees)
        {
            var e = PerceptionServices.Witness(World, new WitnessRequest(kind, new(x, y, 0.01f), yawDegrees), Config.IndexOf(kind), Locomotion.Sandbox);
            Draw(e, character);
            return e;
        }

        void Draw(EntityId e, string kind)
        {
            World.Set(e, CharacterAnimation.For(Assets, Kinds[kind]));
            World.Set(e, MotionInput.Standing);
            CombatServices.Arm(World, e);
        }

        /// <summary><paramref name="who"/> taps attack (a quick blow).</summary>
        public void Swing(EntityId who)
        {
            ref var intent = ref World.Get<CombatIntent>(who);
            intent.Aims = true;
            intent.AimPitch = 0f;
            intent.Attack = true;
            Idle(1);
            World.Get<CombatIntent>(who).Attack = false;
        }

        /// <summary>Stops <paramref name="who"/> as the rules do at zero Health (an <see cref="ActorDied"/>, as a test may stand in for the rules).</summary>
        public void Stop(EntityId who, EntityId by) =>
            World.Events<ActorDied>().Publish(new ActorDied(who, World.Tick, 0, by));

        public Observer O(EntityId e) => World.Get<Observer>(e);
    }

    static readonly string[] AllKinds = ["Christof", "rudolfGuard", "Civ12male"];

    /// <summary>The proving ground (<c>VITOMIR_COURSE_LEVEL</c>), for what needs routes: its navigation and its places.</summary>
    static readonly Lazy<(LevelSpace Level, LevelAreas Areas)?> Course = new(() =>
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        if (string.IsNullOrEmpty(path) || !File.Exists(Path.Combine(path, "level.json"))) return null;
        return (LevelSpace.Open(path), LevelAreas.Open(path));
    });

    /// <summary>On the proving ground: the free yard's open floor round the arena's ring (a free place), and the start square (public).</summary>
    static readonly Vector2 Yard0 = new(40, 3), Square0 = new(-20, 0);

    static Yard Make(ulong seed = 7, bool fights = true, Action<WorldBuilder>? more = null, float guardBravery = -1f, bool course = false, float guardSight = -1f)
    {
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>();
        foreach (var k in AllKinds) kinds[k] = OneOneLibrary.Register(assets, Character(k, k == "Christof" ? "BroadSword" : null));
        if (course) Assert.SkipUnless(Course.Value is not null, "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        var level = course ? Course.Value!.Value.Level : new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        PerceptionServices.Register(b, course ? Course.Value!.Value.Areas : null);
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        CombatTempos.Register(b);       // as the program's worlds have it (the enhanced module registers it)
        DeathServices.Register(b);
        ContactServices.Register(b, new ContactConfig());
        b.Component<Vitals>(digested: false);
        if (fights)
        {
            WorldFights.Register(b);
            RecoveryServices.Register(b);
        }
        more?.Invoke(b);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        var config = w.Service<PerceptionConfig>();
        config.Night = false;
        var guard = PerceptionTests.Guard;
        if (guardBravery >= 0f) guard.Bravery = guardBravery;
        if (guardSight >= 0f) guard.SightRange = guardSight;
        config.Add(new ObserverKind(AssetId.Of("test", "perception/guard"), "guard", [], guard, true));
        config.Add(new ObserverKind(AssetId.Of("test", "perception/townsman"), "townsman", [], PerceptionTests.Townsman, true));
        return new Yard { World = w, Kinds = kinds };
    }

    static float Across(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Y - b.Y).Length();

    // ---- struck ----

    [Fact]
    public void A_guard_struck_draws_on_the_one_who_struck_and_swings()
    {
        var y = Make();
        var a = y.Put("Christof", 0, 0, 0);
        var g = y.Witness("guard", "rudolfGuard", 1.4f, 0, 180);
        Assert.True(WorldFights.CanFight(y.World, g), "the guard holds no blade");
        y.Idle(30);
        y.Swing(a);
        for (int i = 0; i < Ticks(1f) && !y.Contacts.Any(c => c.Attacker == a); i++) y.Idle(1);
        var hit = Assert.Single(y.Contacts, c => c.Attacker == a);
        Assert.Equal(g, hit.Target);
        // Its reaction (FightReactSeconds): it fights at once but only turns to face him; then the brain.
        float react = y.Config.Numbers.FightReactSeconds;
        y.Idle(Ticks(react) - 2);
        Assert.Equal(Reaction.Fight, y.O(g).Doing);
        Assert.False(y.World.Has<DuelBrain>(g));
        Assert.Equal(RouteWalker.StandErrand, y.World.Get<RouteWalker>(g).Errand);
        y.Idle(Ticks(1f) - Ticks(react) + 2);
        var o = y.O(g);
        Assert.Equal(Reaction.Fight, o.Doing);
        Assert.Equal(Vitomir.Platform.Perception.Awareness.Alarmed, o.Level);
        Assert.Contains(y.Awareness, c => c.Observer == g && c.Cause == AwarenessCause.Struck);
        Assert.Equal(a, y.World.Get<DuelBrain>(g).Foe);
        Assert.Equal(a, y.World.Get<WorldFighter>(g).Foe);
        Assert.Equal(RouteWalker.AsideErrand, y.World.Get<RouteWalker>(g).Errand);
        Assert.Equal((g, a, FightCause.Struck), Assert.Single(y.Begins) is var fb ? (fb.Who, fb.Foe, fb.Cause) : default);
        y.Idle(Ticks(3f));
        var first = y.Begun.FirstOrDefault(x => x.Entity == g);
        Assert.True(first.Entity == g, "the guard never swung");
        float after = (first.Tick - hit.Tick) / (float)World.TicksPerSecond;
        output.WriteLine($"struck at {hit.Tick / 120f:0.00} s; fights at once, the brain after {react} s; first blow begun {after:0.00} s later");
        Assert.InRange(after, react, 3f);
    }

    [Fact]
    public void A_townsman_struck_runs_from_the_one_who_struck()
    {
        var y = Make(course: true);
        var a = y.Put("Christof", Yard0.X, Yard0.Y, 0);
        var t = y.Witness("townsman", "Civ12male", Yard0.X + 1.3f, Yard0.Y, 180);
        Assert.False(WorldFights.CanFight(y.World, t));
        y.Idle(30);
        y.Swing(a);
        y.Idle(Ticks(1f));
        Assert.Contains(y.Contacts, c => c.Attacker == a && c.Target == t);
        Assert.Equal(Reaction.Flee, y.O(t).Doing);
        Assert.False(y.World.Has<DuelBrain>(t));
        float before = Across(y.World.Get<Body>(t).Feet, y.World.Get<Body>(a).Feet);
        y.Idle(Ticks(4f));
        float now = Across(y.World.Get<Body>(t).Feet, y.World.Get<Body>(a).Feet);
        output.WriteLine($"the townsman {before:0.0} m off when struck, {now:0.0} m off 4 s later");
        Assert.True(now > before + 5f, $"the townsman did not run ({before:0.0} → {now:0.0} m)");
    }

    // ---- seen ----

    [Fact]
    public void Violence_seen_an_armed_guard_joins_the_fight_and_a_townsman_flees()
    {
        var y = Make(course: true);
        var (x, z) = (Square0.X, Square0.Y);
        var a = y.Put("Christof", x, z, 90);
        var victim = y.Put("Civ12male", x, z + 1.3f, -90);
        var g = y.Witness("guard", "rudolfGuard", x + 6, z + 2, 180);
        var t = y.Witness("townsman", "Civ12male", x - 5, z + 1, 0);
        y.Idle(30);
        y.Swing(a);
        int i = 0;
        for (; i < Ticks(10f) && y.O(g).Doing != Reaction.Fight; i++) y.Idle(1);
        var hit = Assert.Single(y.Contacts, c => c.Attacker == a);
        Assert.Equal(victim, hit.Target);
        output.WriteLine(string.Join("; ", y.Awareness.Select(x => $"{(x.Observer == g ? "guard" : "townsman")} {x.To} at {x.Tick / 120f:0.00} s")));
        output.WriteLine(string.Join("; ", y.Reactions.Select(x => $"{(x.Who == g ? "guard" : "townsman")} {x.Doing} at {x.Tick / 120f:0.00} s")));
        Assert.Equal(Reaction.Fight, y.O(g).Doing);
        Assert.Equal(a, y.World.Get<DuelBrain>(g).Foe);
        Assert.Contains(y.Begins, x => x.Who == g && x.Foe == a && x.Cause == FightCause.Joined);
        Assert.True(y.World.Has<DuelBrain>(g), "one who joins keeps the ladder's timing: no reaction delay");
        var joined = y.Reactions.First(x => x.Who == g && x.Doing == Reaction.Fight);
        var alerted = y.Awareness.First(x => x.Observer == g && x.To == Vitomir.Platform.Perception.Awareness.Alerted);
        Assert.True(joined.Tick >= alerted.Tick, "the guard joined before it made out what it saw");
        y.Idle(Ticks(6f));
        Assert.Contains(y.Reactions, x => x.Who == t && x.Doing == Reaction.Flee);
        Assert.Contains(y.Begun, x => x.Entity == g);
    }

    [Fact]
    public void A_keeper_fighting_is_helped_not_fought_and_a_stray_blow_is_let_pass()
    {
        var y = Make();
        var a = y.Put("Christof", 0, 0, 0);
        var g1 = y.Witness("guard", "rudolfGuard", 1.4f, 0, 180);
        var g2 = y.Witness("guard", "rudolfGuard", 18, 10, 0);     // facing away, beyond g1's shout: it knows nothing of it by itself
        y.Idle(30);
        y.Swing(a);
        y.Idle(Ticks(1f));
        Assert.Equal(Reaction.Fight, y.O(g1).Doing);
        Assert.NotEqual(Reaction.Fight, y.O(g2).Doing);
        // g2 made out a blow struck by g1 (who fights a): it joins against g1's foe.
        ref var o2 = ref y.World.Get<Observer>(g2);
        (o2.Level, o2.Value, o2.Act, o2.Suspect, o2.SuspectPlace, o2.SawActTick) =
            (Vitomir.Platform.Perception.Awareness.Alarmed, 1.5f, MasqueradeAct.Violence, g1, y.World.Get<Body>(g1).Feet, (int)y.World.Tick);
        y.Idle(2);
        Assert.Equal(Reaction.Fight, y.O(g2).Doing);
        Assert.Equal(a, y.World.Get<WorldFighter>(g1).Foe);
        Assert.Equal(a, y.World.Get<WorldFighter>(g2).Foe);
        // A blow from g1 meeting g2 (a stray, g1 fights a) changes nobody's foe.
        y.World.Get<Observer>(g2).Struck = 1;
        y.World.Get<Observer>(g2).StruckBy = g1;
        y.Idle(2);
        Assert.Equal(a, y.World.Get<WorldFighter>(g2).Foe);
        Assert.Equal(Reaction.Fight, y.O(g2).Doing);
    }

    // ---- the end of a fight ----

    [Fact]
    public void The_fighter_stands_down_when_its_foe_is_stopped_and_joins_nothing_over_what_it_saw_before()
    {
        var y = Make();
        var a = y.Put("Christof", 0, 0, 0);
        var g = y.Witness("guard", "rudolfGuard", 1.4f, 0, 180);
        y.Idle(30);
        y.Swing(a);
        y.Idle(Ticks(1.5f));
        Assert.Equal(Reaction.Fight, y.O(g).Doing);
        y.Stop(a, g);
        y.Idle(2);
        Assert.True(y.World.Has<Dead>(a));
        Assert.Equal(Reaction.Watch, y.O(g).Doing);
        Assert.False(y.World.Has<DuelBrain>(g));
        Assert.False(y.World.Has<WorldFighter>(g));
        Assert.NotEqual(0, y.O(g).StoodDownTick);
        Assert.Contains(y.Ends, x => x.Who == g && x.Foe == a && x.Why == FightEnd.FoeDown);
        Assert.False(y.World.Get<CombatIntent>(g).Attack);
        // Still alarmed, it watches; with no new act it does not take up the fight again.
        y.Idle(Ticks(5f));
        Assert.Equal(Vitomir.Platform.Perception.Awareness.Alarmed, y.O(g).Level);
        Assert.Equal(Reaction.Watch, y.O(g).Doing);
        Assert.DoesNotContain(y.Reactions, x => x.Who == g && x.Doing is Reaction.RunTo or Reaction.Challenge);
    }

    [Fact]
    public void The_fighter_gives_up_and_searches_where_it_last_had_its_foe_when_the_foe_is_too_far_too_long()
    {
        var y = Make(guardSight: 200f);     // it sees them all along: the distance alone gives it up
        var a = y.Put("Christof", 0, 0, 0);
        var g = y.Witness("guard", "rudolfGuard", 1.4f, 0, 180);
        y.Idle(30);
        y.Swing(a);
        y.Idle(Ticks(1f));
        Assert.Equal(Reaction.Fight, y.O(g).Doing);
        // The foe gets away: kept 30 m west of the guard (a test may stand in for a foe that outruns it).
        var n = y.Config.Numbers;
        long far = -1;
        Vector3 last = default;
        for (int i = 0; i < Ticks(20f) && y.O(g).Doing == Reaction.Fight; i++)
        {
            var gf = y.World.Get<Body>(g).Feet;
            y.World.Get<Body>(a).Feet = new Vector3(gf.X - 30f, gf.Y, y.World.Get<Body>(a).Feet.Z);
            y.Idle(1);
            if (far < 0 && Across(y.World.Get<Body>(g).Feet, y.World.Get<Body>(a).Feet) > n.GiveUpMetres) { far = y.World.Tick; last = y.World.Get<WorldFighter>(g).LastPlace; }
        }
        Assert.True(far > 0, "the foe never got far");
        var o = y.O(g);
        float waited = (y.World.Tick - far) / (float)World.TicksPerSecond;
        output.WriteLine($"the foe beyond {n.GiveUpMetres} m from {far / 120f:0.00} s; the guard gave up {waited:0.00} s later and searches about {o.Place.X:0.0} {o.Place.Y:0.0}");
        Assert.Equal(Reaction.Search, o.Doing);
        Assert.Contains(y.Ends, x => x.Who == g && x.Why == FightEnd.GaveUp);
        Assert.InRange(waited, n.GiveUpSeconds - 0.5f, n.GiveUpSeconds + 0.5f);
        Assert.False(y.World.Has<DuelBrain>(g));
        Assert.True(Across(o.Place, last) < 3.5f, $"it searches about {o.Place}, not where it last had them ({last})");
        Assert.NotEqual(RouteWalker.AsideErrand, y.World.Get<RouteWalker>(g).Errand);
    }

    [Fact]
    public void The_fighter_gives_up_and_searches_where_it_last_saw_its_foe_when_the_foe_is_out_of_sight_too_long()
    {
        var y = Make();
        y.Config.Night = true;          // a guard sees a running body some 12 m off at night
        var a = y.Put("Christof", 0, 0, 0);
        var g = y.Witness("guard", "rudolfGuard", 1.4f, 0, 180);
        y.Idle(30);
        y.Swing(a);
        y.Idle(Ticks(1f));
        Assert.Equal(Reaction.Fight, y.O(g).Doing);
        // The foe gets away into the dark: kept 18 m off (within the 25 m, beyond its sight).
        var n = y.Config.Numbers;
        long gone = y.World.Tick;
        for (int i = 0; i < Ticks(14f) && y.O(g).Doing == Reaction.Fight; i++)
        {
            var gf = y.World.Get<Body>(g).Feet;
            y.World.Get<Body>(a).Feet = new Vector3(gf.X - 18f, gf.Y, y.World.Get<Body>(a).Feet.Z);
            y.Idle(1);
        }
        var o = y.O(g);
        float waited = (y.World.Tick - gone) / (float)World.TicksPerSecond;
        output.WriteLine($"the foe out of sight from {gone / 120f:0.00} s; the guard gave up {waited:0.00} s later and searches about {o.Place.X:0.0} {o.Place.Y:0.0}");
        Assert.Equal(Reaction.Search, o.Doing);
        Assert.Contains(y.Ends, x => x.Who == g && x.Why == FightEnd.GaveUp);
        Assert.InRange(waited, n.GiveUpUnseenSeconds - 0.5f, n.GiveUpUnseenSeconds + 0.5f);
        Assert.False(y.World.Has<DuelBrain>(g));
        Assert.True(Across(o.Place, Vector3.Zero) < 2.5f, $"it searches about {o.Place}, not where it last saw them (about the origin)");
    }

    [Fact]
    public void A_crippled_fighter_flees_unless_brave_enough()
    {
        foreach (var (bravery, flees) in new[] { (0.5f, true), (0.9f, false) })
        {
            var y = Make(guardBravery: bravery, course: true);
            var a = y.Put("Christof", Yard0.X, Yard0.Y, 0);
            var g = y.Witness("guard", "rudolfGuard", Yard0.X + 1.4f, Yard0.Y, 180);
            y.Idle(30);
            y.Swing(a);
            y.Idle(Ticks(1f));
            Assert.Equal(Reaction.Fight, y.O(g).Doing);
            y.World.Set(g, new Vitals { Health = 8, HealthMax = 100, Band = HealthBand.Crippled });
            y.Idle(2);
            output.WriteLine($"bravery {bravery}: {y.O(g).Doing}");
            Assert.Equal(flees ? Reaction.Flee : Reaction.Fight, y.O(g).Doing);
            Assert.Equal(!flees, y.World.Has<DuelBrain>(g));
            Assert.Equal(flees, y.Ends.Any(x => x.Who == g && x.Why == FightEnd.Crippled));
            if (!flees) continue;
            y.Idle(Ticks(3f));
            Assert.Equal(Reaction.Flee, y.O(g).Doing);     // a keeper fleeing goes on fleeing while alarmed
            Assert.True(Across(y.World.Get<Body>(g).Feet, y.World.Get<Body>(a).Feet) > 4f);
        }
    }

    // ---- the arena's brains ----

    [Fact]
    public void A_brain_perception_did_not_put_on_is_never_re_aimed()
    {
        var y = Make();
        var a = y.Put("Christof", 0, 0, 0);
        var g = y.Witness("guard", "rudolfGuard", 1.4f, 0, 180);
        var other = y.Put("Civ12male", 10, 10, 0);
        var book = y.World.Service<DuelBrainBook>();
        y.World.Set(g, DuelBrain.For(BrainKind.Stand, other, book.Add(new DuelBrainSettings()), 1f, 1f));
        y.Idle(30);
        y.Swing(a);
        y.Idle(Ticks(1f));
        Assert.Contains(y.Contacts, c => c.Attacker == a && c.Target == g);
        Assert.Equal(other, y.World.Get<DuelBrain>(g).Foe);
        Assert.Equal(BrainKind.Stand, y.World.Get<DuelBrain>(g).Kind);
        Assert.False(y.World.Has<WorldFighter>(g));
        Assert.NotEqual(Reaction.Fight, y.O(g).Doing);
    }

    [Fact]
    public void A_world_without_fights_registered_has_none_its_struck_flee()
    {
        var y = Make(fights: false);
        var a = y.Put("Christof", 0, 0, 0);
        var g = y.Witness("guard", "rudolfGuard", 1.4f, 0, 180);
        y.Idle(30);
        y.Swing(a);
        y.Idle(Ticks(1f));
        Assert.Contains(y.Contacts, c => c.Attacker == a && c.Target == g);
        Assert.False(y.World.TryStore<DuelBrain>(out _));
        Assert.Equal(Reaction.Flee, y.O(g).Doing);
    }

    // ---- the player's stop and the sandbox recovery ----

    /// <summary>The module's sheets as the recovery uses them: Health set and read (a test stands in for the module).</summary>
    sealed class Sheets : IPersonSheets
    {
        public readonly Dictionary<EntityId, int> Health = [];
        public IReadOnlyList<string> Templates => [];
        public bool TryTemplate(EntityId person, string templateId, out string error) { error = "no"; return false; }
        public bool TryImport(EntityId person, string sheetText, out string error) { error = "no"; return false; }
        public bool TrySet(EntityId person, string traitId, int value, out string error)
        {
            error = "";
            if (traitId != "health") { error = "no"; return false; }
            Health[person] = value;
            return true;
        }
        public bool TryLearn(EntityId person, string powerId, int rank, out string error) { error = "no"; return false; }
        public bool TryRead(EntityId person, string id, out int value) => Health.TryGetValue(person, out value) && id == "health";
        public string Give(EntityId person, string text, string source) => "";
        public bool TryDescribe(EntityId person, out string json) { json = ""; return false; }
    }

    [Fact]
    public void The_player_stopped_lies_still_then_gets_up_with_its_health_back_and_those_fighting_it_stand_down()
    {
        var sheets = new Sheets();
        var y = Make(more: b => b.Service<IPersonSheets>(sheets));
        var p = y.Put("Christof", 0, 0, 0, player: true);
        var g = y.Witness("guard", "rudolfGuard", 1.4f, 0, 180);
        RecoveryServices.Recover(y.World, p, 5f);
        y.World.Set(p, new Vitals { Health = 0, HealthMax = 100, HealthCap = 80, Band = HealthBand.Stopped });
        y.Idle(30);
        ref var og = ref y.World.Get<Observer>(g);
        (og.Level, og.Value, og.Act, og.Suspect) = (Vitomir.Platform.Perception.Awareness.Alarmed, 1.5f, MasqueradeAct.Violence, p);
        WorldFights.Begin(y.World, g, p, y.Config.IndexOf("guard"));
        og.Doing = Reaction.Fight;
        y.Idle(Ticks(1f));
        y.Stop(p, g);
        y.Idle(2);
        long stopped = y.World.Get<Dead>(p).Tick;
        Assert.Equal(Reaction.Watch, y.O(g).Doing);
        Assert.False(y.World.Has<DuelBrain>(g));
        // Stopped: its control does nothing.
        var at = y.World.Get<Body>(p).Feet;
        var forward = new ActionFrame { Move = new Vector2(0f, 1f) };
        y.Step(forward, Ticks(3f));
        Assert.True(Across(y.World.Get<Body>(p).Feet, at) < 0.05f, "a stopped player moved");
        Assert.Empty(y.Recovered);
        // After its seconds it gets up (its stop played back), Health given back to its effective maximum, then moves again.
        y.Step(forward, Ticks(2.5f));
        Assert.Equal(80, sheets.Health[p]);
        int i = 0;
        for (; i < Ticks(4f) && y.Recovered.Count == 0; i++) y.Step(forward);
        var r = Assert.Single(y.Recovered);
        output.WriteLine($"stopped at {stopped / 120f:0.00} s; Health given back at {stopped / 120f + 5f:0.00} s; standing at {r.Tick / 120f:0.00} s, Health {r.Health}");
        Assert.Equal((p, stopped, 80), (r.Who, r.StoppedTick, r.Health));
        Assert.False(y.World.Has<Dead>(p));
        y.Step(forward, Ticks(1.5f));
        Assert.True(Across(y.World.Get<Body>(p).Feet, at) > 0.5f, "the player did not move once up");
        Assert.False(ActionRoles.IsDeath(y.World.Get<CharacterAnimation>(p).Action));
        // The guard, stood down, does not take the fight up again for the player getting up.
        Assert.NotEqual(Reaction.Fight, y.O(g).Doing);
    }

    [Fact]
    public void Without_recovery_the_player_stays_down()
    {
        var y = Make();
        var p = y.Put("Christof", 0, 0, 0, player: true);
        RecoveryServices.Recover(y.World, p, 0f);
        y.Idle(30);
        y.Stop(p, EntityId.None);
        y.Idle(Ticks(12f));
        Assert.True(y.World.Has<Dead>(p));
        Assert.Empty(y.Recovered);
    }

    [Fact]
    public void A_severed_neck_or_trunk_is_not_got_up_from()
    {
        var y = Make();
        var p = y.Put("Christof", 0, 0, 0, player: true);
        RecoveryServices.Recover(y.World, p, 5f);
        y.Idle(30);
        y.World.Events<ActorDied>().Publish(new ActorDied(p, y.World.Tick, 0, EntityId.None, HarmKind.Lethal, ActorStop.SeveredStop));
        y.Idle(Ticks(12f));
        Assert.True(y.World.Has<Dead>(p));
        Assert.Empty(y.Recovered);
    }

    // ---- determinism, the state, allocation ----

    static ulong Fought(ulong seed, int ticks)
    {
        var y = Make(seed);
        var a = y.Put("Christof", 0, 0, 0);
        y.Witness("guard", "rudolfGuard", 1.4f, 0, 180);
        y.Witness("townsman", "Civ12male", -1.3f, 0.3f, 0);
        y.Idle(30);
        y.Swing(a);
        y.Idle(ticks);
        return y.World.Digest();
    }

    [Fact]
    public void A_fight_is_deterministic()
    {
        Assert.Equal(Fought(3, Ticks(6f)), Fought(3, Ticks(6f)));
    }

    [Fact]
    public void A_fight_is_held_in_digested_components_only_so_a_save_of_them_resumes_it()
    {
        // What a save carries of a fight: the observer (its reaction and foe), the fighter's record and its brain. Taken out
        // and put back mid-fight (as a load would), the fight goes on exactly as one never interrupted.
        Yard Mid()
        {
            var y = Make(5);
            var a = y.Put("Christof", 0, 0, 0);
            y.Witness("guard", "rudolfGuard", 1.4f, 0, 180);
            y.Idle(30);
            y.Swing(a);
            y.Idle(Ticks(2f));
            return y;
        }
        var one = Mid();
        var two = Mid();
        var g = two.World.Store<WorldFighter>().Entities[0];
        Assert.Equal(Reaction.Fight, two.O(g).Doing);
        var (o, f, brain) = (two.World.Get<Observer>(g), two.World.Get<WorldFighter>(g), two.World.Get<DuelBrain>(g));
        two.World.Remove<Observer>(g); two.World.Remove<WorldFighter>(g); two.World.Remove<DuelBrain>(g);
        two.World.Set(g, o); two.World.Set(g, f); two.World.Set(g, brain);
        Assert.Equal(one.World.Digest(), two.World.Digest());
        one.Idle(Ticks(3f));
        two.Idle(Ticks(3f));
        Assert.Equal(one.World.Digest(), two.World.Digest());
        Assert.Equal(Reaction.Fight, two.O(g).Doing);
    }

    [Fact]
    public void Nothing_is_allocated_per_tick_once_a_fight_runs()
    {
        var y = Make();
        var a = y.Put("Christof", 0, 0, 0);
        var g = y.Witness("guard", "rudolfGuard", 1.4f, 0, 180);
        y.Witness("townsman", "Civ12male", -6f, 2f, 0);
        y.Idle(30);
        y.Swing(a);
        y.Idle(Ticks(1.5f));
        Assert.Equal(Reaction.Fight, y.O(g).Doing);
        var w = y.World;
        ISystem[] systems = [new WorldFightSystem(), new ReactionSystem(), new DuelBrainSystem(), new RouteSystem(), new RecoverySystem()];
        var queues = Allocations.Warm(w);
        foreach (var s in systems) s.Run(w);
        Allocations.None("a fight's systems", () =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < 240; t++)
            {
                foreach (var q in queues) q.TurnOver();
                foreach (var s in systems) s.Run(w);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
    }

    // ---- the rule ----

    [Fact]
    public void Stood_down_it_watches_while_it_calms_and_a_keeper_fleeing_goes_on_fleeing()
    {
        var n = PerceptionNumbers.Sandbox;
        var violence = PerceptionNumbers.SandboxActs[(int)MasqueradeAct.Violence];
        foreach (var level in new[] { Vitomir.Platform.Perception.Awareness.Alerted, Vitomir.Platform.Perception.Awareness.Alarmed })
        {
            Assert.Equal(Reaction.Watch, ReactionSystem.Decide(level, PerceptionTests.Guard, violence, n, Reaction.Watch, 0f, 1f, false, stoodDown: true));
            Assert.Equal(Reaction.Watch, ReactionSystem.Decide(level, PerceptionTests.Townsman, violence, n, Reaction.Watch, 0f, 1f, false, stoodDown: true));
        }
        Assert.Equal(Reaction.Routine, ReactionSystem.Decide(Vitomir.Platform.Perception.Awareness.Unaware, PerceptionTests.Guard, violence, n, Reaction.Watch, stoodDown: true));
        Assert.Equal(Reaction.Flee, ReactionSystem.Decide(Vitomir.Platform.Perception.Awareness.Alarmed, PerceptionTests.Guard, violence, n, Reaction.Flee));
        Assert.Equal(Reaction.RunTo, ReactionSystem.Decide(Vitomir.Platform.Perception.Awareness.Alarmed, PerceptionTests.Guard, violence, n, Reaction.Watch));
    }
}
