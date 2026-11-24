// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A walker is shown climbing a cell level's steps, not hopping up them** (ours). The new Old
/// Town's stairs are real steps walked as steps: a walker's height changes by a riser (0.17 m, 12.24 units)
/// in the tick it crosses a riser's line, and without this the model and the game camera jolt at every step.
/// These pin <see cref="CellStepEase"/>: the tick of the riser shows no jump; a run up a flight is shown as
/// a climb whose every tick rises less than a third of a riser; ground the walker simply walks up is followed
/// exactly; a walker who stops comes to stand on the step; a fall or a move by a script is shown as it is;
/// only actors in the world are eased; and the game camera looks at where the walker is shown. Where the
/// walker is never changes: the ease writes <see cref="WorldThing.ShownLift"/> and nothing else.
/// </summary>
public class CellStepEaseTests
{
    const float Riser = 12.24f, Tread = 24f, Tick = 1f / 60f;

    static WorldThing Thing(string name, Vector3 at) => new()
    {
        Index = 0, Guid = 9100, Scene = Vitomir.Formats.NsdThing.Placed(name, at, Vector3.Zero, 0),
        Template = name, Type = ThingType.Prop, Position = at,
    };

    [Fact]
    public void The_tick_a_riser_is_crossed_shows_no_jump()
    {
        float lift = CellStepEase.Next(0f, new Vector3(0, 0, 0), new Vector3(0, 0, Riser), 0f);
        Assert.Equal(0f, Riser + lift, 4);          // shown where it was shown the tick before
    }

    [Fact]
    public void A_run_up_a_flight_is_shown_as_a_climb()
    {
        // Christof runs 300 units a second up twelve steps: his true height goes up a riser at a time
        float lift = 0f, shownBefore = 0f, worst = 0f;
        var at = new Vector3(0, 0, 0);
        for (int tick = 0; tick < 60; tick++)
        {
            float x = at.X + 300f * Tick;
            var to = new Vector3(x, 0, MathF.Min(MathF.Floor(x / Tread), 12f) * Riser);
            lift = CellStepEase.Next(lift, at, to, Tick);
            float shown = to.Z + lift;
            worst = MathF.Max(worst, MathF.Abs(shown - shownBefore));
            Assert.InRange(lift, -Riser - 0.01f, 0f);       // never more than a riser behind, never ahead
            (at, shownBefore) = (to, shown);
        }
        Assert.True(worst < Riser / 3f, $"the shown height rose {worst:0.00} units in one tick");
        Assert.Equal(12f * Riser, at.Z, 3);
    }

    [Fact]
    public void Ground_a_walker_walks_up_is_followed_exactly()
    {
        // a street rising one in three: no step in it
        float lift = 0f;
        var at = new Vector3(0, 0, 0);
        for (int tick = 0; tick < 30; tick++)
        {
            var to = at + new Vector3(5f, 0, 5f / 3f);
            lift = CellStepEase.Next(lift, at, to, Tick);
            Assert.Equal(0f, lift);
            at = to;
        }
    }

    [Fact]
    public void A_walker_who_stops_on_a_step_comes_to_stand_on_it()
    {
        var on = new Vector3(0, 0, Riser);
        float lift = CellStepEase.Next(0f, new Vector3(0, 0, 0), on, 0f);
        for (int tick = 0; tick < 16; tick++) lift = CellStepEase.Next(lift, on, on, Tick);      // a little over a quarter of a second
        Assert.Equal(0f, lift);
    }

    [Fact]
    public void Coming_down_is_eased_the_same_way()
    {
        float lift = CellStepEase.Next(0f, new Vector3(0, 0, Riser), new Vector3(0, 0, 0), 0f);
        Assert.Equal(Riser, lift, 4);               // shown still up on the step
        lift = CellStepEase.Next(lift, new Vector3(0, 0, 0), new Vector3(10f, 0, 0), Tick);
        Assert.Equal(Riser - 10f * CellStepEase.Slope - Tick * CellStepEase.Settle, lift, 3);
    }

    [Fact]
    public void A_fall_or_a_move_by_a_script_is_shown_as_it_is()
    {
        Assert.Equal(0f, CellStepEase.Next(-5f, new Vector3(0, 0, 100f), new Vector3(0, 0, 100f - CellStepEase.MaxStep - 1f), Tick));
        Assert.Equal(0f, CellStepEase.Next(-5f, new Vector3(0, 0, 0), new Vector3(CellStepEase.Put + 1f, 0, Riser), Tick));
    }

    [Fact]
    public void Only_actors_in_the_world_are_eased()
    {
        // a thing that is no actor, and one that is in no place: both are left with no lift
        var crate = Thing("crate", new Vector3(0, 0, 0));
        var ease = new CellStepEase();
        ease.Step([crate], Tick);
        crate.Position = new Vector3(0, 0, Riser);
        ease.Step([crate], Tick);
        Assert.Equal(0f, crate.ShownLift);
    }

    [Fact]
    public void The_game_camera_looks_at_where_the_walker_is_shown()
    {
        var walker = Thing("walker", new Vector3(100f, 50f, 20f));
        var camera = new WorldCamera { HeadOf = null };
        camera.Start(walker);
        camera.Tick(Tick, 16);
        float level = camera.Target.Z;
        walker.Position += new Vector3(0, 0, Riser);
        walker.ShownLift = -Riser;                  // the tick of the riser
        camera.Tick(Tick, 32);
        Assert.Equal(level, camera.Target.Z, 3);
        walker.ShownLift = 0f;                      // a thing in a 1.1 level: no lift, the head is where 1.1 has it
        camera.Tick(Tick, 48);
        Assert.Equal(walker.Position.Z + camera.TargetHeight, camera.Target.Z, 3);
    }
}
