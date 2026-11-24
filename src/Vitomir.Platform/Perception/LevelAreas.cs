// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Perception;

/// <summary>
/// The areas perception reads from a level (ours):
/// <list type="bullet">
/// <item><b>the places its local memory is kept by</b> ("it is local"): the level's layout areas
///  but for the spots that only mark something (a <c>climb</c>, <c>stand</c>, <c>light</c> or
/// <c>conceal</c> key and no <c>masquerade</c>), and the whole level, area 0, where none holds a point
/// (<see cref="At"/>: the smallest holding it);</item>
/// <item><b>the light</b>: an area with a <c>light</c> key (a number, 0 dark to 1 daylight: 0.1 a dark alley, 0.6 a lit
/// square) has that light inside it, the smallest such area holding a point deciding (<see cref="LightAt"/>);</item>
/// <item><b>concealment</b>: an area with a <c>conceal</c> key (a number, 0 to 1: 0.2 a hedge, bushes, hay) makes a body
/// inside it that much as visible (<see cref="ConcealAt"/>; the smallest deciding).</item>
/// </list>
/// A key that is not a number from 0 to 1 is a problem said once (<see cref="Problems"/>) and passed over.
/// </summary>
public sealed class LevelAreas
{
    /// <summary>The area keys perception reads.</summary>
    public const string LightKey = "light", ConcealKey = "conceal";

    readonly (string Name, TurnedBox Box)[] areas;
    readonly (TurnedBox Box, float Value)[] lights, conceals;

    LevelAreas((string, TurnedBox)[] areas, (TurnedBox, float)[] lights, (TurnedBox, float)[] conceals, IReadOnlyList<string> problems)
    {
        this.areas = areas;
        this.lights = lights;
        this.conceals = conceals;
        Problems = problems;
    }

    /// <summary>A level with no areas: the whole level is one, its light the world's, nothing concealing.</summary>
    public static LevelAreas None { get; } = new([], [], [], []);

    /// <summary>How many areas there are, the whole level (0) among them.</summary>
    public int Count => areas.Length + 1;

    /// <summary>How many areas say their own light, and how many conceal.</summary>
    public int Lights => lights.Length;
    public int Conceals => conceals.Length;

    /// <summary>Light area <paramref name="index"/> (of <see cref="Lights"/>, smallest first): its box and its light (for the awareness overlay).</summary>
    public (TurnedBox Box, float Value) Light(int index) => lights[index];

    /// <summary>Conceal area <paramref name="index"/> (of <see cref="Conceals"/>, smallest first): its box and how visible a body in it is.</summary>
    public (TurnedBox Box, float Value) Conceal(int index) => conceals[index];

    /// <summary>What was wrong with the light and conceal keys, one line each.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>Area <paramref name="index"/>'s name: <c>the level</c> for 0.</summary>
    public string Name(int index) => index <= 0 || index > areas.Length ? "the level" : areas[index - 1].Name;

    /// <summary>The area holding <paramref name="point"/>: the smallest that does (1 and up), else the level (0).</summary>
    public int At(Vector3 point)
    {
        for (int i = 0; i < areas.Length; i++)
            if (areas[i].Box.Contains(point)) return i + 1;
        return 0;
    }

    /// <summary>The light at <paramref name="point"/>: the smallest light area's holding it, else <paramref name="world"/>.</summary>
    public float LightAt(Vector3 point, float world)
    {
        for (int i = 0; i < lights.Length; i++)
            if (lights[i].Box.Contains(point)) return lights[i].Value;
        return world;
    }

    /// <summary>How visible a body with its feet at <paramref name="point"/> is for its concealment: the smallest conceal area's holding it, else 1.</summary>
    public float ConcealAt(Vector3 point)
    {
        for (int i = 0; i < conceals.Length; i++)
            if (conceals[i].Box.Contains(point)) return conceals[i].Value;
        return 1f;
    }

    /// <summary>The areas of a manifest (each list the smallest first; of one size, the manifest's order).</summary>
    public static LevelAreas From(L2Manifest manifest)
    {
        var places = new List<(string, TurnedBox)>();
        var lights = new List<(TurnedBox, float)>();
        var conceals = new List<(TurnedBox, float)>();
        var problems = new List<string>();
        foreach (var a in manifest.Areas ?? [])
        {
            if (TurnedBox.From(a.Box) is not { } box) continue;
            var keys = a.Unknown;
            if (Number(keys, LightKey, a.Name, problems) is { } light) lights.Add((box, light));
            if (Number(keys, ConcealKey, a.Name, problems) is { } conceal) conceals.Add((box, conceal));
            bool marker = keys is not null && !keys.ContainsKey(LevelPlaces.Key)
                && (keys.ContainsKey("climb") || keys.ContainsKey("stand") || keys.ContainsKey(LightKey) || keys.ContainsKey(ConcealKey));
            if (!marker) places.Add((a.Name, box));
        }
        return Of(places, lights, conceals, problems);
    }

