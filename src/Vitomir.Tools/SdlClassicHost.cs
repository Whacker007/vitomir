// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
// The frame controller's host in vitomir-tools (the old program's, moved). Its window, GPU, events, clock and
// pointer are the platform's (`Vitomir.Platform.Render/Windowing/SdlHost.cs`), which vitomir-platform's Classic host is too;
// the harness's scripted input is the platform's host's too, and what is this program's own is the sound device;
// (the module never draws) also a cell level's world for its tools, drawn by its old modern path.
using Vitomir.Classic;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Platform.Audio;
using Vitomir.Platform.Render.Windowing;
using Vitomir.Render;
using Vitomir.Render.Modern;

namespace Vitomir;

/// <summary>
/// **The frame controller's host in <c>vitomir-tools</c>** (<see cref="IClassicHost"/>): the platform's SDL host
/// (<see cref="SdlHost"/>: the window and every one of its modes, the GPU and the host's renderer for 1.1's frames, the events,
/// the clock, the pointer and the focus, and the harness's scripted input, <see cref="ScriptedInput"/>:
/// <c>--ui-click</c>, <c>--ui-type</c>, <c>--menu-steps</c> pushed into SDL's queue), with this program's sound device
/// (<see cref="SdlSpeakers"/>) and, its tools' cell world (<see cref="CellWorldHost"/>).
/// </summary>
sealed class SdlClassicHost : SdlHost
{
    public override AudioOutput Speakers(AudioFormat format) => SdlSpeakers.Output(format);

    /// <summary>
    /// The GPU just opened: a cell level's world (which this program's tools — <c>--level-l2</c>,
    /// <c>--cook-l2</c> — are the only way into) is drawn on it under 1.1's things (the platform's host offers none).
    /// </summary>
    protected override void Opened(Gpu gpu) => CellWorlds.Make = (place, sim, world, camera) => CellWorldHost.For(gpu, place, sim, world, camera);

    /// <summary>A cell level Direct3D 12 could not draw (ours: <see cref="D3D12SamplerBudget"/>): the run ends.</summary>
    public override string? Stopped => D3D12SamplerBudget.Stopped;
}

/// <summary>
/// **A cell level's world for the module** (ours; what the old glue's frame renderer made for itself, <see cref="CellWorldPass"/>
/// with its characters, <see cref="CellThingPass"/>, and the game's clock), as the module holds it (<see cref="ICellWorld"/>).
/// </summary>
sealed class CellWorldHost(CellWorldPass pass) : ICellWorld
{
    /// <summary>The place's cell world, or null for a 1.1 level.</summary>
    public static ICellWorld? For(Gpu gpu, Vitomir.Engine.World.Location location, Vitomir.Engine.World.Simulation sim, IClassicWorld world,
                                  Func<Vitomir.Engine.World.FreeCamera> camera) =>
        CellLevel.Of(location.Level) is { } cells
            ? new CellWorldHost(new CellWorldPass(gpu, cells)
            {
                Things = CellThingPass.Wanted ? new CellThingPass(gpu, ((ClassicFrameRenderer)world).World, location.Mesh, location.Materials) : null,
                Clock = () => sim.Seconds, Camera = camera,
            })
            : null;

    public IClassicWorldBelow Below => pass;
    public bool DrawsThings => pass.Things is not null;
    public int StillFrames => pass.StillFrames;

    public (long Loads, long Unloads, long TextureLoads, long TextureShrinks, long UploadedBytes, int ResidentCells) Stream
    {
        get
        {
            var stream = pass.Stream;
            var maps = stream?.Textures;
            return (stream?.Loads ?? 0, stream?.Unloads ?? 0, maps?.Loads ?? 0, maps?.Shrinks ?? 0,
                    (stream?.UploadedBytes ?? 0) + (maps?.UploadedBytes ?? 0), stream?.ResidentCells ?? 0);
        }
    }
}
