// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: a developer's measurement of the swing's arc against the blade actually swung (:
// "it will always stay updated and accurate no matter how the player moves or looks").
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
/// <c>--arc-survey &lt;folder&gt;</c> (a developer's tool, not a test): the player (Christof, the sandbox's sheet,
/// his broadsword) in the free yard. Two series, each a CSV in the folder:
/// <list type="bullet">
/// <item><c>accuracy.csv</c>: each blow (quick1, quick2, strong) in first person and over each shoulder, the view pitched −20°, 0°
/// and +20°, standing, walking, running, running then stopping as the blow begins (stopping), and stopping a tick before the
/// arc is read (stopped): the arc predicted on the tick before the
/// blow begins (<see cref="SwingArcs"/>, as the picture draws it) against the blade contact then swept (the swing's trail,
/// <see cref="SwingTrails"/>) — the largest gap between the predicted tip and the swept tip place for place, over the part the
/// arc shows and over all of it, and the largest distance from a shown predicted tip to the swept tip's path.</item>
/// <item><c>targets.csv</c>: a townsman standing 1.0 and 1.5 m ahead, facing the player; the view turned and pitched until the
/// arc's ribbon passes nearest the middle of his head, chest, belly or thigh (a search, then the view settled and the arc read
/// again); then the blow: what the blade met first, against the part the arc was on.</item>
/// </list>
/// <c>--survey-only accuracy|targets</c> runs one series.
/// </summary>
internal static class ArcSurveyCommand
{
    static readonly float[] Pitches = [-20f, 0f, 20f];
    static readonly (string Name, ViewKind Kind, int Shoulder)[] Views = [("first", ViewKind.First, 1), ("right", ViewKind.Third, 1), ("left", ViewKind.Third, -1)];
    static readonly (string Name, bool Strong, int Variant)[] Blows = [("quick1", false, 0), ("quick2", false, 1), ("strong", true, 0)];
    static readonly string[] Motions = ["standing", "walking", "running", "stopping", "stopped"];
    static readonly (string Name, BodyRegion Region)[] Parts = [("head", BodyRegion.Head), ("chest", BodyRegion.Chest), ("belly", BodyRegion.Belly), ("thigh", BodyRegion.RightThigh)];
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    public static int Run(Cli cli, PlatformSettings settings, HostLog log, string folder)
    {
        Directory.CreateDirectory(folder);
        string only = cli.Arg("--survey-only") ?? "";
        var options = Program.Options(cli, settings, log) with { People = 0, Say = _ => { }, Body = Program.BodyOf(cli, settings, combat: true) };   // 1.1's Christof unless --body
        if (only != "targets") Accuracy(options, Path.Combine(folder, "accuracy.csv"), log);
        if (only != "accuracy") Targets(options, Path.Combine(folder, "targets.csv"), log);
        return 0;
    }

    // ---- accuracy ----

