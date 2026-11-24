// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
// The story sweep (ours): every single-player chronicle's every place started as the game starts
// it and run for a minute with nobody watching, to find what breaks in chapters nobody has played yet.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Vitomir.Engine;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Scripting;

/// <summary>
/// **Ours — a diagnostic, not the game.** Starts every place of every single-player chronicle
/// (<c>Chronicles/*.nsc</c> without the multiplayer bit) the way a new game or an arrival starts
/// one — the level and the chronicle's scene loaded with the chronicle's player in it, the player's
/// <c>.npc</c> sheet, kit and disciplines, the minds woken, a session and a campaign, the level's
/// sound, the chronicle's scripts compiled and entered (<c>created</c>, <c>beginscene</c>) — then
/// steps the simulation for a minute of game time with no window and no rendering, and writes what
/// went wrong: exceptions, every log line at Warning or worse, the script failures with the Java
/// stack, the natives the scripts called that nothing implements, and a few numbers. It also runs
/// the chapter scenes no location line names (<c>CNVT_3_3</c>), which the story reaches through
/// <c>SetupAdvance</c>, on the location whose level they belong to.
/// <para>
/// Every scene starts from nothing — a fresh chronicle set, a fresh script runtime and bridge, the
/// harness seed (<see cref="Chance.HarnessSeed"/>) for every stream and for the scripts'
/// <c>Math.random()</c> — so a scene's row is the same whether the whole game is swept or that one
/// scene, and the order does not matter. The story is not followed: a script asking to travel
/// (an exit, an advance, a jump) is recorded and the place carries on as the minute runs out.
/// Conversations are answered with their first reply as they wait, as <c>--answer 0</c> does, so
/// a scene that talks goes on. The run is <c>vitomir --story-sweep &lt;dir&gt;</c>.
/// </para>
/// </summary>
public sealed class StorySweep(GameFiles files, CodexCompiler compiler)
{
    /// <summary>
    /// <c>--story-buff</c> (a harness knob, not a Fix): the player at this in every stat, as <c>--buff</c>
    /// makes them, so a late chapter's fight does not end the minute before its story has run. 0, the
    /// default, is the chronicle's player as the <c>.npc</c> sheet makes them — alone and at the start's strength.
    /// </summary>
    public float Buff { get; init; }

    /// <summary>
    /// <c>--story-save-check</c> (ours): after the scene's run, the game is **saved, the save
    /// written and read back, loaded as the game loads it** (the chronicle, the place built from its
    /// record, the party, the session, the scripts armed, restored and begun) **and saved again**, and the
    /// two saves are compared part for part. A field a save writes and a load does not put back — or puts
    /// back somewhere else — shows as a difference; so does anything a load does on purpose (a
    /// `beginscene`'s side effects), which the report names. Then the loaded game is run ten seconds, and
    /// anything that throws is a finding.
    /// </summary>
    public bool SaveCheck { get; init; }

    /// <summary>One place to start: a chronicle's location with one of its scenes.</summary>
    /// <param name="Origin"><c>chronicle</c> for the scene the location line names; <c>advance</c> for
    /// a chapter scene of the same level that no line names, which a script's <c>SetupAdvance</c> puts on it.</param>
    public sealed record Target(string Chronicle, int Index, string Location, string Level, string Scene, string Origin)
    {
        public string Id => $"{Chronicle}/{Location}/{(Scene.Length > 0 ? Scene : "-")}";
    }

    /// <summary>Something a scene said or did that is worth a look, grouped in the summary by <see cref="Kind"/> and <see cref="Key"/>.</summary>
    public sealed record Finding(string Kind, string Key, string Detail);

    /// <summary>One scene's run: a row of the TSV.</summary>
    public sealed class Result(Target target)
    {
        public Target Target { get; } = target;
        /// <summary>Whether the place was built and its scripts entered.</summary>
        public bool Started { get; set; }
        /// <summary>Game seconds the simulation reached.</summary>
        public float Seconds { get; set; }
        /// <summary>Whether it reached the full minute (after any exception).</summary>
        public bool Finished { get; set; }
        /// <summary>The first exception that escaped, as <c>Type: message</c>, or "".</summary>
        public string Exception { get; set; } = "";
        public string ManagedTop { get; set; } = "";
        public string JavaTop { get; set; } = "";
        /// <summary>How many steps threw.</summary>
        public int Throws { get; set; }
        public int Things { get; set; }
        public int Minds { get; set; }
        /// <summary>Minds that gave any order in the minute (<see cref="Mind.Moves"/>).</summary>
        public int MindsActed { get; set; }
        public int Scripts { get; set; }
        public int Timers { get; set; }
        /// <summary>Calls the scripts made to the engine (<see cref="CodexHost.Calls"/>): that they ran at all.</summary>
        public int NativeCalls { get; set; }
        public int Conversations { get; set; }
        public int Answers { get; set; }
        public bool PartyAlive { get; set; }
        /// <summary>The party at the start: the player and anyone the scene put in it.</summary>
        public int Party { get; set; }
        /// <summary>Game seconds at which the player was first seen dead, or −1.</summary>
        public float PlayerDiedAt { get; set; } = -1f;
        /// <summary>A journey or an end the scripts asked for, or "".</summary>
        public string Travel { get; set; } = "";
        public long WallMs { get; set; }
        public List<Finding> Findings { get; } = [];
        public int Count(string kind) => Findings.Count(f => f.Kind == kind);
    }

    /// <summary>The kinds of finding, most serious first — the summary's order.</summary>
    public static readonly string[] Kinds =
        ["exception", "save", "script-failure", "script-says", "log-error", "log-warning", "native-missing", "film", "note"];

    /// <summary>Game time each scene is run for, in seconds.</summary>
    public const float DefaultSeconds = 60f;

    /// <summary>Each step, 1/30 s: the harness settle's step (<c>--settle</c>).</summary>
    public const float Step = 1f / 30f;

