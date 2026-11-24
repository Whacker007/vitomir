// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Presentation;

/// <summary>
/// The moves that start at a lip (<see cref="ReachMark"/>), each drawn in the colour the proving ground paints
/// an obstacle for that move (<c>tools/platform/proving-ground/make_course.py</c>'s legend). A lip may carry
/// more than one at once: what a tap of jump does there on the ground, what a sprint does, and what the tap's jump
/// does in the air (now every move at an obstacle is a tap's).
/// </summary>
[Flags]
public enum ReachMoves : ushort
{
    None = 0,
    /// <summary>A tap of jump, close and moving towards it, hops onto it (yellow, the course's "stop").</summary>
    Hop = 1,
    /// <summary>Moving fast into it takes it by itself (orange): a sprint vaults over it, or a run steps over it.</summary>
    Hurdle = 2,
    /// <summary>A tap vaults over it (blue).</summary>
    Vault = 4,
    /// <summary>A tap climbs onto it (blue).</summary>
    ClimbOnto = 8,
    /// <summary>A tap mantles onto it (purple).</summary>
    Mantle = 16,
    /// <summary>A tap grabs it; forward or jump held at the grab's end, pulls up; nothing held, hangs (red).</summary>
    Grab = 32,
    /// <summary>A lip the hands can hold but no move reaches: just too high (dark grey, faint).</summary>
    Out = 64,
    /// <summary>A ladder's foot or top: use there puts the body on it (lime, the course's "ladder").</summary>
    Ladder = 128,
    /// <summary>
    /// A run or a sprint vaults it by itself on the vault clips (light orange; the player's body, the run's own vault on): a thin thing in their
    /// bands, which the sprint vault's orange no longer takes; a walk still taps (its blue beside it).
    /// </summary>
    RunVault = 256,
}

/// <summary>
/// One mark of the reach highlight: the moves that start at a lip and a short line along it from
/// <paramref name="A"/> to <paramref name="B"/> (metres, world; already lifted a little above the top and out
/// from the face so that it is seen, not buried in the edge), the face's outward normal (level) and the lip's
/// height above the ground the body would stand on before it.
/// </summary>
public readonly record struct ReachMark(ReachMoves Moves, Vector3 A, Vector3 B, Vector3 Normal, float Height)
{
    public Vector3 Middle => (A + B) * 0.5f;
}

/// <summary>
/// <b>The reach highlight</b> (as <i>Mirror's Edge</i>'s Runner Vision; ours): the
/// lips round a body where a move would start if the body went there and acted — a top to hop onto, a thing
/// to hurdle or vault, a top to climb or mantle onto, a lip to grab, and, faintly, a lip just out of reach; and since
/// Every ladder's foot and top within its range, where use puts the body on it (<see cref="ReachMoves.Ladder"/>).
/// <b>Presentation only</b>: it reads the level and the body's current numbers (the masquerade mode's, as the
/// <see cref="Locomotion"/> component holds them) and changes nothing — not the world, not its digest, not a
/// recording.
///
/// <para>How it looks (two steps, each a fixed order of sweeps and rays, so the same level, body and numbers
/// always give the same marks): <b>faces</b> — a thin capsule is swept level from the body, in a fan of
/// directions (every <see cref="StepRadians"/> to <see cref="HalfAngle"/> either side of where the view looks)
/// out to <see cref="Range"/>, in three bands of height from just over the stride to just over what the hands
/// reach in a jump; each near-upright face met (its own normal; the contact's on a top's edge) is one cell of
/// <see cref="Spacing"/> along it. <b>Moves</b> — for each cell, a body is stood on the ground in front of the
/// face, facing it, and <see cref="TraversalProbe.Read"/> is asked as the motor asks it (<see cref="BodyMotor"/>'s
/// <c>TryHopUp</c>, <c>SprintVaultAhead</c>, <c>StepOverAhead</c>, <c>TryFromAir</c> and <c>TryGrab</c>: the same reaches, heights and
/// limits, heights above the ground the body stands on): a tap's hop up; a sprint's vault or a run's step-over; the tap's jump's vault, climb onto
/// or mantle, else its grab (when a hanging body fits under the lip). A cell where none of these starts but a
/// holdable lip stands above the stride and no more than <see cref="OutBand"/> over the grab's reach is marked "out of
/// reach". Each move is one step of <c>Mark</c>, in the motor's order: a move added to the motor is a step added
/// there and a flag here. A charged jump's reach (the body's own) is not shown. A lip found is the face's own (on its
/// plane, facing as it does: not a corner read across) and is then followed along the face a cell at a time both
/// ways while a lip goes on, within the range, so that a lip met at a slant is lined whole. The mark lies along
/// the lip, snapped to <see cref="Spacing"/> along it so that neighbouring cells join into one line.</para>
///
/// <para>Not repeated: the motor's last check, that a move's path is clear for the crouched body before it
/// starts (a mantle under a low beam can be marked and still be refused), and the timing of a real approach
/// (the face within reach when jump is pressed, and the tap's jump rising until the obstacle is in reach).</para>
/// </summary>
public static class ReachHighlight
{
    /// <summary>How far from the body the faces are looked for (m).</summary>
    public const float Range = 6f;