    static void Accuracy(SessionOptions options, string path, HostLog log)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        using var session = Session.Create(options with { Place = "22,3,0,0,0" });
        var w = session.World;
        var e = session.Player;
        float feetZ = w.Get<Body>(e).Feet.Z;
        var csv = new StringBuilder();
        // Travel_m, the body's travel from the prediction to the swept blade's last place over the shown part (the
        // arc is in the body's frame, so a moving body's swept blade is off it by this); gap_body_m, the gap place for place over
        // the shown part with each swept place moved back by the body's travel to its tick (the arc against the blade in his frame).
        csv.AppendLine("view,blow,pitch_deg,motion,speed_ms,predicted,swept,shown,gap_shown_m,gap_all_m,gap_path_m,lead_ticks,arc_ends_at_level,predict_ms,travel_m,gap_body_m");
        double worst = 0;
        foreach (var view in Views)
            foreach (var blow in Blows)
                foreach (float pitch in Pitches)
                    foreach (var motion in Motions)
                    {
                        new Placement(new Vector3(22f, 3f, feetZ), 0f, 0f).Apply(w, e);
                        ref var rig = ref w.Get<CameraRig>(e);
                        (rig.Pitch, rig.View, rig.Shoulder, rig.Orbit, rig.OrbitReturn) = (pitch * DetMath.DegToRad, view.Kind, view.Shoulder, 0f, 0f);
                        var moving = motion switch
                        {
                            "walking" => ActionFrame.Idle with { Move = new Vector2(0f, 1f), Held = Buttons.Walk },
                            "running" or "stopping" => ActionFrame.Idle with { Move = new Vector2(0f, 1f) },
                            _ => ActionFrame.Idle,
                        };
                        // stopping: the move keys let go on the tick the blow begins (the arc, a tick before, cannot know); stopped: let go a
                        // tick before the arc is read (it brakes the body as the motor will).
                        var after = motion is "stopping" or "stopped" ? ActionFrame.Idle : moving;
                        for (int i = 0; i < 150; i++) session.Step(i < 90 ? ActionFrame.Idle : moving);   // the last blow's busy time, then the pace reached
                        if (motion == "stopped") moving = ActionFrame.Idle;
                        w.Get<Combatant>(e).NextVariant = blow.Variant;
                        int hold = blow.Strong ? (int)MathF.Round(0.4f * World.TicksPerSecond) : 1;
                        for (int i = 0; i < hold; i++) session.Step(moving with { Held = moving.Held | Buttons.Attack });
                        var v = w.Get<Body>(e).Velocity;
                        float speed = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        bool has = session.SwingArcs.Predict(w, e);
                        double ms = clock.Elapsed.TotalMilliseconds;
                        var predicted = session.SwingArcs.Places.ToArray();
                        int lead = session.SwingArcs.Lead;
                        bool level = session.SwingArcs.EndsAtLevel;
                        float levelAt = session.SwingArcs.LevelAt;      // the arc goes on past the level; the blade stops there
                        int swing = -1;
                        bool ended = false;
                        var feetThen = new List<Vector3> { w.Get<Body>(e).Feet };     // the feet after each tick from the prediction's
                        for (int i = 0; i < 3 * World.TicksPerSecond && !ended; i++)
                        {
                            session.Step(after);
                            feetThen.Add(w.Get<Body>(e).Feet);
                            foreach (var b in w.Events<AttackBegun>().ThisTick) if (b.Entity == e && swing < 0) swing = b.Swing;
                            foreach (var s in w.Events<SwingEnded>().ThisTick) if (s.Attacker == e && s.Swing == swing) ended = true;
                        }
                        var swept = new List<BladePlacement>();
                        if (w.Service<SwingTrails>().Latest(e, out var trail) && trail.Swing == swing) w.Service<SwingTrails>().Places(trail, swept);
                        var g = Gaps(predicted, swept, levelAt);
                        var (travel, bodyGap) = InBodyFrame(predicted, swept, feetThen, lead, levelAt);
                        worst = motion == "standing" ? Math.Max(worst, g.All) : worst;
                        csv.AppendLine(string.Create(Ic,
                            $"{view.Name},{blow.Name},{pitch:0},{motion},{speed:0.00},{(has ? predicted.Length : 0)},{swept.Count},{g.Shown},{g.ShownGap:0.0000},{g.All:0.0000},{g.Path:0.0000},{lead},{(level ? 1 : 0)},{ms:0.000},{travel:0.0000},{bodyGap:0.0000}"));
                    }
        File.WriteAllText(path, csv.ToString());
        log.Say(string.Create(Ic, $"arc survey: {path} in {started.Elapsed.TotalSeconds:0} s; standing, the largest gap {worst * 1000:0.###} mm"));
    }

    /// <summary>
    /// Over the shown part, the body's largest travel from the prediction's tick to a swept place's tick, and the
    /// largest gap place for place with each swept tip moved back by that travel (the arc against the blade in the body's frame).
    /// Place <c>i</c> is the tick <c>lead + i</c> from the prediction's (contact's own order; no sub-steps at this tempo).
    /// </summary>
    static (float Travel, float Gap) InBodyFrame(SwingArcPlace[] predicted, List<BladePlacement> swept, List<Vector3> feet, int lead, float levelAt)
    {
        if (swept.Count == 0 || feet.Count == 0) return (float.NaN, float.NaN);
        float travel = 0f, gap = 0f;
        int n = Compared(predicted.Length, swept.Count, levelAt);
        for (int i = 0; i < n; i++)
        {
            if (!predicted[i].Front) continue;
            var moved = feet[Math.Min(feet.Count - 1, lead + i)] - feet[0];
            travel = MathF.Max(travel, moved.Length());
            gap = MathF.Max(gap, Vector3.Distance(predicted[i].Blade.Tip, swept[i].Tip - moved));
        }
        return (travel, gap);
    }

    /// <summary>
    /// How many places are compared place for place: both have them, and, where the blade meets the level, the
    /// places before the meeting (the trail's last place is cut at it; the arc goes on past it, faint, the whole front path shown).
    /// </summary>
    static int Compared(int predicted, int swept, float levelAt) =>
        levelAt >= 0f ? Math.Min(Math.Min(predicted, swept), (int)MathF.Floor(levelAt) + 1) : Math.Min(predicted, swept);

    /// <summary>The gaps between the predicted places and the swept: place for place over the shown part and over all, and from each shown predicted tip to the swept tip's path.</summary>
    static (int Shown, float ShownGap, float All, float Path) Gaps(SwingArcPlace[] predicted, List<BladePlacement> swept, float levelAt)
    {
        int shown = 0;
        float shownGap = 0f, all = 0f, path = 0f;
        int n = Compared(predicted.Length, swept.Count, levelAt);
        for (int i = 0; i < predicted.Length; i++)
        {
            if (predicted[i].Front) shown++;
            if (i < n)
            {
                float d = Vector3.Distance(predicted[i].Blade.Tip, swept[i].Tip);
                all = MathF.Max(all, d);
                if (predicted[i].Front) shownGap = MathF.Max(shownGap, d);
            }
            if (!predicted[i].Front || swept.Count < 2 || (levelAt >= 0f && i >= n)) continue;   // past the level the blade has stopped
            float best = float.PositiveInfinity;
            for (int k = 1; k < swept.Count; k++) best = MathF.Min(best, Vector3.Distance(predicted[i].Blade.Tip, OnSegment(predicted[i].Blade.Tip, swept[k - 1].Tip, swept[k].Tip)));
            path = MathF.Max(path, best);
        }
        if (swept.Count == 0) (shownGap, all, path) = (float.NaN, float.NaN, float.NaN);
        return (shown, shownGap, all, path);
    }

    // ---- against a target ----

    static void Targets(SessionOptions options, string path, HostLog log)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var csv = new StringBuilder();
        csv.AppendLine("by,view,blow,d_m,part_aimed,yaw_off_deg,pitch_deg,arc_to_part_m,arc_part_first,met,met_site,hit_aimed");
        int runs = 0, hits = 0, arcFirst = 0, arcFirstHits = 0;
        var tally = new Dictionary<string, (int Runs, int Hits)>();
        foreach (var by in new[] { "ribbon", "mark" })
        foreach (var view in Views)
            foreach (var blow in Blows)
                foreach (float d in new[] { 1.0f, 1.5f })
                    foreach (var part in Parts)
                    {
                        var spot = new Vector2(40f, 3f);
                        var their = new Vector3(spot.X, spot.Y + d, 0f);
                        var witness = string.Create(Ic, $"townsman@{their.X:0.###},{their.Y:0.###},0,270");
                        using var session = Session.Create(options with { Place = "40,3,0,90,90", Witnesses = [witness] });
                        var w = session.World;
                        var e = session.Player;
                        var target = Other(w, e);
                        if (target.IsNone) { log.Say("arc survey: no townsman made"); continue; }
                        for (int i = 0; i < 30; i++) session.Step(ActionFrame.Idle);
                        var feet = w.Get<Body>(e).Feet;
                        // The search: the view's yaw and pitch that put the arc's ribbon nearest the part's middle (the body turned and the
                        // aim set as a tick would set them), then the view put there and settled, and the arc read again.
                        w.Get<Combatant>(e).NextVariant = blow.Variant;
                        if (blow.Strong) w.Get<Combatant>(e).AttackHeld = 1f;   // the strong blow's arc (attack held long enough)
                        var aimed = PartMiddle(w, target, part.Region);
                        float bestYaw = 0f, bestPitch = 0f, best = float.PositiveInfinity;
                        float baseYaw = 90f * DetMath.DegToRad;
                        var thirdAim = w.TryService<ThirdPersonAimSettings>(out var tpa) ? tpa : new ThirdPersonAimSettings();
                        for (float yo = -36f; yo <= 36.01f; yo += 3f)
                            for (float p = -40f; p <= 30.01f; p += 2.5f)
                            {
                                w.Get<Body>(e).Yaw = baseYaw + yo * DetMath.DegToRad;
                                w.Get<PoseRig>(e).AimTarget = (p + (view.Kind == ViewKind.Third ? thirdAim.LevelOffsetDegrees : 0f)) * DetMath.DegToRad;
                                if (!session.SwingArcs.Predict(w, e)) continue;
                                // ribbon: the arc's ribbon nearest the part's middle; mark: the meeting's mark on the part (nearest its
                                // middle), else as the ribbon (a large penalty keeps a marked candidate first).
                                float dist = ArcTo(session.SwingArcs.Places, aimed);
                                if (by == "mark")
                                    dist = session.SwingArcs.MeetsRegion == part.Region || (part.Region == BodyRegion.RightThigh && session.SwingArcs.MeetsRegion == BodyRegion.LeftThigh)
                                        ? Vector3.Distance(session.SwingArcs.MeetsPoint, aimed) : 10f + dist;
                                if (dist < best) (best, bestYaw, bestPitch) = (dist, yo, p);
                            }
                        w.Get<Combatant>(e).AttackHeld = -1f;
                        new Placement(feet, 90f + bestYaw, 90f + bestYaw).Apply(w, e);
                        ref var rig = ref w.Get<CameraRig>(e);
                        (rig.Pitch, rig.View, rig.Shoulder, rig.Orbit, rig.OrbitReturn) = (bestPitch * DetMath.DegToRad, view.Kind, view.Shoulder, 0f, 0f);
                        for (int i = 0; i < 40; i++) session.Step(ActionFrame.Idle);
                        w.Get<Combatant>(e).NextVariant = blow.Variant;
                        int hold = blow.Strong ? (int)MathF.Round(0.4f * World.TicksPerSecond) : 1;
                        for (int i = 0; i < hold; i++) session.Step(ActionFrame.Idle with { Held = Buttons.Attack });
                        aimed = PartMiddle(w, target, part.Region);
                        session.SwingArcs.Predict(w, e);
                        float onPart = ArcTo(session.SwingArcs.Places, aimed);
                        var arcs = session.SwingArcs;
                        string arcMeets = arcs.Meets.IsNone ? "none" : arcs.MeetsShield ? "shield" : BodyRegions.Name(arcs.MeetsRegion) ?? "?";
                        ContactMade? first = null;
                        int swing = -1;
                        bool ended = false;
                        for (int i = 0; i < 3 * World.TicksPerSecond && !ended; i++)
                        {
                            session.Step(ActionFrame.Idle);
                            foreach (var b in w.Events<AttackBegun>().ThisTick) if (b.Entity == e && swing < 0) swing = b.Swing;
                            foreach (var c in w.Events<ContactMade>().ThisTick) if (c.Attacker == e) first ??= c;
                            foreach (var s in w.Events<SwingEnded>().ThisTick) if (s.Attacker == e && s.Swing == swing) ended = true;
                        }
                        string met = first is { } m ? (m.Met == ContactMet.Body ? "body" : m.Met.ToString().ToLowerInvariant()) : "none";
                        string site = first is { Site: { } st } ? st : "";
                        bool hit = Same(site, part.Name);
                        runs++;
                        if (hit) hits++;
                        if (Same(arcMeets, part.Name)) { arcFirst++; if (hit) arcFirstHits++; }
                        var key = by + "," + view.Name + "," + blow.Name;
                        tally[key] = (tally.GetValueOrDefault(key).Runs + 1, tally.GetValueOrDefault(key).Hits + (hit ? 1 : 0));
                        csv.AppendLine(string.Create(Ic, $"{by},{view.Name},{blow.Name},{d:0.0},{part.Name},{bestYaw:0.0},{bestPitch:0.0},{onPart:0.000},{arcMeets},{met},{site},{(hit ? 1 : 0)}"));
                    }
        File.WriteAllText(path, csv.ToString());
        log.Say(string.Create(Ic, $"arc survey: {path}: the blade met the part the arc was on {hits} times in {runs}; the arc's first part was the aimed one {arcFirst} times, of which the blade met it {arcFirstHits}; in {started.Elapsed.TotalSeconds:0} s"));
        foreach (var (k, v) in tally) log.Say($"arc survey:   {k}: {v.Hits} of {v.Runs}");
    }

    /// <summary>Whether a contact's site names the part aimed at (the thigh: either thigh).</summary>
    static bool Same(string site, string part) => part == "thigh" ? site.EndsWith("thigh", StringComparison.Ordinal) : site == part;

    /// <summary>The least distance from <paramref name="point"/> to the arc's ribbon (its shown part, mid-blade to tip, five points across each place).</summary>
    static float ArcTo(ReadOnlySpan<SwingArcPlace> places, Vector3 point)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < places.Length; i++)
        {
            if (!places[i].Front) continue;
            var b = places[i].Blade;
            for (int k = 0; k <= 4; k++)
            {
                var q = Vector3.Lerp(b.Guard, b.Tip, 0.5f + 0.125f * k);
                best = MathF.Min(best, Vector3.Distance(q, point));
            }
        }
        return best;
    }

    static EntityId Other(World w, EntityId player)
    {
        var ents = w.Store<Body>().Entities;
        for (int i = 0; i < ents.Length; i++) if (ents[i] != player && w.Has<CharacterAnimation>(ents[i])) return ents[i];
        return EntityId.None;
    }

    static Vector3 PartMiddle(World w, EntityId who, BodyRegion region)
    {
        var volumes = w.Service<HitVolumes>().Posed(w, who);
        for (int i = 0; i < volumes.Length; i++)
            if (volumes[i].Region == region) return (volumes[i].Capsule.A + volumes[i].Capsule.B) * 0.5f;
        return w.Get<Body>(who).Feet;
    }

    static Vector3 OnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float l = ab.LengthSquared();
        return l < 1e-12f ? a : a + ab * Math.Clamp(Vector3.Dot(p - a, ab) / l, 0f, 1f);
    }
}
