// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The sparks kind: bursts of sparks, a spray of molten metal or of blood.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Particles;

/// <summary>
/// **Sparks** (kind 7: <c>sparks</c>, <c>blueSparks</c>, <c>firespray</c>, <c>moltenSpray</c>,
/// <c>bloodspray</c>). The emitter fires **bursts**: every <c>minbirthrate</c> to
/// <c>maxbirthrate</c> milliseconds, 1 to <c>count + 1</c> sparks at once from the same point, each
/// flung at <c>velocity</c> plus a random direction scaled by up to <c>velocityvar</c>, falling by
/// <c>gravity</c>, with no drag and no turbulence. A spark keeps its colour — one draw between
/// <c>startcolormin</c> and <c>startcolormax</c> — and its size (<c>minstartsize</c> to
/// <c>maxstartsize</c>) for all of its life and simply stops being drawn when its time
/// (<c>lifespan</c> ms after its burst) is up: no fade.
/// </summary>
public sealed class SparksEmitter : Emitter
{
    /// <summary>One spark (0x24 bytes each).</summary>
    public struct Spark
    {
        public Vector3 Position;
        public Vector3 Velocity;
        /// <summary>ARGB, fixed at birth.</summary>
        public uint Color;
        /// <summary>The clock it dies at; drawn while now ≤ this.</summary>
        public uint Death;
        public float Size;
    }

    public Spark[] Sparks { get; }

    public SparksEmitter(EmitterSettings settings, ParticleSystem system) : base(settings, system) =>
        Sparks = new Spark[settings.MaxParticles];

    /// <summary>
    /// The bursts due by now. Per burst: the spawn point (moved back along the thing's
    /// movement, as every kind's is), one <c>rand()</c> for its size; then per spark <c>rand()</c>
    /// for the size, the spread, the direction and the colour, the spark aged by how late the
    /// burst is; then the next burst <c>min + LCG % (max − min)</c> milliseconds on.
    /// </summary>
    void Burst()
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
            int n = (int)(rng.Unit() * s.Count) + 1;
            for (; n > 0; n--)
            {
                ref var p = ref Sparks[NextSlot];
                p.Death = s.Lifespan + now;
                p.Size = rng.Unit() * (s.MaxStartSize - s.MinStartSize) + s.MinStartSize;
                p.Position = point;
                float spread = rng.Unit() * s.VelocityVariance;
                var dir = Physics.Direction(rng);
                p.Velocity = dir * spread + s.Velocity;
                p.Color = Blend(s.StartColorMax, s.StartColorMin, rng.Unit());
                p.Position += t * p.Velocity;
                NextSlot++;
                if ((int)(s.MaxParticles * ParticleSystem.Lod) <= NextSlot) NextSlot = 0;
            }
            // Ours: equal rates divide by zero in 1.1; no shipped block has them.
            uint range = s.MaxBirthRate - s.MinBirthRate;
            NextBirth += (range == 0 ? 0 : System.Lcg.Step() % range) + s.MinBirthRate;
            if (range == 0 && s.MinBirthRate == 0) break;
        } while (NextBirth < now);
    }

    /// <summary>The update: the bursts, then every spark still alive falls and moves.</summary>
    public override bool Update(float dt)
    {
        Burst();
        var push = new Vector3(0f, 0f, -(dt * Settings.Gravity));
        uint now = Now;
        bool alive = false;
        int n = Math.Min(Sparks.Length, (int)(Settings.MaxParticles * ParticleSystem.Lod));
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Sparks[i];
            if (now > p.Death) continue;
            alive = true;
            p.Velocity += push;
            p.Position += p.Velocity * dt;
        }
        return !Dying || alive;
    }

    /// <summary>The draw: every spark still alive, in its own colour and size.</summary>
    public override void Collect(List<ParticleSprite> into)
    {
        uint now = Now;
        int n = Math.Min(Sparks.Length, (int)(Settings.MaxParticles * ParticleSystem.Lod));
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Sparks[i];
            if (now <= p.Death) into.Add(new ParticleSprite(p.Position, p.Color, p.Size));
        }
    }
}
