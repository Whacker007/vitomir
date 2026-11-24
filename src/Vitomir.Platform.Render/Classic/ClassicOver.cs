// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What goes over a 1.1 frame, in order (the old glue's passes after the presenter, now data the module hands the host's
// renderer).
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using Vitomir.Platform.Interface;
using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// **What goes over a frame, in order**: the interface's draw list and the backend that draws it, the camera's fade
/// and flash (the frame's own <see cref="ClassicFrame.Screen"/>, drawn after the interface), and the developer overlay's
/// quads. The module fills it each frame, after the frame's snapshot, from its passes (what <c>FrameRenderer.After</c> drew);
/// the host's renderer draws it after the brightness curve, so none of it goes through the curve.
/// </summary>
public sealed class ClassicOver
{
    internal enum Kind { List, Screen, Overlay }

    internal readonly record struct Item(Kind Kind, object Drawer, DrawList? List, ClassicOverlayQuads? Quads);

    readonly List<Item> items = [];

    /// <summary>How many things go over the frame.</summary>
    public int Count => items.Count;

    /// <summary>Emptied for the next frame.</summary>
    public void Clear() => items.Clear();

    /// <summary>A draw list, drawn by <paramref name="backend"/>.</summary>
    public void List(IClassicInterface backend, DrawList list) => items.Add(new(Kind.List, backend, list, null));

    /// <summary>The frame's fade and flash, drawn by <paramref name="effects"/> (nothing over a screen with no world).</summary>
    public void Screen(IClassicEffects effects) => items.Add(new(Kind.Screen, effects, null, null));

    /// <summary>The developer overlay's quads, drawn by <paramref name="overlay"/>.</summary>
    public void Overlay(IClassicOverlay overlay, ClassicOverlayQuads quads) => items.Add(new(Kind.Overlay, overlay, null, quads));

    /// <summary>
    /// **Drawn, in order** onto <paramref name="target"/> (<paramref name="width"/> × <paramref name="height"/>, the picture's): the
    /// renderer's, after the presenter; <paramref name="frame"/> the frame under it, or null for a screen with no world.
    /// </summary>
    public unsafe void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, int width, int height, ClassicFrame? frame)
    {
        foreach (var item in items)
            switch (item.Kind)
            {
                case Kind.List:
                    ((UiRenderer)item.Drawer).DrawTo(cmd, target, item.List!);
                    break;
                case Kind.Screen:
                    if (frame is not null) ((ScreenEffectsPass)item.Drawer).Draw(cmd, target, width, height, frame.Screen);
                    break;
                case Kind.Overlay:
                    ((DeveloperOverlay)item.Drawer).Draw(cmd, target, item.Quads!);
                    break;
            }
    }
}

/// <summary>One corner of the developer overlay's triangles: pixels from the top left, the font atlas's place (−1 for a solid box), the colour.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct OverlayVertex
{
    public Vector2 Pos;
    public Vector2 Uv;
    public Vector4 Colour;
}

/// <summary>
/// **The developer overlay's frame**: its triangles, laid out by the module (<c>Overlay</c>, <c>WatchOverlay</c>: what to
/// say and where), for a surface of <see cref="Width"/> × <see cref="Height"/> pixels; the host's renderer draws them straight
/// onto the finished frame (<see cref="DeveloperOverlay"/>).
/// </summary>
public sealed class ClassicOverlayQuads
{
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public List<OverlayVertex> Vertices { get; } = [];
}
