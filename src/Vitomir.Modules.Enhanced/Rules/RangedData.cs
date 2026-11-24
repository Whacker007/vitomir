// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// <b>Bows</b> (ours, <c>Data/combat/ranged.json</c>, schema <see cref="SchemaId"/>, <b>SANDBOX</b>, by design):
/// each bow's numbers for the platform (<see cref="BowData"/>: the draw, the arrow's speed, mass and drag, the nock, the release),
/// every one with its source; the aim's spread by the archer's ranged Skill (Firearms, the module's "Range": <see cref="SpreadDegrees"/>); and an arrow's harm
/// (<see cref="ArrowHarm"/>: its work's dice). Read strictly: a missing or wrong field is refused by name.
/// </summary>
public sealed class RangedData
{
    public const string SchemaId = "VITOMIR_ENHANCED_RANGED";

    /// <summary>The numbers a bow's <c>arrows_heavy</c> may give (with its <c>source</c>).</summary>
    static readonly string[] Eh1Keys = ["draw_weight_newtons", "speed_at_full_draw_metres_per_second", "arrow_mass_kg", "arrow_drag_per_metre", "source"];

    /// <summary>The bows, in the file's order.</summary>
    public IReadOnlyList<BowData> Bows { get; private init; } = [];

    /// <summary>The trait whose effective rating sets the aim's spread (<c>aim_spread.trait</c>: <c>firearms</c>, the module's "Range").</summary>
    public string SpreadTrait { get; private init; } = "firearms";

    /// <summary>The spread (degrees, one standard deviation each way) by the rating 0, 1, 2, …; above the last, the last.</summary>
    public IReadOnlyList<float> SpreadByRating { get; private init; } = [];

    /// <summary>An arrow's harm: its class in the harm data, the joules of work a die, the most dice.</summary>
    public ArrowHarm Harm { get; private init; } = new("arrow", 30f, 8f);

    /// <summary>The trait whose rating sets the time to full draw (<c>draw_time.trait</c>: <c>firearms</c>, the module's "Range").</summary>
    public string DrawTrait { get; private init; } = "firearms";

    /// <summary>The seconds to full draw by the rating 0, 1, 2, …; above the last, the last (none: the bow's own <c>full_draw_seconds</c>).</summary>
    public IReadOnlyList<float> DrawSecondsByRating { get; private init; } = [];

    /// <summary>The seconds to full draw at <paramref name="rating"/>, or null with no table.</summary>
    public float? DrawSecondsAt(int rating) => DrawSecondsByRating.Count == 0 ? null : DrawSecondsByRating[Math.Clamp(rating, 0, DrawSecondsByRating.Count - 1)];

    /// <summary>The archer brain's numbers (<c>archer_brain</c>; ENGINE_SANDBOX).</summary>
    public ArcherBrainSettings Archer { get; private init; } = new();

    /// <summary>The spread at <paramref name="rating"/> (degrees).</summary>
    public float SpreadAt(int rating) => SpreadByRating.Count == 0 ? 0f : SpreadByRating[Math.Clamp(rating, 0, SpreadByRating.Count - 1)];

