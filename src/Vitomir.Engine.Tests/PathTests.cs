// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Click-to-move: the search, the crossing points it picks, the one test a click has to pass, and
/// a character actually getting there. The rules are the original game's path search and walker;
/// these run over the whole game, because a rule is worth only as much as
/// the data agrees with it.
/// </summary>
public class PathTests
{
    static string[] Levels(Vfs.GameFiles files) =>
        [.. files.List("levels/").Where(n => n.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                 .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order()];

    /// <summary>How near the click a walk must end to count as having got there: two of Christof's radii.</summary>
    const float ArrivedWithin = 64f;

    static Vector3 Middle(WalkNode node)
    {
        var c = Vector2.Zero;
        foreach (var v in node.Outline) c += v;
        c /= node.Outline.Length;
        return new Vector3(c, node.Height(c.X, c.Y));
    }

    /// <summary>
    /// What link flag 8 actually marks. The loader flags a node whose link carries it
    /// and the route builder then leaves that node out of the route — and it turns out those nodes
    /// are, almost exactly, the Walk-flagged surfaces standing on their edge: the step risers and
    /// wall faces nobody can stand on. 7,816 nodes carry the flag and every one of them faces
    /// sideways; of the 7,817 sideways-facing nodes only one does not carry it.
    /// </summary>
    [Fact]
    public void Pass_through_marks_the_surfaces_nobody_can_stand_on()
    {
        using var files = Install.Open();
        long sideways = 0, pass = 0, both = 0, passOnly = 0, sidewaysOnly = 0, nodes = 0, links = 0;
        var flags = new Dictionary<ushort, long>();
        foreach (var name in Levels(files))
        {
            var level = NilLevel.Read(files.Read($"levels/{name}.nil"), name);
            var normal = new float[level.PathNodes.Length];
            Array.Fill(normal, float.NaN);
            foreach (var sector in level.Sectors)
                foreach (var surface in sector.Surfaces)
                    if (surface.PathNode >= 0 && surface.PathNode < normal.Length && float.IsNaN(normal[surface.PathNode]))
                        normal[surface.PathNode] = sector.SurfacePlanes[surface.Plane].Normal.Z;
            nodes += level.PathNodes.Length;
            for (int i = 0; i < level.PathNodes.Length; i++)
            {
                bool up = !float.IsNaN(normal[i]) && normal[i] < WalkMap.MinFloorNormalZ;
                bool through = false;
                foreach (var link in level.PathNodes[i].Links)
                {
                    links++;
                    flags[link.Flags] = flags.GetValueOrDefault(link.Flags) + 1;
                    if ((link.Flags & PathFinder.LinkPassThrough) != 0) through = true;
                }
                if (up) sideways++;
                if (through) pass++;
                if (up && through) both++;
                if (through && !up) passOnly++;
                if (up && !through) sidewaysOnly++;
            }
        }
        Assert.Equal((43_083L, 135_319L), (nodes, links));
        Assert.Equal((7_817L, 7_816L, 7_816L), (sideways, pass, both));
        Assert.Equal(0L, passOnly);
        Assert.Equal(1L, sidewaysOnly);
        // And the flag that bars ordinary characters but not the player is on four links in the
        // whole game, which is why nobody has ever noticed it.
        Assert.Equal(4L, flags.Where(f => (f.Key & PathFinder.LinkNoActors) != 0).Sum(f => f.Value));
    }

    /// <summary>
    /// Every crossing point the search can pick, over every link in the game, stays on its own edge
    /// and half a unit clear of both ends — except on the 2,562 edges shorter than two margins,
    /// where the margin wins and the point can sit just past the end. That is the engine's own
    /// arithmetic (the clamp goes to the margin first and the length second), not a rounding slip.
    /// </summary>
    [Fact]
    public void A_crossing_point_stays_on_its_edge()
    {
        using var files = Install.Open();
        long offEdge = 0, offShort = 0, insideMargin = 0, shortEdges = 0, tested = 0;
        foreach (var name in Levels(files))
        {
            var level = NilLevel.Read(files.Read($"levels/{name}.nil"), name);
            var random = new Random(3);
            foreach (var node in level.PathNodes)
                foreach (var link in node.Links)
                {
                    tested++;
                    if (link.Length < 2 * PathFinder.EdgeMargin) shortEdges++;
                    var from = new Vector3((float)random.NextDouble() * 2000 - 1000,
                                           (float)random.NextDouble() * 2000 - 1000, 0);
                    var direction = Vector3.Normalize(new Vector3((float)random.NextDouble() - 0.5f,
                                                                  (float)random.NextDouble() - 0.5f, 0.01f));
                    var point = PathFinder.Cross(link, from, direction, 32f);
                    float along = Vector3.Dot(point - link.Origin, link.Direction);
                    if (along < -0.01f || along > link.Length + 0.01f)
                    {
                        offEdge++;
                        if (link.Length < 1f) offShort++;
                    }
                    else if (link.Length > 2 * PathFinder.EdgeMargin &&
                             (along < PathFinder.EdgeMargin - 0.01f || along > link.Length - PathFinder.EdgeMargin + 0.01f))
                        insideMargin++;
                }
        }
        Assert.Equal(135_319L, tested);
        Assert.Equal(0L, insideMargin);
        Assert.Equal(2_400L, offEdge);
        Assert.Equal(offEdge, offShort);              // every one of them on a sub-unit edge
        Assert.Equal(2_562L, shortEdges);
    }

    /// <summary>
    /// The engine will not pay more than 5,000 units for a route (`moveWalk::WalkTo`), so
    /// a click far enough away is simply refused and nobody moves. This is the shape of that, and
    /// it is the single biggest thing the original's movement does that looks like a bug: past
    /// about two thousand units the answer is usually no.
    /// </summary>
    [Fact]
    public void The_cost_cap_is_what_refuses_a_long_click()
    {
        using var files = Install.Open();
        float[] edges = [500, 1000, 2000, 3000, 5000, float.MaxValue];
        var tried = new long[6];
        var routed = new long[6];
        foreach (var name in Levels(files))
        {
            var level = NilLevel.Read(files.Read($"levels/{name}.nil"), name);
            var map = new WalkMap(level);
            var finder = new PathFinder(level);
            var ground = Enumerable.Range(0, map.Nodes.Length).Where(i => map.Nodes[i].Outline.Length > 0).ToArray();
            if (ground.Length < 4) continue;
            var random = new Random(11);
            for (int k = 0; k < 40; k++)
            {
                int a = ground[random.Next(ground.Length)], b = ground[random.Next(ground.Length)];
                if (a == b) continue;
                var (from, to) = (Middle(map.Nodes[a]), Middle(map.Nodes[b]));
                int band = 0;
                while (Vector3.Distance(from, to) > edges[band]) band++;
                tried[band]++;
                if (finder.Find(from, a, to, b, 32f, isActor: false, out _) != PathResult.None) routed[band]++;
            }
        }
        Assert.Equal(4_283L, tried.Sum());
        Assert.Equal(3_174L, routed.Sum());
        // Close by it nearly always works; past five thousand units it never does, anywhere.
        Assert.True(routed[0] * 100 / tried[0] >= 95, $"{routed[0]} of {tried[0]} short clicks routed");
        Assert.Equal(0L, routed[5]);
        Assert.Equal(130L, tried[5]);
    }

    /// <summary>
    /// A route never crosses a surface nobody could stand on, even though the graph is full of
    /// them: the route builder drops exactly those nodes.
    /// </summary>
    [Fact]
    public void A_route_never_aims_at_a_surface_you_cannot_stand_on()
    {
        using var files = Install.Open();
        long routes = 0, waypoints = 0, unstandable = 0;
        foreach (var name in Levels(files))
        {
            var level = NilLevel.Read(files.Read($"levels/{name}.nil"), name);
            var map = new WalkMap(level);
            var finder = new PathFinder(level);
            var ground = Enumerable.Range(0, map.Nodes.Length).Where(i => map.Nodes[i].Outline.Length > 0).ToArray();
            if (ground.Length < 4) continue;
            var random = new Random(11);
            for (int k = 0; k < 40; k++)
            {
                int a = ground[random.Next(ground.Length)], b = ground[random.Next(ground.Length)];
                if (a == b) continue;
                if (finder.Find(Middle(map.Nodes[a]), a, Middle(map.Nodes[b]), b, 32f, false, out var route) != PathResult.Path) continue;
                routes++;
                foreach (var step in route)
                {
                    waypoints++;
                    if (map.Nodes[step.Node].Outline.Length == 0) unstandable++;
                }
            }
        }
        Assert.Equal(3_174L, routes);
        Assert.Equal(46_232L, waypoints);
        Assert.Equal(0L, unstandable);
    }

    /// <summary>The same question twice gets the same route — the open list ties are broken the way
    /// the engine breaks them, so this is not a given.</summary>
    [Fact]
    public void The_same_search_gives_the_same_route_twice()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: false, sky: false);
        var map = place.Walk!;
        var finder = place.Paths!;
        var ground = Enumerable.Range(0, map.Nodes.Length).Where(i => map.Nodes[i].Outline.Length > 0).ToArray();
        var random = new Random(2);
        int compared = 0;
        for (int k = 0; k < 60; k++)
        {
            int a = ground[random.Next(ground.Length)], b = ground[random.Next(ground.Length)];
            if (a == b) continue;
            var (from, to) = (Middle(map.Nodes[a]), Middle(map.Nodes[b]));
            var first = finder.Find(from, a, to, b, 32f, false, out var one);
            var second = finder.Find(from, a, to, b, 32f, false, out var two);
            Assert.Equal(first, second);
            Assert.Equal(one, two);
            compared++;
        }
        Assert.True(compared > 40, $"only {compared} routes compared");
    }

    /// <summary>
    /// The one that matters, and the one that was missing: a ray cast through a pixel has to come
    /// back to **that pixel** when its hit point is projected through the camera again. Without it
    /// the vertical was inverted for a whole batch — every click landed mirrored about the middle
    /// of the screen — and every other test here still passed, because they only ever asked whether
    /// the hit was on the surface it claimed, which a wrong ray answers just as well.
    /// </summary>
    [Fact]
    public void A_ray_through_a_pixel_comes_back_to_that_pixel()
    {
        using var files = Install.Open();
        const int width = 1280, height = 720;
        long tested = 0;
        float worst = 0;
        foreach (var name in new[] { "P1_OTDY", "l1_east", "P1_CTDY" })
        {
            var place = Location.Load(files, name, things: false, sky: false, player: "Christof");
            var player = place.Player!;
            var map = place.Walk!;
            var ground = Enumerable.Range(0, map.Nodes.Length).Where(i => map.Nodes[i].Outline.Length > 3).ToArray();
            var random = new Random(9);
            for (int k = 0; k < 20 && ground.Length > 0; k++)
            {
                player.Thing.Position = Middle(map.Nodes[ground[random.Next(ground.Length)]]);
                if (!player.Ground()) continue;
                var camera = place.Camera();
                camera.Pitch = FreeCamera.ChasePitch;
                camera.Yaw = random.Next(360);
                camera.Orbit(player.Position + new Vector3(0, 0, FreeCamera.ChaseHeight), FreeCamera.ChaseDistance, place.Level);
                var viewProjection = camera.ViewProjection((float)width / height);
                for (int gy = 1; gy < 8; gy++)
                    for (int gx = 1; gx < 8; gx++)
                    {
                        float px = gx * width / 8f, py = gy * height / 8f;
                        var (origin, direction) = Pick.Ray(camera, px, py, width, height);
                        if (Pick.Nearest(place.Level, origin, direction) is not { } spot) continue;
                        tested++;
                        var clip = Vector4.Transform(new Vector4(spot.Point, 1f), viewProjection);
                        Assert.True(clip.W > 0, "the hit is behind the camera that saw it");
                        float backX = (clip.X / clip.W + 1) * 0.5f * width;
                        float backY = (1 - clip.Y / clip.W) * 0.5f * height;
                        worst = MathF.Max(worst, MathF.Sqrt((backX - px) * (backX - px) + (backY - py) * (backY - py)));
                    }
            }
        }
        Assert.True(tested > 2_000, $"only {tested} rays hit anything");
        Assert.True(worst < 2f, $"a ray came back {worst:0.#} pixels from where it started");
    }

    /// <summary>
    /// The camera has to stay out of the level, because a click's ray starts where the camera is.
    /// The engine pulls it in with a fat ray; ours with a thin one and the same 10 units
    /// of clearance. Left free it sits inside geometry 14% of the time with the player out of sight
    /// half the time, and then half of all clicks land on the inside of a wall.
    /// </summary>
    [Fact]
    public void The_chase_camera_stays_out_of_the_level()
    {
        using var files = Install.Open();
        long placements = 0, insideFree = 0, insideClipped = 0, blockedFree = 0, blockedClipped = 0;
        foreach (var name in new[] { "P1_OTDY", "l1_east", "P1_HAVN", "P1_CTDY" })
        {
            var place = Location.Load(files, name, things: false, sky: false, player: "Christof");
            var player = place.Player!;
            var map = place.Walk!;
            var ground = Enumerable.Range(0, map.Nodes.Length).Where(i => map.Nodes[i].Outline.Length > 3).ToArray();
            var random = new Random(4);
            for (int k = 0; k < 60 && ground.Length > 0; k++)
            {
                player.Thing.Position = Middle(map.Nodes[ground[random.Next(ground.Length)]]);
                if (!player.Ground()) continue;
                var eye = player.Position + new Vector3(0, 0, FreeCamera.ChaseHeight);
                var look = player.Position + new Vector3(0, 0, 48);
                for (int a = 0; a < 4; a++)
                {
                    placements++;
                    var camera = place.Camera();
                    camera.Pitch = FreeCamera.ChasePitch;
                    camera.Yaw = a * 90f;

                    camera.Orbit(eye, FreeCamera.ChaseDistance);
                    if (place.Solid!.Solid(camera.Position)) insideFree++;
                    if (Blocked(place, camera.Position, look)) blockedFree++;

                    camera.Orbit(eye, FreeCamera.ChaseDistance, place.Level);
                    if (place.Solid.Solid(camera.Position)) insideClipped++;
                    if (Blocked(place, camera.Position, look)) blockedClipped++;
                }
            }
        }
        Assert.True(placements > 500, $"only {placements} camera placements");
        // Left free it is bad; clipped it is all but perfect.
        Assert.True(insideFree * 20 > placements, $"expected the free camera to be inside geometry often, was {insideFree} of {placements}");
        Assert.True(blockedFree * 4 > placements, $"expected the free camera to be blocked often, was {blockedFree} of {placements}");
        Assert.True(insideClipped * 100 < placements, $"clipped camera still inside geometry {insideClipped} of {placements}");
        Assert.True(blockedClipped * 50 < placements, $"clipped camera still blocked {blockedClipped} of {placements}");

        static bool Blocked(Location place, Vector3 from, Vector3 to)
        {
            var away = to - from;
            float want = away.Length();
            return Pick.Nearest(place.Level, from, Vector3.Normalize(away)) is { } hit && hit.Distance < want - 8f;
        }
    }

    /// <summary>
    /// Only walkable floor accepts a click. Rays cast at a level from its own start hit walls,
    /// ceilings and floors alike, and the walkable ones are the only ones the original acts on.
    /// </summary>
    [Fact]
    public void Only_walkable_floor_accepts_a_click()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: false, sky: false);
        var camera = place.Camera();
        int hits = 0, walkable = 0, notWalkable = 0, missed = 0;
        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 16; x++)
            {
                var (origin, direction) = Pick.Ray(camera, x * 80 + 40, y * 80 + 40, 1280, 720);
                var hit = Pick.Nearest(place.Level, origin, direction);
                if (hit is not { } spot) { missed++; continue; }
                hits++;
                if (spot.Walkable) { walkable++; Assert.True(spot.PathNode >= 0); } else notWalkable++;
                // Whatever it hit, the point really is on that surface's plane.
                var plane = place.Level.Sectors[spot.Sector].SurfacePlanes[
                    place.Level.Sectors[spot.Sector].Surfaces[spot.Surface].Plane];
                Assert.Equal(plane.Dist, Vector3.Dot(plane.Normal, spot.Point), 1);
            }
        Assert.True(hits > 100, $"only {hits} of 144 rays hit anything");
        Assert.True(walkable > 10 && notWalkable > 10, $"{walkable} walkable, {notWalkable} not — expected a mix");
    }

    /// <summary>
    /// The whole thing end to end, over every level: pick two places, click on the second, and see
    /// whether the character gets there. 868 of 1,262 clicks are accepted and 755 of those arrive.
    ///
    /// The refusals are the point as much as the arrivals. **394** are refused outright because no
    /// route inside the engine's cost cap exists, and **none** because something is already standing
    /// on the spot — the walker's own `WalkTo` never asks, and the no-route
    /// refusal is the original saying no rather than trying and failing.
    /// </summary>
    [Fact]
    public void A_character_walks_to_where_it_was_sent()
    {
        using var files = Install.Open();
        long tried = 0, noRoute = 0, occupied = 0, walked = 0, arrived = 0, stoppedShort = 0, gaveUp = 0, stillGoing = 0;
        foreach (var name in Levels(files))
        {
            var place = Location.Load(files, name, things: false, sky: false, player: "Christof");
            var player = place.Player;
            var map = place.Walk!;
            if (player is null) continue;
            var ground = Enumerable.Range(0, map.Nodes.Length).Where(i => map.Nodes[i].Outline.Length > 3).ToArray();
            if (ground.Length < 4) continue;
            var random = new Random(5);
            for (int k = 0; k < 12; k++)
            {
                int a = ground[random.Next(ground.Length)], b = ground[random.Next(ground.Length)];
                if (a == b) continue;
                player.Thing.Position = Middle(map.Nodes[a]);
                if (!player.Ground()) continue;
                player.Stop();
                tried++;
                var click = Middle(map.Nodes[b]);
                if (!player.WalkTo(click, b))
                {
                    if (player.Occupant is not null) occupied++; else noRoute++;
                    continue;
                }
                walked++;
                for (float clock = 0; clock < 120f && player.Walking; clock += 1f / 60f) player.Follow(1f / 60f);
                // Arrived means there — within two bodies' width of the click — not merely stopped.
                bool there = Vector2.Distance(new(player.Position.X, player.Position.Y), new(click.X, click.Y)) < ArrivedWithin;
                if (player.GaveUp) gaveUp++; else if (player.Walking) stillGoing++; else if (there) arrived++; else stoppedShort++;
            }
        }
        Assert.Equal(1_262L, tried);
        // **These five moved and the direction is the point.** A walk target the
        // clamp cannot pull clear used to refuse the move; the engine discards that answer and
        // walks at the point anyway. Refusals fell 448 → 394, walks rose
        // 814 → 868 and arrivals 786 → 839 — **fifty-four more of these go somewhere, and
        // fifty-three of them get there.**
        Assert.Equal((394L, 0L), (noRoute, occupied));
        Assert.Equal(868L, walked);
        // **And these moved.** A change took the ground test out of a route's step
        // (`WalkMap.Stride`). A later one found that this test had been counting a walk that *stopped* as
        // one that *arrived*: a detour taken part-way along a route ended the whole walk at the
        // detour's spot (`Player.Detour` left the route marked as being followed), and 141 of the
        // "arrivals" were walks standing somewhere else. Counted by where the walker ended, before
        // the fix: 716 there, 141 short, 1 given up, 10 still going. After it: 817 there — a hundred
        // and one more — and 21 short, the walks a detour leaves blocked near its own end.
        //
        // **And one moved**, when the walker's body test started its flood from the sector
        // the new feet are in rather than from one looked up sixteen
        // units above them (ours): in the warehouse (N1_WAR3, node 117 to 31) a swinging door the old
        // lookup met now stays out of the flood as it does in 1.1, and the walk gets there. Eight
        // others take a little longer or shorter round a door or an `exit_message` and end where
        // they did.
        //
        // **And seventy-eight moved**, when the walker's "is it ahead" test became 1.1's
        // (the free-spot test measures from where the walker stands **now** along its **three-dimensional**
        // move direction, and a slide asks with the leg's direction still the walker's) and
        // the detour's candidate test became the walker's own body test at one unit out and at the
        // spot. Arrivals fell 818 → 763, stopped short rose 20 → 27 and given
        // up 2 → 50; changed one at a time, the slide costs 26 arrivals and the detour's test 30.
        // What the 78 are:
        // - **29 start inside something** — this test stands Christof on the middle of a piece of
        //   ground, and 86 of its 868 walks begin inside a prop. A thing whose middle is ahead of him
        //   now blocks the step, the slide and the detour's first unit alike, as 1.1's does; the old
        //   test let him slide out through the far side of it.
        // - **23 end against a box** — a door, a cart, a bookcase. The slide aims off the line
        //   between the two middles, which for a box is not the face he is against, so the slid-to
        //   point is often inside it: the old test, asked along the slide's own direction, found the
        //   box behind and let him through; 1.1's finds it ahead. He goes to the detour instead, and
        //   a walker that runs out of detours gives up after six half-second waits — which is ours
        //   (`Player.GiveUpAfter`); 1.1's retries for ever.
        // - **11 end against a barrel, a vase or a cart**, having gone round it another way, and
        //   **15 now circle on past the two minutes** (detour, resume, blocked, detour).
        // - Five that did not arrive now do.
        // **And one more arrives**, when a sector's things came to be met newest first as the
        // engine links them (`Bodies.Link`): the body test meets another of two touching
        // things first, and one walk that stopped short of the click gets there.
        //
        // **And twenty-two moved**, when the detour started walking to its spot through the
        // walker's own WalkTo — a search from where the walker stands — where
        // it had aimed at it in a straight line. Of 5,113 detours now 1,927 take a route to their
        // spot. Arrivals fell 763 → 754, stopped short rose 27 → 28, given up fell 50 → 48 and still
        // going rose 28 → 38. Twelve walks now circle for the two minutes (detour by a route, resume,
        // blocked, detour — about two hundred detours each): seven that arrived, three that stopped
        // short and two that gave up. **Every one of the seven lost arrivals crossed a wall edge of
        // its ground on the old straight line** (one to five steps each, by a straight check of every
        // step against its ground's walls) — a stone stack in L1_SET1's corridor, the Society's
        // halls, the warehouse, the monastery, the rabbi's house — and the search does not go through
        // them; the loop they fall into is 1.1's ladder with no give-up. Of the rest, three arrivals
        // and two long circlers now stop short, one give-up stops short, two short walks arrive and
        // one arrival (L1_SOC2) gives up.
        // Together (the newest-first sector lists and the routed detour): 755 arrived and 27
        // stopped short — the sector order's one walk that now arrives on top of the detour's numbers.
        Assert.Equal(755L, arrived);
        Assert.Equal(27L, stoppedShort);
        // **And these moved**, when a route's step found its ground by the engine's ray and
        // kept the surface it had by the engine's inside test (`WalkMap.Stride`): one walk
        // in the east gate (P1_EGAT, the fourth, node 22 to 156) circles an `exit_message` at the
        // level's edge either way, but coming off a slope it now keeps the lower of two touching
        // surfaces where the walk map took the upper, meets the thing on a slightly different line,
        // and circles on past the two minutes instead of tripping the give-up count at 14.9 s —
        // which is ours; the engine never gives up. Every other walk ends as it did (seven a few
        // units from where they ended before).
        Assert.Equal(48L, gaveUp);                                    // 2 until then, 50 until then (above)
        // Still going when the two minutes are up: the long hauls across a level, and the walks that
        // circle a prop standing on their route — detour, resume, blocked, detour — which is the
        // engine's own ladder with nothing to break the loop (it never gives up at all).
        Assert.Equal(38L, stillGoing);                                // 28 until then (above)
    }

    /// <summary>
    /// Something standing on the spot refuses the click outright — the engine asks before it
    /// searches. In Old Town that is a barrel, and it is why clicking one does nothing
    /// rather than walking you into it.
    /// </summary>
    [Fact]
    public void Something_standing_on_the_spot_refuses_the_click()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: false, sky: false, player: "Christof");
        var player = place.Player!;
        int refused = 0, tested = 0;
        foreach (var thing in place.Things)
        {
            if (thing.Collide != CollideShape.Cylinder || ReferenceEquals(thing, player.Thing)) continue;
            int node = place.Walk!.Find(thing.Position, WalkMap.Reach);
            if (node < 0) continue;
            tested++;
            player.Stop();
            if (!player.WalkTo(thing.Position, node, refuseOccupied: true) && player.Occupant is not null) refused++;
        }
        Assert.True(tested > 10, $"only {tested} bodies stand on walkable ground");
        Assert.Equal(tested, refused);
    }

    /// <summary>
    /// **A walk a script ordered never gives up.** The engine has no give-up at all — a walker that
    /// cannot get through stands and retries for ever — and Vitomir's six half-second tries are ours
    /// (<see cref="Player.GiveUpAfter"/>). For a click or an AI walk that is harmless, because the
    /// player or the mind simply orders another; for a patrol it is not, because nothing will ever
    /// order that walk again. So `SendActorToPos` asks for <c>persist</c>. Since the engine step
    /// two routes in the whole-game walk test give up, and this is one of them. The other (in `V1_FROG`)
    /// ends the way the engine ends a detour blocked near its own end — the walk
    /// simply stops, with no `arrived`, and no amount of persistence changes that. Soho's,
    /// which was here until then, now arrives.
    /// </summary>
    [Theory]
    [InlineData("P2_VYSM", 12, 70)]
    public void A_walk_a_script_ordered_retries_for_ever_where_a_click_gives_up(string level, int from, int to)
    {
        using var files = Install.Open();
        // The walk test's own sequence on this level, replayed exactly: a detour's direction is a
        // roll, so the walks before the one that gives up have to happen first, the same way.
        (bool GaveUp, bool Walking, float Left) Replay(bool persist)
        {
            var place = Location.Load(files, level, things: false, sky: false, player: "Christof");
            var player = place.Player!;
            var map = place.Walk!;
            var ground = Enumerable.Range(0, map.Nodes.Length).Where(i => map.Nodes[i].Outline.Length > 3).ToArray();
            var random = new Random(5);
            for (int k = 0; k < 12; k++)
            {
                int a = ground[random.Next(ground.Length)], b = ground[random.Next(ground.Length)];
                if (a == b) continue;
                player.Thing.Position = Middle(map.Nodes[a]);
                if (!player.Ground()) continue;
                player.Stop();
                bool target = a == from && b == to;
                if (!player.WalkTo(Middle(map.Nodes[b]), b, persist: persist && target)) continue;
                for (float clock = 0; clock < 120f && player.Walking; clock += 1f / 60f) player.Follow(1f / 60f);
                if (target) return (player.GaveUp, player.Walking, Vector3.Distance(player.Position, Middle(map.Nodes[b])));
            }
            throw new InvalidOperationException("the walk was not in the sequence");
        }
        var click = Replay(persist: false);
        var script = Replay(persist: true);
        Assert.True(click.GaveUp && !click.Walking, $"the click walk: {click}");
        // Still standing there trying two minutes later, as 1.1's would be — and no nearer.
        Assert.False(script.GaveUp);
        Assert.True(script.Walking, $"the script walk: {script}");
    }

    /// <summary>
    /// Walking up a flight of stairs, which is the case that catches a walker out: the treads are
    /// linked through the riser between them, and the riser is a surface nobody can stand on.
    /// </summary>
    [Fact]
    public void A_character_walks_up_stairs()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: false, sky: false, player: "Christof");
        var player = place.Player!;
        var top = new Vector3(1763, 377, 112);
        int node = place.Walk!.Find(top, 256f);
        Assert.True(node >= 0);
        Assert.True(player.WalkTo(top, node), "no route up the steps");
        Assert.True(player.Route.Length > 5, $"only {player.Route.Length} waypoints");
        float started = player.Position.Z;
        for (float clock = 0; clock < 30f && player.Walking; clock += 1f / 60f) player.Follow(1f / 60f);
        Assert.False(player.GaveUp);
        Assert.Equal(node, player.Node);
        Assert.True(player.Position.Z - started > 100f, $"only climbed {player.Position.Z - started:0}");
    }
    /// <summary>
    /// **A walker faces each leg all at once as it starts it, and keeps that heading to the end of
    /// the leg.** `StartWalk` calls the engine's instant turn and the walker's
    /// update never turns anybody. In 1.1's Old Town by day nearly
    /// every turn its walkers make is over within a tenth of a second.
    /// Vitomir used to swing them round at 540° a second, spreading a turn over as many as 25 looks.
    /// </summary>
    [Fact]
    public void A_walker_faces_each_leg_as_it_starts_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: false, sky: false, player: "Christof");
        var player = place.Player!;
        var map = place.Walk!;
        var ground = Enumerable.Range(0, map.Nodes.Length).Where(i => map.Nodes[i].Outline.Length > 3).ToArray();
        var random = new Random(12);
        static float Towards(Vector3 from, Vector3 to) => MathF.Atan2(-(to.X - from.X), to.Y - from.Y) * (180f / MathF.PI);
        static float Apart(float a, float b) => MathF.Abs(Player.Wrap(a - b));

        int routes = 0, legs = 0;
        while (routes < 20)
        {
            int a = ground[random.Next(ground.Length)], b = ground[random.Next(ground.Length)];
            player.Thing.Position = Middle(map.Nodes[a]);
            if (a == b || !player.Ground()) continue;
            if (!player.WalkTo(Middle(map.Nodes[b]), b, run: false) || player.Route.Length < 2) { player.Stop(); continue; }
            routes++;
            // Facing the first leg before a single step is taken — unless it climbs or drops so
            // steeply (0.8 of its length) that facing along it would tip the character
            // over, in which case the heading is left as it was.
            bool Steep() { var way = player.Target - player.Position; return MathF.Abs(way.Z) / way.Length() >= Player.FaceBelowRise; }
            if (!Steep())
                Assert.True(Apart(player.Heading, Towards(player.Position, player.Target)) < 0.01f,
                            $"set off facing {player.Heading:0.00}, its first leg goes {Towards(player.Position, player.Target):0.00}");
            float heading = player.Heading;
            for (float clock = 0; clock < 120f && player.Walking; clock += 1f / 60f)
            {
                player.Follow(1f / 60f);
                if (!player.Walking || player.Heading == heading) continue;
                // Whenever the heading changes at all, it is a leg starting — the next corner of the
                // route, a detour, a restart after waiting — and the character is already facing
                // along it: no tick ever leaves it part of the way round.
                legs++;
                Assert.False(Steep(), "a steep leg turned the walker");
                Assert.True(Apart(player.Heading, Towards(player.Position, player.Target)) < 0.5f,
                            $"turned to {player.Heading:0.0}, which is not the way its leg goes ({Towards(player.Position, player.Target):0.0})");
                heading = player.Heading;
            }
            player.Stop();
        }
        Assert.True(legs > 20, $"only {legs} turns");
    }

    /// <summary>
    /// An interest spot floats above the ground it stands on — eight units, in Old Town — so the
    /// last leg of a walk to one points almost straight up, and a walker snapped to the floor can
    /// never close it. The engine ends the walk regardless: `NextWaypoint` does nothing
    /// once the waypoints are gone, and the tail of the walker's update stops the
    /// character. Vitomir used to re-aim at the destination instead, so every wanderer in the game
    /// finished its first errand and then walked on the spot for the rest of the level — which is
    /// what play kept showing in Old Town: still walking in place without moving.
    /// </summary>
    [Fact]
    public void A_walker_stops_when_the_last_of_its_leg_is_straight_up()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: false, sky: false, player: "Christof");
        var player = place.Player!;
        var map = place.Walk!;
        var spots = new Ai.InterestSpots(place.WorldScene ?? place.Scene, map);
        var spot = spots.All.First(s => s.Node >= 0);

        // The premise: the spot really is off the ground, so the walk cannot end by arriving.
        float floor = map.Nodes[spot.Node].Height(spot.Position.X, spot.Position.Y);
        Assert.InRange(spot.Position.Z - floor, 1f, 64f);

        player.Thing.Position = Middle(map.Nodes[spot.Node]);
        Assert.True(player.Ground());
        Assert.True(player.WalkTo(spot.Position, spot.Node, run: false));

        for (float clock = 0; clock < 60f && player.Walking; clock += 1f / 60f) player.Follow(1f / 60f);

        Assert.False(player.Walking);            // it stopped rather than walking on the spot
        Assert.False(player.GaveUp);             // and it stopped by arriving, not by giving up
        Assert.Equal(0f, player.Speed);
        // It is under the spot, on the floor — the eight units of altitude are not walked off.
        Assert.Equal(spot.Position.X, player.Position.X, 1f);
        Assert.Equal(spot.Position.Y, player.Position.Y, 1f);
        Assert.Equal(floor, player.Position.Z, 1f);
    }

}
