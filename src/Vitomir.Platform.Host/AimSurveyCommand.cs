// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: a developer's measurement of where the blade goes against where the crosshair points (in play,
// the crosshair in the middle of the screen was not where Christof aimed his melee weapon).
using System.Globalization;
using System.Numerics;
using System.Text;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Host;

/// <summary>
/// <c>--aim-survey &lt;folder&gt;</c> (a developer's tool, not a test): the player (Christof, the sandbox's
/// sheet, his broadsword) swings quick and strong blows in first person and in third over each shoulder, and the blade's
/// tip path — read from the swing's trail (<see cref="SwingTrails"/>: the places contact swept) — is measured against the
/// crosshair. Two series, each a CSV in the folder:
/// <list type="bullet">
/// <item><c>free.csv</c>: the view pitched −30°, −15°, 0°, +15°, +25°, the reticle's point put at 1.0, 1.5, 2.0 and 2.5 m
/// ahead along the body's facing on the view's ray (written into the reticle before each tick, as if something were there:
/// the third-person aim is taken from it), nothing to strike. The miss from the reticle's point to the tip's path (in the
/// camera's frame), the blade's least distance to it, how far forward the tip reaches, where the tip crosses the body's
/// facing line against the ray's height there, and where it crosses the crosshair's upright line on the picture.</item>
/// <item><c>targets.csv</c>: a posted guard (<c>guard</c>, with his shield) and a townsman (<c>townsman</c>, none) standing
/// 1.0 to 2.5 m ahead facing the player, the view turned so the real reticle is on the middle of his head, chest, belly or
/// thigh; what the blade met first (shield, blade, body part, level or nothing) against the part the reticle named. A fresh
/// session each, so nobody remembers the last blow.</item>
/// </list>
/// <c>--survey-at x,y</c> (default 40,3: the free yard) and <c>--survey-facing deg</c> (default 90) place the player;
/// <c>--survey-only free|targets</c> runs one series; <c>--survey-targets guard,townsman</c> and <c>--survey-views
/// first,right,left</c> choose the targets' runs.
/// </summary>
internal static class AimSurveyCommand
{
    static readonly float[] Pitches = [-30f, -15f, 0f, 15f, 25f];
    static readonly float[] Distances = [1.0f, 1.5f, 2.0f, 2.5f];
    static readonly (string Name, ViewKind Kind, int Shoulder)[] Views = [("first", ViewKind.First, 1), ("right", ViewKind.Third, 1), ("left", ViewKind.Third, -1)];
    /// <summary>The blows: Christof's two quick ones (taken in turn in play; each fixed here) and his strong one.</summary>
    static readonly (string Name, bool Strong, int Variant)[] Blows = [("quick1", false, 0), ("quick2", false, 1), ("strong", true, 0)];
    static readonly (string Name, BodyRegion Region)[] Parts = [("head", BodyRegion.Head), ("chest", BodyRegion.Chest), ("belly", BodyRegion.Belly), ("thigh", BodyRegion.RightThigh)];
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    public static int Run(Cli cli, PlatformSettings settings, HostLog log, string folder)
    {
        Directory.CreateDirectory(folder);
        var at = (cli.Arg("--survey-at") ?? "40,3").Split(',');
        var spot = new Vector2(float.Parse(at[0], Ic), float.Parse(at[1], Ic));
        float facing = cli.Float("--survey-facing", 90f);
        string only = cli.Arg("--survey-only") ?? "";
        var options = Program.Options(cli, settings, log) with { People = 0, Say = _ => { }, Body = Program.BodyOf(cli, settings, combat: true) };   // 1.1's Christof unless --body
        if (only != "targets") Free(options, spot, facing, Path.Combine(folder, "free.csv"), log);
        var kinds = (cli.Arg("--survey-targets") ?? "guard,townsman").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var views = (cli.Arg("--survey-views") ?? "first,right,left").Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (only != "free") Targets(options, spot, facing, kinds, views, Path.Combine(folder, "targets.csv"), log);
        return 0;
    }

