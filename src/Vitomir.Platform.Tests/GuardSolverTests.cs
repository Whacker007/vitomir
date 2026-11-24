// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Reflection;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The guard's arm solver: the survey of the earlier build's composed guard (how far its blade passes from
/// each blow), the coverage with the solver on and off (each blow from the front and 45° and 70° to the side, a held guard and a
/// parry pressed at its best time, 20 blows a cell), the intercept chosen (the soonest crossing it can reach), the hand's speed
/// limiting it, an unseen threat ignored, the give springing back to nothing, a load mid-solve going on the same, nothing allocated
/// per tick. Every test skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class GuardSolverTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static OneOneLibrary Lib()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        return Library.Value!;
    }

    static readonly Dictionary<(string, string?), OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null)
    {
        var lib = Lib();
        lock (Loaded)
        {
            if (!Loaded.TryGetValue((name, weapon), out var c))
                Loaded[(name, weapon)] = c = lib.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    /// <summary>The rules' stand-in: every guard contact held (or parried in the window).</summary>
    sealed class StandInGuard : ISystem
    {
        public void Run(World world)
        {
            var made = world.Events<ContactMade>().ThisTick;
            for (int i = 0; i < made.Count; i++)
            {
                var c = made[i];
                if (c.Guard is GuardMeet.None or GuardMeet.Clash) continue;
                var v = c.Guard == GuardMeet.Parry ? GuardVerdict.Parried : GuardVerdict.Held;
                world.Events<GuardResolved>().Publish(new GuardResolved(c.ContactId, c.Attacker, c.Target, c.Tick, c.Met, c.Guard, v, 1, 1, 1, 0.5f, 0.5f, 0.1, 0.1,
                    1f, 0f, v == GuardVerdict.Parried ? 0.3f : 0f, 0f));
            }
        }
    }

    sealed class Ring
    {
        public required World World { get; init; }
        public required Dictionary<string, int> Kinds { get; init; }
        public CharacterAssets Assets => World.Service<CharacterAssets>();
        public readonly List<ContactMade> Contacts = [];
        public readonly List<AttackBegun> Begun = [];
        public readonly List<ParryBegun> Parries = [];
        public readonly List<GuardSolverFollows> Follows = [];
        public readonly List<GuardGave> Gave = [];
        public Action<Ring>? EachTick;

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                Contacts.AddRange(World.Events<ContactMade>().ThisTick);
                Begun.AddRange(World.Events<AttackBegun>().ThisTick);
                Parries.AddRange(World.Events<ParryBegun>().ThisTick);
                Follows.AddRange(World.Events<GuardSolverFollows>().ThisTick);
                Gave.AddRange(World.Events<GuardGave>().ThisTick);
                EachTick?.Invoke(this);
            }
        }

        public EntityId Put(string kind, float x, float y, float yawDegrees)
        {
            var e = MovementServices.SpawnBody(World, new(x, y, 0.01f), yawDegrees * DetMath.DegToRad, Locomotion.Sandbox);
            World.Set(e, CharacterAnimation.For(Assets, Kinds[kind]));
            World.Set(e, MotionInput.Standing);
            CombatServices.Arm(World, e);
            return e;
        }

        public ref CombatIntent Intent(EntityId e) => ref World.Get<CombatIntent>(e);
        public ref Combatant Fighter(EntityId e) => ref World.Get<Combatant>(e);
        public ref CharacterAnimation Anim(EntityId e) => ref World.Get<CharacterAnimation>(e);
        public ref PoseRig Rig(EntityId e) => ref World.Get<PoseRig>(e);

        /// <summary><paramref name="who"/> presses attack, aimed <paramref name="aimDegrees"/>: a tap (a quick blow) or held 0.4 s (the strong one); the blow begins on the release.</summary>
        public void Swing(EntityId who, float aimDegrees = 0f, bool strong = false)
        {
            ref var intent = ref Intent(who);
            intent.Aims = true;
            intent.AimPitch = aimDegrees * DetMath.DegToRad;
            intent.Attack = true;
            Step(strong ? Ticks(0.4f) : 1);
            Intent(who).Attack = false;
            Step();
        }
    }

    static readonly (string Key, string Name, string? Weapon)[] AllKinds =
        [("Christof", "Christof", "BroadSword"), ("ChristofRapier", "Christof", "Rapier"), ("rudolfGuard", "rudolfGuard", null), ("rudolfGuardRapier", "rudolfGuard", "Rapier"), ("Civ12male", "Civ12male", null)];

    static Ring Make(bool solver = true, bool rules = true, Action<GuardSettings>? guard = null, ulong seed = 7, bool perception = false, bool brains = false, bool skills = false)
    {
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>();
        foreach (var (key, name, weapon) in AllKinds) kinds[key] = OneOneLibrary.Register(assets, Character(name, weapon));
        var level = new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        var g = b.Peek.Service<GuardSettings>();
        g.RulesDecide = rules;
        g.Solver = solver;
        guard?.Invoke(g);
        b.Service(new HurtRule { OnContact = false });
        b.Event<HarmResolved>();
        ContactServices.Register(b, new ContactConfig());
        if (rules) b.System(Phase.Resolve, 95, "test.stand-in-guard", new StandInGuard());
        if (brains) DuelBrains.Register(b);
        if (skills) GuardSkills.Register(b);   // the guard by skill
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        return new Ring { World = w, Kinds = kinds };
    }

    /// <summary>A blow: quick 1 (the forehand), quick 2 (the low backhand) or the strong whirl.</summary>
    public enum Blow { Quick1, Quick2, Strong }

    /// <summary>What one blow met first, and how far the guarding blade's line passed from the blow's blade before then (m; the survey).</summary>
    readonly record struct Outcome(string Met, bool AtGuard, bool Parry, float Miss, Vector3 GuardMiddle, Vector3 BlowTip, int Follows, float SolverSeconds, int Solved, float Margin = float.NaN);

    /// <summary>
    /// One blow by Christof (the broadsword) at a defender <paramref name="distance"/> m away at <paramref name="bearing"/>° from the
    /// defender's facing (left positive), aimed <paramref name="aim"/>°; the defender holds his guard from before (<paramref name="parryLead"/>
    /// null) or presses it <paramref name="parryLead"/> s before the blow's Strike mark (its parry window opening then).
    /// </summary>
    Outcome One(string defender, Blow blow, float bearing, float distance, float aim, float? parryLead, bool solver, GuardSkill? skill = null)
    {
        var r = Make(solver: solver, skills: skill is not null);
        var d = r.Put(defender, 0, 0, 90);
        if (skill is { } sk) r.World.Set(d, sk);   // the defender's guard skill
        float at = (90f + bearing) * DetMath.DegToRad;
        var a = r.Put(Attacker, MathF.Cos(at) * distance, MathF.Sin(at) * distance, 90f + bearing + 180f);
        r.Step(30);
        if (parryLead is null) { r.Intent(d).Block = true; r.Step(30); }
        r.Fighter(a).NextVariant = blow == Blow.Quick2 ? 1 : 0;
        // The time from the blow's beginning to its active window and to its Strike mark (its clip's, at its rate).
        float windowAt = 0f, strikeAt = 0f;
        {
            var set = r.Assets.Kind(r.Anim(a).Kind).Actions!;
            var motion = blow == Blow.Strong ? set.For(ActionRole.AttackStrong) : set.For(ActionRole.AttackQuick, blow == Blow.Quick2 ? 1 : 0);
            var win = r.World.Service<ActiveWindows>().For(r.Assets, r.Anim(a).Kind, motion.Clip, motion.StrikeSeconds, r.World.Service<ContactConfig>().Window);
            windowAt = win.From / MathF.Max(1e-3f, motion.Rate);
            strikeAt = motion.StrikeSeconds / MathF.Max(1e-3f, motion.Rate);
        }
        float miss = float.MaxValue;
        Vector3 gm = default, bt = default;
        double solverSeconds = 0; int solved = 0;
        var solverSystem = SolverOf(r.World);
        r.EachTick = x =>
        {
            if (x.Contacts.Count > 0) return;
            if (!ActionRoles.IsAttack(x.Anim(a).Action)) return;
            if (solverSystem is not null && solverSystem.LastSolved > 0) { solverSeconds += solverSystem.LastSeconds; solved += solverSystem.LastSolved; }
            if (!HeldItems.Blade(x.World, d, out var gb) || !HeldItems.Blade(x.World, a, out var ab)) return;
            float m = SegmentDistance(gb.Guard, gb.Tip, ab.Guard, ab.Tip, out _, out _) - gb.Radius - ab.Radius;
            if (m < miss) { miss = m; gm = (gb.Guard + gb.Tip) * 0.5f; bt = ab.Tip; }
        };
        if (parryLead is { } lead)
        {
            int press = Ticks(strikeAt - lead);   // ticks after the blow begins: the press so long before its Strike mark (as the brain times it)
            r.Swing(a, aim, blow == Blow.Strong);
            if (press > 0) r.Step(press - 1);
            r.Intent(d).Block = true;
        }
        else r.Swing(a, aim, blow == Blow.Strong);
        if (Trace) r.EachTick += x =>
        {
            ref var rig = ref x.Rig(d);
            if (x.World.Tick == 64)
            {
                var kd = x.Assets.Kind(x.Anim(d).Kind);
                var bones = x.World.Service<CharacterPoses>().Bones(x.World, d);
                GuardSolver.WeaponArm(x.Assets.ModelOf(x.Anim(d).Kind).Skeleton, kd, kd.Limbs!, out var arm, out _);
                output.WriteLine($"  posed: shoulder {bones[arm.Upper].Translation} elbow {bones[arm.Lower].Translation} wrist {bones[arm.End].Translation} root {bones[kd.Limbs!.Root].Translation} twist {x.Anim(d).GuardTwist} aim {x.Rig(d).AimPitch} action {x.Anim(d).Action} t {x.Anim(d).ActionTime}");
            }
            if (rig.GuardHandWeight > 0f && x.World.Tick % 6 == 0 && HeldItems.Blade(x.World, d, out var bl))
                output.WriteLine($"  t{x.World.Tick} a{x.Anim(a).ActionTime:0.000} w {rig.GuardHandWeight:0.00} hand {rig.GuardHand} posed guard {bl.Guard} off by {(bl.Guard - rig.GuardHand).Length():0.000}; blade {rig.GuardBlade} posed {Vector3.Normalize(bl.Tip - bl.Guard)} c{x.Contacts.Count} aim {x.Rig(d).AimPitch:0.00} twist {x.Anim(d).GuardTwist:0.00}");
        };
        r.Step(Ticks(1.6f));
        if (Trace) foreach (var f in r.Follows) output.WriteLine($"  follows: lead {f.Lead:0.000} margin {f.Margin:0.000}");
        var c = r.Contacts.FirstOrDefault();
        string got = r.Contacts.Count == 0 ? "nothing" : c.Met == ContactMet.Body ? c.Site! : c.Met.ToString();
        bool guardMet = r.Contacts.Count > 0 && c.Target == d && c.Met is ContactMet.Weapon or ContactMet.Shield;
        return new Outcome(got, guardMet, guardMet && c.Guard == GuardMeet.Parry, miss, gm, bt, r.Follows.Count, (float)solverSeconds, solved, r.Follows.Count > 0 ? r.Follows[0].Margin : float.NaN);
    }

    static GuardSolverSystem? SolverOf(World world) => world.TryService<GuardSolverSystem>(out var s) ? s : null;

    /// <summary>The least distance between two segments, and the parameters (0 to 1) of their nearest points.</summary>
    static float SegmentDistance(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1, out float s, out float t)
    {
        var d1 = p1 - p0; var d2 = q1 - q0; var r = p0 - q0;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        if (a <= 1e-9f && e <= 1e-9f) { s = t = 0; return (p0 - q0).Length(); }
        if (a <= 1e-9f) { s = 0; t = Math.Clamp(f / e, 0, 1); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= 1e-9f) { t = 0; s = Math.Clamp(-c / a, 0, 1); }
            else
            {
                float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                s = denom != 0 ? Math.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0) { t = 0; s = Math.Clamp(-c / a, 0, 1); }
                else if (t > 1) { t = 1; s = Math.Clamp((b - c) / a, 0, 1); }
            }
        }
        return ((p0 + d1 * s) - (q0 + d2 * t)).Length();
    }

    static IEnumerable<(float Aim, float Distance, float Bearing)> Cases(float bearing)
    {
        if (bearing == 0f)
        {
            foreach (var aim in new[] { -25f, -20f, -15f, -10f, -5f, 0f, 5f, 10f, 15f, 20f })
                foreach (var dist in new[] { 1.2f, 1.5f })
                    yield return (aim, dist, 0f);
        }
        else
            foreach (var aim in new[] { -25f, -12f, 0f, 12f, 25f })
                foreach (var dist in new[] { 1.2f, 1.5f })
                    foreach (var side in new[] { 1f, -1f })
                        yield return (aim, dist, bearing * side);
    }

    /// <summary>
    /// The survey and the coverage (1 and 3): each blow from the front and 45° and 70° to the side (20 blows a cell: ten
    /// aims at 1.2 and 1.5 m in front; five aims, two distances, both sides at the side), against Christof's lone broadsword guard
    /// held from before, with the solver off (the earlier build's composed guard) and on; and pressed as a parry at the best of four leads before the
    /// blow's Strike mark. Written
    /// out: the met count and the composed guard's miss distances. Run alone (about two minutes): it is the measurement.
    /// </summary>
    [Fact]
    public void Coverage_of_each_blow_from_the_front_and_the_sides_held_and_parried_solver_off_and_on()
    {
        string defender = Environment.GetEnvironmentVariable("VITOMIR_GUARD_DEFENDER") ?? "Christof";
        var lines = new List<string> { $"defender {defender}; attacker {Attacker}; 20 blows a cell" };
        int offHeldAll = 0, onHeldAll = 0;
        var blows = (Environment.GetEnvironmentVariable("VITOMIR_GUARD_BLOWS") ?? "Quick1,Quick2,Strong").Split(',').Select(Enum.Parse<Blow>).ToArray();
        foreach (var blow in blows)
            foreach (var bearing in new[] { 0f, 45f, 70f })
            {
                var misses = new List<float>();
                int offHeld = 0, onHeld = 0;
                var where = new System.Text.StringBuilder();
                foreach (var (aim, dist, bear) in Cases(bearing))
                {
                    var off = One(defender, blow, bear, dist, aim, null, solver: false);
                    var on = One(defender, blow, bear, dist, aim, null, solver: true);
                    if (off.AtGuard) offHeld++;
                    if (on.AtGuard) onHeld++;
                    if (!off.AtGuard && off.Miss < float.MaxValue) misses.Add(off.Miss);
                    where.Append($"[{aim:+0;-0}° {dist} {bear:+0;-0}: off {off.Met} miss {off.Miss:0.00}; on {on.Met} {on.Margin:0.00} {on.Miss:0.00}] ");
                }
                // The parry: pressed 0.15, 0.25, 0.35 or 0.45 s before the blow's Strike mark (the brain presses at 0.25); the best lead kept.
                int bestParry = -1, bestParryOff = -1; float bestLead = 0f, bestLeadOff = 0f;
                foreach (var lead in Environment.GetEnvironmentVariable("VITOMIR_GUARD_PARRY") == "0" ? [] : new[] { 0.15f, 0.25f, 0.35f, 0.45f })
                {
                    int p = 0, pOff = 0;
                    foreach (var (aim, dist, bear) in Cases(bearing))
                    {
                        if (One(defender, blow, bear, dist, aim, lead, solver: true).Parry) p++;
                        if (One(defender, blow, bear, dist, aim, lead, solver: false).Parry) pOff++;
                    }
                    if (p > bestParry) { bestParry = p; bestLead = lead; }
                    if (pOff > bestParryOff) { bestParryOff = pOff; bestLeadOff = lead; }
                }
                misses.Sort();
                string med = misses.Count == 0 ? "-" : $"{misses[misses.Count / 2]:0.00} m (least {misses[0]:0.00}, most {misses[^1]:0.00})";
                lines.Add($"{blow,-6} {bearing,2}°: held off {offHeld,2}/20, on {onHeld,2}/20; parry off {bestParryOff,2}/20 (lead {bestLeadOff:0.00}), on {bestParry,2}/20 (lead {bestLead:0.00}); composed guard's miss, median {med}");
                output.WriteLine($"{blow} {bearing}°: {where}");
                offHeldAll += offHeld;
                onHeldAll += onHeld;
            }
        foreach (var l in lines) output.WriteLine(l);
        Assert.True(onHeldAll > offHeldAll, $"on {onHeldAll}, off {offHeldAll}");
    }

    // ---- coverage by the guard's skill ----

    /// <summary>
    /// The guard skill the enhanced module's curve (guard.json's <c>skill</c>) gives the combat rules' newly Embraced (Dexterity 23) at Melee 0,
    /// 2 and 5, and a Melee 2 fighter at Dexterity 15 and 60 — the hand's factor and the read time's (the module's
    /// <c>GuardSkillCurveTests</c> checks these from the data).
    /// </summary>
    public static readonly (string Name, GuardSkill Skill)[] SkillCases =
    [
        ("Melee 0", new GuardSkill { Hand = 0.7663f, Read = 1.5609f, Guard = 1, Parry = 1, React = 1 }),
        ("Melee 2", new GuardSkill { Hand = 0.9837f, Read = 1.0391f, Guard = 1, Parry = 1, React = 1 }),
        ("Melee 5", new GuardSkill { Hand = 1.2575f, Read = 0.7258f, Guard = 1, Parry = 1, React = 1 }),
        ("Melee 2, Dex 15", new GuardSkill { Hand = 0.9402f, Read = 1.1435f, Guard = 1, Parry = 1, React = 1 }),
        ("Melee 2, Dex 60", new GuardSkill { Hand = 1.1574f, Read = 0.8321f, Guard = 1, Parry = 1, React = 1 }),
    ];

    /// <summary>
    /// Cd-guard's coverage (the blows met at the guard, 20 a cell: each blow from the front and 45° and 70° to the side) for each of
    /// <see cref="SkillCases"/>, the guard held from before and pressed as a parry 0.25 s before the blow's Strike mark (the brain's
    /// lead at the reference) and 0.15 s (the best kept). The measurement (about three minutes): written out; it asserts only that a
    /// higher skill meets no fewer blows in all than a lower.
    /// </summary>
    [Fact]
    public void Coverage_by_skill_Melee_0_2_5_and_Dexterity_low_and_high()
    {
        string defender = Environment.GetEnvironmentVariable("VITOMIR_GUARD_DEFENDER") ?? "Christof";
        var lines = new List<string> { $"defender {defender}; attacker {Attacker}; 20 blows a cell; held / parried (best of 0.15 and 0.25 s before the mark)" };
        var totals = new Dictionary<string, (int Held, int Parried)>();
        foreach (var (name, skill) in SkillCases)
        {
            int heldAll = 0, parriedAll = 0;
            var cells = new System.Text.StringBuilder();
            foreach (var blow in new[] { Blow.Quick1, Blow.Quick2, Blow.Strong })
                foreach (var bearing in new[] { 0f, 45f, 70f })
                {
                    int held = 0, best = 0;
                    foreach (var (aim, dist, bear) in Cases(bearing))
                        if (One(defender, blow, bear, dist, aim, null, solver: true, skill).AtGuard) held++;
                    foreach (var lead in new[] { 0.15f, 0.25f })
                    {
                        int p = 0;
                        foreach (var (aim, dist, bear) in Cases(bearing))
                            if (One(defender, blow, bear, dist, aim, lead, solver: true, skill).Parry) p++;
                        best = Math.Max(best, p);
                    }
                    cells.Append($"{blow} {bearing}°: {held}/{best}; ");
                    heldAll += held;
                    parriedAll += best;
                }
            totals[name] = (heldAll, parriedAll);
            lines.Add($"{name,-16} held {heldAll,3}/180, parried {parriedAll,3}/180 — {cells}");
        }
        foreach (var l in lines) output.WriteLine(l);
        Assert.True(totals["Melee 5"].Held >= totals["Melee 0"].Held, $"held: Melee 5 {totals["Melee 5"].Held}, Melee 0 {totals["Melee 0"].Held}");
        Assert.True(totals["Melee 5"].Parried >= totals["Melee 0"].Parried, $"parried: Melee 5 {totals["Melee 5"].Parried}, Melee 0 {totals["Melee 0"].Parried}");
    }

    /// <summary>The skill's hand factor scales the solver's hand speed exactly (a step of the hand, held and in a parry window), and no skill is the earlier build's.</summary>
    [Fact]
    public void The_skills_hand_moves_the_guarding_hand_that_much_faster_or_slower()
    {
        float Step(GuardSkill? skill)
        {
            var r = Make(skills: skill is not null);
            var d = r.Put("Christof", 0, 0, 90);
            if (skill is { } sk) r.World.Set(d, sk);
            var a = r.Put(Attacker, 0, 1.4f, 270f);
            r.Step(30);
            r.Intent(d).Block = true;
            r.Step(30);
            r.Swing(a, 0f);
            float most = 0f;
            Vector3 last = r.Rig(d).GuardHand;
            for (int i = 0; i < Ticks(0.6f); i++)
            {
                r.Step();
                var hand = r.Rig(d).GuardHand;
                if (r.Rig(d).GuardHandWeight >= 1f && last != Vector3.Zero) most = MathF.Max(most, (hand - last).Length());
                last = hand;
            }
            return most;
        }
        float none = Step(null), normal = Step(GuardSkill.Normal), slow = Step(new GuardSkill { Hand = 0.5f, Read = 1, Guard = 1, Parry = 1, React = 1 });
        output.WriteLine($"the hand's most step a tick (model space): none {none:0.0000}, 1 {normal:0.0000}, 0.5 {slow:0.0000}");
        Assert.Equal(none, normal, 6);
        Assert.True(none > 0f);
        Assert.InRange(slow, 0.45f * none, 0.55f * none);
    }
    /// <summary>
    /// The brain's parry timed by its hand — a brain whose own swing will not be over before its press is due gives the parry up
    /// (counted, no window for that blow); a free one chooses it and presses so the window is open as the blow's strike mark comes; with
    /// <c>brain-parry-fixed</c> (BrainParryByTravel off) it presses whatever its swing, and its press in the swing opens no window (the earlier build's
    /// finding). (That it holds its own next blow while a parry waits is the arena's: parries pressed 0.5 to 3 a bout.)
    /// </summary>
    [Fact]
    public void The_brains_timed_parry_is_chosen_only_when_free_in_time_and_presses_so_the_window_meets_the_strike()
    {
        bool parried = false;
        (DuelBrain Brain, int Windows, long WindowTick, long StrikeTick, int OwnSwings) Run(bool busy, bool timed)
        {
            var r = Make(brains: true, guard: g => g.BrainParryByTravel = timed);
            var a = r.Put("rudolfGuardRapier", 0, 0, 90);
            var d = r.Put("ChristofRapier", 0, 1.3f, -90);
            var book = r.World.Service<DuelBrainBook>();
            float reach = DuelBrains.ReachOf(r.Assets, r.Kinds["ChristofRapier"]);
            // The brain does not strike (StrikeShare: its foe never in its strike range), so its own swing is only the one the test gives it.
            r.World.Set(d, DuelBrain.For(BrainKind.Duel, a, book.Add(new DuelBrainSettings { ParryChance = 1f, BlockChance = 0f, StrikeShare = 0.01f, CloseShare = 3f, TooCloseShare = 0f, BackOffChance = 0f }), reach, reach));
            r.Step(30);
            if (busy) r.Fighter(d).Busy = 2f;   // its own swing's busy time runs well past the foe's blow
            int ownBefore = r.Fighter(d).Swings;
            r.Swing(a, 0f);
            // The blow's strike mark: from its beginning, its clip's mark at its rate (a parried blade never reaches it).
            var begun = r.Begun.First(b => b.Entity == a);
            long strikeTick = begun.Tick + (long)MathF.Round(r.Fighter(a).StrikeAt / MathF.Max(1e-3f, r.Anim(a).ActionRate) * World.TicksPerSecond);
            r.Step(Ticks(0.8f));
            long windowTick = r.Parries.Count > 0 ? r.Parries[0].Tick : -1;
            parried = r.Contacts.Any(c => c.Attacker == a && c.Guard == GuardMeet.Parry);
            return (r.World.Get<DuelBrain>(d), r.Parries.Count, windowTick, strikeTick, r.Fighter(d).Swings - ownBefore);
        }
        var free = Run(busy: false, timed: true);
        bool freeParried = parried;
        var stuck = Run(busy: true, timed: true);
        var old = Run(busy: true, timed: false);
        output.WriteLine($"free: chosen {free.Brain.ParriesChosen}, not free {free.Brain.ParriesNotFree}, pressed {free.Brain.Parries}, lead {free.Brain.ParryLead:0.000} s, window at t{free.WindowTick}, strike mark at t{free.StrikeTick}, met in the window {freeParried}");
        output.WriteLine($"busy: chosen {stuck.Brain.ParriesChosen}, not free {stuck.Brain.ParriesNotFree}, pressed {stuck.Brain.Parries}, windows {stuck.Windows}");
        output.WriteLine($"busy, fixed: pressed {old.Brain.Parries}, windows {old.Windows}");
        Assert.Equal((1, 0, 1, 1), (free.Brain.ParriesChosen, free.Brain.ParriesNotFree, free.Brain.Parries, free.Windows));
        Assert.InRange(free.StrikeTick - free.WindowTick, 1, (long)MathF.Ceiling(0.25f * World.TicksPerSecond) + 2);   // the window open as the strike comes
        Assert.True(freeParried, "the free brain's window met the blow");
        Assert.Equal((1, 1, 0, 0), (stuck.Brain.ParriesChosen, stuck.Brain.ParriesNotFree, stuck.Brain.Parries, stuck.Windows));
        Assert.Equal(0, old.Brain.ParriesChosen);   // The earlier build's brain: no such choice; it presses into its own swing (no window)
        Assert.Equal(1, old.Brain.Parries);
        Assert.Equal(0, old.Windows);
    }

    // ---- end

    static bool Trace => Environment.GetEnvironmentVariable("VITOMIR_PROBE_TRACE") == "1";

    /// <summary>The attacker's kind for the coverage (Christof with the broadsword unless <c>VITOMIR_GUARD_ATTACKER</c> names another of <see cref="AllKinds"/>).</summary>
    static string Attacker => Environment.GetEnvironmentVariable("VITOMIR_GUARD_ATTACKER") ?? "Christof";

    /// <summary>One blow, on and off, written out (a probe while tuning).</summary>
    [Fact]
    public void Probe_one_blow()
    {
        var blow = Enum.Parse<Blow>(Environment.GetEnvironmentVariable("VITOMIR_PROBE_BLOW") ?? "Quick1");
        float bearing = float.Parse(Environment.GetEnvironmentVariable("VITOMIR_PROBE_BEARING") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var aims = (Environment.GetEnvironmentVariable("VITOMIR_PROBE_AIMS") ?? "-20,0,15").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
        float dist = float.Parse(Environment.GetEnvironmentVariable("VITOMIR_PROBE_DIST") ?? "1.3", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var aim in aims)
            foreach (var solver in new[] { false, true })
            {
                var o = One("Christof", blow, bearing, dist, aim, null, solver);
                output.WriteLine($"{blow} {bearing}° aim {aim}: solver {solver}: met {o.Met} (guard {o.AtGuard}); miss {o.Miss:0.000} m guard mid {o.GuardMiddle} tip {o.BlowTip}; follows {o.Follows}; solver {o.SolverSeconds * 1e6 / Math.Max(1, o.Solved):0} us a guard-tick over {o.Solved}");
            }
    }

    // ---- the solver's rules ----

    /// <summary>A held guard (Christof's broadsword) and a forehand at it from the front, 1.3 m, aimed <paramref name="aim"/>°: the ring after the blow, the guard and the attacker.</summary>
    (Ring R, EntityId D, EntityId A) Held(float aim = 0f, Action<GuardSettings>? tune = null, bool solver = true, bool rules = true)
    {
        var r = Make(solver: solver, rules: rules, guard: tune);
        var d = r.Put("Christof", 0, 0, 90);
        var a = r.Put("Christof", 0, 1.3f, -90);
        r.Step(30);
        r.Intent(d).Block = true;
        r.Step(30);
        r.Swing(a, aim);
        return (r, d, a);
    }

    [Fact]
    public void The_intercept_is_the_soonest_crossing_the_hand_can_reach_and_the_blade_is_met_there_no_later()
    {
        var (r, d, a) = Held(0f);
        r.Step(Ticks(1.2f));
        var f = Assert.Single(r.Follows);
        Assert.Equal(d, f.Who);
        Assert.Equal(a, f.Threat);
        Assert.True(f.Margin >= 0f, $"the hand reaches the intercept in time: margin {f.Margin}");
        var c = r.Contacts.First();
        Assert.Equal((ContactMet.Weapon, d), (c.Met, c.Target));
        // Met no later than the intercept's moment (the blade's path crosses the guard's line there; an earlier meeting is the
        // blade crossing it sooner on its way, never later).
        float metAfter = (c.Tick - f.Tick) * World.TickSeconds;
        output.WriteLine($"followed at t{f.Tick}: intercept {f.Lead:0.000} s ahead, the hand {f.Margin:0.000} s early; met {metAfter:0.000} s after");
        Assert.True(metAfter <= f.Lead + 2 * World.TickSeconds, $"met {metAfter} s after, the intercept {f.Lead} s");
        // The solver's state: weight 1 while following; the guard back to the composed one after (the weight fades).
        r.Step(Ticks(1f));
        Assert.Equal(0f, r.Rig(d).GuardHandWeight);
    }

    [Fact]
    public void The_hand_speed_limits_the_guard_a_hand_too_slow_is_late_and_the_blow_beats_it()
    {
        var (fast, fd, _) = Held(0f);
        fast.Step(Ticks(1.2f));
        var (slow, sd, _) = Held(0f, g => { g.SolverHandMetresPerSecond = 0.05f; g.SolverBladeDegreesPerSecond = 10f; g.SolverTwistDegreesPerSecond = 10f; });
        var start = slow.Rig(sd).GuardHand;
        slow.Step(Ticks(1.2f));
        var f = Assert.Single(slow.Follows);
        output.WriteLine($"fast: met {fast.Contacts.First().Met}; slow: margin {f.Margin:0.000}, met {(slow.Contacts.Count > 0 ? slow.Contacts[0].Met + " " + slow.Contacts[0].Site : "nothing")}");
        Assert.Equal(ContactMet.Weapon, fast.Contacts.First().Met);
        Assert.True(f.Margin < 0f, $"the slow hand is late: {f.Margin}");
        Assert.NotEqual(ContactMet.Weapon, slow.Contacts.First().Met);
    }

    [Fact]
    public void An_unseen_threat_moves_nothing_the_players_guard_looking_at_the_ground_keeps_the_composed_pose()   // GuardSightTests' case: a foe 30° off, the view pitched 70° down
    {
        foreach (var pitch in new[] { 0f, -70f })
        {
            const float bearing = 30f;
            var r = Make();
            var g = MovementServices.SpawnControlled(r.World, new(0, 0, 0.01f), 90f * DetMath.DegToRad, Locomotion.Sandbox);
            r.World.Set(g, CharacterAnimation.For(r.Assets, r.Kinds["Christof"]));
            r.World.Set(g, MotionInput.Standing);
            CombatServices.Arm(r.World, g);
            ref var cam = ref r.World.Get<CameraRig>(g);
            cam.View = ViewKind.First;
            cam.Pitch = pitch * DetMath.DegToRad;
            float b = (90f + bearing) * DetMath.DegToRad;
            var a = r.Put("Christof", MathF.Cos(b) * 1.3f, MathF.Sin(b) * 1.3f, 90f + bearing + 180f);
            var block = new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.Block, Vector2.Zero);
            for (int i = 0; i < 40; i++) MovementServices.Step(r.World, block);
            ref var intent = ref r.Intent(a);
            intent.Aims = true;
            intent.Attack = true;
            MovementServices.Step(r.World, block);
            r.Intent(a).Attack = false;
            int follows = 0;
            float most = 0f;
            for (int i = 0; i < Ticks(0.5f); i++)
            {
                MovementServices.Step(r.World, block);
                follows += r.World.Events<GuardSolverFollows>().ThisTick.Count;
                most = MathF.Max(most, r.Rig(g).GuardHandWeight);
            }
            output.WriteLine($"looking {pitch}°: follows {follows}, the solver's weight at most {most}");
            if (pitch == 0f) Assert.True(follows == 1 && most == 1f, "seen: followed");
            else Assert.True(follows == 0 && most == 0f, "unseen: nothing moves");
        }
    }

    [Fact]
    public void The_give_pushes_the_guarding_hand_along_the_blow_and_springs_back_to_nothing()
    {
        var (r, d, _) = Held(0f);
        float most = 0f; long mostAt = 0; long gaveAt = 0;
        for (int i = 0; i < Ticks(1.5f); i++)
        {
            r.Step();
            if (r.Gave.Count > 0 && gaveAt == 0) gaveAt = r.World.Tick;
            float x = r.Rig(d).GuardGive.Length();
            if (x > most) { most = x; mostAt = r.World.Tick; }
        }
        var gave = Assert.Single(r.Gave);
        var c = r.Contacts.First(x => x.ContactId == gave.ContactId);
        output.WriteLine($"gave {gave.Metres:0.000} m (impulse {c.NormalImpulse:0.0} N s, {gave.Verdict}); furthest {most:0.000} m {(mostAt - gaveAt) * World.TickSeconds:0.000} s after; now {r.Rig(d).GuardGive}");
        Assert.False(gave.Shield);
        Assert.InRange(most, gave.Metres * 0.9f, gave.Metres * 1.05f);       // its furthest, about when the spring says
        Assert.InRange((mostAt - gaveAt) * World.TickSeconds, 0.03f, 0.12f);
        Assert.Equal(Vector3.Zero, r.Rig(d).GuardGive);                       // back to nothing, exactly
        Assert.Equal(Vector3.Zero, r.Rig(d).GuardGiveVelocity);
        // A shield gives on its own arm.
        var s = Make();
        var sd = s.Put("rudolfGuard", 0, 0, 90);
        var sa = s.Put("Christof", 0, 1.3f, -90);
        s.Step(30); s.Intent(sd).Block = true; s.Step(30);
        s.Swing(sa, 0f);
        s.Step(Ticks(1.2f));
        output.WriteLine($"shield: {string.Join("; ", s.Gave.Select(x => $"{x.Metres:0.000} m shield {x.Shield}"))}; contacts {string.Join("; ", s.Contacts.Select(x => x.Met))}");
        Assert.Contains(s.Gave, x => x.Shield);
    }

    [Fact]
    public void Off_the_solver_writes_nothing_and_the_guard_is_as_before()
    {
        var (r, d, _) = Held(0f, solver: false);
        for (int i = 0; i < Ticks(1.5f); i++)
        {
            r.Step();
            Assert.Equal(0f, r.Rig(d).GuardHandWeight);
            Assert.Equal(Vector3.Zero, r.Rig(d).GuardGive);
        }
        Assert.Empty(r.Follows);
        Assert.Empty(r.Gave);
    }

    [Fact]
    public void Determinism_a_load_mid_solve_goes_on_the_same_and_a_guarded_bout_allocates_nothing_per_tick()
    {
        void Act(Ring r, EntityId a, EntityId d, int i)
        {
            int at = i % 120;
            r.Intent(a).Aims = true;
            r.Intent(a).AimPitch = (i / 120 % 3 == 0 ? 15f : i / 120 % 3 == 1 ? 0f : -20f) * DetMath.DegToRad;
            r.Intent(a).Attack = at == 2;
            r.Intent(d).Block = at >= 10 && at < 100;
        }
        (Ring, EntityId, EntityId) Bout()
        {
            var r = Make(solver: Environment.GetEnvironmentVariable("VITOMIR_SOLVER_OFF") != "1");
            var d = r.Put("Christof", 0, 0, 90);
            var a = r.Put("Christof", 0.3f, 1.3f, -100);
            return (r, d, a);
        }
        var (x, xd, xa) = Bout();
        var (y, yd, ya) = Bout();
        int solvingAt = -1;
        for (int i = 0; i < 600; i++)
        {
            Act(x, xa, xd, i); Act(y, ya, yd, i);
            x.Step(); y.Step();
            Assert.Equal(x.World.Digest(), y.World.Digest());
            if (solvingAt < 0 && i > 250 && x.Rig(xd).GuardHandWeight == 1f && x.Anim(xa).ActionTime > 0.2f) solvingAt = i;
        }
        if (Environment.GetEnvironmentVariable("VITOMIR_SOLVER_OFF") != "1")
        {
            Assert.True(solvingAt > 0, "the solver followed a blow");
            Assert.True(x.Follows.Count >= 3, $"followed {x.Follows.Count}");
        }
        else solvingAt = 300;
        // A load mid-solve: every digested component copied into a fresh world (its memo of paths empty) goes on the same.
        var (src, sd, sa) = Bout();
        for (int i = 0; i <= solvingAt; i++) { Act(src, sa, sd, i); src.Step(); }
        var (dst, _, _) = Bout();
        dst.Step(solvingAt + 1);
        Copy(src.World, dst.World);
        Assert.Equal(src.World.Digest(), dst.World.Digest());
        for (int i = solvingAt + 1; i < solvingAt + 240; i++)
        {
            Act(src, sa, sd, i); Act(dst, sa, sd, i);
            src.Step(); dst.Step();
            Assert.Equal(src.World.Digest(), dst.World.Digest());
        }
        // Allocation, and the cost: a guarded bout run on, then measured.
        for (int i = 600; i < 1200; i++) { Act(x, xa, xd, i); MovementServices.Step(x.World, ActionFrame.Idle); }
        var solver = x.World.Service<GuardSolverSystem>();
        long before = GC.GetAllocatedBytesForCurrentThread();
        double seconds = 0; int solved = 0;
        for (int i = 1200; i < 1440; i++)
        {
            Act(x, xa, xd, i);
            MovementServices.Step(x.World, ActionFrame.Idle);
            if (solver.LastSolved > 0) { seconds += solver.LastSeconds; solved += solver.LastSolved; }
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{bytes} bytes over 240 ticks of a guarded bout; the solver {seconds * 1e6 / Math.Max(1, solved):0.0} us a guard a tick over {solved} guard-ticks; paths made {solver.PathsMade}");
        Assert.True(bytes < 2048, $"{bytes} bytes allocated");
    }

    static void Copy(World from, World to)
    {
        var method = typeof(GuardSolverTests).GetMethod(nameof(CopyStore), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var s in from.Stores)
        {
            if (!s.Digested) continue;
            var other = to.Stores.First(o => o.ComponentType == s.ComponentType);
            method.MakeGenericMethod(s.ComponentType).Invoke(null, [s, other]);
        }
    }

    static void CopyStore<T>(Store<T> from, Store<T> to) where T : struct
    {
        var ents = to.Entities.ToArray();
        foreach (var e in ents) if (!from.Has(e)) to.Remove(e);
        var src = from.Entities.ToArray();
        for (int i = 0; i < src.Length; i++) to.Set(src[i], from.Get(src[i]));
    }

    /// <summary>
    /// The duelling brain's parry with the solver (the arena's guard-parry pair, standing): the combat rules' pair's kinds — Christof with the
    /// rapier, his brain parrying every blow (parryChance 1, blockChance 0, pressing 0.25 s before the strike), against rudolfGuard's
    /// rapier forehand at five aims, 1.25 m off, solver off and on: the parries met in the window, written out.
    /// </summary>
    [Fact]
    public void The_brains_parry_meets_the_blow_with_the_solver()
    {
        int Count(bool solver)
        {
            int met = 0;
            var line = new System.Text.StringBuilder();
            foreach (var aim in new[] { -20f, -10f, 0f, 10f, 20f })
                foreach (var dist in new[] { 1.15f, 1.25f, 1.35f, 1.45f })
                {
                    var r = Make(solver: solver, brains: true);
                    var a = r.Put("rudolfGuardRapier", 0, 0, 90);
                    var d = r.Put("ChristofRapier", 0, dist, -90);
                    var book = r.World.Service<DuelBrainBook>();
                    float reach = DuelBrains.ReachOf(r.Assets, r.Kinds["ChristofRapier"]);
                    r.World.Set(d, DuelBrain.For(BrainKind.Duel, a, book.Add(new DuelBrainSettings { ParryChance = 1f, BlockChance = 0f, StrikeShare = 0.01f, CloseShare = 3f, TooCloseShare = 0f, BackOffChance = 0f }), reach, reach));
                    r.Step(30);
                    r.Swing(a, aim);
                    r.Step(Ticks(1.2f));
                    var c = r.Contacts.FirstOrDefault(x => x.Attacker == a);
                    bool parried = r.Contacts.Count > 0 && c.Attacker == a && c.Guard == GuardMeet.Parry;
                    if (parried) met++;
                    line.Append($"[{aim:+0;-0} {dist}: {(r.Contacts.Count == 0 ? "nothing" : c.Met == ContactMet.Body ? c.Site : c.Guard.ToString())}, parries {r.Parries.Count}] ");
                }
            output.WriteLine($"solver {solver}: parried {met} of 20: {line}");
            return met;
        }
        int off = Count(false), on = Count(true);
        Assert.True(on > off, $"on {on}, off {off}");
    }

    /// <summary>
    /// A probe (written out, nothing asserted but that it runs): the arena's guard-parry pair as two duelling brains in a ring — Christof
    /// with the rapier parrying half the blows (parryChance 0.5, blockChance 0), rudolfGuard with the rapier on the default brain —
    /// for 60 s; for each blow Christof's solver follows, when (after his press, mid his own swing or not) and how late.
    /// </summary>
    [Fact]
    public void Probe_the_brains_parry_in_a_duel()
    {
        var r = Make(brains: true);
        var c = r.Put("ChristofRapier", 0, 0, 90);
        var g = r.Put("rudolfGuardRapier", 0, 1.3f, -90);
        var book = r.World.Service<DuelBrainBook>();
        float rc = DuelBrains.ReachOf(r.Assets, r.Kinds["ChristofRapier"]), rg = DuelBrains.ReachOf(r.Assets, r.Kinds["rudolfGuardRapier"]);
        r.World.Set(c, DuelBrain.For(BrainKind.Duel, g, book.Add(new DuelBrainSettings { ParryChance = 0.5f, BlockChance = 0f }), rc, rg));
        r.World.Set(g, DuelBrain.For(BrainKind.Duel, c, book.Add(new DuelBrainSettings()), rg, rc));
        int late = 0, inTime = 0, parried = 0, metGuard = 0, follows = 0;
        var lines = new List<string>();
        for (int i = 0; i < Ticks(60f); i++)
        {
            r.Step();
            foreach (var f in r.World.Events<GuardSolverFollows>().ThisTick)
            {
                if (f.Who != c) continue;
                follows++;
                if (f.Margin >= 0f) inTime++; else late++;
                ref var ca = ref r.Anim(c);
                ref var cc = ref r.Fighter(c);
                ref var ga = ref r.Anim(g);
                if (lines.Count < 25) lines.Add($"t{f.Tick}: margin {f.Margin:0.000} lead {f.Lead:0.000}; Christof {ca.Action} w {ca.ActionWeight:0.00} busy {cc.Busy:0.00} parry {cc.ParryLeft:0.00}; the foe's blow at {ga.ActionTime:0.000} of strike {r.Fighter(g).StrikeAt:0.000}, aim {r.World.Get<PoseRig>(g).AimTarget * DetMath.RadToDeg:0}, {Vector3.Distance(r.World.Get<Body>(c).Feet, r.World.Get<Body>(g).Feet):0.00} m");
            }
        }
        foreach (var x in r.Contacts) if (x.Attacker == g && x.Target == c && x.Met == ContactMet.Weapon) { metGuard++; if (x.Guard == GuardMeet.Parry) parried++; }
        output.WriteLine($"Christof followed {follows} (in time {inTime}, late {late}); parries opened {r.Parries.Count(p => p.Entity == c)}; the foe's blows met at his blade {metGuard}, in the window {parried}; the foe's swings {r.Begun.Count(b => b.Entity == g)}");
        foreach (var l in lines) output.WriteLine(l);
    }
}
