// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.World;

namespace Vitomir;

/// <summary>
/// **Ours, a harness.** <c>--journey "exit 10@1; exit 0@4; walk -576,1138,4@7; where@12"</c> with
/// <c>--settle</c>: steps taken during the settle, each at its time in seconds — so a headless run can leave
/// a place by an exit, come back and walk somewhere, and say where the party is at each step.
/// <list type="bullet">
/// <item><c>exit N</c>: the exit of the place the party is in, asked for as a clicked sign asks
/// (<c>CodexSequence.TakeExit</c>) — in a cell level through <see cref="CellExits.Take"/>, the
/// seam a sign's click calls;</item>
/// <item><c>walk x,y,z</c> (engine units) or <c>walkm x,y,z</c> (metres): the leader is sent there;</item>
/// <item><c>where</c>: a line naming the place, the leader's position and facing, and the area.</item>
/// </list>
/// </summary>
public sealed class HarnessJourney
{
    readonly List<(float At, string Verb, string What)> steps = [];
    int next;

    /// <summary>The steps of <paramref name="spec"/>, or null when there is none.</summary>
    public static HarnessJourney? Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var journey = new HarnessJourney();
        foreach (var part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int at = part.LastIndexOf('@');
            float when = at >= 0 && float.TryParse(part[(at + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 0f;
            string what = (at >= 0 ? part[..at] : part).Trim();
            int space = what.IndexOf(' ');
            journey.steps.Add((when, (space < 0 ? what : what[..space]).ToLowerInvariant(), space < 0 ? "" : what[(space + 1)..].Trim()));
        }
        journey.steps.Sort((a, b) => a.At.CompareTo(b.At));
        return journey;
    }

    /// <summary>Takes every step that has come due by <paramref name="t"/> seconds into the settle.</summary>
    public void Tick(Location place, Chronicle? chronicle, float t)
    {
        while (next < steps.Count && steps[next].At <= t)
        {
            var (_, verb, rest) = steps[next++];
            switch (verb)
            {
                case "exit" when int.TryParse(rest, out int n):
                    if (CellLevel.Of(place.Level) is not null) CellExits.Take(place, n);
                    else if (chronicle is null) Console.WriteLine($"journey: exit {n}: no chronicle is running here");
                    else Console.WriteLine(chronicle.TakeExit(n)
                        ? $"journey: exit {n} of {chronicle.NameOf(chronicle.Current)} taken: to {chronicle.NameOf(chronicle.Leaving?.To ?? -1)}, entrance {chronicle.Leaving?.Entrance}"
                        : $"journey: exit {n} of {chronicle.NameOf(chronicle.Current)} does not exist, is closed, or leads nowhere");
                    break;
                case "walk" or "walkm" when Point(rest) is { } p && place.Player is { } leader && place.Walk is { } walk:
                    var target = verb == "walkm" ? p * Vitomir.Formats.L2.L2Manifest.EngineUnitsPerMetre : p;
                    int node = walk.Find(target, 256f);
                    Console.WriteLine(node >= 0 && leader.WalkTo(target, node)
                        ? $"journey: walking to {target.X:0},{target.Y:0},{target.Z:0} by {leader.Route.Length} waypoints"
                        : $"journey: no way to {target.X:0},{target.Y:0},{target.Z:0}");
                    break;
                case "where":
                    Console.WriteLine("journey: " + Where(place, chronicle, t));
                    break;
                default:
                    Console.WriteLine($"journey: '{verb} {rest}' is not a step (exit N, walk x,y,z, walkm x,y,z, where)");
                    break;
            }
        }
    }

    /// <summary>Where the party is, in a line: the place, the leader's position (engine units and metres) and facing, and the area of a cell level.</summary>
    public static string Where(Location place, Chronicle? chronicle, float t)
    {
        var at = place.Player?.Position ?? place.StartPosition;
        float m = Vitomir.Formats.L2.L2Manifest.EngineUnitsPerMetre;
        string area = CellLevel.Of(place.Level) is { } cells ? $", area {cells.Areas.Current?.Name ?? "none (the street)"}" : "";
        return $"at {t:0.0}s in {(chronicle is not null ? chronicle.NameOf(chronicle.Current) + " " : "")}({place.Name}), entrance {place.Entrance}: " +
               $"the leader at {at.X:0},{at.Y:0},{at.Z:0} ({at.X / m:0.00},{at.Y / m:0.00},{at.Z / m:0.00} m), " +
               $"yaw {place.Player?.Thing.Orientation.Z ?? 0:0}{area}" + (place.Player is { Walking: true } ? ", walking" : "");
    }

    static Vector3? Point(string text)
    {
        var c = text.Split(',');
        if (c.Length != 3) return null;
        var v = new float[3];
        for (int i = 0; i < 3; i++)
            if (!float.TryParse(c[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
        return new Vector3(v[0], v[1], v[2]);
    }
}