    /// <summary>How far either side of where the view looks (radians; 115°: ahead and to the sides).</summary>
    public const float HalfAngle = 115f * DetMath.DegToRad;

    /// <summary>Between the fan's directions (radians; 3°: at 6 m about 0.31 m; a lip met once is then followed along).</summary>
    public const float StepRadians = 3f * DetMath.DegToRad;

    /// <summary>A mark's length along its lip, and the cells' (m).</summary>
    public const float Spacing = 0.25f;

    /// <summary>How far over the grab's highest a lip is still marked as just out of reach (m).</summary>
    public const float OutBand = 0.7f;

    /// <summary>A mark above the top, and out from the face (m).</summary>
    public const float Lift = 0.04f, Outward = 0.03f;

    /// <summary>How many cells a lip is followed along either way from where the fan met it.</summary>
    const int MaxAlong = 64;

    /// <summary>How far the lip the probe finds may stand off the face met and still be its lip (m).</summary>
    const float SameFace = 0.2f;

    /// <summary>How nearly the lip's face must face as the face met (the cosine; about 18°).</summary>
    const float SameFacing = 0.95f;

    /// <summary>How far to one side of straight ahead the lip may be (m).</summary>
    const float Ahead = 0.05f;

    /// <summary>The radius of the faces' sweep (m).</summary>
    const float SweepRadius = 0.05f;

    /// <summary>How far from the face the body is stood to ask the probe (m beyond its radius; within every move's reach).</summary>
    const float StandOff = 0.12f;

    /// <summary>A face whose normal's upward part is at most this is looked at (steeper than about 60°): ramps are walked, not climbed.</summary>
    const float FaceZ = 0.5f;

    /// <summary>How high the feet are taken to have risen at the least when the grab is asked (m; a tap's jump reaches a low lip a few ticks up).</summary>
    const float GrabLift = 0.35f;

    /// <summary>What the hands reach over the jump's top as the motor allows a grab (m; <c>TryFromAir</c>'s band).</summary>
    const float GrabOver = 0.05f;

    [ThreadStatic] static List<long>? cells;

    /// <summary>
    /// The marks round <paramref name="body"/> (its feet, its <see cref="Locomotion"/>, the view's yaw when the view
    /// is its own, else its own yaw) in <paramref name="world"/>'s level, into <paramref name="into"/> (cleared
    /// first). Reads only. Answers the count.
    /// </summary>
    public static int For(World world, EntityId body, List<ReachMark> into)
    {
        into.Clear();
        if (!world.Alive(body) || !world.Has<Body>(body) || !world.Has<Locomotion>(body) || !world.TryService<LevelSpace>(out var space)) return 0;
        var b = world.Get<Body>(body);
        float facing = b.Yaw;
        if (world.TryService<ViewState>(out var view) && view.HasView && view.Of == body) facing = view.View.Yaw;
        // The player's vault clips, when the run's own vault is on for it (the motor's: a Controlled body given them).
        OverPaths? runVaults = null;
        if (world.TryService<Animation.People.PersonOverSettings>(out var over) && over.Auto && world.TryStore<Animation.People.PersonOver>(out var overs) && overs.Has(body)
            && world.TryStore<Control.Controlled>(out var tags) && tags.Has(body) && world.TryService<Animation.CharacterAssets>(out var assets) && world.Has<Animation.CharacterAnimation>(body))
            runVaults = assets.Kind(world.Get<Animation.CharacterAnimation>(body).Kind).Person?.Over?.Paths;
        return Find(space.Collision, b.Feet, facing, world.Get<Locomotion>(body), into, runVaults: runVaults);
    }

