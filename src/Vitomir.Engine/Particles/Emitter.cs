// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One emitter: the base class every kind derives from.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Particles;

/// <summary>
/// One particle. The gas kind keeps 0x28 bytes of these and fire and flame 0x30 (the two extra
/// floats are <see cref="Home"/>); the fields are in the engine's order.
/// </summary>
public struct Particle
{
    /// <summary>+0: two bits of the shared generator, drawn at birth and never read again.</summary>
    public int Frame;
    /// <summary>Units a second.</summary>
    public Vector3 Velocity;
    /// <summary>In the world.</summary>
    public Vector3 Position;
    /// <summary>Half the width of its square.</summary>
    public float Size;
    /// <summary>How much of its life is left, 1 at birth; it is drawn while this is above 0.</summary>
    public float Life;
    /// <summary>What <see cref="Life"/> loses a second — one over its lifetime in seconds.</summary>
    public float LifeRate;
    /// <summary>Fire only: the X and Y of the point it was born at, which pulls it back.</summary>
    public Vector2 Home;
}

/// <summary>
/// An emitter: a prototype's numbers (<see cref="Settings"/>), the thing it rides, the frame it
/// emits in and its particles.
///
/// **How one lives**. It is made when a thing's template applies
/// `PARTICLENAME` (tag 119), as a copy of the named prototype, and
/// hangs off the thing (and keeps its thing). It does nothing until it is
/// **registered** in the list of 128 live emitters, which happens when its thing is
/// drawn — a rain emitter registers the moment it is made. Once a frame, at
/// the end of drawing the world, every registered emitter is **updated and
/// then drawn**: the frame is copied from its thing, the kind's own update spawns and
/// moves (<see cref="Update"/>), and the kind's own draw hands its live particles to the renderer
/// (<see cref="Collect"/>). An emitter whose thing has not been drawn for 60 frames drops out of
/// the list (and keeps its particles, frozen, until it is drawn again). When its thing is
/// destroyed it stops spawning, lets what it has die out, and is deleted.
/// </summary>
public abstract class Emitter
{
    protected Emitter(EmitterSettings settings, ParticleSystem system)
    {
        Settings = settings;
        System = system;
        Flags = settings.Flags;
    }

    /// <summary>Its numbers, a copy of the prototype's.</summary>
    public EmitterSettings Settings { get; }
    public EmitterKind Kind => Settings.Kind;
    public string Name => Settings.Name;
    /// <summary>The system it belongs to, which holds the two random sources and the clock.</summary>
    public ParticleSystem System { get; }
    /// <summary>The material cache index its particles are drawn with, or −1.</summary>
    public int Material { get; internal set; } = -1;
    /// <summary>The thing it rides, or null once that is gone.</summary>
    public WorldThing? Thing { get; internal set; }

    // ---- the frame (the thing's 3 × 4 matrix, copied every update) ----------

    /// <summary>The thing's X, Y and Z axes (rows 0–2 of its matrix). Identity until the first update.</summary>
    public Vector3 AxisX { get; set; } = Vector3.UnitX;
    public Vector3 AxisY { get; set; } = Vector3.UnitY;
    public Vector3 AxisZ { get; set; } = Vector3.UnitZ;
    /// <summary>The thing's position (row 3).</summary>
    public Vector3 Origin { get; set; }
    /// <summary>Where the thing was at the last update.</summary>
    internal Vector3 LastOrigin;
    /// <summary>How far it moved since: a birth is moved back along this by how far into the frame it fell.</summary>
    public Vector3 Moved { get; internal set; }

    // ---- the clocks ---------------------------------------------------------------------------------

    /// <summary>When the next particle is due, on the engine's millisecond clock.</summary>
    public uint NextBirth { get; set; }
    /// <summary>The clock at the last update (and at registering).</summary>
    public uint LastUpdate { get; set; }
    /// <summary>The frame its thing was last drawn in.</summary>
    internal int SeenFrame;
    /// <summary>It is in the live list.</summary>
    public bool Registered { get; internal set; }
    /// <summary>Its thing is gone; it spawns no more and is deleted when the last particle dies.</summary>
    public bool Dying { get; internal set; }
    /// <summary>Its flags.</summary>
    public uint Flags { get; set; }
    /// <summary>
    /// <c>DisableEmitter</c> sets it and <c>EnableEmitter</c> clears it. The
    /// draw loop tests it — and nothing else does, so a disabled emitter **goes on
    /// spawning and moving unseen** and is back at full strength the moment it is enabled.
    /// </summary>
    public const uint FlagHidden = 0x10000;
    public bool Hidden => (Flags & FlagHidden) != 0;

