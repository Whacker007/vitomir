// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.World;
using Vitomir.Formats.L2;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **Ours: 1.1's scripts in a cell level**. A flat floor cooked here stands
/// in for the daytime Old Town and carries its scene; the scene's own script, <c>OTDY_4_1</c>, is made with
/// the region and the two actors its parameters name and marks them to be talked to; clicking the stranger
/// at the gate starts his conversation; Christof walking into the region outside the smithy starts the
/// first conversation with the party stood on the scene's conversation start, on the new floor; and a
/// click-exit sign taken reaches the exits seam with 1.1's exit number. Skips without an install.
/// </summary>
[Collection("scripts")]
public class CellSceneScriptTests(Scripts s)
{
    const float M = L2Manifest.EngineUnitsPerMetre;
    const float Floor = 18f;

    /// <summary>A floor of 4 m squares as wide as the Old Town, a quarter of a metre up, cooked as a level that carries its scene.</summary>
    static string Cook()
    {
        var v = new List<Vector3>(); var idx = new List<int>(); var mat = new List<int>();
        const float Z = Floor / M;
        for (int x = -52; x < 92; x += 4)
            for (int y = -60; y < 72; y += 4)
            {
                int at = v.Count;
                v.AddRange([new Vector3(x, y, Z) * M, new Vector3(x + 4, y, Z) * M, new Vector3(x + 4, y + 4, Z) * M, new Vector3(x, y + 4, Z) * M]);
                idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
                mat.AddRange([0, 0]);
            }
        var root = Path.Combine(Path.GetTempPath(), "vitomir-cellscript-" + Guid.NewGuid().ToString("N"));
        CellCook.Cook(new CellCookInput
        {
            Name = "cellscript", Vertices = [.. v], Indices = [.. idx], TriangleMaterials = [.. mat], Materials = ["ground"],
            Ground = _ => true, Passable = _ => false,
            Anchors = [new L2Anchor { Id = "cellscript/starts/0", Kind = "start", Scene = "cellscript", Name = "playerstart00",
                                      Position = [20, 12, 1], Rotation = [0, 0, -180], Group = 0 }],
            Scene = new L2Scene { Level = "P1_OTDY", Chapter = "OTDY_4_1", Drop = ["class:lantern"] },
        }, L2Package.PathFor(root, "cellscript"));
        return root;
    }

    void InTheCellTown(Action<Place> with)
    {
        s.Ready();
        var root = Cook();
        var was = L2Levels.Folder;
        try
        {
            L2Levels.Folder = root;
            L2Levels.StandIns["P1_OTDY"] = L2Package.PathFor(root, "cellscript");
            using var place = Place.Enter(s, "P1_OTDY", "OTDY_4_1");
            Assert.NotNull(CellLevel.Of(place.Where.Level));
            with(place);
        }
        finally
        {
            L2Levels.StandIns.Remove("P1_OTDY");
            L2Levels.Folder = was;
            Directory.Delete(root, recursive: true);
        }
    }

    static WorldThing The(Place place, string template) =>
        place.Where.Things.First(t => t.Template.Equals(template, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void The_scene_s_script_finds_its_stranger_and_its_knight_and_the_stranger_talks_when_clicked()
    {
        InTheCellTown(place =>
        {
            // beginscene marked both to be talked to and paused their minds: the script was handed 1.1's things.
            var (stranger, knight) = (The(place, "stranger"), The(place, "convknight"));
            Assert.True((stranger.Actor!.Flags & Actor.Flag.TalkTo) != 0, "the stranger is not marked to be talked to");
            Assert.True((knight.Actor!.Flags & Actor.Flag.TalkTo) != 0, "the knight is not marked to be talked to");
            Assert.True((stranger.Actor.Flags & Actor.Flag.AiPaused) != 0);
            place.Scenes.Clicked(stranger, place.Christof);
            Assert.NotNull(place.Scenes.Talk!.Current);
            Assert.Equal("4_1_Stranger", place.Scenes.Talk.Current!.Talk.Name, ignoreCase: true);
        });
    }

    [Fact]
    public void Walking_into_the_region_outside_the_smithy_starts_the_first_conversation_on_the_new_ground()
    {
        InTheCellTown(place =>
        {
            var region = The(place, "trigger_region");
            Assert.Null(place.Scenes.Talk!.Current);
            place.Where.Move(place.Christof, new Vector3(region.Position.X, region.Position.Y, Floor));
            Assert.True(place.RunUntil(() => place.Scenes.Talk.Current is not null, 5f), "nothing was said outside the smithy");
            Assert.Equal("4_1_OutSmithy", place.Scenes.Talk.Current!.Talk.Name, ignoreCase: true);
            // TeleportPartyToConversation(…, 0): the party on the scene's first conversation start, which stands on the floor.
            var start = place.Where.ConversationStarts.First(c => c.Group == 0);
            Assert.Equal(Floor, start.Position.Z, 2);
            Assert.True(Vector2.Distance(new(start.Position.X, start.Position.Y), new(place.Christof.Position.X, place.Christof.Position.Y)) < 1f,
                        $"Christof is at {place.Christof.Position}, the conversation start at {start.Position}");
            Assert.Equal(Floor, place.Christof.Position.Z, 0);
        });
    }

    [Fact]
    public void A_click_exit_sign_taken_reaches_the_exits_seam_with_1_1s_exit_number()
    {
        InTheCellTown(place =>
        {
            var took = new List<int>();
            var was = CellExitSeam.Take;
            CellExitSeam.Take = (where, who, exit) => { took.Add(exit); return true; };   // dealt with: 1.1's journey is not begun
            try
            {
                var signs = place.Where.Things.Where(t => t.Script.Equals("ClickExit", StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.True(signs.Count >= 8, $"{signs.Count} click exits in the level's scene");
                foreach (var sign in signs)
                {
                    place.Where.Move(place.Christof, new Vector3(sign.Position.X, sign.Position.Y, Floor));
                    place.Scenes.Clicked(sign, place.Christof);
                    place.Run(0.2f);
                }
                Assert.NotEmpty(took);
                Assert.All(took, n => Assert.InRange(n, 0, 31));
                Assert.Null(place.Session.Chronicle!.Pending);
            }
            finally { CellExitSeam.Take = was; }
        });
    }
}