    // ---- the free series ----

    static void Free(SessionOptions options, Vector2 spot, float facing, string path, HostLog log)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        using var session = Session.Create(options with { Place = Place(spot, 0f, facing) });
        var w = session.World;
        var e = session.Player;
        float feetZ = w.Get<Body>(e).Feet.Z;
        var csv = new StringBuilder();
        csv.AppendLine("view,blow,pitch_deg,d_m,aim_deg,boom_m,eye_fwd_m,eye_side_m,eye_h_m,head_fwd_m,head_side_m,head_h_m,eyes_fwd_m,eyes_side_m,eyes_h_m,chest_fwd_m,chest_side_m,chest_h_m,drift_m,drift_fwd_m,drift_side_m,drift_up_m,r_fwd_m,r_side_m,r_h_m,tip_miss_m,miss_right_m,miss_up_m,miss_depth_m,blade_miss_m,reach_m," +
                       "cross_fwd_m,cross_h_m,ray_h_at_cross_m,cross_err_m,screen_cross_deg,screen_cross_depth_m,r_depth_m,contacts,first_met,sweep_deg,front_share");
        foreach (var view in Views)
            foreach (var blow in Blows)
                foreach (float pitch in Pitches)
                    foreach (float d in Distances)
                    {
                        new Placement(new Vector3(spot.X, spot.Y, feetZ), facing, facing).Apply(w, e);
                        ref var rig = ref w.Get<CameraRig>(e);
                        (rig.Pitch, rig.View, rig.Shoulder, rig.Orbit, rig.OrbitReturn) = (pitch * DetMath.DegToRad, view.Kind, view.Shoulder, 0f, 0f);
                        var r = Swing(session, blow.Strong, blow.Variant, settle: 60, () => Synthetic(w, e, facing, d));
                        if (r.Places.Count < 2) { log.Say($"aim survey: {view.Name} {blow.Name} {pitch}° {d} m: no trail"); continue; }
                        var m = Measure(r, w.Get<Body>(e).Feet, facing, r.Point);
                        var eye = Local(r.RayOrigin - w.Get<Body>(e).Feet, facing);
                        var dr = Local(r.Drift, facing);
                        var (hd, ey, ch) = (Local(r.Head - w.Get<Body>(e).Feet, facing), Local(r.Eyes - w.Get<Body>(e).Feet, facing), Local(r.Chest - w.Get<Body>(e).Feet, facing));
                        csv.AppendLine(string.Create(Ic,
                            $"{view.Name},{blow.Name},{pitch:0},{d:0.0},{r.Aim:0.0},{r.Boom:0.00},{eye.X:0.000},{eye.Y:0.000},{eye.Z:0.000},{hd.X:0.000},{hd.Y:0.000},{hd.Z:0.000},{ey.X:0.000},{ey.Y:0.000},{ey.Z:0.000},{ch.X:0.000},{ch.Y:0.000},{ch.Z:0.000},{r.Drift.Length():0.000},{dr.X:0.000},{dr.Y:0.000},{dr.Z:0.000},{m.RFwd:0.000},{m.RSide:0.000},{m.RH:0.000},{m.TipMiss:0.000},{m.MissRight:0.000},{m.MissUp:0.000},{m.MissDepth:0.000},{m.BladeMiss:0.000},{m.Reach:0.000}," +
                            $"{m.CrossFwd:0.000},{m.CrossH:0.000},{m.RayHAtCross:0.000},{m.CrossH - m.RayHAtCross:0.000},{m.ScreenCrossDeg:0.0},{m.ScreenCrossDepth:0.000},{m.RDepth:0.000},{r.Contacts},{Met(r.First)},{m.SweepDeg:0},{m.FrontShare:0.00}"));
                    }
        File.WriteAllText(path, csv.ToString());
        log.Say($"aim survey: {path} in {started.Elapsed.TotalSeconds:0} s");
    }

    /// <summary>The reticle put on the view's ray at <paramref name="d"/> m ahead along the facing (as if something stood there).</summary>
    static void Synthetic(World w, EntityId e, float facing, float d)
    {
        var view = w.Service<ViewState>();
        if (!view.HasView) return;
        var feet = w.Get<Body>(e).Feet;
        var f = Facing(facing);
        var fwd = view.View.Forward;
        float along = Vector3.Dot(fwd, f);
        if (along < 1e-3f) return;
        float t = (d - Vector3.Dot(view.View.Eye - feet, f)) / along;
        var point = view.View.Eye + fwd * t;
        w.Service<ReticleState>().Answer = new ReticleAnswer(ReticleTarget.Level, 0, point, Vector3.Distance(view.Eyes, point), null, null, false);
    }

    // ---- the targets ----

    static void Targets(SessionOptions options, Vector2 spot, float facing, string[] kinds, string[] views, string path, HostLog log)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var csv = new StringBuilder();
        csv.AppendLine("target,view,blow,d_m,part_aimed,reticle_on,reticle_part,pitch_deg,yaw_off_deg,aim_deg,met,met_site,met_fwd_m,met_side_m,met_h_m,aimed_h_m,tip_miss_m,miss_right_m,miss_up_m,miss_depth_m,places,end_fwd_m,end_side_m,end_h_m");
        var f = Facing(facing);
        int runs = 0;
        foreach (var kind in kinds)
            foreach (var view in Views)
                foreach (var blow in Blows)
                    foreach (float d in Distances)
                        foreach (var part in Parts)
                        {
                            if (Array.IndexOf(views, view.Name) < 0) continue;
                            var feet = new Vector3(spot.X, spot.Y, 0f);
                            var their = feet + f * d;
                            var witness = string.Create(Ic, $"{kind}@{their.X:0.###},{their.Y:0.###},0,{facing + 180f:0.#}");
                            using var session = Session.Create(options with { Place = Place(spot, 0f, facing), Witnesses = [witness] });
                            var w = session.World;
                            var e = session.Player;
                            var target = Other(w, e);
                            runs++;
                            if (target.IsNone) { log.Say($"aim survey: no {kind} made"); continue; }
                            for (int i = 0; i < 30; i++) session.Step(ActionFrame.Idle);
                            feet = w.Get<Body>(e).Feet;
                            var aimed = PartMiddle(w, target, part.Region);
                            float yaw = facing * DetMath.DegToRad, pitch = 0f;
                            for (int it = 0; it < 10; it++)
                            {
                                new Placement(feet, yaw * DetMath.RadToDeg, yaw * DetMath.RadToDeg).Apply(w, e);
                                ref var rig = ref w.Get<CameraRig>(e);
                                (rig.Pitch, rig.View, rig.Shoulder, rig.Orbit, rig.OrbitReturn) = (pitch, view.Kind, view.Shoulder, 0f, 0f);
                                for (int k = 0; k < (it < 8 ? 3 : 30); k++) session.Step(ActionFrame.Idle);
                                aimed = PartMiddle(w, target, part.Region);
                                var v = w.Service<ViewState>().View;
                                var want = Vector3.Normalize(aimed - v.Eye);
                                // Turn by what the ray misses by (the camera swings round its pivot as it turns: a few rounds).
                                yaw += DetMath.WrapAngle(DetMath.Atan2(want.Y, want.X) - v.Yaw);
                                pitch += DetMath.Asin(Math.Clamp(want.Z, -1f, 1f)) - v.Pitch;
                            }
                            var answer = w.Service<ReticleState>().Answer;
                            var r = Swing(session, blow.Strong, blow.Variant, settle: 2, null);
                            var m = r.Places.Count >= 2 ? Measure(r, feet, yaw * DetMath.RadToDeg, aimed) : default;
                            var first = r.First;
                            var met = first is { } c ? (c.Met == ContactMet.Body ? "body" : c.Met.ToString().ToLowerInvariant()) + (c.Target == target || c.Target.IsNone ? "" : " (someone else)") : "none";
                            var site = first is { Site: { } s } ? s : "";
                            var local = first is { } c2 ? Local(c2.Point - feet, yaw * DetMath.RadToDeg) : default;
                            csv.AppendLine(string.Create(Ic,
                                $"{kind},{view.Name},{blow.Name},{d:0.0},{part.Name},{answer.Kind.ToString().ToLowerInvariant()},{answer.Part ?? ""},{pitch * DetMath.RadToDeg:0.0},{DetMath.WrapAngle(yaw - facing * DetMath.DegToRad) * DetMath.RadToDeg:0.0},{r.Aim:0.0},{met},{site},") +
                                (first is null ? ",,," : string.Create(Ic, $"{local.X:0.000},{local.Y:0.000},{local.Z:0.000},")) +
                                string.Create(Ic, $"{aimed.Z - feet.Z:0.000},{m.TipMiss:0.000},{m.MissRight:0.000},{m.MissUp:0.000},{m.MissDepth:0.000},{r.Places.Count},") +
                                (r.Places.Count == 0 ? ",," : string.Create(Ic, $"{End(r, feet, yaw).X:0.000},{End(r, feet, yaw).Y:0.000},{End(r, feet, yaw).Z:0.000}")));
                        }
        File.WriteAllText(path, csv.ToString());
        log.Say($"aim survey: {path}, {runs} sessions in {started.Elapsed.TotalSeconds:0} s");
    }

    /// <summary>The trail's last tip, in the body's frame.</summary>
    static Vector3 End(SwingRun r, Vector3 feet, float yaw) => Local(r.Places[^1].Tip - feet, yaw * DetMath.RadToDeg);

    /// <summary>What a contact met, as the CSV says it.</summary>
    static string Met(ContactMade? c) => c is not { } m ? "none" : m.Met == ContactMet.Body ? $"body:{m.Site}" : m.Met.ToString().ToLowerInvariant();

    static EntityId Other(World w, EntityId player)
    {
        var ents = w.Store<Body>().Entities;
        for (int i = 0; i < ents.Length; i++) if (ents[i] != player && w.Has<CharacterAnimation>(ents[i])) return ents[i];
        return EntityId.None;
    }

    /// <summary>The middle of <paramref name="who"/>'s volume of <paramref name="region"/> on its pose now (its feet's middle when it has none).</summary>
    static Vector3 PartMiddle(World w, EntityId who, BodyRegion region)
    {
        var volumes = w.Service<HitVolumes>().Posed(w, who);
        for (int i = 0; i < volumes.Length; i++)
            if (volumes[i].Region == region) return (volumes[i].Capsule.A + volumes[i].Capsule.B) * 0.5f;
        return w.Get<Body>(who).Feet;
    }

    // ---- a swing ----

    sealed record SwingRun(List<BladePlacement> Places, Vector3 RayOrigin, Vector3 RayDirection, Vector3 Point, float Aim, float Boom, int Contacts, ContactMade? First,
        Vector3 Head, Vector3 Eyes, Vector3 Chest, Vector3 Drift);

    /// <summary>
    /// The player stands <paramref name="settle"/> ticks (the aim's bend and the eye settle), then swings — a tap, or held
    /// 0.4 s for a strong blow — until the swing ends; <paramref name="before"/> runs before every tick while it stands (the
    /// reticle written), and the reticle's answer as it struck is written again before every tick of the swing (the point the
    /// player aimed at stays where it was, as a thing standing there would). The view's eye's greatest move from where it was
    /// as the blow began is kept (in first person the eye rides the head, which the swing carries).
    /// </summary>
    static SwingRun Swing(Session session, bool strong, int variant, int settle, Action? before)
    {
        var w = session.World;
        var e = session.Player;
        for (int i = 0; i < settle; i++) { before?.Invoke(); session.Step(ActionFrame.Idle); }
        w.Get<Combatant>(e).NextVariant = variant;
        var (head, eyes, chest) = (PartMiddle(w, e, BodyRegion.Head), w.Service<ViewState>().Eyes, PartMiddle(w, e, BodyRegion.Chest));
        int hold = strong ? (int)MathF.Round(0.4f * World.TicksPerSecond) : 1;
        float aim = float.NaN;
        int contacts = 0, swing = -1;
        ContactMade? first = null;
        bool ended = false;
        before?.Invoke();   // the answer the blow begins with: written again, as the last tick's Post replaced it
        var kept = w.Service<ReticleState>().Answer;
        var eye0 = w.Service<ViewState>().View.Eye;
        var drift = Vector3.Zero;
        for (int i = 0; i < 3 * World.TicksPerSecond && !ended; i++)
        {
            if (before is not null) w.Service<ReticleState>().Answer = kept;
            var frame = i < hold ? ActionFrame.Idle with { Held = Buttons.Attack } : ActionFrame.Idle;
            session.Step(frame);
            foreach (var b in w.Events<AttackBegun>().ThisTick) if (b.Entity == e && swing < 0) (swing, aim) = (b.Swing, b.AimPitch * DetMath.RadToDeg);
            foreach (var c in w.Events<ContactMade>().ThisTick) if (c.Attacker == e) { contacts++; first ??= c; }
            foreach (var s in w.Events<SwingEnded>().ThisTick) if (s.Attacker == e && s.Swing == swing) ended = true;
            var moved = w.Service<ViewState>().View.Eye - eye0;
            if (moved.LengthSquared() > drift.LengthSquared()) drift = moved;
        }
        // Let the swing's busy time run out, so the next one begins clean.
        for (int i = 0; i < 90; i++) session.Step(ActionFrame.Idle);
        var trails = w.Service<SwingTrails>();
        var places = new List<BladePlacement>();
        Vector3 o = default, dir = default, point = default;
        if (trails.Latest(e, out var trail) && trail.Swing == swing)
        {
            trails.Places(trail, places);
            (o, dir, point) = (trail.RayOrigin, trail.RayDirection, trail.Point);
        }
        return new SwingRun(places, o, dir, point, aim, w.Service<ViewState>().Boom, contacts, first, head, eyes, chest, drift);
    }

    // ---- the measures ----

    readonly record struct Measures(float RFwd, float RSide, float RH, float TipMiss, float MissRight, float MissUp, float MissDepth, float BladeMiss, float Reach,
        float CrossFwd, float CrossH, float RayHAtCross, float ScreenCrossDeg, float ScreenCrossDepth, float RDepth, float SweepDeg, float FrontShare);

    static Vector3 Facing(float degrees)
    {
        var (s, c) = DetMath.SinCos(degrees * DetMath.DegToRad);
        return new Vector3(c, s, 0f);
    }

    /// <summary><paramref name="v"/> (from the feet) in the body's frame: forward along the facing, to its right, up.</summary>
    static Vector3 Local(Vector3 v, float facingDegrees)
    {
        var f = Facing(facingDegrees);
        var right = new Vector3(f.Y, -f.X, 0f);
        return new Vector3(Vector3.Dot(v, f), Vector3.Dot(v, right), v.Z);
    }

    static Measures Measure(SwingRun r, Vector3 feet, float facingDegrees, Vector3 point)
    {
        var tips = r.Places;
        var f = Facing(facingDegrees);
        var right = new Vector3(f.Y, -f.X, 0f);
        var camF = r.RayDirection;
        var camR = Vector3.Cross(camF, Vector3.UnitZ);
        camR = camR.LengthSquared() > 1e-8f ? Vector3.Normalize(camR) : right;
        var camU = Vector3.Cross(camR, camF);

        // The tip's path nearest the point, and the blade's.
        float best = float.PositiveInfinity, blade = float.PositiveInfinity, reach = float.NegativeInfinity;
        var near = tips[0].Tip;
        for (int i = 0; i < tips.Count; i++)
        {
            var p = tips[i];
            blade = MathF.Min(blade, Vector3.Distance(point, OnSegment(point, p.Guard, p.Tip)));
            reach = MathF.Max(reach, Vector3.Dot(p.Tip - feet, f));
            if (i == 0) continue;
            var q = OnSegment(point, tips[i - 1].Tip, p.Tip);
            float d = Vector3.Distance(q, point);
            if (d < best) (best, near) = (d, q);
        }
        var miss = near - point;

        // Where the tip crosses the facing line (its side changes sign): the crossing farthest forward.
        float crossF = float.NaN, crossH = float.NaN;
        for (int i = 1; i < tips.Count; i++)
        {
            float s0 = Vector3.Dot(tips[i - 1].Tip - feet, right), s1 = Vector3.Dot(tips[i].Tip - feet, right);
            if ((s0 <= 0f) == (s1 <= 0f)) continue;
            float u = s0 / (s0 - s1);
            var c = Vector3.Lerp(tips[i - 1].Tip, tips[i].Tip, u);
            float fw = Vector3.Dot(c - feet, f);
            if (float.IsNaN(crossF) || fw > crossF) (crossF, crossH) = (fw, c.Z - feet.Z);
        }
        float rayH = float.NaN;
        float along = Vector3.Dot(r.RayDirection, f);
        if (!float.IsNaN(crossF) && along > 1e-4f)
        {
            float t = (crossF - Vector3.Dot(r.RayOrigin - feet, f)) / along;
            rayH = (r.RayOrigin + r.RayDirection * t).Z - feet.Z;
        }

        // Where the tip crosses the crosshair's upright line on the picture (its camera-right changes sign, in front of the camera).
        float screenDeg = float.NaN, screenDepth = float.NaN;
        for (int i = 1; i < tips.Count; i++)
        {
            var a = tips[i - 1].Tip - r.RayOrigin;
            var b = tips[i].Tip - r.RayOrigin;
            float x0 = Vector3.Dot(a, camR), x1 = Vector3.Dot(b, camR);
            if ((x0 <= 0f) == (x1 <= 0f)) continue;
            var c = Vector3.Lerp(a, b, x0 / (x0 - x1));
            float z = Vector3.Dot(c, camF);
            if (z <= 0.05f) continue;
            float deg = DetMath.Atan2(Vector3.Dot(c, camU), z) * DetMath.RadToDeg;
            if (float.IsNaN(screenDepth) || z > screenDepth) (screenDeg, screenDepth) = (deg, z);
        }
        // How far round the body the tip went (its bearing from the feet, summed), and the share of its places in front.
        float sweep = 0f;
        int front = 0;
        for (int i = 0; i < tips.Count; i++)
        {
            var l = Local(tips[i].Tip - feet, facingDegrees);
            if (l.X > 0f) front++;
            if (i == 0) continue;
            var k = Local(tips[i - 1].Tip - feet, facingDegrees);
            sweep += MathF.Abs(DetMath.WrapAngle(DetMath.Atan2(l.Y, l.X) - DetMath.Atan2(k.Y, k.X)));
        }
        var loc = Local(point - feet, facingDegrees);
        return new Measures(loc.X, loc.Y, loc.Z, best, Vector3.Dot(miss, camR), Vector3.Dot(miss, camU), Vector3.Dot(miss, camF), blade, reach,
            crossF, crossH, rayH, screenDeg, screenDepth, Vector3.Dot(point - r.RayOrigin, camF), sweep * DetMath.RadToDeg, front / (float)tips.Count);
    }

    static Vector3 OnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float l = ab.LengthSquared();
        return l < 1e-12f ? a : a + ab * Math.Clamp(Vector3.Dot(p - a, ab) / l, 0f, 1f);
    }

    static string Place(Vector2 spot, float z, float facing) => string.Create(Ic, $"{spot.X:0.###},{spot.Y:0.###},{z:0.###},{facing:0.#},{facing:0.#}");
}
