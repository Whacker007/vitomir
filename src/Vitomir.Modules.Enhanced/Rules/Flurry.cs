// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// Celerity's flurry's own numbers (<c>Data/rules/flurry.json</c>, ENGINE_SANDBOX): when a swing's extra blows
/// fall and how far the blade's line reaches for each (each extra blow its own strike that must still find its
/// target at its own moment). How many blows a swing carries is Celerity's (<see cref="CelerityEffect"/>, powers.json).
/// </summary>
public sealed class FlurryData
{
    public const string SchemaId = "VITOMIR_ENHANCED_FLURRY";
    public const string Asset = "rules/flurry";

    public string File { get; private init; } = "";
    public FlurrySpacing Spacing { get; private init; }
    public float SpacingSeconds { get; private init; }
    public float ApproachMetres { get; private init; }
    public float ReachMetres { get; private init; }
    /// <summary>The re-strike motion (<see cref="FlurrySettings.Motion"/>), its pull-back in clip seconds and its fastest rate.</summary>
    public bool Motion { get; private init; }
    public float PullBackClipSeconds { get; private init; } = 0.07f;
    public float RestrikeRateMost { get; private init; } = 3f;

    /// <summary>The numbers' defaults (the data's, for a world without the file: a test).</summary>
    public static FlurryData Defaults { get; } = new() { File = "(defaults)", Spacing = FlurrySpacing.Fixed, SpacingSeconds = 0.04f, ApproachMetres = 0.5f, ReachMetres = 0.15f };

    /// <summary>The same numbers with another spacing (the switch <c>flurry-swing-spacing</c>).</summary>
    public FlurryData WithSpacing(FlurrySpacing spacing) => With(spacing, SpacingSeconds, Motion);

    /// <summary>The same numbers with another spacing, fixed spacing's seconds and motion (the module's switches).</summary>
    public FlurryData With(FlurrySpacing spacing, float seconds, bool motion) => new()
    {
        File = File, Spacing = spacing, SpacingSeconds = seconds, ApproachMetres = ApproachMetres, ReachMetres = ReachMetres,
        Motion = motion, PullBackClipSeconds = PullBackClipSeconds, RestrikeRateMost = RestrikeRateMost,
    };

    /// <summary>Gives the numbers and the model to the platform's <see cref="FlurrySettings"/>.</summary>
    public void Apply(FlurrySettings s, FlurryModel model)
    {
        s.Model = model;
        s.Spacing = Spacing;
        s.SpacingSeconds = SpacingSeconds;
        s.ApproachMetres = ApproachMetres;
        s.ReachMetres = ReachMetres;
        s.Motion = Motion;
        s.PullBackClipSeconds = PullBackClipSeconds;
        s.RestrikeRateMost = RestrikeRateMost;
    }

    internal static FlurryData? Read(JsonElement root, string file, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        string status = c.Str(root, "status") ?? "";
        if (status != "ENGINE_SANDBOX") c.Fail($"status is '{status}': the flurry's numbers are ENGINE_SANDBOX (ours) until the rules package gives them");
        double N(JsonElement o, string key, double least, double most)
        {
            if (!o.TryGetProperty(key, out var v)) { c.Fail($"{key} is missing"); return 0; }
            double x = c.Num(v, key) ?? 0;
            if (x < least || x > most) c.Fail($"{key} {x} is outside {least} to {most}");
            return x;
        }
        var sp = c.Obj(root, "spacing");
        var line = c.Obj(root, "line");
        var mo = c.Obj(root, "motion", required: false);   // the re-strike motion (absent: off)
        if (sp is not { } s || line is not { } l) return null;
        bool motion = false;
        float pull = 0.07f, most = 3f;
        if (mo is { } m)
        {
            if (!m.TryGetProperty("on", out var on) || on.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) c.Fail("motion.on must be true or false");
            else motion = on.GetBoolean();
            pull = (float)N(m, "pull_back_clip_s", 0, 1);
            most = (float)N(m, "rate_most", 0.1, 20);
        }
        var kind = c.Str(s, "kind") switch
        {
            "fixed" => FlurrySpacing.Fixed,
            "swing" => FlurrySpacing.Swing,
            var k => Bad(k),
        };
        FlurrySpacing Bad(string? k) { c.Fail($"spacing.kind '{k}' is neither 'fixed' nor 'swing'"); return FlurrySpacing.Fixed; }
        var data = new FlurryData
        {
            File = file, Spacing = kind, SpacingSeconds = (float)N(s, "seconds", 0, 1),
            ApproachMetres = (float)N(l, "approach_m", 0, 2), ReachMetres = (float)N(l, "reach_m", 0, 1),
            Motion = motion, PullBackClipSeconds = pull, RestrikeRateMost = most,
        };
        return problems.Count == before ? data : null;
    }
}

