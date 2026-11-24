// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Globalization;

namespace Vitomir.Platform.Host.Arena;

/// <summary>What an arena run made and how fast.</summary>
public sealed record ArenaReport(IReadOnlyList<BoutResult> Bouts, IReadOnlyList<ContactRow> Contacts, IReadOnlyList<PointSummary> Points,
    string Folder, double LoadSeconds, double WallSeconds, double BuildSeconds, double RunSeconds, long Ticks)
{
    /// <summary>The workers that ran the bouts at once.</summary>
    public int Jobs { get; init; } = 1;

    /// <summary>Bouts a second over the whole run (the load included).</summary>
    public double BoutsPerSecond => WallSeconds > 0 ? Bouts.Count / WallSeconds : 0;

    /// <summary>How many times faster than real time the bouts' ticks ran (their own run time only).</summary>
    public double TimesRealTime => RunSeconds > 0 ? Ticks / (double)Core.World.TicksPerSecond / RunSeconds : 0;

    /// <summary>The speed in words, for the log and the page's head.</summary>
    public string Speed => string.Create(CultureInfo.InvariantCulture,
        $"{Bouts.Count} bouts in {WallSeconds:0.0} s, {BoutsPerSecond:0.00} bouts/s{(Jobs > 1 ? $" with {Jobs} jobs" : "")}; the load {(Jobs > 1 ? $"once a job, {LoadSeconds:0.0} s in all" : $"once {LoadSeconds:0.0} s")}; per bout the world built in {1000 * BuildSeconds / Math.Max(1, Bouts.Count):0} ms and run in {1000 * RunSeconds / Math.Max(1, Bouts.Count):0} ms; {Ticks:N0} ticks at {Ticks / Math.Max(1e-9, RunSeconds):N0} ticks/s, {TimesRealTime:0.0}× real time");
}

/// <summary>
/// The arena: <c>vitomir-platform --arena &lt;spec.json&gt; --out &lt;folder&gt;</c>, headless —
/// no window, nothing drawn. Every swept point's bouts run in order (a fresh world each, <see cref="ArenaRun"/>), the first
/// <see cref="ArenaSpec.Tapes"/> of each point kept as tapes, then <c>bouts.csv</c>, <c>contacts.csv</c> and
/// <c>summary.html</c> written to the folder, and the speed said.
/// </summary>
public static class ArenaRunner
{
    /// <summary>
    /// Runs a spec into <paramref name="folder"/> with the game and switches of <paramref name="baseline"/>. With
    /// <paramref name="jobs"/> above one, that many workers run bouts at once, each on content of its own (a level's
    /// navigation keeps scratch buffers, so worlds sharing one must not step together); a bout is the same bout on any
    /// worker, and the rows are written in the points' and bouts' order whatever finished first.
    /// </summary>
    public static ArenaReport Execute(ArenaSpec spec, SessionOptions baseline, string folder, Action<string> say, Action<ArenaRun>? after = null, int jobs = 1)
    {
        var wall = Stopwatch.StartNew();
        Directory.CreateDirectory(folder);
        var points = spec.Points;
        int total = points.Count * spec.Bouts;
        jobs = Math.Clamp(jobs, 1, Math.Max(1, total));
        var runs = Enumerable.Range(0, jobs).Select(_ => new ArenaRun(spec, baseline)).ToArray();
        var run = runs[0];
        var slots = new BoutResult[total];
        var found = new List<ContactRow>[total];
        void Do(int i, ArenaRun on, Action<string>? speak)
        {
            int p = i / spec.Bouts, b = i % spec.Bouts;
            string? tape = b < spec.Tapes ? Path.Combine(folder, "tapes", $"p{p:000}-b{b:000}.tape") : null;
            found[i] = [];
            var r = on.Bout(p, b, found[i], tape, speak);
            if (tape is not null) r.Tape = Path.GetRelativePath(folder, tape).Replace('\\', '/');
            slots[i] = r;
        }
        // The first bout's world says what it loads and makes; the rest are quiet. The workers' content is loaded one at a time.
        if (total > 0) Do(0, run, say);
        for (int j = 1; j < jobs; j++) runs[j].Load();
        if (jobs == 1) for (int i = 1; i < total; i++) Do(i, run, null);
        else Parallel.For(0, jobs, new ParallelOptions { MaxDegreeOfParallelism = jobs }, j =>
        {
            for (int i = 1 + j; i < total; i += jobs) Do(i, runs[j], null);
        });
        var results = slots.ToList();
        var contacts = found.SelectMany(c => c).ToList();
        double build = results.Sum(r => r.BuildSeconds), ran = results.Sum(r => r.RunSeconds);
        long ticks = results.Sum(r => r.Ticks);
        for (int p = 0; p < points.Count; p++)
        {
            var done = results.Where(x => x.Point == p).ToList();
            say(string.Create(CultureInfo.InvariantCulture,
                $"arena: point {p + 1} of {points.Count}{(points[p].Length == 0 ? "" : " (" + ArenaSummary.PointName(spec, points[p]) + ")")}: "   // the actor, its sheet and the variable
                + $"{done.Count} bouts, {done.Count(x => x.KillSeconds is not null)} kills, wins {string.Join(" ", spec.Fighters.Select((f, i) => $"{f.Name} {done.Count(x => x.Winner == i)}"))}, "
                + $"{done.Sum(x => x.Fighters.Sum(f => f.Swings))} blows, {done.Sum(x => x.Fighters.Sum(f => f.ContactsBody + f.ContactsShield + f.ContactsWeapon + f.ContactsLevel))} contacts{PowersSaid(done)}{SeveranceSaid(done)}"));
        }

        ArenaTables.WriteBouts(Path.Combine(folder, "bouts.csv"), spec, results);
        ArenaTables.WriteContacts(Path.Combine(folder, "contacts.csv"), spec, contacts);
        ArenaPowers.Write(Path.Combine(folder, "powers.csv"), spec, results);                    // the powers by command
        var summary = ArenaSummary.Compute(spec, results);
        var report = new ArenaReport(results, contacts, summary, folder, runs.Sum(r => r.LoadSeconds), wall.Elapsed.TotalSeconds, build, ran, ticks) { Jobs = jobs };
        var code = run.Content?.Modules.Code.ToList() ?? [];
        var head = new ArenaSummary.Head(
            PlatformVersion.Text,
            code.Count == 0 ? "no rules module" : string.Join(", ", code.Select(c => $"{c.Info.Id} {c.Stamp.ConfigHash}")),
            code.Count == 0 ? "" : string.Join(", ", code.Select(c => $"{c.Stamp.ProfileId}{(c.Stamp.NonshippingResearch ? ", research sandbox" : "")}")),
            spec.Seed,
            report.Speed,
            string.Join(", ", (run.Content?.Modules.Modules ?? []).SelectMany(m => m.Switches).Distinct().Order(StringComparer.Ordinal)),
            run.Content?.Modules.Banner);
        File.WriteAllText(Path.Combine(folder, "summary.html"), ArenaSummary.Page(spec, summary, head));
        say($"arena: {report.Speed}");
        say($"arena: wrote {Path.Combine(folder, "bouts.csv")}, contacts.csv ({contacts.Count} contacts), summary.html and {results.Count(r => r.Tape is not null)} tapes in {Path.Combine(folder, "tapes")}");
        after?.Invoke(run);
        foreach (var r in runs) r.Content?.Dispose();
        return report;
    }

