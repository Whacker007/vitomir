// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The three kinds the torches, candles, braziers and smoke are made of: gas,
// and fire and flame (both built on the gas).
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Particles;

/// <summary>
/// **Gas** (kind 1: smoke, mist, steam, a bubbling potion). A particle is born somewhere between
/// <c>mindist</c> and <c>maxdist</c> from the spawn point in a random direction and sets off
/// outwards along that same direction at between <c>minspeed</c> and <c>maxspeed</c>; it then falls
/// by <c>gravity</c> (rises, when negative), is slowed by <c>drag</c>, is pushed by
/// <c>turbulence</c>, **grows** by <c>growthrate</c> a second, and fades from <c>startcolor</c> to
/// <c>endcolor</c> over its life. Drawn at its size.
/// </summary>
public sealed class GasEmitter : Emitter
{
    /// <summary>The drag scale for gas: velocity × (1 − dt × drag × 0.001) a frame.</summary>
    public const float DragScale = -0.001f;

    public GasEmitter(EmitterSettings settings, ParticleSystem system) : base(settings, system) =>
        Particles = new Particle[settings.MaxParticles];

    /// <summary>
    /// The births due by now, each in the engine's order of draws: its spawn point (and
    /// the shape's own draws), two bits of the shared generator, then <c>rand()</c> for the size, the
    /// distance, the direction (three or more), the speed and the extra life. A birth that fell
    /// <c>late</c> milliseconds ago is aged by that much on the spot — moved along its velocity and
    /// its life shortened — so a slow frame does not bunch the particles up.
    /// </summary>
    void Spawn()
    {
        uint now = Now;
        if (!Due(now)) { return; }
        var s = Settings;
        var rng = System.Rand;
        do
        {
            uint late = now - NextBirth;
            float t = late * 0.001f;
            var point = BackDated(EmitPoint(), late, now);
            ref var p = ref Particles[NextSlot];
            p.Frame = (int)(System.Lcg.Step() & 3);
            p.Size = rng.Unit() * s.SizeVariance * s.StartSize + s.StartSize;
            float dist = (s.MaxDist - s.MinDist) * rng.Unit() + s.MinDist;
            var dir = Physics.Direction(rng);
            p.Position = dir * dist + point;
            float speed = (s.MaxSpeed - s.MinSpeed) * rng.Unit() + s.MinSpeed;
            p.Velocity = speed * dir;
            p.Position += t * p.Velocity;
            uint extra = (uint)(int)(rng.Unit() * s.LifeJitter);
            p.LifeRate = 1f / ((extra + s.Lifespan) * 0.001f);
            p.Life = 1f - p.LifeRate * t;
            Advance();
        } while (NextBirth < now);
    }

    /// <summary>The update: the births, then every live particle.</summary>
    public override bool Update(float dt)
    {
        var s = Settings;
        float growth = dt * s.GrowthRate;
        Spawn();
        var push = Push(s, dt, System);
        bool alive = false;
        int n = Active;
        float k = dt * s.Drag * DragScale;
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Particles[i];
            if (!(p.Life > 0f)) continue;
            alive = true;
            var v = p.Velocity + push;
            if (s.Drag != 0f) v = v * k + v;
            p.Velocity = v;
            p.Position += v * dt;
            float size = growth + p.Size;
            p.Size = size < 0f ? 0f : size;
            p.Life -= dt * p.LifeRate;
        }
        return !Dying || alive;
    }

    /// <summary>
    /// This frame's push, the same for every particle: gravity down the world's Z, and one random
    /// direction scaled by <c>turbulence</c> added to it (the gas and fire updates; the flame's
    /// differs, see <see cref="FlameEmitter"/>).
    /// </summary>
    internal static Vector3 Push(EmitterSettings s, float dt, ParticleSystem system)
    {
        var push = Vector3.Zero;
        if (s.Gravity != 0f) push = new Vector3(0f, 0f, -s.Gravity * dt);
        if (s.Turbulence != 0f) push += dt * s.Turbulence * Physics.Direction(system.Rand);
        return push;
    }

    /// <summary>The draw: at its size, in its colour.</summary>
    public override void Collect(List<ParticleSprite> into)
    {
        int n = Active;
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Particles[i];
            if (p.Life > 0f)
                into.Add(new ParticleSprite(p.Position, Blend(Settings.StartColor, Settings.EndColor, p.Life), p.Size));
        }
    }
}

