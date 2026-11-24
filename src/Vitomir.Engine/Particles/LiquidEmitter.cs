// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The liquid kind: a spray thrown out along a thing's Y axis in clumps.
using System.Numerics;

namespace Vitomir.Engine.Particles;

/// <summary>
/// **Liquid** (kind 3: <c>liquid</c>, <c>waterspray</c>, <c>blood0old</c>; the first two are
/// templates nothing in the shipped scenes or scripts makes, the third has no template). A liquid
/// has <c>numgroups</c> **clumps**, each thrown along the thing's Y axis mixed with a random
/// direction (weighted <c>1 − cohesion</c>), from an offset <c>mindist</c>..<c>maxdist</c> out along
/// that direction, at <c>minspeed</c>..<c>maxspeed</c>, holding <c>mingroup</c>..<c>maxgroup</c>
/// drops. The births walk round the clumps; each clump, when its turn comes, sheds its next drop
/// with a chance of <c>groupcohesion</c>, and is thrown afresh once it is empty. A drop falls by
/// <c>gravity</c> and is drawn in <c>startcolor</c> at its size until it dies — no fade, and
/// <c>endcolor</c> is read and never used.
///
/// The random numbers are the shared generator's, stepped in the engine's order, several of them
/// read straight from its mantissa bits; **the clump the walk is at is one global for every liquid
/// emitter**. Ours keeps it on the system and folds it into this emitter's
/// clumps when another has left it past the end (1.1 would read past the array).
/// </summary>
public sealed class LiquidEmitter : Emitter
{
    /// <summary>One clump (0x28 bytes each).</summary>
    public struct Clump
    {
        public Vector3 Offset;
        public Vector3 Direction;
        public Vector3 Velocity;
        /// <summary>Drops left in it.</summary>
        public int Left;
    }

    /// <summary>One drop (0x34 bytes each).</summary>
    public struct Drop
    {
        public int Frame;
        public Vector3 Velocity;
        public Vector3 Position;
        public float Size;
        public float Life;
        /// <summary>The clock it dies at.</summary>
        public uint Death;
    }

    public Clump[]? Clumps { get; private set; }
    public Drop[] Drops { get; }

    public LiquidEmitter(EmitterSettings settings, ParticleSystem system) : base(settings, system) =>
        Drops = new Drop[settings.MaxParticles];

    int ActiveDrops => Math.Min(Drops.Length, (int)(Settings.MaxParticles * ParticleSystem.Lod));

    /// <summary>A shared-generator step as −1..1 from its mantissa.</summary>
    float Signed() { float u = System.Lcg.Unit(); return u + u - 1f; }

    /// <summary>
    /// Throws clump <paramref name="i"/>: the thing's Y axis (its matrix row 1, straight
    /// from the thing) made a unit vector; a random vector of three shared-generator draws made a
    /// unit vector; the direction <c>(1 − cohesion) × random + axis</c>, made a unit vector; the
    /// offset, each component <c>(mindist + unit × (maxdist − mindist)) × direction</c> with a draw
    /// of its own; a <c>rand()</c> speed; and <c>mingroup + trunc(unit × (maxgroup − mingroup))</c>
    /// drops, from one more step.
    /// </summary>
    void Throw(int i)
    {
        var s = Settings;
        ref var c = ref Clumps![i];
        var axis = Thing is { } t ? new Vector3(t.Rotation.M21, t.Rotation.M22, t.Rotation.M23) : AxisY;
        axis = Unit(axis);
        var random = Unit(new Vector3(Signed(), Signed(), Signed()));
        float keep = 1f - s.Cohesion;
        c.Direction = Unit(keep * random + axis);
        float spread = s.MaxDist - s.MinDist;
        c.Offset = new Vector3((System.Lcg.Unit() * spread + s.MinDist) * c.Direction.X,
                               (System.Lcg.Unit() * spread + s.MinDist) * c.Direction.Y,
                               (System.Lcg.Unit() * spread + s.MinDist) * c.Direction.Z);
        float speed = (s.MaxSpeed - s.MinSpeed) * System.Rand.Unit() + s.MinSpeed;
        float u = System.Lcg.Unit();
        c.Velocity = speed * c.Direction;
        c.Left = (int)(u * (s.MaxGroup - s.MinGroup)) + (int)s.MinGroup;
    }

    static Vector3 Unit(Vector3 v)
    {
        float l = v.Length();
        return l != 0f ? v * (1f / l) : v;
    }

    /// <summary>
    /// The births: only with a thing, not dying, and due; a backlog older than a lifespan
    /// forgotten. Then, round the clumps from the shared cursor: an empty clump is thrown afresh;
    /// two steps of the generator (one multiply, 0x17385ca9) give the chance, and under
    /// <c>groupcohesion</c> a drop — two bits of a step for its frame, the next step modulo 1000
    /// for extra life, a <c>rand()</c> for its size (the size spread is 0), the clump's velocity, the
    /// thing's position plus the clump's offset, its death <c>due + extra + lifespan</c>, and the
    /// next birth <c>trunc(birthrate / max(LOD, 0.1)) + step % 20</c> on — and the cursor moves on,
    /// whether or not a drop came.
    /// </summary>
    void Shed()
    {
        uint now = Now;
        var s = Settings;
        if (Dying || Thing is null || NextBirth > now) return;
        if (now - NextBirth > s.Lifespan) NextBirth = now - s.Lifespan;
        var clumps = Clumps!;
        if (clumps.Length == 0) return;
        ref int cursor = ref System.LiquidCursor;
        if (cursor >= clumps.Length) cursor %= clumps.Length;                  // ours, see the summary
        while (NextBirth < now)
        {
            if (clumps[cursor].Left == 0) Throw(cursor);
            System.Lcg.Step();
            if (System.Lcg.Unit() < s.GroupCohesion)
            {
                ref var c = ref clumps[cursor];
                ref var p = ref Drops[NextSlot];
                NextSlot++;
                if (ActiveDrops <= NextSlot) NextSlot = 0;
                p.Frame = (int)(System.Lcg.Step() & 3);
                uint extra = System.Lcg.Step() % 1000;
                p.Life = 1f;
                float r = System.Rand.Unit();
                p.Size = (r + r - 1f) * s.SizeVariance * s.StartSize + s.StartSize;
                p.Velocity = c.Velocity;
                p.Position = Thing.Position + c.Offset;
                p.Death = NextBirth + extra + s.Lifespan;
                float lod = ParticleSystem.Lod > 0.1f ? ParticleSystem.Lod : 0.1f;
                uint gap = (uint)(long)(s.BirthRate / lod);
                NextBirth = System.Lcg.Step() % 20 + NextBirth + gap;
                c.Left--;
            }
            cursor++;
            if (clumps.Length <= cursor) cursor = 0;
        }
    }

    /// <summary>
    /// The update: the clumps made and thrown the first time, the births, then every
    /// drop still alive falls by <c>gravity</c> and moves. Finished when dying with none alive.
    /// </summary>
    public override bool Update(float dt)
    {
        if (Clumps is null)
        {
            Clumps = new Clump[Settings.NumGroups];
            for (int i = 0; i < Clumps.Length; i++) Throw(i);
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
        return !(Dying && !alive);
    }

    /// <summary>The draw: every drop alive, in <c>startcolor</c>, at its size.</summary>
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
