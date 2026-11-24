// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: some predictiveness at some point in the jumping or falling motion, which
// means that at some point the engine commits the player's actor to the motion.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>the commit point in the air</b>, for a body given it (the player's <c>PersonCommit</c>, with the ledge's clips):
/// <list type="bullet">
/// <item><b>Foreseen</b> every tick in the air (<see cref="Foresee"/>): along the capsule's arc, the first of — the hands reaching a lip (by the grab clips'
/// own contact: the capsule where the clip has it at <c>contact_s</c> against the edge, within the grab's reach of the face), the feet clearing the top the
/// arc meets (<see cref="Clears"/>), or the ground (the prelanding's foresight, <see cref="ContactTime"/>).</item>
/// <item><b>Committed</b> <see cref="AirCommitRules.Lead"/> before that contact (or at once when it is first seen nearer): a catch, by the auto-catch, when the jump
/// was taken at the wall (its lip's way in within <see cref="AirCommitRules.AutoCone"/> of the jump's way) or use is held — its clip from that moment,
/// timed to the contact; else the body falls past that lip (not caught again on that way) and the next commit is looked for; to the ground or onto a top,
/// nothing more is caught (input does not change the outcome).</item>
/// <item><b>The catch</b> (<see cref="StartCatch"/>): rising, <c>grab_run</c> (at a run) or <c>grab_jump_up</c> from no earlier than its take-off;
/// descending, <c>grab_fall</c> from its first frame — the clip's time warped from the commit to its <c>contact_s</c> (<see cref="CatchTime"/>: a slow
/// start eased to its own rate at the contact; slower than its own when the commit is further than its own lead), the capsule blended from its own arc
/// to the clip's path laid on the real edge (<see cref="CatchPoint"/>) so the knuckles meet the edge at the contact; a lip above what the arc's capsule
/// reaches with the clip's pose at the arc's top, the arms taking up <see cref="AirCommitRules.ArmReach"/> of it and the capsule lifted by the rest.</item>
/// </list>
/// </summary>
public static partial class BodyMotor
{
    /// <summary>After the hands' contact, how long the arms' share of a reach takes to come out of the capsule's place (s).</summary>
    public const float CatchSettle = 0.25f;

    /// <summary>Braced by default: the blend from a grab's pose into <c>hang_brace_on</c> begun before the grab's end (s; the animation's and the capsule's).</summary>
    public const float BraceBlend = 0.25f;

    /// <summary>How far the hands may be over the edge at a rising contact (m): a body that comes to the wall higher than that falls back to it.</summary>
    public const float CatchOver = 0.25f;

    /// <summary>
    /// How far into a flight a catch committed to begins its clip at the soonest (s; the fade into the fall, <c>PersonAirSettings.FallSeconds</c>): until then the
    /// body flies its arc in the jump's or the fall's clip, its outcome kept, so the clip's start does not cut the fade into them (a pop of the drawn body).
    /// </summary>
    public const float CatchAfter = 0.15f;

    /// <summary>As <see cref="CatchAfter"/>, in a fall not from a jump: the fall's clip begins as the body falls faster than 1 m/s (about 0.1 s off an edge) and fades in over 0.15 s.</summary>
    public const float CatchAfterFall = 0.25f;

    /// <summary>The least of a catch's clip before its hands' contact that waiting for <see cref="CatchAfter"/> may leave it (ticks; 0.15 s): with less, it begins at once.</summary>
    const int CatchLeast = 18;

    /// <summary>
    /// How far along the lip a catch's hang may sit from where the arc meets it, so both hands are on it (a lip ending under a hand), else its middle;
    /// further than this the catch is refused at its commit, and told (<see cref="CatchRefused.LipEnds"/>).
    /// </summary>
    const float CatchSlide = 0.3f;

    /// <summary>The steps <see cref="CatchSlide"/> is looked along in, each way.</summary>
    const int CatchSlideSteps = 6;

    /// <summary>
    /// Whether a catch committed to with its contact <paramref name="ticks"/> away waits to begin its clip: until <see cref="CatchAfter"/> into a jump's flight
    /// (<see cref="CatchAfterFall"/> a fall's), unless waiting would leave its clip under <see cref="CatchLeast"/> before the contact.
    /// </summary>
    static bool CatchWaits(in Body body, int ticks)
    {
        float soonest = body.Jumped != 0 ? CatchAfter : CatchAfterFall;
        int wait = (int)MathF.Ceiling((soonest - body.AirSeconds) / World.TickSeconds - 1e-3f) - 1;
        return wait > 0 && ticks - wait >= CatchLeast;
    }

    /// <summary>How many ticks past its contact a commit to the ground or a top waits before it lapses (the contact did not come: the world changed).</summary>
    public const int CommitLapse = 6;

