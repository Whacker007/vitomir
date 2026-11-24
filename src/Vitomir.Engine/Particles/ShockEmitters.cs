// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Two kinds made of flat textured squares, each with a frame of its own: the shock ring
// and the discipline pad under a caster, which shares
// the shock's parser and draw.
using System.Numerics;

namespace Vitomir.Engine.Particles;

/// <summary>
/// One ring of a shock or a pad (0x3c bytes each): a flat square of half-width
/// <see cref="Size"/> lying in the X/Y plane of its own frame.
/// </summary>
public struct Ring
{
    /// <summary>+0: half the square's width.</summary>
    public float Size;
    /// <summary>Its frame, a 3 × 4 matrix — the rows are its X, Y and Z axes and its centre.</summary>
    public Matrix4x4 Frame;
    /// <summary>How much of its life it has; drawn while not 0.</summary>
    public float Life;
    /// <summary>What <see cref="Life"/> changes by a second, one over the lifespan in seconds.</summary>
    public float Rate;
}

/// <summary>
/// What the three ring kinds share — shock, pad and ring: the rings, the births (which the
/// shock and the ring both call), the spin, and the flat-square draw the shock and the pad use
/// (the ring draws a band instead, <see cref="RingEmitter"/>).
/// </summary>
public abstract class RingBase : Emitter
{
    protected RingBase(EmitterSettings settings, ParticleSystem system) : base(settings, system)
    {
        Rings = new Ring[settings.MaxParticles];
        // The clone: the next ring is due at once.
        NextBirth = system.Now;
    }

    public Ring[] Rings { get; }

    /// <summary>The lifespan's reciprocal (<c>1 / (lifespan × 0.001)</c>).</summary>
    protected float Rate => 1f / (Settings.Lifespan * 0.001f);

    /// <summary>A ring's frame at birth: the emitter's, raised one unit along the world's Z (the ring's own row 3).</summary>
    protected Matrix4x4 BirthFrame(bool identity)
    {
        var m = identity ? Matrix4x4.Identity : new Matrix4x4(
            AxisX.X, AxisX.Y, AxisX.Z, 0f,
            AxisY.X, AxisY.Y, AxisY.Z, 0f,
            AxisZ.X, AxisZ.Y, AxisZ.Z, 0f,
            Origin.X, Origin.Y, Origin.Z, 1f);
        m.M43 += 1f;
        return m;
    }

    /// <summary>
    /// This frame's turn: <c>rotvel × dt</c> as roll, pitch and yaw in degrees — the
    /// rows are (cos p cos y, cos p sin y, −sin p), (sin r sin p cos y − cos r sin y, sin r sin p
    /// sin y + cos r cos y, sin r cos p), (cos r sin p cos y + sin r sin y, cos r sin p sin y − sin r
    /// cos y, cos r cos p) — with <paramref name="move"/> as its row 3. The original reads sine and
    /// cosine from a table of 4096 entries a quarter turn; ours computes them.
    /// </summary>
    public static Matrix4x4 Turn(Vector3 degrees, Vector3 move)
    {
        const float rad = MathF.PI / 180f;
        var (sr, cr) = MathF.SinCos(degrees.X * rad);
        var (sp, cp) = MathF.SinCos(degrees.Y * rad);
        var (sy, cy) = MathF.SinCos(degrees.Z * rad);
        return new Matrix4x4(
            cp * cy, cp * sy, -sp, 0f,
            sp * (sr * cy) - cr * sy, sp * sr * sy + cr * cy, cp * sr, 0f,
            sp * (cr * cy) + sr * sy, sp * (cr * sy) - sr * cy, cr * cp, 0f,
            move.X, move.Y, move.Z, 1f);
    }

    /// <summary>A ring's frame turned by <paramref name="turn"/> in its own axes (turn × frame).</summary>
    protected static Matrix4x4 Apply(Matrix4x4 frame, Matrix4x4 turn) => turn * frame;

    /// <summary>The emitter's frame as a matrix: rows X, Y, Z and the origin.</summary>
    protected Matrix4x4 EmitterFrame => new(
        AxisX.X, AxisX.Y, AxisX.Z, 0f,
        AxisY.X, AxisY.Y, AxisY.Z, 0f,
        AxisZ.X, AxisZ.Y, AxisZ.Z, 0f,
        Origin.X, Origin.Y, Origin.Z, 1f);

