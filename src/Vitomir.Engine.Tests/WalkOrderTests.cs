// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The walker's own `simWalker::WalkTo` and the detour's use of it, as the
/// A comparison with 1.1 read them and they were built. WalkTo
/// searches **before** it touches anything, so a walk with no way there leaves the walker on the
/// route it had; a real route sets the waypoint bit and **leaves the detour bit and its
/// held goal**, so a route ordered mid-detour ends by walking back to the old goal; a
/// goal on the walker's own ground clears both. And the detour walks to its spot through
/// the same WalkTo, a search from where the walker stands. And the attack order stops the
/// walk and leaves the close-in's half-second retry alone (gap 4). The scene is
/// <see cref="HiddenTargetTests"/>' monastery stair: a Cappadocian walked into Christof.
/// </summary>
public class WalkOrderTests
{
    /// <summary>
    /// The middle of some piece of ground whose search from where the walker stands answers
    /// <paramref name="want"/>, between <paramref name="near"/> and <paramref name="far"/> units off.
    /// </summary>
    static (Vector3 Point, int Node)? Ground(Player walker, PathResult want, float near, float far)
    {
        foreach (var n in walker.Map.Nodes)
        {
            if (n.Outline.Length == 0) continue;
            var c = (n.Min + n.Max) / 2f;
            if (!n.Contains(c)) continue;
            var p = new Vector3(c.X, c.Y, n.Height(c.X, c.Y));
            float d = Vector3.Distance(p, walker.Thing.Position);
            if (d < near || d > far) continue;
            if (walker.Path!.Find(walker.Thing.Position, walker.Node, p, n.Index, walker.Radius, isActor: false, out _) == want)
                return (p, n.Index);
        }
        return null;
    }

    /// <summary>The Cappadocian walked into Christof until she steps round him (as HiddenTargetTests).</summary>
    static (Simulation Sim, Player Walker) SteppingRound()
    {
        var (_, _, sim, christof, cappadocian) = HiddenTargetTests.Stair();
        var mind = cappadocian.Mind!;
        mind.NextThink = int.MaxValue;                        // only the walker
        var walker = mind.Walker;
        Assert.True(walker.WalkTo(christof.Position, christof.Mind!.Walker.Node));
        for (int i = 0; i < 300 && !walker.Detouring; i++) sim.Step(1f / 60f);
        Assert.True(walker.Detouring, "she never stepped round him");
        return (sim, walker);
    }

    [Fact]
    public void A_walk_with_no_way_there_leaves_the_route_it_had_walking_at_the_new_speed()
    {
        var (_, _, sim, _, cappadocian) = HiddenTargetTests.Stair();
        var mind = cappadocian.Mind!;
        mind.NextThink = int.MaxValue;
        var walker = mind.Walker;
        var far = Ground(walker, PathResult.Path, 150f, 800f) ?? throw new Xunit.Sdk.XunitException("no ground a route away");
        var nowhere = Ground(walker, PathResult.None, 0f, float.MaxValue) ?? throw new Xunit.Sdk.XunitException("no ground without a way to it");
        Assert.True(walker.WalkTo(far.Point, far.Node, speed: 150f));
        sim.Step(1f / 60f);
        var destination = walker.Destination;
        var route = walker.Route;
        int step = walker.Step;

        Assert.False(walker.WalkTo(nowhere.Point, nowhere.Node, speed: 400f));
        Assert.True(walker.Walking, "the refused order stopped the walk (1.1's refusal touches nothing)");
        Assert.Equal(destination, walker.Destination);
        Assert.Same(route, walker.Route);
        Assert.Equal(step, walker.Step);
        float fastest = 0f;
        for (int i = 0; i < 30; i++) { sim.Step(1f / 60f); fastest = Math.Max(fastest, walker.Speed); }
        Assert.Equal(400f, fastest);                          // the speed is written before the search
    }

    [Fact]
    public void A_route_ordered_mid_detour_keeps_the_held_goal_and_walks_back_to_it_at_the_end()
    {
        var (sim, walker) = SteppingRound();
        var held = walker.HeldGoal!.Value;
        var away = Ground(walker, PathResult.Path, 100f, 400f) ?? throw new Xunit.Sdk.XunitException("no ground a route away");

        Assert.True(walker.WalkTo(away.Point, away.Node));
        Assert.True(walker.Detouring, "a route cleared the detour bit (1.1's route only sets bit 2)");
        Assert.Equal(held, walker.HeldGoal);
        Assert.True(walker.Route.Length > 0);

        // When the route ends — arrived, or blocked with the bit still set — the walker's tail
        // takes the held goal up again.
        int i = 0;
        for (; i < 60 * 30 && walker.Destination != held; i++) sim.Step(1f / 60f);
        Assert.True(i < 60 * 30, "never turned back for the goal it had been detouring towards");
        Assert.False(walker.Detouring);
    }

