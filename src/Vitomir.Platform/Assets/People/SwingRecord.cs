// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The character models' swing record (the layout of their swing bones' kit) as read from a .glb.
using System.Numerics;
using System.Text.Json;
using Vitomir.Formats.Gltf;

namespace Vitomir.Platform.Assets.People;

/// <summary>
/// One swing chain of the record (<c>chains</c>): its bones root first (the root a child of a body bone), the stuff's numbers and what it
/// is kept out of. Every field is the record's, by its name there; directions and angles in the models repository's skeleton space (Blender's: Z up,
/// +X the character's left, +Y behind), turned into the platform's by <see cref="Animation.People.SwingRule"/>.
/// </summary>
public sealed record SwingChain
{
    public required string[] Bones { get; init; }
    /// <summary><c>gravity</c>, <c>stiffness</c> (m/s²: toward the rest direction, toward the parent's lead) and <c>drag</c> (per second).</summary>
    public float Gravity { get; init; }
    public float Stiffness { get; init; }
    public float Drag { get; init; }
    /// <summary><c>radius</c> (m) and <c>radii</c> (one for each bone, used instead), against the colliders.</summary>
    public float Radius { get; init; }
    public float[]? Radii { get; init; }
    /// <summary><c>colliders</c> and <c>middles</c>: groups' names (null: none).</summary>
    public string? Colliders { get; init; }
    public string? Middles { get; init; }
    /// <summary><c>side_deg</c>: the way the chain lies from the body's upright axis at rest (degrees; 0 the left, 90 behind), or null.</summary>
    public float? SideDeg { get; init; }
    /// <summary><c>carried</c>: the share of its head's own travel given to the tail outright (0 to 1).</summary>
    public float Carried { get; init; }
    /// <summary>Heavy stuff (mail): <c>fall_drag</c> (null: none), <c>settle</c> (0: none) and <c>settle_within</c> (degrees; 10).</summary>
    public float? FallDrag { get; init; }
    public float Settle { get; init; }
    public float SettleWithin { get; init; } = 10f;
    /// <summary><c>along</c> (fractions of each bone also tested against the middles), <c>across</c> with <c>across_dir</c> (a broad thing).</summary>
    public float[]? Along { get; init; }
    public float[][]? Across { get; init; }
    public Vector3? AcrossDir { get; init; }
    /// <summary><c>lengths</c>: each bone's length (m), or null (a record from before the models repository).</summary>
    public float[]? Lengths { get; init; }
}

/// <summary>
/// One capsule of a collider group: between two body bones' heads (<c>from</c>, <c>to</c>; an end may be <c>at</c> of the way toward
/// another's: <c>from_toward</c>, <c>to_toward</c>), the part <c>span</c> of that line, <c>radius</c>, <c>fixed</c> (not refitted), <c>within</c>
/// (m: there only while its ends are nearer; 0 always) and <c>sided</c>.
/// </summary>
public sealed record SwingCapsule
{
    public required string From { get; init; }
    public required string To { get; init; }
    public float Radius { get; init; }
    public float SpanFrom { get; init; }
    public float SpanTo { get; init; } = 1f;
    public bool Fixed { get; init; }
    public string? FromToward { get; init; }
    public string? ToToward { get; init; }
    public float At { get; init; } = 0.5f;
    public float Within { get; init; }
    public bool Sided { get; init; }
}

/// <summary>A web: neighbouring chains (indices into the record's chains, in order round the garment), <c>closed</c>, <c>stretch</c> (0: none).</summary>
public sealed record SwingWeb(int[] Chains, bool Closed, float Stretch);

/// <summary>
/// A person's swing record: every worn file's (<c>extras.vitomir.swing</c> on its skeleton's node) merged as the models repository's
/// <c>merge_specs</c> merges them (the chains one after another, the webs' indices moved with them, a later file's collider group of a name in
/// place of an earlier's).
/// </summary>
public sealed record SwingRecord(IReadOnlyList<SwingChain> Chains, IReadOnlyList<KeyValuePair<string, SwingCapsule[]>> Colliders, IReadOnlyList<SwingWeb> Webs)
{
    /// <summary>The record of <paramref name="file"/> (the first node carrying one), or null.</summary>
    public static SwingRecord? Of(GltfFile file)
    {
        foreach (var node in file.Nodes)
            if (node.Extras is { Length: > 0 } extras && Parse(extras) is { } record) return record;
        return null;
    }