    /// <summary>
    /// Reads the file; with <paramref name="eh1"/> (the switch <c>arrows-heavy</c>) a bow's <c>arrows_heavy</c>
    /// (war arrow, as before: its mass, its speed at full draw and the draw weight) stands for its own numbers.
    /// </summary>
    internal static RangedData? Read(JsonElement root, string file, List<string> problems, bool eh1 = false)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        string status = c.Str(root, "status") ?? "";
        if (status != "SANDBOX") c.Fail($"status is '{status}': the bows' numbers are SANDBOX until the rules package gives them");
        var bows = new List<BowData>();
        if (c.Obj(root, "weapons") is { } all)
            foreach (var p in all.EnumerateObject())
            {
                var b = p.Value;
                string path = $"weapons.{p.Name}";
                if (b.ValueKind != JsonValueKind.Object) { c.Fail($"{path} is not an object"); continue; }
                var sources = c.Obj(b, "sources");
                // The heavy war arrow (arrows-heavy), each of its numbers in place of the bow's own; checked whether used or not.
                JsonElement? older = null;
                if (b.TryGetProperty("arrows_heavy", out var o14))
                {
                    if (o14.ValueKind != JsonValueKind.Object) c.Fail($"{path}.arrows_heavy is not an object");
                    else
                    {
                        foreach (var q in o14.EnumerateObject())
                            if (Array.IndexOf(Eh1Keys, q.Name) < 0) c.Fail($"{path}.arrows_heavy.{q.Name} is not one of {string.Join(", ", Eh1Keys)}");
                        if (string.IsNullOrWhiteSpace(c.Str(o14, "source"))) c.Fail($"{path}.arrows_heavy.source is missing");
                        older = o14;
                    }
                }
                float N(string key, double least, double most)
                {
                    if (eh1 && older is { } old && old.TryGetProperty(key, out var ov) && key != "source")
                    {
                        double y = c.Num(ov, $"{path}.arrows_heavy.{key}") ?? least;
                        if (!(y >= least && y <= most)) c.Fail($"{path}.arrows_heavy.{key} {y} is outside {least} to {most}");
                        return (float)y;
                    }
                    if (!b.TryGetProperty(key, out var v)) { c.Fail($"{path}.{key} is missing"); return (float)least; }
                    double x = c.Num(v, $"{path}.{key}") ?? least;
                    if (!(x >= least && x <= most)) c.Fail($"{path}.{key} {x} is outside {least} to {most}");
                    if (sources is { } s && (!s.TryGetProperty(key, out var src) || src.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(src.GetString())))
                        c.Fail($"{path}.sources.{key} is missing: every number says its status and source");
                    return (float)x;
                }
                bows.Add(new BowData(p.Name,
                    DrawWeightNewtons: N("draw_weight_newtons", 0, 2000),
                    FullDrawSeconds: N("full_draw_seconds", 0.05, 10),
                    SpeedAtFullDrawMetresPerSecond: N("speed_at_full_draw_metres_per_second", 1, 1500),
                    SpeedExponent: N("speed_exponent", 0.1, 4),
                    LeastShareToLoose: N("least_share_to_loose", 0, 1),
                    ArrowMassKg: N("arrow_mass_kg", 0.001, 5),
                    ArrowDragPerMetre: N("arrow_drag_per_metre", 0, 1),
                    ArrowRadiusMetres: N("arrow_radius_metres", 0, 0.1),
                    NockSeconds: N("nock_seconds", 0, 10),
                    RaiseSeconds: N("raise_seconds", 0, 10),
                    ReleaseHeightShare: N("release_height_share", 0, 1.5),
                    ReleaseForwardMetres: N("release_forward_metres", 0, 2),
                    FlightSecondsAtMost: N("flight_seconds_at_most", 0.1, 60),
                    FallenBelowMetres: N("fallen_below_metres", 0.1, 1000),
                    SwapSeconds: b.TryGetProperty("swap_seconds", out _) ? N("swap_seconds", 0, 10) : BowData.DefaultSwapSeconds));   // the player's swap
            }
        if (bows.Count == 0) c.Fail("weapons gives no bow");
        string trait = "firearms";
        var spread = new List<float>();
        if (c.Obj(root, "aim_spread") is { } a)
        {
            trait = c.Str(a, "trait") ?? trait;
            if (c.Arr(a, "degrees_by_rating") is { } list)
                foreach (var v in list.EnumerateArray())
                {
                    double x = c.Num(v, "aim_spread.degrees_by_rating") ?? 0;
                    if (!(x >= 0 && x <= 45)) c.Fail($"aim_spread.degrees_by_rating holds {x}, outside 0 to 45");
                    spread.Add((float)x);
                }
            if (spread.Count == 0) c.Fail("aim_spread.degrees_by_rating is empty");
            if (string.IsNullOrWhiteSpace(c.Str(a, "source"))) c.Fail("aim_spread.source is missing");
        }
        var harm = new ArrowHarm("arrow", 30f, 8f);
        if (c.Obj(root, "arrow_harm") is { } h)
        {
            string cls = c.Str(h, "class") ?? "arrow";
            double perDie = h.TryGetProperty("joules_per_die", out var pd) ? c.Num(pd, "arrow_harm.joules_per_die") ?? 30 : 30;
            double most = h.TryGetProperty("dice_at_most", out var dm) ? c.Num(dm, "arrow_harm.dice_at_most") ?? 8 : 8;
            if (!(perDie > 0 && perDie <= 10000)) c.Fail($"arrow_harm.joules_per_die {perDie} is outside (0, 10000]");
            if (!(most >= 0 && most <= 100)) c.Fail($"arrow_harm.dice_at_most {most} is outside 0 to 100");
            if (string.IsNullOrWhiteSpace(c.Str(h, "source"))) c.Fail("arrow_harm.source is missing");
            harm = new ArrowHarm(cls, (float)perDie, (float)most);
        }
        // The archer brain's numbers (ENGINE_SANDBOX), each with its source.
        var archer = new ArcherBrainSettings();
        if (root.TryGetProperty("archer_brain", out var ab))
        {
            if (ab.ValueKind != JsonValueKind.Object) c.Fail("archer_brain is not an object");
            else
            {
                var sources = c.Obj(ab, "sources");
                float A(string key, float otherwise)
                {
                    if (!ab.TryGetProperty(key, out var v)) { c.Fail($"archer_brain.{key} is missing"); return otherwise; }
                    double x = c.Num(v, $"archer_brain.{key}") ?? otherwise;
                    var (least, most) = ArcherBrainSettings.Ranges[key];
                    if (!(x >= least && x <= most)) c.Fail($"archer_brain.{key} {x} is outside {least} to {most}");
                    if (sources is { } src && (!src.TryGetProperty(key, out var sv) || sv.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(sv.GetString())))
                        c.Fail($"archer_brain.sources.{key} is missing: every number says its status and source");
                    return (float)x;
                }
                archer = new ArcherBrainSettings(A("keep_metres", archer.KeepMetres), A("sword_metres", archer.SwordMetres), A("hold_seconds", archer.HoldSeconds),
                    A("error_scale", archer.ErrorScale));
            }
        }
        // The seconds to full draw by Range, for everyone.
        string drawTrait = trait;
        var draw = new List<float>();
        if (root.TryGetProperty("draw_time", out var dt))
        {
            if (dt.ValueKind != JsonValueKind.Object) c.Fail("draw_time is not an object");
            else
            {
                drawTrait = c.Str(dt, "trait") ?? trait;
                if (c.Arr(dt, "seconds_by_rating") is { } list)
                    foreach (var v in list.EnumerateArray())
                    {
                        double x = c.Num(v, "draw_time.seconds_by_rating") ?? 1;
                        if (!(x >= 0.05 && x <= 10)) c.Fail($"draw_time.seconds_by_rating holds {x}, outside 0.05 to 10");
                        draw.Add((float)x);
                    }
                if (draw.Count == 0) c.Fail("draw_time.seconds_by_rating is empty");
                if (string.IsNullOrWhiteSpace(c.Str(dt, "source"))) c.Fail("draw_time.source is missing");
            }
        }
        if (problems.Count > before) return null;
        return new RangedData { Bows = bows, SpreadTrait = trait, SpreadByRating = spread, Harm = harm, Archer = archer, DrawTrait = drawTrait, DrawSecondsByRating = draw };
    }
}

