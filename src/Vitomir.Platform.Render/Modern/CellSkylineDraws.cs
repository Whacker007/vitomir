// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/CellSkylineDraws.cs at b4185ef, reworked for the platform.
using System.Numerics;
using Vitomir.Formats.L2;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours: the skyline's tier**. A cell level's skyline
/// (<see cref="L2Skyline"/>) is the city round the level, made for distance; <see cref="CellStreamScene"/>
/// keeps every part of it loaded from the level's opening and draws it every frame, whatever the view
/// distance — the view distance governs the level's own far versions, and a castle 1.5 km off must not
/// be cut by an 800 m line (no distance cut-offs). What it does not see, the camera's
/// frustum leaves out, part by part and draw by draw. The sun's shadow cascades take a skyline draw only
/// when its box comes within <see cref="ModernStreamSettings.SkylineShadowMetres"/> of the camera, in view
/// or not (<see cref="Casters"/>), which the near tiles' sizes keep to the parts beside the level
/// (<see cref="Vitomir.Engine.Levels.Cells.CellSkyline"/>); the shadows' reach is the level's own box.
/// A part with two versions is drawn as its far one, simpler and of fewer draws, once its box is
/// <see cref="L2Skyline.FarFrom"/> metres or more from the camera (<see cref="Version"/>).
/// </summary>
internal static class CellSkylineDraws
{
    /// <summary>
    /// Which version of a skyline part to draw: its far one (1) when it has two
    /// (<paramref name="versions"/>) and its box (<paramref name="min"/>, <paramref name="max"/>) is at least
    /// <paramref name="farFrom"/> from <paramref name="camera"/> (all in the same units; 0: never far),
    /// else its near one (0).
    /// </summary>
    public static int Version(Vector3 camera, Vector3 min, Vector3 max, float farFrom, int versions) =>
        versions > 1 && farFrom > 0 && Vector3.DistanceSquared(Vector3.Clamp(camera, min, max), camera) >= farFrom * farFrom ? 1 : 0;

    /// <summary>
    /// A skyline part's draws for a frame, in engine units: each whose box (<paramref name="scale"/> × the
    /// part's metres) <paramref name="planes"/> can see, at any distance. The part's buffers start at
    /// <paramref name="firstIndex"/> and <paramref name="baseVertex"/> in the shared ones. Returns the
    /// triangles added. <paramref name="seen"/>, when given, hears of each draw added with its box (a
    /// texture streamer's request).
    /// </summary>
    public static long Add(IReadOnlyList<L2MeshDraw> draws, float scale, ReadOnlySpan<Vector4> planes, long firstIndex, long baseVertex,
                           List<ModernScene.Draw> into, Action<Vector3, Vector3, L2MeshDraw>? seen = null)
    {
        long triangles = 0;
        for (int k = 0; k < draws.Count; k++)      // by index: a list read through its interface makes an enumerator a call
        {
            var d = draws[k];
            var min = d.Min * scale; var max = d.Max * scale;
            if (!ModernMath.BoxVisible(planes, min, max)) continue;
            into.Add(new ModernScene.Draw((uint)(firstIndex + d.FirstIndex), (uint)d.IndexCount, d.Material, min, max, (int)baseVertex));
            triangles += d.IndexCount / 3;
            seen?.Invoke(min, max, d);
        }
        return triangles;
    }

    /// <summary>
    /// Adds to <paramref name="into"/> the draws of a skyline part (as <see cref="Add"/> places them) that cast
    /// the sun's shadow: those whose box comes within <paramref name="reach"/> of <paramref name="camera"/>
    /// (engine units), whether the camera sees them or not — the houses beside the level shade its edge; the
    /// castle 1 km off is no part of the cascades, which reach 250 m. A reach of 0 or less: none cast, not
    /// even a tile whose box holds the camera. Returns how many were added. The level's own draws all cast
    /// (<see cref="CellStreamScene"/> adds them).
    /// </summary>
    public static int Casters(IReadOnlyList<L2MeshDraw> draws, float scale, long firstIndex, long baseVertex, Vector3 camera, float reach,
                              List<ModernScene.Draw> into)
    {
        if (reach <= 0) return 0;
        int added = 0;
        for (int k = 0; k < draws.Count; k++)
        {
            var d = draws[k];
            var min = d.Min * scale; var max = d.Max * scale;
            if (Vector3.DistanceSquared(Vector3.Clamp(camera, min, max), camera) > reach * reach) continue;
            into.Add(new ModernScene.Draw((uint)(firstIndex + d.FirstIndex), (uint)d.IndexCount, d.Material, min, max, (int)baseVertex));
            added++;
        }
        return added;
    }

    /// <summary>
    /// Appends <paramref name="from"/>'s materials — with the textures and images they use — after
    /// <paramref name="into"/>'s, their slots renumbered, and returns the index of the first. The cook does
    /// this so a skyline's materials join the level's list (the manifest's <c>materials</c>); a renderer
    /// reading the level's glTF for its materials does it again with the skyline's.
    /// </summary>
    public static int AppendMaterials(GltfScene into, GltfScene from)
    {
        int first = into.Materials.Count, textureBase = into.Textures.Count, imageBase = into.Images.Count;
        into.Images.AddRange(from.Images);
        into.Textures.AddRange(from.Textures.Select(t => t with { Image = t.Image < 0 ? t.Image : t.Image + imageBase }));
        GltfTextureSlot Move(GltfTextureSlot s) => s.Present ? s with { Texture = s.Texture + textureBase } : s;
        foreach (var m in from.Materials)
        {
            m.BaseColorTexture = Move(m.BaseColorTexture);
            m.MetallicRoughnessTexture = Move(m.MetallicRoughnessTexture);
            m.NormalTexture = Move(m.NormalTexture);
            m.OcclusionTexture = Move(m.OcclusionTexture);
            m.EmissiveTexture = Move(m.EmissiveTexture);
            into.Materials.Add(m);
        }
        return first;
    }
}
