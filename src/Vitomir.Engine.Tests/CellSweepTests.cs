// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.World;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **What moves through a cell level meets its collision triangles**
/// (ours): the game camera's pull-in is a sphere of the camera's radius swept against them, so it stops a
/// radius off a wall and off a house's corner that a thin ray slips past; and the engine's sweep
/// (<see cref="WorldSweep.Line"/>) finds them as its surfaces, so a sword dropped over a cooked floor
/// falls onto it and comes to rest there, and an arrow flown at a wall stops at it. Everything but the
/// install's own templates is generated here: a 20 m floor and a 4 m × 4 m house on it.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellSweepTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;

    /// <summary>
    /// A 20 m × 20 m floor of 1 m squares (material 0, ground) and a 4 m × 4 m × 3 m house from x 2 to
    /// 6 m and y −2 to 2 m (material 1, wall), in engine units, faces outward.
    /// </summary>
    static (List<Vector3> V, List<int> I, List<int> Mat) Scene()
    {
        var v = new List<Vector3>(); var idx = new List<int>(); var mat = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int m)
        {
            int at = v.Count;
            v.AddRange([a * M, b * M, c * M, d * M]);
            idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            mat.AddRange([m, m]);
        }
        for (int x = -10; x < 10; x++)
            for (int y = -10; y < 10; y++)
                Quad(new(x, y, 0), new(x + 1, y, 0), new(x + 1, y + 1, 0), new(x, y + 1, 0), 0);
        const float X0 = 2, X1 = 6, Y0 = -2, Y1 = 2, H = 3;
        Quad(new(X0, Y0, 0), new(X1, Y0, 0), new(X1, Y0, H), new(X0, Y0, H), 1);
        Quad(new(X1, Y0, 0), new(X1, Y1, 0), new(X1, Y1, H), new(X1, Y0, H), 1);
        Quad(new(X1, Y1, 0), new(X0, Y1, 0), new(X0, Y1, H), new(X1, Y1, H), 1);
        Quad(new(X0, Y1, 0), new(X0, Y0, 0), new(X0, Y0, H), new(X0, Y1, H), 1);
        Quad(new(X0, Y0, H), new(X1, Y0, H), new(X1, Y1, H), new(X0, Y1, H), 1);
        return (v, idx, mat);
    }

    static CellCollision Collision()
    {
        var (v, idx, mat) = Scene();
        var surfaces = mat.Select(m => L2Surface.Solid | (m == 0 ? L2Surface.Walk : 0)).ToArray();
        var chunk = CellCollision.Build([.. v.Select(p => p / M)], [.. idx], surfaces, [.. mat.Select(m => (ushort)m)]);
        return new CellCollision(L2Collision.Decode(chunk.Encode()), M);
    }

    /// <summary>The scene cooked into a package under a fresh temporary folder; answers the folder.</summary>
    static string Cook(string name)
    {
        var (v, idx, mat) = Scene();
        var root = Path.Combine(Path.GetTempPath(), "vitomir-cellsweep-" + Guid.NewGuid().ToString("N"));
        CellCook.Cook(new CellCookInput
        {
            Name = name, Vertices = [.. v], Indices = [.. idx], TriangleMaterials = [.. mat], Materials = ["ground", "wall"],
            Ground = m => m == "ground", Passable = _ => false,
            Anchors = [new L2Anchor { Id = $"{name}/starts/0", Kind = "start", Scene = name, Name = "playerstart00",
                                      Position = [-5, 0, 1], Rotation = [0, 0, -90], Group = 0 }],
        }, L2Package.PathFor(root, name));
        return root;
    }

    [Fact]
    public void The_camera_s_sphere_stops_its_radius_off_a_wall_it_heads_into()
    {
        var c = Collision();
        // From 5 m west of the house, a metre up, straight at its west wall (x = 2 m).
        var met = CellSphereSweep.Sweep(c, new Vector3(-5 * M, 0, M), Vector3.UnitX, 20 * M, CameraSweep.Radius);
        Assert.NotNull(met);
        Assert.Equal(7 * M - CameraSweep.Radius, met.Value.Distance, 2);
        Assert.Equal(2 * M, met.Value.Point.X, 2);
        Assert.Equal(-1f, met.Value.Normal.X, 3);                       // the wall's face, towards the sphere
        // Short of the wall by more than the radius: nothing.
        Assert.Null(CellSphereSweep.Sweep(c, new Vector3(-5 * M, 0, M), Vector3.UnitX, 7 * M - CameraSweep.Radius - 1f, CameraSweep.Radius));
    }

    [Fact]
    public void The_camera_s_sphere_catches_a_house_corner_a_thin_ray_slips_past()
    {
        var c = Collision();
        // Along the house's west side, 8 units out from its wall, from south of its south-west corner
        // (2 m, −2 m) northwards: the line never meets the house, the sphere reaches the corner.
        var from = new Vector3(2 * M - 8f, -4 * M, M);
        Assert.Null(c.Ray(from, Vector3.UnitY, 3 * M));
        var met = CellSphereSweep.Sweep(c, from, Vector3.UnitY, 3 * M, CameraSweep.Radius);
        Assert.NotNull(met);
        // The corner's vertical edge is 10 from the centre when the centre is 6 short of y = −2 m (8² + 6² = 10²).
        Assert.Equal(2 * M - 6f, met.Value.Distance, 1);
        Assert.Equal(2 * M, met.Value.Point.X, 1);
        Assert.Equal(-2 * M, met.Value.Point.Y, 1);
        // No closer to anything than the radius, all along the way it was let go.
        for (float t = 0; t <= met.Value.Distance; t += 4f)
        {
            var p = from + Vector3.UnitY * t;
            for (int k = 0; k < c.Triangles; k++)
            {
                var (a, b, cc) = c.Corners(k);
                Assert.True(Vector3.Distance(p, CellCollision.ClosestOnTriangle(p, a, b, cc)) >= CameraSweep.Radius - 0.01f);
            }
        }
    }

    [Fact]
    public void The_game_camera_s_pull_in_on_a_cooked_level_is_the_sphere_s()
    {
        var root = Cook("camsweep");
        try
        {
            var level = CellLevel.Load(L2Package.PathFor(root, "camsweep"));
            var from = new Vector3(2 * M - 8f, -4 * M, M);
            Assert.Equal(2 * M - 6f, CellSweep.Camera(level, from, Vector3.UnitY, 3 * M, CameraSweep.Radius)!.Value, 1);
            Assert.Null(CellSweep.Camera(level, from, -Vector3.UnitY, 3 * M, CameraSweep.Radius));   // away from the house: open
            // Head-on: the eye's centre a radius short of the wall, as 1.1's fat ray leaves it.
            Assert.Equal(7 * M - CameraSweep.Radius, CellSweep.Camera(level, new Vector3(-5 * M, 0, M), Vector3.UnitX, 20 * M, CameraSweep.Radius)!.Value, 1);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void The_sweep_finds_a_cooked_level_s_triangles_as_its_surfaces_nearest_first()
    {
        var root = Cook("sweeplines");
        try
        {
            var level = CellLevel.Load(L2Package.PathFor(root, "sweeplines"));
            // East through the house at a metre up: its west wall at 2 m, then its east wall at 6 m.
            var met = WorldSweep.Line(level.Shell, null, 0, new Vector3(-5 * M, 0, M), Vector3.UnitX, 15 * M,
                                      new WorldSweep.Asks(Pick.Tests.Surfaces));
            Assert.Equal(2, met.Count(m => m.Kind == ContactKind.Surface));
            Assert.Equal(7 * M, met[0].Distance, 1);
            Assert.Equal(-1f, met[0].Normal.X, 3);
            Assert.False(met[0].Walkable);
            Assert.Equal(11 * M, met[1].Distance, 1);
            // Straight down onto the floor: walkable, facing up, on the shell's piece of ground there.
            var down = WorldSweep.Line(level.Shell, null, 0, new Vector3(-5 * M, 3 * M, 50f), -Vector3.UnitZ, 140f,
                                       new WorldSweep.Asks(Pick.Tests.Surfaces));
            var floor = Assert.Single(down);
            Assert.True(floor.Walkable);
            Assert.Equal(1f, floor.Normal.Z, 3);
            Assert.True(floor.Surface >= 0, "no piece of ground under the floor's hit");
            // Asked for things alone, the triangles are not in it.
            Assert.Empty(WorldSweep.Line(level.Shell, null, 0, new Vector3(-5 * M, 0, M), Vector3.UnitX, 15 * M,
                                         new WorldSweep.Asks(Pick.Tests.Things)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>A place made of the cooked scene, with Christof in it, through the game's own loader.</summary>
    static Location Play(Vfs.GameFiles files, string root, string name)
    {
        var was = L2Levels.Folder;
        try
        {
            L2Levels.Folder = root;
            return Location.Load(files, name, player: "Christof");
        }
        finally { L2Levels.Folder = was; }
    }

    [Fact]
    public void A_sword_dropped_over_a_cooked_floor_falls_onto_it_and_comes_to_rest_there()
    {
        using var files = Install.Open();
        var root = Cook("celldrop");
        try
        {
            var place = Play(files, root, "celldrop");
            var sword = place.Place("BroadSword", new Vector3(-5 * M, 3 * M, 100f), Vector3.Zero, 0);
            Assert.NotNull(sword);
            sword!.MoveType = WorldThing.MovePhysics;
            sword.PhysicsFlags = Physics.Gravity;
            sword.Velocity = Vector3.Zero;
            float lowest = float.MaxValue;
            int tick = 0;
            for (; tick < 30 * 30 && (sword.PhysicsFlags & Physics.Asleep) == 0; tick++)
            {
                Physics.Step(place, sword, 1f / 30f);
                lowest = MathF.Min(lowest, sword.Position.Z);
            }
            Assert.True((sword.PhysicsFlags & Physics.Asleep) != 0, $"the sword never came to rest; it is at {sword.Position}");
            Assert.True(lowest >= 0f, $"the sword went through the floor, to z {lowest}");
            Assert.True(sword.Position.Z < 20f, $"the sword came to rest in the air, at z {sword.Position.Z}");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void An_arrow_flown_at_a_wall_stops_at_it()
    {
        using var files = Install.Open();
        var root = Cook("cellshot");
        try
        {
            var place = Play(files, root, "cellshot");
            // Two metres west of the house's west wall, a metre and a half up, flying east at 1,000 a second.
            var arrow = place.Place("arrow", new Vector3(0, 0, 1.5f * M), Vector3.Zero, 0);
            Assert.NotNull(arrow);
            Assert.Equal(ThingType.Projectile, arrow!.Type);
            arrow.MoveType = WorldThing.MovePhysics;
            arrow.PhysicsFlags = 0;                           // awake, no gravity
            arrow.Velocity = new Vector3(1000f, 0, 0);
            float furthest = float.MinValue;
            for (int frame = 0; frame < 60 && !arrow.Removed; frame++)
            {
                Physics.Step(place, arrow, 1f / 60f);
                furthest = MathF.Max(furthest, arrow.Position.X);
            }
            Assert.True(furthest > 2 * M - 40f, $"the arrow never reached the wall; it got to x {furthest}");
            Assert.True(furthest <= 2 * M, $"the arrow went through the wall, to x {furthest}");
            Assert.True(arrow.Removed || arrow.Velocity.X <= 0f, $"the arrow is still flying east at {arrow.Velocity}");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