    /// <summary>The powers at a point, when any was used or refused (the per-point line).</summary>
    static string PowersSaid(List<BoutResult> done) => !done.Any(x => x.Fighters.Any(f => f.Uses + f.PowersRefused > 0)) ? ""
        : string.Create(CultureInfo.InvariantCulture, $", powers used {done.Sum(x => x.Fighters.Sum(f => f.Uses))} (Blood {done.Sum(x => x.Fighters.Sum(f => f.BloodSpent))}), refused {done.Sum(x => x.Fighters.Sum(f => f.PowersRefused))}");

    /// <summary>The point's severances by region, the swings that cut through and on, and the stops a severance made (nothing without one).</summary>
    static string SeveranceSaid(List<BoutResult> done)
    {
        var by = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in done.SelectMany(x => x.Fighters))
            foreach (var (region, n) in f.SeveredByRegion) by[region] = (by.TryGetValue(region, out int had) ? had : 0) + n;
        int chained = done.Sum(x => x.Fighters.Sum(f => f.ChainedSwings));
        if (by.Count == 0 && chained == 0) return "";
        return string.Create(CultureInfo.InvariantCulture,
            $", severances {by.Values.Sum()} ({string.Join(", ", by.Select(kv => $"{kv.Key} {kv.Value}"))}), swings cut through and on {chained}, stops by a severance {done.Count(x => x.Stop == Combat.ActorStop.SeveredStop)}");
    }
}

