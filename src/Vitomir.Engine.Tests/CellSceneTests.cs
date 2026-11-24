// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Ours: a 1.1 scene in a cell level**. A level cooked here — one flat
/// floor a quarter of a metre above 1.1's datum, as wide as the daytime Old Town, with one house standing
/// where the square's knights talk — carries the Old Town's scene by name; the game's own loader then puts
/// 1.1's things in it: each with its 1.1 template and x and y, standing on the new floor where 1.1 has it on
/// the ground and at its 1.1 height where it hangs, filed under the level's one sector; the dropped lanterns
/// keep their places in the thing list and nothing else; the people stay on the ground as the world is
/// stepped; one of them sees another across the square and not through the house (which asks the cooked
/// triangles, not 1.1's trees); and the package holds nothing of the scene but its name. The scene is read
/// from the install, so everything here skips without one.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellSceneTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;
    /// <summary>The floor's height (engine units): a quarter of a metre over 1.1's ground at 0.</summary>
    const float Floor = 18f;
    const string Level = "P1_OTDY", Chapter = "OTDY_4_1";

    /// <summary>
    /// A floor of 4 m squares from (−52, −60) to (92, 72) m at <see cref="Floor"/>, and a house 4 m square and
    /// 4 m high west of the two knights of <c>OTDY_4_1</c>, from (−14, −10) to (−10, −6) m.
    /// </summary>
    static (List<Vector3> V, List<int> I, List<int> Mat) Ground()
    {
        var v = new List<Vector3>(); var idx = new List<int>(); var mat = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int m)
        {
            int at = v.Count;
            v.AddRange([a * M, b * M, c * M, d * M]);
            idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            mat.AddRange([m, m]);
        }
        const float Z = Floor / M;
        for (int x = -52; x < 92; x += 4)
            for (int y = -60; y < 72; y += 4)
                Quad(new(x, y, Z), new(x + 4, y, Z), new(x + 4, y + 4, Z), new(x, y + 4, Z), 0);
        const float X0 = -14, X1 = -10, Y0 = -10, Y1 = -6, H = 4 + Z;
        Quad(new(X0, Y0, Z), new(X1, Y0, Z), new(X1, Y0, H), new(X0, Y0, H), 1);
        Quad(new(X1, Y0, Z), new(X1, Y1, Z), new(X1, Y1, H), new(X1, Y0, H), 1);
        Quad(new(X1, Y1, Z), new(X0, Y1, Z), new(X0, Y1, H), new(X1, Y1, H), 1);
        Quad(new(X0, Y1, Z), new(X0, Y0, Z), new(X0, Y0, H), new(X0, Y1, H), 1);
        Quad(new(X0, Y0, H), new(X1, Y0, H), new(X1, Y1, H), new(X0, Y1, H), 1);
        return (v, idx, mat);
    }

    /// <summary>Cooks the floor into a package called <c>cellscene</c> that carries the Old Town's scene with <paramref name="drop"/>.</summary>
    static string Cook(params string[] drop) => Cook(drop, []);

    /// <summary>The same, with anchors that place things of the scene (<see cref="L2Scene.Places"/>).</summary>
    static string Cook(string[] drop, L2Anchor[] places)
    {
        var (v, idx, mat) = Ground();
        var root = Path.Combine(Path.GetTempPath(), "vitomir-cellscene-" + Guid.NewGuid().ToString("N"));
        CellCook.Cook(new CellCookInput
        {
            Name = "cellscene", Vertices = [.. v], Indices = [.. idx], TriangleMaterials = [.. mat], Materials = ["ground", "wall"],
            Ground = m => m == "ground", Passable = _ => false,
            Anchors = [new L2Anchor { Id = "cellscene/starts/0", Kind = "start", Scene = "cellscene", Name = "playerstart00",
                                      Position = [20, 12, 1], Rotation = [0, 0, -180], Group = 0 }, .. places],
            Scene = new L2Scene { Level = Level, Chapter = Chapter, Drop = [.. drop] },
        }, L2Package.PathFor(root, "cellscene"));
        return root;
    }

    /// <summary>Loads the Old Town from the cooked package standing in for it, runs <paramref name="with"/>, and puts everything back.</summary>
    static void Play(string[] drop, Action<Location, NsdScene, NsdScene> with, L2Anchor[]? places = null)
    {
        using var files = Install.Open();
        var root = Cook(drop, places ?? []);
        var was = L2Levels.Folder;
        try
        {
            L2Levels.Folder = root;
            L2Levels.StandIns[Level] = L2Package.PathFor(root, "cellscene");
            var place = Location.Load(files, Level, scene: Chapter, player: "Christof");
            Assert.NotNull(CellLevel.Of(place.Level));
            with(place, NsdScene.Read(files.Read($"scenes/{Level}.nsd"), Level), NsdScene.Read(files.Read($"scenes/{Chapter}.nsd"), Chapter));
        }
        finally
        {
            L2Levels.StandIns.Remove(Level);
            L2Levels.Folder = was;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_package_names_the_scene_and_holds_nothing_of_it()
    {
        using var files = Install.Open();
        var root = Cook("class:lantern");
        try
        {
            var manifest = L2Package.Open(L2Package.PathFor(root, "cellscene")).Manifest;
            Assert.Equal(Level, manifest.Scene!.Level);
            Assert.Equal(Chapter, manifest.Scene.Chapter);
            Assert.Equal(["class:lantern"], manifest.Scene.Drop!);
            Assert.Single(manifest.Anchors);                                          // its own start, and no thing of 1.1's
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var text = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(file));
                Assert.DoesNotContain("convknight", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("tracker_thing", text, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Without_a_stand_in_the_level_is_1_1s_own()
    {
        using var files = Install.Open();
        var place = Location.Load(files, Level, scene: Chapter);
        Assert.Null(CellLevel.Of(place.Level));
        Assert.Contains(place.Things, t => t.Sector > 0);
    }

    [Fact]
    public void Every_thing_of_both_scenes_is_there_in_1_1s_order_at_1_1s_place_on_the_new_ground()
    {
        Play([], (place, world, chapter) =>
        {
            var placed = world.Things.Concat(chapter.Things).ToArray();
            Assert.Equal(placed.Length + 1, place.Things.Count);                     // and Christof, last
            int stood = 0, hung = 0;
            for (int i = 0; i < placed.Length; i++)
            {
                var (was, now) = (placed[i], place.Things[i]);
                Assert.Equal(was.Template, now.Template);
                Assert.Equal(i, now.Index);
                Assert.Equal(was.Position.X, now.Position.X, 2);
                Assert.Equal(was.Position.Y, now.Position.Y, 2);
                Assert.Equal(0, now.Sector < 0 ? 0 : now.Sector);                    // the shell's one sector (a region has none)
                Assert.Equal(was.Orientation, now.Orientation);
                Assert.Equal(was.Keyframes.Length, now.Keyframes.Length);
                if (MathF.Abs(was.Position.Z - Floor) <= CellScene.StandTolerance)
                {
                    // On the ground in 1.1: on the new floor here, its keyframes moved with it.
                    Assert.Equal(Floor, now.Position.Z, 2);
                    for (int k = 0; k < was.Keyframes.Length; k++)
                        Assert.Equal(was.Keyframes[k].Position.Z + (Floor - was.Position.Z), now.Keyframes[k].Position.Z, 2);
                    stood++;
                }
                else { Assert.Equal(was.Position.Z, now.Position.Z, 2); hung++; }
            }
            Assert.True(stood > 30, $"only {stood} things stand on the ground");
            Assert.True(hung > 30, $"only {hung} things hang at 1.1's height");      // the lanterns, the signs, the trackers
            // What became of each is on record, by 1.1's scene and number.
            var cells = CellLevel.Of(place.Level)!;
            Assert.Equal(placed.Length, cells.Placements.Count);
            Assert.Equal(world.Things.Length, cells.Placements.Count(p => p.Scene == Level));
            var knight = cells.Placements.Single(p => p.Scene == Chapter && p.Template == "convknight");
            Assert.Equal("ground", knight.How);
            Assert.Equal(ThingType.Actor, knight.Type);
            Assert.Equal("an actor", knight.Protected);
            // 1.1's starts, each on the floor where 1.1's ground is near it, in the one sector; the scene's conversation starts too.
            Assert.Equal(world.Starts.Length, place.AllStarts.Length);
            Assert.All(place.AllStarts, s => Assert.Equal(0u, s.Sector));
            Assert.Contains(place.AllStarts, s => MathF.Abs(s.Position.Z - Floor) < 0.01f);
            Assert.Equal(chapter.ConversationStarts.Length, place.ConversationStarts.Length);
            Assert.All(place.ConversationStarts, s => Assert.Equal(Floor, s.Position.Z, 2));
            // The scene's music is 1.1's; the far clip and the fog are the cell level's.
            Assert.Equal(chapter.Music, place.Scene!.Music);
            Assert.Equal(CellShell.FarClip, place.FarPlane);
            Assert.Equal(0u, place.Scene.FogType);
            Assert.Null(place.Sky);
        });
    }

    [Fact]
    public void The_people_stand_on_the_ground_and_stay_on_it_as_the_world_is_stepped()
    {
        Play([], (place, _, _) =>
        {
            var people = place.Things.Where(t => t.IsActor && t.Type == ThingType.Actor && t.MoveType != WorldThing.MovePhysics
                                                 && MathF.Abs(t.Position.Z - Floor) < 1f).ToList();
            Assert.True(people.Count >= 9, $"{people.Count} people on the ground");
            Assert.All(people, p => Assert.True(place.Walk!.Find(p.Position) >= 0, $"{p.Template} is on no ground at {p.Position}"));
            var sim = new Simulation(place);
            for (int i = 0; i < 180; i++) sim.Step(1f / 60f);
            Assert.All(people, p => Assert.Equal(Floor, p.Position.Z, 0));
            Assert.Equal(Floor, place.Player!.Position.Z, 0);
        });
    }

    [Fact]
    public void A_dropped_lantern_keeps_its_place_in_the_list_and_nothing_else_and_a_container_is_never_dropped()
    {
        Play(["class:lantern", "container_barrel1", "tracker_thing", "convknight"], (place, world, chapter) =>
        {
            Assert.Equal(world.Things.Length + chapter.Things.Length + 1, place.Things.Count);
            int lanterns = 0;
            for (int i = 0; i < world.Things.Length; i++)
            {
                var (was, now) = (world.Things[i], place.Things[i]);
                if (was.Template.StartsWith("lantern", StringComparison.OrdinalIgnoreCase))
                {
                    lanterns++;
                    Assert.Equal("", now.Template);
                    Assert.Equal("", now.Model);
                    Assert.Equal("", now.Script);
                    Assert.Equal(ThingType.None, now.Type);
                    Assert.Equal(CollideShape.None, now.Collide);
                }
                else Assert.Equal(was.Template, now.Template);                        // barrels with loot and click exits stay, whatever the rule says
            }
            Assert.True(lanterns > 20, $"{lanterns} lanterns in the level's scene");
            Assert.Contains(place.Things, t => t.Template == "convknight" && t.IsActor);
            var cells = CellLevel.Of(place.Level)!;
            Assert.Equal(lanterns, cells.Placements.Count(p => p.Dropped));
            Assert.Equal("a container", cells.Placements.First(p => p.Template == "container_barrel1").Protected);
        });
    }

    [Fact]
    public void An_anchor_with_a_thing_s_id_moves_it_and_turns_it_and_the_layout_s_places_become_such_anchors()
    {
        // The layout's scene.place, as the cook reads it: the first knight of the chapter scene four metres east, facing north.
        var layout = System.Text.Json.Nodes.JsonNode.Parse("""
            {"level": "P1_OTDY", "scene": {"chapter": "OTDY_4_1", "drop": [], "place": [
                {"id": "OTDY_4_1/things/0", "position": [4, -4.5, 3], "rotation": [0, 0, 90]},
                {"id": "not a thing", "position": [0, 0, 0]}, {"id": "OTDY_4_1/things/1", "position": [1, 2]}]}}
            """);
        var places = L2Scene.Places(layout);
        var only = Assert.Single(places);
        Assert.Equal((L2Scene.ThingAnchor, Chapter, 0), (only.Kind, only.Scene, only.Index));
        var bound = L2Scene.FromLayout(layout, null)!;
        Assert.Equal((Level, Chapter), (bound.Level, bound.Chapter));
        Assert.Empty(bound.Drop!);
        Assert.True(bound.Unknown is null || !bound.Unknown.ContainsKey("place"));
        Play([], (place, world, chapter) =>
        {
            var knight = place.Things[world.Things.Length];                          // the chapter's thing 0
            Assert.Equal(chapter.Things[0].Template, knight.Template);
            Assert.Equal(4 * M, knight.Position.X, 2);
            Assert.Equal(-4.5f * M, knight.Position.Y, 2);
            Assert.Equal(3 * M, knight.Position.Z, 2);                               // 3 m up there is no ground within reach: where the anchor says
            Assert.Equal(new Vector3(0, 0, 90), knight.Orientation);
            var other = place.Things[world.Things.Length + 1];                       // nobody moved the second
            Assert.Equal(chapter.Things[1].Position.X, other.Position.X, 2);
            Assert.True(CellLevel.Of(place.Level)!.Placements.Single(p => p.Scene == Chapter && p.Index == 0).Anchored);
        }, [.. places]);
        // On the ground when the anchor is near it.
        Play([], (place, world, chapter) =>
             {
                 Assert.Equal(Floor, place.Things[world.Things.Length].Position.Z, 2);
                 Assert.Equal(chapter.Things[0].Orientation, place.Things[world.Things.Length].Orientation);   // "thing-at": 1.1's facing kept
             },
             [new L2Anchor { Id = "OTDY_4_1/things/0", Kind = L2Scene.ThingAtAnchor, Scene = Chapter, Index = 0, Position = [4, -4.5f, 0.1f] }]);
    }

    [Fact]
    public void The_props_class_drops_all_of_1_1s_dressing_and_nothing_a_script_or_the_player_needs()
    {
        Play([L2Scene.AllProps], (place, world, chapter) =>
        {
            var cells = CellLevel.Of(place.Level)!;
            Assert.All(cells.Placements.Where(p => p.Dropped), p => Assert.Equal(ThingType.Prop, p.Type));
            Assert.All(cells.Placements.Where(p => p.Dropped), p => Assert.Equal("", p.Protected));
            Assert.DoesNotContain(place.Things, t => t.InWorld && t.Type == ThingType.Prop && t.Script.Length == 0 && t.Scene.Model is null
                                                     && t.Template.StartsWith("barrel", StringComparison.OrdinalIgnoreCase));
            // Every click exit, container, region, ghost with a script and person is still there.
            Assert.Equal(world.Things.Count(t => t.Script.Length > 0), place.Things.Take(world.Things.Length).Count(t => t.Scene.Script.Length > 0));
            Assert.Equal(7, place.Things.Count(t => t.Type == ThingType.Container));
            Assert.Equal(chapter.Things.Length, place.Things.Skip(world.Things.Length).Take(chapter.Things.Length).Count(t => t.Template.Length > 0));
        });
    }

    [Fact]
    public void A_click_exit_is_kept_whole_and_only_not_drawn()
    {
        Play([], (place, world, _) =>
        {
            var exits = place.Things.Where(t => t.Template == "tracker_thing").ToList();
            Assert.Equal(world.Things.Count(t => t.Template == "tracker_thing"), exits.Count);
            Assert.All(exits, t =>
            {
                Assert.True(t.ModelHidden, "1.1's door is drawn");
                Assert.Equal("ClickExit", t.Script);
                Assert.NotNull(t.Scene.Model);                                        // its geometry is still its body's box
                Assert.NotEqual(CollideShape.None, t.Collide);
                Assert.True(t.InWorld);
            });
            // Nothing else is hidden that 1.1 does not hide: a barrel is drawn.
            Assert.Contains(place.Things, t => t.Template.StartsWith("barrel", StringComparison.OrdinalIgnoreCase) && !t.ModelHidden);
        });
    }

    [Fact]
    public void One_of_the_people_sees_another_across_the_open_square_and_not_through_a_house()
    {
        Play([], (place, _, _) =>
        {
            var (a, b) = (place.Things.First(t => t.Template == "convknight"), place.Things.First(t => t.Template == "convknight2"));
            Assert.True(Sight.Sees(place.Level, a, b), "the two knights stand in the open, a few metres apart");
            // Stood either side of the house (x −14..−10 m, y −10..−6 m), neither sees the other; the house is cooked triangles.
            place.Move(a, new Vector3(-16 * M, -8 * M, Floor));
            place.Move(b, new Vector3(-8 * M, -8 * M, Floor));
            Assert.False(Sight.Sees(place.Level, a, b), "the house's walls stand between them");
            place.Move(b, new Vector3(-16 * M, -3 * M, Floor));
            Assert.True(Sight.Sees(place.Level, a, b));
        });
    }

    [Fact]
    public void The_cutscene_camera_s_eye_and_the_clear_line_test_stop_at_the_house()
    {
        Play([], (place, _, _) =>
        {
            // Looking at a point west of the house from an eye east of it: the eye is pulled onto the west wall (x −14 m).
            var (lookAt, eye) = (new Vector3(-16 * M, -8 * M, Floor + 60), new Vector3(-8 * M, -8 * M, Floor + 60));
            var pulled = CameraSweep.CutsceneEye(place, 0, lookAt, eye);
            Assert.NotNull(pulled);
            Assert.Equal(-14 * M, pulled!.Value.X, 1);
            // With nothing between, the eye stays where the shot put it.
            Assert.Null(CameraSweep.CutsceneEye(place, 0, lookAt, new Vector3(-16 * M, -3 * M, Floor + 60)));
            // The line test: stopped by the house, clear beside it; its reach is in lengths of the direction.
            var across = eye - lookAt;
            Assert.False(place.Solid!.Clear(lookAt, 0, across, 1f));
            Assert.True(place.Solid.Clear(lookAt, 0, across, 0.2f));                  // 1.6 m of the 8: short of the wall
            Assert.True(place.Solid.Clear(lookAt, 0, new Vector3(0, 5 * M, 0), 1f));
        });
    }
}
