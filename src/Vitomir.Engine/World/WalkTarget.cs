// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The ring that appears on the ground where you clicked — the world effects' half with no GPU.
using System.Numerics;
using Vitomir.Engine.Scene;

namespace Vitomir.Engine.World;

/// <summary>
/// The click-to-move marker: a single textured quad laid flat on the surface the click ray hit,
/// which shrinks and fades out over one second.
///
/// **It is one quad, not a ring of polygons** — the ring is the texture,
/// `materials/General/WalkTarget.tga` (64x64, in `LMaterials.nob`), whose colour is premultiplied
/// by its own alpha: every texel's RGB is its alpha times a constant warm off-white
/// (0.721, 0.655, 0.545), measured over the whole image with a spread of 0.003. Its centre is
/// hollow and its alpha peaks at 211 about twenty texels out of thirty-two.
///
/// The engine keeps it in three globals — the point, the elapsed time,
/// the active flag — set by the click, cleared by the walker's stop, and drawn by the effects' draw,
/// which is the **last** call the world draw makes, after every piece of geometry.
///
/// **The draw mode is read, not guessed.** The effects' draw passes **13** to the renderer's draw entry, and
/// 13 is <see cref="RenderKind.Glow"/> in the mode table this tree already decoded from the
/// executable: source and destination blend both **one**, so purely
/// additive — which is what a premultiplied texture wants and why fading the vertex colour to black
/// fades the ring to nothing — with the depth test set to **always**, which is what lets a quad
/// sitting exactly in the floor's own plane draw without fighting it. The drop-shadow blobs next
/// door pass **30**, which is <see cref="RenderKind.Shadow"/>; the two together are what identify
/// the argument as a mode index rather than a flag word.
/// </summary>
public sealed class WalkTarget
{
    /// <summary>How long it lives, in seconds (`_DAT_004eb3f4` = 1.0).</summary>
    public const float Lifetime = 1f;

    /// <summary>
    /// Half the quad's width: <c>remaining x 31.8 + 0.2</c> (`_DAT_004eb738`, `_DAT_004eb734`). So
    /// it is **64 world units across** the instant you click and **0.4** just before it goes.
    /// </summary>
    public const float SizeRamp = 31.8f, SizeBase = 0.2f;

    /// <summary>The vertex colour's full value (`_DAT_004eb714` = 255.0); alpha is always this.</summary>
    public const float Full = 255f;

    /// <summary>The material the engine asks the registry for.</summary>
    public const string Material = "walktarget";

    /// <summary>Mode 13, which the effects' draw passes to the draw entry.</summary>
    public const RenderKind Mode = RenderKind.Glow;

    /// <summary>Whether there is one to draw.</summary>
    public bool Active { get; private set; }

    /// <summary>Where it sits: the point the click ray hit, with no lift off the floor at all.</summary>
    public Vector3 Point { get; private set; }

    /// <summary>The clicked surface's own plane normal, which is what tilts the quad on a slope.</summary>
    public Vector3 Normal { get; private set; } = Vector3.UnitZ;

    /// <summary>Seconds since the click. The engine advances this inside the draw.</summary>
    public float Elapsed { get; private set; }

    /// <summary>
    /// Places one. **A click that picked no surface places nothing** — the engine's
    /// first line is a null test on the node it would take the ground plane from, and it returns
    /// without touching anything else, so a failed pick leaves whatever was there still running.
    /// </summary>
    public void Set(Vector3 point, Vector3? normal)
    {
        if (normal is not { } n || n.LengthSquared() <= 0f) return;
        Point = point;
        Normal = Vector3.Normalize(n);
        Elapsed = 0f;
        Active = true;
    }

    /// <summary>
    /// Takes it away. The engine calls this from three places, and all three are
    /// **about the character rather than the clock**: the walker stopping (whether it
    /// arrived or gave up), the walk order being cancelled, and selecting a party member
    /// who is not moving. So the ring usually goes when you get there, not a second
    /// after you set off.
    /// </summary>
    public void Clear() => Active = false;

    /// <summary>
    /// Moves the clock on and answers whether there is still anything to draw. The engine does this
    /// at the head of the draw itself: <c>elapsed += dt</c>, clamp to <see cref="Lifetime"/>, and
    /// when nothing is left, clear the flag. It uses the **interface's** clock (`_DAT_006f2050`,
    /// whose pause word nothing sets), so the ring goes on spinning and runs out while the game is
    /// paused (<see cref="DrawClocks"/>).
    /// </summary>
    public bool Advance(float seconds)
    {
        if (!Active) return false;
        Elapsed += seconds;
        if (Remaining > 0f) return true;
        Active = false;
        return false;
    }

    /// <summary>One at the click, nought when it is spent.</summary>
    public float Remaining => Active ? Lifetime - MathF.Min(Elapsed, Lifetime) : 0f;

    /// <summary>Half the quad's width now.</summary>
    public float HalfSize => Remaining * SizeRamp + SizeBase;

    /// <summary>
    /// The vertex colour now, in the engine's own 0..255: <c>(b, b, b, 255)</c> with
    /// <c>b = remaining x 255</c>. **The alpha never changes** — the fade is the colour going to
    /// black, which is invisible only because the blend is additive.
    /// </summary>
    public Vector4 Colour => new(Remaining * Full, Remaining * Full, Remaining * Full, Full);

    /// <summary>
    /// The quad's four corners, in the plane of the surface that was clicked, with the texture's
    /// whole 0..1 laid across them.
    ///
    /// **The quad's rotation within that plane is ours and cannot be seen.** The engine builds its
    /// basis by crossing the surface normal against fixed axes; this crosses against
    /// whichever world axis the normal leans on least, which is a different basis and the same
    /// picture, because the artwork is a ring and a ring has no corners to give it away.
    /// </summary>
    public (Vector3 Position, Vector2 Uv)[] Corners()
    {
        var n = Normal;
        var axis = MathF.Abs(n.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(axis, n));
        var v = Vector3.Cross(n, u);
        float s = HalfSize;
        return
        [
            (Point - u * s + v * s, new Vector2(0f, 1f)),
            (Point + u * s + v * s, new Vector2(0f, 0f)),
            (Point + u * s - v * s, new Vector2(1f, 0f)),
            (Point - u * s - v * s, new Vector2(1f, 1f)),
        ];
    }
}
