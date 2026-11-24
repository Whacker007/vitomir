// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// An actor's <c>movement</c> and its duelling brain's <c>approach</c> — 1.1's creatures moving and closing as 1.1 has
/// them. <c>movement</c>: <c>human</c> (the default, as before), <c>neonate</c>, <c>elder</c> (the module's movement profiles) or
/// <c>template</c> (its 1.1 template's own walk and run, read from the install at run time). <c>control.ai.approach</c>: <c>duel</c>
/// (the default: straight at its foe, never pausing) or <c>oneone</c> (1.1's manoeuvre, with its 1.1 template's AI file's numbers). A
/// preset's (<c>szlachta</c>: <c>template</c> and <c>oneone</c>) is the default when the actor says nothing.
/// </summary>
internal sealed partial class ScenarioReader
{
    /// <summary>The movements an actor may have.</summary>
    internal static readonly string[] Movements = ["human", "neonate", "elder", Session.TemplateMovement];

    /// <summary>The approaches a duelling brain may have.</summary>
    internal static readonly string[] Approaches = ["duel", "oneone"];

    /// <summary>The actors' fields that their preset gave (<c>Szlachta.movement</c>, <c>Szlachta.approach</c>), for the setup's sources.</summary>
    internal readonly HashSet<string> FromPreset = new(StringComparer.Ordinal);

    /// <summary>The actor's movement: given, else its preset's, else <c>human</c>.</summary>
    string MovementOf(JsonElement a, string at, FoePreset? preset, string name)
    {
        if (Word(a, "movement", at, Movements, "a movement (human, neonate, elder: the module's movement profiles; template: its 1.1 template's own walk and run)") is { } given) return given;
        if (preset?.Movement is { } pm && Movements.Contains(pm)) { FromPreset.Add(name + ".movement"); return pm; }
        return "human";
    }

    /// <summary>The duelling brain's approach as given in its <c>ai</c> object (null when not given; the actor's preset then decides).</summary>
    string? ApproachGiven(JsonElement ai, string ap) =>
        ai.ValueKind == JsonValueKind.Object ? Word(ai, ArenaSpec.ApproachField, ap, Approaches, "an approach (duel: straight at its foe; oneone: 1.1's manoeuvre)") : null;

    /// <summary>A duelling brain's approach made explicit: given, else its preset's, else <c>duel</c> (nothing for another kind of AI).</summary>
    void ApproachDefault(JsonObject control, FoePreset? preset, string name)
    {
        if (control["ai"] is not JsonObject ai || ai["kind"]?.GetValue<string>() != "duel" || ai.ContainsKey(ArenaSpec.ApproachField)) return;
        if (preset?.Approach is { } pa) FromPreset.Add(name + ".approach");
        ai[ArenaSpec.ApproachField] = preset?.Approach is { } pb ? ArenaSpec.NameOf(pb) : "duel";
    }
}
