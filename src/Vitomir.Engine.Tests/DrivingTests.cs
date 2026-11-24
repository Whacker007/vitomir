// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Driving a character from the keyboard is Vitomir's own addition, so the rule it obeys has to be
/// argued rather than copied from 1.1. The rule is: **it may go wherever clicking could send it, and
/// nowhere else.** These tests are what hold that.
/// </summary>
public class DrivingTests
{
    /// <summary>Which pieces of ground can be stepped between, following the walk graph.</summary>
    static int[] Components(WalkMap walk)
    {
        var comp = new int[walk.Nodes.Length];
        Array.Fill(comp, -1);
        int c = 0;
        for (int i = 0; i < walk.Nodes.Length; i++)
        {
            if (!walk.IsGround(i) || comp[i] >= 0) continue;
            var q = new Queue<int>([i]); comp[i] = c;
            while (q.Count > 0)
                foreach (int n in walk.Nodes[q.Dequeue()].Neighbours)
                    if (walk.IsGround(n) && comp[n] < 0) { comp[n] = c; q.Enqueue(n); }
            c++;
        }
        return comp;
    }

    [Fact]
    public void Driving_can_only_reach_what_the_levels_own_links_reach()
    {
        using var files = Install.Open();
        int levels = 0, edges = 0;
        foreach (var path in files.List("Levels"))
        {
            if (!path.EndsWith(".nil", StringComparison.OrdinalIgnoreCase)) continue;
            NilLevel level;
            try { level = NilLevel.Read(files.Read(path), path); } catch { continue; }
            var walk = new WalkMap(level);
            if (walk.Count == 0) continue;
            levels++;

            // A step of driving lands on the node it started from or one of that node's walk-graph
            // neighbours, and nowhere else (WalkMap.Land). So the whole question is whether every
            // one of those neighbours is somewhere the level's own path links also go — because
            // those links are the graph click-to-move routes over.
            for (int i = 0; i < walk.Nodes.Length; i++)
            {
                if (!walk.IsGround(i)) continue;
                var linked = new HashSet<int>();
                var q = new Queue<int>([i]);
                var seen = new HashSet<int> { i };
                while (q.Count > 0)
                    foreach (var l in level.PathNodes[q.Dequeue()].Links)
                    {
                        int t = (int)l.Target;
                        if (t < 0 || t >= walk.Nodes.Length || !seen.Add(t)) continue;
                        if (walk.IsGround(t)) linked.Add(t); else q.Enqueue(t);   // a riser: keep going
                    }
                foreach (int n in walk.Nodes[i].Neighbours)
                {
                    if (!walk.IsGround(n)) continue;
                    edges++;
                    Assert.True(linked.Contains(n),
                        $"{path}: driving could step from {i} to {n}, which the level's own links do not join");
                }
            }
        }
        Assert.True(levels > 100, $"only {levels} levels read");
        Assert.True(edges > 100_000, $"only {edges} steps checked");
    }

