// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The beam kind: lasers, lightning, the sewers' electric arcs.
using System.Numerics;

namespace Vitomir.Engine.Particles;

/// <summary>
/// **Beam** (kind 9: <c>blueLightning</c>, <c>redLaser</c>, <c>blueElecArc</c>, <c>smokeBeam</c>,
/// <c>bloodStream</c>, …). A beam runs **from its thing to the thing's frame 1** — the second of
/// the keyframes a level designer gave it, or that a script set with <c>AllocateFrames(2)</c> and
/// <c>SetFramePosition(1, …)</c> (the New York sewers' electric plates) — cut into segments of
/// <c>size</c>, every <c>updaterate</c> milliseconds given a new wobble across it (noise, or with
/// <c>subtype sinus</c> a sine wave that is largest mid-way and nothing at the ends) and a new turn
/// about its own length, and drawn as a ribbon <c>startsize</c>..<c>endsize</c> wide **turned to
/// face the camera**, its texture scrolling along it at <c>texturerate</c> units a second.
/// <c>birthrate</c>, <c>lifespan</c> and <c>count</c> say how often a fresh beam is struck, how long
/// each lasts and how many there are (0: no end); <c>delay</c> holds the first back.
///
/// With **no thing** the update answers 0 at once, so a beam whose thing is destroyed
/// goes with it rather than fading.
/// </summary>
public sealed class BeamEmitter : Emitter
{
    /// <summary>One beam (0x40 bytes each).</summary>
    public struct Bolt
    {
        /// <summary>Its width, set from its life at each new wobble.</summary>
        public float Size;
        /// <summary>1 when struck, down to 0 over the lifespan.</summary>
        public float Life;
        /// <summary>How many points along it (at least 2).</summary>
        public int Points;
        /// <summary>Each point's wobble across it.</summary>
        public float[] Offsets;
        /// <summary>How far its texture has scrolled.</summary>
        public float TexOffset;
        /// <summary>Its turn about its own length, degrees.</summary>
        public float Roll;
        /// <summary>Its length when last drawn.</summary>
        public float Length;
        /// <summary>When it is next given a new wobble.</summary>
        public uint NextUpdate;
        /// <summary>The slot has been used.</summary>
        public bool Struck;
    }

    public Bolt[] Bolts { get; }
    /// <summary>How many more beams may be struck, when <c>count</c> is not 0.</summary>
    public int Remaining { get; set; }

    /// <summary>
    /// The texture's height in texels (the material's first layer's bitmap):
    /// what the scroll wraps by and a unit along the beam is measured against. The system sets it
    /// from the material; ours: 1 when there is none.
    /// </summary>
    public int TextureHeight { get; set; } = 1;

    /// <summary>Made: the slots, the count left, and the first birth <c>delay</c> after now.</summary>
    public BeamEmitter(EmitterSettings settings, ParticleSystem system) : base(settings, system)
    {
        Bolts = new Bolt[settings.MaxParticles];
        Remaining = settings.Count;
        NextBirth = settings.Delay + system.Now;
    }

    /// <summary>Where the beam ends: the thing's frame 1. Ours: no beam with fewer than two frames — the engine reads past a one-frame array.</summary>
    Vector3? End => Thing is { Keyframes.Length: >= 2 } t ? t.Keyframes[1].Position : null;

