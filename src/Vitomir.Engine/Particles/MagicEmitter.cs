// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The magic kind: the swirl of glints round a caster or a target.
using System.Numerics;

namespace Vitomir.Engine.Particles;

/// <summary>
/// **Magic** (kind 10: <c>redMagic</c>, <c>blueMagic</c>, <c>yellowMagic</c>, <c>blackMagic</c>,
/// the mystic's glows, the Haus de Hexe teleporter). Each particle is a point **circling a
/// centre**: the centre is born where the thing is and drifts by <c>velocity</c> (rising, in every
/// shipped block); the particle sits <c>radius</c> out from it (<c>minradius</c>..<c>maxradius</c>,
/// growing by <c>mingrowth</c>..<c>maxgrowth</c> a second) at an angle that starts anywhere round
/// the circle and turns at <c>minspeed</c>..<c>maxspeed</c> degrees a second; it is drawn from
/// <c>endsize</c> to <c>startsize</c> and from its end colour to its start colour by its life, and
/// lives <c>lifespan</c>. A particle's circle lies in the plane of the thing's X axis at its birth,
/// turned about the world's vertical — so the swirl is a rising, widening spiral.
/// </summary>
public sealed class MagicEmitter : Emitter
{
    /// <summary>One particle (0x44 bytes each).</summary>
    public struct Mote
    {
        /// <summary>+0: the centre it circles.</summary>
        public Vector3 Centre;
        /// <summary>The thing's X axis at its birth — the direction it is thrown out along, before the turn.</summary>
        public Vector3 Axis;
        /// <summary>Its turn, degrees (roll, pitch, yaw) — only the yaw is ever set.</summary>
        public Vector3 Angles;
        /// <summary>What the turn gains a second.</summary>
        public Vector3 Spin;
        /// <summary>How far out it is, and what that gains a second.</summary>
        public float Radius, Growth;
        /// <summary>Its colour at birth and at death.</summary>
        public uint StartColor, EndColor;
        /// <summary>1 at birth, drawn while above 0.</summary>
        public float Life;
    }

    public Mote[] Motes { get; }

    /// <summary>Made: the array, and the first birth due at once.</summary>
    public MagicEmitter(EmitterSettings settings, ParticleSystem system) : base(settings, system)
    {
        Motes = new Mote[settings.MaxParticles];
        NextBirth = system.Now;
    }

    int ActiveMotes => Math.Min(Motes.Length, (int)(Settings.MaxParticles * ParticleSystem.Lod));

    /// <summary>
    /// The births due: not while dying; a backlog older than a lifespan forgotten. Each:
    /// the centre where the thing is, the axis the thing's X, full life; then
    /// <c>rand()</c> for the radius, the starting yaw (× 360), the spin, the growth and the colours
    /// (one draw for both), the centre moved along <c>velocity</c> by how late it is, the slot
    /// round, and the next <c>trunc(min + rand × (max − min))</c> milliseconds on.
    /// </summary>
    void Spawn()
    {
        uint now = Now;
        if (Dying || NextBirth > now) return;
        var s = Settings;
        if (now - NextBirth > s.Lifespan) NextBirth = now - s.Lifespan;
        if (!(NextBirth < now)) return;
        var rng = System.Rand;
        do
        {
            ref var p = ref Motes[NextSlot];
            p.Centre = LastOrigin;
            p.Life = 1f;
            float late = (now - NextBirth) * 0.001f;
            p.Axis = AxisX;
            p.Radius = rng.Unit() * (s.MaxRadius - s.MinRadius) + s.MinRadius;
            p.Angles = new Vector3(0, 0, rng.Unit() * 360f);
            p.Spin = new Vector3(0, 0, rng.Unit() * (s.MaxSpeed - s.MinSpeed) + s.MinSpeed);
            p.Growth = rng.Unit() * (s.MaxGrowth - s.MinGrowth) + s.MinGrowth;
            float c = rng.Unit();
            p.StartColor = Blend(s.StartColorMax, s.StartColorMin, c);
            p.EndColor = Blend(s.EndColorMax, s.EndColorMin, c);
            p.Centre += late * s.Velocity;
            NextSlot++;
            if (ActiveMotes <= NextSlot) NextSlot = 0;
            uint gap = (uint)(int)(rng.Unit() * (s.MaxBirthGap - s.MinBirthGap) + s.MinBirthGap);
            NextBirth += gap;
            // Ours: a gap of 0 never ends the loop in 1.1 either; no shipped block has one.
            if (gap == 0) break;
        } while (NextBirth < now);
    }

    /// <summary>
    /// The update: the births, then every particle with life drifts by <c>velocity</c>,
    /// turns by its spin, widens by its growth and loses <c>dt × 1000 / lifespan</c> of life.
    /// </summary>
    public override bool Update(float dt)
    {
        Spawn();
        var s = Settings;
        float fade = 1000f / s.Lifespan;
        bool alive = false;
        int n = ActiveMotes;
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Motes[i];
            if (!(p.Life > 0f)) continue;
            alive = true;
            p.Centre += dt * s.Velocity;
            p.Angles += dt * p.Spin;
            p.Radius = dt * p.Growth + p.Radius;
            p.Life -= fade * dt;
        }
        return !Dying || alive;
    }

    /// <summary>
    /// The draw: each particle with life at its centre + radius × its axis turned by its
    /// angles, sized <c>endsize + (startsize − endsize) × life</c>, coloured from its end
    /// colour to its start colour by life.
    /// </summary>
    public override void Collect(List<ParticleSprite> into)
    {
        var s = Settings;
        int n = ActiveMotes;
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Motes[i];
            if (!(p.Life > 0f)) continue;
            var turn = RingBase.Turn(p.Angles, Vector3.Zero);
            var out_ = Vector3.TransformNormal(p.Axis, turn);
            into.Add(new ParticleSprite(p.Radius * out_ + p.Centre, Blend(p.StartColor, p.EndColor, p.Life),
                                        (s.StartSize - s.EndSize) * p.Life + s.EndSize));
        }
    }
}
