// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Navigation;

/// <summary>What <see cref="SnagCheck.Run"/> is to do.</summary>
public sealed record SnagCheckOptions
{
    /// <summary>How many seeded random pairs of reachable places to drive between.</summary>
    public int Pairs { get; init; } = 20;
    public ulong Seed { get; init; } = 1;
    /// <summary>The body's numbers: a movement profile's, as play gives the player (the program's <c>--profile</c>), else the sandbox's.</summary>
    public Locomotion Numbers { get; init; } = Locomotion.Sandbox;
    /// <summary>The name of the profile <see cref="Numbers"/> are, for the summary (<c>human</c>, <c>elder</c>, …).</summary>
    public string Profile { get; init; } = "sandbox";
    /// <summary>Whether <see cref="Numbers"/> are the profile's held to the human's (<c>masquerade</c>) or its own (<c>unbound</c>), for the summary.</summary>
    public string Mode { get; init; } = "unbound";
    /// <summary>Notes the caller has about the run (what the body is, when it is not a profile), written first as <c>{"kind":"note"}</c> lines.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
    public Gait Gait { get; init; } = Gait.Run;
    /// <summary>
    /// The half-life of the body's starts and stops on the spring (s; <see cref="BodyMotor.Spring"/>), as play gives the
    /// player in a world on it (<see cref="StartStopSprings"/>); 0, the earlier build's fixed ramp (the default, and with <c>accel-ramp</c>).
    /// </summary>
    public float HalfLife { get; init; }
    /// <summary>With the spring, its landings as before (the switch <c>landing-slide</c>); by default they keep only the speed along the way asked.</summary>
    public bool LandingSlide { get; init; }
    /// <summary>The farthest a pair's goal is looked for from its start (m, over the mesh).</summary>
    public float Radius { get; init; } = 60f;
    /// <summary>The shortest route worth driving (m).</summary>
    public float MinimumLength { get; init; } = 5f;
    /// <summary>No progress for this long while the route goes on is a snag (s).</summary>
    public float StuckSeconds { get; init; } = 1.5f;
    /// <summary>Ending a tick deeper than this inside a triangle is being embedded (m).</summary>
    public float EmbeddedDepth { get; init; } = 0.001f;
    /// <summary>Where the JSON lines are written as they are found, or null.</summary>
    public TextWriter? Output { get; init; }
}

/// <summary>One finding: what (<c>stuck</c>, <c>off-mesh</c>, <c>fell</c>, <c>embedded</c>, <c>no-ground</c>), on which pair, when, where, and at which corner of how many.</summary>
public readonly record struct SnagFinding(string Kind, int Pair, long Tick, Vector3 At, int Corner, int Corners, float Value);

/// <summary>A pair driven: its ends, the route's corners and length, and whether the body arrived and how long it took.</summary>
public readonly record struct SnagPair(int Pair, Vector3 From, Vector3 To, int Corners, float Length, bool Arrived, float Seconds);

/// <summary>The body a snag check drives: a movement profile's name, its numbers, and whether they are held to the human's (<c>masquerade</c>) or its own (<c>unbound</c>).</summary>
public readonly record struct SnagBody(string Profile, Locomotion Numbers, string Mode)
{
    /// <summary>The half-life of its starts and stops on the spring in that mode (s; 0 the ramp; <see cref="MovementProfiles.HalfLife"/>).</summary>
    public float HalfLife { get; init; }
}

/// <summary>What a run found: every pair, every finding, and the JSON lines that say so.</summary>
public sealed class SnagReport
{
    public List<SnagPair> Pairs { get; } = [];
    public List<SnagFinding> Findings { get; } = [];
    public List<string> Lines { get; } = [];
    /// <summary>
    /// What the run says about the whole of it rather than a place, each also a <c>{"kind":"note"}</c> line:
    /// the package's navigation baked for an agent that differs from the body (<see cref="NavAgent.Note"/>), its
    /// cells baked for different agents. Not findings: they do not change the exit code.
    /// </summary>
    public List<string> Notes { get; } = [];
    /// <summary>The agent the package's navigation was baked for (its cells' <c>NAVM</c>), or null when it does not say.</summary>
    public NavAgent? Agent { get; set; }
    public int Arrived => Pairs.Count(p => p.Arrived);
    public int Count(string kind) => Findings.Count(f => f.Kind == kind);
}

