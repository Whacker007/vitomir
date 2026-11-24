// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The move order's action**: `rpgAction:MoveTo` makes a walking character
/// ACTION 1, its tick puts it back to nothing the moment the walk stops, a second move
/// order does not cancel the first, and every other order's cancel
/// stops the walk. Earlier, no Vitomir walk set the action at all, so Vitomir's walkers
/// were `none` where 1.1's are `move`, and a flinch or an attack order left a walk running that
/// 1.1 stops.
/// </summary>
public class MoveOrderTests
{
    static (Location Place, Minds Minds, Vfs.GameFiles Files) Load(string level, string? scene = null)
    {
        var files = Install.Open();
        var place = Location.Load(files, level, sky: false, scene: scene, player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1);
        return (place, minds, files);
    }

    /// <summary>
    /// Old Town by day, the townspeople on their errands for twenty seconds: everybody the AI walks
    /// is ACTION 1 while walking and nothing once stopped, as in 1.1 (`move` whenever
    /// it has speed, `none` whenever it has none). The crow flies without the move order
    /// and is never `move`.
    /// </summary>
    [Fact]
    public void A_character_the_AI_walks_is_moving_exactly_as_long_as_the_walk()
    {
        var (place, minds, files) = Load("P1_OTDY", "OTDY_4_1");
        using var _ = files;
        var sim = new Simulation(place);
        int walkingSamples = 0;
        for (int i = 0; i < 30 * 20; i++)
        {
            sim.Step(1f / 30f);
            foreach (var m in minds.All)
            {
                if (ReferenceEquals(m.Thing, place.Player?.Thing) || m.Thing.Actor is not { } a) continue;
                if (m.Mode == AiMode.PedFly) { Assert.NotEqual(ActionMode.Move, a.Action); continue; }
                if (m.Walker.Walking) { walkingSamples++; Assert.Equal(ActionMode.Move, a.Action); }
                else Assert.True(a.Action != ActionMode.Move, $"{m.Thing.Template} {m.Mode} at {i}: {a.Action}, walker gave up {m.Walker.GaveUp}, busy {a.BusyUntil} now {place.Milliseconds}");
            }
        }
        Assert.True(walkingSamples > 100, $"only {walkingSamples} samples of anybody walking");
    }

    /// <summary>
    /// The player's click: the move order's head (<see cref="Actions.ReadyToMove"/>), the walk, and
    /// ACTION 1 (<see cref="MoveOrder.Taken"/>). A second click does not cancel, so the
    /// walk goes on to the new point; the action ends when he gets there.
    /// </summary>
    [Fact]
    public void A_click_walk_is_moving_until_it_arrives_and_a_second_click_does_not_cancel_it()
    {
        var (place, _, files) = Load("P1_OTDY", "OTDY_4_1");
        using var __ = files;
        var player = place.Player!;
        var me = player.Thing.Actor!;
        var sim = new Simulation(place);
        var goal = player.Position + new Vector3(0f, 120f, 0f);
        int node = place.Walk!.Find(goal);
        Assert.True(node >= 0);

        Assert.True(Actions.ReadyToMove(player.Thing));
        Assert.True(player.WalkTo(goal, node));
        MoveOrder.Taken(player.Thing);
        Assert.Equal(ActionMode.Move, me.Action);
        sim.Step(1f / 30f);
        Assert.Equal(ActionMode.Move, me.Action);

        // A second click while walking: no cancel, so no stop — the walk simply re-routes.
        var further = goal + new Vector3(40f, 0f, 0f);
        int node2 = place.Walk.Find(further);
        Assert.True(Actions.ReadyToMove(player.Thing));
        Assert.True(player.Walking);
        Assert.True(player.WalkTo(further, node2));
        MoveOrder.Taken(player.Thing);

        for (int i = 0; i < 30 * 5 && player.Walking; i++) sim.Step(1f / 30f);
        Assert.False(player.Walking);
        sim.Step(1f / 30f);
        Assert.Equal(ActionMode.None, me.Action);
    }

    /// <summary>
    /// **Any other order's cancel stops a walk**: the target, the
    /// pending discipline and the busy time are cleared with it, and — unlike the catch-all arm —
    /// `ef_restrain` is left alone. Once a walking character was ACTION 0 and the cancel did
    /// nothing at all.
    /// </summary>
    [Fact]
    public void Cancelling_a_move_order_stops_the_walk_and_forgets_the_target()
    {
        var (place, _, files) = Load("P1_OTDY", "OTDY_4_1");
        using var __ = files;
        var player = place.Player!;
        var me = player.Thing.Actor!;
        var goal = player.Position + new Vector3(0f, 200f, 0f);
        Assert.True(player.WalkTo(goal, place.Walk!.Find(goal)));
        MoveOrder.Taken(player.Thing);
        me.Target = place.Things.First(t => t.Actor is not null && !ReferenceEquals(t, player.Thing));

        Actions.Cancel(player.Thing);
        Assert.False(player.Walking);
        Assert.Equal(ActionMode.None, me.Action);
        Assert.Null(me.Target);
    }

    /// <summary>
    /// A busy character cannot be cancelled out of anything (the cancel's first test), and so neither can a
    /// move order take one — it answers "not now" before touching the walk.
    /// </summary>
    [Fact]
    public void A_busy_character_is_not_given_a_move_order()
    {
        var (place, _, files) = Load("P1_OTDY", "OTDY_4_1");
        using var __ = files;
        var thing = place.Player!.Thing;
        thing.Actor!.BusyUntil = place.Milliseconds + 500;
        Assert.False(MoveOrder.Ready(thing));
        Assert.False(Actions.ReadyToMove(thing));
    }

    /// <summary>
    /// **The held-button walk**: the ground under a held pointer is ordered again
    /// once it is more than 50 units from the character itself — not from the walk's goal
    /// — without a cancel, and the next look is 200 ms off.
    /// </summary>
    [Fact]
    public void A_held_button_steers_the_walk_and_a_pointer_at_his_feet_does_not()
    {
        var (place, _, files) = Load("P1_OTDY", "OTDY_4_1");
        using var __ = files;
        var player = place.Player!;
        var me = player.Thing.Actor!;
        var goal = player.Position + new Vector3(0f, 200f, 0f);
        Assert.True(player.WalkTo(goal, place.Walk!.Find(goal)));
        MoveOrder.Taken(player.Thing);
        Assert.Equal(place.Milliseconds + MoveOrder.HeldLookMs, me.ActionRetryAt);

        var feet = player.Position + new Vector3(30f, 0f, 0f);
        Assert.False(MoveOrder.Again(player.Thing, player, me, new MoveOrder.Held(feet, place.Walk.Find(feet), false), place.Milliseconds));
        Assert.Equal(goal.Y, player.Destination.Y, 1f);

        var aside = player.Position + new Vector3(150f, 100f, 0f);
        int node = place.Walk.Find(aside);
        Assert.True(node >= 0);
        Assert.True(MoveOrder.Again(player.Thing, player, me, new MoveOrder.Held(aside, node, false), place.Milliseconds));
        Assert.True(Vector2.Distance(new(player.Destination.X, player.Destination.Y), new(aside.X, aside.Y)) < 40f);
        Assert.Equal(ActionMode.Move, me.Action);
    }
}