/// <summary>
/// **Fire** (kind 2: every torch, candle and brazier flame). Born like a gas particle, but each one
/// remembers the X and Y it was born at and is **pulled back towards that column** every frame
/// (its sideways velocity loses <c>(position − home) × dt</c>), so the flames lick upwards rather
/// than spreading. Rises by default (gravity −80), heavily damped (drag 200). It does not grow;
/// it is drawn at its size **times its remaining life**, so a flame shrinks to a point as it dies.
/// </summary>
public sealed class FireEmitter : Emitter
{
    /// <summary>The drag scale for fire and flame: velocity × (1 − dt × drag × 0.01) a frame.</summary>
    public const float DragScale = -0.01f;

    public FireEmitter(EmitterSettings settings, ParticleSystem system) : base(settings, system) =>
        Particles = new Particle[settings.MaxParticles];

    /// <summary>
    /// The births due by now. The order of draws differs from the gas: the extra life
    /// and the distance come from the **shared generator** rather than <c>rand()</c>, and the
    /// distance is drawn before the direction.
    /// </summary>
    void Spawn()
    {
        uint now = Now;
        if (!Due(now)) return;
        var s = Settings;
        var rng = System.Rand;
        do
        {
            uint late = now - NextBirth;
            float t = late * 0.001f;
            var point = BackDated(EmitPoint(), late, now);
            ref var p = ref Particles[NextSlot];
            p.Frame = (int)(System.Lcg.Step() & 3);
            p.Life = 1f;
            p.Size = rng.Unit() * s.SizeVariance * s.StartSize + s.StartSize;
            uint life = s.Lifespan;
            if (s.LifeJitter != 0) life += System.Lcg.Step() % s.LifeJitter;
            p.LifeRate = 1f / (life * 0.001f);
            p.Life = 1f - p.LifeRate * t;
            float dist = System.Lcg.Unit() * (s.MaxDist - s.MinDist) + s.MinDist;
            p.Home = new Vector2(point.X, point.Y);
            var dir = Physics.Direction(rng);
            p.Position = dir * dist + point;
            float speed = (s.MaxSpeed - s.MinSpeed) * rng.Unit() + s.MinSpeed;
            p.Velocity = speed * dir;
            p.Position += t * p.Velocity;
            Advance();
        } while (NextBirth < now);
    }

    /// <summary>
    /// The update: births only while the thing is in the world, then every
    /// live particle pushed, damped, moved and **pulled home** — the pull uses the position it has
    /// just moved to, and leaves the vertical velocity alone.
    /// </summary>
    public override bool Update(float dt)
    {
        var s = Settings;
        if (Thing is { InWorld: true }) Spawn();
        var push = GasEmitter.Push(s, dt, System);
        bool alive = false;
        int n = Active;
        float k = dt * s.Drag * DragScale;
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Particles[i];
            if (!(p.Life > 0f)) continue;
            alive = true;
            var v = p.Velocity + push;
            if (s.Drag != 0f) v = v * k + v;
            p.Position += v * dt;
            p.Velocity = new Vector3(v.X - (p.Position.X - p.Home.X) * dt, v.Y - (p.Position.Y - p.Home.Y) * dt, v.Z);
            p.Life -= dt * p.LifeRate;
        }
        return !Dying || alive;
    }

    /// <summary>The draw: at its size times its life.</summary>
    public override void Collect(List<ParticleSprite> into)
    {
        int n = Active;
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Particles[i];
            if (p.Life > 0f)
                into.Add(new ParticleSprite(p.Position, Blend(Settings.StartColor, Settings.EndColor, p.Life), p.Size * p.Life));
        }
    }
}