    /// <summary>The particle array.</summary>
    public Particle[] Particles { get; protected init; } = [];
    /// <summary>The slot the next birth goes in; the array is a ring.</summary>
    public uint NextSlot { get; set; }

    /// <summary>
    /// How many of the particles are looked at: <c>trunc(maxparticles × particleLOD)</c>, the same
    /// bound in the update, the birth and the draw.
    /// </summary>
    protected int Active => Math.Min(Particles.Length, (int)(Settings.MaxParticles * ParticleSystem.Lod));

    /// <summary>
    /// The kind's update (its virtual update): births, then every live particle moved and aged by
    /// <paramref name="dt"/> seconds. Answers false when the emitter is finished — dying, with
    /// nothing left alive — which is the only time any of the built kinds says so.
    /// </summary>
    public abstract bool Update(float dt);

    /// <summary>The kind's draw: its live particles as sprites, in array order.</summary>
    public abstract void Collect(List<ParticleSprite> into);

    /// <summary>
    /// The kinds that draw quads already laid out in the world rather than camera-facing squares
    /// (a trail's ribbon, a shock ring, a pad): the same draw, through the renderer's
    /// quad entries. Nothing by default.
    /// </summary>
    public virtual void CollectQuads(List<ParticleQuad> into) { }

    /// <summary>
    /// A rain's streaks (drawn by the renderer's rain entry): quads whose width lies along the
    /// camera's right, so the renderer lays them out. Nothing by default.
    /// </summary>
    public virtual void CollectStreaks(List<ParticleStreak> into) { }

    /// <summary>
    /// The base update: how far the thing moved, its matrix copied, the kind's update,
    /// and the clock noted. A thing that is gone leaves the frame where it last was.
    /// </summary>
    internal bool Step(float dt, uint now)
    {
        if (Thing is { } t)
        {
            var at = t.Position;
            Moved = at - LastOrigin;
            LastOrigin = at;
            var r = t.Rotation;
            AxisX = new Vector3(r.M11, r.M12, r.M13);
            AxisY = new Vector3(r.M21, r.M22, r.M23);
            AxisZ = new Vector3(r.M31, r.M32, r.M33);
            Origin = at;
        }
        else Moved = Vector3.Zero;
        bool alive = Update(dt);
        LastUpdate = now;
        return alive;
    }

    /// <summary>
    /// Where a new particle starts, in the world: the emitter's shape laid out in its
    /// frame. Draws <c>rand()</c> as the shape needs, in the engine's order.
    /// </summary>
    public Vector3 EmitPoint()
    {
        var s = Settings;
        var rng = System.Rand;
        float a, b, c;
        switch (s.Shape)
        {
            case EmitShape.Point:
                return Origin;
            case EmitShape.Line:
            {
                float t = rng.Unit();
                (a, b, c) = (t * s.EmitDir.X, t * s.EmitDir.Y, t * s.EmitDir.Z);
                break;
            }
            case EmitShape.Disc:
                (a, b) = Disc(s.EmitRadius);
                c = 0f;
                break;
            case EmitShape.Rectangle:
                a = (rng.Unit() - 0.5f) * s.EmitWidth; a += a;
                b = (rng.Unit() - 0.5f) * s.EmitHeight; b += b;
                c = 0f;
                break;
            case EmitShape.Sphere:
            {
                // Returned as drawn: no frame, no position (see EmitShape.Sphere).
                float r = s.EmitRadius, twice = r + r, rr = r * r;
                float x, y, z;
                do
                {
                    x = rng.Unit() * twice - r;
                    y = rng.Unit() * twice - r;
                    z = rng.Unit() * twice - r;
                } while (z * z + y * y + x * x > rr);
                return new Vector3(x, y, z);
            }
            case EmitShape.Cylinder:
                (a, b) = Disc(s.EmitRadius);
                c = rng.Unit() * s.EmitHeight;
                break;
            case EmitShape.Box:
                a = (rng.Unit() - 0.5f) * s.EmitWidth; a += a;
                b = (rng.Unit() - 0.5f) * s.EmitHeight; b += b;
                c = (rng.Unit() - 0.5f) * s.EmitRadius; c += c;
                break;
            case EmitShape.Ring:
            {
                // Which of sine and cosine goes to which axis is not read (the original uses a table
                // lookup); the angle is uniform, so the ring comes out the same either way.
                float angle = rng.Unit() * 360f * (MathF.PI / 180f);
                c = rng.Unit() * s.EmitHeight;
                a = MathF.Sin(angle) * s.EmitRadius;
                b = MathF.Cos(angle) * s.EmitRadius;
                break;
            }
            default:
                // The shape switch has eight entries; any other shape leaves the caller's point
                // as it was, which in every caller is uninitialised stack. Ours: the emitter.
                return Origin;
        }
        return a * AxisX + b * AxisY + c * AxisZ + Origin;
    }

