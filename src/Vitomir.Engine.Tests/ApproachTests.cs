// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Walking up to a thing in order to do something to it — the approach
/// every order shares.
///
/// These exist because of a playtest: Christof would not fetch a gold bag off a table in the
/// monastery, and had to be walked over by hand. Vitomir asked exactly one question — "what is
/// the ground under it?" — where the engine asks five, and asked it through a window of its own
/// (64 down) where the engine casts a ray (from 50 over the point, 140 long). The bag is
/// 80 over the floor: 1.1's first rung meets that floor, and Vitomir's does
/// (`ExitApproachTests` has the playtest that found the window). The later rungs are pinned here on
/// the same bag lifted out of the ray's reach.
/// </summary>
public class ApproachTests
{
    static (Location Where, GameFiles Files) Enter(string level)
    {
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel(level);
        var loc = Location.Load(files, level, sky: false, scene: where?.Entry?.SceneName, player: "Christof");
        loc.Awaken(new AiClasses(files));
        return (loc, files);
    }

    /// <summary>
    /// The gold bag on the table in the first monastery. **Three** of the level's four gold bags
    /// are on the floor and one is not, and the one that is not is the point — so it is selected by
    /// that rather than by name, which also means the test says out loud what it is about.
    /// </summary>
    static WorldThing Goldbag(Location where) =>
        where.Things.First(t => t.Template.Equals("goldbag", StringComparison.OrdinalIgnoreCase)
                                && where.Walk!.Find(t.Position) < 0);

