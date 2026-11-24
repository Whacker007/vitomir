// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Driving gets over the steps a click walk gets over** — in play, driving
/// had numerous troubles over some short steps and other areas, and click-to-move was
/// needed to get past them. Found in that play's tape (the keys held, and
/// where Christof was): the stops were at the lips of steps and a ledge, in the Silver
/// Mines, the university and the Old Town. Three causes, all in driving, which is ours:
///
/// <list type="bullet">
/// <item>**A riser's gap.** Where two linked treads do not meet — the riser between them slopes
/// (steeper than the walk map keeps) or the lower tread is set back — the keyboard's step, which lands
/// only on ground, stopped at the gap's edge. A click walk crosses it, because 1.1's walker keeps the
/// surface it had over no ground (<see cref="WalkMap.Stride"/>). Driving now does too, to
/// linked ground straight ahead within <see cref="WalkMap.DriveGap"/> (<c>WalkMap.Drive.cs</c>).</item>
/// <item>**The `bodycollision` Fix**, which was on (the tape's <c>fixes</c> line). Its body was
/// measured from the feet, so its knee was inside every step it came to; now each point of it stands
/// on the ground under it and only its leading half counts (<see cref="DriveBody"/>).</item>
/// <item>**The axis slide on a slanting edge.** Pressed along a ledge whose edge is a few degrees off
/// an axis, both one-axis tries leave the ground; the drive now runs along the edge itself, as the
/// route a click would take does.</item>
/// </list>
///
/// The spots are Christof's own, from that play, each driven in the direction he was pushing.
/// What must still stop a drive stops it: a wall, a drop to ground the level does not link (which a
/// click walk goes round), and — with the Fix — a corner at the shoulder.
/// </summary>
public class DrivingStepsTests
{
    /// <summary>The unit direction of a heading, the way <see cref="Player.Turn"/> gives it.</summary>
    static Vector2 Toward(float heading)
    {
        float r = heading * MathF.PI / 180f;
        return new Vector2(-MathF.Sin(r), MathF.Cos(r));
    }

    static Player Stand(Location place, Vector3 at)
    {
        var player = place.Player!;
        player.Stop();
        player.Thing.Position = at;
        Assert.True(player.Ground(), $"no ground under {at}");
        return player;
    }

    /// <summary>
    /// The riser's gap, driven from the keyboard with the game's own settings: over it onto the next
    /// tread, at 60 and 20 frames a second, walking and running. Before, each stopped at the lip.
    /// </summary>
    [Theory]
    [InlineData("P1_OTDY", 3330.5f, 886.7f, -96f, 105f, 694, 60, true)]     // an Old Town flight, down 16 over a 6-unit gap
    [InlineData("P1_OTDY", 3330.5f, 886.7f, -96f, -105f, 681, 20, false)]   // and up it
    [InlineData("P1_SIM1", -2869.2f, 1278.6f, -607.8f, 90f, 340, 60, true)] // the mines: down 10 over a 6-unit gap
    [InlineData("P1_SIM1", -1543.7f, 1345.6f, -266.2f, -90f, 380, 20, true)] // up 18 over a sloping riser 8 deep
    [InlineData("P1_SIM1", -1913.9f, -56.5f, -229.2f, 180f, 194, 60, false)] // up 21 over one 8 deep
    public void A_driven_character_gets_over_a_riser_s_gap_onto_the_linked_tread(
        string level, float x, float y, float z, float heading, int tread, int fps, bool run)
    {
        using var files = Install.Open();
        var place = Location.Load(files, level, sky: false, player: "Christof");
        var player = Stand(place, new Vector3(x, y, z));
        var start = player.Thing.Position;
        var push = Toward(heading);
        var map = place.Walk!;
        Assert.Contains(tread, map.Nodes[player.Node].Neighbours);        // linked: a click walk's ground

        player.Running = run;
        var visited = new HashSet<int>();
        for (int frame = 0; frame < fps * 3 / 4; frame++)
        {
            player.Move(push, 1f / fps);
            visited.Add(player.Node);
        }
        var end = player.Thing.Position;
        float along = Vector2.Dot(new Vector2(end.X - start.X, end.Y - start.Y), push);
        Assert.True(visited.Contains(tread), $"stopped at {end} on {player.Node}, {along:0} units on, short of tread {tread}");
        Assert.True(along > 40f, $"only {along:0} units on");

        // And a click on the tread walks there too.
        var goal = Enumerable.Range(1, 30).Select(d => start + new Vector3(push * (2f * d), 0f))
                             .First(p => map.Nodes[tread].Contains(new Vector2(p.X, p.Y)));
        player.Stop();
        player.Thing.Position = start; player.Ground();
        Assert.True(player.WalkTo(goal with { Z = map.Nodes[tread].Height(goal.X, goal.Y) }, tread));
    }

