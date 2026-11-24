// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the reader of Data/combat/directions.json.
using System.Text.Json;
using Vitomir.Platform.Control;

namespace Vitomir.Modules.Enhanced.Configuration;

/// <summary>
/// Directional attacks' data (<c>enhanced:combat/directions</c>, ENGINE_SANDBOX, ours): the direction → blow table's rows
/// (<see cref="DirectionRow"/>), the choosing's and the holding's numbers (<see cref="DirectionSettings"/>). Read strictly: an unknown
/// direction or rule, a number out of its range, a missing number is a problem. Given to the platform's
/// <see cref="DirectionalAttacks"/> by the module (<see cref="Apply"/>).
/// </summary>
public sealed class DirectionsData
{
    public const string SchemaId = "VITOMIR_ATTACK_DIRECTIONS";
    public const string Asset = "combat/directions";

    public string File { get; private init; } = "";
    public IReadOnlyList<DirectionRow> Rows { get; private init; } = DirectionalAttacks.DefaultRows;
    public DirectionSettings Settings { get; private init; } = new();

    /// <summary>The table and numbers to the platform's service.</summary>
    public void Apply(DirectionalAttacks d)
    {
        d.Rows = Rows;
        d.Settings = Settings;
    }

    static readonly string[] Directions = ["left", "right", "overhead", "thrust"];

    internal static DirectionsData? Read(JsonElement root, string file, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        string status = c.Str(root, "status") ?? "";
        if (status != "ENGINE_SANDBOX") c.Fail($"status is '{status}': directional attacks' numbers are ENGINE_SANDBOX (ours)");
        float N(JsonElement o, string key, double least, double most)
        {
            if (!o.TryGetProperty(key, out var v)) { c.Fail($"{key} is missing"); return 0; }
            double x = c.Num(v, key) ?? 0;
            if (x < least || x > most) c.Fail($"{key} {x} is outside {least} to {most}");
            return (float)x;
        }

        var rows = new List<DirectionRow>();
        if (c.Arr(root, "blows") is { } blows)
            foreach (var b in blows.EnumerateArray())
            {
                if (b.ValueKind != JsonValueKind.Object) { c.Fail("a row of blows is not an object"); continue; }
                foreach (var p in b.EnumerateObject())
                    if (p.Name is not ("kind" or "carry" or "note") && Array.IndexOf(Directions, p.Name) < 0)
                        c.Fail($"blows: '{p.Name}' is not a direction (left, right, overhead, thrust), kind, carry or note");
                string? kind = c.Str(b, "kind", required: false) is { } k && k != "any" ? k : null;
                int? carry = null;
                if (b.TryGetProperty("carry", out var cv))
                {
                    if (cv.ValueKind == JsonValueKind.Number && cv.TryGetInt32(out int n)) carry = n;
                    else if (!(cv.ValueKind == JsonValueKind.String && cv.GetString() == "any")) c.Fail("blows: carry is a carry class (a number) or \"any\"");
                }
                string? D(string name) => b.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                rows.Add(new DirectionRow(kind, carry, D("left"), D("right"), D("overhead"), D("thrust")));
            }

        var s = new DirectionSettings();
        if (c.Obj(root, "choosing") is { } ch)
        {
            var side = c.Str(ch, "default_side") ?? "right";
            if (side is not ("left" or "right")) c.Fail($"choosing.default_side '{side}' is left or right");
            s = s with
            {
                FoeReachMetres = N(ch, "foe_reach_metres", 0.5, 20),
                FoeConeDegrees = N(ch, "foe_cone_degrees", 1, 180),
                OverheadAboveShare = N(ch, "overhead_above_share", 0, 2),
                ThrustBelowShare = N(ch, "thrust_below_share", -1, 1),
                NoFoeOverheadDegrees = N(ch, "no_foe_overhead_degrees", 0, 90),
                NoFoeThrustDegrees = N(ch, "no_foe_thrust_degrees", -90, 0),
                NoFoeSideDegrees = N(ch, "no_foe_side_degrees", 0, 180),
                DefaultSide = side == "left" ? AttackDirection.Left : AttackDirection.Right,
                EyeShare = N(ch, "eye_share", 0.3, 1.2),
                MouseWindowSeconds = N(ch, "mouse_window_seconds", 0, 2),
                MouseDeadZoneDegrees = N(ch, "mouse_dead_zone_degrees", 0, 45),
                MouseVerticalScale = N(ch, "mouse_vertical_scale", 0, 10),
                MouseInvert = c.Bool(ch, "mouse_invert") ?? false,
                KeysThreshold = N(ch, "keys_threshold", 0.05, 1),
            };
            if (s.OverheadAboveShare <= s.ThrustBelowShare) c.Fail("choosing: overhead_above_share must be above thrust_below_share");
        }
        if (c.Obj(root, "ready") is { } r)
        {
            var rule = c.Str(r, "rule") ?? "turn";
            if (rule is not ("turn" or "window")) c.Fail($"ready.rule '{rule}' is turn or window");
            s = s with
            {
                Ready = rule == "window" ? ReadyRule.Window : ReadyRule.Turn,
                ReadyBeforeWindowSeconds = N(r, "ready_before_window_seconds", 0, 1),
                TurnSearchShare = N(r, "turn_search_share", 0, 1),
                ReadyBeforeStrikeSeconds = N(r, "ready_before_strike_seconds", 0, 2),
                HoldMostSeconds = N(r, "hold_most_seconds", 0, 60),
                // A held blow switched to another direction (optional: absent, the platform's defaults).
                SwitchWhileHeld = c.Bool(r, "switch_while_held") ?? s.SwitchWhileHeld,
                SwitchFadeSeconds = r.TryGetProperty("switch_fade_seconds", out _) ? N(r, "switch_fade_seconds", 0, 1) : s.SwitchFadeSeconds,
            };
        }
        if (problems.Count != before) return null;
        return new DirectionsData { File = file, Rows = rows.Count > 0 ? rows : DirectionalAttacks.DefaultRows, Settings = s };
    }
}