/// <summary>
/// An arrow's harm (ours, SANDBOX: <c>ranged.json</c>'s <c>arrow_harm</c>): its damage potential is its work at contact,
/// <c>½·m·v²</c> (<see cref="ContactMade.ProjectileMassKg"/>, <see cref="ContactMade.PointSpeed"/>), over <paramref name="JoulesPerDie"/>, at
/// most <paramref name="DiceAtMost"/>; the harm data's class <paramref name="Class"/> gives its kind and addend. Not the archer's Strength.
/// </summary>
public sealed record ArrowHarm(string Class, float JoulesPerDie, float DiceAtMost)
{
    /// <summary>The work (J) of a projectile of <paramref name="massKg"/> at <paramref name="speed"/> m/s.</summary>
    public static double Work(float massKg, float speed) => 0.5 * massKg * (double)speed * speed;

    /// <summary>The dice of that work.</summary>
    public double Dice(float massKg, float speed) => Math.Min(DiceAtMost, Work(massKg, speed) / JoulesPerDie);
}

/// <summary>
/// The bows given to a world that has them (ours): <see cref="RangedData"/>'s bows into the platform's <see cref="BowConfig"/>,
/// the aim's spread read from the archer's sheet (its effective <see cref="RangedData.SpreadTrait"/>, Firearms, the module's "Range"; rating 0 when the registry
/// has no such trait or the archer no sheet), and <see cref="ArrowHarmRules"/> for the staged harm of an arrow's contact.
/// </summary>
public static class RangedServices
{
    public static void Register(WorldBuilder world, RangedData data, BowConfig config, Sheets.ActorSheets sheets, bool traitKnown)
    {
        if (config.Bows.Count == 0) config.Bows.AddRange(data.Bows);
        config.Archer = data.Archer;   // the archer brain's numbers
        // The time to full draw by the archer's Range (none without a sheet: the bow's own).
        string drawTrait = data.DrawTrait;
        if (data.DrawSecondsByRating.Count > 0)
            config.FullDrawSeconds = (w, e) => traitKnown && sheets.Has(e) ? data.DrawSecondsAt(sheets.Effective(e, drawTrait)) ?? 0f : 0f;
        string trait = data.SpreadTrait;
        config.SpreadDegrees = (w, e) => data.SpreadAt(traitKnown && sheets.Has(e) ? sheets.Effective(e, trait) : 0);
        world.Service(new ArrowHarmRules(data.Harm, config));
    }
}

/// <summary>The staged harm's view of arrows (ours): the harm data's weapon for an arrow's contact (its archer's bow) and the dice of its work.</summary>
public sealed class ArrowHarmRules(ArrowHarm harm, BowConfig bows)
{
    public ArrowHarm Harm { get; } = harm;

    /// <summary>The bow an arrow's contact came from (its archer's: the harm data maps the bow to the class <c>arrow</c>), or null.</summary>
    public string? BowOf(Platform.Core.World world, in ContactMade c)
    {
        if (!world.TryStore<Bow>(out var store)) return null;
        ref var bow = ref store.Find(c.Attacker);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref bow) || (uint)bow.Weapon >= (uint)bows.Bows.Count) return null;
        return bows.Bows[bow.Weapon].Id;
    }
}
