// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host;

/// <summary>
/// One <c>--foe &lt;who&gt;@x,y,z[,yaw]</c> (ours; <i>"Set up a fight where I can go after
/// one"</i>): a hostile fighter placed in play — <see cref="Who"/> a preset of the modules' <c>combat/foes</c> data
/// (<see cref="FoePresets"/>: <c>szlachta</c>, <c>guard</c>, <c>watchman</c>, <c>townsman-club</c>, <c>townsman-dagger</c>) — standing at
/// <see cref="Feet"/> (metres) and facing <see cref="YawDegrees"/> (counter-clockwise from +X; 0 when not given). Carried in a
/// recording's head (<see cref="TapeNote"/>).
/// </summary>
public readonly record struct FoeRequest(string Who, Vector3 Feet, float YawDegrees)
{
    /// <summary>Reads <c>who@x,y,z[,yaw]</c> (a point for the decimals); false with <paramref name="error"/> saying why.</summary>
    public static bool TryParse(string? text, out FoeRequest foe, out string error)
    {
        foe = default;
        error = $"--foe wants <who>@x,y,z[,yaw] (a foe's preset, metres, degrees), for example --foe szlachta@40,3,0,180; not '{text}'";
        if (string.IsNullOrWhiteSpace(text)) return false;
        int at = text.IndexOf('@');
        if (at <= 0) return false;
        var parts = text[(at + 1)..].Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 3 or > 4) return false;
        var v = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !float.IsFinite(v[i])) return false;
        foe = new FoeRequest(text[..at].Trim(), new Vector3(v[0], v[1], v[2]), parts.Length == 4 ? v[3] : 0f);
        error = "";
        return true;
    }

    /// <summary>As <see cref="TryParse"/> reads it: <c>szlachta@40,3,0,180</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Who}@{Feet.X:0.###},{Feet.Y:0.###},{Feet.Z:0.###},{YawDegrees:0.###}");

    /// <summary>The tape head's note the foes are carried in, each as <see cref="ToString"/> writes it, joined by <c>;</c> (written only when there are foes).</summary>
    public const string TapeNote = "foes";
}

/// <summary>
/// A foe's preset: the 1.1 character it is drawn and animated as (<see cref="Template"/>), the 1.1 weapon it holds
/// (<see cref="Weapon"/>, null for the character's own), whether it carries a shield (<see cref="Shield"/>, checked; null for whatever
/// its weapon holds) and the rules module's sheet template it is given (<see cref="Sheet"/>) — the arena's fighters' four fields, so
/// a foe is dressed and armed as the arena's <c>fight-*.json</c> dresses its fighters; and how near the player must come before it fights
/// (<see cref="WakeMetres"/>, the file's <c>wake_metres</c> unless the preset gives its own; 0: at once; <see cref="Combat.FoeWaits"/>).
/// </summary>
/// How it moves (<see cref="Movement"/>: a movement profile's name or <c>template</c>, its 1.1 template's walk and run read
/// from the install; null for the human's) and how its brain comes on from beyond its striking range (<see cref="Approach"/>: null or
/// <see cref="Combat.DuelApproach.Duel"/> the duelling brain's own, <see cref="Combat.DuelApproach.OneOne"/> 1.1's manoeuvre).
/// A bow it carries (<see cref="Ranged"/>, the module's bow id, with <see cref="Arrows"/>) and its brain (<see cref="Brain"/>:
/// null or <c>duel</c> the duelling brain, <c>archer</c> the archer brain, <see cref="Combat.ArcherBrain"/>).
public sealed record FoePreset(string Name, string Shown, string Template, string? Weapon, bool? Shield, string Sheet, float WakeMetres = 0f,
    string? Movement = null, Combat.DuelApproach? Approach = null, string? Ranged = null, int Arrows = 24, string? Brain = null)
{
    /// <summary>The brains a preset may name.</summary>
    public static readonly string[] Brains = ["duel", "archer"];

    /// <summary>Whether it is an archer (the archer brain, with a bow).</summary>
    public bool Archer => Brain == "archer" && Ranged is not null;
}

/// <summary>
/// The foes' presets: the modules' <c>combat/foes</c> asset (the enhanced module's <c>Data/combat/foes.json</c>, schema
/// <see cref="Schema"/>, SANDBOX), the last module in load order that has it; every problem in it named.
/// </summary>
public static class FoePresets
{
    public const string Asset = "combat/foes";
    public const string Schema = "VITOMIR_ENHANCED_FOES";