    /// <summary>The record in a node's extras (<c>{"vitomir": {"swing": …}}</c>), or null.</summary>
    public static SwingRecord? Parse(string extras)
    {
        using var doc = JsonDocument.Parse(extras);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("vitomir", out var v) || v.ValueKind != JsonValueKind.Object
            || !v.TryGetProperty("swing", out var s) || s.ValueKind != JsonValueKind.Object) return null;
        var chains = new List<SwingChain>();
        if (s.TryGetProperty("chains", out var cs) && cs.ValueKind == JsonValueKind.Array)
            foreach (var c in cs.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object || !c.TryGetProperty("bones", out var bones) || bones.ValueKind != JsonValueKind.Array) continue;
                chains.Add(new SwingChain
                {
                    Bones = [.. bones.EnumerateArray().Select(b => b.GetString() ?? "")],
                    Gravity = Num(c, "gravity", 0f), Stiffness = Num(c, "stiffness", 0f), Drag = Num(c, "drag", 0f),
                    Radius = Num(c, "radius", 0f), Radii = Nums(c, "radii"),
                    Colliders = Str(c, "colliders"), Middles = Str(c, "middles"),
                    SideDeg = c.TryGetProperty("side_deg", out var sd) && sd.ValueKind == JsonValueKind.Number ? sd.GetSingle() : null,
                    Carried = Num(c, "carried", 0f),
                    FallDrag = c.TryGetProperty("fall_drag", out var fd) && fd.ValueKind == JsonValueKind.Number ? fd.GetSingle() : null,
                    Settle = Num(c, "settle", 0f), SettleWithin = Num(c, "settle_within", 10f),
                    Along = Nums(c, "along"),
                    Across = c.TryGetProperty("across", out var ac) && ac.ValueKind == JsonValueKind.Array
                        ? [.. ac.EnumerateArray().Select(a => a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetSingle()).ToArray() : [])]
                        : null,
                    AcrossDir = Nums(c, "across_dir") is { Length: 3 } d ? new Vector3(d[0], d[1], d[2]) : null,
                    Lengths = Nums(c, "lengths"),
                });
            }
        var colliders = new List<KeyValuePair<string, SwingCapsule[]>>();
        if (s.TryGetProperty("colliders", out var gs) && gs.ValueKind == JsonValueKind.Object)
            foreach (var g in gs.EnumerateObject())
            {
                if (g.Value.ValueKind != JsonValueKind.Array) continue;
                var caps = new List<SwingCapsule>();
                foreach (var c in g.Value.EnumerateArray())
                {
                    if (c.ValueKind != JsonValueKind.Object || Str(c, "from") is not { } from || Str(c, "to") is not { } to) continue;
                    var span = Nums(c, "span");
                    caps.Add(new SwingCapsule
                    {
                        From = from, To = to, Radius = Num(c, "radius", 0f),
                        SpanFrom = span is { Length: 2 } ? span[0] : 0f, SpanTo = span is { Length: 2 } ? span[1] : 1f,
                        Fixed = c.TryGetProperty("fixed", out var f) && f.ValueKind == JsonValueKind.True,
                        FromToward = Str(c, "from_toward"), ToToward = Str(c, "to_toward"), At = Num(c, "at", 0.5f),
                        Within = Num(c, "within", 0f),
                        Sided = c.TryGetProperty("sided", out var sided) && sided.ValueKind == JsonValueKind.True,
                    });
                }
                colliders.RemoveAll(k => k.Key == g.Name);
                colliders.Add(new(g.Name, [.. caps]));
            }
        var webs = new List<SwingWeb>();
        if (s.TryGetProperty("webs", out var ws) && ws.ValueKind == JsonValueKind.Array)
            foreach (var w in ws.EnumerateArray())
                if (w.ValueKind == JsonValueKind.Object && w.TryGetProperty("chains", out var wc) && wc.ValueKind == JsonValueKind.Array)
                    webs.Add(new SwingWeb([.. wc.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32())],
                                          w.TryGetProperty("closed", out var cl) && cl.ValueKind == JsonValueKind.True, Num(w, "stretch", 0f)));
        return chains.Count > 0 ? new SwingRecord(chains, colliders, webs) : null;
    }

    /// <summary>One record from several files' in order (the models repository's <c>merge_specs</c>), or null when none has a chain.</summary>
    public static SwingRecord? Merge(IEnumerable<SwingRecord?> records)
    {
        var chains = new List<SwingChain>();
        var colliders = new List<KeyValuePair<string, SwingCapsule[]>>();
        var webs = new List<SwingWeb>();
        foreach (var r in records)
        {
            if (r is null) continue;
            int off = chains.Count;
            chains.AddRange(r.Chains);
            foreach (var g in r.Colliders)
            {
                int at = colliders.FindIndex(k => k.Key == g.Key);   // a dict's update: a name already there keeps its place
                if (at >= 0) colliders[at] = g; else colliders.Add(g);
            }
            webs.AddRange(r.Webs.Select(w => w with { Chains = [.. w.Chains.Select(c => c + off)] }));
        }
        return chains.Count > 0 ? new SwingRecord(chains, colliders, webs) : null;
    }

    static float Num(JsonElement e, string name, float fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    static float[]? Nums(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetSingle())] : null;

    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
