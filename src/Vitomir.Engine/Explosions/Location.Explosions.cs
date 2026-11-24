// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The place's explosions and dynamic lights, kept beside the place's other per-place state.
namespace Vitomir.Engine.World;

public sealed partial class Location
{
    Vitomir.Engine.Explosions.ExplosionSystem? explosions;
    Vitomir.Engine.Explosions.DynamicLights? lights;
    int explosionsAt = int.MinValue;

    /// <summary>This place's explosions, made the first time anything asks.</summary>
    public Vitomir.Engine.Explosions.ExplosionSystem Explosions => explosions ??= new Vitomir.Engine.Explosions.ExplosionSystem(this);

    /// <summary>
    /// This place's dynamic lights: made the first time
    /// anything asks, which gives every thing already here whose template carries `DYNA_INTENSITY`
    /// its light.
    /// </summary>
    public Vitomir.Engine.Explosions.DynamicLights Lights => lights ??= new Vitomir.Engine.Explosions.DynamicLights(this);

    /// <summary>
    /// A thing entering the world that is an explosion begins (the constructor's clock, its script
    /// and flags, `WORLD ORIENT`). Called from <see cref="Enter"/>, after the finalize.
    /// </summary>
    void Entered(WorldThing thing)
    {
        if (thing.Type == ThingType.Explosion) Explosions.Begin(thing, Milliseconds);
    }

    /// <summary>The frame's length as the explosions saw it, for the ramps and the decals later in the frame.</summary>
    int explosionsFrame;

    /// <summary>
    /// The explosions' part of the frame before the world's walk: the frame's length as they see it
    /// and the clock their ticks read. Each explosion then ticks in its own thing's tick
    /// (<see cref="TickExplosion"/>) and the lights are put on their things
    /// after the walk (<see cref="EndExplosions"/>). The ramps come after the camera
    /// (<see cref="StepLightRamps"/>) and the intensities and blobs in the draw
    /// (<see cref="StepLightPass"/>). Called from <see cref="Simulation"/>'s walk (`World/ThingTick.cs`).
    /// </summary>
    public void BeginExplosions(int milliseconds)
    {
        explosionsFrame = explosionsAt == int.MinValue ? 0 : milliseconds - explosionsAt;
        explosionsAt = milliseconds;
        explosions?.Clock(milliseconds);
    }

    /// <summary>A `THING_TYPE_EXPLOSION` thing's own tick, with the combat generator.</summary>
    public void TickExplosion(WorldThing thing, int milliseconds, Ai.Rng damageRng) =>
        explosions?.TickThing(thing, milliseconds, damageRng);

    /// <summary>
    /// After the walk: the ended explosions let go, and every lit light put where its thing is.
    /// 1.1 puts each light on its thing in that thing's own tick, right after its move;
    /// Vitomir puts them all on once the walk is over (ours), which differs only for a
    /// thing moved by a later thing's tick in the same frame, and only in the draw.
    /// </summary>
    public void EndExplosions()
    {
        explosions?.Prune();
        Lights.PlaceAll();
    }

    /// <summary>The value ramps, stepped once a frame by the world tick after the camera.</summary>
    public void StepLightRamps(int milliseconds) => Lights.StepRamps(milliseconds, explosionsFrame);

    /// <summary>
    /// The world draw's dynamic-light pass, run by the
    /// simulation after the light styles have stepped (the light styles' step, the pass's first
    /// call): this frame's intensity for every light that is on, its blob, and the decals' fade
    /// (the pass's second loop).
    /// </summary>
    public void StepLightPass()
    {
        Lights.Light(Styles);
        StepDecals(explosionsFrame);
    }
}