    /// <summary>
    /// **A wall still stops a drive.** The Old Town flight, pushed for a second into the corner of the
    /// building beside it (at head height 28 units off, north-east): the driver slides along what
    /// it can and ends on its own ground, clear of the wall, no nearer the corner than the wall lets it.
    /// </summary>
    [Fact]
    public void A_wall_still_stops_a_drive()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", sky: false, player: "Christof");
        var player = Stand(place, new Vector3(3330.5f, 886.7f, -96f));
        var start = player.Thing.Position;
        var push = Toward(-45f);
        player.Running = true;
        for (int frame = 0; frame < 60; frame++) player.Move(push, 1f / 60f);
        var end = player.Thing.Position;
        Assert.True(place.Walk!.Find(end) >= 0, $"off the ground at {end}");
        Assert.False(player.Solid!.Solid(end + new Vector3(0f, 0f, 48f)), $"inside the wall at {end}");
        Assert.True(Vector2.Dot(new Vector2(end.X - start.X, end.Y - start.Y), push) < 28f, $"through the corner to {end}");
    }

    /// <summary>
    /// **Pressed along a slanting edge, a drive runs along it** as a click walk's route does: the
    /// mines' ledge above the lower gallery, whose edge lies five degrees off east. Christof stopped
    /// dead there pushing east-north-east (the axis slide leaves the ground both ways) and was clicked
    /// on; the route's first leg runs along that edge.
    /// </summary>
    [Fact]
    public void Pressed_along_a_slanting_edge_a_drive_runs_along_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_SIM1", sky: false, player: "Christof");
        var player = Stand(place, new Vector3(-2564.4f, 1069.1f, -562.3f));
        var start = player.Thing.Position;
        player.Running = true;
        for (int frame = 0; frame < 30; frame++) player.Move(Toward(-65f), 1f / 60f);
        var end = player.Thing.Position;
        Assert.True(end.X - start.X > 40f, $"only got to {end}");
        Assert.True(place.Walk!.Find(end) >= 0, $"off the ground at {end}");
    }

    /// <summary>
    /// **A drop the level does not link is not walked off**, gap or no gap: the same spot in the mines,
    /// a ledge 14 over ground twelve units on that is not linked to it. Driving stays on the ledge; a
    /// click on the ground below walks round by the linked way down, not over the lip.
    /// </summary>
    [Fact]
    public void A_drop_to_unlinked_ground_is_not_driven_off_and_a_click_walks_round()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_SIM1", sky: false, player: "Christof");
        var map = place.Walk!;
        var player = Stand(place, new Vector3(-2564.4f, 1069.1f, -562.3f));
        var start = player.Thing.Position;
        int ledge = player.Node;
        var push = Toward(0f);
        var below = start + new Vector3(push * 16f, 0f);
        int ground = map.Find(below, WalkMap.Reach);
        Assert.True(ground >= 0 && map.Nodes[ground].Height(below.X, below.Y) < start.Z - 10f, "no drop there");
        Assert.DoesNotContain(ground, map.Nodes[ledge].Neighbours);

        player.Running = true;
        for (int frame = 0; frame < 30; frame++) player.Move(push, 1f / 60f);
        Assert.Equal(ledge, player.Node);
        Assert.Equal(start.Z, player.Thing.Position.Z, 0.5f);
        Assert.True(Vector2.Dot(new Vector2(player.Thing.Position.X - start.X, player.Thing.Position.Y - start.Y), push) < 1f);

        player.Stop();
        player.Thing.Position = start; player.Ground();
        var goal = below with { Z = map.Nodes[ground].Height(below.X, below.Y) };
        Assert.True(player.WalkTo(goal, ground));
        Assert.NotEmpty(player.Route);
        Assert.True(Vector3.Distance(player.Route[0].Point, start) > 40f,
                    $"the click walks off the lip, by {player.Route[0].Point}");
    }

    /// <summary>
    /// Pushes a body from <paramref name="start"/> along <paramref name="push"/> in steps of a unit,
    /// through the keyboard's step with the given body test — what <c>Player.Clear</c> does with the
    /// `bodycollision` Fix on, without installing the Fix (a process-wide static other tests read).
    /// </summary>
    static float Push(WalkMap map, int node, Vector3 start, Vector2 push, float distance,
                      Func<Vector3, Vector3, bool> fits)
    {
        var at = start;
        for (float gone = 0f; gone < distance; gone += 1f)
        {
            var from = at;
            var step = map.Move(node, from, from + new Vector3(push, 0f), feet => fits(from, feet));
            node = step.Node; at = step.Position;
        }
        return Vector2.Dot(new Vector2(at.X - start.X, at.Y - start.Y), push);
    }

    /// <summary>
    /// **With the `bodycollision` Fix, a step is climbed and descended.** The body measured from the
    /// feet (<see cref="Collision.Fits"/>, the Fix until now) is refused at each of these lips from the
    /// play; standing each point on the ground under it and counting the half ahead
    /// (<see cref="DriveBody"/>) gets over every one.
    /// </summary>
    [Theory]
    [InlineData("P1_SIM1", -1167.7f, -290.1f, -7.8f, 90f)]     // down 8
    [InlineData("P1_SIM1", -1343.6f, -317.0f, -55.9f, 90f)]    // down 12
    [InlineData("P1_SIM1", -1874.0f, -261.2f, -167.9f, 60f)]   // down 14
    [InlineData("P1_SIM1", -1543.7f, 1345.6f, -266.2f, -90f)]  // up 18, a sloping riser
    [InlineData("P1_SIM1", -1913.9f, -56.5f, -229.2f, 180f)]   // up 21, a sloping riser
    [InlineData("P1_SIM1", -1788.3f, 512f, -352f, 90f)]        // along a tunnel
    [InlineData("P1_UNIV", 639.8f, 630.7f, 96f, -90f)]          // the university, down 16
    public void With_the_body_fix_a_driven_character_still_gets_over_the_step(
        string level, float x, float y, float z, float heading)
    {
        using var files = Install.Open();
        var place = Location.Load(files, level, sky: false, player: "Christof");
        var player = Stand(place, new Vector3(x, y, z));
        var solid = player.Solid!;
        float radius = player.Radius;
        var push = Toward(heading);

        float plain = Push(place.Walk!, player.Node, player.Thing.Position, push, 40f,
                           (_, feet) => solid.Fits(feet, radius));
        Assert.True(plain < 20f, $"the plain body test lets it {plain:0} units on already");

        float stepped = Push(place.Walk!, player.Node, player.Thing.Position, push, 40f,
                             (from, feet) => DriveBody.Fits(solid, from, feet, radius));
        Assert.True(stepped > 35f, $"the body stops {stepped:0} units on");
    }

    /// <summary>
    /// **With the Fix, a corner at the shoulder still stops the drive** — what the Fix is for. On the
    /// Old Town flight, going up, a corner of the building 28 units off the leading shoulder at head
    /// height, which the drive without the Fix goes past.
    /// </summary>
    [Fact]
    public void With_the_body_fix_a_corner_at_the_shoulder_still_stops_the_drive()
    {
        using var files = Install.Open();
        var town = Location.Load(files, "P1_OTDY", sky: false, player: "Christof");
        var walker = Stand(town, new Vector3(3330.5f, 886.7f, -96f));
        var solid = walker.Solid!;
        var up = Toward(-90f);
        Assert.True(Push(town.Walk!, walker.Node, walker.Thing.Position, up, 40f, (_, _) => true) > 35f,
                    "the flight is not open to a drive without the Fix");
        Assert.True(Push(town.Walk!, walker.Node, walker.Thing.Position, up, 40f,
                         (from, feet) => DriveBody.Fits(solid, from, feet, walker.Radius)) < 5f,
                    "the Fix let the shoulder into the corner");
    }
}
