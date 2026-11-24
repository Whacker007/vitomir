// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **What `--walk-to` says became of a walk** (ours, the harness's; <see cref="WalkOutcome"/>). A walk
/// whose legs are all covered used to be printed as "arrived, 0 short of the destination" on a
/// measurement in plan alone, so a walker a storey under its goal, or 23 m under the road (the levels
/// session's reports), had arrived. Pinned: an ended walk is an arrival on the piece of ground it
/// was sent to, or within half a metre of the destination in space; over or under its goal it is
/// "stopped off its goal", with how far above or below; a walker that gave up or is still going reads
/// as before; and the lines themselves, since a walk check reads them: an arrival's
/// is word for word what it was.
/// </summary>
public class WalkOutcomeTests
{
    static readonly Vector3 Goal = new(1000, 2000, 300);

    [Fact]
    public void A_walk_that_ended_on_the_piece_of_ground_it_was_sent_to_arrived()
    {
        var r = WalkOutcome.Of(gaveUp: false, walking: false, at: Goal + new Vector3(0.3f, -0.2f, 0), node: 980, map: null, destination: Goal, sent: 980);
        Assert.Equal(WalkOutcome.Kind.Arrived, r.Kind);
        Assert.Equal("arrived after 14.5s, 2290 units from where it started, 0.4 short of the destination, on node 980",
                     WalkOutcome.Line(r, 14.5f, 2290.2f));
    }

    [Fact]
    public void A_walk_that_ended_on_other_ground_within_half_a_metre_of_its_destination_arrived()
    {
        var r = WalkOutcome.Of(false, false, Goal + new Vector3(20, 0, 10), node: 12, map: null, destination: Goal, sent: 13);
        Assert.Equal(WalkOutcome.Kind.Arrived, r.Kind);
        Assert.Equal("arrived after 3.0s, 500 units from where it started, 20 short of the destination, on node 12", WalkOutcome.Line(r, 3f, 500f));
    }

    [Fact]
    public void A_walk_that_ended_under_its_goal_did_not_arrive()
    {
        // The reported walker: over its destination in plan, 23 m (1,656 units) under the road, on a polygon it was not sent to.
        var r = WalkOutcome.Of(false, false, Goal + new Vector3(0, 0, -1656), node: 490, map: null, destination: Goal, sent: 980);
        Assert.Equal(WalkOutcome.Kind.OffItsGoal, r.Kind);
        Assert.Equal(-1656f, r.Above);
        Assert.Equal("stopped off its goal after 20.0s, 2290 units from where it started, 0 short of the destination, 1656 below it, on node 490",
                     WalkOutcome.Line(r, 20f, 2290f));
        // And one a storey over it.
        var over = WalkOutcome.Of(false, false, Goal + new Vector3(0.5f, 0, 252), node: 7, map: null, destination: Goal, sent: 3);
        Assert.Equal(WalkOutcome.Kind.OffItsGoal, over.Kind);
        Assert.Equal("stopped off its goal after 9.1s, 800 units from where it started, 0.5 short of the destination, 252 above it, on node 7",
                     WalkOutcome.Line(over, 9.1f, 800f));
    }

    [Fact]
    public void A_walker_that_gave_up_or_is_still_going_reads_as_it_always_has()
    {
        var gaveUp = WalkOutcome.Of(true, false, Goal + new Vector3(120, 0, 0), 5, null, Goal, 9);
        Assert.Equal(WalkOutcome.Kind.GaveUp, gaveUp.Kind);
        Assert.Equal("gave up after 30.0s, 40 units from where it started, 120 short of the destination, on node 5", WalkOutcome.Line(gaveUp, 30f, 40f));
        var going = WalkOutcome.Of(false, true, Goal + new Vector3(0, 700.25f, 0), 5, null, Goal, 9);
        Assert.Equal(WalkOutcome.Kind.StillGoing, going.Kind);
        Assert.Equal("still going after 150.0s, 9000 units from where it started, 700.3 short of the destination, on node 5", WalkOutcome.Line(going, 150f, 9000f));
    }
}
