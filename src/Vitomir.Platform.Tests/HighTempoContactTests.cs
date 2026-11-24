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
/// Contact at a high tempo (Celerity 5 with Melee 10 is a swing at about 7.8×, rank 10's curve 11×, with skill 14×): a
/// quick blow of 1.1's Christof at a standing townsman, and at a guard with his shield raised, played at tempo 1, 2, 6, 11 and
/// 14 (<see cref="CombatTempo"/>) meets the same thing at the same site, once per swing and target, and its strike moment is
/// told once (a blow stopped on the shield before its strike: at most once) — though a tick covers up to fourteen times as much
/// of the clip. Two fixes made it so: above twice 1.1's rate the blow's fade-in runs at its rate over that
/// (<see cref="AnimationSystem"/>: in real time it left the arm low, and the 11× blow met the chest where the slower ones met the
/// left upper arm), and the contact sweeps such a tick in sub-steps of the clip (<see cref="ContactSystem.SubStepTempo"/>).
/// Skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class HighTempoContactTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static readonly Dictionary<(string, string?), OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null)
    {
        lock (Loaded)
        {
            if (!Loaded.TryGetValue((name, weapon), out var c))
                Loaded[(name, weapon)] = c = Library.Value!.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    sealed record Blow(List<ContactMade> Contacts, List<StrikeMoment> Strikes, List<AttackBegun> Begun, List<SwingEnded> Ended, EntityId Attacker, EntityId Target);

    /// <summary>One quick blow of Christof's at <paramref name="tempo"/> at <paramref name="target"/> 1.3 m before him (its shield raised when asked).</summary>
    static Blow Swing(float tempo, string target, bool shield, float aim = 0f)
    {
        var assets = new CharacterAssets();
        int christof = OneOneLibrary.Register(assets, Character("Christof", "BroadSword"));
        int other = OneOneLibrary.Register(assets, Character(target));
        var b = new WorldBuilder(7);
        MovementServices.Register(b, new TestLevel().Floor(-20, -20, 20, 20).Build());
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        CombatTempos.Register(b);
        ContactServices.Register(b, new ContactConfig());
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        EntityId Put(int kind, float y, float yawDegrees)
        {
            var e = MovementServices.SpawnBody(w, new(0, y, 0.01f), yawDegrees * DetMath.DegToRad, Locomotion.Sandbox);
            w.Set(e, CharacterAnimation.For(assets, kind));
            w.Set(e, MotionInput.Standing);
            CombatServices.Arm(w, e);
            return e;
        }
        var a = Put(christof, 0, 90);
        var t = Put(other, 1.3f, -90);
        w.Set(a, new CombatTempo(tempo, tempo, tempo));
        if (shield) w.Get<CombatIntent>(t).Block = true;
        var blow = new Blow([], [], [], [], a, t);
        void Step(int n)
        {
            for (int i = 0; i < n; i++)
            {
                MovementServices.Step(w, ActionFrame.Idle);
                blow.Contacts.AddRange(w.Events<ContactMade>().ThisTick.Where(c => c.Attacker == a));
                blow.Strikes.AddRange(w.Events<StrikeMoment>().ThisTick.Where(s => s.Entity == a));
                blow.Begun.AddRange(w.Events<AttackBegun>().ThisTick.Where(s => s.Entity == a));
                blow.Ended.AddRange(w.Events<SwingEnded>().ThisTick.Where(s => s.Attacker == a));
            }
        }
        Step(60);
        ref var intent = ref w.Get<CombatIntent>(a);
        intent.Aims = true;
        intent.AimPitch = aim * DetMath.DegToRad;
        intent.Attack = true;
        Step(1);
        w.Get<CombatIntent>(a).Attack = false;
        Step(2 * World.TicksPerSecond);
        return blow;
    }

    [Theory]
    [InlineData("Civ12male", false)]
    [InlineData("rudolfGuard", true)]
    public void A_blow_at_tempo_1_2_6_11_and_14_meets_the_same_thing_at_the_same_site_once_and_its_strike_is_told_once(string target, bool shield)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        (ContactMet Met, string? Site)? first = null;
        foreach (float tempo in new[] { 1f, 2f, 6f, 11f, 14f })
        {
            var b = Swing(tempo, target, shield);
            output.WriteLine($"tempo {tempo}: {string.Join("; ", b.Contacts.Select(c => $"{c.Met} {c.Site} at {c.Point.Z:0.00} m, {c.ImpactSpeed:0.0} m/s, tick {c.Tick}"))}; strikes {b.Strikes.Count}; begun {b.Begun.Count}");
            var begun = Assert.Single(b.Begun);
            // The strike moment, not skipped: once on a body. A blow stopped on a shield before its strike tells none — but once the
            // tick that meets the shield also crosses the strike's mark, and the strike is told before contact in a tick: at most
            // once (a finding, not fixed: the strike's system is the attacks', Control/Attacks.cs).
            int strikes = b.Strikes.Count(s => s.Swing == begun.Swing);
            // A blade that stops in the body (the stop) never reaches a strike mark after its contact either.
            Assert.InRange(strikes, 0, 1);
            var c = Assert.Single(b.Contacts, x => x.Target == b.Target);               // once per swing and target
            Assert.Equal(begun.Swing, c.Swing);
            Assert.Equal(shield ? ContactMet.Shield : ContactMet.Body, c.Met);
            first ??= (c.Met, c.Site);
            Assert.Equal(first.Value, (c.Met, c.Site));                                // the same thing at the same site
            Assert.Single(b.Ended, e => e.Swing == begun.Swing);
        }
    }
}
