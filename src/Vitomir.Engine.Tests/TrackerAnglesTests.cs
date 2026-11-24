// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A tracker turns from keyframe 0's orientation** (gap 6 of a comparison with 1.1). A track
/// move's turn is the keyframe's orientation less the tracker's own angles,
/// which every constructor seeds from keyframe 0 and only the tracker
/// itself moves on (a swing leaves just its turn there). Earlier, Vitomir turned
/// from the placed orientation, which is the same for 558 of the 559 shipped things with keyframes.
/// </summary>
public class TrackerAnglesTests
{
    static WorldThing Tracker(Vector3 placed) => new()
    {
        Index = 1, Guid = 1, Scene = new NsdThing(), Template = "t", Type = ThingType.Prop,
        MoveType = WorldThing.MoveTrack,
        Keyframes =
        [
            new NsdKeyframe(new Vector3(0f, 0f, 35f), Vector3.Zero),
            new NsdKeyframe(new Vector3(0f, 0f, 90f), new Vector3(100f, 0f, 0f)),
            new NsdKeyframe(new Vector3(0f, 0f, 0f), new Vector3(200f, 0f, 0f)),
        ],
        Orientation = placed,
    };

    [Fact]
    public void A_track_move_turns_from_keyframe_0_not_from_how_it_was_placed()
    {
        var stone = Tracker(placed: Vector3.Zero);
        Assert.Equal(new Vector3(0f, 0f, 35f), stone.TrackerAngles);

        Assert.True(stone.MoveToFrame(1, 100f));                // one second
        stone.Advance(0.5f);

        // Half-way from 35 to 90, not from 0: the move's first frame snaps it to keyframe 0's angle.
        Assert.Equal(62.5f, stone.Orientation.Z, 3);
        Assert.Equal(stone.Orientation, stone.TrackerAngles);
    }

    [Fact]
    public void The_next_move_turns_from_where_the_last_one_ended_whatever_a_script_set_since()
    {
        var stone = Tracker(placed: Vector3.Zero);
        Assert.True(stone.MoveToFrame(1, 1000f));
        Assert.Equal(1, stone.Advance(1f));
        Assert.Equal(new Vector3(0f, 0f, 90f), stone.TrackerAngles);

        stone.Orientation = new Vector3(0f, 0f, 180f);           // SetOrientation writes no tracker field
        Assert.True(stone.MoveToFrame(2, 100f));
        Assert.Equal(new Vector3(0f, 0f, 90f), stone.Motion!.FromOrientation);
    }

    [Fact]
    public void A_swing_leaves_only_its_turn_in_the_tracker_s_angles()
    {
        var door = Tracker(placed: new Vector3(0f, 0f, 35f));
        Assert.True(door.RotatePivot(1, 1f));                    // keyframe 1's orientation is the turn: 90
        door.Advance(1f);

        Assert.Equal(new Vector3(0f, 0f, 125f), door.Orientation);
        Assert.Equal(new Vector3(0f, 0f, 90f), door.TrackerAngles);
    }
}
