// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: one run again by its id, and an event's chain.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// <c>--only-run &lt;run_id&gt;</c>'s pieces — a run id read (<c>p003-r017</c>: point 3, run 17), the recorded
/// run found in an earlier results folder (<c>--compare &lt;results.json&gt;</c>: its <c>runs.csv</c> row: seed and digest), the
/// verdict written beside the run (<c>only-run.json</c>) — and <c>--trace-event</c>: an event's chain in a run's stream
/// (<c>events.jsonl</c>), its ancestors by <c>parentEventId</c> up to the first and every event that follows from it.
/// </summary>
public static partial class ScenarioTraceEvent
{
    [GeneratedRegex(@"^p(\d+)-r(\d+)$")]
    private static partial Regex RunIdForm();

    [GeneratedRegex(@"^p(\d+)@(\d+)$")]
    private static partial Regex PointSeedForm();

    /// <summary>A point and a seed (<c>p003@4717</c>: an assertion's first failing seed at point 3); false when it is not one.</summary>
    public static bool TryPointSeed(string text, out int point, out ulong seed)
    {
        point = -1;
        seed = 0;
        var m = PointSeedForm().Match(text.Trim());
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out point)
               && ulong.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out seed);
    }

    /// <summary>A run id (<c>p003-r017</c>) as its point and run; false when it is not one.</summary>
    public static bool TryRunId(string text, out int point, out int run)
    {
        point = run = -1;
        var m = RunIdForm().Match(text.Trim());
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out point)
               && int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out run);
    }

    /// <summary>The run as recorded: its seed and end digest from the <c>runs.csv</c> beside <paramref name="results"/> (a results.json, or its folder); null with why.</summary>
    public static (ulong Seed, string Digest, string ScenarioSha)? Recorded(string results, string runId, out string why)
    {
        why = "";
        string folder = Directory.Exists(results) ? results : Path.GetDirectoryName(Path.GetFullPath(results)) ?? ".";
        string runsCsv = Path.Combine(folder, "runs.csv");
        if (!File.Exists(runsCsv)) { why = $"{runsCsv} is not there (the earlier run's tables were not written: \"outputs\": {{ \"csv\": true }})"; return null; }
        using var reader = new StreamReader(runsCsv);
        var head = Csv(reader.ReadLine() ?? "");
        int id = head.IndexOf("run_id"), seed = head.IndexOf("seed"), digest = head.IndexOf("digest"), sha = head.IndexOf("scenario_sha256");
        if (id < 0 || seed < 0 || digest < 0) { why = $"{runsCsv} has no run_id, seed and digest columns"; return null; }
        for (string? line; (line = reader.ReadLine()) is not null;)
        {
            var cells = Csv(line);
            if (cells.Count <= Math.Max(id, Math.Max(seed, digest)) || cells[id] != runId) continue;
            if (!ulong.TryParse(cells[seed], NumberStyles.None, CultureInfo.InvariantCulture, out ulong s)) { why = $"{runsCsv}: {runId}'s seed \"{cells[seed]}\" is not a number"; return null; }
            return (s, cells[digest], sha >= 0 && sha < cells.Count ? cells[sha] : "");
        }
        why = $"{runsCsv} has no run {runId}";
        return null;
    }

    /// <summary>A run's end digest from a runs.csv (the one just written), or null.</summary>
    public static string? DigestIn(string folder, string runId) => Recorded(folder, runId, out _)?.Digest;

    static List<string> Csv(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    /// <summary>
    /// <c>--trace-event</c>: <paramref name="spec"/> is <c>&lt;stream&gt;#&lt;eventId&gt;</c> (a run's <c>events.jsonl</c>), <c>&lt;run_id&gt;#&lt;eventId&gt;</c>
    /// with <paramref name="outFolder"/> (its <c>events/&lt;run_id&gt;.events.jsonl</c>), or an event id alone with <paramref name="stream"/>.
    /// Prints the chain (with <paramref name="json"/>: one JSON document on <paramref name="stdout"/>); 0, or 1 when it cannot.
    /// </summary>
    public static int Run(string spec, string? outFolder, string? stream, bool json, Action<string> say, TextWriter stdout)
    {
        string path;
        string idText;
        int hash = spec.LastIndexOf('#');
        if (hash < 0)
        {
            if (stream is null) { say("error: --trace-event takes <events.jsonl>#<eventId>, or <run_id>#<eventId> with --out <results folder> (e.g. p000-r003#41, as results.json's evidence names events)"); return 1; }
            (path, idText) = (stream, spec);
        }
        else
        {
            string left = spec[..hash];
            idText = spec[(hash + 1)..];
            path = TryRunId(left, out _, out _) && outFolder is not null ? Path.Combine(outFolder, "events", left + ".events.jsonl") : left;
        }
        if (!long.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out long eventId)) { say($"error: --trace-event: \"{idText}\" is not an event id (a whole number: the stream's eventId)"); return 1; }
        if (!File.Exists(path)) { say($"error: --trace-event: {path} is not there (a run's stream is kept when the trace policy keeps the run: --only-run <run_id> writes it)"); return 1; }
        var events = new Dictionary<long, JsonObject>();
        var children = new Dictionary<long, List<long>>();
        bool first = true;
        foreach (var line in File.ReadLines(path))
        {
            if (first) { first = false; continue; }   // the head
            if (line.Length == 0 || JsonNode.Parse(line) is not JsonObject o || o["eventId"] is not JsonValue idv || !idv.TryGetValue(out long id)) continue;
            events[id] = o;
            if (o["parentEventId"] is JsonValue pv && pv.TryGetValue(out long parent))
            {
                if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
                list.Add(id);
            }
        }
        if (!events.TryGetValue(eventId, out var self)) { say($"error: --trace-event: {path} has no event {eventId} (its events are 0 to {events.Count - 1})"); return 1; }
        var ancestors = new List<long>();
        for (long at = eventId; events[at]["parentEventId"] is JsonValue up && up.TryGetValue(out long p) && events.ContainsKey(p) && !ancestors.Contains(p); at = p) ancestors.Insert(0, p);
        var descendants = new List<(long Id, int Depth)>();
        void Down(long id, int depth)
        {
            if (!children.TryGetValue(id, out var kids)) return;
            foreach (var k in kids) { descendants.Add((k, depth)); Down(k, depth + 1); }
        }
        Down(eventId, 1);
        if (json)
        {
            var doc = new JsonObject
            {
                ["stream"] = Path.GetFullPath(path),
                ["eventId"] = eventId,
                ["event"] = self.DeepClone(),
                ["ancestors"] = new JsonArray([.. ancestors.Select(a => (JsonNode?)events[a].DeepClone())]),
                ["descendants"] = new JsonArray([.. descendants.Select(d => (JsonNode?)events[d.Id].DeepClone())]),
            };
            stdout.WriteLine(doc.ToJsonString(ScenarioSpec.Writing));
            return 0;
        }
        say($"trace-event: event {eventId} of {Path.GetFullPath(path)}: {ancestors.Count} before it (its parents up to the first), {descendants.Count} after it (every event that follows from it)");
        int depth0 = 0;
        foreach (var a in ancestors) say("  " + new string(' ', 2 * depth0++) + Line(events[a]));
        say("  " + new string(' ', 2 * depth0) + "> " + Line(self));
        foreach (var (id, d) in descendants) say("  " + new string(' ', 2 * (depth0 + d)) + Line(events[id]));
        return 0;
    }

    /// <summary>An event in one line: id, time, kind, who on whom, and its own fields (people by name), shortened.</summary>
    static string Line(JsonObject o)
    {
        string kind = o["kind"]?.GetValue<string>() ?? "?";
        double seconds = o["seconds"] is JsonValue sv && sv.TryGetValue(out double s) ? s : 0;
        var actors = o["actors"] as JsonObject;
        var fields = new List<string>();
        if (o["event"] is JsonObject ev)
            foreach (var (k, v) in ev)
            {
                if (k is "Tick" || v is null) continue;
                string text = actors?[k] is JsonValue an && an.TryGetValue(out string? name) ? name! : v is JsonValue jv ? jv.ToJsonString().Trim('"') : v.ToJsonString();
                fields.Add($"{k} {text}");
            }
        if (o["derived"] is JsonObject dv) foreach (var (k, v) in dv) fields.Add($"{k} {v?.ToJsonString().Trim('"')}");
        string all = string.Join(", ", fields);
        if (all.Length > 220) all = all[..217] + "...";
        return string.Create(CultureInfo.InvariantCulture, $"#{o["eventId"]} {seconds:0.000} s {kind}: {all}");
    }
}
