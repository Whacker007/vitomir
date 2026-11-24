// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips hang, shimmy, brace, pull-up, drop and grabs carry their capsule from the hang's place under the
// edge (CONVENTIONS, Notes, traverse.hang, shimmy, grab, loops_from).
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>the ledge on clips</b> (the models repository's <see cref="LedgePaths"/>), for a body given them (the player's <c>PersonLedge</c>, with the
/// switch on): <b>the capsule is placed and moved by the clips' paths, not by the motor's own</b>.
/// <list type="bullet">
/// <item><b>The place</b>: in the hang the capsule stands the notes' <c>edge_fwd_m</c> back from the real edge (the probe's lip) and <c>edge_up_m</c> below its
/// top; every ledge clip's path is relative to that place (the braced hang's rows sit at their own offset). The motor's tests of room take the capsule kept
/// a radius and two skins clear of the face under the edge (the man's place is a centimetre nearer the face than his radius: the drawn body is where
/// the notes say, the tests where the capsule fits).</item>
/// <item><b>Which hang</b>: braced where there is a wall under the edge for the feet (<see cref="WallUnder"/>: level rays at the braced hang's feet), free
/// where there is not; the grabs' own end hangs, then <c>hang_brace_on</c> or <c>hang_brace_off</c> when the wall says otherwise; the same at the end of
/// every shimmy cycle.</item>
/// <item><b>The shimmy</b>: by whole cycles at the clips' speed; a cycle starts only where the lip goes on under the leading hand to its end and the hanging
/// body has room; at its end it goes on while the sideways wish holds that way, else the hang (the cycle ends in the hang's first frame).</item>
/// <item><b>Pull-up</b> (jump, or a fresh forward, as before; the existing room check, a top that can be stood on, and the clip's path clear for the body
/// above the top) along its path, ending standing on the top. <b>Drop</b> (crouch, cancel, the grip out): its path for its length, then the fall from
/// there, the hang's height its fall's. <b>Push-off</b> backwards: the earlier build's, from where the body hangs.</item>
/// <item><b>The grabs</b>: a tap standing below a lip in <c>grab_jump_up</c>'s band (its face within reach) starts it at once; running at one in
/// <c>grab_run</c>'s band the press is held (<see cref="LedgePress"/>) for the run's left footfall where the face is the clip's own distance ahead (the
/// animation tells when, as for the running vault); falling past a lip with jump pressed or held (the existing detection's condition), <c>grab_fall</c>
/// begins as the capsule comes to its <c>starts_over_hang_m</c> over the hang's place. Each is warped so its hands meet the real edge at
/// <c>contact_s</c>: from the ground the start's difference across and up taken out between its take-off and the contact (the jump a little higher or lower
/// than the clip's own); from the air over <see cref="AirBlend"/>. Anything else that grabs (a jump that meets a lip outside the bands, a charged jump's)
/// and the lowering from an edge are the motor's own moves as before, ending in the clip's hang place.</item>
/// </list>
/// </summary>
public static partial class BodyMotor
{
    /// <summary>A change of clip from a pose that is not the new clip's first frame: the capsule's difference taken out, and the animation's fade, over this long (s).</summary>
    public const float LedgeBlend = 0.15f;

    /// <summary>
    /// How long after the hands' contact a grab whose player holds on flows into the pull-up (s of the clip): the hands planted, the arms' share of a
    /// high catch out first. There should be little or no delay once the hands hit the hang.
    /// </summary>
    public const float FlowAfter = 0.12f;

    /// <summary>Over how long (s) the capsule's difference from the pull-up's path is taken out when it is entered from a grab's settle or a brace.</summary>
    public const float FlowBlend = 0.3f;

    /// <summary>The animation's fade from the grab (or the hang) into the pull-up entered past its start (s).</summary>
    public const float FlowFade = 0.25f;

    /// <summary>Over how much of the pull-up (s of it) the animation's matching entry is brought onto the motor's time.</summary>
    public const float FlowConverge = 0.3f;

    /// <summary>
    /// Where a pull-up entered to go on is entered — 0.1 s before its body begins to rise (its path 3 cm over its start), past the still hang it
    /// begins with (the free one's 0.15 s, the braced one's 0.2 s).
    /// </summary>
    public static float PullUpEntry(LedgeClip c)
    {
        float u0 = c.At(0f).Up;
        for (float t = 0f; t < c.Duration; t += World.TickSeconds)
            if (c.At(t).Up - u0 > 0.03f) return MathF.Max(0f, t - 0.1f);
        return 0f;
    }

    /// <summary>After a drop, how long before a lip can be taken again (s): the old drop's moment.</summary>
    public const float LedgeAgain = 0.3f;

    /// <summary>How far beyond its radius the capsule is kept from the face under the edge in the motor's tests of room (m).</summary>
    public const float FaceClear = 2f * Skin;

    /// <summary>
    /// How far beyond the running grab's own distance from the face a press made running looks for its lip (m): about a run's stride and a half, as the
    /// running vault's (<see cref="VaultLead"/> past the radius, its own face 1.0 m ahead).
    /// </summary>
    public const float GrabLead = 1.9f;

    /// <summary>How far past the radius a press looks for a lip to grab (m): the running grab's start and <see cref="GrabLead"/>, at least <see cref="VaultLead"/>.</summary>
    static float GrabReachAhead(LedgePaths paths, in Locomotion loco) =>
        MathF.Max(VaultLead, (paths.Of(LedgeRole.GrabRun)?.OwnFace ?? 0f) + GrabLead - loco.Radius);

    /// <summary>The pieces a ledge clip's path is checked in before it starts.</summary>
    const int LedgeSamples = 24;

    /// <summary>What a press or a fall would grab: the lip, its top, its height above the ground (m), the way into the wall and the face's distance ahead along it.</summary>
    struct GrabPlan
    {
        public float Top, H, Face;
        public Vector3 Lip, LipNormal;
        public Vector2 In;
    }

    // ---------------------------------------------------------------- the press

    /// <summary>
    /// <b>a press of jump</b> by a body given the ledge's clips (<see cref="BodySystem"/>, before the motor and the jump's squash): true when the press is
    /// a grab's. Standing (slower than <see cref="VaultRunning"/>) below a lip in <c>grab_jump_up</c>'s band whose face is within reach: let go to start it this
    /// tick. Running at a lip in <c>grab_run</c>'s band: held (<see cref="LedgeMove.Pending"/>) until the time the animation gives runs out — the run's footfall
    /// the clip begins on — then let go to start it; let go as a jump if the body leaves the ground first.
    /// </summary>
    public static bool LedgePress(ref LedgeMove ledge, ref MoveIntent given, in Body body, in Locomotion loco, CollisionWorld level, LedgePaths paths)
    {
        const float dt = World.TickSeconds;
        ledge.Go = 0;
        if (ledge.Pending != 0)
        {
            given.Jump = false;
            ledge.Held += dt;
            if (ledge.Wait >= 0f) ledge.Wait -= dt;
            if (body.State != BodyState.Ground)
            {
                ledge.Pending = 0;    // off an edge first: a jump, with the edge's grace
                given.Jump = true;
            }
            else if ((ledge.Wait > -0.5f && ledge.Wait <= 1e-4f) || ledge.Held >= MostHold - 1e-4f)
            {
                ledge.Pending = 0;
                given.Jump = true;
                ledge.Go = 1;
            }
            return true;
        }
        if (!given.Jump || body.State != BodyState.Ground || loco.CanCharge || body.Crouched != 0 || ledge.Clip != 0) return false;
        if (!PlanGrab(level, body.Feet, WayOf(given, body.Yaw), loco, paths, GrabReachAhead(paths, loco), out var plan)) return false;
        var across = new Vector2(body.Velocity.X, body.Velocity.Y);
        float speed = across.Length();
        if (speed >= VaultRunning)
        {
            if (paths.Of(LedgeRole.GrabRun) is not { } run || !run.Serves(plan.H) || Vector2.Dot(across, plan.In) < 0.7f * speed || plan.Face < run.OwnFace - VaultNearer) return false;
            ledge.Pending = 1;
            ledge.Wait = -1f;
            ledge.Held = 0f;
            ledge.Face = plan.Face;
            ledge.Speed = speed;
            given.Jump = false;
            return true;
        }
        if (paths.Of(LedgeRole.GrabStand) is not { } stand || !stand.Serves(plan.H) || plan.Face > loco.Radius + CloseReach + 1e-3f) return false;
        ledge.Go = 1;
        return true;
    }

    /// <summary>A lip the hands can hold ahead along <paramref name="way"/> within <paramref name="reach"/> beyond the radius, up to the highest grab's band: false for none (none, one not to be climbed).</summary>
    static bool PlanGrab(CollisionWorld level, Vector3 feet, Vector2 way, in Locomotion loco, LedgePaths paths, float reach, out GrabPlan plan)
    {
        plan = default;
        float high = paths.HighestGrab;
        if (way.LengthSquared() < 1e-6f || high <= 0f) return false;
        if (!TraversalProbe.Read(level, feet, way, loco, loco.Radius + reach, high + 0.3f, out var ob) || !ob.HasTop || ob.NoClimb || !ob.Holdable) return false;
        var n = new Vector2(ob.LipNormal.X, ob.LipNormal.Y);
        float nl = n.Length();
        if (nl < 1e-4f) return false;
        var inward = -n / nl;
        plan = new GrabPlan
        {
            Top = ob.Top, H = ob.Top - (feet.Z - Skin), Lip = ob.Lip, LipNormal = ob.LipNormal, In = inward,
            Face = Vector2.Dot(new Vector2(ob.Lip.X - feet.X, ob.Lip.Y - feet.Y), inward),
        };
        return true;
    }

    /// <summary>A press let go to start a grab from the ground (<see cref="LedgeMove.Go"/>): the running grab about its own distance from the face, else the standing one within reach.</summary>
    /// <remarks><paramref name="twins"/> (<see cref="AirCommitRules.BracedTwins"/>): the standing grab's braced twin where there is a wall under the edge.</remarks>
    static bool TryLedgeFromGround(in Mover m, ref Body body, ref Traversal move, ref LedgeMove ledge, LedgePaths paths, ref Vector3 feet, in Locomotion loco,
                                   in MoveIntent intent, Vector2 way, ref MotorOutcome o, bool twins = false)
    {
        if (!PlanGrab(m.Level, feet, way, loco, paths, GrabReachAhead(paths, loco), out var plan)) return false;
        LedgeClip? c = null;
        if (paths.Of(LedgeRole.GrabRun) is { } run && run.Serves(plan.H) && plan.Face >= run.OwnFace - VaultNearer - 0.05f && plan.Face <= run.OwnFace + 0.8f) c = run;
        else if (paths.Of(LedgeRole.GrabStand) is { } stand && stand.Serves(plan.H) && plan.Face <= m.R + CloseReach + 1e-3f) c = stand;
        if (c is not null && twins && paths.TwinOf(c) is { } twin && !ReferenceEquals(twin, c)
            && WallUnder(m.Level, paths, HangPlace(paths, plan.Lip, plan.LipNormal, plan.Top), plan.In)) c = twin;
        if (c is null || !StartGrab(m, ref body, ref move, ref ledge, paths, c, plan, ref feet, loco, intent, 0f, air: false, ref o)) return false;
        o.JumpAttempted = intent.Jump;
        o.AttemptOnGround = true;
        return true;
    }

    /// <summary>
    /// Falling past a lip with jump pressed or held (or from a jump, falling): <c>grab_fall</c> begun as the capsule comes to its <c>starts_over_hang_m</c>
    /// over the hang's place (entered where its path's height matches when the body is already lower, up to the hands' contact), when the hang fits.
    /// </summary>
    static bool TryGrabFall(in Mover m, ref Body body, ref Traversal move, ref LedgeMove ledge, LedgePaths paths, ref Vector3 feet, in Locomotion loco,
                            in MoveIntent intent, Vector2 way, ref MotorOutcome o)
    {
        const float dt = World.TickSeconds;
        if (body.State != BodyState.Air || body.Velocity.Z >= 0f || move.Kind == TraversalKind.Drop || paths.Of(LedgeRole.GrabFall) is not { } c) return false;
        bool fromJump = body.Jumped != 0;
        if (!fromJump && body.JumpGrace <= Eps) return false;
        if (float.IsNaN(c.StartsOver) || float.IsNaN(c.ContactS)) return false;
        float look = c.EdgeUp + 0.3f;
        if (!TraversalProbe.Read(m.Level, feet, way, loco, MathF.Max(m.R + GrabReach, c.EdgeFwd - c.StartsOff + 0.1f), look, out var ob)
            || !ob.HasTop || ob.NoClimb || !ob.Holdable) return false;
        // From a jump, what the jump's moves take (a vault, a climb, a mantle: to the mantle band's top above the ground left) is theirs.
        if (fromJump && ob.Top - (body.TakeOffZ - Skin) <= MovesReach(body, loco).MantleMax + Within) return false;
        float over = feet.Z - (ob.Top - c.EdgeUp);
        float fall = -body.Velocity.Z * dt;
        if (over > c.StartsOver + fall + 1e-4f) return false;   // not yet
        // Where its path is as high over the place as the capsule is (from its first frame to the hands' contact): too late after.
        float entry = 0f;
        if (over < c.StartsOver)
        {
            entry = float.NaN;
            float prev = c.At(0f).Up;
            for (float t = dt; t <= c.ContactS + 1e-4f; t += dt)
            {
                float u = c.At(t).Up;
                if (u <= over) { entry = t - dt + dt * (prev - over) / MathF.Max(1e-6f, prev - u); break; }
                prev = u;
            }
            if (float.IsNaN(entry)) return false;
        }
        var n = new Vector2(ob.LipNormal.X, ob.LipNormal.Y);
        float nl = n.Length();
        if (nl < 1e-4f) return false;
        var inward = -n / nl;
        var plan = new GrabPlan
        {
            Top = ob.Top, H = ob.Top - (body.TakeOffZ - Skin), Lip = ob.Lip, LipNormal = ob.LipNormal, In = inward,
            Face = Vector2.Dot(new Vector2(ob.Lip.X - feet.X, ob.Lip.Y - feet.Y), inward),
        };
        // Faster than the clip was made falling at: its fall to the hands' contact played at the fall's speed (no slower than its own, at most thrice).
        float rate = float.IsNaN(c.FallSpeed) || c.FallSpeed <= 0f ? 1f : Math.Clamp(-body.Velocity.Z / c.FallSpeed, 1f, 3f);
        return StartGrab(m, ref body, ref move, ref ledge, paths, c, plan, ref feet, loco, intent, MathF.Min(entry, c.ContactS), air: true, ref o, rate);
    }

    /// <summary>The hang's place for a lip (the probe's, its top at <paramref name="top"/>): the notes' <c>edge_fwd_m</c> out from it, <c>edge_up_m</c> below the top.</summary>
    public static Vector3 HangPlace(LedgePaths paths, Vector3 lip, Vector3 lipNormal, float top) =>
        new(lip.X + lipNormal.X * paths.EdgeFwd, lip.Y + lipNormal.Y * paths.EdgeFwd, top - paths.EdgeUp);

    /// <summary>Whether a standing body hangs at <paramref name="place"/> (kept <see cref="FaceClear"/> beyond its radius from the face under the lip at <paramref name="lip"/>).</summary>
    static bool HangFits(CollisionWorld level, in Locomotion loco, Vector3 place, Vector3 lip, Vector3 lipNormal) =>
        Fits(level, ClearOfFace(loco, place, lip, lipNormal), loco.Radius, loco.Height);

    /// <summary><paramref name="place"/> kept <see cref="FaceClear"/> beyond the radius out from the face under the lip at <paramref name="lip"/>.</summary>
    static Vector3 ClearOfFace(in Locomotion loco, Vector3 place, Vector3 lip, Vector3 lipNormal)
    {
        var n = new Vector2(lipNormal.X, lipNormal.Y);
        float back = Vector2.Dot(new Vector2(place.X - lip.X, place.Y - lip.Y), n);
        float need = loco.Radius + FaceClear;
        return back < need ? place + new Vector3(n * (need - back), 0f) : place;
    }

    /// <summary>
    /// Where the motor's own grab or lowering takes a body given the ledge's clips: the clip's hang place kept clear of the face (its own sweep would
    /// meet the face there; the hang's blend takes the rest, the man's 1.2 cm).
    /// </summary>
    public static Vector3 OwnMoveHang(LedgePaths paths, in Locomotion loco, Vector3 lip, Vector3 lipNormal, float top) =>
        ClearOfFace(loco, HangPlace(paths, lip, lipNormal, top), lip, lipNormal);

    /// <summary>
    /// Starts a grab's clip from <paramref name="feet"/> at <paramref name="entry"/> s of it (<paramref name="air"/>: in the air, the difference taken out over
    /// <see cref="AirBlend"/>; else between its take-off and the hands' contact), when the hang fits and its path is clear; runs its first tick.
    /// </summary>
    static bool StartGrab(in Mover m, ref Body body, ref Traversal move, ref LedgeMove ledge, LedgePaths paths, LedgeClip c, in GrabPlan plan, ref Vector3 feet,
                          in Locomotion loco, in MoveIntent intent, float entry, bool air, ref MotorOutcome o, float rate = 1f)
    {
        var place = HangPlace(paths, plan.Lip, plan.LipNormal, plan.Top);
        if (!HangFits(m.Level, loco, place, plan.Lip, plan.LipNormal)) return false;
        var next = new LedgeMove
        {
            Clip = paths.IndexOf(c) + 1, Time = entry, Age = 0f, Braced = c.Braced ? 1 : 0, Place = place, In = plan.In, Top = plan.Top,
            Floor = air ? float.NegativeInfinity : feet.Z, Fade = 1, Rate = rate,
        };
        next.Delta = feet - LedgeRaw(next, c, entry);
        if (air)
        {
            // Over AirBlend, or by the hands' contact when that comes sooner (a late entry, a fast fall), two ticks at the least.
            float by = float.IsNaN(c.ContactS) ? AirBlend : Math.Clamp((c.ContactS - entry) / MathF.Max(1f, rate), 2f * World.TickSeconds, AirBlend);
            (next.AcrossFrom, next.AcrossTo, next.UpFrom, next.UpTo) = (0f, by, 0f, by);
        }
        else
        {
            float off = float.IsNaN(c.TakeoffS) ? 0f : c.TakeoffS, on = float.IsNaN(c.ContactS) ? c.Duration : c.ContactS;
            (next.AcrossFrom, next.AcrossTo, next.UpFrom, next.UpTo) = (off, MathF.Max(off, on), off, MathF.Max(off, on));
        }
        // The path clear for the tucked body (above the ground it starts on, clear of the face) to the hang.
        var prev = feet;
        for (int i = 1; i <= LedgeSamples; i++)
        {
            float t = entry + (c.Duration - entry) * i / LedgeSamples;
            var p = LedgePoint(next, c, t, t - entry);
            if (LedgeSwept(m, loco, next, paths, prev, p, TuckedHeight(loco), clear: true, out _)) return false;
            prev = p;
        }
        if (move.Kind != TraversalKind.None) { o.MoveEnded = move.Kind; o.MoveCompleted = true; }
        move = new Traversal
        {
            Kind = TraversalKind.Grab, Progress = 0f, Seconds = MathF.Max(c.Duration - entry, World.TickSeconds), From = feet,
            To = LedgePoint(next, c, c.Duration, c.Duration - entry), Lip = plan.Lip, LipNormal = plan.LipNormal, Height = plan.H,
        };
        ledge = next;
        o.MoveStarted = TraversalKind.Grab;
        o.GripFeet = feet;
        body.State = BodyState.Traversing;
        body.Stoop = 0f;
        body.Crouched = 0;
        body.Coyote = 0f; body.JumpBuffer = 0f; body.Rising = 0; body.Jumped = 0; body.Charging = 0;
        body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1; body.GroundSeconds = 0f;
        body.Yaw = DetMath.WrapAngle(DetMath.Atan2(plan.In.Y, plan.In.X));
        GrabTick(m, ref body, ref move, ref ledge, paths, c, ref feet, loco, intent, ref o, first: true,
                 ref System.Runtime.CompilerServices.Unsafe.NullRef<AirCommit>(), null);
        return true;
    }

    // ---------------------------------------------------------------- the path

    /// <summary>The level unit way to the left of a body facing <paramref name="inward"/>.</summary>
    static Vector2 LeftOf(Vector2 inward) => new(-inward.Y, inward.X);

    /// <summary>The clip's path at <paramref name="t"/> s laid on the hang's place: forward along the way in, to the left, up.</summary>
    internal static Vector3 LedgeRaw(in LedgeMove l, LedgeClip c, float t)
    {
        var (f, u, s) = c.At(t);
        var left = LeftOf(l.In);
        return new Vector3(l.Place.X + l.In.X * f + left.X * s, l.Place.Y + l.In.Y * f + left.Y * s, l.Place.Z + u);
    }

    /// <summary>
    /// <b>where the feet are</b> in a ledge clip at <paramref name="t"/> s of it, <paramref name="age"/> s after it began: its path laid on the hang's place,
    /// and the difference at its start taken out across and up over their windows.
    /// </summary>
    public static Vector3 LedgePoint(in LedgeMove l, LedgeClip c, float t, float age)
    {
        float wa = l.AcrossTo > l.AcrossFrom + 1e-5f ? Smooth((age - l.AcrossFrom) / (l.AcrossTo - l.AcrossFrom)) : age >= l.AcrossTo ? 1f : 0f;
        float wu = l.UpTo > l.UpFrom + 1e-5f ? Smooth((age - l.UpFrom) / (l.UpTo - l.UpFrom)) : age >= l.UpTo ? 1f : 0f;
        var p = LedgeRaw(l, c, t);
        return new Vector3(p.X + l.Delta.X * (1f - wa), p.Y + l.Delta.Y * (1f - wa), p.Z + l.Delta.Z * (1f - wu));
    }

    /// <summary>
    /// Another clip from <paramref name="time"/> s of it, the feet where they are: the difference from its path taken out over <paramref name="blend"/> s (0: none),
    /// the animation fading into it when <paramref name="fade"/>.
    /// </summary>
    static void Switch(ref LedgeMove l, LedgePaths paths, LedgeClip c, float time, Vector3 feet, float blend, bool fade)
    {
        l.Clip = paths.IndexOf(c) + 1;
        l.Time = time;
        l.Age = 0f;
        l.Delta = blend > 0f ? feet - LedgeRaw(l, c, time) : Vector3.Zero;
        (l.AcrossFrom, l.AcrossTo, l.UpFrom, l.UpTo) = (0f, blend, 0f, blend);
        l.Fade = fade ? 1 : 0;
        l.Rate = 0f;
    }

    /// <summary>A point kept a radius and <see cref="FaceClear"/> out from the face under the edge (the line through the edge across the hang's place), and over the floor.</summary>
    static Vector3 Cleared(in LedgeMove l, LedgePaths paths, Vector3 p, float r)
    {
        var edge = new Vector2(l.Place.X + l.In.X * paths.EdgeFwd, l.Place.Y + l.In.Y * paths.EdgeFwd);
        float back = Vector2.Dot(edge - new Vector2(p.X, p.Y), l.In);
        float need = r + FaceClear;
        if (back < need) { p.X -= l.In.X * (need - back); p.Y -= l.In.Y * (need - back); }
        if (p.Z < l.Floor) p.Z = l.Floor;
        return p;
    }

    /// <summary>
    /// Whether the body of <paramref name="height"/> moving from <paramref name="from"/> to <paramref name="to"/> in a ledge clip meets anything, and how far it
    /// gets (a share): swept from where it fits (<paramref name="clear"/>: kept out from the face and over the floor).
    /// </summary>
    static bool LedgeSwept(in Mover m, in Locomotion loco, in LedgeMove l, LedgePaths paths, Vector3 from, Vector3 to, float height, bool clear, out float share)
    {
        share = 1f;
        var a = clear ? Cleared(l, paths, from, m.R) : from;
        var b = clear ? Cleared(l, paths, to, m.R) : to;
        var d = b - a;
        float len = d.Length();
        if (len < 1e-7f) return false;
        var mover = new Mover(m.Level, m.Others, m.Self, loco, height);
        if (!Sweep(mover, a, d / len, len + Skin, out var hit) || hit.Distance >= len - 1e-4f) return false;
        share = MathF.Max(0f, hit.Distance - Skin) / len;
        return true;
    }

    // ---------------------------------------------------------------- the wall under the edge

    /// <summary>
    /// <b>whether there is a wall under the edge for the feet</b> of a body hanging at <paramref name="place"/> facing <paramref name="inward"/>: at each foot
    /// of the braced hang's first frame (<see cref="LedgePaths.BracedFeet"/>), a level ray in from 0.3 m out meets an upright face looking out (within about
    /// 45°) between <see cref="WallBefore"/> before the ball and <see cref="WallBeyond"/> beyond it.
    /// </summary>
    public static bool WallUnder(CollisionWorld level, LedgePaths paths, Vector3 place, Vector2 inward)
    {
        if (paths.BracedFeet.Length == 0 || paths.Of(LedgeRole.Hang, braced: true) is not { } braced) return false;
        var (bf, bu, _) = braced.At(0f);
        var left = LeftOf(inward);
        var dir = new Vector3(inward, 0f);
        foreach (var foot in paths.BracedFeet)
        {
            var ball = new Vector3(place.X + inward.X * (bf + foot.X) + left.X * foot.Y, place.Y + inward.Y * (bf + foot.X) + left.Y * foot.Y, place.Z + bu + foot.Z);
            const float from = 0.3f;
            var start = ball - dir * from;
            if (level.Ray(start, dir, from + WallBeyond) is not { } hit) return false;
            if (hit.Distance < from - WallBefore) return false;
            if (hit.Normal.X * -inward.X + hit.Normal.Y * -inward.Y < 0.7f || MathF.Abs(hit.Normal.Z) > 0.35f) return false;
        }
        return true;
    }

    /// <summary>How far before a braced foot's ball the wall may stand (into the body) and how far beyond it (out of the foot's reach) for a wall under the edge (m).</summary>
    public const float WallBefore = 0.1f, WallBeyond = 0.2f;

    // ---------------------------------------------------------------- a tick

    /// <summary>
    /// <b>one tick of a body in a ledge clip</b> (<see cref="LedgeMove.Clip"/>): a grab along its path to the hang; the hang, a shimmy's cycle or a brace,
    /// with the hang's controls; a pull-up onto the top; a drop to the fall.
    /// </summary>
    /// <remarks><paramref name="k"/> and <paramref name="rules"/>, the commit point's (a null reference and null for none): a catch's flight, braced by default.</remarks>
    /// <remarks><paramref name="h"/> and <paramref name="hr"/>, the hang's corners and leaps (a null reference and null for none: the earlier build's).</remarks>
    static void LedgeTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref Vector3 feet, in Locomotion loco, in MoveIntent intent,
                          ref MotorOutcome o, ref AirCommit k, AirCommitRules? rules, ref HangMove h, HangRules? hr)
    {
        if (paths.At(l.Clip - 1) is not { } c)
        {
            l = default;
            Abort(m, ref body, ref move, ref feet, loco, ref o);
            return;
        }
        body.Yaw = DetMath.WrapAngle(DetMath.Atan2(l.In.Y, l.In.X));   // the clip is drawn facing the wall
        switch (c.Role)
        {
            case LedgeRole.GrabStand or LedgeRole.GrabRun or LedgeRole.GrabFall:
                // A catch committed to is not let go before the hands' contact.
                if (intent.Cancel && !(rules is not null && !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref k) && k.Catch == 1 && l.Age < k.Contact))
                {
                    // Cancel lets go, as of an old grab.
                    o.Cancelled = true;
                    l = default;
                    LetGo(m, ref body, ref move, feet, ref o);
                    return;
                }
                GrabTick(m, ref body, ref move, ref l, paths, c, ref feet, loco, intent, ref o, first: false, ref k, rules);
                return;
            case LedgeRole.PullUp: PullUpTick(m, ref body, ref move, ref l, paths, c, ref feet, loco, intent, ref o); return;
            case LedgeRole.Drop: DropTick(m, ref body, ref move, ref l, paths, c, ref feet, loco, ref o); return;
            case LedgeRole.Leap or LedgeRole.Corner when hr is not null: HangMoveTick(m, ref body, ref move, ref l, paths, c, ref feet, loco, intent, ref o, ref h, hr); return;
            default: HangClipTick(m, ref body, ref move, ref l, paths, c, ref feet, loco, intent, ref o, rules, ref h, hr); return;
        }
    }

    /// <summary>The clip's time a tick on (to its end; a loop round), its age with it.</summary>
    static void Tick(ref LedgeMove l, LedgeClip c)
    {
        const float dt = World.TickSeconds;
        l.Age += dt;
        float step = dt;
        if (l.Rate > 0f && !float.IsNaN(c.ContactS) && l.Time < c.ContactS)
        {
            // A falling grab to the hands' contact at the fall's own speed (the clip's made falling at its fall_speed_m_s), its own after.
            float fast = (c.ContactS - l.Time) / l.Rate;
            step = fast >= dt ? dt * l.Rate : c.ContactS - l.Time + (dt - fast);
        }
        else if (l.Rate > 0f && c.Role == LedgeRole.PullUp) step = dt * l.Rate;   // a pull-up going on, faster than its own
        l.Time += step;
        if (c.Loops) { if (l.Time >= c.Duration) l.Time -= c.Duration; }
        else if (l.Time > c.Duration - 1e-4f) l.Time = c.Duration;
    }

    /// <summary>A grab along its path; the hands' contact told; at its end the hang (its own, then a brace when the wall says otherwise), or a pull-up when forward or jump is held.</summary>
    /// <remarks>
    /// A catch's (<paramref name="k"/>, the commit point's; a null reference for none) along its own flight (<see cref="CatchPoint"/>, <see cref="CatchTime"/>);
    /// and, with <paramref name="rules"/>, braced by default: a grab ending in the free hang where there is a wall under the edge goes into the brace
    /// <see cref="AirCommitRules.BraceOverlap"/> before its end, blended over <see cref="BraceBlend"/>, with no free hang between.
    /// </remarks>
    static void GrabTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref Vector3 feet, in Locomotion loco,
                         in MoveIntent intent, ref MotorOutcome o, bool first, ref AirCommit k, AirCommitRules? rules)
    {
        const float dt = World.TickSeconds;
        float before = first ? l.Time - 1e-6f : l.Time;
        bool catching = rules is not null && !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref k) && k.Catch == 1;
        Vector3 p;
        if (catching)
        {
            // A catch's clip time from its age since the commit (its own curve to the contact), the capsule on its flight.
            l.Age += dt;
            l.Time = CatchTime(c, k.Entry, k.Contact, l.Age);
            p = CatchPoint(l, paths, c, k, l.Time, l.Age);
            if (l.Age >= k.Contact + CatchSettle - 1e-5f) k.Catch = 0;   // settled: the clip's own path from here (no difference left to take out)
        }
        else
        {
            Tick(ref l, c);
            p = LedgePoint(l, c, l.Time, l.Age);
        }
        if (LedgeSwept(m, loco, l, paths, feet, p, TuckedHeight(loco), clear: true, out float share))
        {
            feet += (p - feet) * share;
            l = default;
            Abort(m, ref body, ref move, ref feet, loco, ref o);
            return;
        }
        body.Velocity = (p - feet) / dt;
        feet = p;
        move.Progress = MathF.Min(1f, 1f - (c.Duration - l.Time) / move.Seconds);
        if (!float.IsNaN(c.ContactS) && before < c.ContactS && l.Time >= c.ContactS - 1e-5f)
        {
            // The hands on the edge: the lip held (LedgeGrabbed).
            o.Grabbed = true;
            o.GripLip = move.Lip;
            o.GripHeight = move.Height;
            if (o.GripFeet == Vector3.Zero) o.GripFeet = move.From;
        }
        // Holding on (forward toward the wall, jump held, or jump pressed during the grab after its first tick: read every tick of it) — into the pull-up as soon as the
        // hands are planted (FlowAfter past the contact; a high catch's arms' share out first), past its still start, faster; no settle, brace or hang.
        if (rules is { Climb: true } && !float.IsNaN(c.ContactS))
        {
            if (intent.Jump && !first) l.Want = 1;   // not the press that began it (a tap from the ground)
            bool arms = catching && k.Catch == 1 && k.Arms > 0f;
            if (!arms && l.Time >= c.ContactS + FlowAfter - 1e-5f && (l.Want != 0 || body.JumpGrace > Eps || ForwardTo(intent, move.LipNormal)))
            {
                int was = catching ? k.Catch : 0;
                if (catching) k.Catch = 0;
                if (TryLedgePullUp(m, ref body, ref move, ref l, paths, ref feet, loco, ref o, c.Braced, rules.PullUpRate, FlowBlend))
                {
                    o.MoveEnded = TraversalKind.Grab;
                    return;
                }
                if (catching) k.Catch = was;
            }
        }
        // Braced by default: a grab that ends free, a wall under the edge — into the brace before its end, blended (no free hang between).
        bool overlap = rules is { BraceOverlap: > 0f } && !c.Braced && !float.IsNaN(c.ContactS) && l.Time > c.ContactS && l.Time < c.Duration
                       && l.Time >= c.Duration - rules.BraceOverlap && !(catching && k.Catch == 1) && paths.Of(LedgeRole.BraceOn) is not null
                       && WallUnder(m.Level, paths, l.Place, l.In);
        if (l.Time < c.Duration && !overlap) return;
        o.MoveEnded = TraversalKind.Grab; o.MoveCompleted = true;
        BeginLedgeHang(m, ref body, ref move, ref l, paths, c.Braced, feet, loco, ref o, from: c);
        if (overlap && paths.At(l.Clip - 1) is { Role: LedgeRole.BraceOn })
        {
            // The brace from the grab's pose: the animation's fade and the capsule's difference over BraceBlend.
            l.Fade = 3;
            (l.AcrossTo, l.UpTo) = (BraceBlend, BraceBlend);
        }
        // As the old grab's end: forward or jump held (jump with the grace) pulls up at once where there is room.
        if ((body.JumpGrace > Eps || ForwardTo(intent, move.LipNormal)) && TryLedgePullUp(m, ref body, ref move, ref l, paths, ref feet, loco, ref o))
            o.MoveEnded = TraversalKind.Grab;
    }

    /// <summary>
    /// The body hangs from the edge at the hang's place on its clip's hang loop (<paramref name="braced"/> or free), from its first frame when it comes from a
    /// clip that ends on it (<paramref name="from"/>), faded in otherwise; then a brace on or off when the wall under the edge says the other hang.
    /// </summary>
    static void BeginLedgeHang(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, bool braced, Vector3 feet, in Locomotion loco,
                               ref MotorOutcome o, LedgeClip? from)
    {
        var hang = paths.Of(LedgeRole.Hang, braced) ?? paths.Of(LedgeRole.Hang, !braced)!;
        Switch(ref l, paths, hang, 0f, feet, LedgeBlend, fade: from is null);
        l.Braced = hang.Braced ? 1 : 0;
        l.Way = 0;
        l.Floor = float.NegativeInfinity;
        var lip = move.Lip;
        var n = move.LipNormal;
        body.State = BodyState.Hanging;
        body.Crouched = 0;
        body.Velocity = Vector3.Zero;
        body.ForwardArmed = 0;
        body.AirSeconds = 0f;
        body.Shimmy = 0;
        move = new Traversal
        {
            Kind = TraversalKind.Hang, Seconds = loco.GripSeconds > 0f ? loco.GripSeconds : float.PositiveInfinity,
            From = feet, To = feet, Lip = lip, LipNormal = n, Height = lip.Z - feet.Z,
        };
        o.MoveStarted = TraversalKind.Hang;
        Brace(m, ref l, paths, feet, fade: from is null);
    }

    /// <summary>
    /// A brace on (or off) begun when the wall under the edge says the other hang than the one hung in (from the hang's first frame: no fade, unless
    /// <paramref name="fade"/>: the hang itself was to be faded in, after the motor's own grab or lowering).
    /// </summary>
    static bool Brace(in Mover m, ref LedgeMove l, LedgePaths paths, Vector3 feet, bool fade = false)
    {
        bool wall = WallUnder(m.Level, paths, l.Place, l.In);
        if (wall == (l.Braced != 0)) return false;
        if (paths.Of(wall ? LedgeRole.BraceOn : LedgeRole.BraceOff) is not { } brace) return false;
        Switch(ref l, paths, brace, 0f, feet, LedgeBlend, fade);
        return true;
    }

    /// <summary>
    /// A tick of the hang, a shimmy's cycle or a brace: crouch pressed, cancel or the grip out lets go (the drop); jump with back held pushes off;
    /// jump or a fresh forward asks for the pull-up (taken in the hang); the sideways wish shimmies by whole cycles.
    /// </summary>
    /// <remarks>
    /// <paramref name="rules"/>' <see cref="AirCommitRules.Climb"/>: a pull-up asked in a brace on or off is taken at once (the earlier build's waited for the hang),
    /// as from a grab going on; in the hang, at its own rate past its still start; either way faded in at its matching phase.
    /// </remarks>
    static void HangClipTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref Vector3 feet, in Locomotion loco,
                             in MoveIntent intent, ref MotorOutcome o, AirCommitRules? rules = null) =>
        HangClipTick(m, ref body, ref move, ref l, paths, c, ref feet, loco, intent, ref o, rules, ref System.Runtime.CompilerServices.Unsafe.NullRef<HangMove>(), null);

    /// <remarks>
    /// The hang's corners and leaps (<paramref name="hr"/>; null: the earlier hang's): from the braced hang (or a braced shimmy's cycle) crouch pressed with use held
    /// leaps down (none to leap to: the drop, a lip caught on the way down with use held), jump with back held leaps back (none: the push-off); in the hang jump
    /// with a side held leaps that way (none: as before), jump (or forward with it) pulls up where it can, else leaps up; a shimmy into a corner goes round it.
    /// After a corner the wish that went into it keeps the shimmy going while it holds (<see cref="HangMove.Carry"/>).
    /// </remarks>
    static void HangClipTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref Vector3 feet, in Locomotion loco,
                             in MoveIntent intent, ref MotorOutcome o, AirCommitRules? rules, ref HangMove h, HangRules? hr)
    {
        const float dt = World.TickSeconds;
        body.AirSeconds += dt;
        bool limited = loco.GripSeconds < float.PositiveInfinity && loco.GripSeconds > 0f;
        move.Seconds = loco.GripSeconds > 0f ? loco.GripSeconds : float.PositiveInfinity;
        move.Progress = limited ? MathF.Min(1f, body.AirSeconds / loco.GripSeconds) : 0f;
        bool carried = hr is not null && Carried(ref h, intent);   // the wish that went round a corner, still held: the shimmy's, not forward or back
        bool forward = !carried && ForwardTo(intent, move.LipNormal);
        if (!forward) body.ForwardArmed = 1;
        bool leaps = hr is { Leaps: true } && c.Role is LedgeRole.Hang or LedgeRole.Shimmy;   // from the hang or a shimmy's cycle
        if ((intent.Crouch && body.CrouchWas == 0) || intent.Cancel || (limited && body.AirSeconds >= loco.GripSeconds))
        {
            // Crouch with use held: the leap down where there is a lip to leap to; else the drop, and a lip caught on the way down (no wait after it).
            if (leaps && !intent.Cancel && intent.Crouch && intent.UseHeld && !(limited && body.AirSeconds >= loco.GripSeconds)
                && TryLeap(m, ref body, ref move, ref l, paths, ref feet, loco, HangLeap.Down, ref o, ref h, hr!)) return;
            if (intent.Cancel) o.Cancelled = true;
            StopShimmy(ref body, move, feet, ShimmyStop.Left, ref o);
            StartDrop(m, ref body, ref move, ref l, paths, ref feet, loco, ref o);
            if (hr is not null && intent.UseHeld && !intent.Cancel && l.Clip > 0) l.Want = DropCatches;
            return;
        }
        if (intent.Jump && !carried && BackFrom(intent, move.LipNormal))
        {
            // The leap back to a facing ledge where there is one.
            if (leaps && TryLeap(m, ref body, ref move, ref l, paths, ref feet, loco, HangLeap.Back, ref o, ref h, hr!)) return;
            // The earlier build's push-off backwards, from where the body hangs (no clip: the old drop's stand-in).
            l = default;
            PushOffLip(m, ref body, ref move, feet, loco, ref o);
            return;
        }
        if (intent.Jump || (forward && body.ForwardArmed != 0)) l.Want = hr is not null && intent.Jump ? JumpWant : 1;   // asked by jump (a leap's), or by forward alone
        bool climb = rules is { Climb: true };
        if (climb && l.Want != 0 && c.Role is LedgeRole.BraceOn or LedgeRole.BraceOff)
        {
            // Asked in the brace (the grab's settle going on): the pull-up of the hang it goes to, at once, going on.
            l.Want = 0;
            if (TryLedgePullUp(m, ref body, ref move, ref l, paths, ref feet, loco, ref o, c.Braced, rules!.PullUpRate, FlowBlend))
            {
                StopShimmy(ref body, move, feet, ShimmyStop.Left, ref o);
                return;
            }
        }
        Tick(ref l, c);
        bool done = !c.Loops ? l.Time >= c.Duration : c.Role == LedgeRole.Shimmy && l.Age >= c.Duration - 1e-4f;
        if (c.Role == LedgeRole.Shimmy && done)
        {
            EndCycle(m, ref body, ref move, ref l, paths, c, ref feet, loco, intent, ref o, ref h, hr);
            if (paths.At(l.Clip - 1) is { Role: LedgeRole.Corner }) return;   // into a corner (its first tick run)
        }
        else if (c.Role is LedgeRole.BraceOn or LedgeRole.BraceOff && done)
        {
            // Into the hang it ends in, from its first frame.
            var hang = paths.Of(LedgeRole.Hang, c.Braced) ?? paths.Of(LedgeRole.Hang, !c.Braced)!;
            Switch(ref l, paths, hang, 0f, feet, LedgeBlend, fade: false);
            l.Braced = hang.Braced ? 1 : 0;
        }
        var p = LedgePoint(l, paths.At(l.Clip - 1)!, l.Time, l.Age);
        body.Velocity = (p - feet) / dt;
        feet = p;
        move.From = feet; move.To = feet;
        if (paths.At(l.Clip - 1)!.Role != LedgeRole.Hang) return;
        // In the hang: a pull-up asked (where there is room), else a shimmy's cycle.
        if (l.Want != 0)
        {
            int want = l.Want;
            l.Want = 0;
            // Jump with a side held: the leap that way where there is a lip to leap to (none: as before).
            int side = carried ? 0 : SideWish(intent, move.LipNormal);
            if (want == JumpWant && hr is { Leaps: true } && side != 0 && !forward
                && TryLeap(m, ref body, ref move, ref l, paths, ref feet, loco, side > 0 ? HangLeap.Left : HangLeap.Right, ref o, ref h, hr)) return;
            if (climb ? TryLedgePullUp(m, ref body, ref move, ref l, paths, ref feet, loco, ref o, l.Braced != 0, 1f, LedgeBlend)
                      : TryLedgePullUp(m, ref body, ref move, ref l, paths, ref feet, loco, ref o))
            {
                StopShimmy(ref body, move, feet, ShimmyStop.Left, ref o);
                return;
            }
            // Jump, no top to pull up onto: the leap up where there is a lip about a metre above.
            if (want == JumpWant && hr is { Leaps: true } && (side == 0 || forward)
                && TryLeap(m, ref body, ref move, ref l, paths, ref feet, loco, HangLeap.Up, ref o, ref h, hr)) return;
        }
        StartCycle(m, ref body, ref move, ref l, paths, ref feet, loco, intent, ref o, ref h, hr);
    }

    /// <summary><see cref="LedgeMove.Want"/> when the pull-up was asked by jump (a leap's control; 1 is forward alone), and on a drop with use held (a lip caught on the way down, no wait after it).</summary>
    const int JumpWant = 2, DropCatches = 3;

    /// <summary>The sideways wish as a shimmy's way (+1 to the left, −1 to the right of a body facing the wall), 0 for none.</summary>
    static int SideWish(in MoveIntent intent, Vector3 lipNormal)
    {
        var right = new Vector2(-lipNormal.Y, lipNormal.X);
        float lateral = intent.Direction.LengthSquared() > 0.01f ? Vector2.Dot(intent.Direction, right) : 0f;
        return lateral > ShimmyCone ? -1 : lateral < -ShimmyCone ? 1 : 0;
    }

    /// <summary>In the hang, a shimmy's cycle the sideways wish asks for, where it fits whole; a stop told when it does not.</summary>
    /// <remarks>an earlier build (<paramref name="hr"/>): a corner of the lip within its clip's reach that way is gone round (one that cannot be is told why, the shimmy as before).</remarks>
    static void StartCycle(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref Vector3 feet, in Locomotion loco,
                           in MoveIntent intent, ref MotorOutcome o, ref HangMove h, HangRules? hr)
    {
        int way = hr is not null && h.Carry != 0 ? h.Carry : SideWish(intent, move.LipNormal);
        if (way == 0)
        {
            StopShimmy(ref body, move, feet, ShimmyStop.Released, ref o);
            return;
        }
        if (paths.Of(LedgeRole.Shimmy, l.Braced != 0, way) is not { } c) return;
        if (hr is { Corners: true } && TryCorner(m, ref body, ref move, ref l, paths, c, ref feet, loco, way, fromCycleEnd: false, 0f, intent, ref o, ref h, hr)) return;
        if (!CycleFits(m, loco, l, paths, c, feet, move.LipNormal))
        {
            StopShimmy(ref body, move, feet, ShimmyStop.End, ref o);
            return;
        }
        Switch(ref l, paths, c, 0f, feet, LedgeBlend, fade: true);
        l.Way = way;
        if (body.Shimmy != -way)
        {
            if (body.Shimmy != 0) StopShimmy(ref body, move, feet, ShimmyStop.Released, ref o);
            o.ShimmyStarted = -way;   // The earlier build's way: +1 to the right
            o.ShimmyLip = move.Lip;
            o.ShimmyFeet = feet;
            body.Shimmy = -way;
        }
        // Its first tick.
        Tick(ref l, c);
        var p = LedgePoint(l, c, l.Time, l.Age);
        body.Velocity = (p - feet) / World.TickSeconds;
        feet = p;
        move.From = feet; move.To = feet;
    }

    /// <summary>
    /// Whether a whole cycle fits: the lip goes on under the leading hand to the cycle's end (every 0.1 m: a level top at the edge's height with an upright
    /// face under it, not marked not to be climbed: the test) and the hanging body has room along it (kept clear of the face).
    /// </summary>
    static bool CycleFits(in Mover m, in Locomotion loco, in LedgeMove l, LedgePaths paths, LedgeClip c, Vector3 feet, Vector3 lipNormal)
    {
        float s = c.At(c.Duration).Left;
        float step = MathF.Abs(s);
        if (step < 1e-3f) return false;
        var left = LeftOf(l.In);
        var along = new Vector3(left * MathF.Sign(s), 0f);
        var lip = new Vector3(l.Place.X + l.In.X * paths.EdgeFwd, l.Place.Y + l.In.Y * paths.EdgeFwd, l.Top);
        float hand = c.HandsApart > 0f ? 0.5f * c.HandsApart : ShimmyHand;
        for (float d = hand; ; d = MathF.Min(d + 0.1f, step + hand))
        {
            if (!LipGoesOn(m.Level, lip + along * d, l.Top, lipNormal)) return false;
            if (d >= step + hand - 1e-4f) break;
        }
        var from = Cleared(l, paths, feet, m.R);
        var mover = new Mover(m.Level, m.Others, m.Self, loco, loco.Height);
        return !(Sweep(mover, from, along, step + Skin, out var hit) && hit.Distance < step - 1e-4f);
    }

    /// <summary>
    /// A shimmy's cycle at its end: the hang's place a step along; then a brace when the wall there says the other hang, the next cycle when the wish holds
    /// the same way and it fits, else the hang (the cycle ends on its first frame).
    /// </summary>
    /// <remarks>an earlier build (<paramref name="hr"/>): the wish holding the same way at a corner of the lip within its clip's reach, round it from the cycle's end (one that cannot be is told why, the shimmy as before).</remarks>
    static void EndCycle(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref Vector3 feet, in Locomotion loco,
                         in MoveIntent intent, ref MotorOutcome o, ref HangMove h, HangRules? hr)
    {
        float carry = MathF.Max(0f, l.Age - c.Duration);
        float s = c.At(c.Duration).Left;
        var left = LeftOf(l.In);
        l.Place += new Vector3(left * s, 0f);
        move.Lip += new Vector3(left * s, 0f);
        int was = l.Way;
        if (Brace(m, ref l, paths, feet)) { l.Time = carry; l.Age = carry; l.Way = 0; return; }   // the shimmy goes on after it while the wish holds
        int way = hr is not null && h.Carry != 0 ? h.Carry : SideWish(intent, move.LipNormal);
        if (hr is { Corners: true } && way == was && l.Want == 0
            && TryCorner(m, ref body, ref move, ref l, paths, c, ref feet, loco, way, fromCycleEnd: true, carry, intent, ref o, ref h, hr)) return;
        bool fits = way == was && CycleFits(m, loco, l, paths, c, feet, move.LipNormal);
        if (fits && l.Want == 0)
        {
            Switch(ref l, paths, c, carry, feet, 0f, fade: false);   // the same clip from its first frame again: no difference, no fade
            l.Age = carry;
            l.Way = way;
            return;
        }
        var hang = paths.Of(LedgeRole.Hang, l.Braced != 0) ?? paths.Of(LedgeRole.Hang, l.Braced == 0)!;
        Switch(ref l, paths, hang, carry, feet, LedgeBlend, fade: false);
        l.Age = carry;
        l.Way = 0;
        if (l.Want == 0) StopShimmy(ref body, move, feet, way != was ? ShimmyStop.Released : ShimmyStop.End, ref o);
    }

    // ---------------------------------------------------------------- the pull-up, the drop

    /// <summary>
    /// From the hang up onto the top on the pull-up's clip: the existing room check (a top that can be stood on, read from where the body hangs) and the
    /// clip's path clear for the body above the top (the earlier build's sweep); false (still hanging) else.
    /// </summary>
    static bool TryLedgePullUp(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref Vector3 feet, in Locomotion loco, ref MotorOutcome o) =>
        TryLedgePullUp(m, ref body, ref move, ref l, paths, ref feet, loco, ref o, l.Braced != 0, 0f, LedgeBlend);

    /// <remarks>
    /// <paramref name="rate"/> above nought, going on (<see cref="AirCommitRules.Climb"/>): the pull-up of the hang <paramref name="braced"/> or not,
    /// entered past its still start (<see cref="PullUpEntry"/>) and played at that rate, the capsule's difference taken out over <paramref name="blend"/>, the
    /// animation entering it at its matching phase (<see cref="LedgeMove.Fade"/> 4). Nought: the earlier build's, from its first frame at its own rate.
    /// </remarks>
    static bool TryLedgePullUp(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref Vector3 feet, in Locomotion loco, ref MotorOutcome o,
                               bool braced, float rate, float blend)
    {
        float reach = MovesReach(body, loco).StandingReach;
        var from = Cleared(l, paths, feet, m.R);
        if (!TraversalProbe.Read(m.Level, from, l.In, loco, m.R + GrabReach, reach + 0.3f, out var ob) || !ob.HasTop || !ob.Standable) return false;
        if ((paths.Of(LedgeRole.PullUp, braced) ?? paths.Of(LedgeRole.PullUp, !braced)) is not { } c) return false;
        bool on = rate > 0f;
        float t0 = on ? PullUpEntry(c) : 0f;
        float r = on ? rate : 1f;
        var next = l;
        Switch(ref next, paths, c, t0, feet, blend, fade: true);
        var prev = feet;
        for (int i = 1; i <= LedgeSamples; i++)
        {
            float t = t0 + (c.Duration - t0) * i / LedgeSamples;
            var p = LedgePoint(next, c, t, (t - t0) / r);
            if (Blocked(m, loco, l.Top, prev, p, out _)) return false;
            prev = p;
        }
        var end = LedgePoint(next, c, c.Duration, (c.Duration - t0) / r);
        if (!Fits(m.Level, end + new Vector3(0f, 0f, Skin), loco.Radius, TuckedHeight(loco))) return false;
        o.MoveEnded = move.Kind; o.MoveCompleted = true;
        move = new Traversal
        {
            Kind = TraversalKind.PullUp, Progress = 0f, Seconds = MathF.Max((c.Duration - t0) / r, World.TickSeconds), From = feet, To = end,
            Lip = move.Lip, LipNormal = move.LipNormal, Height = move.Lip.Z - feet.Z,
        };
        l = next;
        l.Want = 0;
        if (on)
        {
            l.Rate = r;
            l.Fade = 4;   // the animation: in at its matching phase (PeopleLedge)
            l.Braced = c.Braced ? 1 : 0;
            l.Floor = float.NegativeInfinity;
        }
        o.MoveStarted = TraversalKind.PullUp;
        body.State = BodyState.Traversing;
        body.Crouched = 0;
        PullUpTick(m, ref body, ref move, ref l, paths, c, ref feet, loco, default, ref o);
        return true;
    }

    /// <summary>A pull-up along its path (the body above the top swept); at its end standing on the top (crouched where it cannot stand).</summary>
    static void PullUpTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref Vector3 feet, in Locomotion loco,
                           in MoveIntent intent, ref MotorOutcome o)
    {
        const float dt = World.TickSeconds;
        Tick(ref l, c);
        var p = l.Time >= c.Duration ? move.To : LedgePoint(l, c, l.Time, l.Age);
        if (Blocked(m, loco, l.Top, feet, p, out float share))
        {
            feet += (p - feet) * share;
            l = default;
            Abort(m, ref body, ref move, ref feet, loco, ref o);
            return;
        }
        body.Velocity = (p - feet) / dt;
        feet = p;
        move.Progress = MathF.Min(1f, (l.Rate > 0f ? l.Age : l.Time) / move.Seconds);   // a pull-up going on, entered past its start
        if (l.Time < c.Duration) return;
        o.MoveEnded = TraversalKind.PullUp; o.MoveCompleted = true;
        move = default;
        l = default;
        SetDown(m, ref body, ref feet, loco, intent, Vector3.Zero);
    }

    /// <summary>The hands let go: the drop's clip (of the hang's kind) from where the body hangs, the capsule along its path (falling from its first frame).</summary>
    static void StartDrop(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref Vector3 feet, in Locomotion loco, ref MotorOutcome o)
    {
        if ((paths.Of(LedgeRole.Drop, l.Braced != 0) ?? paths.Of(LedgeRole.Drop, l.Braced == 0)) is not { } c)
        {
            l = default;
            LetGo(m, ref body, ref move, feet, ref o);
            return;
        }
        o.Dropped = true;
        o.GripLip = move.Lip;
        o.GripFeet = feet;
        o.MoveEnded = move.Kind; o.MoveCompleted = true;
        float hangZ = feet.Z;
        Switch(ref l, paths, c, 0f, feet, LedgeBlend, fade: true);
        l.Want = 0;
        l.Way = 0;
        l.Floor = hangZ;   // the hang's height, the fall's from it (BodyMotor.ToAir)
        move = new Traversal
        {
            Kind = TraversalKind.Drop, Progress = 0f, Seconds = MathF.Max(c.Duration, World.TickSeconds), From = feet, To = LedgePoint(l, c, c.Duration, c.Duration),
            Lip = move.Lip, LipNormal = move.LipNormal, Height = move.Lip.Z - feet.Z,
        };
        o.MoveStarted = TraversalKind.Drop;
        body.State = BodyState.Traversing;
        body.Shimmy = 0;
        DropTick(m, ref body, ref move, ref l, paths, c, ref feet, loco, ref o);
    }

    /// <summary>A drop along its path (swept clear of the face); at its end, or where it meets something, the fall from there at the path's speed.</summary>
    static void DropTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref Vector3 feet, in Locomotion loco, ref MotorOutcome o)
    {
        const float dt = World.TickSeconds;
        float hangZ = l.Floor;
        var floorless = l;
        floorless.Floor = float.NegativeInfinity;
        Tick(ref l, c);
        var p = LedgePoint(l, c, l.Time, l.Age);
        var v = (p - feet) / dt;
        bool met = LedgeSwept(m, loco, floorless, paths, feet, p, loco.Height, clear: true, out float share);
        if (met) p = feet + (p - feet) * share;
        feet = p;
        body.Velocity = v;
        move.Progress = MathF.Min(1f, l.Time / move.Seconds);
        if (!met && l.Time < c.Duration) return;
        // The fall from here: the capsule's speed the path's, its fall measured from the hang.
        o.MoveEnded = TraversalKind.Drop; o.MoveCompleted = !met;
        move = default;
        bool catches = l.Want == DropCatches;   // let go with use held: a lip caught on the way down, at once
        l = default;
        l.Again = catches ? 0f : LedgeAgain;
        body.State = BodyState.Air;
        body.Crouched = 0;
        body.Coyote = 0f; body.JumpBuffer = 0f; body.AirSeconds = c.Duration;
        body.TakeOffZ = hangZ; body.HighestZ = hangZ; body.Rising = 0; body.Jumped = 0; body.Powered = 0;
        body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1;
    }

    // ---------------------------------------------------------------- the motor's own moves into the clip's hang

    /// <summary>
    /// A grab or a lowering of the motor's own (outside the grab clips' bands, a charged jump's, a lowering from an edge) ended in the hang: the clip's
    /// hang from its place (where the move ended), faded in; then, after a grab, a pull-up when forward or jump is held, as the old grab's end.
    /// </summary>
    static void LedgeHangAfter(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref Vector3 feet, in Locomotion loco,
                               in MoveIntent intent, TraversalKind ended, ref MotorOutcome o)
    {
        var n = new Vector2(move.LipNormal.X, move.LipNormal.Y);
        float nl = n.Length();
        var inward = nl > 1e-4f ? -n / nl : Facing(body.Yaw);
        l = default;
        l.Place = HangPlace(paths, move.Lip, move.LipNormal, move.Lip.Z);
        l.In = inward;
        l.Top = move.Lip.Z;
        body.Yaw = DetMath.WrapAngle(DetMath.Atan2(inward.Y, inward.X));
        // The free hang faded in from the move's stand-in, then the brace on where there is a wall under the edge (as after the grabs' clips).
        BeginLedgeHang(m, ref body, ref move, ref l, paths, braced: false, feet, loco, ref o, from: null);
        if (ended == TraversalKind.Grab && (body.JumpGrace > Eps || ForwardTo(intent, move.LipNormal))
            && TryLedgePullUp(m, ref body, ref move, ref l, paths, ref feet, loco, ref o))
            o.MoveEnded = ended;
    }
}