    /// <summary>After this many steps in a row throw, the scene is given up.</summary>
    public const int GiveUpAfter = 30;

    /// <summary>
    /// Every place to start: each single-player chronicle's locations in file order with the scene the
    /// line names (the one with none is started with its level's world alone), then the chapter
    /// scenes of each level no line names, on the location of that level whose own scene is of the
    /// same chapter — or the latest chapter before it, or the first. A scene is a level's when its
    /// name starts with the level's name after its prefix (<c>P1_CNVT</c> → <c>CNVT_3_3</c>).
    /// </summary>
    public static List<Target> Targets(GameFiles files, ChronicleSet set)
    {
        var list = new List<Target>();
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var single = set.All.Where(c => !c.Data.Kind.HasFlag(NscChronicleFlags.Multiplayer)).ToList();
        foreach (var c in single)
            for (int i = 0; i < c.Count; i++)
            {
                var at = c.At(i)!;
                // A location whose scene is `null` exists only to be locked (`DummyNorthGate`): the game never enters it.
                if (at.SceneName.Length == 0) continue;
                list.Add(new Target(c.Name, i, at.Name, at.LevelName, at.SceneName, "chronicle"));
                if (at.SceneName.Length > 0) named.Add(at.SceneName);
            }

        var scenes = files.List("scenes/")
            .Where(p => p.EndsWith(".nsd", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var levels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in single)
            for (int i = 0; i < c.Count; i++)
            {
                var level = c.At(i)!.LevelName;
                if (c.At(i)!.SceneName.Length == 0 || !levels.Add(level)) continue;
                string key = level.Contains('_') ? level[(level.IndexOf('_') + 1)..] : level;
                foreach (var scene in scenes)
                {
                    if (named.Contains(scene) || !Chapter.IsMatch(scene)
                        || !scene.StartsWith(key + "_", StringComparison.OrdinalIgnoreCase)) continue;
                    named.Add(scene);
                    int chapter = ChapterOf(scene);
                    var homes = single.SelectMany(h => Enumerable.Range(0, h.Count)
                            .Select(j => (h, j, loc: h.At(j)!)))
                        .Where(x => x.loc.SceneName.Length > 0
                                 && string.Equals(x.loc.LevelName, level, StringComparison.OrdinalIgnoreCase)).ToList();
                    var home = homes.FirstOrDefault(x => ChapterOf(x.loc.SceneName) == chapter);
                    if (home.loc is null)
                        home = homes.Where(x => ChapterOf(x.loc.SceneName) is var k && k >= 0 && k < chapter)
                                    .OrderByDescending(x => ChapterOf(x.loc.SceneName)).FirstOrDefault();
                    if (home.loc is null) home = homes[0];
                    list.Add(new Target(home.h.Name, home.j, home.loc.Name, level, scene, "advance"));
                }
            }
        return list;
    }

    static readonly Regex Chapter = new(@"^[A-Za-z0-9]+_(\d+)_\d+", RegexOptions.CultureInvariant);
    static int ChapterOf(string scene) =>
        Chapter.Match(scene) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;

    /// <summary>
    /// Starts one place and runs it for <paramref name="seconds"/> of game time. Never throws: what
    /// goes wrong is the result.
    /// </summary>
    public Result Run(Target target, float seconds = DefaultSeconds)
    {
        var result = new Result(target);
        var wall = Stopwatch.StartNew();
        var heard = new HeardSink();
        using var listening = Diag.Listen(heard);
        var failures = new List<(ScriptException Wrapped, Exception Root)>();
        ScriptException.Made = (w, root) => { lock (failures) failures.Add((w, root)); };
        CodexScenes? scenes = null;
        CodexHost? host = null;
        var streamsBefore = Chance.Game;
        try
        {
            Chance.Start(new Streams(Chance.HarnessSeed, "--story-sweep"));
            ScriptRandom.Seed(Chance.HarnessSeed);

            // The chronicle as a new game would find it — read afresh, so no scene sees another's flags.
            var set = ChronicleSet.Load(files);
            var chronicle = set[target.Chronicle] ?? throw new InvalidOperationException($"no chronicle {target.Chronicle}");
            if (target.Origin == "advance")
            {
                // What `SetupAdvance` and the advance itself do to the location (as <c>Chronicle.ApplyAdvanceScene</c> does).
                chronicle.At(target.Index)!.Scene = target.Scene + ".nsd";
                set = ChronicleSet.Of([.. set.All]);
            }
            chronicle.Current = target.Index;
            if (target.Scene.Length > 0 && !files.TryRead($"scenes/{target.Scene}.nsd", out _))
                result.Findings.Add(new("log-warning", $"the scene {target.Scene}.nsd is not installed; the level's world alone is loaded",
                                        "chronicle line"));

            // Who the player is: the chronicle's metafile (only Prague's names one), else Christof —
            // what `--player` and a new game do (Program.cs).
            string player = chronicle.Meta?.Player is { Length: > 0 } p ? p : "Christof";
            var place = Location.Load(files, target.Level, sky: false,
                                      scene: target.Scene.Length > 0 && files.TryRead($"scenes/{target.Scene}.nsd", out _) ? target.Scene : null,
                                      player: player);
            var effects = new EffectDefinitions(files);
            var powers = new DisciplineDefinitions(files);
            NpcFile? sheet = null;
            if (place.Player?.Thing.Actor is { RpgStats.Length: > 0 } me)
            {
                string npcPath = NpcFile.PathFor(me.RpgStats);
                if (!files.TryRead(npcPath, out var npcData))
                    result.Findings.Add(new("note", $"no {npcPath}; the player keeps the template's numbers", npcPath));
                else
                {
                    sheet = NpcFile.Read(npcData, npcPath);
                    foreach (var w in sheet.Warnings) result.Findings.Add(new("log-warning", "npc: " + w, npcPath));
                    CharacterSheets.Read(me, sheet);                                  // the sheet's stats and bounds onto the player
                    if (sheet.Items.Count > 0 && place.Templates is { } templates)
                        StartingKit.Give(place.Player.Thing, sheet, templates, effects);
                }
            }
            if (Buff > 0 && place.Player?.Thing.Actor is { } strong)
            {
                // `--buff`'s arithmetic (Program.cs): every capped stat raised, health and blood three times over.
                for (int i = 0; i < Actor.Stats; i++)
                {
                    if (i is (int)ActorStat.Generation or (int)ActorStat.Humanity or (int)ActorStat.Frenzy) continue;
                    if (strong.StatMax[i] != Actor.NoStatLimit) strong.StatMax[i] = MathF.Max(strong.StatMax[i], Buff);
                    strong.Stat[i] = strong.SetBaseStat(i, Buff);
                }
                strong.MaxHealth = MathF.Max(strong.MaxHealth, Buff * 3f);
                strong.Health = strong.MaxHealth;
                strong.Stat[(int)ActorStat.BloodPool] = strong.SetBaseStat((int)ActorStat.BloodPool, Buff * 3f);
                strong.Stat[(int)ActorStat.Blood] = strong.SetBaseStat((int)ActorStat.Blood, Buff * 3f);
            }
            var minds = place.Awaken(new AiClasses(files), 1, effects, powers);
            minds.MainFlags = MainFlagsWord.AtStart(Engine.Ui.GameOptions.Current, false);
            if (sheet is { Disciplines.Count: > 0 } && place.Player?.Thing is { } learner)
                foreach (var line in sheet.Disciplines)
                {
                    if (powers[line.Name] is not { } def)
                    { result.Findings.Add(new("log-warning", $"npc: no discipline called {line.Name}", line.Name)); continue; }
                    if (DisciplineBook.Make(learner, def) is not { } inst) continue;
                    DisciplineBook.SetLevel(learner, powers, line.Name, line.Level);
                    if (line.Quick >= 0) inst.QuickSlot = line.Quick;
                }

            var audio = new GameAudio(files);
            audio.Random = minds.Random;
            place.Audio = audio;
            var session = new Session { Here = place, Chronicle = chronicle };
            place.Session = session;
            session.Quests.Read = name => files.TryRead($"quests/{Path.GetFileNameWithoutExtension(name)}.nqd", out var nqd) ? nqd : null;
            var campaign = new Campaign
            {
                Chronicle = chronicle, Here = place,
                Build = (level, scene, entrance) => Location.Load(files, level, sky: false, scene: scene, player: player, entrance: entrance),
            };
            session.Teleports.Campaign = campaign;
            if (chronicle.At(target.Index) is { } startedIn) startedIn.Flags |= Campaign.ShowInMap;

            var camera = new WorldCamera();
            if (place.Player is { } lead) camera.Start(lead.Thing);
            var talk = new Conversations();
            host = new CodexHost();
            var runtime = ScriptRuntime.Start(compiler, host);
            var films = new List<string>();
            scenes = new CodexScenes(runtime)
            {
                Chronicles = set, Camera = camera, Talk = talk,
                Nls = new Engine.Ui.Strings(files).Find,
                RankGained = session.RankGained,
                PlayFilm = name => { films.Add(name); return false; },
            };
            audio.Enter(place);                                                   // the place's sound starts before its scripts
            scenes.Enter(place);
            result.Started = true;
            result.Scripts = scenes.Running.Count();
            var sim = new Simulation(place) { Scripts = scenes, Camera = camera, Talk = talk };
            var party = place.Things.Where(t => t.Actor is { } a && (a.Has(Actor.Flag.Party) || ReferenceEquals(t, place.Player?.Thing))).ToList();
            result.Party = party.Count;

            Conversation? last = null;
            int inRow = 0;
            for (float t = 0; t < seconds; t += Step)
            {
                try
                {
                    sim.Step(Step);
                    inRow = 0;
                }
                catch (Exception e)
                {
                    result.Throws++;
                    if (result.Exception.Length == 0) Describe(e, result);
                    if (++inRow >= GiveUpAfter) break;
                }
                result.Seconds = t + Step;
                if (result.PlayerDiedAt < 0 && place.Player?.Thing.Actor is { Dead: true }) result.PlayerDiedAt = result.Seconds;
                if (talk.Current is { } now && !ReferenceEquals(now, last)) { result.Conversations++; last = now; }
                if (talk.Current is { Waiting: true, Offered.Count: > 0 } asking)
                {
                    asking.Pick(0, sim.Milliseconds);
                    result.Answers++;
                }
                if (result.Travel.Length == 0 && chronicle.Pending is { } asked)
                    result.Travel = $"{asked.Kind}" + (chronicle.Leaving is { } go ? $" to {chronicle.NameOf(go.To)} entrance {go.Entrance}" : "")
                                  + (asked.Kind == SequenceKind.EndGame ? asked.Reason switch
                                    {
                                        Chronicle.EndStory => " (the story's end)", Chronicle.EndPartyDead => " (the party dead)",
                                        Chronicle.EndHumanityLost => " (humanity lost)", _ => $" (reason {asked.Reason})",
                                    } : "")
                                  + $" at {result.Seconds:0.0}s";
            }
            result.Finished = result.Seconds >= seconds - Step / 2 && inRow < GiveUpAfter;
            result.Things = place.Things.Count(t => t.InWorld);
            result.Minds = minds.All.Length;
            result.MindsActed = minds.All.Count(m => m.Moves > 0);
            result.Timers = scenes.TimersFired;
            result.NativeCalls = host.Calls;
            result.PartyAlive = party.Any(t => !t.Actor!.Dead);
            foreach (var film in films) result.Findings.Add(new("film", $"a script asked for the film {film}", film));
            if (SaveCheck)
            {
                Engine.Save.SaveGame? first = null;
                try { first = Engine.Save.SaveCapture.Take(place, session, campaign); }
                catch (Exception e) { result.Findings.Add(new("save", $"the save threw: {e.GetType().Name}: {First(e.Message)}", Top(e.StackTrace, 4))); }
                string weather = scenes.WeatherEffect;
                scenes.Leave();                                                     // the game ends here and a load begins (a new run)
                scenes = null;
                if (first is not null) RoundTrip(result, target, player, runtime, first, weather, made => scenes = made);
            }
        }
        catch (Exception e)
        {
            Describe(e, result);
            result.Throws++;
        }
        finally
        {
            try { scenes?.Leave(); } catch (Exception e) { result.Findings.Add(new("exception", $"leaving: {e.GetType().Name}: {First(e.Message)}", "")); }
            ScriptException.Made = null;
            if (streamsBefore is null) Chance.Stop(); else Chance.Start(streamsBefore);
        }

        // What the scripts asked for that nothing implements (CodexHost's log-once stubs, Debug in the log).
        if (host is not null)
            foreach (var (key, n) in host.Missing.OrderBy(k => k.Key, StringComparer.Ordinal))
                result.Findings.Add(new("native-missing", key, $"{n} call{(n == 1 ? "" : "s")}"));
        // The script failures with what the script threw; their one-line log messages are left out below.
        var failed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (wrapped, root) in failures)
        {
            failed.Add(wrapped.ClassName);
            // A script's own throw carries its Java frames; an engine native's .NET exception, the managed ones.
            result.Findings.Add(new("script-failure", $"{wrapped.ClassName}: {wrapped.JavaType}{Said(root)}",
                                    root is java.lang.Throwable ? JavaTop(root, 3) : Top(root.StackTrace, 3)));
        }
        foreach (var (level, category, message) in heard.Lines)
        {
            // A failure's own log line ("… failed: …", "could not create the script …") says less than its finding above.
            if (category == "Vitomir.Scripting" && failures.Any(f => message.Contains(f.Wrapped.ClassName, StringComparison.Ordinal))) continue;
            result.Findings.Add(level < LogLevel.Warning
                ? new("script-says", First(message["console: ".Length..]), "CodexConsole.PrintError/PrintException")
                : new(level >= LogLevel.Error ? "log-error" : "log-warning", $"{category}: {First(message)}", ""));
        }
        result.WallMs = wall.ElapsedMilliseconds;
        return result;
    }