/// <summary>
/// The rules' side of a Celerity swing's extra blows: the model the module's switches chose and what a strike
/// met at a guard is judged by (the earlier build's contest: held, parried or overpowered).
/// </summary>
public static class FlurryRules
{
    /// <summary>
    /// The guard's contest for an extra blow met at a guard (the earlier build's <see cref="GuardRules.Judge"/>, from the guard's own stream): the
    /// attacker's side with the blow's whole budget (each extra blow is its own strike), the defender's its own. Null without the
    /// guard's rules (guard-off: a guard met holds).
    /// </summary>
    public static GuardOutcome? JudgeGuard(World world, ActorSheets sheets, HarmRules harm, HarmWeapons weapons, IDeterministicRandom random, in FlurryStrike s)
    {
        if (!world.TryService<GuardRules>(out var g) || s.Guard == GuardMeet.None) return null;
        if (!sheets.Has(s.Attacker) || !sheets.Has(s.Target)) return null;
        var a = Side(world, sheets, harm, weapons, g, s.Attacker);
        var d = Side(world, sheets, harm, weapons, g, s.Target);
        return g.Judge(in a, in d, s.Met, s.Guard, random);
    }

    static GuardSide Side(World world, ActorSheets sheets, HarmRules harm, HarmWeapons weapons, GuardRules g, EntityId e)
    {
        var d = g.Data;
        string? cls = harm.Data.ClassOf(weapons.Of(world, e))?.Id;
        return new GuardSide(sheets.Effective(e, d.Strength), sheets.Effective(e, d.Dexterity), sheets.Effective(e, d.Melee), d.ForceOf(cls), 1f);
    }

    /// <summary>The trace's words for a check that did not land (the reason a line gives).</summary>
    public static string Why(in FlurryStrike s, GuardOutcome? verdict) => s.Check switch
    {
        FlurryCheck.Guarded => verdict is { } v
            ? $"extra blow {s.Index} of {s.Count} met {Article(s.Guard)} at its moment: {v.Verdict.ToString().ToLowerInvariant()} (the guard's contest)"
            : $"extra blow {s.Index} of {s.Count} met {Article(s.Guard)} at its moment: held",
        FlurryCheck.OutOfReach => FormattableString.Invariant($"extra blow {s.Index} of {s.Count}: the target was out of the blade's reach at its moment (moved {s.Moved:0.00} m)"),
        FlurryCheck.Dodged => FormattableString.Invariant($"extra blow {s.Index} of {s.Count}: the target's dodge carried it out of the blade's line (moved {s.Moved:0.00} m)"),
        FlurryCheck.AttackerStopped => $"extra blow {s.Index} of {s.Count}: the attacker no longer swings that swing (a cancel, its stop, its weapon hand)",
        FlurryCheck.TargetDown => $"extra blow {s.Index} of {s.Count}: the target is down",
        _ => "",
    };

    static string Article(GuardMeet m) => m switch
    {
        GuardMeet.Shield => "a raised shield",
        GuardMeet.Blade => "a blade held in guard",
        GuardMeet.Parry => "a blade in its parry window",
        GuardMeet.Incidental => "a shield in the way",
        _ => "a guard",
    };
}