/// <summary>
/// <b>snagcheck</b> (ours, headless, for cell levels): bodies driven
/// by the real <see cref="BodyMotor"/> along Detour routes between seeded random pairs of reachable places,
/// one pair at a time, steering corner to corner as <see cref="RouteSystem"/> does. It reports, as JSON lines,
/// where a body is <b>stuck</b> (no progress for <see cref="SnagCheckOptions.StuckSeconds"/> while its
/// route goes on), leaves the walkable area unexpectedly (<b>off-mesh</b>: no walkable polygon within
/// 0.6 m across and 1 m up or down; <b>fell</b>: a landing from higher than a step), or ends a tick
/// <b>embedded</b> in a triangle; then a pair line for each pair and a summary.
/// </summary>
public static class SnagCheck
{
    /// <summary>
    /// The body for profile <paramref name="asked"/> (its name or id; null for the human, else the first) as play gives
    /// the player: in the masquerade, so held to the human's numbers, as the player starts in a public place;
    /// its own numbers when <paramref name="unbound"/>. Null when there are no profiles or none of that name.
    /// </summary>
    public static SnagBody? BodyOf(MovementProfiles profiles, string? asked, bool unbound)
    {
        if (profiles.All.Count == 0) return null;
        int i = asked is null ? (profiles.HumanIndex >= 0 ? profiles.HumanIndex : 0) : profiles.IndexOf(asked);
        if (i < 0) return null;
        var mode = unbound ? MasqueradeMode.Unbound : MasqueradeMode.Masquerade;
        return new SnagBody(profiles.All[i].Name, profiles.Numbers(i, mode), unbound ? "unbound" : "masquerade") { HalfLife = profiles.HalfLife(i, mode) };
    }

