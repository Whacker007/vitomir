// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository moves carry their capsule; the engine plays the path as its timed move, warped so the hands
// meet the real edge (CONVENTIONS, Notes, traverse).
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>the clip-carried moves</b> (the models repository, <c>hop_up</c>, <c>vault_low</c>/<c>_high</c>,
/// <c>climb_onto_low</c>/<c>_high</c>, <c>mantle_low</c>/<c>_high</c>; <see cref="OverPaths"/>), for a body given them (the people's
/// <c>PersonOver</c>, with the switch on): <b>the capsule moves along the clip's path, not by the traversal's own</b>, for the clip's length.
/// <list type="bullet">
/// <item><b>Which move</b>: the existing detection's (<see cref="TraversalProbe"/>: thin with ground beyond, a vault; a top that can be stood on, a hop
/// up to the tap's reach, a climb onto to <see cref="Locomotion.VaultMax"/>, a mantle to <see cref="Locomotion.MantleMax"/>), the clip by the clips'
/// own bands (<see cref="OverPaths.For"/>). Where they differ the clips' bands rule: no clip-carried vault below the vault clips' lowest (0.6 m: a
/// thin top lower than that is not vaulted, from the ground or the air); above every band (a body's powers) the traversal's own move as before.</item>
/// <item><b>When</b>: a tap on the ground starts it at once — a hop up, a climb onto or a mantle at a face within <see cref="CloseReach"/>, a vault
/// from a walk or a stand at a face about the clip's own distance ahead — the clip making its own hop (earlier, a climb and a mantle began in the
/// air from the tap's jump). Running at a vault, the press is held (<see cref="OverPress"/>) until the run's footfall the clip begins on (the
/// animation tells when: the left foot coming down nearest the clip's distance from the face), so the run goes into the vault and out of it.
/// From the air (a jump that met the obstacle: the traversal's own detection), the clip entered at the hands' contact, the capsule's difference
/// from its path taken out over <see cref="AirBlend"/>.</item>
/// <item><b>The warp</b>: the clip's first frame's capsule laid where the clip's face and top meet the real ones (<see cref="OverMove.Origin"/>), the
/// start's difference across taken out over the clip's <c>warp</c> window (so the hands, put down at its end, meet the real edge), the height's
/// difference laid on by the path's rise (on the ground the feet stay on the ground; over the top the body is as far over the real top as the
/// clip's over its own; a vault comes down to the ground it left).</item>
/// <item><b>Collision</b> as the traversal's: the path checked before the move starts — refused, as if not asked for, when it would meet anything
/// (and then the traversal's own move is tried) — and swept tick by tick (a body stepping into the way ends it, and the body falls from where it
/// got to); the capsule swept is the body above the obstacle's top (the clip's legs go past its face below it, as the drawn body's do).</item>
/// <item><b>The end</b>: on the top, standing (or crouched where it cannot stand); a vault on the ground beyond at the clip's speed out, into the run.</item>
/// </list>
/// </summary>
public static partial class BodyMotor
{
    /// <summary>From the air, how long the capsule's difference from the clip's path takes to go (s).</summary>
    public const float AirBlend = 0.15f;

    /// <summary>How far beyond the radius a press made running looks for a vault (m): about a run's stride and a half ahead of the clip's start.</summary>
    public const float VaultLead = 2.6f;

    /// <summary>How much nearer than the clip's own distance from the face a vault may begin (m), and how much further (from a walk or a stand).</summary>
    public const float VaultNearer = 0.4f, VaultFurther = 0.1f;

    /// <summary>The longest a press is held for the run's footfall (s).</summary>
    public const float MostHold = 1.0f;

    /// <summary>The ground speed (m/s) from which a press at a vault is a running one (held for the footfall): the walk-run threshold less its band.</summary>
    public const float VaultRunning = 2.1f;

    /// <summary>The pieces the path is checked in before a clip-carried move starts.</summary>
    const int OverSamples = 24;

    /// <summary>What a press would start: the clip, the obstacle's top, lip and face, the height above the ground (m), the face's distance ahead along <see cref="Way"/>.</summary>
    struct OverPlan
    {
        public OverClip Clip;
        public Obstacle Ob;   // what the probe read (for the auto vault's face and far side)
        public float Top, H, Face;
        public Vector3 Lip, LipNormal;
        public Vector2 Way;
    }

    /// <summary>
    /// <b>a press of jump</b> by a body given the clip-carried moves (<see cref="BodySystem"/>, before the motor and before the jump's squash):
    /// true when the press is this one's (the squash not to hold it). Running at a vault, it is held (<see cref="OverMove.Pending"/>) until the time
    /// the animation gives runs out — the run's footfall the clip begins on — and then let go to start it (<see cref="OverMove.Go"/>); let go as a
    /// jump if the body leaves the ground first. On the ground at a move it can start at once, let go to start it this tick.
    /// </summary>
    public static bool OverPress(ref OverMove over, ref MoveIntent given, in Body body, in Locomotion loco, CollisionWorld level, OverPaths paths) =>
        OverPress(ref over, ref given, body, loco, level, paths, auto: false);

    /// <summary>
    /// As the overload before; <paramref name="auto"/> (the player's body, <see cref="Animation.People.PersonOverSettings.Auto"/>):
    /// running or sprinting at a thin thing in the vault clips' bands, the run's vault begins by itself, no press (<see cref="AutoVault"/>): held for
    /// the run's left footfall as a press is (<see cref="OverMove.Pending"/> 2), let go to start it (<see cref="OverMove.Go"/> 2, no jump), and given up
    /// (nothing at all) when the body turns away, slows to a walk, crouches or leaves the ground first; a tapped running vault is let go as
    /// <see cref="OverMove.Go"/> 3. Both are played at the speed they meet it (<see cref="VaultRate"/>).
    /// </summary>
    /// <remarks><paramref name="low"/> (tier 1's <see cref="MovesRules.LowVaults"/>): the low vaults too (<see cref="PlanLow"/>: <c>hurdle_low</c>, <c>vault_speed</c>).</remarks>
    public static bool OverPress(ref OverMove over, ref MoveIntent given, in Body body, in Locomotion loco, CollisionWorld level, OverPaths paths, bool auto, bool low = false)
    {
        const float dt = World.TickSeconds;
        over.Go = 0;
        if (over.Pending != 0)
        {
            bool self = over.Pending == 2;   // begun by itself (no press to let go of as a jump)
            given.Jump = false;
            over.Held += dt;
            if (over.Wait >= 0f) over.Wait -= dt;
            else if (self)
            {
                // Not yet told (the run's loop not yet playing): the face's distance and the closing speed kept up for when it is.
                float closing = Vector2.Dot(new Vector2(body.Velocity.X, body.Velocity.Y), over.Way);
                over.Face -= closing * dt;
                over.Speed = closing;
                over.Held = 0f;   // the hold's longest counted from when it is told
            }
            if (body.State != BodyState.Ground)
            {
                over.Pending = 0;     // off an edge first: a jump, with the edge's grace (by itself, nothing)
                given.Jump = !self;
            }
            else if (self && !StillAuto(over, given, body))
            {
                over.Pending = 0;     // turned away, slowed or crouched: given up
                over.Wait = -1f;
            }
            else if ((over.Wait > -0.5f && over.Wait <= 1e-4f) || over.Held >= MostHold - 1e-4f)
            {
                over.Pending = 0;
                given.Jump = !self;
                over.Go = self ? 2 : auto ? 3 : 1;
            }
            return true;
        }
        if (auto && !given.Jump && AutoVault(ref over, given, body, loco, level, paths, low)) return true;   // the low vaults
        if (!given.Jump || body.State != BodyState.Ground || loco.CanCharge || body.Crouched != 0) return false;
        var way = WayOf(given, body.Yaw);
        if (!Plan(level, body.Feet, way, loco, paths, VaultLead, out var plan)) return false;
        var across = new Vector2(body.Velocity.X, body.Velocity.Y);
        float speed = across.Length();
        if (plan.Clip.Kind == TraversalKind.Vault && speed >= VaultRunning && Vector2.Dot(across, plan.Way) >= 0.7f * speed
            && plan.Face >= plan.Clip.FaceM - VaultNearer)
        {
            over.Pending = 1;
            over.Wait = -1f;
            over.Held = 0f;
            over.Face = plan.Face;
            over.Speed = speed;
            given.Jump = false;
            return true;
        }
        if (!Now(plan, loco)) return false;
        over.Go = auto ? 3 : 1;
        return true;
    }

    /// <summary>Within how many degrees of square to a vault's face a body running at it must go, and wish to, for the vault to begin by itself.</summary>
    public const float AutoConeDegrees = 35f;

    /// <summary>How far ahead the run's own vault is looked for: the seconds of the body's speed before the clip's start (within the hold's longest).</summary>
    public const float AutoLookSeconds = 0.95f;

    /// <summary>The fastest a vault's clip is played to the speed it is met at (a share of its own; the sprint's 5.5 m/s is 1.83).</summary>
    public const float MostVaultRate = 2f;

    static readonly float AutoCone = DetMath.Cos(AutoConeDegrees * DetMath.DegToRad);

    /// <summary>
    /// <b>the run's and the sprint's own vault</b> — on the ground, at the run's pace or faster (the gait a run or a
    /// sprint, the ground speed at least <see cref="VaultRunning"/>, no route's footing guard), going and wishing within <see cref="AutoConeDegrees"/>
    /// of square at a thin thing the vault clips serve (<see cref="Plan"/>: thin, ground beyond, in the clips' bands, 0.6–1.2 m), its face rising from
    /// the feet's ground and its far side a face down to ground within the stride (<see cref="RisesFromFeet"/>, <see cref="Beyond"/>: as the sprint
    /// vault's), looked for <see cref="AutoLookSeconds"/> of the speed before the clip's start: held as a press would be (<see cref="OverMove.Pending"/> 2)
    /// for the run's left footfall the animation picks. A walk still needs a tap; a deep top is climbed onto only with a tap; a thing passed along or met
    /// at a slant beyond the cone, or above the bands, is not vaulted.
    /// </summary>
    /// <remarks>
    /// <paramref name="low"/>: also a thin top in <c>hurdle_low</c>'s band (0.35–0.6 m) and a low deep top in
    /// <c>vault_speed</c>'s (0.4–0.65 m high, 0.3–0.75 m deep: a trough, a bench) by the same rules (<see cref="PlanLow"/>), where the vault clips' own do not
    /// serve it. A walk still stops at them (a tap hops up a bench).
    /// </remarks>
    static bool AutoVault(ref OverMove over, in MoveIntent given, in Body body, in Locomotion loco, CollisionWorld level, OverPaths paths, bool low = false)
    {
        if (body.State != BodyState.Ground || body.Crouched != 0 || FollowsRoute(given) || given.Gait < Gait.Run) return false;
        var wish = given.Direction;
        float wl = wish.Length();
        if (wl < 0.1f) return false;   // (a start's clip gives the wish as its speed's share of the pace: any wish at the run's speed)
        var across = new Vector2(body.Velocity.X, body.Velocity.Y);
        float speed = across.Length();
        if (speed < VaultRunning || paths.Any(TraversalKind.Vault) is not { } first) return false;
        float reach = MathF.Max(VaultLead, first.FaceM + speed * AutoLookSeconds - loco.Radius);
        bool planned = Plan(level, body.Feet, wish / wl, loco, paths, reach, out var plan) && plan.Clip.Kind == TraversalKind.Vault;
        if (!planned && low) planned = PlanLow(level, body.Feet, wish / wl, loco, paths, reach, out plan);
        if (!planned) return false;
        if (Vector2.Dot(across / speed, plan.Way) < AutoCone || Vector2.Dot(wish / wl, plan.Way) < AutoCone) return false;
        if (plan.Face < plan.Clip.FaceM - VaultNearer || !RisesFromFeet(level, body.Feet, plan.Ob) || !Beyond(level, body.Feet, plan.Way, loco, plan.Ob, out _)) return false;
        over.Pending = 2;
        over.Wait = -1f;
        over.Held = 0f;
        over.Face = plan.Face;
        over.Speed = Vector2.Dot(across, plan.Way);   // its closing speed
        over.Way = plan.Way;
        return true;
    }

    /// <summary>Whether a vault held by itself still holds: on the ground, uncrouched, at the run's gait and pace, wishing within the cone of its way.</summary>
    static bool StillAuto(in OverMove over, in MoveIntent given, in Body body)
    {
        if (body.Crouched != 0 || given.Gait < Gait.Run || FollowsRoute(given)) return false;
        var wish = given.Direction;
        float wl = wish.Length();
        var across = new Vector2(body.Velocity.X, body.Velocity.Y);
        return wl >= 0.1f && Vector2.Dot(wish / wl, over.Way) >= AutoCone && Vector2.Dot(across, over.Way) >= 0.8f * VaultRunning;
    }

    /// <summary>
    /// The rate a vault's clip is played at when met at <paramref name="speed"/> m/s (toward its face): its own (1) to its own speed in, faster
    /// to the speed met (the sprint's 5.5 m/s against the clip's 3.0: 1.83), at most <see cref="MostVaultRate"/>; the capsule's path and the clip
    /// both go at it, and the body leaves at the clip's speed out times it.
    /// </summary>
    public static float VaultRate(OverClip clip, float speed) =>
        clip.Kind != TraversalKind.Vault || !(clip.SpeedIn > 0f) ? 1f : Math.Clamp(speed / clip.SpeedIn, 1f, MostVaultRate);

    /// <summary>The way a press looks: the wish's, else the facing.</summary>
    static Vector2 WayOf(in MoveIntent intent, float yaw)
    {
        var wish = intent.Direction;
        float wl = wish.Length();
        return wl > 0.1f ? wish / wl : Facing(yaw);
    }

    /// <summary>Whether a planned move starts on the press: a hop up, a climb or a mantle at a face within <see cref="CloseReach"/>; a vault about the clip's distance from its face.</summary>
    static bool Now(in OverPlan plan, in Locomotion loco) => plan.Clip.Kind == TraversalKind.Vault
        ? plan.Face >= plan.Clip.FaceM - VaultNearer && plan.Face <= plan.Clip.FaceM + VaultFurther
        : plan.Face <= loco.Radius + CloseReach + 1e-3f;

    /// <summary>
    /// What a press at <paramref name="feet"/> along <paramref name="way"/> would start, by the traversal's own reading and choice of move and the
    /// clips' bands: false when nothing (no top, one not to be climbed, no clip for its height).
    /// </summary>
    static bool Plan(CollisionWorld level, Vector3 feet, Vector2 way, in Locomotion loco, OverPaths paths, float reach, out OverPlan plan)
    {
        plan = default;
        if (way.LengthSquared() < 1e-6f) return false;
        float high = 0f;
        foreach (var c in paths.Clips) high = MathF.Max(high, c.High);
        if (!TraversalProbe.Read(level, feet, way, loco, loco.Radius + reach, high + 0.25f, out var ob) || !ob.HasTop || ob.NoClimb) return false;
        float h = ob.Top - (feet.Z - Skin);
        var kind = TraversalKind.None;
        bool thin = ob.Depth < loco.MantleMinDepth - 1e-4f;
        if (thin && ob.Beyond && h > loco.StepHeight) kind = TraversalKind.Vault;
        else if (ob.Standable && h > loco.StepHeight && h <= HopReach(loco) + Within) kind = TraversalKind.HopUp;
        else if (ob.Standable && h >= loco.VaultMin - Within && h <= loco.VaultMax + Within) kind = TraversalKind.ClimbOnto;
        else if (ob.Standable && h > loco.VaultMax + Within && h <= loco.MantleMax + Within) kind = TraversalKind.Mantle;
        if (kind == TraversalKind.None || paths.For(kind, h) is not { } clip) return false;
        var n = new Vector2(ob.LipNormal.X, ob.LipNormal.Y);
        float nl = n.Length();
        var u = nl > 1e-4f ? -n / nl : way;
        plan = new OverPlan
        {
            Clip = clip, Ob = ob, Top = ob.Top, H = h, Lip = ob.Lip, LipNormal = ob.LipNormal, Way = u,
            Face = Vector2.Dot(new Vector2(ob.Lip.X - feet.X, ob.Lip.Y - feet.Y), u),
        };
        return true;
    }

    /// <summary>
    /// <b>what the run's own low vault would start</b> (the models repository): by the probe's reading as <see cref="Plan"/>'s, a top above
    /// the stride that a low vault serves (<see cref="OverPaths.LowFor"/>: a thin one <c>hurdle_low</c>, a low deep one <c>vault_speed</c>), with ground beyond;
    /// false for none.
    /// </summary>
    static bool PlanLow(CollisionWorld level, Vector3 feet, Vector2 way, in Locomotion loco, OverPaths paths, float reach, out OverPlan plan)
    {
        plan = default;
        if (way.LengthSquared() < 1e-6f || !paths.HasLow) return false;
        float high = 0f;
        foreach (var c in paths.Clips) if (c.RunOnly) high = MathF.Max(high, c.High);
        if (!TraversalProbe.Read(level, feet, way, loco, loco.Radius + reach, high + 0.25f, out var ob) || !ob.HasTop || ob.NoClimb || !ob.Beyond) return false;
        float h = ob.Top - (feet.Z - Skin);
        if (h <= loco.StepHeight) return false;
        if (paths.LowFor(h, ob.Depth, ob.Depth < loco.MantleMinDepth - 1e-4f) is not { } clip) return false;
        var n = new Vector2(ob.LipNormal.X, ob.LipNormal.Y);
        float nl = n.Length();
        var u = nl > 1e-4f ? -n / nl : way;
        plan = new OverPlan
        {
            Clip = clip, Ob = ob, Top = ob.Top, H = h, Lip = ob.Lip, LipNormal = ob.LipNormal, Way = u,
            Face = Vector2.Dot(new Vector2(ob.Lip.X - feet.X, ob.Lip.Y - feet.Y), u),
        };
        return true;
    }

    /// <summary>A press let go to start a clip-carried move from the ground (<see cref="OverMove.Go"/>): started this tick when the plan holds now.</summary>
    /// <remarks><paramref name="low"/>: the run's own vault (<see cref="OverMove.Go"/> 2 or 3) may be a low one (<see cref="PlanLow"/>).</remarks>
    static bool TryOverFromGround(in Mover m, ref Body body, ref Traversal move, ref OverMove over, OverPaths paths, ref Vector3 feet, in Locomotion loco,
                                  in MoveIntent intent, Vector2 way, ref MotorOutcome o, bool low = false)
    {
        bool planned = Plan(m.Level, feet, way, loco, paths, VaultLead, out var plan);
        if (low && over.Go >= 2 && (!planned || plan.Clip.Kind != TraversalKind.Vault) && PlanLow(m.Level, feet, way, loco, paths, VaultLead, out var lowPlan))
            (planned, plan) = (true, lowPlan);   // the run's own low vault
        if (!planned) return false;
        bool near = plan.Clip.Kind == TraversalKind.Vault
            ? plan.Face >= plan.Clip.FaceM - VaultNearer - 0.05f && plan.Face <= plan.Clip.FaceM + 0.8f
            : plan.Face <= m.R + CloseReach + 1e-3f;
        // A vault met running (by itself, or tapped under the auto vault's rules) played at the speed it is met at.
        float rate = over.Go >= 2 ? VaultRate(plan.Clip, Vector2.Dot(new Vector2(body.Velocity.X, body.Velocity.Y), plan.Way)) : 1f;
        if (!near || !StartOver(m, ref body, ref move, ref over, paths, plan, ref feet, loco, intent, air: false, replacing: false, ref o, rate)) return false;
        o.JumpAttempted = intent.Jump;
        o.AttemptOnGround = true;
        return true;
    }

    /// <summary>
    /// A move the traversal's own detection started in the air this tick (a vault, a climb onto, a mantle from a jump), carried by its clip from the
    /// hands' contact when one serves its height and its path is clear; else left as it is.
    /// </summary>
    static void OverFromAir(in Mover m, ref Body body, ref Traversal move, ref OverMove over, OverPaths paths, ref Vector3 feet, in Locomotion loco,
                            in MoveIntent intent, ref MotorOutcome o)
    {
        if (move.Kind is not (TraversalKind.Vault or TraversalKind.ClimbOnto or TraversalKind.Mantle) || paths.For(move.Kind, move.Height) is not { } clip) return;
        var n = new Vector2(move.LipNormal.X, move.LipNormal.Y);
        float nl = n.Length();
        if (nl < 1e-4f) return;
        var u = -n / nl;
        var plan = new OverPlan
        {
            Clip = clip, Top = move.Lip.Z, H = move.Height, Lip = move.Lip, LipNormal = move.LipNormal, Way = u,
            Face = Vector2.Dot(new Vector2(move.Lip.X - feet.X, move.Lip.Y - feet.Y), u),
        };
        var keep = move;
        var bodyWas = body;
        var feetWas = feet;
        var oWas = o;
        if (!StartOver(m, ref body, ref move, ref over, paths, plan, ref feet, loco, intent, air: true, replacing: true, ref o))
        {
            (move, body, feet, o) = (keep, bodyWas, feetWas, oWas);
            over.Clip = 0;
        }
    }

    /// <summary>
    /// Starts a clip-carried move from <paramref name="feet"/> (<paramref name="air"/>: in the air, the clip entered at its hands' contact) when its
    /// path is clear, and runs its first tick; <paramref name="replacing"/>: in place of the traversal's own move begun this tick.
    /// </summary>
    static bool StartOver(in Mover m, ref Body body, ref Traversal move, ref OverMove over, OverPaths paths, in OverPlan plan, ref Vector3 feet,
                          in Locomotion loco, in MoveIntent intent, bool air, bool replacing, ref MotorOutcome o, float rate = 1f)
    {
        var c = plan.Clip;
        var u = plan.Way;
        float entry = air ? MathF.Min(c.Duration, float.IsNaN(c.ContactS) ? c.WarpTo : c.ContactS) : 0f;
        // The height's difference laid on by the rise: onto a top, the clip's up scaled so it ends on the real top (its own ends a little over its own);
        // over a thing, the clip's over its own top.
        var next = new OverMove { Clip = paths.IndexOf(c) + 1, Way = u, Top = plan.Top, Air = air ? 1 : 0, Entry = entry,
                                  Stretch = plan.H - (c.EndsOnTop ? c.RiseTo : c.TopM) };
        // The clip's first frame where its face and top meet the real ones: its face FaceM ahead along the way (across), its ground the real one.
        float ahead = plan.Face - c.FaceM;
        next.Origin = new Vector3(feet.X + u.X * ahead, feet.Y + u.Y * ahead, plan.Top - plan.H + Skin);
        next.Delta = feet - Raw(next, c, entry);
        var tr = new Traversal
        {
            Kind = c.Kind, Progress = 0f, Seconds = MathF.Max(rate != 1f ? (c.Duration - entry) / rate : c.Duration - entry, World.TickSeconds),   // a rate
            From = feet, Lip = plan.Lip, LipNormal = plan.LipNormal, Height = plan.H,
        };
        tr.To = OverPoint(next, c, c.Duration);
        if (!OverClear(m, loco, next, c, feet)) return false;
        if (!replacing && move.Kind != TraversalKind.None) { o.MoveEnded = move.Kind; o.MoveCompleted = true; }
        move = tr;
        over.Clip = next.Clip; over.Way = next.Way; over.Top = next.Top; over.Air = next.Air; over.Entry = next.Entry;
        over.Stretch = next.Stretch; over.Origin = next.Origin; over.Delta = next.Delta;
        over.Pending = 0; over.Go = 0;
        o.MoveStarted = c.Kind;
        body.State = BodyState.Traversing;
        body.Stoop = 0f;
        body.Coyote = 0f; body.JumpBuffer = 0f; body.Rising = 0; body.Jumped = 0; body.Charging = 0;
        body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1; body.GroundSeconds = 0f;
        body.Yaw = DetMath.WrapAngle(DetMath.Atan2(u.Y, u.X));
        OverAdvance(m, ref body, ref move, ref over, c, ref feet, loco, intent, ref o);
        return true;
    }

    /// <summary>The clip's path laid on the move's origin, the height's difference by its rise, without the start's difference.</summary>
    static Vector3 Raw(in OverMove over, OverClip c, float t)
    {
        var (f, up) = c.At(t);
        float rise = c.RiseTo > 1e-3f ? Math.Clamp(up / c.RiseTo, 0f, 1f) : 1f;
        return new Vector3(over.Origin.X + over.Way.X * f, over.Origin.Y + over.Way.Y * f, over.Origin.Z + up + over.Stretch * rise);
    }

    /// <summary>
    /// <b>where a clip-carried move's feet are</b> at <paramref name="t"/> s of its clip: the clip's path laid on the move's origin along its way,
    /// the height's difference laid on by the path's rise, and the start's difference taken out over the warp (from the ground: the clip's
    /// <c>warp</c> window; from the air: <see cref="AirBlend"/> from its entry).
    /// </summary>
    public static Vector3 OverPoint(in OverMove over, OverClip c, float t)
    {
        float w = over.Air != 0 ? Smooth((t - over.Entry) / AirBlend)
                : c.WarpTo > c.WarpFrom + 1e-4f ? Smooth((t - c.WarpFrom) / (c.WarpTo - c.WarpFrom)) : 1f;
        return Raw(over, c, t) + over.Delta * (1f - w);
    }

    static float Smooth(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    /// <summary>Whether the move's whole path from <paramref name="feet"/> is clear (<see cref="OverSamples"/> pieces).</summary>
    static bool OverClear(in Mover m, in Locomotion loco, in OverMove over, OverClip c, Vector3 feet)
    {
        var prev = feet;
        for (int i = 1; i <= OverSamples; i++)
        {
            var p = OverPoint(over, c, over.Entry + (c.Duration - over.Entry) * i / OverSamples);
            if (Blocked(m, loco, over.Top, prev, p, out _)) return false;
            prev = p;
        }
        return true;
    }

    /// <summary>
    /// Whether the body moving from <paramref name="from"/> to <paramref name="to"/> in a clip-carried move meets anything, and how far it gets (a share
    /// of the way): the capsule swept is the body above the obstacle's top (<paramref name="top"/> and <see cref="Clearance"/>; the tucked height above
    /// the feet, at least a ball): the clip's legs pass the face below it as the drawn body's do.
    /// </summary>
    static bool Blocked(in Mover m, in Locomotion loco, float top, Vector3 from, Vector3 to, out float share)
    {
        share = 1f;
        float floor = top + Clearance;
        float z0 = MathF.Max(from.Z, floor), z1 = MathF.Max(to.Z, floor);
        float height = MathF.Max(2f * loco.Radius, MathF.Max(from.Z, to.Z) + TuckedHeight(loco) - MathF.Max(z0, z1));
        var raised = new Mover(m.Level, m.Others, m.Self, loco, height);
        var a = new Vector3(from.X, from.Y, z0);
        var d = new Vector3(to.X, to.Y, z1) - a;
        float len = d.Length();
        if (len < 1e-7f) return false;
        if (!Sweep(raised, a, d / len, len + Skin, out var hit) || hit.Distance >= len - 1e-4f) return false;
        share = MathF.Max(0f, hit.Distance - Skin) / len;
        return true;
    }

    /// <summary>One tick along a clip-carried move; at its end the body on the top (standing, or crouched) or, over a thing, on the ground beyond at the clip's speed out.</summary>
    static void OverAdvance(in Mover m, ref Body body, ref Traversal move, ref OverMove over, OverClip c, ref Vector3 feet, in Locomotion loco,
                            in MoveIntent intent, ref MotorOutcome o)
    {
        const float dt = World.TickSeconds;
        float left = move.Seconds * (1f - move.Progress);   // the clip's time still to go (s), for a last tick shorter than a tick
        float t = MathF.Min(1f, (move.Progress * move.Seconds + dt) / move.Seconds);
        if (t > 1f - 1e-4f) t = 1f;   // the last tick its own (a rounding short of the end would leave a tick of none)
        var p = t >= 1f ? move.To : OverPoint(over, c, over.Entry + t * (c.Duration - over.Entry));
        if (Blocked(m, loco, over.Top, feet, p, out float share))
        {
            feet += (p - feet) * share;
            over.Clip = 0;
            Abort(m, ref body, ref move, ref feet, loco, ref o);
            return;
        }
        body.Velocity = (p - feet) / dt;
        feet = p;
        move.Progress = t;
        body.Yaw = DetMath.WrapAngle(DetMath.Atan2(over.Way.Y, over.Way.X));   // the clip is drawn going its own way
        if (t < 1f) return;
        // A vault played faster (VaultRate) leaves at its speed out times its rate (from the ground the rate is the clip's length over the move's).
        float played = over.Air == 0 && move.Seconds > 0f ? MathF.Max(1f, (c.Duration - over.Entry) / move.Seconds) : 1f;
        var v = c.EndsOnTop ? Vector3.Zero : new Vector3(over.Way * (c.ExitSpeed > 0f ? c.ExitSpeed * played : 0f), 0f);
        o.MoveEnded = move.Kind; o.MoveCompleted = true;
        move = default;
        over.Clip = 0;
        SetDown(m, ref body, ref feet, loco, intent, v);
        // A vault played faster (the run's own vault at a sprint) seldom ends on a tick: the rest of its last tick carried on at the speed out, on
        // the ground beyond as the run would (it went 2.0 cm where the ticks before went 4.5: a 25 mm pop of the pelvis). Only then: a clip at its own
        // rate, and every move of the earlier builds, ends as before.
        float rest = dt - left;
        if (played > 1f && rest > 1e-5f && body.State == BodyState.Ground && v.LengthSquared() > 1e-6f)
        {
            var step = v * rest;
            float len = step.Length();
            if (!Sweep(m, feet, step / len, len + Skin, out var hit) || hit.Distance >= len) feet += step;
            else feet += step / len * MathF.Max(0f, hit.Distance - Skin);
        }
    }

    /// <summary>Whether the body's middle at <paramref name="feet"/> is over a drop: no ground within a step under it (<see cref="Mover.LeavesAtEdge"/>).</summary>
    static bool OverTheEdge(in Mover m, Vector3 feet) => m.Level.Ray(feet + new Vector3(0f, 0f, 0.05f), -Vector3.UnitZ, m.Step + 0.06f) is not { } under || under.Normal.Z < m.MinN;

    /// <summary>
    /// <b>where a body going along <paramref name="way"/> leaves the ground</b> (for the animation's run off an edge: the run's last footfall
    /// before it): stepping from <paramref name="feet"/> as the motor's footing does (a drop within the step is walked down), the distance (m) to the
    /// first place within <paramref name="most"/> with no footing, and the drop below it (m); <paramref name="atCentre"/>, as a body that lets go at an
    /// edge's (<see cref="Mover.LeavesAtEdge"/>). False when the ground goes on, or a wall comes first.
    /// </summary>
    public static bool LeaveAhead(CollisionWorld level, Vector3 feet, Vector2 way, in Locomotion loco, float most, out float distance, out float drop, bool atCentre = true)
    {
        distance = 0f; drop = 0f;
        if (way.LengthSquared() < 1e-6f) return false;
        // Every 8 cm, then the last stretch halved three times (to 1 cm).
        const float step = 0.08f;
        var m = new Mover(level, default, 0, loco, loco.Height) { LeavesAtEdge = atCentre };
        float z = feet.Z, held = 0f;
        for (int i = 1; i * step <= most + 1e-4f; i++)
        {
            float s = i * step;
            if (FootingAt(m, feet, way, s, ref z)) { held = s; continue; }
            float lo = held, hi = s;
            for (int k = 0; k < 3; k++)
            {
                float mid = 0.5f * (lo + hi), zm = z;
                if (FootingAt(m, feet, way, mid, ref zm)) { lo = mid; z = zm; } else hi = mid;
            }
            if (level.Ray(new Vector3(feet.X, feet.Y, feet.Z + 0.5f), new Vector3(way, 0f), hi) is not null) return false;   // a wall first
            distance = hi;
            var at = new Vector3(feet.X + way.X * hi, feet.Y + way.Y * hi, z);
            drop = level.Ray(at + new Vector3(0f, 0f, 0.05f), -Vector3.UnitZ, 40f) is { } under ? at.Z - under.Point.Z : 40f;
            return true;
        }
        return false;
    }

    /// <summary>Whether a body <paramref name="s"/> m along <paramref name="way"/> from <paramref name="feet"/> (its feet at <paramref name="z"/>) has footing as the motor's walk finds it (and the height then).</summary>
    static bool FootingAt(in Mover m, Vector3 feet, Vector2 way, float s, ref float z)
    {
        var q = new Vector3(feet.X + way.X * s, feet.Y + way.Y * s, z);
        if (!Probe(m, q, m.Step + m.R + Skin, keeping: true, out var sup) || sup.FeetZ < q.Z - m.Step - 1e-4f || sup.FeetZ > q.Z + m.Step + 1e-4f
            || (m.LeavesAtEdge && sup.Edge && OverTheEdge(m, q))) return false;
        z = sup.FeetZ;
        return true;
    }

    /// <summary>
    /// <b>the time from now to contact</b> (s) of a body in the air at <paramref name="feet"/> going at <paramref name="velocity"/> under
    /// <paramref name="gravity"/>, its feet's point flown on the arc a tick at a time against the level (ground it can stand on); NaN within <paramref name="most"/> s none.
    /// </summary>
    public static float ContactTime(CollisionWorld level, Vector3 feet, Vector3 velocity, float gravity, float most = 2.5f)
    {
        const float dt = World.TickSeconds;
        var p = feet;
        var v = velocity;
        int ticks = (int)(most / dt);
        for (int i = 0; i < ticks; i++)
        {
            var next = p + v * dt + new Vector3(0f, 0f, -0.5f * gravity * dt * dt);
            v.Z -= gravity * dt;
            var d = next - p;
            float len = d.Length();
            if (len > 1e-7f && level.Ray(p, d / len, len) is { } hit && hit.Normal.Z > 0.5f) return i * dt + dt * hit.Distance / len;
            p = next;
        }
        return float.NaN;
    }
}
