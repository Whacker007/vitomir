// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The skyline's tier on the CPU (ours): a part's draws join the
/// frame's draw list wherever the camera's frustum meets them, kilometres past the streaming view
/// distance, and only there; a part far enough off is drawn as its far version; only its draws near the
/// camera cast the sun's shadow, in view or not; its materials join a level's with their texture slots renumbered.
/// </summary>
public class CellSkylineDrawTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;

    static L2MeshDraw Box(int material, Vector3 min, Vector3 max, int first) => new(material, first, 6, min, max, 1f);

    [Fact]
    public void A_skyline_draw_kilometres_beyond_the_view_distance_is_in_the_draw_list_when_in_view_and_out_when_behind()
    {
        // Looking north (+X) from 2 m up at the Old Town's middle.
        var eye = new Vector3(0, 0, 2) * M;
        var view = Matrix4x4.CreateLookAt(eye, eye + Vector3.UnitX, Vector3.UnitZ);
        var planes = ModernMath.FrustumPlanes(view * ModernMath.ReversedInfinitePerspective(1.0f, 16f / 9f, 10f));
        float viewDistance = new ModernStreamSettings().ViewDistanceMetres;
        var draws = new List<L2MeshDraw>
        {
            Box(30, new(1450, -300, 60), new(1600, 300, 180), 0),          // the castle, 1.5 km ahead
            Box(31, new(4900, -2000, -5), new(5100, 2000, 120), 6),        // hills 5 km ahead
            Box(31, new(-3100, -500, 0), new(-2900, 500, 60), 12),         // 3 km behind
            Box(32, new(200, 4000, -5), new(400, 4200, 0), 18),            // 4 km off to the left, outside the field of view
        };
        Assert.True(1450 > viewDistance && 4900 > viewDistance);
        var list = new List<ModernScene.Draw>();
        var seen = new List<int>();
        long triangles = CellSkylineDraws.Add(draws, M, planes, firstIndex: 1000, baseVertex: 500, list, (_, _, d) => seen.Add(d.FirstIndex));

        Assert.Equal(2, list.Count);
        Assert.Equal(4, triangles);
        Assert.Equal([0, 6], seen);
        Assert.Equal(new uint[] { 1000, 1006 }, list.Select(d => d.FirstIndex).ToArray());
        Assert.All(list, d => Assert.Equal(500, d.BaseVertex));
        Assert.Equal(new Vector3(1450, -300, 60) * M, list[0].Min);   // the boxes in engine units
        Assert.Equal([30, 31], list.Select(d => d.Material).ToArray());
    }

    [Fact]
    public void A_skyline_part_is_drawn_as_its_far_version_from_the_far_distance_on_and_as_its_near_one_inside_it()
    {
        var camera = new Vector3(0, 0, 2) * M;
        float farFrom = 900f * M;
        var near = (Min: new Vector3(300, -250, -5) * M, Max: new Vector3(800, 250, 40) * M);      // 300 m off
        var edge = (Min: new Vector3(900, -250, -5) * M, Max: new Vector3(1400, 250, 40) * M);     // its nearest point 900 m off
        var far = (Min: new Vector3(2000, -250, -5) * M, Max: new Vector3(2500, 250, 40) * M);
        var over = (Min: new Vector3(-250, -250, -5) * M, Max: new Vector3(250, 250, 40) * M);     // the camera inside its box
        Assert.Equal(0, CellSkylineDraws.Version(camera, near.Min, near.Max, farFrom, 2));
        Assert.Equal(1, CellSkylineDraws.Version(camera, edge.Min, edge.Max, farFrom, 2));
        Assert.Equal(1, CellSkylineDraws.Version(camera, far.Min, far.Max, farFrom, 2));
        Assert.Equal(0, CellSkylineDraws.Version(camera, over.Min, over.Max, farFrom, 2));
        Assert.Equal(0, CellSkylineDraws.Version(camera, far.Min, far.Max, farFrom, 1));            // a part with one version has only that
        Assert.Equal(0, CellSkylineDraws.Version(camera, far.Min, far.Max, 0f, 2));                 // 0: never far
        Assert.Equal(1, CellSkylineDraws.Version(new Vector3(0, 0, 1500) * M, near.Min, near.Max, farFrom, 2));   // 1.5 km up, the near tile is far
    }

    [Fact]
    public void Of_the_skyline_only_the_draws_near_the_camera_cast_the_sun_s_shadow_in_view_or_not_and_none_at_a_reach_of_0()
    {
        // Looking north (+X) from 2 m up; the camera stands inside the box of the land tile round the level.
        var camera = new Vector3(0, 0, 2) * M;
        var draws = new List<L2MeshDraw>
        {
            Box(30, new(40, -5, 0), new(50, 5, 10), 0),              // a house beside the level, ahead: within 150 m
            Box(31, new(-130, -5, 0), new(-120, 5, 10), 6),          // a house behind the camera, within 150 m
            Box(32, new(-250, -250, -5), new(250, 250, 1), 12),      // the land tile whose box holds the camera
            Box(33, new(1400, -300, 60), new(1600, 300, 180), 18),   // the castle
            Box(34, new(-3100, -500, 0), new(-2900, 500, 60), 24),   // hills behind
        };
        var casters = new List<ModernScene.Draw>();
        Assert.Equal(3, CellSkylineDraws.Casters(draws, M, firstIndex: 1000, baseVertex: 500, camera, 150 * M, casters));
        Assert.Equal([30, 31, 32], casters.Select(d => d.Material).ToArray());
        Assert.Equal([1000u, 1006u, 1012u], casters.Select(d => d.FirstIndex).ToArray());
        Assert.All(casters, d => Assert.Equal(500, d.BaseVertex));
        Assert.Equal(new Vector3(-130, -5, 0) * M, casters[1].Min);

        var none = new List<ModernScene.Draw>();
        Assert.Equal(0, CellSkylineDraws.Casters(draws, M, 0, 0, camera, 0, none));   // 0: none, not even the tile that holds the camera
        Assert.Empty(none);
    }

    [Fact]
    public void A_skyline_s_materials_are_appended_after_the_level_s_with_their_textures_and_images_renumbered()
    {
        var level = new GltfScene();
        level.Images.Add(new GltfImage { Name = "cobble" });
        level.Textures.Add(new GltfTexture(0, true, true));
        level.Materials.Add(new GltfMaterial { Name = "ground", BaseColorTexture = new(0, 0) });
        var sky = new GltfScene();
        sky.Images.Add(new GltfImage { Name = "facade" });
        sky.Images.Add(new GltfImage { Name = "tiles" });
        sky.Textures.Add(new GltfTexture(1, true, true));
        sky.Textures.Add(new GltfTexture(0, true, true));
        sky.Materials.Add(new GltfMaterial { Name = "city_roof", BaseColorTexture = new(0, 0) });
        sky.Materials.Add(new GltfMaterial { Name = "city_plaster", BaseColorTexture = new(1, 0), NormalTexture = GltfTextureSlot.None });
        sky.Materials.Add(new GltfMaterial { Name = "water_river" });

        int first = CellSkylineDraws.AppendMaterials(level, sky);

        Assert.Equal(1, first);
        Assert.Equal(["ground", "city_roof", "city_plaster", "water_river"], level.Materials.Select(m => m.Name).ToArray());
        Assert.Equal(["cobble", "facade", "tiles"], level.Images.Select(i => i.Name).ToArray());
        Assert.Equal("tiles", level.Images[level.Textures[level.Materials[1].BaseColorTexture.Texture].Image].Name);
        Assert.Equal("facade", level.Images[level.Textures[level.Materials[2].BaseColorTexture.Texture].Image].Name);
        Assert.False(level.Materials[2].NormalTexture.Present);
        Assert.True(ModernWater.IsWater(level.Materials[3]));
        Assert.False(ModernWater.IsWater(level.Materials[0]));
        Assert.Equal(0f, ModernWater.Flag(level.Materials[1]));
        Assert.True(ModernWater.Flag(level.Materials[3]) >= 1f);
    }
}
