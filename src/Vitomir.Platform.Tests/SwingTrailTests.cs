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
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The swing's trail (<c>Presentation/SwingTrail.cs</c>): the places kept in order, cut where the blade stopped;
/// the aim kept from the blow's start; the tip's nearest pass to the reticle's ray; the lines to draw, fading and let go; two
/// swings at once kept apart; nothing allocated as places are added; and, with the 1.1 install, a swing's trail is the blade
/// contact swept — through the active window on open ground, up to the bite on a body.
/// </summary>
public sealed class SwingTrailTests(ITestOutputHelper output)
{
    static readonly EntityId A = new(1, 1), B = new(2, 1);

    static BladePlacement Blade(float x, float y = 1f, float z = 1.2f) => new(new Vector3(x, y - 0.8f, z), new Vector3(x, y, z), 0.02f);

    [Fact]
    public void A_swing_keeps_its_places_in_order_and_ends_where_the_blade_stopped()
    {
        var trails = new SwingTrails();
        trails.Begin(A, 1, 100, SwingTrails.Aim.None);
        trails.Record(A, 1, 101, [Blade(0f), Blade(0.1f)], 1f);
        trails.Record(A, 1, 102, [Blade(0.1f), Blade(0.2f)], 1f);
        trails.Record(A, 1, 103, [Blade(0.2f), Blade(0.3f)], 0.5f);   // stopped half way through the tick
        Assert.True(trails.Latest(A, out var t));
        var places = new List<BladePlacement>();
        trails.Places(t, places);
        Assert.Equal([0f, 0.1f, 0.2f, 0.25f], places.Select(p => MathF.Round(p.Tip.X, 4)));
        Assert.Equal(100, t.FirstTick);
        Assert.Equal(103, t.LastTick);

        // A fast swing's sub-steps: the places between kept, cut at the stop.
        trails.Begin(A, 2, 200, SwingTrails.Aim.None);
        trails.Record(A, 2, 201, [Blade(0f), Blade(0.1f), Blade(0.2f), Blade(0.3f), Blade(0.4f)], 0.6f);
        Assert.True(trails.Latest(A, out var fast));
        trails.Places(fast, places);
        Assert.Equal([0f, 0.1f, 0.2f, 0.24f], places.Select(p => MathF.Round(p.Tip.X, 4)));
    }

    [Fact]
    public void The_aim_is_the_blows_start_and_the_nearest_pass_to_its_ray_is_found()
    {
        var trails = new SwingTrails();
        // The ray from (0, -2, 1.5) along +Y; the tip sweeps across x at y = 1, z = 1.2: it passes 0.3 m under the ray at x = 0.
        var aim = new SwingTrails.Aim(true, new Vector3(0f, -2f, 1.5f), Vector3.UnitY, true, new Vector3(0f, 1f, 1.5f));
        trails.Begin(A, 1, 10, aim);
        trails.Record(A, 1, 11, [Blade(-0.6f), Blade(-0.2f)], 1f);
        trails.Record(A, 1, 12, [Blade(-0.2f), Blade(0.2f)], 1f);
        trails.Record(A, 1, 13, [Blade(0.2f), Blade(0.6f)], 1f);
        Assert.True(trails.Latest(A, out var t));
        Assert.True(t.HasRay && t.HasPoint);
        Assert.Equal(aim.Point, t.Point);
        Assert.Equal(0.3f, t.Nearest, 4);
        Assert.Equal(new Vector3(0f, 1f, 1.2f), t.NearestTip);
        Assert.Equal(new Vector3(0f, 1f, 1.5f), t.NearestOnRay);
        // A segment behind the ray's start is measured from its start.
        Assert.Equal(3f, SwingTrails.SegmentToRay(new Vector3(-1f, -3f, 0f), new Vector3(1f, -3f, 0f), Vector3.Zero, Vector3.UnitY, out _, out var onRay), 4);
        Assert.Equal(Vector3.Zero, onRay);
    }