    /// <summary>The ground meets the capsule within five skins of it, as the motor lands it (the prelanding's foresight).</summary>
    const float CommitMeets = 5f * Skin;

    /// <summary>What the arc meets first (<see cref="Foresee"/>).</summary>
    struct Foreseen
    {
        public AirOutcome What;
        public int Ticks;
        public LedgeClip? Clip;
        public Obstacle Lip;
        public float Stretch;
        public int Way;
    }

    /// <summary>
    /// <b>whether the feet clear the top</b> of a face met along the arc from <paramref name="feet"/> at <paramref name="v"/>: the capsule's middle comes to
    /// the face's line (the lip at <paramref name="lip"/>, its outward normal <paramref name="lipNormal"/>) with its feet above the top. A body already over
    /// the line clears it when its feet are over the top.
    /// </summary>
    public static bool Clears(Vector3 feet, Vector3 v, float gravity, Vector3 lip, Vector3 lipNormal, float top)
    {
        var n = new Vector2(-lipNormal.X, -lipNormal.Y);
        float nl = n.Length();
        if (nl < 1e-4f) return false;
        n /= nl;
        float d0 = Vector2.Dot(new Vector2(lip.X - feet.X, lip.Y - feet.Y), n);
        if (d0 <= 0f) return feet.Z >= top + Skin;
        float vin = Vector2.Dot(new Vector2(v.X, v.Y), n);
        if (vin <= 1e-3f) return false;
        float t = d0 / vin;
        return feet.Z + v.Z * t - 0.5f * gravity * t * t >= top + Skin;
    }