    /// <summary>
    /// The save check (<see cref="SaveCheck"/>): save, write, read, load as the game does, save again,
    /// compare; then run the loaded game ten seconds.
    /// </summary>
    void RoundTrip(Result result, Target target, string player, ScriptRuntime runtime, Engine.Save.SaveGame first,
                   string weather, Action<CodexScenes> made)
    {
        string file = Path.Combine(Path.GetTempPath(), $"vitomir-story-save-{Guid.NewGuid():n}{Engine.Save.SaveArchive.Extension}");
        try
        {
            Engine.Save.SaveArchive.Write(first, file);
            var read = Engine.Save.SaveArchive.Read(file);

            // The load, in the game's order (Program.cs): the chronicle, the place from its record, the
            // party, the counter and the clock, the minds woken from the save, the session, the scripts.
            var set = ChronicleSet.Load(files);
            var chronicle = set[target.Chronicle]!;
            Engine.Save.SaveApply.Chronicle(chronicle, read.Chronicle!);
            var world = read.World[read.Header.Location];
            var into = Location.Load(files, world.Level, sky: false, scene: world.Scene.Length > 0 ? world.Scene : null,
                                     player: player, entrance: world.Entrance, guids: Engine.Save.SaveGuids.ForLoad(read));
            var effects = new EffectDefinitions(files);
            var powers = new DisciplineDefinitions(files);
            var restore = Engine.Save.SaveApply.Place(into, world, effects, powers);
            var steered = Engine.Save.SaveApply.Party(into, read.Party, restore, effects, powers);
            Engine.Save.SaveGuids.Loaded(into, read);
            into.Milliseconds = (int)read.Header.ClockMs;
            string? keepDir = Environment.GetEnvironmentVariable("VITOMIR_SAVE_CHECK_KEEP") is { Length: > 0 } kd ? Path.Combine(kd, target.Id.Replace('/', '_')) : null;
            if (keepDir is not null) Engine.Save.SaveArchive.WritePlain(Engine.Save.SaveCapture.Take(into, new Session { Here = into, Chronicle = chronicle }, null), Path.Combine(keepDir, "applied"));
            var minds = into.Awaken(new AiClasses(files), 1, effects, powers, fromSave: true);
            minds.MainFlags = MainFlagsWord.AtStart(Engine.Ui.GameOptions.Current, false);
            if (keepDir is not null) Engine.Save.SaveArchive.WritePlain(Engine.Save.SaveCapture.Take(into, new Session { Here = into, Chronicle = chronicle }, null), Path.Combine(keepDir, "awake"));
            if (steered is not null) minds.Steered = steered;
            var audio = new GameAudio(files) { Random = minds.Random };
            into.Audio = audio;
            var again = new Session { Here = into, Chronicle = chronicle };
            into.Session = again;
            again.Quests.Read = name => files.TryRead($"quests/{Path.GetFileNameWithoutExtension(name)}.nqd", out var nqd) ? nqd : null;
            Engine.Save.SaveApply.Session(again, read.Session, into);
            var loadedCampaign = new Campaign
            {
                Chronicle = chronicle, Here = into,
                Build = (level, scene, entrance) => Location.Load(files, level, sky: false, scene: scene, player: player, entrance: entrance),
            };
            again.Teleports.Campaign = loadedCampaign;
            foreach (var (name, record) in read.World)
            {
                int index = chronicle.Find(name);
                if (index < 0) continue;
                if (index == chronicle.Current) loadedCampaign.Keep(index, into);
                else loadedCampaign.Remember(index, record);
            }
            var camera = new WorldCamera();
            if (into.Player is { } lead) camera.Start(lead.Thing);
            var talk = new Conversations();
            var scenes = new CodexScenes(runtime)
            {
                Chronicles = set, Camera = camera, Talk = talk, Nls = new Engine.Ui.Strings(files).Find,
                RankGained = again.RankGained, PlayFilm = _ => false,
            };
            made(scenes);
            scenes.Reset();
            if (read.Chronicle is { } story) { scenes.ArmChronicle(story.Scripts); scenes.WeatherEffect = story.Weather ?? ""; }
            scenes.Arm(into, restore);
            audio.Enter(into);
            // The second save is taken where the load is whole and nothing has begun: every script made
            // and given back its words, timers and captures, and no `beginscene` yet — whose side effects
            // (a Pedestrian's blood top-up) are a load's on purpose, in 1.1 too.
            Engine.Save.SaveGame? second = null;
            scenes.SceneStarting = (_, restored) => { if (restored && second is null) second = Engine.Save.SaveCapture.Take(into, again, loadedCampaign); };
            scenes.Enter(into);
            second ??= Engine.Save.SaveCapture.Take(into, again, loadedCampaign);
            if (Environment.GetEnvironmentVariable("VITOMIR_SAVE_CHECK_KEEP") is { Length: > 0 } keep)   // a harness knob: both saves, loose
            {
                string at = Path.Combine(keep, target.Id.Replace('/', '_'));
                Engine.Save.SaveArchive.WritePlain(first, Path.Combine(at, "first"));
                Engine.Save.SaveArchive.WritePlain(second, Path.Combine(at, "second"));
            }
            if (weather != (second.Chronicle?.Weather ?? "")) result.Findings.Add(new("save", $"the weather was '{weather}' and came back '{second.Chronicle?.Weather}'", ""));
            Compare(result, first, second);

            // And the loaded game goes on.
            var sim = new Simulation(into) { Scripts = scenes, Camera = camera, Talk = talk };
            sim.Resume(read.Header.ClockMs / 1000f);
            for (float t = 0; t < 10f; t += Step)
            {
                try { sim.Step(Step); }
                catch (Exception e)
                {
                    result.Findings.Add(new("save", $"after the load: {e.GetType().Name}: {First(e.Message)}", Top(e.StackTrace, 4)));
                    break;
                }
                if (talk.Current is { Waiting: true, Offered.Count: > 0 } asking) asking.Pick(0, sim.Milliseconds);
            }
        }
        catch (Exception e) { result.Findings.Add(new("save", $"the load threw: {e.GetType().Name}: {First(e.Message)}", Top(e.StackTrace, 4))); }
        finally { try { File.Delete(file); File.Delete(Engine.Save.SaveArchive.PreviousOf(file)); } catch (IOException) { } }
    }

