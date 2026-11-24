// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vitomir.Platform.Host.Scenario;

// Ours: a scenario actor's archer brain, control.ai { "kind": "archer", <its numbers> }.
internal sealed partial class ScenarioReader
{
    /// <summary>
    /// The archer brain's numbers a scenario may set (<see cref="Combat.ArcherBrainSettings"/>; the module's
    /// <c>ranged.json</c> <c>archer_brain</c> gives the rest): the field, the data's name, its range and unit.
    /// </summary>
    internal static readonly (string Field, string Data, string Unit)[] ArcherFields =
    [
        ("keepMetres", "keep_metres", "m: it comes on until its foe is this near, then stands and shoots"),
        ("swordMetres", "sword_metres", "m: its foe this near, it draws its melee weapon (the duelling brain)"),
        ("holdSeconds", "hold_seconds", "s: a full draw held this long before the loose"),
        ("errorScale", "error_scale", "times the bow's spread by its Range (firearms): its aim's error, drawn once a shot"),
    ];

    /// <summary><c>{ "kind": "archer", … }</c>: the numbers given, each checked against its range (the others the module's).</summary>
    JsonObject ArcherNumbers(JsonElement v, string ap)
    {
        var o = new JsonObject { ["kind"] = "archer" };
        if (v.ValueKind != JsonValueKind.Object) return o;
        Known(v, ap, ["kind", .. ArcherFields.Select(f => f.Field)], "the archer brain");
        foreach (var (field, data, unit) in ArcherFields)
        {
            if (!v.TryGetProperty(field, out var x) || x.ValueKind == JsonValueKind.Null) continue;
            var (least, most) = Combat.ArcherBrainSettings.Ranges[data];
            if (Number(x, ScenarioPointer.Of(ap, field), least, most, unit) is { } n) o[field] = n;
        }
        return o;
    }
}