    /// <summary>
    /// <b>a tick of the commit point</b> in the air (before the moves from the air): the outcome foreseen within its lead committed to, a catch begun.
    /// True when a catch began (the body is in its clip).
    /// </summary>
    static bool CommitTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove ledge, LedgePaths paths, ref AirCommit commit, AirCommitRules rules,
                           ref Vector3 feet, in Locomotion loco, in MoveIntent intent, Vector2 aim, Vector2 vh, ref MotorOutcome o)
    {
        if (rules.Version >= 2) return CommitTick2(m, ref body, ref move, ref ledge, paths, ref commit, rules, ref feet, loco, intent, aim, vh, ref o);
        const float dt = World.TickSeconds;
        if (commit.Outcome is (int)AirOutcome.Top or (int)AirOutcome.Ground)
        {
            // Committed to the landing: nothing is caught on the way; a contact that does not come (the world changed) lapses.
            if (--commit.Ticks >= -CommitLapse) return false;
            commit.Outcome = 0;
        }
        int most = Math.Max(1, (int)MathF.Round(rules.Lead / dt));
        var v = new Vector3(vh, body.Velocity.Z);
        bool pending = commit.Outcome == (int)AirOutcome.Catch && commit.Catch == 2;
        var f = Foresee(m, body, loco, paths, commit, feet, v, aim, pending ? most + CommitLapse : most, v2: false);
        if (pending)
        {
            // A catch committed to, its clip's start waiting (CatchAfter): begun when it comes (the outcome kept), or the lip gone (the world changed: past it).
            if (f.What == AirOutcome.Catch && f.Clip is not null && !f.Lip.NoClimb)
            {
                if (CatchWaits(body, f.Ticks)) return false;
                if (StartCatch(m, ref body, ref move, ref ledge, paths, ref commit, rules, f.Clip, f.Lip, f.Ticks, f.Stretch, f.Way, ref feet, loco, intent, vh, ref o))
                    return true;
            }
            commit.Catch = 0;
            commit.Outcome = (int)AirOutcome.Past;
            if (f.What == AirOutcome.Catch) { commit.PastTop = f.Lip.Top; commit.PastLip = f.Lip.Lip; commit.PastWay = f.Way; }
            return false;
        }
        if (f.What == AirOutcome.None) return false;
        o.Committed = f.What;
        o.CommitSeconds = f.Ticks * dt;
        o.CommitWay = f.Way;
        if (f.What != AirOutcome.Catch)
        {
            commit.Outcome = (int)f.What;
            commit.Ticks = f.Ticks;
            if (f.What == AirOutcome.Top) o.CommitLip = f.Lip.Lip;
            return false;
        }
        o.CommitLip = f.Lip.Lip;
        // A jump taken at the wall catches by itself; else use held.
        var n = new Vector2(-f.Lip.LipNormal.X, -f.Lip.LipNormal.Y);
        float nl = n.Length();
        n = nl > 1e-4f ? n / nl : Facing(body.Yaw);
        float across = vh.Length();
        var jumpWay = across >= 0.5f ? vh / across : Facing(body.Yaw);
        bool auto = rules.AutoCatch && body.Jumped != 0 && Vector2.Dot(jumpWay, n) >= DetMath.Cos(rules.AutoCone * DetMath.DegToRad) - 1e-5f;
        bool wants = auto || intent.UseHeld;
        o.CommitAuto = auto;
        if (wants && !f.Lip.NoClimb && f.Clip is not null && CatchWaits(body, f.Ticks))
        {
            // Committed: its clip begins a little later in the flight (CatchAfter), the outcome kept whatever is asked meanwhile.
            commit.Outcome = (int)AirOutcome.Catch;
            commit.Catch = 2;
            commit.Ticks = f.Ticks;
            return false;
        }
        if (f.Lip.NoClimb || !wants || f.Clip is null
            || !StartCatch(m, ref body, ref move, ref ledge, paths, ref commit, rules, f.Clip, f.Lip, f.Ticks, f.Stretch, f.Way, ref feet, loco, intent, vh, ref o))
        {
            if (f.Lip.NoClimb && wants) Refuse(ref body, TraversalKind.Grab, f.Lip, f.Lip.Top - (body.TakeOffZ - Skin), ref o, once: true);
            // Past it, on this way (not looked at again going this way), the next commit looked for.
            o.Committed = AirOutcome.Past;
            commit.Outcome = (int)AirOutcome.Past;
            commit.PastTop = f.Lip.Top;
            commit.PastLip = f.Lip.Lip;
            commit.PastWay = f.Way;
            return false;
        }
        commit.Outcome = (int)AirOutcome.Catch;
        return true;
    }

    /// <summary>
    /// <b>a tick of the commit point</b> by the earlier build's rules (<see cref="AirCommitRules.Version"/> 2). As the earlier build's, with: a catch decided whole at the commit —
    /// the lip taken under the hands at the contact (the arc there laid square onto the edge of the face it goes into, not where the look along the way met
    /// it), going on under both hands (slid along it at most <see cref="CatchSlide"/>; else under the hang's middle), the hang fitting there and the catch's
    /// path clear — and kept as decided (a catch waiting for its start begins from that plan,
    /// not from a new look); a lip within reach not caught is told why (<see cref="CatchRefused"/>), the catch's path met after the commit too.
    /// </summary>
    static bool CommitTick2(in Mover m, ref Body body, ref Traversal move, ref LedgeMove ledge, LedgePaths paths, ref AirCommit commit, AirCommitRules rules,
                            ref Vector3 feet, in Locomotion loco, in MoveIntent intent, Vector2 aim, Vector2 vh, ref MotorOutcome o)
    {
        const float dt = World.TickSeconds;
        if (commit.Outcome is (int)AirOutcome.Top or (int)AirOutcome.Ground)
        {
            // Committed to the landing: nothing is caught on the way; a contact that does not come (the world changed) lapses.
            if (--commit.Ticks >= -CommitLapse) return false;
            commit.Outcome = 0;
        }
        if (commit.Outcome == (int)AirOutcome.Catch && commit.Catch == 2)
        {
            // A catch committed to, waiting for its start: begun from its plan when it comes (nothing asked changes it).
            commit.Ticks--;
            if (commit.Ticks > 0 && CatchWaits(body, commit.Ticks)) return false;
            var planned = new Obstacle { Found = true, HasTop = true, Holdable = true, Lip = commit.Goal, LipNormal = commit.GoalSpeed, Top = commit.Keep };
            if (paths.At((int)commit.Entry - 1) is { } pc && commit.Ticks > 0
                && StartCatch(m, ref body, ref move, ref ledge, paths, ref commit, rules, pc, planned, commit.Ticks, commit.Arms, (int)commit.Contact, ref feet, loco,
                              intent, vh, ref o, false, out var late))
                return true;
            // The world changed under it (its path met on the way): past it, told.
            o.Committed = AirOutcome.Past;
            o.CommitWhy = commit.Ticks > 0 ? CatchRefused.PathBlocked : CatchRefused.LipGone;
            o.CommitLip = planned.Lip;
            o.CommitWay = (int)commit.Contact;
            commit.Catch = 0;
            commit.Outcome = (int)AirOutcome.Past;
            (commit.PastTop, commit.PastLip, commit.PastWay) = (planned.Top, planned.Lip, (int)commit.Contact);
            return false;
        }
        int most = Math.Max(1, (int)MathF.Round(rules.Lead / dt));
        var v = new Vector3(vh, body.Velocity.Z);
        var f = Foresee(m, body, loco, paths, commit, feet, v, aim, most, v2: true);
        if (f.What == AirOutcome.None) return false;
        o.Committed = f.What;
        o.CommitSeconds = f.Ticks * dt;
        o.CommitWay = f.Way;
        if (f.What != AirOutcome.Catch || f.Clip is null)
        {
            commit.Outcome = (int)f.What;
            commit.Ticks = f.Ticks;
            if (f.What == AirOutcome.Top) o.CommitLip = f.Lip.Lip;
            return false;
        }
        // The lip under the hands at the contact: the arc there laid square onto the edge (of the face the arc goes into, where the probe met a corner).
        float across = vh.Length();
        var jumpWay = across >= 0.5f ? vh / across : Facing(body.Yaw);
        var lip = f.Lip;
        lip.LipNormal = FaceAt(m.Level, f.Lip, jumpWay);
        var n = new Vector2(-lip.LipNormal.X, -lip.LipNormal.Y);
        float nl = n.Length();
        n = nl > 1e-4f ? n / nl : Facing(body.Yaw);
        var along = new Vector2(-n.Y, n.X);
        float tc = f.Ticks * dt;
        var at = new Vector2(feet.X + vh.X * tc, feet.Y + vh.Y * tc);
        var edge = new Vector2(f.Lip.Lip.X, f.Lip.Lip.Y);
        var under = edge + along * Vector2.Dot(at - edge, along);
        lip.Lip = new Vector3(under, f.Lip.Top);
        o.CommitLip = lip.Lip;
        // A jump taken at the wall catches by itself; else use held.
        bool auto = rules.AutoCatch && body.Jumped != 0 && Vector2.Dot(jumpWay, n) >= DetMath.Cos(rules.AutoCone * DetMath.DegToRad) - 1e-5f;
        bool wants = auto || intent.UseHeld;
        o.CommitAuto = auto;
        var why = CatchRefused.None;
        if (lip.NoClimb) why = CatchRefused.NoClimb;
        else if (!wants) why = CatchRefused.UseNotHeld;
        else
        {
            // The lip going on under both hands at the contact, slid along it at most CatchSlide (the nearer way first) when a hand would be off its end; else
            // (as before catches at an end, the probe's lip) under the hang's middle, a hand past the end.
            float half = f.Clip.HandsApart > 0f ? 0.5f * f.Clip.HandsApart : ShimmyHand;
            var along3 = new Vector3(along, 0f);
            bool on = false;
            for (int pass = 0; pass < 2 && !on; pass++)
                for (int i = 0; i <= 2 * CatchSlideSteps && !on; i++)
                {
                    float d = (i + 1) / 2 * (CatchSlide / CatchSlideSteps) * (i % 2 == 0 ? 1f : -1f);
                    var at3 = lip.Lip + along3 * d;
                    if (pass == 0 ? LipGoesOn(m.Level, at3 + along3 * half, lip.Top, lip.LipNormal) && LipGoesOn(m.Level, at3 - along3 * half, lip.Top, lip.LipNormal)
                                  : LipGoesOn(m.Level, at3, lip.Top, lip.LipNormal))
                        (on, lip.Lip) = (true, at3);
                }
            o.CommitLip = lip.Lip;
            if (!on) why = CatchRefused.LipEnds;
            else if (!StartCatch(m, ref body, ref move, ref ledge, paths, ref commit, rules, f.Clip, lip, f.Ticks, f.Stretch, f.Way, ref feet, loco, intent, vh, ref o,
                                 true, out why)) { }
        }
        if (why != CatchRefused.None)
        {
            if (why == CatchRefused.NoClimb && wants) Refuse(ref body, TraversalKind.Grab, lip, lip.Top - (body.TakeOffZ - Skin), ref o, once: true);
            // Past it, on this way (not looked at again going this way), told why; the next commit looked for.
            o.Committed = AirOutcome.Past;
            o.CommitWhy = why;
            commit.Outcome = (int)AirOutcome.Past;
            (commit.PastTop, commit.PastLip, commit.PastWay) = (lip.Top, f.Lip.Lip, f.Way);
            return false;
        }
        commit.Outcome = (int)AirOutcome.Catch;
        if (CatchWaits(body, f.Ticks))
        {
            // Committed whole: its clip begins a little later in the flight (CatchAfter), from this plan whatever is asked meanwhile.
            commit.Catch = 2;
            commit.Ticks = f.Ticks;
            (commit.Goal, commit.GoalSpeed, commit.Keep, commit.Arms, commit.Entry, commit.Contact) = (lip.Lip, lip.LipNormal, lip.Top, f.Stretch, paths.IndexOf(f.Clip) + 1, f.Way);
            return false;
        }
        return StartCatch(m, ref body, ref move, ref ledge, paths, ref commit, rules, f.Clip, lip, f.Ticks, f.Stretch, f.Way, ref feet, loco, intent, vh, ref o);
    }

    /// <summary>
    /// The face under a lip the probe met: where it met a corner (its normal between the two faces), the face there the arc goes into most squarely,
    /// read by rays at the lip from out along the probe's normal; else the probe's normal.
    /// </summary>
    static Vector3 FaceAt(CollisionWorld level, in Obstacle lip, Vector2 way)
    {
        var n = new Vector2(lip.LipNormal.X, lip.LipNormal.Y);
        float nl = n.Length();
        if (nl < 1e-4f) return lip.LipNormal;
        n /= nl;
        var side = new Vector2(-n.Y, n.X);
        var best = lip.LipNormal;
        float most = Vector2.Dot(n, -way);
        var from = new Vector3(lip.Lip.X + n.X * 0.3f, lip.Lip.Y + n.Y * 0.3f, lip.Top - 0.08f);
        for (int s = -1; s <= 1; s += 2)
        {
            var to = new Vector3(lip.Lip.X + side.X * s * 0.1f - n.X * 0.02f, lip.Lip.Y + side.Y * s * 0.1f - n.Y * 0.02f, lip.Top - 0.08f);
            var d = to - from;
            float len = d.Length();
            if (level.Ray(from, d / len, len + 0.1f) is not { } hit || MathF.Abs(hit.Normal.Z) > 0.3f) continue;
            var fn = Vector2.Normalize(new Vector2(hit.Normal.X, hit.Normal.Y));
            if (Vector2.Dot(fn, n) < 0.5f) continue;   // not a face of this corner
            float dot = Vector2.Dot(fn, -way);
            if (dot > most + 1e-3f) (most, best) = (dot, new Vector3(fn, 0f));
        }
        return best;
    }

    /// <summary>
    /// What the arc from <paramref name="feet"/> at <paramref name="v"/> meets first within <paramref name="most"/> ticks: a lip's contact (rising or falling, by the
    /// grab clips' own contact), the ground (or a top it clears: <see cref="AirOutcome.Top"/>), or nothing.
    /// </summary>
    /// <remarks>
    /// <paramref name="v2"/>: from a jump only the lips its own moves take (over a step and within the mantle band above the ground left) are theirs, so a
    /// jump's descent catches the lips under its take-off (an earlier build left every lip at or under it, a jump off a top caught nothing below); a lip the hands reach
    /// only at full reach as the arc comes to the wall past its top is caught there (<c>grab_fall</c>, the stretch as the arms' reach; an earlier build caught none); and with
    /// nothing holdable along the way the body goes, the wish (or the facing) is looked along too, for a lip beside the fall.
    /// </remarks>
    static Foreseen Foresee(in Mover m, in Body body, in Locomotion loco, LedgePaths paths, in AirCommit commit, Vector3 feet, Vector3 v, Vector2 aim, int most,
                            bool v2)
    {
        const float dt = World.TickSeconds;
        float g = loco.Gravity;
        var f = new Foreseen { What = AirOutcome.None, Ticks = int.MaxValue };
        float tg = ContactTime(m.Level, feet - new Vector3(0f, 0f, CommitMeets), v, g, (most + 1) * dt);
        int kg = float.IsNaN(tg) ? int.MaxValue : Math.Max(1, (int)MathF.Ceiling(tg / dt - 1e-3f));
        if (kg <= most)
        {
            // The ground, or a top (the arc comes down on something higher than a step over the ground it left).
            float zg = feet.Z + v.Z * tg - 0.5f * g * tg * tg;
            f.What = zg > body.TakeOffZ + loco.StepHeight ? AirOutcome.Top : AirOutcome.Ground;
            f.Ticks = kg;
            f.Lip = new Obstacle { Lip = new Vector3(feet.X + v.X * tg, feet.Y + v.Y * tg, zg), Top = zg };
        }
        // The lip ahead, along the way the body goes (or the wish, the facing, when it hardly goes across), looked for from the lowest the window reaches to above
        // its highest by the hands' reach.
        var across = new Vector2(v.X, v.Y);
        float sp = across.Length();
        var way = sp >= 0.5f ? across / sp : aim;
        if (way.LengthSquared() < 1e-6f) return f;
        int last = Math.Min(most, kg);
        float tl = last * dt, ta = v.Z > 0f && g > 0f ? MathF.Min(v.Z / g, tl) : 0f;
        float lowZ = feet.Z + v.Z * tl - 0.5f * g * tl * tl, highZ = feet.Z + v.Z * ta - 0.5f * g * ta * ta;
        var reach = MovesReach(body, loco);
        var from = new Vector3(feet.X, feet.Y, MathF.Min(feet.Z, lowZ));
        bool found = TraversalProbe.Read(m.Level, from, way, loco, loco.Radius + GrabReach + sp * tl, highZ - from.Z + reach.StandingReach + 0.3f, out var ob)
                     && (ob.HasTop || ob.NoClimb);
        // Nothing holdable along the way: along the wish (or the facing), a lip beside the fall.
        if (v2 && !(found && (ob.NoClimb || ob.Holdable)) && aim.LengthSquared() > 1e-6f && Vector2.Dot(aim, way) < 0.95f
            && TraversalProbe.Read(m.Level, from, aim, loco, loco.Radius + GrabReach + 0.3f, highZ - from.Z + reach.StandingReach + 0.3f, out var beside)
            && (beside.NoClimb || (beside.HasTop && beside.Holdable)))
            (found, ob) = (true, beside);
        if (!found) return f;
        // A top the arc clears: landed on (or flown over), never caught.
        if (ob.HasTop && Clears(feet, v, g, ob.Lip, ob.LipNormal, ob.Top))
        {
            if (f.What == AirOutcome.Ground) f.What = AirOutcome.Top;
            if (f.What == AirOutcome.Top) f.Lip = ob;
            return f;
        }
        if (!ob.NoClimb && !ob.Holdable) return f;
        // From a jump, what the jump's moves take (a vault, a climb, a mantle: to the mantle band's top above the ground left) is theirs (as before falling grab).
        float above = ob.Top - (body.TakeOffZ - Skin);
        if (body.Jumped != 0 && above <= reach.MantleMax + Within && (!v2 || above > loco.StepHeight)) return f;
        var n = new Vector2(-ob.LipNormal.X, -ob.LipNormal.Y);
        float nl = n.Length();
        if (nl < 1e-4f) return f;
        n /= nl;
        var rise = sp >= VaultRunning ? paths.Of(LedgeRole.GrabRun) ?? paths.Of(LedgeRole.GrabStand) : paths.Of(LedgeRole.GrabStand) ?? paths.Of(LedgeRole.GrabRun);
        var fall = paths.Of(LedgeRole.GrabFall);
        float zRise = rise is null || float.IsNaN(rise.ContactS) ? float.NaN : ob.Top - (rise.EdgeUp - rise.At(rise.ContactS).Up);
        float zFall = fall is null || float.IsNaN(fall.ContactS) ? float.NaN : ob.Top - (fall.EdgeUp - fall.At(fall.ContactS).Up);
        float stretchMax = float.IsNaN(zRise) ? 0f : MathF.Max(0f, reach.StandingReach + 0.05f - (ob.Top - zRise));
        float stretchFall = float.IsNaN(zFall) ? 0f : MathF.Max(0f, reach.StandingReach + 0.05f - (ob.Top - zFall));
        float d0 = Vector2.Dot(new Vector2(ob.Lip.X - feet.X, ob.Lip.Y - feet.Y), n), vin = Vector2.Dot(across, n);
        float keep = loco.Radius + Skin, inReach = loco.Radius + GrabReach + 1e-3f;
        bool samePast = MathF.Abs(ob.Top - commit.PastTop) < 0.05f
                        && MathF.Abs(Vector2.Dot(new Vector2(ob.Lip.X - commit.PastLip.X, ob.Lip.Y - commit.PastLip.Y), n)) < 0.1f;
        bool risePast = samePast && commit.PastWay == 1, fallPast = samePast && commit.PastWay == -1;
        // Rising, the capsule at the clip's own contact against the edge (the hands at most CatchOver over it) — while the arc still closes on the wall only
        // where it comes to the clip's own distance from the face (a run's catch at the wall: one that would come to it past the arc's top is the falling
        // one's there); or, at the arc's top within reach, under it by no more than the hands' reach allows (the arms and the capsule's lift take it up).
        // Falling, as it comes down to the falling grab's contact within reach.
        float faceRise = rise is null || float.IsNaN(rise.ContactS) ? 0f : rise.EdgeFwd - rise.At(rise.ContactS).Forward;
        float zPrev = feet.Z;
        bool nearPrev = MathF.Max(d0, keep) <= inReach;
        for (int k = 1; k <= last; k++)
        {
            float t = k * dt;
            float z = feet.Z + v.Z * t - 0.5f * g * t * t;
            float vzBefore = v.Z - g * (k - 1) * dt, vz = v.Z - g * t;
            float d = MathF.Max(d0 - vin * t, keep);
            bool near = d <= inReach;
            // Come to within reach of the face past the arc's top, under the falling grab's contact but within the hands' reach: caught there.
            if (v2 && near && !nearPrev && fall is not null && !float.IsNaN(zFall) && !fallPast && vzBefore <= 0f && z < zFall && z >= zFall - stretchFall)
                return Caught(ref f, k, fall, ob, zFall - z, -1);
            nearPrev = near;
            if (near && rise is not null && !float.IsNaN(zRise) && !risePast && vzBefore > 0f)
            {
                if (z >= zRise && z <= zRise + CatchOver && (vin <= 0.5f || d <= faceRise + 0.05f)) return Caught(ref f, k, rise, ob, 0f, 1);
                if (vz <= 0f && z < zRise && z >= zRise - stretchMax) return Caught(ref f, k, rise, ob, zRise - z, 1);
            }
            if (near && fall is not null && !float.IsNaN(zFall) && !fallPast && z < zPrev && zPrev > zFall && z <= zFall)
                return Caught(ref f, k, fall, ob, 0f, -1);
            zPrev = z;
        }
        return f;

        static Foreseen Caught(ref Foreseen f, int k, LedgeClip c, in Obstacle ob, float stretch, int way)
        {
            f = new Foreseen { What = AirOutcome.Catch, Ticks = k, Clip = c, Lip = ob, Stretch = stretch, Way = way };
            return f;
        }
    }

    /// <summary>
    /// The catch's clip begun at the commit (<paramref name="ticks"/> to the hands' contact): from its take-off (rising) or its first frame (falling), or later
    /// when the contact is nearer than its own lead; the hang fitting and the path clear for the tucked body. Runs its first tick.
    /// </summary>
    /// <remarks><paramref name="dry"/>: only whether it would begin (the hang fits, the path is clear), nothing changed; <paramref name="why"/> why not.</remarks>
    static bool StartCatch(in Mover m, ref Body body, ref Traversal move, ref LedgeMove ledge, LedgePaths paths, ref AirCommit commit, AirCommitRules rules,
                           LedgeClip c, in Obstacle ob, int ticks, float stretch, int way, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, Vector2 vh,
                           ref MotorOutcome o, bool dry = false) => StartCatch(m, ref body, ref move, ref ledge, paths, ref commit, rules, c, ob, ticks, stretch, way,
                                                                               ref feet, loco, intent, vh, ref o, dry, out _);

    static bool StartCatch(in Mover m, ref Body body, ref Traversal move, ref LedgeMove ledge, LedgePaths paths, ref AirCommit commit, AirCommitRules rules,
                           LedgeClip c, in Obstacle ob, int ticks, float stretch, int way, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, Vector2 vh,
                           ref MotorOutcome o, bool dry, out CatchRefused why)
    {
        const float dt = World.TickSeconds;
        why = CatchRefused.NoRoom;
        var place = HangPlace(paths, ob.Lip, ob.LipNormal, ob.Top);
        if (!HangFits(m.Level, loco, place, ob.Lip, ob.LipNormal)) return false;
        why = CatchRefused.PathBlocked;
        var n = new Vector2(-ob.LipNormal.X, -ob.LipNormal.Y);
        float nl = n.Length();
        if (nl < 1e-4f) return false;
        n /= nl;
        // A wall under the edge: the grab's braced twin, ending braced (no brace after it).
        if (rules.BracedTwins && paths.TwinOf(c) is { } twin && !ReferenceEquals(twin, c) && WallUnder(m.Level, paths, place, n)) c = twin;
        float contact = ticks * dt;
        float first = way > 0 && !float.IsNaN(c.TakeoffS) ? c.TakeoffS : 0f;
        float entry = MathF.Max(first, c.ContactS - contact);
        var next = new LedgeMove
        {
            Clip = paths.IndexOf(c) + 1, Time = entry, Age = 0f, Braced = c.Braced ? 1 : 0, Place = place, In = n, Top = ob.Top,
            Floor = float.NegativeInfinity, Fade = 2, Rate = (c.ContactS - entry) / contact,   // the mean rate to the contact (the animation's fade)
        };
        var k = new AirCommit
        {
            Outcome = (int)AirOutcome.Catch, Catch = 1, From = feet, Speed = new Vector3(vh, body.Velocity.Z), Gravity = loco.Gravity, Contact = contact, Entry = entry,
            Arms = MathF.Min(stretch, rules.ArmReach), Keep = loco.Radius + Skin, PastWay = commit.PastWay, PastTop = commit.PastTop, PastLip = commit.PastLip,
        };
        // The capsule at the contact (the clip's place there, under it by the arms' share) and its velocity then (the clip's own just after it).
        k.Goal = LedgeRaw(next, c, c.ContactS) - new Vector3(0f, 0f, k.Arms);
        float after = MathF.Min(c.Duration, c.ContactS + dt);
        k.GoalSpeed = after > c.ContactS ? (LedgeRaw(next, c, after) - LedgeRaw(next, c, c.ContactS)) / (after - c.ContactS) : Vector3.Zero;
        // The path clear for the tucked body, from here through the contact and the settle to the hang.
        float total = contact + MathF.Max(0f, c.Duration - c.ContactS);
        var prev = feet;
        for (int i = 1; i <= LedgeSamples; i++)
        {
            float age = total * i / LedgeSamples;
            var p = CatchPoint(next, paths, c, k, CatchTime(c, entry, contact, age), age);
            if (LedgeSwept(m, loco, next, paths, prev, p, TuckedHeight(loco), clear: true, out _)) return false;
            prev = p;
        }
        why = CatchRefused.None;
        if (dry) return true;
        if (move.Kind != TraversalKind.None) { o.MoveEnded = move.Kind; o.MoveCompleted = true; }
        move = new Traversal
        {
            Kind = TraversalKind.Grab, Progress = 0f, Seconds = MathF.Max(total, dt), From = feet, To = prev, Lip = ob.Lip, LipNormal = ob.LipNormal,
            Height = ob.Top - (body.TakeOffZ - Skin),
        };
        ledge = next;
        commit = k;
        o.MoveStarted = TraversalKind.Grab;
        o.GripFeet = feet;
        body.State = BodyState.Traversing;
        body.Stoop = 0f;
        body.Crouched = 0;
        body.Coyote = 0f; body.JumpBuffer = 0f; body.Rising = 0; body.Jumped = 0; body.Charging = 0;
        body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1; body.GroundSeconds = 0f;
        body.Yaw = DetMath.WrapAngle(DetMath.Atan2(n.Y, n.X));
        GrabTick(m, ref body, ref move, ref ledge, paths, c, ref feet, loco, intent, ref o, first: true, ref commit, rules);
        return true;
    }

    /// <summary>
    /// A catch's clip time <paramref name="age"/> s after the commit: from <paramref name="entry"/> to the clip's <c>contact_s</c> over <paramref name="contact"/>
    /// s along a curve whose rate is the clip's own at the contact (a slower start when the commit is further than the clip's own lead), its own after.
    /// </summary>
    public static float CatchTime(LedgeClip c, float entry, float contact, float age)
    {
        if (age >= contact) return MathF.Min(c.Duration, c.ContactS + (age - contact));
        float span = c.ContactS - entry;
        if (span <= 0f || contact <= 0f) return c.ContactS;
        // phase = entry + a·age + b·age², with phase(contact) = contact_s and its rate 1 there (a ≥ 0; when the span is under half the time, a = 0).
        float b = (contact - span) / (contact * contact), a = 1f - 2f * b * contact;
        if (a < 0f) { a = 0f; b = span / (contact * contact); }
        return entry + a * age + b * age * age;
    }

    /// <summary>
    /// Where a catch's capsule is <paramref name="age"/> s after the commit, its clip at <paramref name="t"/>: before the hands' contact its own arc with a
    /// correction growing from nothing (a cubic's: none and at no rate at the commit, so it leaves the arc at the arc's own place and speed) to the difference
    /// from the clip's place at the contact laid on the real edge (under it by the arms' share) and from the clip's velocity just after it, so it is on the
    /// clip's path at the contact at the clip's own speed; after the contact the clip's path, the arms' share taken out over <see cref="CatchSettle"/>.
    /// </summary>
    public static Vector3 CatchPoint(in LedgeMove l, LedgePaths paths, LedgeClip c, in AirCommit k, float t, float age)
    {
        if (age >= k.Contact - 1e-5f)
        {
            float x = (age - k.Contact) / CatchSettle;
            return LedgeRaw(l, c, t) - new Vector3(0f, 0f, k.Arms * (1f - Smooth(x)));
        }
        float u = Math.Clamp(age / k.Contact, 0f, 1f), u2 = u * u, u3 = u2 * u;
        var arc = CatchArc(l, paths, k, age);
        var arcEnd = CatchArc(l, paths, k, k.Contact);
        var arcSpeed = (arcEnd - CatchArc(l, paths, k, k.Contact - 1e-3f)) / 1e-3f;
        return arc + (k.Goal - arcEnd) * (3f * u2 - 2f * u3) + (k.GoalSpeed - arcSpeed) * (k.Contact * (u3 - u2));
    }

    /// <summary>How softly a catch's arc comes to the face under the edge (m): it is eased onto <see cref="AirCommit.Keep"/> from this much further out.</summary>
    const float CatchSoft = 0.1f;

    /// <summary>
    /// The capsule's own arc <paramref name="age"/> s after the commit, kept off the face under the edge: coming to it, eased onto <see cref="AirCommit.Keep"/> from
    /// <see cref="CatchSoft"/> further out (as the motor's air slides along it, but with no corner in its speed: the drawn body would pop).
    /// </summary>
    static Vector3 CatchArc(in LedgeMove l, LedgePaths paths, in AirCommit k, float age)
    {
        var p = k.From + k.Speed * age - new Vector3(0f, 0f, 0.5f * k.Gravity * age * age);
        var edge = new Vector2(l.Place.X + l.In.X * paths.EdgeFwd, l.Place.Y + l.In.Y * paths.EdgeFwd);
        float d = Vector2.Dot(edge - new Vector2(p.X, p.Y), l.In);
        float x = (d - k.Keep) / CatchSoft;
        if (x < 1f)
        {
            float kept = k.Keep + CatchSoft * DetMath.Exp(x - 1f);   // the same place and slope at x = 1, the face's keep far in
            p.X -= l.In.X * (kept - d); p.Y -= l.In.Y * (kept - d);
        }
        return p;
    }
}
