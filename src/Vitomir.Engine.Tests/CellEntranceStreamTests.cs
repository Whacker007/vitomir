// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.World;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Ours: a cell level opened at an entrance far from its first start.** A level opens with the
/// cells round one start read at once and streams the rest as the player moves. Earlier, that start was
/// always the level's first, so a party begun at an entrance farther off than the simulation's radius stood
/// on ground that was not loaded and had no piece of ground under it (the whole city's blockout is kilometres
/// across, its entrances hundreds of metres apart). These pin that the entrance asked for
/// (<see cref="CellStreamOptions.Entrance"/>) is the one whose ground is in when the level opens, that a level
/// opened without one opens as before, and that an entrance the level does not have falls back to its first start.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellEntranceStreamTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;
    static readonly Vector3 First = new(5, 7, 0), Far = new(115, 7, 0);

    /// <summary>A floor 120 m × 15 m of 1 m squares, cut into cells of 15 m, with entrance 0 at one end and entrance 3 at the other.</summary>
    static (string Root, string Package) Cook()
    {
        var v = new List<Vector3>(); var idx = new List<int>(); var mat = new List<int>();
        for (int x = 0; x < 120; x++)
            for (int y = 0; y < 15; y++)
            {
                int at = v.Count;
                v.AddRange([new Vector3(x, y, 0) * M, new Vector3(x + 1, y, 0) * M, new Vector3(x + 1, y + 1, 0) * M, new Vector3(x, y + 1, 0) * M]);
                idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
                mat.AddRange([0, 0]);
            }
        var root = Path.Combine(Path.GetTempPath(), "vitomir-entrancetest-" + Guid.NewGuid().ToString("N"));
        var package = L2Package.PathFor(root, "entrancetest");
        L2Anchor Start(string name, Vector3 at, uint group) =>
            new() { Id = $"entrancetest/starts/{group}", Kind = "start", Scene = "entrancetest", Name = name, Position = [at.X, at.Y, at.Z], Rotation = [0, 0, 0], Group = group };
        CellCook.Cook(new CellCookInput
        {
            Name = "entrancetest", Vertices = [.. v], Indices = [.. idx], TriangleMaterials = [.. mat], Materials = ["ground"],
            Ground = m => m == "ground", Passable = _ => false, CellSize = 15f, BlockCells = 2,
            Anchors = [Start("playerstart00", First, 0), Start("playerstart03", Far, 3)],
        }, package);
        return (root, package);
    }

    [Fact]
    public void A_level_opened_at_an_entrance_has_that_entrance_s_ground_in_at_once()
    {
        var (root, package) = Cook();
        try
        {
            var level = CellLevel.Load(package, new CellStreamOptions { SimRadius = 5, Hysteresis = 3, Wait = true, Entrance = 3 });
            var walk = new WalkMap(level.Shell);
            Assert.True(walk.Find(Far * M) >= 0, "no ground under the entrance the level was opened at");
            Assert.True(walk.Find(First * M) < 0, "the first start's ground was read though the level opened elsewhere");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_level_opened_at_no_entrance_opens_round_its_first_start_as_before()
    {
        var (root, package) = Cook();
        try
        {
            var level = CellLevel.Load(package, new CellStreamOptions { SimRadius = 5, Hysteresis = 3, Wait = true });
            var walk = new WalkMap(level.Shell);
            Assert.True(walk.Find(First * M) >= 0, "no ground at the level's first start");
            Assert.True(walk.Find(Far * M) < 0, "ground far off before it is loaded");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void An_entrance_the_level_does_not_have_opens_it_round_its_first_start()
    {
        var (root, package) = Cook();
        try
        {
            var level = CellLevel.Load(package, new CellStreamOptions { SimRadius = 5, Hysteresis = 3, Wait = true, Entrance = 9 });
            Assert.True(new WalkMap(level.Shell).Find(First * M) >= 0, "no ground at the level's first start");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
