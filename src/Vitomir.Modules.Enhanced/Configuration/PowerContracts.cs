// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;

namespace Vitomir.Modules.Enhanced.Configuration;

/// <summary>
/// One typed power contract of the rules package (<c>power_verb_contracts.json</c>, schema
/// <c>VTMR_H1_POWER_VERBS</c>): its id, the learned option it requires, its source rank reference, what kind
/// of effect it is and its status. The package says these are representative, not the full catalogue;
/// The actor sheet keeps them as data for the option gate's callers; a later increment gives them behaviour.
/// </summary>
public sealed record PowerContract(string PowerId, string LearnedOptionRequired, string SourceRankReference,
                                   string EffectType, string TargetType, string Status, string NumericalCoefficients);

/// <summary>The power contracts by id, in id order.</summary>
public sealed class PowerContracts
{
    public const string SchemaId = "VTMR_H1_POWER_VERBS";

    readonly SortedDictionary<string, PowerContract> byId;

    PowerContracts(SortedDictionary<string, PowerContract> byId, IReadOnlyList<string> limitations)
    {
        this.byId = byId;
        Limitations = limitations;
    }

    public IEnumerable<PowerContract> All => byId.Values;

    public int Count => byId.Count;

    /// <summary>The package's own caveats on the contracts.</summary>
    public IReadOnlyList<string> Limitations { get; }

    public PowerContract? Find(string powerId) => byId.TryGetValue(powerId, out var p) ? p : null;

    internal static PowerContracts? Read(JsonElement root, string file, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        var byId = new SortedDictionary<string, PowerContract>(StringComparer.Ordinal);
        if (c.Arr(root, "powers") is { } powers)
            foreach (var p in powers.EnumerateArray())
            {
                var id = c.Str(p, "power_id");
                var option = c.Str(p, "learned_individual_option_required");
                // A source rank is a number (1..10) or a word (COMMON, POWER_SPECIFIC): kept as its text.
                string rank = p.TryGetProperty("source_rank_reference", out var sr) && sr.ValueKind is JsonValueKind.Number or JsonValueKind.String
                    ? (sr.ValueKind == JsonValueKind.String ? sr.GetString()! : sr.GetRawText())
                    : "";
                if (rank.Length == 0) c.Fail($"power '{id}': 'source_rank_reference' is missing or neither a number nor a text");
                var contract = new PowerContract(id ?? "", option ?? "", rank,
                    c.Str(p, "effect_type") ?? "", c.Str(p, "target_type") ?? "", c.Str(p, "status") ?? "",
                    c.Str(p, "numerical_coefficients") ?? "");
                if (id is not null && !byId.TryAdd(id, contract)) c.Fail($"power '{id}' appears twice");
            }
        var limitations = new List<string>();
        if (c.Arr(root, "limitations", required: false) is { } lim)
            foreach (var l in lim.EnumerateArray())
                if (l.ValueKind == JsonValueKind.String) limitations.Add(l.GetString()!);
        return problems.Count > before ? null : new PowerContracts(byId, limitations);
    }
}