    /// <summary>Two saves, part for part and value for value, the wall-clock time left out.</summary>
    static void Compare(Result result, Engine.Save.SaveGame first, Engine.Save.SaveGame second)
    {
        first.Header.Saved = second.Header.Saved = "";
        var a = Engine.Save.SaveArchive.Parts(first, strict: true).ToDictionary(p => p.Name, p => p.Text);
        var b = Engine.Save.SaveArchive.Parts(second, strict: true).ToDictionary(p => p.Name, p => p.Text);
        foreach (var name in a.Keys.Union(b.Keys).Order(StringComparer.Ordinal))
        {
            if (!a.TryGetValue(name, out var x)) { result.Findings.Add(new("save", $"{name}: only after the load", "")); continue; }
            if (!b.TryGetValue(name, out var y)) { result.Findings.Add(new("save", $"{name}: lost by the load", "")); continue; }
            if (x == y) continue;
            var diffs = new List<(string Path, string Was, string Now)>();
            var nx = System.Text.Json.Nodes.JsonNode.Parse(x);
            var ny = System.Text.Json.Nodes.JsonNode.Parse(y);
            Expected(nx, ny);
            Walk(nx, ny, "", diffs);
            foreach (var (path, was, now) in diffs.Take(40))
                result.Findings.Add(new("save", $"{name}: {Generic(path)}", $"{path}: saved {was} | after the load {now}"));
            if (diffs.Count > 40) result.Findings.Add(new("save", $"{name}: {diffs.Count - 40} more differences", ""));
        }
    }

