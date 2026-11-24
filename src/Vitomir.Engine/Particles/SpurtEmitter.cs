// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The spurt kind: blood from a neck or a severed head, water from a spout.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Particles;

/// <summary>
/// **Spurt** (kind 13: <c>blood0</c> on a beheaded body's neck, <c>headBlood</c> on every severed
/// head, <c>neckBlood</c>, the water spouts and drips, <c>moltenSplash</c>). A spurt has
/// <c>numgroups</c> **jets**. Each jet is a point on the emitter's shape and a direction —
/// <c>velocity</c> plus a random unit vector × <c>velocityvar</c>, made a unit vector — both
/// **kept in the thing's own frame**, so the jet rides the thing: a head that tumbles sprays the
/// way it now faces. A jet keeps its aim for <c>mingroup</c>..<c>maxgroup</c> milliseconds and is
/// then aimed afresh. Every <c>minbirthrate</c>..<c>maxbirthrate</c> milliseconds it sheds a drop:
/// at the jet's point, along the jet's direction at <c>minspeed</c>..<c>maxspeed</c>, of a size
/// between <c>minstartsize</c> and <c>maxstartsize</c>. A drop falls by <c>gravity</c> — no drag,
/// no turbulence — and is drawn in <c>startcolor</c> at its size, unchanged, until <c>lifespan</c>
/// milliseconds after it fell due, and then not at all.
///
/// Read as written, <c>blood0</c>'s numbers would shed one drop every half second per jet; an
/// unsigned comparison in the births (see <see cref="Shed"/>) makes it a lifespan's worth nearly
/// every frame instead, which is the stream the game shows.
/// </summary>
public sealed class SpurtEmitter : Emitter
{
    /// <summary>One jet (0x24 bytes each).</summary>
    public struct Group
    {
        /// <summary>+0: where it sheds from, in the thing's frame.</summary>
        public Vector3 Position;
        /// <summary>Which way, a unit vector in the thing's frame.</summary>
        public Vector3 Direction;
        /// <summary>When its next drop is due.</summary>
        public uint NextBirth;
        /// <summary>What its drops' speed is scaled by — always 1.</summary>
        public float Speed;
        /// <summary>When it is aimed afresh.</summary>
        public uint End;
    }

    /// <summary>One drop (0x20 bytes each).</summary>
    public struct Drop
    {
        public Vector3 Position;
        public Vector3 Velocity;
        /// <summary>The clock it dies at; drawn while now is before it.</summary>
        public uint Death;
        /// <summary>Half the width of its square.</summary>
        public float Size;
    }

    /// <summary>The jets, made at the first update (null until then, as in the original).</summary>
    public Group[]? Groups { get; private set; }
    public Drop[] Drops { get; }

    public SpurtEmitter(EmitterSettings settings, ParticleSystem system) : base(settings, system) =>
        Drops = new Drop[settings.MaxParticles];

    int ActiveDrops => Math.Min(Drops.Length, (int)(Settings.MaxParticles * ParticleSystem.Lod));

    /// <summary>
    /// Aims jet <paramref name="i"/>: a point from the emitter's shape turned back into
    /// the thing's frame (its rigid inverse), a direction <c>velocity + unit × velocityvar</c>
    /// made a unit vector, its clock at now and its end <c>rand × maxgroup</c>
    /// on for the first aim, <c>mingroup + rand × (maxgroup − mingroup)</c> after.
    /// </summary>
    void Aim(int i, bool first)
    {
        var s = Settings;
        var rng = System.Rand;
        ref var g = ref Groups![i];
        var world = EmitPoint();
        var d = world - Origin;
        g.Position = new Vector3(Vector3.Dot(AxisX, d), Vector3.Dot(AxisY, d), Vector3.Dot(AxisZ, d));
        var unit = Physics.Direction(rng);
        var dir = s.VelocityVariance * unit + s.Velocity;
        float length = dir.Length();
        if (length != 0f) dir *= 1f / length;
        g.Direction = dir;
        uint now = Now;
        float r = rng.Unit();
        g.End = first ? (uint)(int)(r * s.MaxGroup) + now
                      : (uint)(int)(r * (s.MaxGroup - s.MinGroup) + s.MinGroup) + now;
        g.Speed = 1f;
        g.NextBirth = now;
    }