    [Fact]
    public void The_lines_are_the_tip_path_the_blade_every_few_and_the_marks_and_they_fade_then_go()
    {
        var trails = new SwingTrails { BladeEvery = 2 };
        var aim = new SwingTrails.Aim(true, new Vector3(0f, -2f, 1.5f), Vector3.UnitY, true, new Vector3(0f, 1f, 1.5f));
        trails.Begin(A, 1, 120, aim);
        for (int k = 0; k < 4; k++) trails.Record(A, 1, 121 + k, [Blade(-0.4f + 0.2f * k), Blade(-0.2f + 0.2f * k)], 1f);   // five places
        var lines = new List<SwingTrailLine>();
        double last = 124 / (double)World.TicksPerSecond;
        trails.Fill(last + 0.1, lines);
        Assert.Equal(4, lines.Count(l => l.Kind == SwingTrailKind.Tip));
        Assert.Equal(3, lines.Count(l => l.Kind == SwingTrailKind.Blade));         // places 0, 2 and 4
        Assert.Single(lines, l => l.Kind == SwingTrailKind.Reticle);
        Assert.Single(lines, l => l.Kind == SwingTrailKind.Nearest);
        Assert.Single(lines, l => l.Kind == SwingTrailKind.Miss);
        Assert.All(lines, l => Assert.Equal(1f, l.Fade));
        trails.Fill(last + trails.KeepSeconds - 0.5, lines);
        Assert.All(lines, l => Assert.Equal(0.5f, l.Fade, 3));                     // fading over the last second
        trails.Fill(last + trails.KeepSeconds + 0.1, lines);
        Assert.Empty(lines);
        // Someone not viewed: no marks.
        trails.Record(B, 1, 1000, [Blade(0f), Blade(0.2f)], 1f);
        trails.Fill(1000 / (double)World.TicksPerSecond, lines);
        Assert.DoesNotContain(lines, l => l.Kind is SwingTrailKind.Reticle or SwingTrailKind.Nearest or SwingTrailKind.Miss);
        Assert.Equal(1, lines.Count(l => l.Kind == SwingTrailKind.Tip));
    }