    /// <summary>The areas of a level package's manifest, or none when it cannot be read.</summary>
    public static LevelAreas Open(string packageFolder)
    {
        try { return From(L2Package.Open(packageFolder).Manifest); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) { return None; }
    }

    /// <summary>Areas made in code (tests): each list sorted smallest first, then as given.</summary>
    public static LevelAreas Of(IEnumerable<(string Name, TurnedBox Box)> places, IEnumerable<(TurnedBox Box, float Value)>? lights = null,
        IEnumerable<(TurnedBox Box, float Value)>? conceals = null, IReadOnlyList<string>? problems = null) =>
        new([.. Smallest(places, p => p.Box)], [.. Smallest(lights ?? [], l => l.Box)], [.. Smallest(conceals ?? [], c => c.Box)], problems ?? []);

    static IEnumerable<T> Smallest<T>(IEnumerable<T> items, Func<T, TurnedBox> box)
    {
        var list = items.Select((x, i) => (Item: x, Index: i)).ToList();
        list.Sort((x, y) =>
        {
            TurnedBox a = box(x.Item), b = box(y.Item);
            int c = (a.Size.X * a.Size.Y * a.Size.Z).CompareTo(b.Size.X * b.Size.Y * b.Size.Z);
            return c != 0 ? c : x.Index.CompareTo(y.Index);
        });
        return list.Select(l => l.Item);
    }

    static float? Number(Dictionary<string, System.Text.Json.JsonElement>? keys, string key, string area, List<string> problems)
    {
        if (keys is null || !keys.TryGetValue(key, out var v)) return null;
        if (v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetSingle(out float f) && f >= 0f && f <= 1f) return f;
        problems.Add($"area {area}: {key} is {v.GetRawText()}, not a number from 0 to 1; it is passed over");
        return null;
    }
}

/// <summary>
/// An act in view (ours): a risk to the Masquerade (<see cref="MasqueradeRisk"/>) kept while it can still be seen —
/// who did it, what, how big (its measure over a human's limit, capped), the limit (a speed's act lasts while the body
/// is over it), when it was raised and how long it has lasted.
/// </summary>
public struct ActInView
{
    public EntityId Actor;
    public MasqueradeAct Act;
    public TraversalKind Move;
    public float Size, Limit, Seconds;
    public long Raised;
}

/// <summary>
/// Perception's state outside the observers (ours): the acts in view (at most <see cref="MaxActs"/>, the oldest
/// giving way) and each area's heat and how long it has been hot. Digested in a fixed order (it is simulation state);
/// allocates nothing after it is made.
/// </summary>
public sealed class PerceptionState : IDigestedService
{
    /// <summary>The most acts kept in view at once.</summary>
    public const int MaxActs = 16;

    readonly ActInView[] acts = new ActInView[MaxActs];
    int count;

    public PerceptionState(LevelAreas areas)
    {
        Areas = areas;
        Heat = new float[areas.Count];
        HotSeconds = new float[areas.Count];
    }

    /// <summary>The level's areas.</summary>
    public LevelAreas Areas { get; }

    /// <summary>Each area's heat, 0 to 1, and how long since it began to be hot (s).</summary>
    public float[] Heat { get; }
    public float[] HotSeconds { get; }

    /// <summary>The acts in view now.</summary>
    public ReadOnlySpan<ActInView> Acts => acts.AsSpan(0, count);

    internal Span<ActInView> ActsToChange => acts.AsSpan(0, count);

    /// <summary>
    /// Keeps <paramref name="act"/> in view: one act per actor — a new one from the same body replaces the one in view if
    /// it is at least as big, else the one in view starts again —, the oldest giving way when all are taken.
    /// </summary>
    internal void Keep(in ActInView act)
    {
        for (int i = 0; i < count; i++)
        {
            if (acts[i].Actor != act.Actor) continue;
            if (act.Size >= acts[i].Size) acts[i] = act;
            else { acts[i].Seconds = 0; acts[i].Raised = act.Raised; }
            return;
        }
        if (count == MaxActs) Drop(0);
        acts[count++] = act;
    }

    /// <summary>Lets act <paramref name="i"/> go out of view (the order of the others kept).</summary>
    internal void Drop(int i)
    {
        for (int k = i + 1; k < count; k++) acts[k - 1] = acts[k];
        count--;
        acts[count] = default;
    }

    public void Digest(ref Fnv1a64 hash)
    {
        hash.Add(count);
        for (int i = 0; i < count; i++)
        {
            ref readonly var a = ref acts[i];
            hash.Add(a.Actor.Packed); hash.Add((int)a.Act); hash.Add((int)a.Move);
            hash.Add(a.Size); hash.Add(a.Limit); hash.Add(a.Seconds); hash.Add(a.Raised);
        }
        for (int i = 0; i < Heat.Length; i++) { hash.Add(Heat[i]); hash.Add(HotSeconds[i]); }
    }
}