    public static SnagReport Run(LevelSpace level, SnagCheckOptions? options = null)
    {
        var o = options ?? new SnagCheckOptions();
        var report = new SnagReport();
        var nav = level.Navigation;
        if (nav is null)
        {
            Emit(report, o, "{\"summary\":true,\"error\":\"the level has no navigation mesh\"}");
            return report;
        }
        var rng = new Rng(o.Seed ^ Fnv1a64.Of("snagcheck"));
        var loco = o.Numbers;
        // The mesh's agent against the body: a mesh baked for a smaller or bolder agent plans routes the body cannot take.
        report.Agent = nav.Agent;
        foreach (var given in o.Notes) Note(report, o, given);
        if (nav.Agent is { } agent && agent.Note(loco) is { Length: > 0 } note) Note(report, o, note);
        if (nav.MixedAgents) Note(report, o, "the package's cells were baked for different agents; the summary gives the first cell's");
        var corners = new Vector3[LevelNavigation.MaxCorners];
        var probe = new Vector2(0.6f, 1.0f);
        long tick = 0;
        for (int pair = 0; pair < o.Pairs; pair++)
        {
            // A pair: a random place, a reachable one round it, and a route between them long enough to drive.
            Vector3 from = default, to = default;
            int n = -1;
            for (int attempt = 0; attempt < 50 && n < 0; attempt++)
            {
                if (!nav.RandomPoint(rng, out from) || !nav.RandomReachable(from, o.Radius, rng, out to)) continue;
                n = nav.Route(from, to, corners);
                if (n > 0 && Length(from, corners, n) < o.MinimumLength) n = -1;
            }
            if (n <= 0) continue;
            float length = Length(from, corners, n);
            var body = Body.At(from + new Vector3(0, 0, 0.05f), 0f);
            var spring = new StartStopSpring { HalfLife = o.HalfLife };   // 0, the ramp
            var idle = new MoveIntent { Gait = o.Gait };
            bool[] said = new bool[5];
            int settle = 0;
            for (; settle < World.TicksPerSecond && body.State != BodyState.Ground; settle++)
                BodyMotor.Step(ref body, ref spring, loco, idle, level.Collision, [], 0, tick++, landingSlide: o.LandingSlide);
            if (body.State != BodyState.Ground)
            {
                Find(report, o, new SnagFinding("no-ground", pair, tick, body.Feet, 0, n, 0f));
                report.Pairs.Add(new SnagPair(pair, from, to, n, length, false, 0f));
                Emit(report, o, PairLine(report.Pairs[^1]));
                continue;
            }
            int next = 0;
            float nearest = float.MaxValue, stalled = 0f, seconds = 0f;
            float limit = length / MathF.Max(0.5f, loco.SpeedOf(o.Gait)) * 2f + 10f;
            bool arrived = false;
            while (seconds < limit)
            {
                var corner = corners[next];
                float across = RouteSteer.Across(body.Feet, corner);
                bool last = next == n - 1;
                if (across < (last ? RouteSteer.ArriveRadius : RouteSteer.CornerRadius))
                {
                    if (last) { arrived = true; break; }
                    next++; nearest = float.MaxValue; stalled = 0f;
                    continue;
                }
                if (across < nearest - RouteSteer.Progress) { nearest = across; stalled = 0f; }
                else if ((stalled += World.TickSeconds) >= o.StuckSeconds)
                {
                    Find(report, o, new SnagFinding("stuck", pair, tick, body.Feet, next, n, stalled));
                    break;
                }
                var intent = RouteSteer.Toward(body.Feet, corner, o.Gait);
                var outcome = BodyMotor.Step(ref body, ref spring, loco, intent, level.Collision, [], 0, tick++, landingSlide: o.LandingSlide);
                seconds += World.TickSeconds;
                if (outcome.Landed && outcome.FallHeight > loco.StepHeight + 0.05f && !said[2])
                {
                    said[2] = true;
                    Find(report, o, new SnagFinding("fell", pair, tick, body.Feet, next, n, outcome.FallHeight));
                }
                // The body's own capsule as it is (its head may be kept down on stairs, Body.Stoop).
                float depth = level.Collision.Penetration(BodyMotor.CapsuleOf(body, loco));
                if (depth > o.EmbeddedDepth && !said[3])
                {
                    said[3] = true;
                    Find(report, o, new SnagFinding("embedded", pair, tick, body.Feet, next, n, depth));
                }
                if (tick % 30 == 0 && !said[1] && nav.Nearest(body.Feet, out _, probe) == 0)
                {
                    said[1] = true;
                    Find(report, o, new SnagFinding("off-mesh", pair, tick, body.Feet, next, n, 0f));
                }
            }
            report.Pairs.Add(new SnagPair(pair, from, to, n, length, arrived, seconds));
            Emit(report, o, PairLine(report.Pairs[^1]));
        }
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{{\"summary\":true,\"level\":\"{Escape(level.Name)}\",\"pairs\":{report.Pairs.Count},\"arrived\":{report.Arrived}");
        foreach (var kind in new[] { "stuck", "off-mesh", "fell", "embedded", "no-ground" })
            sb.Append(CultureInfo.InvariantCulture, $",\"{kind}\":{report.Count(kind)}");
        // The pace, the profile and its body, and the package's navigation agent with where it differs from the body.
        sb.Append(CultureInfo.InvariantCulture, $",\"pace\":\"{PaceWord(o.Gait)}\",\"profile\":\"{Escape(o.Profile)}\",\"mode\":\"{Escape(o.Mode)}\",\"seed\":{o.Seed}");
        // How the body starts and stops: the spring's half-life, or the ramp.
        sb.Append(CultureInfo.InvariantCulture, $",\"accel\":\"{(o.HalfLife > 0f ? $"spring {o.HalfLife:0.###} s" : "ramp")}\"");
        sb.Append(CultureInfo.InvariantCulture, $",\"body\":{{\"radius\":{loco.Radius:0.###},\"height\":{loco.Height:0.###},\"step\":{loco.StepHeight:0.###},\"slope\":{loco.SteepestGroundDegrees:0.###}}}");
        sb.Append(",\"navAgent\":").Append(nav.Agent is { } a ? a.Json() : "null");
        sb.Append(",\"navAgentDiffers\":[").Append(nav.Agent is { } d ? string.Join(",", d.DiffersFrom(loco).Select(w => $"\"{w}\"")) : "").Append(']');
        // The ledge filter the mesh was baked with (level.json's navigation.ledges): "stairs" for the cook's own, else "recast".
        sb.Append(",\"navLedges\":\"").Append(Escape(level.NavLedges ?? "recast")).Append('"');
        sb.Append(",\"notes\":").Append(report.Notes.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append('}');
        Emit(report, o, sb.ToString());
        return report;
    }

    static float Length(Vector3 from, Vector3[] corners, int n)
    {
        float l = 0f; var at = from;
        for (int i = 0; i < n; i++) { l += Vector3.Distance(at, corners[i]); at = corners[i]; }
        return l;
    }

    static void Find(SnagReport report, SnagCheckOptions o, SnagFinding f)
    {
        report.Findings.Add(f);
        Emit(report, o, string.Create(CultureInfo.InvariantCulture,
            $"{{\"kind\":\"{f.Kind}\",\"pair\":{f.Pair},\"tick\":{f.Tick},\"at\":{V(f.At)},\"corner\":{f.Corner},\"of\":{f.Corners},\"value\":{f.Value:0.###}}}"));
    }

    static void Note(SnagReport report, SnagCheckOptions o, string text)
    {
        report.Notes.Add(text);
        Emit(report, o, $"{{\"kind\":\"note\",\"text\":\"{Escape(text)}\"}}");
    }

    /// <summary>The pace as the summary and <c>--gait</c> say it.</summary>
    public static string PaceWord(Gait gait) => gait switch { Gait.Walk => "walk", Gait.Sprint => "sprint", _ => "run" };

    /// <summary>A string's characters made safe inside a JSON string's quotes.</summary>
    static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
            if (c is '"' or '\\') sb.Append('\\').Append(c);
            else if (c < ' ') sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
            else sb.Append(c);
        return sb.ToString();
    }

    static string PairLine(SnagPair p) => string.Create(CultureInfo.InvariantCulture,
        $"{{\"pair\":{p.Pair},\"from\":{V(p.From)},\"to\":{V(p.To)},\"corners\":{p.Corners},\"length\":{p.Length:0.##},\"arrived\":{(p.Arrived ? "true" : "false")},\"seconds\":{p.Seconds:0.##}}}");

    static string V(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"[{v.X:0.###},{v.Y:0.###},{v.Z:0.###}]");

    static void Emit(SnagReport report, SnagCheckOptions o, string line)
    {
        report.Lines.Add(line);
        o.Output?.WriteLine(line);
    }
}
