// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Ours: 1.1's things rise with ground that is deliberately not 1.1's**
/// (<see cref="L2Lift"/>, <see cref="CellScene"/>). By design
/// the sunken court before the Old Town's University is made level with the street; 1.1 has the haven's
/// four arrival points, a lantern on the bowl's wall and more down in that bowl, 1.78 m under the street.
///
/// The level cooked here is made up: a flat floor a quarter of a metre over 1.1's datum, as
/// <see cref="CellSceneTests"/> has, with a court from (44, 0) to (60, 24) m three quarters of a metre over
/// the datum — 182 units above the bowl's floor in 1.1, which is further than a start looks for its ground
/// (160) and far further than a thing does (36). Without a lift, then, what 1.1 has in the bowl is left
/// under the court; that is pinned first, so the tests after it mean something. With a lift of 182 units
/// over the court (cells of half a metre, zeros at the rim), the arrival points stand on the court, the
/// lantern hangs as far above the court as it hung above the bowl, every thing over the court was looked for
/// from its 1.1 height plus the lift with its keyframes moved by as much, and everything outside the patch
/// stands exactly where it stands without one. A patch of zeros changes nothing at all. The scene is read
/// from the install, so everything here skips without one.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellSceneLiftTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;
    const string Level = "P1_OTDY", Chapter = "OTDY_4_1";
    /// <summary>The floor and the court (engine units over 1.1's datum), and 1.1's bowl under the court.</summary>
    const float Floor = 18f, Court = 54f, Bowl = -128f;
    /// <summary>The court's lift: from 1.1's bowl to the court (engine units).</summary>
    const float Lift = Court - Bowl;
    /// <summary>The court's outline, metres.</summary>
    const float X0 = 44, X1 = 60, Y0 = 0, Y1 = 24;

    static bool OverCourt(Vector3 p) => p.X >= X0 * M && p.X < X1 * M && p.Y >= Y0 * M && p.Y < Y1 * M;

    /// <summary>A floor of 4 m squares from (−52, −60) to (92, 72) m at <see cref="Floor"/>, those of the court at <see cref="Court"/>.</summary>
    static (List<Vector3> V, List<int> I, List<int> Mat) Ground()
    {
        var v = new List<Vector3>(); var idx = new List<int>(); var mat = new List<int>();
        for (int x = -52; x < 92; x += 4)
            for (int y = -60; y < 72; y += 4)
            {
                float z = x >= X0 && x < X1 && y >= Y0 && y < Y1 ? Court : Floor;
                int at = v.Count;
                v.AddRange([new(x * M, y * M, z), new((x + 4) * M, y * M, z), new((x + 4) * M, (y + 4) * M, z), new(x * M, (y + 4) * M, z)]);
                idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
                mat.AddRange([0, 0]);
            }
        return (v, idx, mat);
    }

    /// <summary>
    /// The court's patch: half-metre cells from a metre outside the court all round, <paramref name="metres"/>
    /// in every cell whose centre is over the court and 0 in the rim.
    /// </summary>
    static L2Lift Patch(float metres)
    {
        const float Cell = 0.5f;
        int nx = (int)((X1 - X0 + 2) / Cell), ny = (int)((Y1 - Y0 + 2) / Cell);
        var values = new float[nx * ny];
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                float x = X0 - 1 + (i + 0.5f) * Cell, y = Y0 - 1 + (j + 0.5f) * Cell;
                values[j * nx + i] = x >= X0 && x < X1 && y >= Y0 && y < Y1 ? metres : 0f;
            }
        return new L2Lift { Name = "court", X0 = X0 - 1, Y0 = Y0 - 1, Cell = Cell, Nx = nx, Ny = ny, Values = values };
    }

    /// <summary>What a load of the level gave: its placements, starts and things, taken before the level is let go.</summary>
    sealed record Loaded(List<CellScenePlacement> Placements, NsdStart[] Starts, Vector3[] Things, NsdKeyframe[][] Keyframes,
                         Vector3[] ConversationStarts, L2Manifest Manifest, NsdScene World, NsdScene ChapterScene);

    /// <summary>Cooks the floor with <paramref name="lifts"/>, loads the Old Town standing on it, and answers what was placed.</summary>
    static Loaded Load(List<L2Lift>? lifts)
    {
        using var files = Install.Open();
        var (v, idx, mat) = Ground();
        var root = Path.Combine(Path.GetTempPath(), "vitomir-celllift-" + Guid.NewGuid().ToString("N"));
        var was = L2Levels.Folder;
        try
        {
            CellCook.Cook(new CellCookInput
            {
                Name = "celllift", Vertices = [.. v], Indices = [.. idx], TriangleMaterials = [.. mat], Materials = ["ground"],
                Ground = m => m == "ground", Passable = _ => false,
                Anchors = [new L2Anchor { Id = "celllift/starts/0", Kind = "start", Scene = "celllift", Name = "playerstart00",
                                          Position = [20, 12, 1], Rotation = [0, 0, -180], Group = 0 }],
                Scene = new L2Scene { Level = Level, Chapter = Chapter, Drop = [] }, Lifts = lifts,
            }, L2Package.PathFor(root, "celllift"));
            L2Levels.Folder = root;
            L2Levels.StandIns[Level] = L2Package.PathFor(root, "celllift");
            var place = Location.Load(files, Level, scene: Chapter, player: "Christof");
            var cells = CellLevel.Of(place.Level)!;
            var world = NsdScene.Read(files.Read($"scenes/{Level}.nsd"), Level);
            var chapter = NsdScene.Read(files.Read($"scenes/{Chapter}.nsd"), Chapter);
            int placed = world.Things.Length + chapter.Things.Length;
            return new Loaded([.. cells.Placements], place.AllStarts,
                              [.. place.Things.Take(placed).Select(t => t.Position)], [.. place.Things.Take(placed).Select(t => t.Keyframes)],
                              [.. place.ConversationStarts.Select(s => s.Position)], cells.Manifest, world, chapter);
        }
        finally
        {
            L2Levels.StandIns.Remove(Level);
            L2Levels.Folder = was;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Without_a_lift_what_1_1_has_in_the_bowl_is_left_under_the_raised_court()
    {
        var bare = Load(null);
        Assert.Null(bare.Manifest.Lifts);
        // The haven's four arrival points (entrance 1) are 1.1's, 1.78 m under its street; the court is out of their reach.
        var haven = bare.Starts.Where(s => s.Group == 1).ToArray();
        Assert.Equal(4, haven.Length);
        Assert.All(haven, s => Assert.True(OverCourt(s.Position), $"a haven start at {s.Position} is not over the test's court"));
        Assert.All(haven, s => Assert.Equal(Bowl, s.Position.Z, 0));
        // And so is everything 1.1 stood on the bowl's floor or hung near it.
        var under = bare.Placements.Where(p => OverCourt(p.From) && p.From.Z < Floor - CellScene.StandTolerance - 1f).ToList();
        Assert.NotEmpty(under);
        Assert.All(under, p => Assert.Equal(p.From.Z, p.At.Z, 2));
        Assert.All(bare.Placements, p => Assert.Equal(0f, p.Lift));
    }

    [Fact]
    public void The_havens_arrival_points_stand_on_the_court()
    {
        var lifted = Load([Patch(Lift / M)]);
        Assert.Equal("court", Assert.Single(lifted.Manifest.Lifts!).Name);        // the cook kept the patch in the package
        var haven = lifted.Starts.Where(s => s.Group == 1).ToArray();
        Assert.Equal(4, haven.Length);
        Assert.All(haven, s => Assert.Equal(Court, s.Position.Z, 1));
        // The starts on the plain floor are where they are without a lift.
        var bare = Load(null);
        for (int i = 0; i < bare.Starts.Length; i++)
            if (!OverCourt(bare.Starts[i].Position)) Assert.Equal(bare.Starts[i].Position, lifted.Starts[i].Position);
    }

    [Fact]
    public void A_lantern_on_the_bowls_wall_hangs_as_far_above_the_court_as_it_hung_above_the_bowl()
    {
        var lifted = Load([Patch(Lift / M)]);
        // Things hanging over 1.1's bowl: more than the stand tolerance above its floor, and under the street.
        var hung = lifted.Placements.Where(p => OverCourt(p.From) && p.From.Z > Bowl + CellScene.StandTolerance + 1f && p.From.Z < Floor).ToList();
        Assert.Contains(hung, p => p.Template.StartsWith("lantern", StringComparison.OrdinalIgnoreCase));
        foreach (var p in hung.Where(p => p.From.Z + Lift > Court + CellScene.StandTolerance + 1f))
        {
            Assert.Equal("kept", p.How);
            Assert.Equal(Lift, p.Lift, 2);
            Assert.Equal(p.From.Z - Bowl, p.At.Z - Court, 1);                       // its height above the ground is 1.1's
        }
    }

    [Fact]
    public void Every_thing_over_the_court_is_looked_for_from_its_1_1_height_plus_the_lift_and_its_keyframes_move_by_as_much()
    {
        var lifted = Load([Patch(Lift / M)]);
        var placed = lifted.World.Things.Concat(lifted.ChapterScene.Things).ToArray();
        Assert.Equal(placed.Length, lifted.Placements.Count);
        int over = 0, stood = 0;
        for (int i = 0; i < placed.Length; i++)
        {
            var p = lifted.Placements[i];
            Assert.Equal(placed[i].Position, p.From);
            if (!OverCourt(p.From)) { Assert.Equal(0f, p.Lift); continue; }
            over++;
            Assert.Equal(Lift, p.Lift, 2);
            // On 1.1's ground in the bowl: on the court. Anything else: at 1.1's height plus the lift, or on the court when that is within the tolerance.
            bool near = MathF.Abs(p.From.Z + Lift - Court) <= CellScene.StandTolerance;
            if (near) { Assert.Equal("ground", p.How); Assert.Equal(Court, p.At.Z, 1); stood++; }
            else Assert.Equal(p.From.Z + Lift, p.At.Z, 1);
            Assert.Equal(p.From.X, p.At.X, 2);
            Assert.Equal(p.From.Y, p.At.Y, 2);
            Assert.Equal(p.At, lifted.Things[i]);                                   // the thing in the world is where the record says
            for (int k = 0; k < placed[i].Keyframes.Length; k++)
                Assert.Equal(placed[i].Keyframes[k].Position + (p.At - p.From), lifted.Keyframes[i][k].Position);
        }
        Assert.True(over >= 5, $"only {over} things of 1.1's stand over the test's court");
    }

    [Fact]
    public void Everything_outside_the_patch_stands_where_it_stands_without_one_and_a_patch_of_zeros_changes_nothing()
    {
        var bare = Load(null);
        var lifted = Load([Patch(Lift / M)]);
        var zeros = Load([Patch(0f)]);
        Assert.Equal(bare.Placements.Count, lifted.Placements.Count);
        for (int i = 0; i < bare.Placements.Count; i++)
        {
            Assert.Equal(bare.Placements[i], zeros.Placements[i]);                  // a patch of zeros: the same record, field for field
            Assert.Equal(bare.Things[i], zeros.Things[i]);
            if (OverCourt(bare.Placements[i].From)) continue;
            Assert.Equal(bare.Placements[i], lifted.Placements[i]);
            Assert.Equal(bare.Things[i], lifted.Things[i]);
        }
        Assert.Equal(bare.Starts.Select(s => s.Position), zeros.Starts.Select(s => s.Position));
        Assert.Equal(bare.ConversationStarts, zeros.ConversationStarts);
        for (int i = 0; i < bare.ConversationStarts.Length; i++)
            if (!OverCourt(bare.ConversationStarts[i])) Assert.Equal(bare.ConversationStarts[i], lifted.ConversationStarts[i]);
    }

    [Fact]
    public void A_position_is_raised_by_the_lift_of_its_cell_in_engine_units()
    {
        // No install needed: the sums the loader does, on the made-up patch.
        List<L2Lift> lifts = [Patch(Lift / M)];
        var inBowl = new Vector3(48.125f * M, 16.333f * M, Bowl);                  // one of the haven's arrival points in 1.1
        Assert.Equal(Lift, CellScene.LiftAt(lifts, M, inBowl), 2);
        Assert.Equal(Court, CellScene.Raised(lifts, M, inBowl).Z, 2);
        Assert.Equal(inBowl.X, CellScene.Raised(lifts, M, inBowl).X);
        var rim = new Vector3(43.5f * M, 16f * M, 0f);                             // in the patch's rim of zeros
        Assert.Equal(rim, CellScene.Raised(lifts, M, rim));
        var away = new Vector3(-10f * M, 20f * M, 0f);
        Assert.Equal(away, CellScene.Raised(lifts, M, away));
        Assert.Equal(inBowl, CellScene.Raised(null, M, inBowl));                   // a level without lifts
    }
}