    /// <summary>
    /// The marks round a body whose feet are at <paramref name="feet"/>, its view looking along
    /// <paramref name="facing"/> (yaw, radians), with the numbers <paramref name="loco"/>, in
    /// <paramref name="level"/>, into <paramref name="into"/> (cleared first): a pure function of these. Answers the count.
    /// </summary>
    public static int Find(CollisionWorld level, Vector3 feet, float facing, in Locomotion loco, List<ReachMark> into,
                           float range = Range, float halfAngle = HalfAngle, OverPaths? runVaults = null)
    {
        into.Clear();
        var seen = cells ??= new List<long>(512);
        seen.Clear();
        float low = MathF.Max(0f, loco.StepHeight) + 0.02f;
        float top = GrabTop(loco) + OutBand + 0.1f;
        if (range <= 0f || top <= low + 0.1f) return 0;
        float band = (top - low) / 3f;
        int steps = Math.Max(1, (int)MathF.Ceiling(2f * halfAngle / StepRadians));
        for (int i = 0; i <= steps; i++)
        {
            float a = facing - halfAngle + i * (2f * halfAngle / steps);
            var (s, c) = DetMath.SinCos(a);
            var dir = new Vector3(c, s, 0f);
            for (int k = 0; k < 3; k++)
            {
                float z0 = low + k * band;
                var sweep = new Capsule(feet + new Vector3(0, 0, z0 + SweepRadius), MathF.Max(0f, band - 2f * SweepRadius), SweepRadius);
                if (level.CapsuleSweep(sweep, dir, range) is not { } hit) continue;
                // The face's own normal, not the contact's: met at a corner, the contact's points across it, and a
                // body stood along that would read the corner, not the face. Met on the edge of a top (the top's
                // triangle touched with the face's), the face under the edge, by a level ray just below it; with
                // none (a thin plate's edge), the contact's, level.
                Vector3 normal = hit.FaceNormal, point = hit.Point;
                if (MathF.Abs(normal.Z) > FaceZ)
                {
                    if (level.Ray(new Vector3(point.X - c * 0.2f, point.Y - s * 0.2f, point.Z - 0.03f), dir, 0.4f) is { } under
                        && MathF.Abs(under.FaceNormal.Z) <= FaceZ)
                        (normal, point) = (under.FaceNormal, under.Point);
                    else normal = hit.Normal;
                }
                if (MathF.Abs(normal.Z) > FaceZ) continue;                     // a floor, a ceiling or a ramp
                var n = new Vector2(normal.X, normal.Y);
                float nl = n.Length();
                if (nl < 0.3f) continue;
                n /= nl;
                if (n.X * c + n.Y * s > 0f) n = -n;                           // turned to the sweep, however it is wound
                if (n.X * c + n.Y * s > -0.05f) continue;                    // met side-on
                long key = Cell(new Vector2(point.X, point.Y), n);
                if (seen.Contains(key)) continue;
                seen.Add(key);
                if (!Mark(level, feet, point, n, loco, into, runVaults)) continue;
                // Then along the face both ways, a cell at a time, while a lip goes on there: at a slant the fan's
                // directions fall wider apart along a face than its cells, and a lip would show in pieces.
                var t = new Vector2(-n.Y, n.X);
                for (int side = -1; side <= 1; side += 2)
                {
                    var p = point;
                    for (int j = 0; j < MaxAlong; j++)
                    {
                        p += new Vector3(t * (side * Spacing), 0f);
                        if (Vector2.DistanceSquared(new Vector2(p.X, p.Y), new Vector2(feet.X, feet.Y)) > range * range) break;
                        long next = Cell(new Vector2(p.X, p.Y), n);
                        if (seen.Contains(next)) break;
                        seen.Add(next);
                        if (!Mark(level, feet, p, n, loco, into, runVaults)) break;
                    }
                }
            }
        }
        Ladders(level, feet, range, into);
        return into.Count;
    }