    [Fact]
    public void The_gold_bag_on_the_table_is_eighty_over_the_floor()
    {
        // The premise, pinned so the rest of the file cannot quietly stop testing anything.
        using var files = Install.Open();
        var (where, f) = Enter("P1_MON1");
        using var _ = f;
        var bag = Goldbag(where);
        // Out of Vitomir's own window (64 down), which is how it is picked out of the four …
        Assert.True(where.Walk!.Find(bag.Position) < 0, "the bag is on the floor after all");
        // … and inside the engine's ray (90 below the point), which is what the approach asks.
        int floor = where.Walk.Beneath(bag.Position, bag);
        Assert.True(floor >= 0, "the engine's ray finds no floor under the bag");
        Assert.Equal(80f, bag.Position.Z - where.Walk.Nodes[floor].Height(bag.Position.X, bag.Position.Y), 1);
        // And it is one of four: the level puts three on the ground and this one on a table, which
        // is why picking the first `goldbag` by name would have tested the wrong thing entirely.
        Assert.Equal(4, where.Things.Count(t => t.Template.Equals("goldbag", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(CollideShape.Cylinder, bag.Collide);
        Assert.True(bag.Radius > 0);
    }

    /// <summary>The bag put out of the ray's reach (over 90 above the floor), so the first rung fails and the ladder is climbed.</summary>
    static WorldThing Lifted(WorldThing bag)
    {
        bag.Position += new Vector3(0f, 0f, 20f);
        return bag;
    }

    [Fact]
    public void The_approach_walks_at_the_floor_under_the_bag()
    {
        // The first rung, by the engine's ray: the floor under the table, at the bag's own x and y
        // and the floor's height there. The walker's collision stops him at the table.
        var (where, files) = Enter("P1_MON1");
        using var _ = files;
        var bag = Goldbag(where);
        var player = where.Player!;

        var (to, node) = Actions.Toward(player.Thing, bag);
        Assert.True(node >= 0, "nowhere to stand to reach the gold bag");
        Assert.Equal(bag.Position.X, to.X);
        Assert.Equal(bag.Position.Y, to.Y);
        Assert.Equal(where.Walk!.Nodes[node].Height(to.X, to.Y), to.Z, 3);
        Assert.True(to.Z < bag.Position.Z, $"destination {to} is still level with the bag at {bag.Position}");
    }

    [Fact]
    public void And_out_of_the_rays_reach_the_ladder_finds_somewhere_to_stand_anyway()
    {
        var (where, files) = Enter("P1_MON1");
        using var _ = files;
        var bag = Lifted(Goldbag(where));
        var player = where.Player!;
        Assert.True(where.Walk!.Beneath(bag.Position, bag) < 0, "the lifted bag is still within the ray");

        var (to, node) = Actions.Toward(player.Thing, bag);
        Assert.True(node >= 0, "nowhere to stand to reach the gold bag");
        // It is the same spot, stepped back toward the walker and dropped to the walker's height:
        // the floor beside the table, not the table top.
        float back = Vector2.Distance(new Vector2(to.X, to.Y), new Vector2(bag.Position.X, bag.Position.Y));
        Assert.InRange(back, 1f, bag.Radius + player.Thing.Radius + Actions.ApproachMargin + 0.01f);
        Assert.True(to.Z < bag.Position.Z, $"destination {to} is still level with the bag at {bag.Position}");
    }

    [Fact]
    public void So_the_order_starts_and_the_character_actually_moves()
    {
        var (where, files) = Enter("P1_MON1");
        using var _ = files;
        var bag = Goldbag(where);
        var player = where.Player!;

        // Stand him somewhere he can reach the bag from, but not next to it.
        var (to, node) = Actions.Toward(player.Thing, bag);
        Assert.True(node >= 0);

        var started = Actions.Fetch(player.Thing, bag, nowMs: 0);
        Assert.Equal(OrderResult.Started, started);
        Assert.True(player.Walking, "the order started but nobody is walking");
        Assert.Equal(ActionMode.Take, player.Thing.Actor!.Action);
    }

    [Fact]
    public void A_character_gets_no_fallback_spot_the_way_a_prop_does()
    {
        // Types 0 and 7 leave before the back-off. The engine will not invent a place to
        // stand next to a person — if they are somewhere you cannot walk, you cannot reach them.
        var (where, files) = Enter("P1_MON1");
        using var _ = files;
        var player = where.Player!;
        var someone = where.Things.First(t => t.IsActor && !ReferenceEquals(t, player.Thing));

        // Put them somewhere with no ground under them at all, high above the level.
        someone.Position = someone.Position with { Z = someone.Position.Z + 100_000f };
        Assert.True(where.Walk!.Find(someone.Position) < 0);
        Assert.Equal(-1, Actions.Toward(player.Thing, someone).Node);

        // A prop in the same impossible place is still refused — the fallbacks look near it, not
        // near the walker — so this is about the rung, not about the height.
        var prop = where.Things.First(t => t.Type == ThingType.Prop);
        prop.Position = someone.Position;
        Assert.Equal(-1, Actions.Toward(player.Thing, prop).Node);
    }

    [Fact]
    public void Something_standing_on_the_floor_is_walked_at_where_it_is()
    {
        // The first rung, and the one that has always worked: no back-off, no height change.
        var (where, files) = Enter("P1_MON1");
        using var _ = files;
        var player = where.Player!;
        var grounded = where.Things.First(t => t.Item is not null && where.Walk!.Find(t.Position) >= 0);

        var (to, node) = Actions.Toward(player.Thing, grounded);
        Assert.True(node >= 0);
        Assert.Equal(grounded.Position.X, to.X);
        Assert.Equal(grounded.Position.Y, to.Y);
        Assert.Equal(where.Walk!.Nodes[node].Height(to.X, to.Y), to.Z, 3);      // on the surface's plane
    }

    /// <summary>
    /// **The approach keeps the surface it found on the target** (the thing's surface; the
    /// A comparison with 1.1's gap): the first approach to a bag out of the ray's reach finds the floor
    /// beside it by the ladder's later rungs, and every one after that takes the first rung — that floor, at
    /// **the bag's own x and y**, at the floor's height there. The bag moved, the kept
    /// surface goes (ours: what the engine's upkeep of the pointer comes to).
    /// </summary>
    [Fact]
    public void Later_approaches_aim_under_the_target_on_the_surface_the_first_one_found()
    {
        var (where, files) = Enter("P1_MON1");
        using var _ = files;
        var bag = Lifted(Goldbag(where));
        var player = where.Player!;

        var (first, node) = Actions.Toward(player.Thing, bag);
        Assert.True(node >= 0);
        Assert.NotEqual(new Vector2(bag.Position.X, bag.Position.Y), new Vector2(first.X, first.Y));

        var (again, same) = Actions.Toward(player.Thing, bag);
        Assert.Equal(node, same);
        Assert.Equal(bag.Position.X, again.X);
        Assert.Equal(bag.Position.Y, again.Y);
        Assert.Equal(where.Walk!.Nodes[node].Height(bag.Position.X, bag.Position.Y), again.Z, 3);
        Assert.True(again.Z < bag.Position.Z);

        bag.Position += new Vector3(1f, 0f, 0f);
        var (moved, _) = Actions.Toward(player.Thing, bag);
        Assert.NotEqual(bag.Position.X, moved.X);                 // searched afresh: the ladder again
    }

    /// <summary>
    /// And the gold bag is still fetched with the cache in place: the retries aim under it, the
    /// walker is stopped at the table, and the take's reach is met from there.
    /// </summary>
    [Fact]
    public void The_gold_bag_on_the_table_is_still_fetched()
    {
        var (where, files) = Enter("P1_MON1");
        using var _ = files;
        var bag = Goldbag(where);
        var player = where.Player!;
        var sim = new Simulation(where);
        Assert.Equal(OrderResult.Started, Actions.Fetch(player.Thing, bag, sim.Milliseconds));
        for (int i = 0; i < 30 && bag.InWorld; i++) sim.Advance(1f, 1f / 30f);
        Assert.False(bag.InWorld, $"Christof stood at {player.Thing.Position}, the bag at {bag.Position}");
    }

    [Fact]
    public void Reaching_something_you_are_standing_on_top_of_has_no_line_to_back_off_along()
    {
        var (where, files) = Enter("P1_MON1");
        using var _ = files;
        var player = where.Player!;
        var bag = Goldbag(where);
        bag.Position = player.Thing.Position with { Z = player.Thing.Position.Z + 500f };

        // Directly overhead: the first rung fails and there is no direction to step back in. The
        // engine normalises a zero vector here and gets NaNs; Vitomir answers "nowhere" instead,
        // which is the same refusal without the arithmetic.
        Assert.Equal(-1, Actions.Toward(player.Thing, bag).Node);
    }

    [Fact]
    public void A_thing_marked_BLOCKSELECT_swallows_the_click_it_cannot_take()
    {
        // The rollover's own iteration stops at one of these, and the click then
        // substitutes `iVar1 = 1` for the whole click dispatch — so the click reaches neither the
        // blocker nor the ground behind it. The monastery's raising doors are built exactly this
        // way (`NO HIGHLIGHT` **and** `BLOCKSELECT`), which is why clicking one does nothing at
        // all in the original, and why Vitomir used to walk Christof into the door instead.
        var (where, files) = Enter("P1_MON1");
        using var _ = files;
        var player = where.Player!.Thing;
        var door = where.Things.First(t => t.Has(WorldThing.Flag.BlockSelect));
        var reachable = where.Things.First(t => t.Type == ThingType.Prop && !t.Has(WorldThing.Flag.BlockSelect)
                                                && t.Collide != CollideShape.None);

        // Looking straight down the line at the blocker, with the other thing behind it.
        var from = door.Position - Vector3.Normalize(door.Position - player.Position) * 400f;
        reachable.Position = door.Position + Vector3.Normalize(door.Position - from) * 200f;
        var ray = Vector3.Normalize(door.Position - from);

        var taken = Pick.NearestThing([door, reachable], from, ray, out bool blocked,
                                      ignore: player, offers: t => !t.Has(WorldThing.Flag.NoHighlight)
                                                                   && !t.Has(WorldThing.Flag.BlockSelect));
        Assert.True(blocked, "the blocker did not block");
        Assert.Null(taken);

        // And it only blocks what is behind it: the same thing in front is still reached.
        reachable.Position = from + ray * 100f;
        Assert.NotNull(Pick.NearestThing([door, reachable], from, ray, out bool past,
                                         ignore: player,
                                         offers: t => !t.Has(WorldThing.Flag.BlockSelect)));
        Assert.False(past);
    }

    [Fact]
    public void The_monastery_s_raising_doors_are_the_ones_built_that_way()
    {
        // Not invented for the test: the level places two, both `door_raise1_nhl`, both driven by
        // a `DoorSwitchPivot` script that is watching a switch somewhere else entirely.
        var (where, files) = Enter("P1_MON1");
        using var _ = files;
        var blockers = where.Things.Where(t => t.Has(WorldThing.Flag.BlockSelect)).ToArray();
        Assert.NotEmpty(blockers);
        Assert.All(blockers, t => Assert.True(t.Has(WorldThing.Flag.NoHighlight),
                                              $"{t.Template} blocks selection but is still highlighted"));
        Assert.Contains(blockers, t => t.Script == "DoorSwitchPivot");
    }

    [Fact]
    public void The_three_orders_wait_three_different_lengths()
    {
        // Not a detail worth inventing, and all three are the engine's own: the attack's approach adds 500 to
        // the retry time, the use's 250, the take's 150.
        Assert.Equal(500, Actions.ApproachRetryMs);
        Assert.Equal(250, Actions.UseRetryMs);
        Assert.Equal(150, Actions.TakeRetryMs);
    }
}

/// <summary>
/// Leaving a place. `CodexSequence.TakeExit` is what every `ClickExit` in the game calls — 216
/// things across the campaign — and its engine half turns out to be much smaller than the feature
/// it belongs to: validate, remember, and set a state the main loop will notice.
/// </summary>
public class TakeExitTests
{
    static Chronicle Prague()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1");
        Assert.NotNull(where);
        var chronicle = where!.Value.Chronicle;
        chronicle.Current = chronicle.Find(where.Value.Entry!.Name);
        Assert.True(chronicle.Current >= 0);
        return chronicle;
    }

    [Fact]
    public void An_exit_that_leads_somewhere_is_remembered_rather_than_walked_through()
    {
        var chronicle = Prague();
        int here = chronicle.Current;
        int exit = Enumerable.Range(0, 8).First(n => chronicle.IsExitValid(chronicle.NameOf(here), n));

        Assert.Null(chronicle.Leaving);
        Assert.True(chronicle.TakeExit(exit));
        var going = chronicle.Leaving;
        Assert.NotNull(going);
        Assert.Equal(exit, going!.Value.Exit);
        Assert.True(going.Value.To >= 0);
        // Recorded, and nothing has moved: the exit request sets a state and returns.
        Assert.Equal(here, chronicle.Current);

        // What the main loop's state 4 would do with it.
        Assert.Equal(going, chronicle.Arrive());
        Assert.Equal(going.Value.To, chronicle.Current);
        Assert.Null(chronicle.Leaving);
    }

    [Fact]
    public void A_closed_exit_a_missing_one_and_one_that_leads_nowhere_are_all_refused_in_silence()
    {
        var chronicle = Prague();
        string here = chronicle.NameOf(chronicle.Current);
        int exit = Enumerable.Range(0, 8).First(n => chronicle.IsExitValid(here, n));

        // Past the end of the list.
        Assert.False(chronicle.TakeExit(999));
        Assert.False(chronicle.TakeExit(-1));
        Assert.Null(chronicle.Leaving);

        // Shut — and the story shuts exits all the time, so this is the live case.
        chronicle.SetExitClosed(here, exit, true);
        Assert.False(chronicle.TakeExit(exit));
        Assert.Null(chronicle.Leaving);
        chronicle.SetExitClosed(here, exit, false);
        Assert.True(chronicle.TakeExit(exit));

        // Leading nowhere.
        chronicle.Arrive();
        chronicle.Current = chronicle.Find(here);
        Assert.True(chronicle.ChangeExit(here, exit, here, 0));
        chronicle.At(here)!.Exits[exit].To = -1;
        Assert.False(chronicle.TakeExit(exit));
    }
}