    /// <summary>
    /// A beam struck — **one** a call, however many are due: only with a thing that has
    /// frames, not dying, due, and with count left. A backlog older than a lifespan is forgotten.
    /// The slot's width is <c>startsize</c>, its life 1, its next wobble at once; its length to the
    /// end, and — only when that is more than 0 — <c>max(2, trunc(ceil(length / size)))</c> points,
    /// the count down one, the next beam <c>birthrate</c> after now, and the slot round.
    /// </summary>
    void Strike()
    {
        uint now = Now;
        var s = Settings;
        if (Thing is null || End is not { } end || Dying || NextBirth > now) return;
        if (s.Count != 0 && Remaining == 0) return;
        if (now - NextBirth > s.Lifespan) NextBirth = now - s.Lifespan;
        if (!(NextBirth < now)) return;
        ref var b = ref Bolts[NextSlot];
        b.Size = s.StartSize;
        b.TexOffset = 0f;
        b.NextUpdate = 0;
        b.Life = 1f;
        float length = (end - Origin).Length();
        b.Length = length;
        if (!(length > 0f)) return;
        int n = (int)MathF.Ceiling(length / s.SegmentLength);
        if ((uint)n < 2) n = 2;
        b.Points = n;
        b.Offsets = new float[n];
        if (s.Count != 0) Remaining--;
        NextBirth = s.BirthRate + now;
        b.Struck = true;
        NextSlot++;
        if (s.MaxParticles <= NextSlot) NextSlot = 0;
    }

    /// <summary>
    /// The update: with no thing, finished; otherwise a beam struck if due, and every
    /// beam with life: at its update time, the next one <c>updaterate</c> on, one <c>rand()</c>, the
    /// wobble — noise: each inner point <c>rand × 2 × turbulence − turbulence</c>, the ends 0;
    /// sinus: <c>(1 − |d − L/2| / (L/2)) × turbulence × sin((r × 2 × turbulence + d) × frequency)</c>
    /// at <c>d = i × size</c> along a beam <c>L</c> long — a <c>rand()</c> × 360 for its turn, and its
    /// width by its life; then its texture scrolled back by <c>texturerate × dt</c> (wrapping by the
    /// texture's height), and its life down by <c>dt / lifespan</c>.
    /// </summary>
    public override bool Update(float dt)
    {
        var s = Settings;
        float fade = dt / (s.Lifespan * 0.001f);
        if (Thing is null) return false;
        Strike();
        uint now = Now;
        var rng = System.Rand;
        bool alive = false;
        for (int k = 0; k < Bolts.Length; k++)
        {
            ref var b = ref Bolts[k];
            if (Thing is null || !(b.Life > 0f)) continue;
            alive = true;
            if (b.NextUpdate <= now && b.Offsets is { } offsets)
            {
                float amp = s.Turbulence + s.Turbulence;
                b.NextUpdate = s.UpdateRate + now;
                float r = rng.Unit();
                float along = 0f;
                for (int i = 0; i < b.Points && i < offsets.Length; i++)
                {
                    if (!s.Sinus)
                        offsets[i] = i == 0 || i == b.Points - 1 ? 0f : rng.Unit() * amp - s.Turbulence;
                    else
                    {
                        float angle = (r * amp + along) * s.Frequency;
                        float half = b.Length * 0.5f;
                        float off = MathF.Abs(along - half);
                        offsets[i] = (1f - off / half) * s.Turbulence * MathF.Sin(angle * (MathF.PI / 180f));
                    }
                    along += s.SegmentLength;
                }
                b.Roll = rng.Unit() * 360f;
                b.Size = (s.StartSize - s.EndSize) * b.Life + s.EndSize;
            }
            float tex = b.TexOffset - dt * s.TextureRate;
            b.TexOffset = tex;
            if (tex < 0f)
            {
                int h = Math.Max(1, TextureHeight);
                int whole = (int)(tex / h);
                b.TexOffset = (uint)((1 - whole) * h) + tex;
            }
            b.Life -= fade;
        }
        return !(Dying && !alive);
    }

    /// <summary>A beam is quads; see <see cref="CollectQuads"/>.</summary>
    public override void Collect(List<ParticleSprite> into) { }

    /// <summary>The records the renderer takes in one call (0x30 bytes each).</summary>
    public const int RecordsPerCall = 64;

