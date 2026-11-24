// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's renderer for 1.1's frames on a GPU.
using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// **The host's renderer for 1.1's frames** (<see cref="IClassicRenderer"/>) on <paramref name="gpu"/>: each drawer the controller
/// asks for is made when it asks, as the controller made its renderers until then (the order a process's GPU objects are made in
/// is kept), and drawn from what the controller hands it.
/// </summary>
public sealed class ClassicRenderer(Gpu gpu) : IClassicRenderer
{
    /// <summary>The GPU (the host's, and the tools' and the tests' that draw through this).</summary>
    public Gpu Gpu => gpu;

    public string Driver => gpu.Driver;
    public string ShaderFormatName => gpu.ShaderFormatName;
    public bool ColourIsBgra => gpu.ColourIsBgra;
    public bool SkyOff { get => SkyRenderer.Off; set => SkyRenderer.Off = value; }

    public IClassicWorld World(IReadOnlyList<ClassicPicture> lightmaps, ClassicSky? sky) => new ClassicFrameRenderer(gpu, lightmaps, sky);

    public IClassicScreen Screen() => new ClassicScreenRenderer(gpu);

    /// <summary>The 2D backend, then its model inserts (made in that order, as the runs made them).</summary>
    public IClassicInterface Interface()
    {
        var backend = new UiRenderer(gpu);
        backend.Models = new SceneInserts(gpu);
        return backend;
    }

    public IClassicOverlay Overlay() => new DeveloperOverlay(gpu);

    public IClassicEffects Effects() => new ScreenEffectsPass(gpu);
}