    [Fact]
    public void A_driven_character_gets_down_the_stairs_and_into_the_cellar()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_HAVN", things: true, sky: false, scene: "HAVN_7_7",
                                  player: "Christof");
        var player = place.Player!;
        var walk = player.Map;
        var comp = Components(walk);

        // The cellar: the deepest piece of ground click-to-move will actually route to. Not simply
        // the lowest, because the engine's own 5,000-unit cost cap refuses the far end of a level
        // outright and this test is about driving, not about that cap.
        float Z(int i) { var n = walk.Nodes[i]; return n.Height((n.Min.X + n.Max.X) / 2, (n.Min.Y + n.Max.Y) / 2); }
        Vector3 Middle(int i)
        {
            var node = walk.Nodes[i];
            var mid = Vector2.Zero;
            foreach (var v in node.Outline) mid += v;
            mid /= node.Outline.Length;
            if (!node.Contains(mid)) mid = Vector2.Lerp(mid, node.Outline[0], 0.5f);
            return new Vector3(mid, node.Height(mid.X, mid.Y));
        }

        int spawn = player.Node;
        int cellar = -1;
        Vector3 goal = default;
        foreach (int i in Enumerable.Range(0, walk.Nodes.Length).Where(walk.IsGround).OrderBy(Z))
        {
            var point = Middle(i);
            if (!player.WalkTo(point, i, run: false)) { player.Stop(); continue; }
            cellar = i; goal = point; break;
        }
        Assert.True(cellar >= 0, "click-to-move cannot route anywhere below the spawn");
        Assert.True(Z(cellar) < Z(spawn) - 200f,
                    $"the deepest routable ground is {Z(spawn) - Z(cellar):0} below the spawn, not a cellar");

        // It is one piece of ground with the spawn: the stairs and the thresholds all join up.
        Assert.Equal(comp[spawn], comp[cellar]);

        // And driving really gets there. The test steers along the route click-to-move would have
        // taken, waypoint by waypoint, using the keyboard for every step — because the property
        // under test is "the keyboard can follow wherever a route goes", not "a straight line solves
        // a warren of cellars".
        var route = player.Route.Select(w => w.Point).Append(goal).ToArray();
        player.Stop();
        Assert.True(route.Length > 4, $"the route is only {route.Length} waypoints");

        foreach (var point in route)
        {
            float best = float.MaxValue;
            for (int i = 0; i < 60 * 20; i++)
            {
                var here = new Vector2(player.Thing.Position.X, player.Thing.Position.Y);
                float gap = Vector2.Distance(here, new Vector2(point.X, point.Y));
                best = MathF.Min(best, gap);
                if (gap < 24f) break;
                player.Move(Vector2.Normalize(new Vector2(point.X, point.Y) - here), 1f / 60f);
            }
            Assert.True(best < 24f,
                $"driving stalled {best:0} units short of {point.X:0},{point.Y:0},{point.Z:0} "
                + $"at {player.Thing.Position.X:0},{player.Thing.Position.Y:0},{player.Thing.Position.Z:0}");
        }
        Assert.Equal(cellar, player.Node);
    }

    [Fact]
    public void The_body_test_is_a_fix_and_it_is_off()
    {
        Assert.False(new Fixes().BodyCollision);
        var fixes = new Fixes();
        fixes["bodycollision"] = true;
        Assert.True(fixes.BodyCollision);
        Assert.Contains(fixes.All, f => f.Name == "bodycollision" && f.On);
    }

    /// <summary>
    /// In driving mode at a sprint, Christof sometimes got stuck on
    /// the lip of a doorway: the one into the first room of the monastery, with the chest
    /// and the gold. Between the start and that room the floor is six treads two to four units
    /// deep and then a twelve-unit drop, and a sprint's step jumped a tread and landed on ground
    /// not linked to where it started. Reproduced at 60, 30 and 20 frames a second before the fix.
    /// </summary>
    [Theory]
    [InlineData(60)]
    [InlineData(30)]
    [InlineData(20)]
    public void A_sprint_through_the_monastery_s_first_doorway_does_not_stick(int fps)
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var p = place.Player!;
        var goal = new Vector3(128, 1250, -24);
        Assert.True(p.WalkTo(goal, place.Walk!.Find(goal, WalkMap.Reach), run: true));
        var route = p.Route.Select(w => w.Point).Append(p.Destination).ToArray();
        p.Stop();

        // Drive it — the keyboard held towards each of the route's points in turn, running.
        p.Running = true;
        float dt = 1f / fps;
        int next = 0;
        for (int frame = 0; frame < 20 * fps && next < route.Length; frame++)
        {
            var to = route[next] - p.Thing.Position;
            var flat = new Vector2(to.X, to.Y);
            if (flat.Length() < 20f) { next++; continue; }
            p.Move(Vector2.Normalize(flat), dt);
        }
        Assert.True(next == route.Length, $"stuck at {p.Thing.Position} on the way to point {next} of {route.Length}");
    }

    /// <summary>
    /// **A driven character stopped by a script stands at once** (1.1's stop reselects the
    /// locomotion there and then). In play, driving Christof into the smithy's
    /// region, the script's `Stop()` left his run standing until the next frame chose the stand — over
    /// the talking pose the conversation had just put on him.
    /// </summary>
    [Fact]
    public void A_driven_character_stopped_by_a_script_stands_at_once_and_keeps_a_pose_put_on_after()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var player = place.Player!;
        var animation = player.Thing.Animation!;
        player.Running = true;
        for (int i = 0; i < 20; i++) player.Move(player.Facing, 1f / 60f);
        Assert.StartsWith("run", animation.Mode!.Name);

        player.Stop();                                        // the script's Stop(), as OTDY_4_1.entered calls it
        Assert.StartsWith("stand", animation.Mode!.Name);
        Assert.Equal(0f, player.Speed);

        // The conversation's first line: the talking pose (Conversation.Play, mode 117).
        var talk = player.Motions!.Find(animation.Set, Rpg.Conversation.TalkMode)!.Value;
        animation.Play(talk.Mode, talk.Animation, player.Motions, onMotion: true, rate: Anim.ThingAnimation.Rate);
        // The next frames: the keys still held, but he cannot move (Actions.ReadyToMove), so a zero push.
        for (int i = 0; i < 10; i++) player.Move(System.Numerics.Vector2.Zero, 1f / 60f);
        Assert.Equal("talk", animation.Mode!.Name);
    }
}
