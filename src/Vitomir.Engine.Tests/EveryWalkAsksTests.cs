// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Every walk the move order makes asks whether somebody stands there, but the approach**.
/// Of the sixteen callers of `rpgAction:MoveTo` only the
/// approach pushes flag `0x100`; the rest — the AI's walks, the held-button walk, the
/// click and the host's order — are refused, before anything is touched, when the nearest body at
/// the point is somebody else's (<see cref="MoveOrder.Head"/>). And the follow picks its slot with
/// a query of its own first (<see cref="FreeSpot.Fits"/>): no filters, the
/// member itself left out, so a slot with a body or an item lying in it is skipped — where the move
/// order's test passes both over — and "followed" is answered whatever the order then says. The
/// scene is <see cref="PartyTests"/>' Old Town square.
/// </summary>
public class EveryWalkAsksTests
{
    [Fact]
    public void The_move_orders_head_refuses_a_spot_somebody_stands_on_unless_its_order_allows_it()
    {
        using var files = Install.Open();
        var (place, _, _, member) = PartyTests.Recruit(files, PartyStance.Neutral);
        var christof = place.Player!.Thing;
        var walker = member.Walker;
        member.Thing.Position = christof.Position + new Vector3(300f, 0f, 0f);
        Assert.True(walker.Ground());

        Assert.Equal(1, MoveOrder.Head(member.Thing, walker, christof.Position));
        Assert.Same(christof, walker.Occupant);
        Assert.Equal(0, MoveOrder.Head(member.Thing, walker, christof.Position, anyoneThere: true));  // the approach
        Assert.Null(walker.Occupant);
        Assert.Equal(0, MoveOrder.Head(member.Thing, walker, member.Thing.Position));   // its own body is no refusal
        member.Thing.Actor!.BusyUntil = place.Milliseconds + 1_000;
        Assert.Equal(2, MoveOrder.Head(member.Thing, walker, christof.Position));      // not now comes first
    }

    [Fact]
    public void The_follows_query_counts_the_dead_and_the_items_the_move_orders_test_passes_over()
    {
        using var files = Install.Open();
        var (place, minds, _, member) = PartyTests.Recruit(files, PartyStance.Neutral);
        var christof = place.Player!.Thing;
        var walker = member.Walker;
        member.Thing.Position = christof.Position + new Vector3(300f, 0f, 0f);
        Assert.True(walker.Ground());
        var spot = christof.Position + new Vector3(150f, 0f, 0f);
        Assert.True(FreeSpot.Fits(walker, spot), "the test spot is not clear to start with");

        var body = minds.All.First(m => m != member && m.Thing.Collide != CollideShape.None
                                        && !ReferenceEquals(m.Thing, christof)).Thing;
        body.Actor!.Flags |= Actor.Flag.Dead;
        place.Move(body, spot);
        Assert.Null(FreeSpot.Taken(walker, spot));       // the move order passes over the dead (flag 4)
        Assert.False(FreeSpot.Fits(walker, spot));       // the free-spot query sets no filters

        body.Actor.Flags &= ~Actor.Flag.Dead;
        Assert.Same(body, FreeSpot.Taken(walker, spot));
        place.Move(body, spot + new Vector3(0f, 2_000f, 0f));
        place.Move(member.Thing, spot);
        walker.Ground();
        Assert.True(FreeSpot.Fits(walker, member.Thing.Position));   // the member itself is left out
    }

    [Fact]
    public void A_follower_passes_over_a_slot_with_a_body_lying_in_it()
    {
        using var files = Install.Open();
        var (place, minds, sim, member) = PartyTests.Recruit(files, PartyStance.Neutral);
        var leader = place.Player!.Thing;
        minds.Selected = leader;
        place.Move(member.Thing, leader.Position + new Vector3(600f, 0f, 0f));
        Assert.True(member.Walker.Ground(), "the test spot is not walkable ground");

        // The member's own slot — the first of the fan, straight out towards where it stands.
        var away = member.Thing.Position - leader.Position;
        away.Z = 0f;
        away = Vector3.Normalize(away);
        float reach = Minds.FormationRadius[0] * Minds.Leash(PartyStance.Neutral);
        var first = leader.Position + away * reach;
        float turn = Minds.FormationAngle[1] * MathF.PI / 180f;
        var second = leader.Position + new Vector3(away.X * MathF.Cos(turn) - away.Y * MathF.Sin(turn),
                                                   away.X * MathF.Sin(turn) + away.Y * MathF.Cos(turn), 0f) * reach;
        var body = minds.All.First(m => m != member && m.Thing.Collide != CollideShape.None
                                        && !ReferenceEquals(m.Thing, leader)).Thing;
        body.Actor!.Flags |= Actor.Flag.Dead;
        place.Move(body, first);
        Assert.False(FreeSpot.Fits(member.Walker, first));

        member.NextThink = 0;
        for (int i = 0; i < 30 && !member.Walker.Walking; i++) sim.Step(1f / 30f);
        Assert.True(member.Walker.Walking, "it never set off after its leader");
        var to = member.Walker.Destination;
        Assert.True(Vector2.Distance(new(to.X, to.Y), new(second.X, second.Y))
                    < Vector2.Distance(new(to.X, to.Y), new(first.X, first.Y)),
                    $"it walked to {to}, not the second slot {second} (the first, with the body, is {first})");
    }
}
