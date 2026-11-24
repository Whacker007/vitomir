// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What goes over a frame, as the module says it (the module never draws): the old glue's passes
// after the presenter (src/Vitomir.Render's FrameRenderer.IPass, Passes, ListPass, FrameRenderer.ScreenPass), each now emitting
// into the frame's ClassicOver what the host's renderer then draws.
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Classic;

/// <summary>
/// **Something said over a finished frame** (earlier, the old glue's <c>FrameRenderer.IPass</c>, which drew it): emitted
/// into the frame's <see cref="ClassicOver"/> for a target <paramref name="width"/> × <paramref name="height"/>, after the frame's
/// snapshot (<paramref name="frame"/>; null for a screen with no world) and before the host's renderer draws it.
/// </summary>
public interface IFrameLayer
{
    void Emit(ClassicOver into, int width, int height, ClassicFrame? frame, FreeCamera camera, Simulation sim);
}

/// <summary>
/// Several layers over one finished frame, in order. The game's own UI goes down first and the
/// developer overlay on top of it, so a diagnostic is never hidden behind the thing it is
/// diagnosing.
/// </summary>
public sealed class FrameLayers(params IFrameLayer[] layers) : IFrameLayer
{
    public IReadOnlyList<IFrameLayer> All { get; } = layers;

    public void Emit(ClassicOver into, int width, int height, ClassicFrame? frame, FreeCamera camera, Simulation sim)
    {
        foreach (var layer in All) layer.Emit(into, width, height, frame, camera, sim);
    }
}

/// <summary>
/// **An interface pass said** (<c>ListPass</c>): its canvas emptied, the pass emitted into it from the engine
/// (<see cref="IUiPass"/>), and the list handed to the 2D backend that draws it.
/// </summary>
public sealed class ListLayer(IClassicInterface backend, IUiPass pass) : IFrameLayer
{
    public IUiPass Pass { get; } = pass;

    public void Emit(ClassicOver into, int width, int height, ClassicFrame? frame, FreeCamera camera, Simulation sim)
    {
        var ui = Pass.Ui;
        ui.Clear();
        Pass.Emit(width, height, camera, sim);
        into.List(backend, ui.List);
    }
}

public static class ListLayers
{
    /// <summary><paramref name="pass"/> drawn by <paramref name="backend"/> over a frame.</summary>
    public static ListLayer Of(this IClassicInterface backend, IUiPass pass) => new(backend, pass);
}

/// <summary>
/// **The camera's fade and flash over the interface** (drawn after the interface): the frame's snapshot's
/// <see cref="ClassicFrame.Screen"/>, drawn by the host's <see cref="IClassicEffects"/>, which reads nothing else. Said
/// over a screen with no world (the front end's), it draws nothing.
/// </summary>
public sealed class ScreenLayer(IClassicEffects effects) : IFrameLayer
{
    public IClassicEffects Effects => effects;

    public void Emit(ClassicOver into, int width, int height, ClassicFrame? frame, FreeCamera camera, Simulation sim)
    {
        if (frame is not null) into.Screen(effects);
    }
}
