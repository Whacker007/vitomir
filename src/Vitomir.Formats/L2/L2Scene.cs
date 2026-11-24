// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Vitomir.Formats.L2;

/// <summary>
/// **Ours: the 1.1 scene a cell level stands for** (manifest key <c>scene</c>).
/// A cell level made on a 1.1 level's plan can carry that level's scene: its people, signs, containers,
/// triggers and scripts. The package holds none of it — only which scene, by name: <see cref="Level"/> is the
/// 1.1 level whose own scene (<c>scenes/LEVEL.nsd</c>: the lanterns, barrels, exits and starts) the cell level
/// takes the place of, and <see cref="Chapter"/> the chronicle scene played in it when nobody asks for another
/// (<c>OTDY_4_1</c> for the daytime Old Town). Both are read from the install when the level loads, exactly as
/// for a 1.1 level, and every thing is put at its 1.1 position on the cell level's own ground.
///
/// <see cref="Drop"/> lists the 1.1 props that are left out because the new town has its own: each entry is a
/// template name (<c>lantern1_off</c>; <c>*</c> stands for any run of letters, so <c>lantern*</c>), a class
/// (<c>class:lantern</c>, <see cref="Classes"/>; <c>class:props</c> is every thing whose template's type is
/// a prop, <see cref="AllProps"/>), or one placed thing by its anchor id
/// (<c>P1_OTDY/things/26</c>: the scene's name and the thing's number in it). <see cref="Keep"/> takes things
/// back out of a wider rule the same way. The engine never drops an actor, a container, a trigger, a thing with
/// a script or one a script is handed, whatever the list says.
/// </summary>
public sealed class L2Scene
{
    [JsonPropertyName("level")] public string Level { get; set; } = "";
    /// <summary>The chronicle scene played in the level when none is asked for, or null for the chronicle's own choice.</summary>
    [JsonPropertyName("chapter")] public string? Chapter { get; set; }
    [JsonPropertyName("drop")] public List<string>? Drop { get; set; }
    [JsonPropertyName("keep")] public List<string>? Keep { get; set; }
    /// <summary>
    /// The things that stay — with their scripts, their bodies and what they say under the pointer — but are
    /// not drawn (rules as for <see cref="Drop"/>). Null stands for <see cref="DefaultHide"/>: 1.1's click
    /// exits, which are its doors (each <c>tracker_thing</c> carries the door's own geometry, cut out of the
    /// level); the new town draws its own doors, and the click exit stays as the thing that is pointed at.
    /// </summary>
    [JsonPropertyName("hide")] public List<string>? Hide { get; set; }
    /// <summary>Properties this build does not know, kept for the write.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>
    /// The classes a drop rule can name (ours), each a list of template-name patterns: 1.1's street lanterns
    /// and torches, its doors, its wells and its shop signs — the things the Old Town generator makes its own of.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Classes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["lantern"] = ["lantern*", "torch*", "walltorch*", "brazier*"],
        ["door"] = ["door*", "*_door", "*door"],
        ["well"] = ["well*"],
        ["sign"] = ["*sign*"],
    };

    /// <summary>What is hidden when the manifest names no rule (ours): 1.1's click exits.</summary>
    public static readonly string[] DefaultHide = ["tracker_thing"];

    /// <summary>Whether the rules hide the thing's model (<see cref="Hide"/>, or <see cref="DefaultHide"/> when there is none).</summary>
    public bool Hides(string scene, int index, string template) =>
        Matches(Hide ?? [.. DefaultHide], scene, index, template);

    /// <summary>The rule that drops every unprotected thing of type prop: all of 1.1's dressing, for a town that brings its own.</summary>
    public const string AllProps = "class:props";

    /// <summary>
    /// What a cell level drops when its layout carries a scene and names no rule of its own (ours): 1.1's
    /// lanterns, which the generator's own lanterns stand in for.
    /// </summary>
    public static readonly string[] DefaultDrop = ["class:lantern"];

    /// <summary>
    /// Whether the rules drop the thing numbered <paramref name="index"/> in the scene called
    /// <paramref name="scene"/>, placed from <paramref name="template"/>: some <see cref="Drop"/> entry matches
    /// (<paramref name="prop"/> says its type is a prop, for <see cref="AllProps"/>) and no <see cref="Keep"/>
    /// entry does. Names are compared without regard to case.
    /// </summary>
    public bool Drops(string scene, int index, string template, bool prop = false) =>
        (Matches(Drop, scene, index, template) || prop && Drop is not null && Drop.Contains(AllProps, StringComparer.OrdinalIgnoreCase))
        && !Matches(Keep, scene, index, template);

    static bool Matches(List<string>? rules, string scene, int index, string template)
    {
        if (rules is null) return false;
        foreach (var rule in rules)
        {
            if (rule.StartsWith("class:", StringComparison.OrdinalIgnoreCase))
            {
                if (Classes.TryGetValue(rule[6..], out var patterns) && patterns.Any(p => Like(p, template))) return true;
            }
            else if (rule.Contains('/'))
            {
                if (rule.Equals($"{scene}/things/{index}", StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (Like(rule, template)) return true;
        }
        return false;
    }

    /// <summary>Whether <paramref name="name"/> is <paramref name="pattern"/>, a name in which <c>*</c> stands for any run of characters.</summary>
    public static bool Like(string pattern, string name) =>
        Regex.IsMatch(name, "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The scene a cook is asked to bind: the <c>--scene</c> option (<c>LEVEL/CHAPTER</c>, or <c>CHAPTER</c>
    /// alone when the layout names its level, or <c>LEVEL/</c> for the level's scene with the chronicle's
    /// chapter), over the layout's own <c>scene</c> (<c>{"level": "P1_OTDY", "chapter": "OTDY_4_1", "drop":
    /// […], "keep": […]}</c>) and its <c>level</c>. Null when neither names a level. A layout that lists no
    /// <c>drop</c> gets <see cref="DefaultDrop"/>; an empty list drops nothing. <paramref name="drop"/> (the
    /// cook's <c>--scene-drop a,b,c</c>) replaces the layout's list.
    /// </summary>
    public static L2Scene? FromLayout(JsonNode? layout, string? option, string? drop = null)
    {
        L2Scene? scene = null;
        if (layout?["scene"] is JsonObject node)
        {
            try { scene = node.Deserialize<L2Scene>(L2Manifest.Json); }
            catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException) { scene = null; }
        }
        if (option is { Length: > 0 })
        {
            scene ??= new L2Scene();
            int slash = option.IndexOf('/');
            if (slash >= 0)
            {
                scene.Level = option[..slash];
                scene.Chapter = slash + 1 < option.Length ? option[(slash + 1)..] : null;
            }
            else scene.Chapter = option;
        }
        if (scene is null) return null;
        if (scene.Level.Length == 0 && layout?["level"] is JsonValue v && v.TryGetValue<string>(out var level)) scene.Level = level;
        if (scene.Level.Length == 0) return null;
        if (string.IsNullOrEmpty(scene.Chapter)) scene.Chapter = null;
        if (drop is not null) scene.Drop = [.. drop.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        scene.Drop ??= [.. DefaultDrop];
        scene.Unknown?.Remove("place");                     // the placements become anchors (Places), not a second copy here
        return scene;
    }

    /// <summary>The kind of an anchor that places one thing of the carried scene and turns it.</summary>
    public const string ThingAnchor = "thing";
    /// <summary>The kind of an anchor that places one thing and leaves it facing as 1.1 has it (its rotation is not read).</summary>
    public const string ThingAtAnchor = "thing-at";

    /// <summary>
    /// **Things moved by anchors**: the layout's <c>scene.place</c> — each
    /// <c>{"id": "OTDY_4_1/things/0", "position": [x, y, z], "rotation": [0, 0, yaw]}</c>, metres and degrees
    /// in the level's frame, the rotation optional — as anchors of kind <see cref="ThingAnchor"/> (or
    /// <see cref="ThingAtAnchor"/> when no rotation is given) for the manifest. When the level loads, a placed thing of the scene with an anchor of its id stands where the
    /// anchor says instead of where 1.1 has it (and faces the anchor's way when it gives one); its height is
    /// still taken from the ground. An entry without an id of the form <c>SCENE/things/N</c> or without three
    /// finite coordinates is left out.
    /// </summary>
    public static List<L2Anchor> Places(JsonNode? layout)
    {
        var anchors = new List<L2Anchor>();
        if (layout?["scene"]?["place"] is not JsonArray list) return anchors;
        foreach (var node in list)
        {
            if (node?["id"] is not JsonValue idv || !idv.TryGetValue<string>(out var id) || ThingOf(id) is not { } thing) continue;
            if (Floats(node["position"]) is not { Length: 3 } position) continue;
            var rotation = Floats(node["rotation"]);
            anchors.Add(new L2Anchor
            {
                Id = id, Kind = rotation is { Length: 3 } ? ThingAnchor : ThingAtAnchor, Scene = thing.Scene, Index = thing.Index, Name = id,
                Position = position, Rotation = rotation is { Length: 3 } ? rotation : [0, 0, 0],
            });
        }
        return anchors;
    }

    /// <summary>The scene and the number in an anchor id of the form <c>SCENE/things/N</c>, or null.</summary>
    public static (string Scene, int Index)? ThingOf(string id)
    {
        var parts = id.Split('/');
        return parts.Length == 3 && parts[1].Equals("things", StringComparison.OrdinalIgnoreCase) && parts[0].Length > 0
               && int.TryParse(parts[2], out int n) && n >= 0 ? (parts[0], n) : null;
    }

    static float[]? Floats(JsonNode? node)
    {
        if (node is not JsonArray a) return null;
        var values = new float[a.Count];
        for (int i = 0; i < values.Length; i++)
        {
            if (a[i] is not JsonValue v) return null;
            double d;
            try { d = v.GetValue<double>(); }
            catch (Exception e) when (e is InvalidOperationException or FormatException) { return null; }
            if (!double.IsFinite(d)) return null;
            values[i] = (float)d;
        }
        return values;
    }

    public override string ToString() =>
        $"{Level}" + (Chapter is null ? "" : $" with {Chapter}") + (Drop is { Count: > 0 } ? $", dropping {string.Join(", ", Drop)}" : "");
}
