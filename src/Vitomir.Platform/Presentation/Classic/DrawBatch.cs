// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The level's and the things' geometry as the 1.1 look draws it (earlier, in Engine/Scene/SceneMesh.cs; data of the Classic
// snapshot).
using System.Numerics;
using System.Runtime.InteropServices;
using Vitomir.Formats;

namespace Vitomir.Platform.Presentation.Classic;

/// <summary>One vertex as the GPU sees it (32 bytes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SceneVertex
{
    public Vector3 Position;
    public Vector2 Uv;
    public Vector2 LightmapUv;
    /// <summary>Light as R, G, B, A bytes (the game files store B, G, R, A).</summary>
    public uint Color;

    public static uint FromBgra(uint bgra) => ((bgra >> 16) & 0xff) | (bgra & 0xff00) | ((bgra & 0xff) << 16) | (bgra & 0xff000000);
}

public enum Lighting { Lightmap, Vertex, Full }

/// <summary>Which part of the location a batch belongs to; the sky is drawn between the two.</summary>
public enum BatchLayer { Level, Thing }

/// <summary>One draw call.</summary>
/// <param name="LightmapPage">For <see cref="Lighting.Lightmap"/>: the static (style 0) page.</param>
/// <param name="Center">Centre of the batch's bounds, for sorting translucent batches.</param>
public sealed record DrawBatch(int Material, Lighting Light, int LightmapPage, bool TwoSided, float AlphaScale,
                               uint FirstIndex, uint IndexCount, Vector3 Center, string Source,
                               BatchLayer Layer = BatchLayer.Level, int Thing = -1)
{
    /// <summary>
    /// The surface's lightmap layers, in file order: one steady layer (style 0) is the ordinary
    /// case; more than one means a light style animates the surface and it takes a draw per layer.
    /// </summary>
    public NilLightmapLayer[] Layers { get; init; } = [];

    /// <summary>
    /// For a thing's batch: which model instance it is part of — the thing's own
    /// (<c>NamedShapes.Body</c>, <see cref="Body"/>) or one of its sub-model slots — and the mesh number
    /// (<c>MeshNum</c>) of the group it was built from. Together they are what a named shape's hide
    /// bit is looked up by (the engine's <c>NamedShapes.IsHidden</c>).
    /// </summary>
    public int Slot { get; init; } = Body;
    /// <inheritdoc cref="Slot"/>
    public int Mesh { get; init; } = -1;

    /// <summary>The thing's own model, not one of its sub-model slots (the engine's <c>NamedShapes.Body</c>).</summary>
    public const int Body = -1;

    /// <summary>For a level batch: the sector it was built from (−1 for a thing's).</summary>
    public int Sector { get; init; } = -1;

    /// <summary>
    /// For a level batch: each of its group's surfaces and the run of the index buffer its triangles take, in order (ours):
    /// what a surface drawn see-through is cut out of the batch by (<see cref="ClassicScene.Faded"/>). Empty for a thing's.
    /// </summary>
    public SurfaceRun[] Runs { get; init; } = [];
}

/// <summary>One level surface's triangles in the index buffer: its number in its sector, and its run (a fan of its corners).</summary>
public readonly record struct SurfaceRun(int Surface, uint FirstIndex, uint IndexCount);
