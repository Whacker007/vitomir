// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's renderer for 1.1's frames (the module never draws). What 1.1's frame controller hands its host to draw, and the handles it keeps while a place is shown.
using System.Numerics;
using SDL;
using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// **The host's renderer for 1.1's frames** (what <see cref="IClassicHost.OpenGpu"/> gives): 1.1's frame controller asks it
/// for the drawers a run keeps (a place's world, a screen with no world, the interface's 2D backend, the developer overlay, the
/// screen's fade and flash), each made at the point the controller made its renderer until then, and hands them what each frame
/// is — the Classic snapshot (<see cref="ClassicFrame"/>) and what goes over it (<see cref="ClassicOver"/>) — to draw. Every
/// type it takes is the platform's; the controller holds no GPU object and makes no GPU call.
/// </summary>
public interface IClassicRenderer
{
    /// <summary>The GPU's driver's name (the place's log line).</summary>
    string Driver { get; }
    /// <summary>The shaders' format's name (the place's log line).</summary>
    string ShaderFormatName { get; }
    /// <summary>Whether a still's bytes come back in BGRA order (a still written swaps them).</summary>
    bool ColourIsBgra { get; }
    /// <summary>The sky never drawn (the console's <c>sky 0</c>); every place's.</summary>
    bool SkyOff { get; set; }

    /// <summary>A place's world, for the place's lightmaps and its sky as its snapshot first sent them.</summary>
    IClassicWorld World(IReadOnlyList<ClassicPicture> lightmaps, ClassicSky? sky);
    /// <summary>A screen with no world: black, and what goes over it (the front end, a film, the loading plaque).</summary>
    IClassicScreen Screen();
    /// <summary>The interface's 2D backend (the draw lists) with its model inserts (the heads, the portrait).</summary>
    IClassicInterface Interface();
    /// <summary>The developer overlay's drawer (its quads, <see cref="ClassicOverlayQuads"/>).</summary>
    IClassicOverlay Overlay();
    /// <summary>The camera's fade and flash drawn over everything the game draws (the frame's <see cref="ClassicFrame.Screen"/>).</summary>
    IClassicEffects Effects();
}

/// <summary>
/// **A place's world as the host's renderer keeps it** (what <c>FrameRenderer</c> drew with): the level and its things, the
/// sky, the shadows, the mirrors, the particles, the blobs, the decals, the halos, the click ring and the brightness curve.
/// </summary>
public interface IClassicWorld : IDisposable
{
    /// <summary>The brightness curve's gamma (the Options' brightness, the console's <c>gamma</c>).</summary>
    float Gamma { get; set; }
    /// <summary>A viewer aid: the sky over the whole world instead of inside its window (<c>--sky-everywhere</c>).</summary>
    bool SkyEverywhere { get; set; }
    /// <summary>The sky's models drawn only if their name holds <paramref name="only"/>, never if it holds <paramref name="hide"/> (<c>--sky-only</c>, <c>--sky-hide</c>); none for a place with no sky.</summary>
    void Sky(string? only, string? hide);
    /// <summary>A click ring for this world, of the place's material <paramref name="material"/> (loaded by the game first).</summary>
    IClassicRing NewRing(int material);
    /// <summary>The ring drawn where the frame shows one; null draws none.</summary>
    IClassicRing? Ring { get; set; }
    /// <summary>A world drawn under the things instead of the level's own surfaces (a cell level's world); null for every 1.1 level.</summary>
    IClassicWorldBelow? Below { get; set; }

    /// <summary>Textures the place's materials name that could not be had (the place's log line).</summary>
    int MissingTextures { get; }
    /// <summary>Textures made so far (the slow frames' detail).</summary>
    int TexturesMade { get; }
    /// <summary>Times the scene's buffers were built again (the slow frames' detail).</summary>
    int Rebuilds { get; }
    /// <summary>Pipelines made so far (the frame log).</summary>
    int PipelinesMade { get; }
    /// <summary>The animated things the last frame sent up, and those it passed over out of view.</summary>
    (int Posed, int Passed) LastAnimated { get; }
    /// <summary>Where the last frame's drawing went, milliseconds of this thread's time (the slow-frame line).</summary>
    (double Animated, double Shadows, double Mirrors, double World, double Interface) LastDrawMs { get; }

    /// <summary>The frame drawn into the host's picture: <paramref name="frame"/>'s world, then <paramref name="over"/> over it.</summary>
    void Draw(in GpuPicture picture, ClassicFrame frame, ClassicOver? over);
    /// <summary>The same with no window, its pixels handed back (RGBA, top row first; <see cref="ColourIsBgra"/> may swap them).</summary>
    byte[] Still(int width, int height, ClassicFrame frame, ClassicOver? over);
}

/// <summary>A click ring a world draws (<see cref="IClassicWorld.NewRing"/>).</summary>
public interface IClassicRing : IDisposable;

/// <summary>A screen with no world (<see cref="IClassicRenderer.Screen"/>): black, and what goes over it.</summary>
public interface IClassicScreen
{
    /// <summary>Black into the host's picture, and <paramref name="over"/> on it.</summary>
    void Draw(in GpuPicture picture, ClassicOver? over);
    /// <summary>The same with no window, as RGBA with the top row first.</summary>
    byte[] Still(int width, int height, ClassicOver? over);
}

/// <summary>The interface's 2D backend (<see cref="IClassicRenderer.Interface"/>): what a draw list in <see cref="ClassicOver"/> is drawn by.</summary>
public interface IClassicInterface : IDisposable
{
    /// <summary>The model insert's renderer for the draw list's scene <paramref name="scene"/> made now (the heads' was made with the run's passes).</summary>
    void MakeInsert(int scene);
    /// <summary>The model inserts' renderers let go (before the backend itself, as the run let them go).</summary>
    void DisposeInserts();
}

/// <summary>The developer overlay's drawer (<see cref="IClassicRenderer.Overlay"/>).</summary>
public interface IClassicOverlay : IDisposable;

/// <summary>The screen's fade and flash drawer (<see cref="IClassicRenderer.Effects"/>).</summary>
public interface IClassicEffects : IDisposable;

/// <summary>
/// **A world drawn before the things instead of the level's own surfaces** (ours: a cell level's, through the
/// modern path; switched off, no player's switch). It fills the frame's colour and the game's depth buffer; the
/// world pass then keeps both and draws the things into them. Only <c>vitomir-tools</c> make one.
/// </summary>
public unsafe interface IClassicWorldBelow : IDisposable
{
    /// <param name="x0">The world's first column; <paramref name="cols"/> × <paramref name="rows"/> its size from the top,
    /// <paramref name="magnify"/> its zoom; <paramref name="viewProj"/> the game's own view and projection.</param>
    bool Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* colour, SDL_GPUTexture* depth, int width, int height,
              int x0, int cols, int rows, float magnify, Matrix4x4 viewProj);
}
