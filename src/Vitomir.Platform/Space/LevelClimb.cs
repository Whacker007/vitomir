// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Formats.L2;

namespace Vitomir.Platform.Space;

/// <summary>One of a level's areas that is not to be climbed in: its name and its box.</summary>
public sealed record NoClimbArea(string Name, TurnedBox Box);

/// <summary>
/// One of a level's areas whose upright faces are <b>walls with holds</b> (<c>"climb": "holds"</c>): anyone may climb them, masquerade or not.
/// Its name, its box and the level's own holds in it (<c>"holds": [[x, y, z], ...]</c>: each hold's top middle, metres, the level's frame;
/// empty: the holds are a lattice on the face at the clips' spacing, BodyMotor.Wall.cs).
/// </summary>
public sealed record HoldsArea(string Name, TurnedBox Box, Vector3[] Holds);

/// <summary>
/// <b>Where a level says nothing may be climbed</b> (ours, the geometry decides
/// what can be climbed, plus a cheap flag a level sets on an object or an area — a cart's top, a quest's edge, a roof
/// that must not be reached — instead of invisible blockers). This is the area half: each of the manifest's areas
///  whose <c>climb</c> key says <c>"none"</c> (the manifest keeps an area's keys it does
/// not know, <see cref="L2Area.Unknown"/>). A lip inside one of their boxes is never taken (<see
/// cref="Bodies.TraversalProbe"/>); walking, stepping and standing there are as anywhere. The object half is the
/// collision's <see cref="L2Surface.NoClimb"/>. A value that is not <c>"none"</c> is a problem said once
/// (<see cref="Problems"/>) and the area counts as unmarked. Read only while the world runs; the boxes' turns are worked
/// out once with <see cref="Core.DetMath"/> (<see cref="TurnedBox"/>), so the answer is the same on every machine.
/// </summary>
public sealed class LevelClimb
{
    /// <summary>The key an area says it with, and its one word.</summary>
    public const string Key = "climb", NoneWord = "none";

    /// <summary>The word that marks an area's faces walls with holds, and the key of the level's own holds in it.</summary>
    public const string HoldsWord = "holds", HoldsKey = "holds";

    readonly NoClimbArea[] areas;
    readonly HoldsArea[] holds;

    /// <summary>A level with no area marked: lips are taken wherever the geometry offers them.</summary>
    public static LevelClimb None { get; } = new([], []);

    LevelClimb(NoClimbArea[] areas, IReadOnlyList<string> problems, HoldsArea[]? holds = null)
    {
        this.areas = areas;
        this.holds = holds ?? [];
        Problems = problems;
    }

    /// <summary>The areas whose faces are walls with holds, in the manifest's order.</summary>
    public IReadOnlyList<HoldsArea> HoldsAreas => holds;

    /// <summary>The first area of walls with holds whose box holds <paramref name="point"/>, or null.</summary>
    public HoldsArea? HoldsAt(Vector3 point)
    {
        for (int i = 0; i < holds.Length; i++)
            if (holds[i].Box.Contains(point)) return holds[i];
        return null;
    }

    /// <summary>
    /// The level's own hold nearest <paramref name="point"/> within <paramref name="reach"/> (m) of an area holding the point that gives its holds, false
    /// for none (an area of the lattice gives none: <see cref="HoldsArea.Holds"/> empty).
    /// </summary>
    public bool HoldNear(Vector3 point, float reach, out Vector3 hold)
    {
        hold = default;
        float best = reach * reach;
        bool found = false;
        for (int i = 0; i < holds.Length; i++)
        {
            if (holds[i].Holds.Length == 0 || !holds[i].Box.Contains(point)) continue;
            foreach (var h in holds[i].Holds)
            {
                float d = Vector3.DistanceSquared(h, point);
                if (d <= best) { best = d; hold = h; found = true; }
            }
        }
        return found;
    }

    /// <summary>The areas not to be climbed in, in the manifest's order.</summary>
    public IReadOnlyList<NoClimbArea> Areas => areas;

    /// <summary>What was wrong with the marks (a value other than <c>none</c>, a box that is not sound), one line each.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>Whether <paramref name="point"/> (metres, the level's frame) is inside an area not to be climbed in.</summary>
    public bool NoClimbAt(Vector3 point)
    {
        for (int i = 0; i < areas.Length; i++)
            if (areas[i].Box.Contains(point)) return true;
        return false;
    }

    /// <summary>The marked areas of a manifest.</summary>
    public static LevelClimb From(L2Manifest manifest)
    {
        var problems = new List<string>();
        var found = new List<NoClimbArea>();
        var walls = new List<HoldsArea>();
        foreach (var a in manifest.Areas ?? [])
        {
            if (a.Unknown is null || !a.Unknown.TryGetValue(Key, out var v)) continue;
            string? word = v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (word == HoldsWord)
            {
                // Walls with holds, and the level's own holds when it gives them (each [x, y, z]; one that does not read is a problem, passed over).
                if (TurnedBox.From(a.Box) is not { } hb) { problems.Add($"area {a.Name}: its box is not sound; its climb mark is passed over"); continue; }
                var points = new List<Vector3>();
                if (a.Unknown.TryGetValue(HoldsKey, out var list))
                {
                    if (list.ValueKind != JsonValueKind.Array) problems.Add($"area {a.Name}: holds is not a list; the lattice is taken");
                    else
                        foreach (var h in list.EnumerateArray())
                        {
                            if (h.ValueKind == JsonValueKind.Array && h.GetArrayLength() == 3 && h[0].ValueKind == JsonValueKind.Number && h[1].ValueKind == JsonValueKind.Number
                                && h[2].ValueKind == JsonValueKind.Number)
                                points.Add(new Vector3((float)h[0].GetDouble(), (float)h[1].GetDouble(), (float)h[2].GetDouble()));
                            else problems.Add($"area {a.Name}: a hold {h.GetRawText()} is not [x, y, z]; passed over");
                        }
                }
                walls.Add(new HoldsArea(a.Name, hb, [.. points]));
                continue;
            }
            if (word != NoneWord)
            {
                problems.Add($"area {a.Name}: climb is {v.GetRawText()}, not \"{NoneWord}\" or \"{HoldsWord}\"; the area is taken as unmarked (climbed as its geometry allows)");
                continue;
            }
            if (TurnedBox.From(a.Box) is not { } box) { problems.Add($"area {a.Name}: its box is not sound; its climb mark is passed over"); continue; }
            found.Add(new NoClimbArea(a.Name, box));
        }
        return found.Count == 0 && problems.Count == 0 && walls.Count == 0 ? None : new LevelClimb([.. found], problems, [.. walls]);
    }

    /// <summary>Areas made in code (tests, tools).</summary>
    public static LevelClimb Of(IEnumerable<NoClimbArea> areas) => new([.. areas], []);

    /// <summary>Areas made in code, with walls with holds (tests, tools).</summary>
    public static LevelClimb Of(IEnumerable<NoClimbArea> areas, IEnumerable<HoldsArea> holds) => new([.. areas], [], [.. holds]);
}