    [Fact]
    public void A_walk_on_the_walkers_own_ground_mid_detour_forgets_the_detour()
    {
        var (_, walker) = SteppingRound();
        var here = Ground(walker, PathResult.Straight, 0f, 400f) ?? throw new Xunit.Sdk.XunitException("no ground straight ahead");

        Assert.True(walker.WalkTo(here.Point, here.Node));
        Assert.False(walker.Detouring);                       // `& ~0xa`
        Assert.Null(walker.HeldGoal);
        Assert.Empty(walker.Route);
    }

    /// <summary>
    /// **The detour's walk is a search**: whatever spot the roll picked, the
    /// walker is on its way there by the search's own answer from where it stood — a route when the
    /// spot is on other ground, a straight leg when it is on the same — and never through a wall.
    /// The Cappadocian is walked into Christof from twelve directions round him on the stair's
    /// landing, and every detour she takes is checked; some of them step onto other ground.
    /// </summary>
    [Fact]
    public void A_detour_walks_to_its_spot_by_the_searchs_answer()
    {
        var (place, _, sim, christof, cappadocian) = HiddenTargetTests.Stair();
        var mind = cappadocian.Mind!;
        var walker = mind.Walker;
        int detours = 0, routes = 0;
        for (int k = 0; k < 12; k++)
        {
            float a = k * MathF.PI / 6f;
            var start = christof.Position + new Vector3(MathF.Cos(a) * 110f, MathF.Sin(a) * 110f, 0f);
            if (walker.Map.Find(start, WalkMap.Reach) < 0) continue;
            walker.Stop();
            place.Move(cappadocian, start);
            walker.Ground();
            mind.NextThink = int.MaxValue;
            if (!walker.WalkTo(christof.Position, christof.Mind!.Walker.Node)) continue;
            Vector3 from = default; int fromNode = -1;
            for (int i = 0; i < 300 && !walker.Detouring && walker.Walking; i++)
            {
                from = walker.Thing.Position; fromNode = walker.Node;
                sim.Step(1f / 60f);
            }
            if (!walker.Detouring) continue;
            detours++;
            var spot = walker.Destination;
            int spotNode = walker.Map.Find(spot, WalkMap.Reach);
            var answer = walker.Path!.Find(from, fromNode, spot, spotNode, walker.Radius, isActor: false, out _);
            Assert.NotEqual(PathResult.None, answer);
            Assert.Equal(answer == PathResult.Path, walker.Route.Length > 0);
            if (answer == PathResult.Path) routes++;
        }
        Assert.True(detours >= 4, $"only {detours} detours");
        Assert.True(routes >= 1, $"none of {detours} detours stepped onto other ground");
    }

    [Fact]
    public void An_attack_order_stops_the_walk_and_waits_out_the_last_closing_walks_retry()
    {
        var (place, _, sim, christof, cappadocian) = HiddenTargetTests.Stair();
        var mind = cappadocian.Mind!;
        mind.NextThink = int.MaxValue;
        var walker = mind.Walker;
        var me = cappadocian.Actor!;
        var far = Ground(walker, PathResult.Path, 150f, 800f) ?? throw new Xunit.Sdk.XunitException("no ground a route away");
        Assert.True(walker.WalkTo(far.Point, far.Node));
        sim.Step(1f / 60f);
        int now = place.Milliseconds;
        me.ActionRetryAt = now + 400;                         // a closing walk ordered 100 ms ago

        Assert.Equal(Rpg.OrderResult.Started, Rpg.Actions.Attack(cappadocian, christof, me.Weapon, 0, now));
        Assert.False(walker.Walking, "the order left the walk running (1.1's order stops it)");
        Assert.Equal(now + 400, me.ActionRetryAt);            // not reset
        while (place.Milliseconds < now + 350) sim.Step(1f / 60f);
        Assert.False(walker.Walking, "closed in before the retry came round");
        while (place.Milliseconds < now + 500) sim.Step(1f / 60f);
        Assert.True(walker.Walking || me.Action != Rpg.ActionMode.Attack, "never closed in once the retry was up");
    }
}