    /// <summary>
    /// The rings due (the shock's and the ring's): only while not dying, due, with count
    /// left and a thing. Then every ring due by now — **the count is looked at only on the way in**,
    /// so a backlog lays several even with a count of one — each in the emitter's frame (the world's,
    /// with flag bit 1), a unit up, at <c>startsize</c> and full life; the next <c>birthrate</c> on;
    /// the count down one if it is not already 0.
    /// </summary>
    protected void LayDue()
    {
        uint now = Now;
        var s = Settings;
        if (Dying || NextBirth > now || s.Count == 0 || Thing is null) return;
        float rate = Rate;
        do
        {
            ref var r = ref Rings[NextSlot];
            r.Frame = BirthFrame((Flags & 1) != 0);
            r.Rate = rate;
            r.Size = s.StartSize;
            r.Life = 1f;
            NextBirth += s.BirthRate;
            NextSlot++;
            if (s.MaxParticles <= NextSlot) NextSlot = 0;
            if (s.Count != 0) s.Count--;
            // Ours: a birthrate of 0 would lay rings for ever; no shipped block has one.
            if (s.BirthRate == 0) break;
        } while (NextBirth <= now);
    }

    /// <summary>
    /// The shock's and the ring's aging: this frame's turn and move —
    /// <c>rotvel × dt</c> and <c>velocity × dt</c> — and every ring with life loses
    /// <c>dt / lifespan</c>; one still at or above 0 is turned and moved in its own frame and grows
    /// by <c>growthrate × dt</c>, one below is put out. Answers whether any is alive.
    /// </summary>
    protected bool Age(float dt)
    {
        var s = Settings;
        var turn = Turn(s.RotVel * dt, s.Velocity * dt);
        bool alive = false;
        int n = Math.Min(Rings.Length, (int)s.MaxParticles);
        for (int i = 0; i < n; i++)
        {
            ref var r = ref Rings[i];
            if (r.Life == 0f) continue;
            float life = r.Life - dt * r.Rate;
            r.Life = life;
            if (life >= 0f)
            {
                r.Frame = Apply(r.Frame, turn);
                alive = true;
                r.Size = dt * s.GrowthRate + r.Size;
            }
            else r.Life = 0f;
        }
        return alive;
    }

    /// <summary>Rings are quads; see <see cref="CollectQuads"/>.</summary>
    public override void Collect(List<ParticleSprite> into) { }

    /// <summary>
    /// The draw (the renderer's ring entry): every ring with life, as a square
    /// in its frame's X/Y plane — corners centre −X+Y (u 0, v 1), −X−Y (0, 0), +X−Y (1, 0), +X+Y (1, 1),
    /// each X and Y its axis × its size — coloured from <c>endcolor</c> towards <c>startcolor</c> by
    /// its life, rounded half to even. Handed over here in the quad layout the trail uses, which
    /// draws the same two triangles.
    /// </summary>
    public override void CollectQuads(List<ParticleQuad> into)
    {
        var s = Settings;
        int n = Math.Min(Rings.Length, (int)s.MaxParticles);
        for (int i = 0; i < n; i++)
        {
            ref var r = ref Rings[i];
            if (r.Life == 0f) continue;
            var x = new Vector3(r.Frame.M11, r.Frame.M12, r.Frame.M13) * r.Size;
            var y = new Vector3(r.Frame.M21, r.Frame.M22, r.Frame.M23) * r.Size;
            var o = r.Frame.Translation;
            uint c = Blend(s.StartColor, s.EndColor, r.Life);
            into.Add(new ParticleQuad(o - x + y, o + x + y, o + x - y, o - x - y, 1f, 0f, c, c));
        }
    }
}