    /// <summary>
    /// What a load changes **on purpose**, as 1.1's does, taken out before the comparison:
    /// a place's thing list comes back turned round (the load links each record at
    /// the head); and a character with a mind — not player-type — has its discipline list turned round
    /// by the load and its AI file's disciplines learnt again as its mind is made,
    /// which renumbers its quick slots and selects the first.
    /// </summary>
    static void Expected(System.Text.Json.Nodes.JsonNode? x, System.Text.Json.Nodes.JsonNode? y)
    {
        if (x is not System.Text.Json.Nodes.JsonObject ox || y is not System.Text.Json.Nodes.JsonObject oy) return;
        if (ox["listOrder"] is System.Text.Json.Nodes.JsonArray lx && oy["listOrder"] is System.Text.Json.Nodes.JsonArray ly)
        {
            var reversed = lx.Select(n => n?.ToJsonString()).Reverse().ToList();
            var after = ly.Select(n => n?.ToJsonString()).ToList();
            // Anything made after the load goes in at the head; the rest is the saved order turned round.
            if (after.Where(reversed.Contains).SequenceEqual(reversed.Where(after.Contains))) { ox.Remove("listOrder"); oy.Remove("listOrder"); }
        }
        // Every character's discipline list is compared by name, since the load turns it round;
        // a character with a mind has its AI file's learnt again and its quick slots and
        // selection made afresh, so those are left out for one that is not player-type.
        var pairs = new List<(System.Text.Json.Nodes.JsonObject, System.Text.Json.Nodes.JsonObject)>();
        if (ox["things"] is System.Text.Json.Nodes.JsonObject tx && oy["things"] is System.Text.Json.Nodes.JsonObject ty)
            foreach (var (id, node) in tx.ToList())
                if (node?["actor"] is System.Text.Json.Nodes.JsonObject ax && ty[id]?["actor"] is System.Text.Json.Nodes.JsonObject ay) pairs.Add((ax, ay));
        if (ox["members"] is System.Text.Json.Nodes.JsonArray mx && oy["members"] is System.Text.Json.Nodes.JsonArray my)
            for (int i = 0; i < Math.Min(mx.Count, my.Count); i++)
                if (mx[i]?["actor"] is System.Text.Json.Nodes.JsonObject ax && my[i]?["actor"] is System.Text.Json.Nodes.JsonObject ay) pairs.Add((ax, ay));
        foreach (var (ax, ay) in pairs)
        {
            bool ai = ax["playerThing"]?.GetValue<bool>() != true;
            foreach (var actor in new[] { ax, ay })
            {
                if (actor["disciplines"] is not System.Text.Json.Nodes.JsonArray list) continue;
                var byName = new System.Text.Json.Nodes.JsonObject();
                foreach (var d in list.OfType<System.Text.Json.Nodes.JsonObject>().OrderBy(d => d["name"]?.ToString(), StringComparer.Ordinal))
                {
                    var copy = System.Text.Json.Nodes.JsonNode.Parse(d.ToJsonString())!.AsObject();
                    if (ai) copy.Remove("quickSlot");
                    byName[d["name"]?.ToString() ?? "?"] = copy;
                }
                actor["disciplines"] = byName;
                if (ai) actor.Remove("discipline");
            }
        }
    }