    /// <summary>
    /// The drops due by now, jet by jet, only while not dying and with a thing. A jet
    /// whose time is up is aimed afresh first (and so sheds nothing this frame: its clock is now). A
    /// backlog older than a lifespan is forgotten — and the test is unsigned, so a jet whose next drop
    /// is still **in the future** reads as having a huge backlog and is put back to
    /// <c>now − lifespan</c>: nearly every frame each jet sheds a lifespan's worth of drops at once
    /// (<c>blood0</c>: six), all from the same point along the same line at their different speeds.
    /// That is what makes a spurt a stream rather than a drop every half second, and it is 1.1's
    /// arithmetic (an unsigned subtraction and compare, as the original does), kept. Each drop: its death <c>lifespan</c> after it fell
    /// due, its size (<c>rand()</c>), its velocity — the jet's direction turned by the thing's
    /// **current** frame × a speed (<c>rand()</c>) — and its place, the jet's point in
    /// the current frame; the next is <c>trunc(min + rand × (max − min))</c> milliseconds on. **A late
    /// drop is not moved on for its lateness** (unlike gas and sparks): a slow frame's drops start
    /// together.
    /// </summary>
    void Shed()
    {
        uint now = Now;
        if (Dying || Thing is null) return;
        var s = Settings;
        var rng = System.Rand;
        var groups = Groups!;
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i].End <= now) Aim(i, first: false);
            ref var g = ref groups[i];
            uint next = g.NextBirth;
            if (now - next > s.Lifespan) next = now - s.Lifespan;
            while (next < now)
            {
                ref var p = ref Drops[NextSlot];
                NextSlot++;
                if (ActiveDrops <= NextSlot) NextSlot = 0;
                p.Death = s.Lifespan + next;
                p.Size = rng.Unit() * (s.MaxStartSize - s.MinStartSize) + s.MinStartSize;
                var dir = g.Direction;
                var v = new Vector3(AxisZ.X * dir.Z + AxisY.X * dir.Y + AxisX.X * dir.X,
                                    AxisZ.Y * dir.Z + AxisY.Y * dir.Y + AxisX.Y * dir.X,
                                    AxisZ.Z * dir.Z + AxisY.Z * dir.Y + AxisX.Z * dir.X);
                float speed = (rng.Unit() * (s.MaxSpeed - s.MinSpeed) + s.MinSpeed) * g.Speed;
                p.Velocity = v * speed;
                var at = g.Position;
                p.Position = new Vector3(AxisZ.X * at.Z + AxisY.X * at.Y + at.X * AxisX.X + Origin.X,
                                         AxisY.Y * at.Y + AxisZ.Y * at.Z + AxisX.Y * at.X + Origin.Y,
                                         AxisY.Z * at.Y + AxisZ.Z * at.Z + AxisX.Z * at.X + Origin.Z);
                uint gap = (uint)(int)(rng.Unit() * (s.MaxBirthGap - s.MinBirthGap) + s.MinBirthGap);
                next += gap;
                // Ours: a gap of 0 never ends the loop in 1.1 either; no shipped block has one.
                if (gap == 0) break;
            }
            g.NextBirth = next;
        }
        NextBirth = groups.Length > 0 ? groups[^1].NextBirth : NextBirth;
    }

    /// <summary>
    /// The update: the jets made and aimed the first time (every one a first aim), the
    /// drops due, then every drop still alive falls by <c>gravity</c> and moves — the velocity first,
    /// then the position with it. Finished only when dying with nothing alive.
    /// </summary>
    public override bool Update(float dt)
    {
        if (Groups is null)
        {
            Groups = new Group[Settings.NumGroups];
            for (int i = 0; i < Groups.Length; i++) Aim(i, first: true);
        }
        Shed();
        uint now = Now;
        float fall = -(dt * Settings.Gravity);
        bool alive = false;
        int n = ActiveDrops;
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Drops[i];
            if (!(now < p.Death)) continue;
            alive = true;
            p.Velocity = new Vector3(p.Velocity.X + 0f, p.Velocity.Y + 0f, fall + p.Velocity.Z);
            p.Position += p.Velocity * dt;
        }
        return !Dying || alive;
    }

    /// <summary>The draw: every drop still alive, in <c>startcolor</c>, at its size.</summary>
    public override void Collect(List<ParticleSprite> into)
    {
        uint now = Now;
        int n = ActiveDrops;
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Drops[i];
            if (now < p.Death) into.Add(new ParticleSprite(p.Position, Settings.StartColor, p.Size));
        }
    }
}