/// <summary>
/// **Shock** (kind 8: <c>shockwave</c>, <c>shockflame</c>, <c>shockrings</c>, <c>flamewave</c>, the
/// inverted waves; explosions spawn them). Every <c>birthrate</c> milliseconds, while its
/// <c>count</c> lasts (−1: for ever), a flat ring is laid at the thing, one unit up, in the thing's
/// frame; it then grows by <c>growthrate</c> a second, turns by <c>rotvel</c>, moves by
/// <c>velocity</c> **along its own axes**, and fades from <c>startcolor</c> to <c>endcolor</c>
/// over <c>lifespan</c>. The emitter reports itself finished the moment no ring is alive, which
/// drops it from the live list (it comes back the next time its thing is drawn).
/// </summary>
public sealed class ShockEmitter(EmitterSettings settings, ParticleSystem system) : RingBase(settings, system)
{
    /// <summary>The update: the rings due, then their aging. Finished when none is alive.</summary>
    public override bool Update(float dt)
    {
        LayDue();
        return Age(dt);
    }
}

/// <summary>
/// **Pad** (kind 12: the discipline circles — <c>ankhsPad</c>, <c>flamePad</c>,
/// <c>ringOfSkullsRed</c>, <c>sawToothBlue</c>, …). All of its <c>count</c> rings (one, in every
/// shipped block) are laid at once, the first time it updates, with **no life**; each then gains
/// life up to 1 over <c>lifespan</c> (1000 ms unless the block says), sized from <c>endsize</c>
/// towards <c>startsize</c> by it — so the circle opens out under the caster — spins by
/// <c>rotvel</c>, and is kept on the thing, one unit up, every frame. When the thing goes the
/// rings lose their life again, shrink back, and the emitter is deleted once they are out.
/// </summary>
public sealed class PadEmitter(EmitterSettings settings, ParticleSystem system) : RingBase(settings, system)
{
    /// <summary>
    /// The rings: while not dying, every one of <c>count</c> at once, each in the
    /// emitter's frame, a unit up, at <c>startsize</c>, with life 0.
    /// </summary>
    void Lay()
    {
        var s = Settings;
        if (Dying || s.Count == 0) return;
        float rate = Rate;
        do
        {
            ref var r = ref Rings[NextSlot];
            r.Frame = BirthFrame(identity: false);
            r.Rate = rate;
            r.Size = s.StartSize;
            r.Life = 0f;
            NextSlot++;
            if (s.MaxParticles <= NextSlot) NextSlot = 0;
            if (s.Count != 0) s.Count--;
        } while (s.Count != 0);
    }

    /// <summary>
    /// The update: the rings, then this frame's turn (its move is never set — ours: none;
    /// the centre is put back on the thing straight after), and for every ring laid so far — up to
    /// the next slot — its life up by <c>dt / lifespan</c> to at most 1, or, dying, down to at least
    /// 0 (a ring at 0 is left alone); then turned, put on the thing a unit up, and sized
    /// <c>endsize + (startsize − endsize) × life</c>. Answers whether any is alive, which while the
    /// thing is there is always.
    /// </summary>
    public override bool Update(float dt)
    {
        var s = Settings;
        if (s.Count != 0) Lay();
        var turn = Turn(s.RotVel * dt, Vector3.Zero);
        bool alive = false;
        int n = Math.Min(Rings.Length, (int)NextSlot);
        for (int i = 0; i < n; i++)
        {
            ref var r = ref Rings[i];
            if (!Dying)
            {
                float life = dt * r.Rate + r.Life;
                r.Life = life <= 1f ? life : 1f;
            }
            else
            {
                float life = r.Life - dt * r.Rate;
                if (life < 0f) life = 0f;
                r.Life = life;
                if (life == 0f) continue;
            }
            r.Frame = Apply(r.Frame, turn);
            r.Frame.M41 = Origin.X;
            r.Frame.M42 = Origin.Y;
            r.Frame.M43 = Origin.Z + 1f;
            alive = true;
            r.Size = (s.StartSize - s.EndSize) * r.Life + s.EndSize;
        }
        return alive;
    }
}