    static void Walk(System.Text.Json.Nodes.JsonNode? x, System.Text.Json.Nodes.JsonNode? y, string path, List<(string, string, string)> into)
    {
        if (x is System.Text.Json.Nodes.JsonObject ox && y is System.Text.Json.Nodes.JsonObject oy)
        {
            foreach (var key in ox.Select(kv => kv.Key).Union(oy.Select(kv => kv.Key)))
                Walk(ox[key], oy[key], path.Length == 0 ? key : $"{path}.{key}", into);
            return;
        }
        if (x is System.Text.Json.Nodes.JsonArray ax && y is System.Text.Json.Nodes.JsonArray ay && ax.Count == ay.Count)
        {
            for (int i = 0; i < ax.Count; i++) Walk(ax[i], ay[i], $"{path}[{i}]", into);
            return;
        }
        // Numbers a thousandth apart are the same number gone round a matrix (a thing's turn, set again).
        if (x is System.Text.Json.Nodes.JsonValue vx && y is System.Text.Json.Nodes.JsonValue vy
            && vx.TryGetValue<double>(out var dx) && vy.TryGetValue<double>(out var dy) && Math.Abs(dx - dy) < 1e-3) return;
        string sx = x?.ToJsonString() ?? "(absent)", sy = y?.ToJsonString() ?? "(absent)";
        if (sx != sy) into.Add((path, Cut(sx), Cut(sy)));
    }

    static string Cut(string s) => s.Length > 160 ? s[..160] + "…" : s;

    /// <summary>A difference's path with the things' own names taken out, so the summary groups alike ones.</summary>
    static string Generic(string path) =>
        Regex.Replace(Regex.Replace(path, @"things\.[^.\[]+", "things.*"), @"\[\d+\]", "[]");

    static string Said(Exception root)
    {
        string? m = root is java.lang.Throwable t ? t.getMessage() : root.Message;
        return string.IsNullOrWhiteSpace(m) ? "" : " (" + First(m) + ")";
    }

    static void Describe(Exception e, Result r)
    {
        var root = e;
        for (int guard = 0; guard < 16 && root.InnerException is { } inner; guard++) root = inner;
        if (root is java.lang.reflect.InvocationTargetException ite && ite.getTargetException() is { } target) root = target;
        r.Exception = $"{root.GetType().FullName}: {First(root.Message)}";
        r.ManagedTop = Top(e.StackTrace, 3);
        r.JavaTop = JavaTop(root, 3);
        r.Findings.Add(new("exception", $"{root.GetType().Name}: {First(root.Message)}", r.ManagedTop));
    }

    /// <summary>The first few frames a Java throwable carries (the script's own stack), or "".</summary>
    static string JavaTop(Exception e, int n)
    {
        if (e is not java.lang.Throwable t) return "";
        var frames = t.getStackTrace();
        var lines = new List<string>();
        foreach (var f in frames)
        {
            if (lines.Count >= n) break;
            var cls = f.getClassName();
            if (cls.StartsWith("java.", StringComparison.Ordinal) || cls.StartsWith("sun.", StringComparison.Ordinal)
                || cls.StartsWith("cli.", StringComparison.Ordinal) || cls.StartsWith("ikvm.", StringComparison.Ordinal)) continue;
            lines.Add($"{cls}.{f.getMethodName()}:{f.getLineNumber()}");
        }
        if (lines.Count == 0 && frames.Length > 0) lines.Add($"{frames[0].getClassName()}.{frames[0].getMethodName()}:{frames[0].getLineNumber()}");
        return string.Join(" < ", lines);
    }

