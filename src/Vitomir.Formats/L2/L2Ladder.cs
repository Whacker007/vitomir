// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Vitomir.Formats.L2;

/// <summary>
/// <b>What a level says about traversal that the platform reads at run time</b> (ours, manifest key
/// <c>traversal</c>): today its ladders. The layout's own <c>traversal</c> object also
/// names the meshes not to be climbed or stood on, but those the cook turns into the collision's flags and never
/// carries here; only <see cref="Ladders"/> come through. Absent when a level has none, so a package cooked before,
/// or from a layout without ladders, is written and read as it was. A build that does not know the key keeps it.
/// </summary>
public sealed class L2Traversal
{
    /// <summary>The level's ladders, in the layout's order (their index is their name in the log when they have none).</summary>
    [JsonPropertyName("ladders")] public List<L2Ladder>? Ladders { get; set; }

    /// <summary>Properties this build does not know, kept for the write.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>
    /// The traversal a generator's <c>layout.json</c> gives for the manifest: its <c>traversal.ladders</c>, each read as
    /// an <see cref="L2Ladder"/> (what else a ladder says kept); null when there are none. A ladder that is not sound
    /// (<see cref="L2Ladder.Fault"/>) is left out and said in <paramref name="faults"/>: the cook stops on one, since a
    /// ladder dropped in silence is a way up that is not there.
    /// </summary>
    public static L2Traversal? FromLayout(JsonNode? layout, List<string>? faults = null)
    {
        if (layout is not JsonObject || layout["traversal"]?["ladders"] is not JsonArray list) return null;
        var ladders = new List<L2Ladder>();
        for (int n = 0; n < list.Count; n++)
        {
            L2Ladder? ladder;
            try { ladder = list[n].Deserialize<L2Ladder>(L2Manifest.Json); }
            catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
            {
                faults?.Add($"ladder {n}: {e.Message}");
                continue;
            }
            if (ladder is null) { faults?.Add($"ladder {n}: not an object"); continue; }
            if (ladder.Fault(n) is { } fault) { faults?.Add(fault); continue; }
            ladders.Add(ladder);
        }
        return ladders.Count > 0 ? new L2Traversal { Ladders = ladders } : null;
    }
}

/// <summary>
/// <b>A ladder</b> (ours; 1.1 has none): a straight line from a
/// <see cref="Foot"/> to a <see cref="Top"/>, <see cref="Width"/> wide, climbed from the side <see cref="Facing"/> looks
/// to. In metres in the level's frame, Z up; the facing in degrees counter-clockwise from +X, as every yaw of the format.
///
/// <list type="bullet">
/// <item><see cref="Foot"/>: the middle of the ladder's climbing face where it meets the ground below — the face the
/// hands and feet touch (the rails' front), at the ground's height.</item>
/// <item><see cref="Top"/>: the middle of that face at the height of the floor it leads onto (the top's edge): a body
/// climbs until its hands are over it and steps off onto the floor beyond, away from <see cref="Facing"/>.</item>
/// <item><see cref="Facing"/>: the way the climbing side looks out from the ladder (the outward normal of its face,
/// level): a body on it is on that side and faces the opposite way, towards the ladder.</item>
/// </list>
///
/// The ladder's collision is the level's own (a mesh that draws it, solid or passable, or none): the platform keeps
/// a climbing body a little off the face, so rails and rungs behind the face are never met. A ladder may lean (the
/// foot out from under the top) up to 45°.
/// </summary>
public sealed class L2Ladder
{
    /// <summary>The steepest lean from upright a ladder may have (its rise at least its run).</summary>
    public const float MostLean = 1f;

    /// <summary>The least rise a ladder has (m).</summary>
    public const float LeastRise = 0.5f;

    /// <summary>What the ladder is, for a reader and the log (optional).</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>The middle of the climbing face at the ground (m).</summary>
    [JsonPropertyName("foot")] public float[] Foot { get; set; } = [];
    /// <summary>The middle of the climbing face at the top floor's height (m).</summary>
    [JsonPropertyName("top")] public float[] Top { get; set; } = [];
    /// <summary>How wide it is between the rails' outsides (m); 0.5 when not given.</summary>
    [JsonPropertyName("width")] public float Width { get; set; } = 0.5f;
    /// <summary>The way its climbing side looks out (degrees, counter-clockwise from +X).</summary>
    [JsonPropertyName("facing")] public float Facing { get; set; }
    /// <summary>Properties this build does not know, kept for the write.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>
    /// What is wrong with the ladder, or null when it is sound: a foot and a top of three finite numbers each, the top
    /// at least <see cref="LeastRise"/> above the foot and leaning no more than 45°, a finite width above zero and a
    /// finite facing. <paramref name="index"/> names it when it has no name.
    /// </summary>
    public string? Fault(int index = 0)
    {
        string who = string.IsNullOrEmpty(Name) ? $"ladder {index}" : $"ladder {Name}";
        if (Foot is not { Length: 3 } || !Finite(Foot)) return $"{who}: its foot is not three finite numbers";
        if (Top is not { Length: 3 } || !Finite(Top)) return $"{who}: its top is not three finite numbers";
        float rise = Top[2] - Foot[2];
        if (!(rise >= LeastRise)) return $"{who}: its top is {rise:0.###} m above its foot, less than {LeastRise} m";
        float dx = Top[0] - Foot[0], dy = Top[1] - Foot[1];
        if (MathF.Sqrt(dx * dx + dy * dy) > rise * MostLean) return $"{who}: it leans more than 45° from upright";
        if (!(Width > 0f) || !float.IsFinite(Width)) return $"{who}: its width is {Width}, not a length above zero";
        if (!float.IsFinite(Facing)) return $"{who}: its facing is not a finite number";
        return null;
    }

    static bool Finite(float[] v) => float.IsFinite(v[0]) && float.IsFinite(v[1]) && float.IsFinite(v[2]);

    public override string ToString() =>
        $"{(string.IsNullOrEmpty(Name) ? "(unnamed)" : Name)}: ({Foot[0]:0.##}, {Foot[1]:0.##}, {Foot[2]:0.##}) up to " +
        $"({Top[0]:0.##}, {Top[1]:0.##}, {Top[2]:0.##}), {Width:0.##} m wide, facing {Facing:0.#}°";
}
