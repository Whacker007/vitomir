// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The old glue as the tests drive it (the module never draws): a place's frame as the module
// says it (Vitomir.Classic's PlaceFrame) drawn by the host's renderer (ClassicRenderer), as the frame controller composes them.
using System.Numerics;
using SDL;
using Vitomir.Classic;
using Vitomir.Engine.Scene;
using Vitomir.Engine.World;

namespace Vitomir.Render.Tests;

/// <summary>
/// **The frame the tests draw**: the module's side of a place's frame (<see cref="PlaceFrame"/>: the game side, the poses,
/// the snapshot, what goes over it) on the host's renderer for 1.1's frames (<see cref="ClassicRenderer"/>), with the members the
/// old glue's <c>FrameRenderer</c> gave the tests, so their intent and assertions stay as they were.
/// </summary>
public sealed class FrameRenderer : IDisposable
{
    public FrameRenderer(Gpu gpu, Location location, Simulation? sim = null)
    {
        Renderer = new ClassicRenderer(gpu);
        Place = new PlaceFrame(Renderer, location, sim);
    }

    public ClassicRenderer Renderer { get; }
    public PlaceFrame Place { get; }
    /// <summary>The host's renderer's world for the place.</summary>
    public ClassicFrameRenderer Drawn => (ClassicFrameRenderer)Place.World;
    public WorldRenderer World => Drawn.World;
    public Simulation Sim => Place.Sim;
    public WorldFrame Game => Place.Game;
    public ScenePoses Poses => Place.Poses;
    public WorldSnapshot Snapshot => Place.Snapshot;
    public ISceneScripts Scripts { get => Place.Scripts; set => Place.Scripts = value; }
    public Engine.Ui.HoverGlow? Glow { get => Place.Glow; set => Place.Glow = value; }
    public FogSettings Fog { get => Place.Fog; set => Place.Fog = value; }
    public IFrameLayer? After { get => Place.After; set => Place.After = value; }
    public WalkTargetRenderer? Marker { get => (WalkTargetRenderer?)Place.Marker; set => Place.Marker = value; }
    public Matrix4x4? ViewProjection => Place.ViewProjection;
    public float ViewHeight { get => Place.ViewHeight; set => Place.ViewHeight = value; }
    public Func<float>? WorldView { get => Place.WorldView; set => Place.WorldView = value; }
    public int WorldRows(int height) => Place.WorldRows(height);

    public static Matrix4x4 WorldViewport(FreeCamera camera, int width, int height, int rows, float far, float magnify = 1f, float stretch = 1f, int drawn = 0) =>
        PlaceFrame.WorldViewport(camera, width, height, rows, far, magnify, stretch, drawn);

    public byte[] RenderToImage(int width, int height, FreeCamera camera) => Place.RenderToImage(width, height, camera);

    public void Dispose() => Place.Dispose();
}

/// <summary>A layer said and drawn at once onto a target (what the old glue's passes did; the UI tests draw one pass this way).</summary>
public static class LayerDraws
{
    public static unsafe void Draw(this IFrameLayer layer, SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, int width, int height,
                                   FreeCamera camera, Simulation sim)
    {
        var over = new ClassicOver();
        layer.Emit(over, width, height, null, camera, sim);
        over.Draw(cmd, target, width, height, null);
    }
}