    /// <summary>
    /// A ladder's foot and top within <paramref name="range"/> of the feet (level distance, and no further up or down),
    /// each a line across its width: use there puts the body on it (<see cref="BodyMotor"/>, <c>BodyMotor.Ladder.cs</c>).
    /// Every ladder in range is shown, whichever way the view looks: a level has few.
    /// </summary>
    static void Ladders(CollisionWorld level, Vector3 feet, float range, List<ReachMark> into)
    {
        var ladders = level.Ladders;
        for (int i = 0; i < ladders.Count; i++)
        {
            ref readonly var l = ref ladders[i];
            var half = l.Side * (l.Width * 0.5f);
            var lift = new Vector3(0f, 0f, Lift) + l.Out * Outward;
            if (InRange(l.Foot, feet, range)) into.Add(new ReachMark(ReachMoves.Ladder, l.Foot - half + lift, l.Foot + half + lift, l.Out, 0f));
            if (InRange(l.Top, feet, range)) into.Add(new ReachMark(ReachMoves.Ladder, l.Top - half + lift, l.Top + half + lift, l.Out, l.Rise));
        }
    }

    static bool InRange(Vector3 at, Vector3 feet, float range) =>
        Vector2.DistanceSquared(new Vector2(at.X, at.Y), new Vector2(feet.X, feet.Y)) <= range * range && MathF.Abs(at.Z - feet.Z) <= range;

    /// <summary>The highest a lip can be grabbed above the ground left (m): what the hands reach at the top of a tap's jump (<c>TryFromAir</c>).</summary>
    public static float GrabTop(in Locomotion loco) => loco.StandingReach + loco.TapHeight + GrabOver;

    /// <summary>A cell of a face: its place along the face (in <see cref="Spacing"/>), its distance out (in 0.2 m) and its facing (in eighths).</summary>
    static long Cell(Vector2 p, Vector2 n)
    {
        var t = new Vector2(-n.Y, n.X);
        long along = (long)MathF.Round(Vector2.Dot(p, t) / Spacing);
        long across = (long)MathF.Round(Vector2.Dot(p, n) / 0.2f);
        long facing = (long)MathF.Round((n.X + 1f) * 2f) * 5 + (long)MathF.Round((n.Y + 1f) * 2f);
        return (along & 0xFFFFFF) | (across & 0xFFFFFF) << 24 | facing << 48;
    }

