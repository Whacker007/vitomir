// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.World;

/// <summary>
/// **A walker with no ground under it still walks.** The engine's walker update works
/// out the next point along the leg, asks the thing test and, when
/// nothing is in the way, hands the point to `simThing::SetPosition` — which writes the
/// position **first and unconditionally**, and only then looks for a surface: the one it had while
/// the point is over it, else the ground ray, which, finding nothing, leaves
/// the surface as it was. A walker that has none (none cached — a thing made at run time,
/// <see cref="WorldThing.Unsurfaced"/>) therefore goes on having none and goes on moving, straight
/// along its leg at the leg's own height (the position set takes the height off a surface only when there
/// is one), until a step lands over ground the ray finds.
///
/// Vitomir kept a route step only when it came back with a piece of ground, so a surfaceless walker
/// whose first step was over nothing never moved at all. That is the silver mines' rats, seen
/// in play: a <c>Generator</c> prop with no keyframes makes its <c>rat</c>
/// exactly where the prop stands (`SpawnThing`), and the mines' generators stand inside
/// the rat holes, off every piece of the walk map with no floor beneath them. The rats saw
/// Christof, were ordered to him at 250, played `ratrun` — and stood in their holes until he came
/// to them. 1.1 writes those steps, so they run out of the hole.
///
/// The keyboard's step (<see cref="WalkMap.Move"/>, ours) is not a route step and keeps to the
/// ground as before; a step a thing refused is not taken either way. The straight leg itself, and what
/// <c>spawnwalls</c> (quality of life) does once such a walker finds its feet, is <see cref="Player"/>'s (<see cref="Qol.SpawnWalls"/>).
/// </summary>
public static class SurfacelessStep
{
    /// <summary>
    /// Whether a step that found no ground is written anyway: a route step (<see
    /// cref="WalkMap.Stride"/>, the engine's) that no thing refused.
    /// </summary>
    public static bool Taken(bool onRoute, WalkMap.Step step) => onRoute && !step.Blocked;
}
