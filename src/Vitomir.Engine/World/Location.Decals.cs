// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The place's decals, kept beside its explosions and lights.
namespace Vitomir.Engine.World;

public sealed partial class Location
{
    Vitomir.Engine.Explosions.Decals? decals;

    /// <summary>
    /// This place's decals — explosions' wall marks and projectiles' impact marks — made
    /// the first time anything asks, with the maxed-out preset's 500 slots (<see cref="Explosions.Decals.Rendered"/>).
    /// </summary>
    public Vitomir.Engine.Explosions.Decals Decals => decals ??= new Vitomir.Engine.Explosions.Decals(this);

    /// <summary>The fade of the effects' tick's second loop, once a frame, when there are any.</summary>
    void StepDecals(int frameMs) => decals?.Step(frameMs * 0.001f);
}
