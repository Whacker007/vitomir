// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The rain kind: London's rain and drizzle, Vienna's snow.
using System.Numerics;

namespace Vitomir.Engine.Particles;

/// <summary>
/// **Rain** (kind 6: <c>rain</c>, <c>lightRain</c>, <c>heavyRain</c>, <c>drizzle</c>, <c>snow</c>).
/// Not a stream of births: a fixed crowd of <c>maxparticles</c> drops scattered once through a box
/// and then **kept in a box <c>startsize</c> across centred on the camera** — every frame each drop
/// falls by <c>velocity</c>, and one that leaves the box on any side comes back in on the other
/// (<c>fmod</c> by the box's side). So the weather goes wherever the camera goes, and the thing it
/// hangs on only decides whether it is there at all. With <c>turbulence</c> each drop also drifts
/// sideways, its own drift nudged at random every frame and dropped to nothing whenever it passes
/// <c>velocityvar</c>. Drawn in <c>startcolor</c> — as squares <c>width</c> in half-size when
/// <c>length</c> is 0 (the snow, the drizzle), otherwise as streaks <c>length</c> seconds of fall
/// long and <c>width</c> wide.
///
/// A rain emitter joins the live list the moment it is made and marks itself seen every
/// frame it updates, so it never drops out for being unseen. When its thing goes it thins out: a
/// drop fewer every <c>birthrate</c> milliseconds until none is left.
/// </summary>
public sealed class RainEmitter : Emitter
{
    /// <summary>One drop (0x14 bytes each): its place and its sideways drift.</summary>
    public struct Drop
    {
        public Vector3 Position;
        /// <summary>The drift along X and Y, units a second.</summary>
        public Vector2 Drift;
    }

    public Drop[] Drops { get; }

    /// <summary>
    /// Made: every drop scattered through a box twice <c>startsize</c> across
    /// **around the world's origin** — three <c>rand()</c> each, x, y, z — which the first update
    /// folds into the camera's box. The drift is not set: the array is <c>malloc</c>'d and only the
    /// positions written, so it starts as whatever the heap held (ours: 0; it is only read with
    /// turbulence, and a stray value past <c>velocityvar</c> is zeroed at the first frame).
    /// </summary>
    public RainEmitter(EmitterSettings settings, ParticleSystem system) : base(settings, system)
    {
        Drops = new Drop[settings.MaxParticles];
        var rng = system.Rand;
        float size = settings.StartSize;
        for (int i = 0; i < Drops.Length; i++)
        {
            float x = (rng.Unit() - Half) * size; x += x;
            float y = (rng.Unit() - Half) * size; y += y;
            float z = (rng.Unit() - Half) * size; z += z;
            Drops[i].Position = new Vector3(x, y, z);
        }
    }

    /// <summary>One half.</summary>
    const float Half = 0.5f;

    int ActiveDrops => Math.Min(Drops.Length, (int)(Settings.MaxParticles * ParticleSystem.Lod));