    /// <summary>
    /// A body stood in front of the face met at <paramref name="at"/> (outward normal <paramref name="n"/>), on the
    /// ground there, facing it: the moves the motor would start there, marked along the lip the probe finds.
    /// Answers whether a lip of that face is marked there (newly or already).
    /// </summary>
    static bool Mark(CollisionWorld level, Vector3 body, Vector3 at, Vector2 n, in Locomotion loco, List<ReachMark> into, OverPaths? runVaults = null)
    {
        float r = loco.Radius, step = loco.StepHeight, minStand = loco.MinStandNormalZ - 2e-5f;
        var stand = new Vector2(at.X, at.Y) + n * (r + StandOff);
        float from = MathF.Max(at.Z, body.Z + step) + 0.05f;
        if (level.Ray(new Vector3(stand, from), -Vector3.UnitZ, from - body.Z + 2f) is not { } g
            || level.Normal(g.Triangle).Z < minStand) return false;
        var feet = new Vector3(stand, g.Point.Z + BodyMotor.Skin);
        // Room there for a crouched body over the stride (on a stair the next riser stands within its radius, and a
        // body stands there all the same).
        float crouched = MathF.Max(MathF.Min(loco.CrouchHeight, loco.Height), 2f * r);
        float over = MathF.Min(MathF.Max(0f, step), crouched - 2f * r);
        if (level.Overlaps(Capsule.OfBody(feet + new Vector3(0, 0, BodyMotor.Skin + over), r, crouched - over), 1e-4f)) return false;
        var way = -n;

        var moves = ReachMoves.None;
        Obstacle lip = default;
        bool have = false;
        // The ground the body stands on, a skin under its feet: every height is measured from it, as the motor's.
        float ground = feet.Z - BodyMotor.Skin;

        // The moves, one step each in the motor's order; a lip takes the first one's colour and dashes in the others'.
        if (Hop(level, feet, way, loco, ground, out var ob)) Take(ReachMoves.Hop, ob);
        // By pace: the sprint vault at the sprint's full speed, else the run's step-over at the run's — the motor's
        // own tests (BodyMotor.SprintVaultAhead, BodyMotor.StepOverAhead), both shown in the hurdle's orange.
        if (BodyMotor.SprintVaultAhead(level, feet, way, loco.SprintSpeed, loco, out ob, out _)) Take(ReachMoves.Hurdle, ob);
        else if (BodyMotor.StepOverAhead(level, feet, way, loco.RunSpeed, loco, out ob, out _)) Take(ReachMoves.Hurdle, ob);
        var air = FromAir(level, feet, way, loco, ground, out ob);
        if (air != ReachMoves.None) Take(air, ob);
        else if (Grab(level, feet, way, loco, ground, out ob)) Take(ReachMoves.Grab, ob);

        // Nothing starts: a holdable lip just over what the hands reach is marked faintly.
        // (Not looked at first to leave a wall early: a look from that high starts inside whatever overhangs the lip
        // — an arcade over a landing — and reads it as a wall going on up; on 1.1's Old Town.)
        float outTop = GrabTop(loco) + OutBand;
        if (moves == ReachMoves.None
            && TraversalProbe.Read(level, feet, way, loco, r + BodyMotor.CloseReach, outTop + 0.1f, out ob) && ob.HasTop && ob.Holdable)
        {
            float h = ob.Top - ground;
            if (h > step && h <= outTop) Take(ReachMoves.Out, ob);
        }
        if (!have) return false;
        // A thin thing the player's vault clips take is the run's own vault's (light orange), not the sprint vault's; a walk taps it (blue).
        if (runVaults is not null && (moves & ReachMoves.Vault) != 0 && runVaults.For(TraversalKind.Vault, lip.Top - ground) is not null)
            moves = (moves & ~ReachMoves.Hurdle) | ReachMoves.RunVault;
        // The lip of the face met, not of something beyond or before it, nor a corner read across: on the face's
        // plane, facing as it does, and straight ahead of the body (at a face's end the probe's ball meets the corner
        // to one side, and what it reads there is the corner's).
        if (MathF.Abs((lip.Lip.X - at.X) * n.X + (lip.Lip.Y - at.Y) * n.Y) > SameFace
            || lip.LipNormal.X * n.X + lip.LipNormal.Y * n.Y < SameFacing
            || MathF.Abs((lip.Lip.X - stand.X) * -n.Y + (lip.Lip.Y - stand.Y) * n.X) > Ahead) return false;
        // Just past a face's end the probe's ball still meets its corner nearly ahead: the face itself must be there,
        // under the lip, straight ahead (or, with nothing under the edge, a thin plate's, met square).
        if (level.Ray(new Vector3(stand, lip.Top - 0.03f), new Vector3(way, 0f), r + BodyMotor.CloseReach + 0.2f) is { } under)
        {
            if (under.FaceNormal.X * n.X + under.FaceNormal.Y * n.Y < SameFacing && -(under.FaceNormal.X * n.X + under.FaceNormal.Y * n.Y) < SameFacing) return false;
        }
        else if (lip.LipNormal.X * n.X + lip.LipNormal.Y * n.Y < 0.999f) return false;

        // The mark: along the lip, snapped to the cells so that neighbours join, a little above and out from it.
        var ln = new Vector2(lip.LipNormal.X, lip.LipNormal.Y);
        float lnl = ln.Length();
        ln = lnl > 1e-4f ? ln / lnl : n;
        var t = new Vector2(-ln.Y, ln.X);
        var p = new Vector2(lip.Lip.X, lip.Lip.Y);
        float s = Vector2.Dot(p, t);
        var mid = p + t * (MathF.Round(s / Spacing) * Spacing - s) + ln * Outward;
        for (int i = 0; i < into.Count; i++)
        {
            var o = into[i];
            var om = o.Middle;
            if (Vector2.DistanceSquared(new Vector2(om.X, om.Y), mid) < 0.25f * Spacing * Spacing
                && MathF.Abs(om.Z - (lip.Top + Lift)) < 0.08f && o.Normal.X * ln.X + o.Normal.Y * ln.Y > 0.9f) return true;
        }
        var half = t * (0.5f * Spacing);
        float z = lip.Top + Lift;
        into.Add(new ReachMark(moves, new Vector3(mid - half, z), new Vector3(mid + half, z), new Vector3(ln, 0f), lip.Top - ground));
        return true;

        void Take(ReachMoves m, in Obstacle o)
        {
            moves |= m;
            if (!have) { lip = o; have = true; }
        }
    }