    /// <summary>
    /// The draw (the renderer's trail entry). Per beam with life and width: the
    /// direction to the end made a frame whose Y is the beam (X = Y × up, Z = X × Y; the
    /// world's X stands in for up on a vertical beam), placed on the thing and turned by the
    /// beam's roll about its length; the camera (<see cref="ParticleSystem.Eye"/>) taken
    /// into that frame and the width laid across the beam at <c>−atan2(x, z)</c> — square
    /// to the line of sight. Point <c>i</c>, <c>i × length / (n − 1)</c> along, gives two corners
    /// ± half the width across it, shifted along X by its wobble, at u 0 and 1 and
    /// <c>v = (along + scroll) / texture height</c>, coloured from <c>endcolor</c> to
    /// <c>startcolor</c> by life. They go to the renderer 64 at a time, and each call's are joined
    /// only among themselves — **a beam of more than 64 points has a gap** every 64, 1.1's.
    /// </summary>
    public override void CollectQuads(List<ParticleQuad> into)
    {
        var s = Settings;
        if (Thing is null || End is not { } end) return;
        var eye = System.Eye;
        for (int k = 0; k < Bolts.Length; k++)
        {
            ref var b = ref Bolts[k];
            if (!(b.Life > 0f) || !(b.Size > 0f) || b.Offsets is not { } offsets) continue;
            float w = b.Size * 0.5f;
            uint color = Blend(s.StartColor, s.EndColor, b.Life);
            var d = end - Origin;
            float length = d.Length();
            if (length != 0f) d *= 1f / length;
            b.Length = length;
            int n = Math.Max(2, (int)(b.Points * ParticleSystem.Lod));
            float step = length / (n - 1);
            var frame = Basis(d);
            frame.M41 = Origin.X; frame.M42 = Origin.Y; frame.M43 = Origin.Z;
            frame = RingBase.Turn(new Vector3(0, b.Roll, 0), Vector3.Zero) * frame;
            Matrix4x4.Invert(frame, out var inverse);
            var local = Vector3.Transform(eye, inverse);
            float theta = -MathF.Atan2(local.X, local.Z) * (180f / MathF.PI);
            var (sin, cos) = MathF.SinCos(theta * (MathF.PI / 180f));
            float cw = cos * w, sw = sin * w;
            var x = new Vector3(frame.M11, frame.M12, frame.M13);
            var y = new Vector3(frame.M21, frame.M22, frame.M23);
            var z = new Vector3(frame.M31, frame.M32, frame.M33);
            var o = frame.Translation;
            float h = Math.Max(1, TextureHeight);
            Vector3 pa = default, pb = default;
            float pv = 0f;
            float along = 0f;
            for (int i = 0; i < n; i++)
            {
                float off = i < offsets.Length ? offsets[i] : 0f;
                var a = x * (cw + off) + z * sw + y * along + o;
                var c = x * (off - cw) + z * -sw + y * along + o;
                float v = (along + b.TexOffset) / h;
                if (i % RecordsPerCall != 0)
                    into.Add(new ParticleQuad(pa, pb, c, a, pv, v, color, color));
                (pa, pb, pv) = (a, c, v);
                along += step;
            }
        }
    }

    /// <summary>
    /// A frame whose Y axis is <paramref name="d"/>: X = Y × (0, 0, 1), made a unit
    /// vector, Z = X × Y; when the beam is vertical (X comes out 0), Z = (1, 0, 0) × Y made a unit
    /// vector and X = Y × Z. No position.
    /// </summary>
    public static Matrix4x4 Basis(Vector3 d)
    {
        var x = new Vector3(d.Y, -d.X, 0f);
        float len = x.Length();
        Vector3 z;
        if (len != 0f)
        {
            x *= 1f / len;
            z = new Vector3(x.Y * d.Z - x.Z * d.Y, x.Z * d.X - x.X * d.Z, x.X * d.Y - x.Y * d.X);
        }
        else
        {
            z = new Vector3(0f, -d.Z, d.Y);
            float zl = z.Length();
            if (zl != 0f) z *= 1f / zl;
            x = new Vector3(d.Y * z.Z - d.Z * z.Y, d.Z * z.X - d.X * z.Z, d.X * z.Y - d.Y * z.X);
        }
        return new Matrix4x4(x.X, x.Y, x.Z, 0f, d.X, d.Y, d.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f);
    }
}