    /// <summary>The presets the modules give (empty when none has the asset), by name (case aside), with what was wrong said.</summary>
    public static IReadOnlyDictionary<string, FoePreset> From(LoadedGame modules, Action<string> say)
    {
        var presets = new Dictionary<string, FoePreset>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in modules.Modules)
        {
            var id = AssetId.Of(m.Manifest.Id, Asset);
            if (!modules.Assets.TryResolve(id, out _)) continue;
            var problems = new List<string>();
            var read = Read(System.Text.Encoding.UTF8.GetString(modules.Assets.Read(id)).TrimStart('﻿'), id.ToString(), problems);
            foreach (var p in problems) say($"foes: {p}");
            if (problems.Count == 0) { presets.Clear(); foreach (var (k, v) in read) presets[k] = v; }
        }
        return presets;
    }

    /// <summary>Reads the presets from <paramref name="json"/>; each problem added to <paramref name="problems"/> (then the result is not to be used).</summary>
    public static Dictionary<string, FoePreset> Read(string json, string file, List<string> problems)
    {
        var presets = new Dictionary<string, FoePreset>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("schema", out var schema) || schema.GetString() != Schema) problems.Add($"{file}: schema is not {Schema}");
            if (!root.TryGetProperty("status", out var status) || status.GetString() != "SANDBOX") problems.Add($"{file}: status is not SANDBOX (the foes' presets are ours, for the sandbox)");
            if (!root.TryGetProperty("foes", out var foes) || foes.ValueKind != JsonValueKind.Object) { problems.Add($"{file}: foes is not an object of presets by name"); return presets; }
            float Wake(JsonElement o, float otherwise, string what)
            {
                if (!o.TryGetProperty("wake_metres", out var v)) return otherwise;
                if (v.ValueKind != JsonValueKind.Number || !(v.GetDouble() is >= 0 and <= 100)) { problems.Add($"{file}: {what}wake_metres is a number of metres, 0 to 100"); return otherwise; }
                return (float)v.GetDouble();
            }
            float wake = Wake(root, 0f, "");
            foreach (var p in foes.EnumerateObject())
            {
                string name = p.Name;
                if (name.Length == 0 || name.IndexOfAny([',', '@', ';', ' ']) >= 0) { problems.Add($"{file}: the preset name '{name}' is empty or holds , @ ; or a space"); continue; }
                var f = p.Value;
                string? Text(string key) => f.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
                string? template = Text("template"), sheet = Text("sheet");
                if (template is null || sheet is null) { problems.Add($"{file}: foes.{name} needs a template (a 1.1 character) and a sheet (the rules module's template)"); continue; }
                bool? shield = null;
                if (f.TryGetProperty("shield", out var sh))
                {
                    if (sh.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) { problems.Add($"{file}: foes.{name}.shield is true or false"); continue; }
                    shield = sh.GetBoolean();
                }
                // ---- how it moves and comes on
                Combat.DuelApproach? approach = null;
                if (Text("approach") is { } an)
                {
                    approach = Arena.ArenaSpec.ApproachOf(an);
                    if (approach is null) { problems.Add($"{file}: foes.{name}.approach is duel or oneone, not '{an}'"); continue; }
                }
                // ---- end
                // ---- a bow, its arrows and the brain
                string? brain = Text("brain");
                if (brain is not null && Array.IndexOf(FoePreset.Brains, brain) < 0) { problems.Add($"{file}: foes.{name}.brain is {string.Join(" or ", FoePreset.Brains)}, not '{brain}'"); continue; }
                int arrows = 24;
                if (f.TryGetProperty("arrows", out var ar))
                {
                    if (ar.ValueKind != JsonValueKind.Number || !ar.TryGetInt32(out arrows) || arrows is < 0 or > 1000) { problems.Add($"{file}: foes.{name}.arrows is a whole number, 0 to 1000"); continue; }
                }
                if (brain == "archer" && Text("ranged") is null) { problems.Add($"{file}: foes.{name} has the archer brain and no ranged (a bow's id)"); continue; }
                // ---- end
                presets[name] = new FoePreset(name, Text("shown") ?? name, template, Text("weapon"), shield, sheet, Wake(f, wake, $"foes.{name}."),
                    Text("movement"), approach, Text("ranged"), arrows, brain);
            }
        }
        catch (JsonException e) { problems.Add($"{file}: not JSON ({e.Message})"); }
        return presets;
    }

    /// <summary>The preset <paramref name="who"/> names, or a <see cref="SessionException"/> listing the presets there are.</summary>
    public static FoePreset Find(IReadOnlyDictionary<string, FoePreset> presets, string who) =>
        presets.TryGetValue(who, out var p) ? p
        : throw new SessionException(presets.Count == 0
            ? $"--foe {who}: no module of this game gives foes' presets ({Asset})"
            : $"--foe {who}: there is no foe '{who}' (the presets: {string.Join(", ", presets.Keys.Order(StringComparer.Ordinal))})");
}
