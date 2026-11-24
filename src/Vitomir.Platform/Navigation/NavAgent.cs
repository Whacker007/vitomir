// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Formats.L2;
using Vitomir.Platform.Bodies;

namespace Vitomir.Platform.Navigation;

/// <summary>
/// The agent a level's navigation mesh was baked for, as each cell's <c>NAVM</c> chunk records it
/// (ours): its radius, height and climb (metres), the steepest slope it walks (degrees), and the
/// voxels' size and height. The cook bakes for <see cref="CookDefault"/> unless told otherwise
/// (<c>--nav-agent</c>); the platform's human is 0.3 m round, 1.8 m tall, stepping 0.35 m, walking 40°.
/// </summary>
public readonly record struct NavAgent(float Radius, float Height, float Climb, float MaxSlope, float CellSize, float CellHeight)
{
    /// <summary>The cook's own default agent (its <c>NavSettings.Default</c>): 0.25 m round, 1.7 m tall, climbing 0.4 m, on slopes to 45°.</summary>
    public static readonly NavAgent CookDefault = new(0.25f, 1.7f, 0.4f, 45f, 0.15f, 0.05f);

    /// <summary>A number counts as the body's within this (metres, degrees): both are written as decimals and read as floats.</summary>
    const float Slack = 1e-4f;

    /// <summary>The agent a cell's <c>NAVM</c> chunk records.</summary>
    public static NavAgent Of(L2NavMesh chunk) =>
        new(chunk.AgentRadius, chunk.AgentHeight, chunk.AgentClimb, chunk.MaxSlope, chunk.CellSize, chunk.CellHeight);

    /// <summary>
    /// Where this agent asks more of <paramref name="body"/> than it can do, or less room than it needs, in this
    /// order: <c>climb</c> above the body's step, <c>slope</c> above the steepest ground it walks, <c>radius</c>
    /// below its radius, <c>height</c> below its height. Empty when the mesh's routes are ones the body can take.
    /// </summary>
    public IReadOnlyList<string> DiffersFrom(in Locomotion body)
    {
        var d = new List<string>(4);
        if (Climb > body.StepHeight + Slack) d.Add("climb");
        if (MaxSlope > body.SteepestGroundDegrees + Slack) d.Add("slope");
        if (Radius < body.Radius - Slack) d.Add("radius");
        if (Height < body.Height - Slack) d.Add("height");
        return d;
    }

    /// <summary>
    /// The note a snag check gives when the agent differs from the body (<see cref="DiffersFrom"/>), in words, or
    /// empty: "the package's navigation was baked for an agent that climbs 0.4 m (the body steps 0.35 m), ...".
    /// </summary>
    public string Note(in Locomotion body)
    {
        var parts = new List<string>(4);
        foreach (var what in DiffersFrom(body))
            parts.Add(what switch
            {
                "climb" => F($"climbs {Climb:0.###} m (the body steps {body.StepHeight:0.###} m)"),
                "slope" => F($"walks slopes to {MaxSlope:0.#}° (the body to {body.SteepestGroundDegrees:0.#}°)"),
                "radius" => F($"is {Radius:0.###} m round (the body {body.Radius:0.###} m)"),
                _ => F($"is {Height:0.###} m tall (the body {body.Height:0.###} m)"),
            });
        if (parts.Count == 0) return "";
        string list = parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        return $"the package's navigation was baked for an agent that {list}: its routes may ask more of the body than it can do (cook it with --nav-agent human, or the body's own numbers)";
    }

    /// <summary>The agent as a JSON object: <c>{"radius":0.25,"height":1.7,"climb":0.4,"slope":45,"cell":0.15,"cellHeight":0.05}</c>.</summary>
    public string Json() => F($"{{\"radius\":{Radius:0.###},\"height\":{Height:0.###},\"climb\":{Climb:0.###},\"slope\":{MaxSlope:0.###},\"cell\":{CellSize:0.###},\"cellHeight\":{CellHeight:0.###}}}");

    static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