    /// <summary>A point in a disc of <paramref name="radius"/>, by rejection: two draws a try.</summary>
    (float, float) Disc(float radius)
    {
        float twice = radius + radius, rr = radius * radius;
        float a, b;
        do
        {
            a = System.Rand.Unit() * twice - radius;
            b = System.Rand.Unit() * twice - radius;
        } while (a * a + b * b > rr);
        return (a, b);
    }

    /// <summary>
    /// A particle's colour: each byte of <c>endcolor</c> towards <c>startcolor</c> by its life,
    /// rounded to the nearest (the float-to-int by 1.5 × 2²³, so halves go to even) —
    /// as the original's three kinds do.
    /// </summary>
    public static uint Blend(uint start, uint end, float life)
    {
        uint result = 0;
        for (int shift = 24; shift >= 0; shift -= 8)
        {
            float s = (start >> shift) & 0xff, e = (end >> shift) & 0xff;
            int v = (int)MathF.Round((s - e) * life + e, MidpointRounding.ToEven);
            result |= (uint)(v & 0xff) << shift;
        }
        return result;
    }

    /// <summary>
    /// The next birth after one, and the slot after this one (the tail of every birth loop):
    /// <c>trunc(birthrate / max(particleLOD, 0.1))</c> milliseconds on, plus up to
    /// <see cref="EmitterSettings.BirthJitter"/> from the shared generator, and the ring wraps at
    /// <see cref="Active"/>.
    /// </summary>
    protected void Advance()
    {
        float lod = ParticleSystem.Lod > 0.1f ? ParticleSystem.Lod : 0.1f;
        NextBirth += (uint)(long)(Settings.BirthRate / lod);
        if (Settings.BirthJitter != 0) NextBirth = System.Lcg.Step() % Settings.BirthJitter + NextBirth;
        NextSlot++;
        if (Active <= NextSlot) NextSlot = 0;
    }

    /// <summary>
    /// The head of every birth loop: nothing while dying or before the next birth is due; a
    /// backlog older than one lifespan is forgotten (the next birth is moved up to
    /// <c>now − lifespan</c>). Answers false when there is nothing to do.
    /// </summary>
    protected bool Due(uint now)
    {
        if (Dying || NextBirth > now) return false;
        if (now - NextBirth > Settings.Lifespan) NextBirth = now - Settings.Lifespan;
        return NextBirth < now;
    }

    /// <summary>
    /// Where a birth that fell <paramref name="late"/> milliseconds before now started: the spawn
    /// point moved back along the thing's movement by the share of the last frame that
    /// <paramref name="late"/> is. The share is <c>late / (now − last update)</c>, and the frame an
    /// emitter registers in has <c>now − last update = 0</c> — so its first births divide by zero
    /// and, with a thing that has not moved, come out at NaN and are never seen. That is 1.1's, and
    /// kept: it is the backlog of one lifespan, and it would be gone in a second anyway.
    /// </summary>
    protected Vector3 BackDated(Vector3 point, uint late, uint now)
    {
        float share = -((float)late / (float)(now - LastUpdate));
        return new Vector3(share * Moved.X + point.X, share * Moved.Y + point.Y, share * Moved.Z + point.Z);
    }

    /// <summary>The time the frame has to cover; see <see cref="ParticleSystem"/>.</summary>
    protected uint Now => System.Now;
}

/// <summary>
/// An emitter of a kind Vitomir does not simulate yet: it exists — its thing has it, the natives
/// can ask its name and kind, it can be enabled and disabled — and it never has a particle.
/// </summary>
public sealed class DormantEmitter(EmitterSettings settings, ParticleSystem system) : Emitter(settings, system)
{
    public override bool Update(float dt) => !Dying;
    public override void Collect(List<ParticleSprite> into) { }
}
