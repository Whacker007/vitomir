// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Configuration;

/// <summary>
/// The enhanced module's movement profiles (ours): one file per kind of body in <c>Data/movement/</c>
/// (<c>enhanced:movement/human</c>, <c>…/neonate</c>, <c>…/elder</c>), each the body's numbers by
/// <see cref="Locomotion"/>'s own names (the rules package's where it has them: <c>JumpImpulseBudget</c>,
/// <c>AccelerationCapacity</c>, <c>FallTolerance</c> …), read <b>strictly</b>: a field missing, unknown, of the wrong
/// kind or out of its range is named, by file and field, and the module refuses with every one (fail closed, as for
/// its rules profiles).
/// <code>
/// { "schema": "VITOMIR_MOVEMENT_PROFILE", "status": "SANDBOX", "title": "...", "note": "...",
///   "numbers": { "Radius": 0.3, ..., "JumpHeight": 0.5, ..., "GripSeconds": "unlimited" } }
/// </code>
/// The jump may be given as the rules' <c>JumpImpulseBudget</c> (the upward speed, m/s) or, more readably, as
/// <c>JumpHeight</c> (the metres the feet rise; the budget is then √(2·Gravity·height)) — one of the two, not both.
/// <c>SprintEndurance</c> and <c>GripSeconds</c> may be <c>"unlimited"</c>. <c>StartStopHalfLife</c> is the half-life of the
/// body's starts and stops on the spring (s, 0 to 1; 0 the fixed ramp), the one number that is not <see cref="Locomotion"/>'s. <c>status</c> is <c>SANDBOX</c> for the
/// engine's research numbers (the only kind until the rules package sends its curves). The numbers worked out from the
/// others (<see cref="Locomotion.WorkedOut"/>: the tap's jump and the powered jump and reach) are never given:
/// a file that gives one is refused; they are filled from the body's own (<see cref="Locomotion.WithOwnPowers"/>),
/// and the tap made a human's when the profiles are added (<see cref="MovementProfiles"/>).
/// </summary>
internal static class MovementProfileData
{
    /// <summary>The schema a movement profile's file names.</summary>
    public const string Schema = "VITOMIR_MOVEMENT_PROFILE";

    /// <summary>The status of research numbers.</summary>
    public const string Sandbox = "SANDBOX";

    /// <summary>The readable stand-in for <c>JumpImpulseBudget</c>.</summary>
    public const string JumpHeight = "JumpHeight";

    /// <summary>
    /// The half-life of the body's starts and stops on the ground (s; the spring,
    /// <see cref="BodyMotor.Spring"/>; 0, the earlier build's fixed ramp), required in every file; not a <see cref="Locomotion"/> number (those
    /// are digested in every world): the profile's <see cref="MovementProfile.HalfLife"/>.
    /// </summary>
    public const string StartStopHalfLife = "StartStopHalfLife";

    /// <summary>The longest half-life that is still a start or a stop (s).</summary>
    public const float LongestHalfLife = 1f;

    static readonly string[] TopKeys = ["schema", "status", "title", "note", "numbers"];
    static readonly string[] MayBeUnlimited = [nameof(Locomotion.SprintEndurance), nameof(Locomotion.GripSeconds)];
    static readonly string[] AboveZero =
    [
        nameof(Locomotion.Radius), nameof(Locomotion.Height), nameof(Locomotion.EyeHeight), nameof(Locomotion.CrouchHeight),
        nameof(Locomotion.Gravity), nameof(Locomotion.WalkSpeed), nameof(Locomotion.RunSpeed), nameof(Locomotion.SprintSpeed),
        nameof(Locomotion.AccelerationCapacity), nameof(Locomotion.BrakingDeceleration), nameof(Locomotion.MantleSeconds),
        nameof(Locomotion.ChargeSeconds),
    ];

    /// <summary>
    /// Reads profile <paramref name="id"/> from <paramref name="root"/>; every problem is added to
    /// <paramref name="problems"/> (prefixed with the id) and null is answered when there is any.
    /// </summary>
    public static MovementProfile? Read(AssetId id, JsonElement root, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(id.ToString(), problems);
        if (root.ValueKind != JsonValueKind.Object) { c.Fail("a movement profile must be an object"); return null; }
        foreach (var p in root.EnumerateObject())
            if (Array.IndexOf(TopKeys, p.Name) < 0) c.Fail($"unknown field '{p.Name}' (the fields are {string.Join(", ", TopKeys)})");
        c.Schema(root, Schema);
        var status = c.Str(root, "status");
        if (status is not null && status != Sandbox) c.Fail($"status is '{status}'; the only status known is '{Sandbox}' (the engine's research numbers) until the rules package's curves");
        var title = c.Str(root, "title") ?? "";
        c.Str(root, "note", required: false);
        if (c.Obj(root, "numbers") is not { } numbers) return null;

        var given = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in numbers.EnumerateObject())
        {
            if (given.ContainsKey(p.Name)) { c.Fail($"numbers: '{p.Name}' is given twice"); continue; }
            given[p.Name] = p.Value;
            if (!LocomotionFields.Has(p.Name) && p.Name != JumpHeight && p.Name != StartStopHalfLife)
                c.Fail($"numbers: unknown number '{p.Name}' (the numbers are {string.Join(", ", LocomotionFields.Names.Where(n => Array.IndexOf(Locomotion.WorkedOut, n) < 0))}, {StartStopHalfLife}; {JumpHeight} may stand for JumpImpulseBudget)");
            else if (Array.IndexOf(Locomotion.WorkedOut, p.Name) >= 0)
                c.Fail($"numbers: '{p.Name}' is worked out from the body's own numbers (the tap a human's jump, the powered jump and reach its own), not given");
        }