    [Fact]
    public void Two_swings_at_once_keep_their_own_places_and_adding_allocates_nothing()
    {
        // Measured as the allocation tests measure (Allocations.None: the window measured more than once, each on
        // fresh trails made the same way; the last ones checked below).
        var trails = new SwingTrails();
        BladePlacement[] a = [Blade(0f), Blade(0f)];
        BladePlacement[] b = [Blade(5f), Blade(5f)];
        Allocations.None("recording two swings' 298 ticks", () =>
        {
            trails = new SwingTrails();
            trails.Begin(A, 1, 0, SwingTrails.Aim.None);
            trails.Begin(B, 1, 0, SwingTrails.Aim.None);
            trails.Record(A, 1, 1, a, 1f);
            trails.Record(B, 1, 1, b, 1f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 2; tick < 300; tick++)
            {
                trails.Record(A, 1, tick, a, 1f);
                trails.Record(B, 1, tick, b, 1f);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
        var places = new List<BladePlacement>();
        Assert.True(trails.Latest(A, out var ta));
        trails.Places(ta, places);
        Assert.All(places, p => Assert.Equal(0f, p.Tip.X));
        Assert.True(trails.Latest(B, out var tb));
        trails.Places(tb, places);
        Assert.All(places, p => Assert.Equal(5f, p.Tip.X));
        Assert.Equal(300, places.Count);                                           // the first tick's two, then one a tick
    }

    // ---- with the 1.1 install: the trail is what contact swept ----

    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    /// <summary>A probe right after contact (Contact/61): each tick, the attacker's blade as contact's sweep ended with it.</summary>
    sealed class BladeProbe : ISystem
    {
        public EntityId Of = EntityId.None;
        public readonly List<(long Tick, BladePlacement Blade)> Seen = [];
        public void Run(World world)
        {
            if (!Of.IsNone && ContactSystem.Striking(world, Of, 0.06f, out var blade)) Seen.Add((world.Tick, blade));
        }
    }

    static (World World, Dictionary<string, int> Kinds, BladeProbe Probe) Make()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>
        {
            ["Christof"] = OneOneLibrary.Register(assets, Library.Value!.Character("Christof", "BroadSword")!),
            ["Civ12male"] = OneOneLibrary.Register(assets, Library.Value!.Character("Civ12male")!),
        };
        var b = new WorldBuilder(7);
        MovementServices.Register(b, new TestLevel().Floor(-60, -60, 60, 60).Build());
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        ContactServices.Register(b, new ContactConfig());
        var probe = new BladeProbe();
        b.System(Phase.Contact, ContactServices.ContactOrder + 1, "test.blade-probe", probe);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        return (w, kinds, probe);
    }

    static EntityId Put(World w, Dictionary<string, int> kinds, string kind, float x, float y, float yawDegrees)
    {
        var e = MovementServices.SpawnBody(w, new(x, y, 0.01f), yawDegrees * DetMath.DegToRad, Locomotion.Sandbox);
        w.Set(e, CharacterAnimation.For(w.Service<CharacterAssets>(), kinds[kind]));
        w.Set(e, MotionInput.Standing);
        CombatServices.Arm(w, e);
        return e;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_swings_trail_is_the_blade_contact_swept_through_its_window_and_ends_at_the_bite(bool target)
    {
        var (w, kinds, probe) = Make();
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        probe.Of = a;
        EntityId t = target ? Put(w, kinds, "Civ12male", 0, 1.3f, -90) : EntityId.None;
        for (int i = 0; i < 30; i++) MovementServices.Step(w, ActionFrame.Idle);
        ref var intent = ref w.Get<CombatIntent>(a);
        (intent.Aims, intent.AimPitch, intent.Attack) = (true, 0f, true);
        MovementServices.Step(w, ActionFrame.Idle);
        w.Get<CombatIntent>(a).Attack = false;
        // Each tick the blade is swept (and not stopped), the trail's newest place is the blade contact's sweep ended with.
        var places = new List<BladePlacement>();
        var trails = w.Service<SwingTrails>();
        var contacts = new List<ContactMade>();
        int swept = 0;
        for (int i = 0; i < 160; i++)
        {
            MovementServices.Step(w, ActionFrame.Idle);
            contacts.AddRange(w.Events<ContactMade>().ThisTick);
            var (tick, blade) = probe.Seen[^1];
            if (!trails.Latest(a, out var now) || now.LastTick != tick || now.Count == 0 || contacts.Count > 0) continue;
            swept++;
            trails.Places(now, places);
            Assert.Equal(blade.Tip, places[^1].Tip);
            Assert.Equal(blade.Guard, places[^1].Guard);
        }
        Assert.True(trails.Latest(a, out var trail));
        trails.Places(trail, places);
        output.WriteLine($"{(target ? "a townsman 1.3 m ahead" : "open ground")}: {places.Count} places over ticks {trail.FirstTick}..{trail.LastTick}; contacts {string.Join(", ", contacts.Select(c => $"{c.Met} {c.Site}"))}");
        Assert.False(trail.HasRay);                                                // nobody views: no aim
        if (!target)
        {
            Assert.Empty(contacts);
            Assert.InRange(places.Count, 30, 45);                                  // chrisAttackSword's window, 0.313 to 0.625 s, a place a tick
            Assert.Equal(places.Count - 1, swept);
        }
        else
        {
            var c = Assert.Single(contacts);
            Assert.Equal(ContactMet.Body, c.Met);
            Assert.Equal(c.Tick, trail.LastTick);                                  // nothing after the bite
            // The blade's last place passes through the struck point (its edge, within its radius and the body's).
            var last = places[^1];
            var ab = last.Tip - last.Guard;
            float s = Math.Clamp(Vector3.Dot(c.Point - last.Guard, ab) / ab.LengthSquared(), 0f, 1f);
            Assert.InRange(Vector3.Distance(last.Guard + ab * s, c.Point), 0f, 0.15f);
        }
    }
}