    /// <summary>A tap on the ground: a hop up onto a top that can be stood on, above the stride to the tap's jump and the knees (<c>BodyMotor.TryHopUp</c>).</summary>
    static bool Hop(CollisionWorld level, Vector3 feet, Vector2 way, in Locomotion loco, float ground, out Obstacle ob)
    {
        float hop = BodyMotor.HopReach(loco);
        if (hop <= loco.StepHeight || !TraversalProbe.Read(level, feet, way, loco, loco.Radius + BodyMotor.CloseReach, hop + 0.25f, out ob)
            || !ob.HasTop || !ob.Standable) { ob = default; return false; }
        float h = ob.Top - ground;
        return h > loco.StepHeight && h <= hop + BodyMotor.Within;
    }

    /// <summary>The tap's jump in the air: a vault, a climb onto or a mantle (<c>BodyMotor.TryFromAir</c>), read from the ground jumped from.</summary>
    static ReachMoves FromAir(CollisionWorld level, Vector3 feet, Vector2 way, in Locomotion loco, float ground, out Obstacle ob)
    {
        var from = new Vector3(feet.X, feet.Y, ground);
        if (!TraversalProbe.Read(level, from, way, loco, loco.Radius + BodyMotor.CloseReach, loco.MantleMax + 0.2f, out ob) || !ob.HasTop) return ReachMoves.None;
        float h = ob.Top - ground, within = BodyMotor.Within;
        if (h > loco.StepHeight && h <= loco.VaultMax + within && ob.Depth < loco.MantleMinDepth - 1e-4f && ob.Beyond) return ReachMoves.Vault;
        if (ob.Standable && h >= loco.VaultMin - within && h <= loco.VaultMax + within) return ReachMoves.ClimbOnto;
        if (ob.Standable && h > loco.VaultMax + within && h <= loco.MantleMax + within) return ReachMoves.Mantle;
        return ReachMoves.None;
    }

    /// <summary>
    /// The tap's jump's grab, read from the feet risen as when the hands meet a lip (<c>BodyMotor.TryFromAir</c>): the
    /// hands at the lip somewhere in the jump (from the mantle band's top to what they reach at its top) and a hanging
    /// body fitting under it.
    /// </summary>
    static bool Grab(CollisionWorld level, Vector3 feet, Vector2 way, in Locomotion loco, float ground, out Obstacle ob)
    {
        ob = default;
        if (loco.StandingReach <= 0f) return false;
        // Risen at least a little, and far enough that the read (to the reach and 0.3 m over the feet, as the motor's)
        // sees what the hands reach at the jump's top.
        float lift = MathF.Min(MathF.Max(0f, loco.TapHeight), MathF.Max(GrabLift, GrabTop(loco) - loco.StandingReach - 0.3f + 0.05f));
        var risen = feet + new Vector3(0, 0, lift);
        if (!TraversalProbe.Read(level, risen, way, loco, loco.Radius + BodyMotor.GrabReach, loco.StandingReach + 0.3f, out ob) || !ob.HasTop || !ob.Holdable) return false;
        float h = ob.Top - ground;
        return h > loco.MantleMax - 1e-4f && h <= GrabTop(loco) && HangFits(level, ob, risen, loco);
    }

    /// <summary>Whether a body hangs from <paramref name="lip"/> (<c>BodyMotor.TryGrab</c>): at arm's length, else with the arms bent at the height the feet have risen to.</summary>
    static bool HangFits(CollisionWorld level, in Obstacle lip, Vector3 risen, in Locomotion loco)
    {
        float r = loco.Radius;
        var hang = new Vector3(lip.Lip.X + lip.LipNormal.X * (r + 0.01f), lip.Lip.Y + lip.LipNormal.Y * (r + 0.01f), lip.Top - loco.StandingReach);
        if (Fits(level, hang, r, loco.Height)) return true;
        hang.Z = MathF.Max(hang.Z, risen.Z);
        return Fits(level, hang, r, loco.Height);
    }

    static bool Fits(CollisionWorld level, Vector3 feet, float radius, float height) =>
        !level.Overlaps(Capsule.OfBody(feet + new Vector3(0, 0, BodyMotor.Skin * 0.5f), radius, height), 1e-4f);
}
