// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A track mover's frame** (<see cref="TrackMoves"/>) — two things the
/// A comparison with 1.1 found. The engine re-finds a moving thing's sector every frame and files it there
/// (a swing through SetTransform), where Vitomir had moved
/// the point only. And at the keyframe it calls `arrived` **before** it stops the `moving` loop and
/// says `stopmove`, and does neither when `arrived` has set the thing moving again —
/// so the conveyers and path movers that chain their legs from `arrived` keep
/// one loop going. The first monastery's own keyframed things, as the scene has them. And a
/// move asked at a speed of 0 or less keeps the speed over the distance as its rate, so
/// it never arrives — standing still at 0, running backwards below it.
/// </summary>
public class TrackMoveTests
{
    static (Location Place, WorldThing Mover, WorldThing Christof) Monastery()
    {
        var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var mover = place.Things.First(t => t.MoveType == WorldThing.MoveTrack && t.Keyframes.Length > 1 && t.InWorld);
        return (place, mover, place.Player!.Thing);
    }

    [Fact]
    public void A_mover_sent_on_from_its_arrival_keeps_its_loop_and_says_no_stopmove()
    {
        var (place, mover, _) = Monastery();
        mover.Position = mover.Keyframes[0].Position;
        mover.Orientation = mover.Keyframes[0].Orientation;
        Assert.True(mover.MoveToFrame(1, 1e6f));
        mover.MovingSound = 77;                                   // the loop the first leg started
        int heard = -1;
        TrackMoves.Step(place, mover, 1f, frame => { heard = frame; mover.MoveToFrame(0, 1e6f); });
        Assert.Equal(1, heard);
        Assert.NotNull(mover.Motion);                             // on its way back already
        Assert.Equal(77, mover.MovingSound);                      // the same loop, never stopped

        heard = -1;
        TrackMoves.Step(place, mover, 1f, frame => heard = frame);
        Assert.Equal(0, heard);
        Assert.Null(mover.Motion);
        Assert.Equal(0, mover.MovingSound);                       // nothing sent it on: the loop stops
    }

    [Fact]
    public void A_mover_that_crosses_into_another_sector_is_filed_there()
    {
        var (place, mover, christof) = Monastery();
        var to = christof.Position + new Vector3(0f, 0f, 40f);
        long there = place.At(to, christof.Sector);
        Assert.SkipWhen(there == mover.Sector, "the mover already stands in Christof's sector");
        mover.Keyframes = [mover.Keyframes[0] with { Position = mover.Position, Orientation = mover.Orientation },
                           new NsdKeyframe(mover.Orientation, to)];
        long from = mover.Sector;
        Assert.True(mover.MoveToFrame(1, 1e6f));
        TrackMoves.Step(place, mover, 1f);
        Assert.Equal(to, mover.Position);
        Assert.Equal(there, mover.Sector);
        if (mover.Collide != CollideShape.None && place.Props is { } bodies)
        {
            Assert.Contains(mover, bodies.In([(int)there]));
            Assert.DoesNotContain(mover, bodies.In([(int)from]).Where(t => t.Sector == from));
        }
    }

    static WorldThing Lift() => new()
    {
        Index = -1, Guid = 5, Scene = new NsdThing(), Template = "lift", Type = ThingType.Prop,
        MoveType = WorldThing.MoveTrack, Position = Vector3.Zero,
        Keyframes = [new NsdKeyframe(Vector3.Zero, Vector3.Zero), new NsdKeyframe(new Vector3(0, 0, 90), new Vector3(100, 0, 0))],
    };

    [Fact]
    public void A_move_at_a_speed_of_zero_never_arrives_and_one_below_zero_runs_backwards()
    {
        // At 0 the rate is 0: a second, a minute on, it has not stirred, and it is still moving.
        var still = Lift();
        Assert.True(still.MoveToFrame(1, 0f));
        for (int i = 0; i < 60; i++) Assert.Equal(-1, still.Advance(1f));
        Assert.Equal(Vector3.Zero, still.Position);
        Assert.NotNull(still.Motion);

        // At −50 over a distance of 100 it goes half its length a second the wrong way, turning back
        // as it goes, and keeps going (the track move has no floor on how far along it is).
        var back = Lift();
        Assert.True(back.MoveToFrame(1, -50f));
        Assert.Equal(-1, back.Advance(1f));
        Assert.Equal(-50f, back.Position.X, 3);
        Assert.Equal(-45f, back.Orientation.Z, 3);
        Assert.Equal(-1, back.Advance(3f));
        Assert.Equal(-200f, back.Position.X, 3);
        Assert.NotNull(back.Motion);

        // Any speed over 0 arrives as ever.
        var lift = Lift();
        Assert.True(lift.MoveToFrame(1, 50f));
        Assert.Equal(-1, lift.Advance(1f));
        Assert.Equal(50f, lift.Position.X, 3);
        Assert.Equal(1, lift.Advance(1.5f));
        Assert.Equal(100f, lift.Position.X, 3);
    }
}
