// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// <b>The pace of a fight between humans</b> (ours, <c>Data/combat/pace.json</c>, schema <see cref="SchemaId"/>,
/// <b>SANDBOX</b>; <i>"humans moving at human speeds … Mount and Blade and KCD"</i>; the combat pace
/// survey):
/// <list type="bullet">
/// <item><see cref="Tempo"/>: the <b>global combat tempo</b> (0.70), multiplied into every fighter's swing and recovery rates
/// under Melee's or Brawl's and Celerity's (<see cref="SkillTempoSystem"/>); not the block's rise, a hurt or a death.</item>
/// <item><see cref="FightingPace"/>: the most a body moves while its blow swings or is held ready (1.8 m/s; the platform's
/// <see cref="Platform.Bodies.FightingPace"/>).</item>
/// <item>The dodge's pace (<see cref="DodgeUse"/>, <see cref="DodgeMoveSeconds"/>, <see cref="DodgeRecoverSeconds"/>): built, off
/// by default.</item>
/// </list>
/// </summary>
public sealed class PaceData
{
    public const string SchemaId = "VITOMIR_ENHANCED_PACE";

    /// <summary>The global combat tempo (1 is 1.1's own timing).</summary>
    public float Tempo { get; private init; } = 1f;

    /// <summary>The fighting pace (m/s).</summary>
    public float FightingPace { get; private init; } = 1.8f;

    /// <summary>Whether the dodge's pace replaces <c>skill.json</c>'s human burst and recovery by default.</summary>
    public bool DodgeUse { get; private init; }

    /// <summary>The dodge's burst and recovery at this pace (s, a human's with no Dodge).</summary>
    public float DodgeMoveSeconds { get; private init; } = 0.35f;
    public float DodgeRecoverSeconds { get; private init; } = 0.45f;

    /// <summary>The sheet templates of the rushing creatures (<c>rush_unpaced.templates</c>: the szlachta) that keep their own pace through their blows with the switch <c>rush-unpaced</c>; empty when the file gives none.</summary>
    public IReadOnlyList<string> RushUnpaced { get; private init; } = [];

    internal static PaceData? Read(JsonElement root, string file, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        string status = c.Str(root, "status") ?? "";
        if (status != "SANDBOX") c.Fail($"status is '{status}': the pace's numbers are SANDBOX until the rules package gives them");
        float N(JsonElement o, string path, string key, double least, double most)
        {
            if (!o.TryGetProperty(key, out var v)) { c.Fail($"{path}.{key} is missing"); return (float)least; }
            double x = c.Num(v, $"{path}.{key}") ?? least;
            if (!(x >= least && x <= most)) c.Fail($"{path}.{key} {x} is outside {least} to {most}");
            return (float)x;
        }
        float tempo = 1f, pace = 1.8f, move = 0.35f, recover = 0.45f;
        bool use = false;
        if (c.Obj(root, "tempo") is { } t) tempo = N(t, "tempo", "global", Platform.Combat.CombatTempoOverride.Least, Platform.Combat.CombatTempoOverride.Most);
        if (c.Obj(root, "fighting_pace") is { } f) pace = N(f, "fighting_pace", "most_metres_per_second", 0.1, 20);
        if (c.Obj(root, "dodge") is { } d)
        {
            use = c.Bool(d, "use") ?? false;
            move = N(d, "dodge", "move_seconds", 0.05, 2);
            recover = N(d, "dodge", "recover_seconds", 0, 3);
        }
        // The rushing creatures (optional: a file without it lets none through).
        var rush = new List<string>();
        if (root.TryGetProperty("rush_unpaced", out var ru))
        {
            if (ru.ValueKind != JsonValueKind.Object || !ru.TryGetProperty("templates", out var ts) || ts.ValueKind != JsonValueKind.Array)
                c.Fail("rush_unpaced is an object with templates, a list of sheet templates (\"sandbox.szlachta\")");
            else
                foreach (var id in ts.EnumerateArray())
                {
                    if (id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())) { c.Fail($"rush_unpaced.templates holds {id.GetRawText()}, not a sheet template's id"); continue; }
                    if (!rush.Contains(id.GetString()!)) rush.Add(id.GetString()!);
                }
        }
        if (problems.Count > before) return null;
        return new PaceData { Tempo = tempo, FightingPace = pace, DodgeUse = use, DodgeMoveSeconds = move, DodgeRecoverSeconds = recover, RushUnpaced = rush };
    }
}
