// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The duelling brain's shield read — by its skill's chance (<see cref="GuardSkill.ShieldRead"/>) a blow
/// at a foe who carries a shield is aimed where the shield is not (over its top, as it is held now); at chance 0 nothing is drawn and the
/// brain is the earlier build's exactly. Christof's broadsword brain against rudolfGuard standing with his kite shield resting. Needs the 1.1 install.
/// </summary>
public sealed class ShieldReadTests(ITestOutputHelper output)
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

    sealed record Blow(long Tick, AttackDirection Direction, float Pitch, float Over, float Top, float FoeTop);

    sealed record Duel(List<Blow> Blows, int Body, int Shield, int Other, int ReadsOver, int ReadsUnder, int NoRoom, int Unthrust);

    /// <summary>
    /// A 30 s duel: Christof's brain (no block, no back-off) against rudolfGuard standing (his shield resting). <paramref name="read"/>
    /// null: no <see cref="GuardSkill"/> store at all (the platform without a module's skill); else Christof's skill with that read.
    /// Each blow's aim is recorded with where it would pass at the guard's distance, and the shield's top and the guard's top then.
    /// </summary>
    static Duel Fight(float? read, float seconds = 30f)
    {
        var assets = new CharacterAssets();
        int christof = OneOneLibrary.Register(assets, Character("Christof", "BroadSword"));
        int guard = OneOneLibrary.Register(assets, Character("rudolfGuard"));
        var b = new WorldBuilder(5);
        MovementServices.Register(b, new TestLevel().Floor(-60, -60, 60, 60).Build());
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        ContactServices.Register(b, new ContactConfig());
        DuelBrains.Register(b);
        if (read is not null) GuardSkills.Register(b);
        b.Service(new DirectionalAttacks { Rows = [.. DirectionalAttacks.DefaultRows, new DirectionRow(null, 1, null, null, "stitched:overhead", "stitched:thrust")] });   // all four (DirectionTests')
        var aim = new SwingAim();
        b.Service(aim);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        var ea = MovementServices.SpawnBody(w, new(0f, 0f, 0.02f), DetMath.HalfPi, Locomotion.Sandbox);
        var eb = MovementServices.SpawnBody(w, new(0f, 1.5f, 0.02f), -DetMath.HalfPi, Locomotion.Sandbox);
        foreach (var (e, k) in new[] { (ea, christof), (eb, guard) })
        {
            w.Set(e, CharacterAnimation.For(assets, k));
            w.Set(e, MotionInput.Standing);
            CombatServices.Arm(w, e);
        }
        if (read is { } r)
        {
            var skill = GuardSkill.Normal;
            skill.ShieldRead = r;
            w.Set(ea, skill);
            w.Set(eb, GuardSkill.Normal);
        }
        var book = w.Service<DuelBrainBook>();
        float ra = DuelBrains.ReachOf(assets, christof), rb = DuelBrains.ReachOf(assets, guard);
        w.Set(ea, DuelBrain.For(BrainKind.Duel, eb, book.Add(new DuelBrainSettings { BackOffChance = 0f, BlockChance = 0f }), ra, rb));
        w.Set(eb, DuelBrain.For(BrainKind.Stand, ea, book.Add(new DuelBrainSettings()), rb, ra));
        var blows = new List<Blow>();
        int body = 0, shield = 0, other = 0;
        var firstMet = new HashSet<int>();
        for (int t = 0; t < (int)(seconds * World.TicksPerSecond); t++)
        {
            MovementServices.Step(w, ActionFrame.Idle);
            foreach (var x in w.Events<AttackBegun>().ThisTick)
            {
                if (x.Entity != ea) continue;
                var a = w.Get<Body>(ea);
                var f = w.Get<Body>(eb);
                Assert.True(HeldItems.Shield(w, eb, out var box));
                float up = MathF.Abs(box.AxisX.Z) * box.HalfSize.X + MathF.Abs(box.AxisY.Z) * box.HalfSize.Y + MathF.Abs(box.AxisZ.Z) * box.HalfSize.Z;
                float d = MathF.Max(aim.LeastDistance, MathF.Sqrt((f.Feet.X - a.Feet.X) * (f.Feet.X - a.Feet.X) + (f.Feet.Y - a.Feet.Y) * (f.Feet.Y - a.Feet.Y)));
                float from = x.Direction == AttackDirection.Thrust ? SwingAim.Top(w, ea, in a) * aim.ThrustFromShare : aim.SweepHeight;
                // Where the aimed line passes over the guard's feet: the blow's height there (m over his feet).
                float over = a.Feet.Z + from + d * DetMath.Sin(x.AimPitch) / DetMath.Cos(x.AimPitch) - f.Feet.Z;
                blows.Add(new Blow(x.Tick, x.Direction, x.AimPitch, over, box.Centre.Z + up - f.Feet.Z, SwingAim.Top(w, eb, in f)));
            }
            foreach (var c in w.Events<ContactMade>().ThisTick)
            {
                if (c.Attacker != ea || !firstMet.Add(c.Swing)) continue;
                if (c.Met == ContactMet.Body) body++;
                else if (c.Met == ContactMet.Shield) shield++;
                else other++;
            }
        }
        var brain = w.Get<DuelBrain>(ea);
        return new Duel(blows, body, shield, other, brain.ShieldReadsOver, brain.ShieldReadsUnder, brain.ShieldReadsNoRoom, brain.ShieldReadsUnthrust);
    }

    [Fact]
    public void A_brain_that_reads_the_shield_aims_every_blow_over_its_top_and_cuts_instead_of_thrusting()
    {
        var plain = Fight(0f);
        var reads = Fight(1f);
        string Line(Duel d) => $"blows {d.Blows.Count}, over/under/no room {d.ReadsOver}/{d.ReadsUnder}/{d.NoRoom}, thrusts cut instead {d.Unthrust}; "
            + $"first contacts body {d.Body}, shield {d.Shield}, other {d.Other}; aimed at {string.Join(" ", d.Blows.Take(8).Select(b => $"{b.Direction}:{b.Over:0.00}"))} "
            + $"(the shield's top {d.Blows.Average(b => b.Top):0.00}, his top {d.Blows.Average(b => b.FoeTop):0.00})";
        output.WriteLine("read 0: " + Line(plain));
        output.WriteLine("read 1: " + Line(reads));
        Assert.True(plain.Blows.Count >= 12 && reads.Blows.Count >= 12, "it fought");
        Assert.Equal((0, 0, 0, 0), (plain.ReadsOver, plain.ReadsUnder, plain.NoRoom, plain.Unthrust));
        // Read every time: every blow aimed over the shield as it rests (above its top, at or below his top), the reads counted.
        Assert.Equal(reads.Blows.Count, reads.ReadsOver + reads.ReadsUnder + reads.NoRoom);
        Assert.True(reads.ReadsOver >= reads.Blows.Count - 1, "over the shield: room above it");
        Assert.All(reads.Blows, b => Assert.True(b.Over > b.Top + 0.03f && b.Over < b.FoeTop + 0.05f, $"{b.Direction} at {b.Over:0.00} m, the shield's top {b.Top:0.00}, his {b.FoeTop:0.00}"));
        // A thrust is driven at the shield's face whatever its aim: a read blow is a cut instead (by the weights: none of them thrusts).
        Assert.DoesNotContain(reads.Blows, b => b.Direction == AttackDirection.Thrust);
        Assert.True(reads.Unthrust >= 1, "a drawn thrust was cut instead");
        // Not read: the drawn heights, most of them under the shield's top (the chest and the hips). (Standing square to Christof 1.5 m
        // away, rudolfGuard's resting kite shield still takes both: the read buys its body contacts in a moving fight, the arena's runs.)
        Assert.True(plain.Blows.Count(b => b.Over < b.Top) > plain.Blows.Count / 2, "the drawn aims meet the shield's height");
    }

    [Fact]
    public void At_chance_0_nothing_is_drawn_and_the_brain_is_as_without_the_read_blow_for_blow()
    {
        // The skill store with a read of 0 against no skill store at all: the same blows at the same ticks, the same directions and aims.
        var none = Fight(null, 15f);
        var zero = Fight(0f, 15f);
        Assert.True(none.Blows.Count >= 6);
        Assert.Equal(none.Blows.Select(b => (b.Tick, b.Direction, b.Pitch)), zero.Blows.Select(b => (b.Tick, b.Direction, b.Pitch)));
        Assert.Equal((none.Body, none.Shield, none.Other), (zero.Body, zero.Shield, zero.Other));
    }
}