        var l = default(Locomotion);
        foreach (var name in LocomotionFields.Names)
        {
            if (name == nameof(Locomotion.JumpImpulseBudget) || Array.IndexOf(Locomotion.WorkedOut, name) >= 0) continue;
            if (!given.TryGetValue(name, out var v)) { c.Fail($"numbers: '{name}' is missing"); continue; }
            if (Number(c, name, v) is { } f) l = LocomotionFields.With(l, name, f);
        }
        bool hasBudget = given.TryGetValue(nameof(Locomotion.JumpImpulseBudget), out var budget);
        bool hasHeight = given.TryGetValue(JumpHeight, out var height);
        if (hasBudget && hasHeight) c.Fail($"numbers: give the jump as 'JumpImpulseBudget' or as '{JumpHeight}', not both");
        else if (!hasBudget && !hasHeight) c.Fail($"numbers: 'JumpImpulseBudget' (or '{JumpHeight}', metres) is missing");
        else if (hasBudget && Number(c, nameof(Locomotion.JumpImpulseBudget), budget) is { } b) l.JumpImpulseBudget = b;
        else if (hasHeight && Number(c, JumpHeight, height) is { } h) l.JumpImpulseBudget = Locomotion.ImpulseFor(h, l.Gravity > 0 ? l.Gravity : 9.81f);

        // The spring's half-life (0 the ramp), at most LongestHalfLife.
        float halfLife = 0f;
        if (!given.TryGetValue(StartStopHalfLife, out var hl)) c.Fail($"numbers: '{StartStopHalfLife}' is missing (the half-life of the starts and stops, s; 0 for the fixed ramp)");
        else if (Number(c, StartStopHalfLife, hl) is { } h2)
        {
            if (h2 > LongestHalfLife) c.Fail($"numbers: '{StartStopHalfLife}' is {h2}; a half-life above {LongestHalfLife} s is no start or stop");
            else halfLife = h2;
        }

        l = l.WithOwnPowers();
        if (problems.Count == before) Sense(c, l);
        return problems.Count == before ? new MovementProfile(id, title, l, status == Sandbox) { HalfLife = halfLife } : null;
    }

    static float? Number(JsonChecks c, string name, JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.String && v.GetString() == "unlimited")
        {
            if (Array.IndexOf(MayBeUnlimited, name) >= 0) return float.PositiveInfinity;
            c.Fail($"numbers: '{name}' cannot be unlimited (only {string.Join(" and ", MayBeUnlimited)} can)");
            return null;
        }
        if (c.Num(v, $"numbers: '{name}'") is not { } d) return null;
        if (d < 0) { c.Fail($"numbers: '{name}' is {d}; it must not be below 0"); return null; }
        if (d == 0 && Array.IndexOf(AboveZero, name) >= 0) { c.Fail($"numbers: '{name}' must be above 0"); return null; }
        return (float)d;
    }

    /// <summary>The numbers' sense together: the eyes and the crouch within the height, the step below it, a share and an angle in range, the bands in order.</summary>
    static void Sense(JsonChecks c, in Locomotion l)
    {
        if (l.EyeHeight > l.Height) c.Fail($"numbers: EyeHeight {l.EyeHeight} is above Height {l.Height}");
        if (l.CrouchHeight > l.Height) c.Fail($"numbers: CrouchHeight {l.CrouchHeight} is above Height {l.Height}");
        if (l.CrouchHeight < 2 * l.Radius) c.Fail($"numbers: CrouchHeight {l.CrouchHeight} is below the capsule's width ({2 * l.Radius})");
        if (l.StepHeight >= l.Height) c.Fail($"numbers: StepHeight {l.StepHeight} is not below Height {l.Height}");
        if (l.AirControl > 1) c.Fail($"numbers: AirControl {l.AirControl} is a share, 0 to 1");
        if (l.SteepestGroundDegrees is <= 0 or >= 90) c.Fail($"numbers: SteepestGroundDegrees {l.SteepestGroundDegrees} must be between 0 and 90");
        if (l.WalkSpeed > l.RunSpeed || l.RunSpeed > l.SprintSpeed) c.Fail($"numbers: the paces must rise, walk {l.WalkSpeed} ≤ run {l.RunSpeed} ≤ sprint {l.SprintSpeed}");
        if (l.VaultMin > l.VaultMax || l.VaultMax > l.MantleMax) c.Fail($"numbers: the bands must rise, VaultMin {l.VaultMin} ≤ VaultMax {l.VaultMax} ≤ MantleMax {l.MantleMax}");
        if (l.SafeFall < l.StepHeight) c.Fail($"numbers: SafeFall {l.SafeFall} is below StepHeight {l.StepHeight} (a walk would stop at every step down)");
        if (l.StepHeight > l.VaultMin) c.Fail($"numbers: StepHeight {l.StepHeight} is above VaultMin {l.VaultMin} (a stride would take what a vault should)");
    }
}