    static string Top(string? stack, int n) =>
        stack is null ? "" : string.Join(" < ", stack.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("at ", StringComparison.Ordinal)).Take(n)
            .Select(l => { int paren = l.IndexOf('('); return paren > 0 ? l[3..paren] : l[3..]; }));

    static string First(string? s) => (s ?? "").Split('\n')[0].Trim().Replace('\t', ' ');

    /// <summary>The run's Warning-and-worse lines, while one scene runs (<see cref="Diag.Listen"/>).</summary>
    sealed class HeardSink : ILogSink
    {
        public SinkChannel Channel => SinkChannel.Everything;
        public List<(LogLevel Level, string Category, string Message)> Lines { get; } = [];
        public void Write(LogLevel level, string category, string message, Exception? error)
        {
            // The scripts' own complaints (`CodexConsole.PrintError`/`PrintException`: "<ERROR>: …", "<EXCPT>: …")
            // reach the log at Information as console lines (CodexScenes.Print); they are findings too.
            if (level < LogLevel.Warning && !(category == "Vitomir.Scripting" && message.StartsWith("console: <", StringComparison.Ordinal)
                                               && (message.StartsWith("console: <ERROR>", StringComparison.Ordinal)
                                                   || message.StartsWith("console: <EXCPT>", StringComparison.Ordinal)))) return;
            lock (Lines) Lines.Add((level, category, error is null ? message : $"{message} — {error.GetType().Name}: {First(error.Message)}"));
        }
        public void Dispose() { }
    }

    /// <summary>A scene that takes longer than this on the wall clock is taken to have hung.</summary>
    public const int HangMs = 10 * 60 * 1000;

    /// <summary>
    /// The whole sweep, as <c>--story-sweep &lt;dir&gt;</c> runs it: every <see cref="Targets"/> place that
    /// <paramref name="only"/> lets through (comma-separated chronicle, location, level or scene names;
    /// null for all), one after another, the report rewritten after each so a sweep that dies leaves
    /// everything before it — and <c>story-progress.txt</c> names the scene it died in. A scene that
    /// hangs (<see cref="HangMs"/>) is written as such and ends the sweep, since its thread cannot be
    /// stopped; the rest can be run with <c>--story-sweep-only</c>. Answers 0, or 3 after a hang.
    /// </summary>
    public static int Sweep(GameFiles files, CodexCompiler compiler, string dir, string? only, float seconds, TextWriter log, float buff = 0f, bool saveCheck = false)
    {
        Directory.CreateDirectory(dir);
        var targets = Targets(files, ChronicleSet.Load(files));
        if (only is { Length: > 0 })
        {
            var wanted = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            targets = targets.Where(t => wanted.Any(w => w.Equals(t.Chronicle, StringComparison.OrdinalIgnoreCase)
                                                      || w.Equals(t.Location, StringComparison.OrdinalIgnoreCase)
                                                      || w.Equals(t.Level, StringComparison.OrdinalIgnoreCase)
                                                      || w.Equals(t.Scene, StringComparison.OrdinalIgnoreCase)
                                                      || w.Equals(t.Id, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        string header = $"written {DateTime.Now:yyyy-MM-dd HH:mm} by Vitomir, {seconds:0.#} s of game time a scene, "
                      + $"seed {Chance.HarnessSeed}" + (only is { Length: > 0 } ? $", only {only}" : "")
                      + (buff > 0 ? $", the player buffed to {buff:0}" : ", the player as the sheet makes them")
                      + (saveCheck ? ", each saved, loaded and saved again at the end" : "");
        log.WriteLine($"story sweep: {targets.Count} scenes, {seconds:0.#} s each, into {dir}");
        var sweep = new StorySweep(files, compiler) { Buff = buff, SaveCheck = saveCheck };
        var results = new List<Result>();
        var all = Stopwatch.StartNew();
        int n = 0;
        foreach (var target in targets)
        {
            n++;
            File.WriteAllText(Path.Combine(dir, "story-progress.txt"), $"running {n} of {targets.Count}: {target.Id}\n");
            Result? done = null;
            var worker = new Thread(() => done = sweep.Run(target, seconds)) { IsBackground = true, Name = "story sweep" };
            worker.Start();
            if (!worker.Join(HangMs))
            {
                var hung = new Result(target) { Started = true, Exception = $"hung: no end after {HangMs / 60000} minutes of wall time" };
                hung.Findings.Add(new("exception", "the scene hung", ""));
                results.Add(hung);
                Write(dir, results, header);
                log.WriteLine($"  {n,3}/{targets.Count} {target.Id}: HUNG; the sweep stops here (run the rest with --story-sweep-only)");
                return 3;
            }
            results.Add(done!);
            Write(dir, results, header);
            log.WriteLine($"  {n,3}/{targets.Count} {target.Id}: {(done!.Started ? done.Finished ? "ran" : $"stopped at {done.Seconds:0.0}s" : "did not start")}"
                        + (done.Exception.Length > 0 ? $", {done.Exception}" : "")
                        + $", {done.Count("script-failure")} script failures, {done.Count("script-says")} script complaints, {done.Count("log-error")} errors, {done.Count("log-warning")} warnings,"
                        + $" {done.Count("native-missing")} natives missing"
                        + (saveCheck ? $", {done.Count("save")} save differences" : "") + $" ({done.WallMs} ms)");
        }
        File.WriteAllText(Path.Combine(dir, "story-progress.txt"), $"done: {targets.Count} scenes in {all.Elapsed.TotalMinutes:0.0} minutes\n");
        log.WriteLine($"story sweep: {targets.Count} scenes in {all.Elapsed.TotalMinutes:0.0} minutes; see {Path.Combine(dir, "story-sweep.md")}");
        return 0;
    }

    // ---- The report ----------------------------------------------------------------------------

    /// <summary>The TSV's columns, one row a scene.</summary>
    public static readonly string[] Columns =
    [
        "chronicle", "index", "location", "level", "scene", "origin", "started", "finished", "seconds",
        "exception", "managed_top", "java_top", "throws", "script_failures", "script_says", "errors", "warnings",
        "natives_missing", "things", "minds", "minds_acted", "scripts", "timers", "native_calls", "conversations", "answers",
        "party", "party_alive", "player_died_at", "travel", "wall_ms",
    ];

    public static string Row(Result r) => string.Join('\t', new object[]
    {
        r.Target.Chronicle, r.Target.Index, r.Target.Location, r.Target.Level, r.Target.Scene, r.Target.Origin,
        r.Started ? 1 : 0, r.Finished ? 1 : 0, r.Seconds.ToString("0.0", CultureInfo.InvariantCulture),
        r.Exception, r.ManagedTop, r.JavaTop, r.Throws, r.Count("script-failure"), r.Count("script-says"), r.Count("log-error"),
        r.Count("log-warning"), r.Count("native-missing"), r.Things, r.Minds, r.MindsActed, r.Scripts, r.Timers, r.NativeCalls,
        r.Conversations, r.Answers, r.Party, r.PartyAlive ? 1 : 0,
        r.PlayerDiedAt < 0 ? "" : r.PlayerDiedAt.ToString("0.0", CultureInfo.InvariantCulture), r.Travel, r.WallMs,
    }.Select(v => Clean(Convert.ToString(v, CultureInfo.InvariantCulture))));

    static string Clean(string? s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>
    /// Writes <c>story-sweep.tsv</c> (one row a scene), <c>story-findings.tsv</c> (one row a finding) and
    /// <c>story-sweep.md</c> (the findings grouped by kind and message, with how many scenes each affects,
    /// the most widespread first) into <paramref name="dir"/>.
    /// </summary>
    public static void Write(string dir, IReadOnlyList<Result> results, string header)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "story-sweep.tsv"),
            new[]
            {
                "# Vitomir story sweep — one row per scene: started as the game starts it, run with no window.",
                "# " + header,
                string.Join('\t', Columns),
            }.Concat(results.Select(Row)));
        File.WriteAllLines(Path.Combine(dir, "story-findings.tsv"),
            new[]
            {
                "# Vitomir story sweep — every finding, one per line: the scene, its kind, what was said, and where it points.",
                "# " + header,
                "scene\tkind\tmessage\tdetail",
            }.Concat(results.SelectMany(r => r.Findings.Select(f => $"{Clean(r.Target.Id)}\t{f.Kind}\t{Clean(f.Key)}\t{Clean(f.Detail)}"))));
        File.WriteAllText(Path.Combine(dir, "story-sweep.md"), Summary(results, header));
    }

    /// <summary>A message with its numbers taken out, so the same complaint about two things groups as one.</summary>
    public static string Group(string message) => Numbers.Replace(message, "#");
    static readonly Regex Numbers = new(@"(?<![A-Za-z_])-?\d+(\.\d+)?", RegexOptions.CultureInvariant);

    public static string Summary(IReadOnlyList<Result> results, string header)
    {
        var s = new StringBuilder();
        int started = results.Count(r => r.Started), finished = results.Count(r => r.Finished);
        s.AppendLine("# Story sweep");
        s.AppendLine();
        s.AppendLine($"{header}. {results.Count} scenes: {started} started, {finished} ran the full time, "
                   + $"{results.Count(r => r.Exception.Length > 0)} with an exception, "
                   + $"{results.Count(r => r.Started && !r.PartyAlive)} with the party dead or absent at the end, "
                   + $"{results.Count(r => r.Travel.Length > 0)} asked to travel.");
        s.AppendLine();
        s.AppendLine("| kind | distinct | scenes affected |");
        s.AppendLine("|---|---:|---:|");
        foreach (var kind in Kinds)
        {
            var of = results.Where(r => r.Findings.Any(f => f.Kind == kind)).ToList();
            int distinct = results.SelectMany(r => r.Findings).Where(f => f.Kind == kind).Select(f => Group(f.Key)).Distinct().Count();
            s.AppendLine($"| {kind} | {distinct} | {of.Count} |");
        }
        foreach (var kind in Kinds)
        {
            var groups = results
                .SelectMany(r => r.Findings.Where(f => f.Kind == kind).Select(f => (r, f)))
                .GroupBy(x => Group(x.f.Key))
                .Select(g => (Key: g.Key, Scenes: g.Select(x => x.r.Target.Id).Distinct().ToList(), Detail: g.First().f.Detail, Example: g.First().f.Key))
                .OrderByDescending(g => g.Scenes.Count).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
            if (groups.Count == 0) continue;
            s.AppendLine();
            s.AppendLine($"## {kind} ({groups.Count})");
            s.AppendLine();
            foreach (var g in groups)
            {
                string where = g.Scenes.Count <= 4 ? string.Join(", ", g.Scenes) : string.Join(", ", g.Scenes.Take(3)) + $", … ({g.Scenes.Count - 3} more)";
                s.AppendLine($"- **{g.Scenes.Count}** — `{Md(g.Example)}`" + (g.Detail.Length > 0 ? $" — {Md(g.Detail)}" : "") + $" — {where}");
            }
        }
        var went = results.Where(r => r.Travel.Length > 0).ToList();
        if (went.Count > 0)
        {
            s.AppendLine();
            s.AppendLine("## scenes whose scripts asked to travel or end (the sweep does not follow)");
            s.AppendLine();
            foreach (var r in went)
                s.AppendLine($"- {r.Target.Id}: {r.Travel}" + (r.PlayerDiedAt >= 0 ? $"; the player died at {r.PlayerDiedAt:0.0}s" : ""));
        }
        var odd = results.Where(r => !r.Started || !r.Finished || r.Started && !r.PartyAlive).ToList();
        if (odd.Count > 0)
        {
            s.AppendLine();
            s.AppendLine("## scenes that did not start, did not finish, or ended without a living party");
            s.AppendLine();
            foreach (var r in odd)
                s.AppendLine($"- {r.Target.Id}: " + (!r.Started ? "did not start" : !r.Finished ? $"stopped at {r.Seconds:0.0}s" : "party dead or absent")
                           + (r.Exception.Length > 0 ? $" — `{Md(r.Exception)}`" : ""));
        }
        return s.ToString();
    }

    static string Md(string s) => s.Replace('`', '\'').Replace('|', '/');
}
