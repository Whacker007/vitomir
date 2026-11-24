// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The clock, and the line it draws. Everything that moves on its own moves in the simulation, and
/// the simulation knows nothing about drawing — which is the whole point: a fight has to be able to
/// run with no window open.
/// </summary>
public class SimulationTests
{
    [Fact]
    public void The_engine_does_not_know_about_drawing()
    {
        var referenced = typeof(Location).Assembly.GetReferencedAssemblies()
                                         .Select(a => a.Name ?? "").ToArray();

        // The renderer depends on the engine. Nothing goes back the other way, and SDL only exists
        // on the far side of that line.
        Assert.DoesNotContain("Vitomir.Render", referenced);
        Assert.DoesNotContain(referenced, n => n.Contains("SDL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_engine_type_pretends_to_be_a_renderer()
    {
        // `Scene` holds the CPU-side description of what to draw — meshes, materials, blend modes —
        // which is game state the scripts write to. A namespace called `Render` on this side of the
        // line would mean the split had quietly come undone.
        var namespaces = typeof(Location).Assembly.GetTypes()
                                         .Select(t => t.Namespace ?? "").Distinct().ToArray();
        Assert.DoesNotContain(namespaces, n => n.EndsWith(".Render", StringComparison.Ordinal));
    }

    [Fact]
    public void A_place_runs_with_nothing_drawing_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var minds = place.Awaken(new AiClasses(files));
        var sim = new Simulation(place);

        var start = minds.All.ToDictionary(m => m.Thing.Guid, m => m.Thing.Position);
        sim.Advance(30f, 1f / 30f);

        // The clock kept the time it was given, to the slice.
        Assert.Equal(30f, sim.Seconds, 1);
        // The materials' own clock moved with it, in milliseconds.
        Assert.True(place.Materials.ClockMs >= 29_000f, $"material clock {place.Materials.ClockMs}");
        // And the AI walked people about, with no renderer in the process at all.
        int moved = minds.All.Count(m => Vector3.Distance(start[m.Thing.Guid], m.Thing.Position) > 1f);
        Assert.True(moved > 0, "nobody moved in thirty seconds of simulation");
    }

    [Fact]
    public void A_blow_lands_with_no_window_open()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var sim = new Simulation(place);

        // Two halberd-carrying knights. They are `THING_AF_TALKTO` here, because in this scene they
        // are somebody Christof talks to; the flag is cleared because what is under test is the
        // clock, not who may be attacked.
        var knights = place.Things.Where(t => t.Template.StartsWith("convknight", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, knights.Length);
        var (attacker, target) = (knights[0], knights[1]);
        target.Actor!.Flags &= ~Actions.Unattackable;

        // Inside the halberd's reach, so the order strikes rather than walks.
        target.Position = attacker.Position + new Vector3(10f, 0f, 0f);
        Assert.Equal(OrderResult.Started, Actions.Attack(attacker, target, attacker.Actor!.Weapon, 0, 0));

        float health = target.Actor.Health;
        sim.Advance(5f, 1f / 30f);

        // One swing, and the moment it landed is the frame the artist put the `Strike` tag on —
        // not the frame the order was given and not the frame the animation ended.
        var blow = Assert.Single(sim.Blows);
        Assert.InRange(blow.AtMs, 1, 2000);
        Assert.Same(target, blow.Target);
        // A hit takes health off; a miss does not. Either way the roll happened.
        Assert.Equal(blow.Blow.Hit && blow.Harm.Taken > 0f, target.Actor.Health < health);
        // And the swing is over: one order, one blow, back to standing.
        Assert.Equal(ActionMode.None, attacker.Actor.Action);
    }

    [Fact]
    public void Animations_advance_on_the_clock_not_on_the_frame()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var sim = new Simulation(place);

        var animated = place.Mesh.Animated.FirstOrDefault();
        Assert.NotNull(animated);
        float before = animated!.Animation.Motion?.Player.Frame ?? animated.Animation.Action?.Player.Frame ?? -1f;

        sim.Advance(1f, 1f / 30f);
        float after = animated.Animation.Motion?.Player.Frame ?? animated.Animation.Action?.Player.Frame ?? -1f;
        Assert.True(before != after || before < 0f, "the animation did not move on the clock");
    }
}
