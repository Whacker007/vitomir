// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// What stopped the player (ours, for the log; <c>BodyMotor.Stops.cs</c>, <see cref="PlayerStopped"/>): walking into
/// a thing the world tells once a stop what it was — its height and depth and the move that would take it (a run's
/// step-over, a sprint's vault, a tap's hop, climb, mantle or grab), a wall, a face marked not to be climbed — and nothing
/// when setting off, turning or running free, nothing for a body that is not the player's, and a new stop after moving on.
/// </summary>
public sealed class PlayerStopTests
{
    static readonly ActionFrame Walk = new(new Vector2(0, 1), Vector2.Zero, Buttons.Walk, default);
    static readonly ActionFrame Back = new(new Vector2(0, -1), Vector2.Zero, Buttons.Walk, default);

    static (World W, EntityId Player) Make(LevelSpace level)
    {
        var w = MovementServices.Register(new WorldBuilder(1), level).Build();
        return (w, MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox));
    }

    static List<PlayerStopped> Run(World w, int ticks, ActionFrame frame, List<PlayerStopped>? told = null)
    {
        told ??= [];
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(w, frame);
            told.AddRange(w.Events<PlayerStopped>().ThisTick);
        }
        return told;
    }

    static TestLevel Floor() => new TestLevel().Floor(-10, -10, 20, 10);

    /// <summary>A thing across the way, its face at x 2, <paramref name="height"/> high and <paramref name="depth"/> deep.</summary>
    static LevelSpace Thing(float height, float depth) => Floor().Box(new(2, -3, 0), new(2 + depth, 3, height)).Build($"thing-{height}-{depth}");

    [Theory]
    [InlineData(0.45f, 0.1f, StopCause.TooHigh, TraversalKind.StepOver, StopTaker.Run)]     // a thin rail: a run steps over it
    [InlineData(0.8f, 0.1f, StopCause.TooHigh, TraversalKind.Hurdle, StopTaker.Sprint)]     // a sprint vaults it
    [InlineData(0.5f, 2f, StopCause.TooHigh, TraversalKind.HopUp, StopTaker.Tap)]           // a tap hops up onto it
    [InlineData(0.9f, 2f, StopCause.TooHigh, TraversalKind.ClimbOnto, StopTaker.Tap)]
    [InlineData(1.5f, 2f, StopCause.TooHigh, TraversalKind.Mantle, StopTaker.Tap)]
    [InlineData(2.6f, 2f, StopCause.TooHigh, TraversalKind.Grab, StopTaker.Tap)]
    [InlineData(4f, 2f, StopCause.Wall, TraversalKind.None, StopTaker.None)]                 // above anything the human reaches
    public void Walking_into_a_thing_tells_once_what_it_was_and_the_move_that_takes_it(float height, float depth, StopCause cause, TraversalKind move, StopTaker by)
    {
        var (w, _) = Make(Thing(height, depth));
        var told = Run(w, 4 * World.TicksPerSecond, Walk);
        var stop = Assert.Single(told);
        Assert.Equal((cause, move, by, Gait.Walk), (stop.Cause, stop.Move, stop.By, stop.Gait));
        Assert.True(stop.Way.X > 0.99f, stop.ToString());
        Assert.True(stop.Feet.X > 2f - 0.3f - BodyMotor.StopLook && stop.Feet.X < 2f - 0.29f, stop.ToString());
        if (cause == StopCause.TooHigh)
        {
            Assert.Equal(height, stop.Height, 2);
            Assert.Equal(MathF.Min(depth, TraversalProbe.DeepEnough), stop.Depth, 1);
        }
    }

    [Fact]
    public void A_face_marked_not_to_be_climbed_is_told_so()
    {
        var built = Floor().Box(new(2, -3, 0), new(4, 3, 1.5f)).Build("marked");
        var level = new LevelSpace("marked", built.Collision.WithClimb(
            LevelClimb.Of([new NoClimbArea("edge", new TurnedBox(new(1.5f, -4f, 1.0f), 0f, new(1.5f, 8f, 1f)))])));
        var (w, _) = Make(level);
        var stop = Assert.Single(Run(w, 4 * World.TicksPerSecond, Walk));
        Assert.Equal(StopCause.NoClimb, stop.Cause);
        Assert.Equal(1.5f, stop.Height, 2);
        Assert.Equal((TraversalKind.None, StopTaker.None), (stop.Move, stop.By));
    }

    [Fact]
    public void A_stop_is_told_once_however_long_and_a_new_one_after_moving_on()
    {
        var (w, _) = Make(Thing(4f, 2f));
        var told = Run(w, 4 * World.TicksPerSecond, Walk);
        Run(w, World.TicksPerSecond, Back, told);
        Run(w, 3 * World.TicksPerSecond, Walk, told);
        Assert.Equal(2, told.Count);
        Assert.True(told[1].Tick - told[0].Tick > World.TicksPerSecond, $"{told[0].Tick}, {told[1].Tick}");
    }

    [Fact]
    public void Setting_off_turning_and_running_on_an_open_floor_tell_nothing()
    {
        var (w, _) = Make(Floor().Build("open"));
        var told = Run(w, 30, ActionFrame.Idle);
        Run(w, 60, Walk, told);
        Run(w, 60, Back, told);
        Run(w, 60, new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.None, default), told);   // a run (always run)
        Run(w, 60, new ActionFrame(new Vector2(0, -1), Vector2.Zero, Buttons.Sprint, default), told);
        Run(w, 30, ActionFrame.Idle, told);
        Assert.Empty(told);
    }

    [Fact]
    public void Climbing_down_from_a_ledge_with_the_way_still_held_tells_no_stop()
    {
        // A walk in play: the tick a move ends the body stands from rest while the player still pushes, and
        // that was told as a stop at "something the level does not show" after each pull-up, climb down and ladder.
        var level = new TestLevel().Floor(-10, -10, 20, 10).Box(new(-10, -3, 0), new(2, 3, 2)).Build("ledge-2");
        var w = MovementServices.Register(new WorldBuilder(1), level).Build();
        MovementServices.SpawnControlled(w, new(0, 0, 2.01f), 0f, Locomotion.Sandbox);
        var down = new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.Walk | Buttons.Crouch, default);
        var told = new List<PlayerStopped>();
        bool climbed = false;
        for (int i = 0; i < 4 * World.TicksPerSecond; i++)
        {
            MovementServices.Step(w, down);
            told.AddRange(w.Events<PlayerStopped>().ThisTick);
            climbed |= w.Events<ClimbedDown>().ThisTick.Count > 0;
        }
        Assert.True(climbed, "it did not climb down");
        Assert.Empty(told);
    }

    [Fact]
    public void A_body_not_the_player_s_walking_into_a_wall_is_not_told()
    {
        var (w, _) = Make(Thing(4f, 2f));
        var other = MovementServices.SpawnBody(w, new(0, -2, 0.01f), 0f, Locomotion.Sandbox);
        var told = new List<PlayerStopped>();
        for (int i = 0; i < 4 * World.TicksPerSecond; i++)
        {
            w.Get<MoveIntent>(other) = Drive.Toward(1, 0, Gait.Walk);
            MovementServices.Step(w, ActionFrame.Idle);
            told.AddRange(w.Events<PlayerStopped>().ThisTick);
        }
        Assert.True(w.Get<Body>(other).Feet.X > 1.6f, "the other body never reached the wall");
        Assert.Empty(told);
    }
}
