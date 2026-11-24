// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json.Nodes;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// <c>--check</c>'s part for the overrides: a module that refuses its data as a scenario overrode it is a
/// scenario error at the override's own pointer (<c>/overrides/data/2/value</c>) with the module's message (found by loading the
/// modules with each override alone when there are several), not a crash at run time; a clip rate that names no clip of its actor is
/// an error at <c>/overrides/clipRate/&lt;i&gt;/clip</c> with the actor's clips.
/// </summary>
public static partial class ScenarioCheckCommand
{
    /// <summary>What the modules said when they refused (their reasons, or the exception's message).</summary>
    static string Said(Exception e) => e switch
    {
        ModuleRefusedException r => string.Join("; ", r.Reasons),
        ModuleCheckException c => string.Join("; ", c.Problems),
        _ => $"{e.GetType().Name}: {e.Message}",
    };

    /// <summary>
    /// The runner's own check of the data overrides before it runs (the modules alone loaded once for each distinct set of a point's
    /// overrides, no world): a value the module refuses is a refusal at its pointer (exit 1), as <c>--check</c> says it, not exit 2.
    /// </summary>
    public static IReadOnlyList<ScenarioError> DataErrors(CompiledScenario scenario, SessionOptions baseline)
    {
        var errors = new List<ScenarioError>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var point in scenario.Points)
        {
            if (point.DataOverrides is not { } note || !seen.Add(note)) continue;
            var options = OptionsFor(scenario, point, 0, baseline) with { Say = _ => { } };
            try { ModulesCommand.Load(options, null); }
            catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException)) { errors.AddRange(DataRefusals(scenario, point, baseline, e)); }
        }
        return errors;
    }

    /// <summary>The errors of a point whose modules refused their data as overridden, each at the override that the module refused.</summary>
    static IEnumerable<ScenarioError> DataRefusals(CompiledScenario scenario, SweepPoint point, SessionOptions baseline, Exception e)
    {
        var all = DataOverrides.FromNote(point.DataOverrides);
        string at = point.Name.Length == 0 ? "" : $" at point {point.Index} ({point.Name})";
        string Pointer(int k) => all[k].Op == DataOverrideOp.Remove ? $"/overrides/data/{k}/pointer" : $"/overrides/data/{k}/value";
        string Message(int k, Exception x) =>
            $"the game's modules refused {all[k].Pointer} = {all[k].Value ?? "(removed)"} in {all[k].Asset}{at}: {Said(x)}";
        if (all.Count == 1) { yield return new ScenarioError(ScenarioErrorCode.Refused, Pointer(0), Message(0, e), []); yield break; }
        var options = ScenarioCheckCommand.OptionsFor(scenario, point, 0, baseline) with { Say = _ => { } };
        bool any = false;
        for (int k = 0; k < all.Count; k++)
        {
            Exception? alone = null;
            try { ModulesCommand.Load(options with { DataOverrides = DataOverrides.ToNote([all[k]]) }, null); }
            catch (Exception x) when (x is not (OutOfMemoryException or StackOverflowException)) { alone = x; }
            if (alone is null) continue;
            any = true;
            yield return new ScenarioError(ScenarioErrorCode.Refused, Pointer(k), Message(k, alone), []);
        }
        if (!any)
            yield return new ScenarioError(ScenarioErrorCode.Refused, "/overrides/data",
                $"the game's modules refused the data overrides together{at} (each alone is accepted): {Said(e)}", []);
    }

    /// <summary>The last scenario's actors' clips (<see cref="SayClips"/>), for its <c>--json</c> report's <c>clips</c>; taken once.</summary>
    [ThreadStatic] static JsonObject? clips;

    static JsonObject? TakeClips()
    {
        var c = clips;
        clips = null;
        return c;
    }

    /// <summary>Each actor's clips as its world plays them (what <c>overrides.clipRate</c> may name), said once a scenario.</summary>
    static void SayClips(Session session, IReadOnlyList<string> actors, HostLog log)
    {
        clips = new JsonObject();
        if (!session.World.TryService<Vitomir.Platform.Animation.CharacterAssets>(out var assets)) return;
        var anims = session.World.TryStore<Vitomir.Platform.Animation.CharacterAnimation>(out var a) ? a : null;
        for (int i = 0; i < actors.Count && i < session.Fighters.Count; i++)
        {
            if (anims is null || !anims.Has(session.Fighters[i])) continue;
            int kind = anims.Get(session.Fighters[i]).Kind;
            if ((uint)kind >= (uint)assets.KindCount) continue;
            var names = Session.ClipNames(assets, assets.Kind(kind));
            clips[actors[i]] = new JsonArray([.. names.Select(n => (JsonNode?)n)]);
            log.Say($"check: {actors[i]}'s clips (a clip rate names one, or {string.Join(", ", Session.ClipRateKinds)}): {string.Join(", ", names)}");
        }
    }

    /// <summary>A clip rate the world refused (it names no clip of its actor), at its own pointer; null when the message is not one.</summary>
    static ScenarioError? ClipRateError(CompiledScenario scenario, SweepPoint point, string actor, string message)
    {
        const string head = "clipRate \"";
        int start = message.IndexOf(head, StringComparison.Ordinal);
        if (start < 0) return null;
        int end = message.IndexOf('"', start + head.Length);
        if (end < 0) return null;
        string clip = message[(start + head.Length)..end];
        var rates = point.Resolved?["overrides"]?["clipRate"] as JsonArray;
        int index = -1;
        for (int i = 0; rates is not null && i < rates.Count; i++)
            if (rates[i]?["actor"]?.GetValue<string>() == actor && rates[i]?["clip"]?.GetValue<string>() == clip) { index = i; break; }
        if (index < 0) return null;
        string at = point.Name.Length == 0 ? "" : $" at point {point.Index} ({point.Name})";
        var clips = new List<string>();
        int open = message.IndexOf("(its clips: ", StringComparison.Ordinal);
        if (open >= 0)
        {
            int close = message.IndexOf(';', open);
            if (close > open) clips.AddRange(message[(open + 12)..close].Split(", ", StringSplitOptions.RemoveEmptyEntries));
        }
        clips.AddRange(Session.ClipRateKinds);
        return new ScenarioError(ScenarioErrorCode.UnknownName, $"/overrides/clipRate/{index}/clip", $"{actor}: {message[start..]}{at}", ScenarioPointer.Nearest(clip, clips));
    }
}