/// <summary>The program's <c>--arena</c>.</summary>
internal static class ArenaCommand
{
    /// <summary>
    /// <c>--arena &lt;spec.json&gt; --out &lt;folder&gt;</c> [<c>--level</c> for a spec that names none] [<c>--bouts n</c>,
    /// <c>--tapes n</c>, <c>--seed n</c>, <c>--seconds s</c> over the spec's]; the game's switches as ever (<c>--sandbox</c>).
    /// Exit code 0 when it ran, 1 when the spec or the start is refused.
    /// </summary>
    internal static int Run(Cli cli, PlatformSettings settings, HostLog log, string specPath)
    {
        string folder = cli.Arg("--out") ?? throw new SessionException("--arena needs --out <folder> for its tables, summary and tapes");
        ArenaSpec spec;
        try { spec = ArenaSpec.ReadFile(specPath, cli.Arg("--level")); }
        catch (ArenaSpecException e)
        {
            log.Say($"error: {e.Message} [{e.Error}]");
            return 1;
        }
        spec = spec with
        {
            Bouts = cli.Int("--bouts", spec.Bouts),
            Tapes = cli.Int("--tapes", spec.Tapes),
            Seed = cli.Arg("--seed") is { } s && ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed) ? seed : spec.Seed,
            Seconds = cli.Float("--seconds", spec.Seconds),
        };
        var baseline = new SessionOptions
        {
            Level = spec.Level,
            Game = Program.GameFolder(cli, settings),
            GameList = ModulesCommand.GameChoice(cli),
            ModulesFolder = cli.Arg("--modules"),
            Switches = ModulesCommand.Switches(cli),
            Say = log.Say,
            LegTurn = !cli.Flag("--no-leg-turn"),                 // the legs' turn, and speed by direction
            DirectionPace = !cli.Flag("--no-direction-pace"),
            CombatTempo = Program.CombatTempoOf(cli),   // --combat-tempo, over the spec's combat_tempo (ArenaRun.OptionsFor)
            StitchedAsQuick = Program.StitchedAsQuick(cli),        // the stitched blows as the quick ones, to measure and film them
        };
        log.Say(string.Create(CultureInfo.InvariantCulture,
            $"arena: {Path.GetFullPath(specPath)}: {spec.Fighters.Count} fighters, {spec.Points.Count} point(s) × {spec.Bouts} bouts, at most {spec.Seconds:0.#} s each, seeds from {spec.Seed}, {spec.Tapes} tape(s) per point, to {Path.GetFullPath(folder)}"));
        // --picture-at s,s,…: the first bout drawn at those times (a spectator's view; --picture WxH, --show-hit-volumes).
        var times = (cli.Arg("--picture-at") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : throw new SessionException($"--picture-at: '{t}' is not seconds"))
            .ToList();
        string out_ = Path.GetFullPath(folder);
        int jobs = cli.Int("--jobs", 1);              // bouts run at once, each worker on content of its own
        bool profile = cli.Flag("--arena-profile");    // a developer's measurement: what a tick costs, system by system
        // --film-at s0:s1[:fps] — the first bout filmed (--film-view side|near|first|third[,…], --film-name, --film-caption).
        var film = Film(cli);
        ArenaRunner.Execute(spec, baseline, out_, log.Say, times.Count == 0 && !profile && film is null ? null : run =>
        {
            if (profile) ArenaProfile.Measure(run, spec, log.Say);
            ArenaPictures.Draw(run, spec, out_, times, cli.Size("--picture") ?? (1280, 720), settings, cli.Flag("--show-hit-volumes"), log.Say, cli.Flag("--show-cues"), cli.Flag("--show-swing"));
            if (film is { } f)
                foreach (var v in f.Views)
                    ArenaPictures.Film(run, out_, f.Views.Length > 1 ? $"{f.Name}-{v}" : f.Name, f.From, f.To, f.Fps, v, f.Caption, cli.Size("--picture") ?? (960, 540), settings,
                        cli.Flag("--show-hit-volumes"), cli.Flag("--show-swing"), log.Say,
                        arc: cli.Arg("--swing-arc") is { } sa && float.TryParse(sa, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pct) ? Math.Clamp(pct, 0f, 100f) / 100f : 0f,
                        combatLog: cli.Flag("--show-combat-log"));
        }, jobs);
        return 0;
    }

    /// <summary>The film asked for (<c>--film-at s0:s1[:fps]</c>, fps 120 by default: one frame a tick), or null.</summary>
    static (float From, float To, float Fps, string[] Views, string Name, string? Caption)? Film(Cli cli)
    {
        if (cli.Arg("--film-at") is not { } at) return null;
        var parts = at.Split(':');
        float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v >= 0f ? v : throw new SessionException($"--film-at: '{s}' is not seconds (s0:s1[:fps])");
        if (parts.Length is < 2 or > 3) throw new SessionException($"--film-at: '{at}' is not s0:s1[:fps]");
        float from = F(parts[0]), to = F(parts[1]), fps = parts.Length == 3 ? F(parts[2]) : Vitomir.Platform.Core.World.TicksPerSecond;
        if (to <= from || fps <= 0f) throw new SessionException($"--film-at: '{at}': s1 must be after s0 and fps above 0");
        var views = (cli.Arg("--film-view") ?? "side").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var v in views)
            if (v is not ("side" or "near" or "first" or "third")) throw new SessionException($"--film-view: '{v}' is side, near, first or third");
        return (from, to, fps, views, cli.Arg("--film-name") ?? "bout0", cli.Arg("--film-caption"));
    }
}