/// <summary>
/// **Ring** (kind 11: <c>energyRing</c>, <c>ringOfFlames</c>, <c>flameRingDisc</c>,
/// <c>ringTower</c>, <c>wraithRing</c>, …). Its rings are laid and aged as a shock's are
/// (then the turn, move and growth), but each is drawn as a **band**: <c>numsegs</c> points round a
/// circle of the ring's size in its X/Y plane, each joined to the next and to a point
/// <c>height</c> above it along the ring's Z, the height running from <c>endheight</c> to
/// <c>startheight</c> by its life; the texture wraps <c>texturerate</c> times round, the bottom edge
/// is coloured <c>startcolor</c>→<c>endcolor</c> and the top <c>startcolormax</c>→<c>endcolormax</c>.
/// With flag bit 1 the rings are kept in the thing's frame and follow it.
///
/// **The update always answers 1**, so a ring emitter is never finished: once its
/// thing is gone it stays in the live list for good, drawing nothing, and holds its slot — 1.1's,
/// kept.
/// </summary>
public sealed class RingEmitter : RingBase
{
    /// <summary>The unit directions round the circle (at most 64): (cos, sin, 0) of <c>i × 360 / n</c>.</summary>
    public Vector3[] Spokes { get; }

    /// <summary>
    /// Made: the spokes, <c>n = trunc(numsegs × particleLOD)</c> of them, each
    /// (1, 0, 0) turned by <c>i × 360 / n</c> degrees about Z. The clone asserts
    /// <c>numsegs</c> is 1..63; ours keeps it there.
    /// </summary>
    public RingEmitter(EmitterSettings settings, ParticleSystem system) : base(settings, system)
    {
        int n = Segments;
        Spokes = new Vector3[n];
        float step = 360f / n;
        for (int i = 0; i < n; i++)
        {
            var m = Turn(new Vector3(0, 0, i * step), Vector3.Zero);
            Spokes[i] = new Vector3(m.M11, m.M12, m.M13);
        }
    }

    /// <summary>The ring asserts <c>numsegs &lt; 64</c> and <c>&gt; 0</c>; ours holds it to 1..63.</summary>
    public const uint MaxSegments = 63;

    int Segments => Math.Clamp((int)(Math.Clamp(Settings.NumSegs, 1u, MaxSegments) * ParticleSystem.Lod), 1, (int)MaxSegments);

    /// <summary>The update: the rings due, their aging — and answers 1 whatever happens.</summary>
    public override bool Update(float dt)
    {
        LayDue();
        Age(dt);
        return true;
    }

    /// <summary>
    /// The draw (the renderer's trail entry): per ring with life, its frame
    /// (composed with the emitter's with flag bit 1), its height
    /// <c>endheight + (startheight − endheight) × life</c> turned into that frame, and <c>n + 1</c>
    /// records — spoke <c>i</c> × size into the frame at the bottom (u = i × texturerate / n, v 0),
    /// the same plus the height at the top (v 1), the last a copy of the first at u = texturerate —
    /// each two consecutive records one quad.
    /// </summary>
    public override void CollectQuads(List<ParticleQuad> into)
    {
        var s = Settings;
        int n = Math.Min(Spokes.Length, Segments);
        float du = s.TextureRate / n;
        int count = Math.Min(Rings.Length, (int)s.MaxParticles);
        for (int k = 0; k < count; k++)
        {
            ref var r = ref Rings[k];
            if (r.Life == 0f) continue;
            var f = (Flags & 1) == 0 ? r.Frame : r.Frame * EmitterFrame;
            float h = (s.StartHeight - s.EndHeight) * r.Life + s.EndHeight;
            var up = Vector3.TransformNormal(new Vector3(0, 0, h), f);
            uint bottom = Blend(s.StartColor, s.EndColor, r.Life);
            uint top = Blend(s.StartColorMax, s.EndColorMax, r.Life);
            Vector3 b0 = default, t0 = default;
            for (int i = 0; i <= n; i++)
            {
                var b = Vector3.Transform(Spokes[i % n] * r.Size, f);
                var t = up + b;
                float u = i == n ? s.TextureRate : i * du;
                if (i > 0)
                    into.Add(new ParticleQuad(b0, t0, t, b,
                                              new Vector2((i - 1) * du, 0), new Vector2((i - 1) * du, 1), new Vector2(u, 1), new Vector2(u, 0),
                                              bottom, top, top, bottom));
                (b0, t0) = (b, t);
            }
        }
    }
}
