// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;

namespace Vitomir.Modules.Enhanced.Configuration;

/// <summary>
/// One open number of the rules package's gate register (<c>NUMERIC_GATE_REGISTER.json</c>): what must be
/// settled, its research status (<c>OPEN_NUMERIC</c>, <c>SANDBOX_ONLY_COEFFICIENT</c>,
/// <c>SOURCE_NEAR_PROVISIONAL</c>, <c>DEFERRED_BY_USER</c> …), and whether the game has adopted a number for
/// it (none has yet: the register counts 36 of 36 not shipping-approved).
/// </summary>
public sealed record NumericGate(string Id, string Domain, string Priority, string RequiredSetting, string ResearchStatus,
                                 bool GameNumericAdopted, bool ImplementationVerified, string Reference, string ResolutionRule);

/// <summary>The gate register, by gate id, and the module's reading of which gate governs which profile value.</summary>
public sealed class GateRegister
{
    public const string SchemaId = "VTMR_H1_NUMERIC_GATES";
    public const string MappingSchemaId = "VITOMIR_PROFILE_VALUE_GATES";

    readonly SortedDictionary<string, NumericGate> gates;
    readonly SortedDictionary<string, string> gateOfValue;

    GateRegister(SortedDictionary<string, NumericGate> gates, SortedDictionary<string, string> gateOfValue, string mappingStatus)
    {
        this.gates = gates;
        this.gateOfValue = gateOfValue;
        MappingStatus = mappingStatus;
    }

    /// <summary>The gates in id order.</summary>
    public IEnumerable<NumericGate> Gates => gates.Values;

    /// <summary>The mapping file's own status (ours, proposed, until the rules package confirms it).</summary>
    public string MappingStatus { get; }

    public NumericGate? Gate(string id) => gates.TryGetValue(id, out var g) ? g : null;

    /// <summary>The gate a profile value answers to, or null when the mapping names none.</summary>
    public NumericGate? GateOf(string valueKey) => gateOfValue.TryGetValue(valueKey, out var id) ? Gate(id) : null;

    internal static GateRegister? Read(JsonElement register, string registerFile, JsonElement mapping, string mappingFile, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(registerFile, problems);
        c.Schema(register, SchemaId);
        var gates = new SortedDictionary<string, NumericGate>(StringComparer.Ordinal);
        if (c.Arr(register, "gates") is { } arr)
        {
            int i = 0;
            foreach (var g in arr.EnumerateArray())
            {
                var id = c.Str(g, "id");
                var gate = new NumericGate(
                    id ?? "",
                    c.Str(g, "domain") ?? "",
                    c.Str(g, "priority") ?? "",
                    c.Str(g, "required_setting") ?? "",
                    c.Str(g, "research_status") ?? "",
                    c.Bool(g, "game_numeric_adopted") ?? false,
                    c.Bool(g, "implementation_verified") ?? false,
                    c.Str(g, "reference") ?? "",
                    c.Str(g, "resolution_rule") ?? "");
                if (id is not null && !gates.TryAdd(id, gate)) c.Fail($"gate '{id}' appears twice");
                i++;
            }
        }

        var m = new JsonChecks(mappingFile, problems);
        m.Schema(mapping, MappingSchemaId);
        var registerSchema = m.Str(mapping, "gate_register_schema");
        if (registerSchema is not null && registerSchema != SchemaId) m.Fail($"gate_register_schema is '{registerSchema}', not '{SchemaId}'");
        var status = m.Str(mapping, "status") ?? "";
        var gateOfValue = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (m.Obj(mapping, "values") is { } values)
            foreach (var p in values.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.String) { m.Fail($"values.{p.Name} must name a gate id"); continue; }
                var gid = p.Value.GetString()!;
                if (!gates.ContainsKey(gid)) m.Fail($"values.{p.Name} names gate '{gid}', which the register does not have");
                gateOfValue[p.Name] = gid;
            }
        return problems.Count > before ? null : new GateRegister(gates, gateOfValue, status);
    }
}