/// <summary>
/// **Flame** (kind 4: the flamethrower, the flame trap, a steam jet). Every particle is launched at
/// the block's <c>velocity</c>, turned into the thing's frame, with each component scaled by a
/// random 0.75–1.25; it then falls, is damped, and **grows from <c>startsize</c> to
/// <c>endsize</c>** as it dies. No extra life (the flame constructor zeroes it).
/// </summary>
public sealed class FlameEmitter : Emitter
{
    public FlameEmitter(EmitterSettings settings, ParticleSystem system) : base(settings, system) =>
        Particles = new Particle[settings.MaxParticles];

    /// <summary>
    /// The births due by now. A size is drawn and stored though the draw never uses it —
    /// the <c>rand()</c> is spent all the same.
    /// </summary>
    void Spawn()
    {
        uint now = Now;
        if (!Due(now)) return;
        var s = Settings;
        var rng = System.Rand;
        do
        {
            uint late = now - NextBirth;
            float t = late * 0.001f;
            var point = BackDated(EmitPoint(), late, now);
            ref var p = ref Particles[NextSlot];
            p.Frame = (int)(System.Lcg.Step() & 3);
            p.Life = 1f;
            p.Size = rng.Unit() * s.SizeVariance * s.StartSize + s.StartSize;
            uint life = s.Lifespan;
            if (s.LifeJitter != 0) life += System.Lcg.Step() % s.LifeJitter;
            p.LifeRate = 1f / (life * 0.001f);
            p.Life = 1f - p.LifeRate * t;
            p.Position = point;
            // The velocity turned by the frame, z first as the engine adds them.
            var v = s.Velocity;
            p.Velocity = new Vector3(AxisZ.X * v.Z + AxisY.X * v.Y + AxisX.X * v.X,
                                     AxisZ.Y * v.Z + AxisY.Y * v.Y + AxisX.Y * v.X,
                                     AxisZ.Z * v.Z + AxisY.Z * v.Y + AxisX.Z * v.X);
            float lowest = 1f - s.VelocityVariance * 0.5f;
            float vx = (rng.Unit() * s.VelocityVariance + lowest) * p.Velocity.X;
            float vy = (rng.Unit() * s.VelocityVariance + lowest) * p.Velocity.Y;
            float vz = (rng.Unit() * s.VelocityVariance + lowest) * p.Velocity.Z;
            p.Velocity = new Vector3(vx, vy, vz);
            p.Position += p.Velocity * t;
            Advance();
        } while (NextBirth < now);
    }

    /// <summary>
    /// The update. The push differs from gas and fire in two ways that are the engine's:
    /// turbulence **replaces** gravity rather than adding to it, and with neither the push is never
    /// written (whatever the stack held; ours: nothing). No shipped flame has neither.
    /// </summary>
    public override bool Update(float dt)
    {
        var s = Settings;
        if (Thing is { InWorld: true }) Spawn();
        var push = Vector3.Zero;
        if (s.Gravity != 0f) push = new Vector3(0f, 0f, -s.Gravity * dt);
        if (s.Turbulence != 0f) push = dt * s.Turbulence * Physics.Direction(System.Rand);
        bool alive = false;
        int n = Active;
        float k = dt * s.Drag * FireEmitter.DragScale;
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Particles[i];
            if (!(p.Life > 0f)) continue;
            alive = true;
            var v = p.Velocity + push;
            if (s.Drag != 0f) v = v * k + v;
            p.Velocity = v;
            p.Position += v * dt;
            p.Life -= dt * p.LifeRate;
        }
        return !Dying || alive;
    }

    /// <summary>The draw: from the block's end size towards its start size by its life.</summary>
    public override void Collect(List<ParticleSprite> into)
    {
        int n = Active;
        var s = Settings;
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Particles[i];
            if (p.Life > 0f)
                into.Add(new ParticleSprite(p.Position, Blend(s.StartColor, s.EndColor, p.Life),
                                            (s.StartSize - s.EndSize) * p.Life + s.EndSize));
        }
    }
}
