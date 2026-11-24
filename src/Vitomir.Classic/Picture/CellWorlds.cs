// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The levels generator's cell world under 1.1's things (ours; the cell-level code stays in, switched off, no player
// switch).
using Vitomir.Engine.World;

namespace Vitomir.Classic;

/// <summary>
/// **A cell level's world, as the module holds it** (ours): the drawer the host's renderer draws under the things
/// (<see cref="Below"/>), and what the run asks of it.
/// </summary>
public interface ICellWorld
{
    /// <summary>What the host's renderer draws before the things (the world's depth in the game's).</summary>
    IClassicWorldBelow Below { get; }
    /// <summary>The world draws 1.1's characters itself (lit by its sun), so the game's shadows are not cast.</summary>
    bool DrawsThings { get; }
    /// <summary>How many frames of its one view a still takes (the screen's reflections read the frame before).</summary>
    int StillFrames { get; }
    /// <summary>Its streaming's totals for the frame log (cells and textures in and out, bytes sent up, cells resident).</summary>
    (long Loads, long Unloads, long TextureLoads, long TextureShrinks, long UploadedBytes, int ResidentCells) Stream { get; }
}

/// <summary>
/// **Where a cell level's world comes from** (ours): nowhere, unless the program that hosts the module says so.
/// <c>vitomir-tools</c> sets <see cref="Make"/> when its GPU opens (its tools, <c>--level-l2</c> and <c>--cook-l2</c>, are the only way
/// into a cell level, and its old modern path draws one); the platform's host never does, so under <c>vitomir-platform --game
/// classic</c> the cell code is in (the engine's, in the module's <c>bin\</c>) and switched off: no player's switch reaches it and no
/// cell world is drawn. <c>ControllerBoundaryTests</c> keeps it so.
/// </summary>
public static class CellWorlds
{
    /// <summary>A place's cell world (the place, its simulation, the host's world it is drawn under, the camera the frame is drawn from), or null for a 1.1 level.</summary>
    public static Func<Location, Simulation, IClassicWorld, Func<FreeCamera>, ICellWorld?>? Make { get; set; }
}
