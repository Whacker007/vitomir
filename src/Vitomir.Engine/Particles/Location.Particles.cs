// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The place's particle system, kept beside the place's other per-place state.
namespace Vitomir.Engine.World;

public sealed partial class Location
{
    Vitomir.Engine.Particles.ParticleSystem? particles;

    /// <summary>
    /// This place's particles: made the first time anything asks, which gives
    /// every thing already here whose template names a `PARTICLENAME` its emitter. The simulation
    /// steps it and the renderer draws it; see <see cref="Vitomir.Engine.Particles.ParticleSystem"/> for the API.
    /// </summary>
    public Vitomir.Engine.Particles.ParticleSystem Particles => particles ??= new Vitomir.Engine.Particles.ParticleSystem(this);
}