    /// <summary>
    /// The update:
    /// <list type="number">
    /// <item>the box: the camera's position ± half of
    /// <c>startsize</c>, the camera kept in the emitter's last-position field;</item>
    /// <item>unless dying, the frame is marked seen — the rain never drops out;</item>
    /// <item>with turbulence, each drop's drift gets <c>dt × turbulence</c> × two draws in −1..1
    /// (x then y), each component zeroed if its size is now past <c>velocityvar</c>, and the drop
    /// moves by it;</item>
    /// <item>dying: while drops remain and the next "birth" is past, one drop fewer and the next
    /// <c>birthrate / max(particleLOD, 0.1)</c> on — and with none left the emitter is
    /// finished;</item>
    /// <item>every drop falls by <c>velocity × dt</c>, and each coordinate outside the box comes back
    /// in: above it, <c>min + fmod(p − min, side)</c>; below, <c>max − fmod(max − p, side)</c>.</item>
    /// </list>
    /// </summary>
    public override bool Update(float dt)
    {
        var s = Settings;
        float side = s.StartSize;
        float t = dt * s.Turbulence;
        var fall = new Vector3(dt * s.Velocity.X, dt * s.Velocity.Y, dt * s.Velocity.Z);
        var eye = System.Eye;
        LastOrigin = eye;
        var max = new Vector3(side * Half + eye.X, side * Half + eye.Y, side * Half + eye.Z);
        var min = new Vector3(side * -Half + eye.X, side * -Half + eye.Y, side * -Half + eye.Z);
        int n = ActiveDrops;
        if (!Dying) SeenFrame = System.Frame;
        var rng = System.Rand;
        if (t != 0f)
            for (int i = 0; i < n; i++)
            {
                ref var p = ref Drops[i];
                float rx = rng.Unit(); rx = rx + rx - 1f;
                float ry = rng.Unit(); ry = ry + ry - 1f;
                float dx = rx * t + p.Drift.X, dy = ry * t + p.Drift.Y;
                p.Drift = new Vector2(dx, dy);
                if (s.VelocityVariance < MathF.Abs(dx)) p.Drift.X = 0f;
                if (s.VelocityVariance < MathF.Abs(dy)) p.Drift.Y = 0f;
                p.Position.X = dt * p.Drift.X + p.Position.X;
                p.Position.Y = dt * p.Drift.Y + p.Position.Y;
            }
        if (Dying)
        {
            uint now = Now;
            float lod = ParticleSystem.Lod > 0.1f ? ParticleSystem.Lod : 0.1f;
            while ((int)(s.MaxParticles * ParticleSystem.Lod) != 0 && NextBirth < now)
            {
                s.MaxParticles--;
                NextBirth += (uint)(long)(s.BirthRate / lod);
            }
            if (s.MaxParticles == 0) return false;
            n = Math.Min(Drops.Length, (int)(s.MaxParticles * ParticleSystem.Lod));
        }
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Drops[i];
            p.Position += fall;
            p.Position.X = Wrap(p.Position.X, min.X, max.X, side);
            p.Position.Y = Wrap(p.Position.Y, min.Y, max.Y, side);
            p.Position.Z = Wrap(p.Position.Z, min.Z, max.Z, side);
        }
        return true;
    }

    /// <summary>One coordinate folded back into the box (<c>fmod</c>).</summary>
    public static float Wrap(float p, float min, float max, float side)
    {
        if (!(p <= max)) return Fmod(p - min, side) + min;
        if (p < min) return max - Fmod(max - p, side);
        return p;
    }

    /// <summary>C's <c>fmod</c>: the remainder with the sign of the dividend.</summary>
    static float Fmod(float x, float y) => (float)((double)x % y);

    /// <summary>
    /// The draw: with no <c>length</c>, a square per drop in <c>startcolor</c> at
    /// <c>width</c> (the sprite entry, 500 at a time); otherwise a streak per drop from where it is to
    /// where it will be <c>length</c> seconds on (the renderer's rain entry, 1000 at a time).
    /// </summary>
    public override void Collect(List<ParticleSprite> into)
    {
        var s = Settings;
        if (s.Length != 0f) return;
        int n = ActiveDrops;
        for (int i = 0; i < n; i++) into.Add(new ParticleSprite(Drops[i].Position, s.StartColor, s.Width));
    }

    /// <inheritdoc cref="Collect"/>
    public override void CollectStreaks(List<ParticleStreak> into)
    {
        var s = Settings;
        if (s.Length == 0f) return;
        var ahead = new Vector3(s.Length * s.Velocity.X, s.Length * s.Velocity.Y, s.Length * s.Velocity.Z);
        int n = ActiveDrops;
        for (int i = 0; i < n; i++)
        {
            var p = Drops[i].Position;
            into.Add(new ParticleStreak(p, ahead + p, s.StartColor, s.Width));
        }
    }
}
