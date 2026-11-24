// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The multiplayer runs that need no window: a host nobody plays on, and a
// player that plays by itself — so a game can be run on one laptop with a headless host, a headless
// player or two, and the real game joined to them.
//
//   --headless-server        host a game with no window: the chronicle's world, its scripts and AI,
//                            and the host's console (type a line to chat, /start, /pause, /kick <slot>,
//                            /players, /talk <slot> <file> [conversation], /quit; a piped stdin works too)
//   --net-bot                a player that joins by itself and wanders, fights and chats
//   --port <n>               the host's port (1.1's 4994 by default)
//   --join <host[:port]>     where the bot (or the game) joins
//   --mp-chronicle <name>    the chronicle to host (MP_DAInn, the Dark Ages Inn salon, by default)
//   --game-name <text>       the game's name  --password <text>  --max-players <n> (1 to 8; 1.1's 8 by default)
//   --moderated              Storyteller moderation (the host may take the Storyteller's seat)
//   --no-auto-st             without Auto Storyteller
//   --name <text>            the player's name   --player-template <t>  the character's model
//   --settle <seconds>       stop after this long (otherwise run until /quit or Ctrl-C)
//   --crypt <folder>         (the bot) join as the character of --name kept there (a `.vplayer`)
//   --no-upnp                do not ask the router to map the port (UPnP; see Engine.Net.PortMapping)
using System.Globalization;
using System.Numerics;
using Vitomir.Engine;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Scripting;
using Vitomir.Classic;

namespace Vitomir;

public static class NetRuns
{
    /// <summary>Whether the command line asks for one of these runs.</summary>
    public static bool Wanted(string[] args) => args.Contains("--headless-server") || args.Contains("--net-bot");

    internal static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
    }

    /// <summary>The game the flags describe: the chronicle's (`GameSettings.For`), or a bare level and scene.</summary>
    public static GameSettings? Settings(string[] args, ChronicleSet chronicles, string playerName)
    {
        var flags = GameFlags.AutoStoryteller;
        if (args.Contains("--no-auto-st")) flags &= ~GameFlags.AutoStoryteller;
        if (args.Contains("--moderated")) flags |= GameFlags.Moderated;
        string name = Arg(args, "--game-name") ?? $"{playerName}'s game";
        string password = Arg(args, "--password") ?? "";
        int max = int.TryParse(Arg(args, "--max-players"), out var m) ? Math.Clamp(m, 1, GameSettings.Slots) : GameSettings.Slots;   // 1.1's eight
        string wanted = Arg(args, "--mp-chronicle") ?? "MP_DAInn";
        if (Arg(args, "--level") is { } level)
            return new GameSettings
            {
                Name = name, Level = level, Scene = Arg(args, "--scene") ?? "", Flags = flags, Password = password,
                MaxPlayers = max, Entrance = int.TryParse(Arg(args, "--entrance"), out var e) ? e : 0,
            };
        if (chronicles[wanted] is not { } chronicle)
        {
            Console.Error.WriteLine($"net: no chronicle called {wanted}; the multiplayer ones are " +
                string.Join(", ", chronicles.All.Where(c => c.Kind.HasFlag(Formats.NscChronicleFlags.Multiplayer)).Select(c => c.Name)));
            return null;
        }
        return GameSettings.For(chronicle, name, flags, password, max);
    }

    /// <summary>
    /// **The run these flags ask for, a frame at a time** (the network game under the
    /// platform): a state of the program's pushed frame on a host that opens no window, as the front end and the game are. Each frame
    /// is one turn of the loop the run had of its own until then (from one look at the clock to the next), and its sleep at the end of
    /// a turn is the host's wait before the next frame (<see cref="RunStep.After"/>, the host's <c>LoopResult.NextFrameNs</c>), so no
    /// frame holds the host's frame loop. The run starts in the frame its <see cref="RunStart"/> prepared it in.
    /// </summary>
    internal static ClassicRun Run(IClassicHost host, RunStart start, FrameInput input) =>
        start.args.Contains("--headless-server") ? new ServerRun(host, start, input) : new BotRun(host, start, input);

    /// <summary>What a turn waited before the next until then (<c>Thread.Sleep</c>), now the host's wait: the host's 5 ms, the bot's 15 ms.</summary>
    internal const ulong ServerWaitNs = 5_000_000UL, BotWaitNs = 15_000_000UL;

    // ---- the host with no window ----------------------------------------------------------------

    /// <summary>
    /// <c>--headless-server</c>: the chronicle's world with its scripts and AI, stepped to the wall clock at 30 steps a second, the
    /// players' places, the chronicle's journeys, the wire polled and the console's lines read, then the host's 5 ms wait.
    /// </summary>
    sealed class ServerRun : ClassicRun
    {
        readonly ChronicleSet chronicles;
        GameSettings settings = null!;
        Location place = null!;
        Session session = null!;
        Simulation sim = null!;
        NetPeer peer = null!;
        GameServer server = null!;
        CodexScenes? scenes;
        Campaign? campaign;
        System.Collections.Concurrent.ConcurrentQueue<string>? typed;
        double settle, simulated;
        ulong startNs;
        bool running = true;
        /// <summary>Why the host stops: /quit, its window closed; null for --settle's end.</summary>
        string? stopping;
        const float step = 1f / 30f;

        internal ServerRun(IClassicHost host, RunStart start, FrameInput input) : base(host, start, input) => chronicles = start.chronicles;

        internal override int? Begin(ulong now)
        {
            int port = int.TryParse(NetRuns.Arg(args, "--port"), out var p) ? p : NetPeer.DefaultPort;
            // Somewhere to print and to type the host's lines: a console window of its own, named for the
            // game and its port, never the terminal it was started from; a redirect left as it is. What was done goes in the log.
            Said(host.TextConsole(ownWindow: true, $"Vitomir dedicated host: {NetRuns.Arg(args, "--game-name") ?? "Server's game"}, UDP {port}"));
            if (Settings(args, chronicles, "Server") is not { } chosen) return 2;
            settings = chosen;
            var strings = new Engine.Ui.Strings(files);

            Console.WriteLine($"net: hosting \"{settings.Name}\" — {settings.Chronicle} at {settings.Level}" +
                              $"{(settings.Scene.Length > 0 ? "/" + settings.Scene : "")}, entrance {settings.Entrance}, UDP {port}");
            place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null,
                                  entrance: settings.Entrance, guids: ThingGuids.For(host: true));   // the host's bit
            var effects = new EffectDefinitions(files);
            var powers = new DisciplineDefinitions(files);
            place.Awaken(new AiClasses(files), 1, effects, powers);
            session = new Session { Here = place, Chronicle = chronicles[settings.Chronicle] };
            place.Session = session;
            session.Messages.Text = strings;
            session.Quests.Read = name => files.TryRead($"quests/{Path.GetFileNameWithoutExtension(name)}.nqd", out var nqd) ? nqd : null;
            session.Messages.Posted += m => Console.WriteLine($"  message: {m.Text}");
            sim = new Simulation(place);

            try { peer = new NetPeer(port); }
            catch (PortTakenException taken)
            {
                // One plain line, in the log as on the console, and the host ends (2) instead of throwing.
                Console.Error.WriteLine($"net: {taken.Message}");
                Said($"net: {taken.Message}");
                return 2;
            }
            server = new GameServer(settings, peer) { Find = strings.Find, Identity = NetIdentity.Of(files) };   // the build and data a join must match
            Console.WriteLine($"net: this host: {server.Identity}");
            server.Log += Console.WriteLine;
            server.Posted += (text, _) => Console.WriteLine($"  room: {text}");
            server.Heard += (_, text) => Console.WriteLine($"  chat: {text}");
            server.HostChat += (text, _) => Console.WriteLine($"  host's chat: {text}");

            if (!Flag("--no-scripts"))
            {
                try
                {
                    var engine = new CodexHost();
                    var compiler = CodexCompiler.For(files, Vitomir.Engine.Vfs.GameData.CodexCache, Flag(CodexCompiler.SourcesSwitch));
                    var runtime = ScriptRuntime.Start(compiler, engine, Flag("--rebuild-scripts"));
                    foreach (var line in runtime.Build.Lines()) Console.WriteLine(line);
                    scenes = new CodexScenes(runtime) { Chronicles = chronicles, Nls = strings.Find };
                    NetNatives.Register(scenes, server);
                    // The chronicle's conversations: a player's goes to their machine (`HostScenes`).
                    scenes.Talk = sim.Talk = new Conversations();
                    sim.Scripts = scenes;
                    scenes.Enter(place);
                    Console.WriteLine($"scripts: {scenes.Running.Count()} running{(scenes.ChronicleName is { } c ? ", " + c : "")}");
                }
                catch (Exception e) { Console.WriteLine($"scripts: not running ({e.Message.Split('\n')[0]})"); }
            }
            server.Start();
            server.Enter(place, null);
            if (!Flag("--no-upnp")) _ = Connectors.Current.Offer(peer.Port, Console.WriteLine);   // the router asked (the connector seam)
            // The chronicle's journeys: an exit a script takes moves the whole game (`Campaign`, as the
            // single-player game's own does), and every player's machine is told to follow.
            var story = scenes?.Chronicle ?? chronicles[settings.Chronicle];
            if (story is not null && story.Current < 0)
                story.Current = Math.Max(0, Enumerable.Range(0, story.Count)
                    .FirstOrDefault(i => story.At(i)!.LevelName.Equals(settings.Level, StringComparison.OrdinalIgnoreCase), -1));
            campaign = story is null ? null : new Campaign
            {
                Chronicle = story,
                Here = place,
                Build = (level, scene, entrance) =>
                {
                    var next = Location.Load(files, level, scene: scene, entrance: entrance);
                    next.Awaken(new AiClasses(files), 1, effects, powers);
                    return next;
                },
            };
            campaign?.Host();                                     // the host's eleven, each place filed (CampaignFiles.cs)
            // Players in different places: a player goes somewhere alone, the campaign loads it (NetPlaces).
            if (campaign is not null) NetPlaces.Wire(server, campaign, session, scenes, () => sim.Talk);
            Console.WriteLine($"net: listening on UDP {peer.Port}. Join with:  vitomir --join 127.0.0.1:{peer.Port}   " +
                              $"(or a bot: vitomir --net-bot --join 127.0.0.1:{peer.Port})");
            Console.WriteLine("net: type a line to chat; /players, /pause, /loadscreen [off], /kick <slot>, /talk <slot> <file> [conversation], " +
                              "/text <slot> <document>, /advance <slot> [experience], /jump <slot|name> <location> [entrance], /shop <slot> <file.nbs> [cash], " + NetSeenConsole.Help + ", /quit");

            typed = StartReader();
            settle = double.TryParse(NetRuns.Arg(args, "--settle"), CultureInfo.InvariantCulture, out var s) ? s : double.PositiveInfinity;
            // The host's clock from here, as the run's own stopwatch started here until then.
            startNs = host.Now();
            return null;
        }

        /// <summary>The seconds since the run's clock started, at the host clock <paramref name="ns"/>.</summary>
        double Seconds(ulong ns) => ns > startNs ? (ns - startNs) / 1e9 : 0;

        internal override RunStep Step(ulong now)
        {
            double elapsed = Seconds(now);
            if (!(running && elapsed < settle))
            {
                // Why the host stops, in the module's log as on the console (the console may be a window closing).
                string why = stopping ?? $"--settle {settle:0.###} has run its time";
                Said($"net: the host stops: {why}");
                server.Dispose();
                peer.Dispose();
                string stopped = $"net: the host has stopped after {Seconds(host.Now()):0} s";
                Console.WriteLine(stopped);
                Said(stopped);
                return RunStep.Done(0);
            }
            while (simulated < elapsed)
            {
                sim.Step(server.Paused ? 0f : step);
                server.Places.Step(server.Paused ? 0f : step);           // the places players went to alone (HostPlaces)
                simulated += step;
            }
            campaign?.SwapChapters();                                        // the end of the frame's chapter swap (ChapterSwap.cs)
            if (campaign?.Chronicle.Leaving is { } leaving && campaign.Travel() is { } arrival)
            {
                scenes?.Leave(leaving.Exit < 0 ? 0 : leaving.Exit, keep: campaign.Resident.Values.Any(r => ReferenceEquals(r, arrival.From)));
                var was = sim;
                place = arrival.To;
                place.Session = session;
                session.Here = place;
                sim = new Simulation(place) { Scripts = scenes ?? (ISceneScripts)NoScripts.Instance, Talk = was.Talk };
                sim.Resume(was.Seconds);
                scenes?.Enter(place);
                server.Travel(place, null);
            }
            server.Poll((int)(elapsed * 1000));
            while (running && typed is not null && typed.TryDequeue(out var line))
            {
                running = Talk(server, sim, line) || NetSeenConsole.Command(server, line, Console.WriteLine) || Command(server, line);
                if (!running) stopping = "/quit typed on its console";
            }
            // Its console window closed by its X (or Ctrl+C in it): the host stops as /quit stops it.
            if (running && host.TextConsoleClosed)
            {
                Console.WriteLine("net: the host's console window was closed: the host stops as /quit stops it");
                stopping = "its console window was closed (its X, or Ctrl+C in it)";
                running = false;
            }
            // The turn's end: 5 ms before the next (a sleep here until then; the host's wait).
            return RunStep.After(host.Now() + ServerWaitNs);
        }
    }

    /// <summary>
    /// `/talk &lt;slot&gt; &lt;file&gt; [conversation]` (ours, for trying the network conversations without a
    /// chronicle's script to start one): the conversation started for that slot's character as a script's
    /// `ExecuteConversation` would, with its callbacks said to the console. Answers true when it was
    /// this command (the host keeps running).
    /// </summary>
    static bool Talk(GameServer server, Simulation sim, string line)
    {
        var words = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3 || !words[0].Equals("/talk", StringComparison.OrdinalIgnoreCase)) return false;
        if (!int.TryParse(words[1], out int slot) || slot < 0 || slot >= server.Slots.Count
            || server.Slots[slot]?.Avatar is not { Place: { } place } body || sim.Talk is not { } talk)
        {
            Console.WriteLine("  talk: no such player");
            return true;
        }
        string file = words[2], name = words.Length > 3 ? words[3] : Path.GetFileNameWithoutExtension(words[2]);
        bool began = talk.Execute(place, file, name, ConversationFlag.Callbacks, body.Guid, place.Milliseconds, wire: c =>
        {
            c.Reached += (id, ev, step, ms, who) => Console.WriteLine($"  talk: convreached({id}, {ev}, {step}, {ms}, {who})");
            c.Chosen += (id, ev, n, ms, who) => Console.WriteLine($"  talk: convchosen({id}, {ev}, {n}, {ms}, {who})");
            c.Ended += (id, aborted, code) => Console.WriteLine($"  talk: convended({id}, {aborted}, {code})");
        });
        Console.WriteLine($"  talk: {name} for {server.Slots[slot]!.Name} — {(began ? "started" : "refused")}");
        return true;
    }

    /// <summary>The host's console: chat, and a few of the Storyteller's controls in words.</summary>
    static bool Command(GameServer server, string line)
    {
        line = line.Trim();
        if (line.Length == 0) return true;
        switch (line.Split(' ')[0].ToLowerInvariant())
        {
            case "/quit": return false;
            case "/bundle": Console.WriteLine(Engine.Net.LogBundle.MakeHere(server.Identity)); return true;   // the tester's log bundle (item 6)
            case "/pause": server.SetPaused(!server.Paused); return true;
            case "/loadscreen": server.LoadScreen(!line.EndsWith(" off")); return true;   // the players' load screen by hand (a diagnostic)
            case "/players":
                foreach (var p in server.Players)
                    Console.WriteLine($"  {p.Slot}: {p.Name} ({p.Character.Template}){(p.Avatar is { } a ? $" thing {a.Guid} at {a.Position.X:0},{a.Position.Y:0},{a.Position.Z:0}, health {a.Actor?.Health:0}" : "")}" +
                                      $"{(p.Link is { } l ? $", {l.Remote}, {l.RoundTripMs:0} ms" : " (host)")}");
                return true;
            case "/jump":
                Console.WriteLine(NetPlaces.Jump(server, line.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
                return true;
            case "/kick" when int.TryParse(line.Split(' ').ElementAtOrDefault(1), out var slot):
                server.Kick(slot);
                return true;
            // `/text <slot> <document>` (ours): the read pane opened on that player's machine, as a
            // script's `ExecuteText(player, …)` does (message 0x26).
            case "/text" when int.TryParse(line.Split(' ').ElementAtOrDefault(1), out var slot)
                              && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(2) is { } document:
                if (server.Slots.ElementAtOrDefault(slot)?.Avatar is { } reader)
                    Console.WriteLine($"  text: {document} for {server.Slots[slot]!.Name} — {server.Texts.Text(0, reader.Guid, 0, document)}");
                else Console.WriteLine("  text: no such player");
                return true;
            // `/advance <slot> [experience]` (ours): that much experience given and the
            // advancement screen opened on the player's machine, as a script's `Advance(player)` does.
            case "/advance" when int.TryParse(line.Split(' ').ElementAtOrDefault(1), out var slot):
                if (server.Slots.ElementAtOrDefault(slot)?.Avatar is { Actor: not null } who)
                {
                    if (int.TryParse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(2), out var xp) && xp > 0)
                        Experience.Award(who, xp);
                    Console.WriteLine($"  advance: {server.Slots[slot]!.Name} — {(server.Texts.Advance(who.Guid) ? "sent" : "not a player's")}");
                }
                else Console.WriteLine("  advance: no such player");
                return true;
            // `/shop <slot> <file.nbs> [cash]` (ours): a shop built on the character nearest that player
            // and opened on their machine, as a keeper's script's `AddBuySell` and `ExecuteBuySell`
            // would (message 0x42) — at the shipped scripts' 1.25 and 0.8, taking everything
            // — with that much money given first.
            case "/shop" when int.TryParse(line.Split(' ').ElementAtOrDefault(1), out var slot)
                              && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(2) is { } shopFile:
                if (server.Slots.ElementAtOrDefault(slot)?.Avatar is { Actor: not null } shopper && server.Place is { Session: { } game } here)
                {
                    if (int.TryParse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(3), out var cash) && cash > 0)
                        game.AddCash(shopper, cash);
                    // Nobody's character first; in a place with none (the inn), another player's.
                    var keeper = here.Things.Where(t => t.IsActor && t.InWorld && !ReferenceEquals(t, shopper))
                                     .OrderBy(t => server.Players.Any(p => ReferenceEquals(p.Avatar, t)))
                                     .ThenBy(t => Vector3.Distance(t.Position, shopper.Position)).FirstOrDefault();
                    if (keeper is null || !game.Shops.Add(here, shopFile, keeper, "SMITHY", 0, 1.25f, 0.8f, 0x1ff))
                        Console.WriteLine($"  shop: no keeper, or no stores/{shopFile}");
                    else
                        Console.WriteLine($"  shop: {shopFile} at {keeper.Template} for {server.Slots[slot]!.Name} ({Shops.CashOf(shopper)} to spend) — "
                                        + server.Shops.Execute(keeper, shopper.Guid, Shops.WantFeedback, null));
                }
                else Console.WriteLine("  shop: no such player");
                return true;
        }
        server.Say(line);
        return true;
    }

    /// <summary>What the host did for the run's console, in the game's log (the host has said it in its own).</summary>
    internal static void Said(string? line)
    {
        if (line is not null) Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(Vitomir.Engine.Diagnostics.Diag.For("Vitomir.Net"), "{Line}", line);
    }

    static System.Collections.Concurrent.ConcurrentQueue<string> StartReader()
    {
        var q = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var t = new Thread(() =>
        {
            // A console window closed under it ends the reading, not the program.
            try { while (Console.ReadLine() is { } l) q.Enqueue(l); }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
        }) { IsBackground = true, Name = "host console" };
        t.Start();
        return q;
    }

    // ---- a player that plays itself -------------------------------------------------------------

    /// <summary>
    /// `--net-bot`: joins, loads the place with no scripts and no AI (the host has those), and every
    /// few seconds wanders somewhere, goes for anyone hostile it can see, and says something — through
    /// the same order functions a click calls, so every order goes over the wire. A turn a frame, then
    /// the host's 15 ms wait.
    /// </summary>
    sealed class BotRun : ClassicRun
    {
        GameClient client = null!;
        Location? place;
        Simulation? sim;
        Conversations talk = null!;
        Random rng = null!;
        string name = "";
        double settle, last, nextMove = 3, nextSay = 8;
        ulong startNs;
        static readonly string[] lines = ["Good evening.", "%2", ":bow", "Anyone about?", "!Over here!", ":wave"];

        internal BotRun(IClassicHost host, RunStart start, FrameInput input) : base(host, start, input) { }

        internal override int? Begin(ulong now)
        {
            Said(host.TextConsole(ownWindow: false));                             // the terminal it was started from, if any
            var address = Connectors.Current.Find(NetRuns.Arg(args, "--join") ?? $"127.0.0.1:{NetPeer.DefaultPort}");
            if (address is null) { Console.Error.WriteLine("net: --join wants host[:port]"); return 2; }
            name = NetRuns.Arg(args, "--name") ?? $"Bot{Environment.ProcessId % 100}";
            string template = NetRuns.Arg(args, "--player-template") ?? "MP_Erik";
            var strings = new Engine.Ui.Strings(files);
            var joining = new NetCharacter { Name = name, Template = template };
            if (NetRuns.Arg(args, "--crypt") is { } crypt && new Crypt(crypt).All().FirstOrDefault(c => NetJoin.SameName(c.Name, name)) is { } kept)
                (joining, template) = (kept, kept.Template);                           // a kept character, whole
            client = new GameClient(new NetPeer(0), address, joining)
            {
                Find = strings.Find,
                Identity = NetIdentity.Of(files),
            };
            client.Log += Console.WriteLine;
            client.Posted += (text, _) => Console.WriteLine($"  message: {text}");
            client.Chatted += (text, _, _) => Console.WriteLine($"  chat: {text}");
            // The conversations the host sends it: said to the console, and the first reply taken.
            talk = new Conversations();
            client.Scenes.Talk = talk;
            client.Scenes.Subtitled += text => Console.WriteLine($"  overheard: {text}");
            client.Scenes.Conversing += c =>
            {
                Console.WriteLine($"  talk: {c.Talk.Name} begins");
                c.Spoke += line => Console.WriteLine($"  talk: {line}");
                c.Closed += () => Console.WriteLine($"  talk: {c.Talk.Name} ended, code {c.ReturnCode}");
            };
            client.Started += () =>
            {
                var s = client.Settings!;
                Console.WriteLine($"net: loading {s.Level}{(s.Scene.Length > 0 ? "/" + s.Scene : "")}");
                place = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: template, entrance: s.Entrance);
                sim = new Simulation(place) { Talk = talk };
                client.Enter(place, place.Player?.Thing);
            };
            Console.WriteLine($"net: {name} ({template}) joining {address}");
            settle = double.TryParse(NetRuns.Arg(args, "--settle"), CultureInfo.InvariantCulture, out var st) ? st : double.PositiveInfinity;
            rng = new Random(Environment.ProcessId);
            // The host's clock from here, as the run's own stopwatch started here until then.
            startNs = host.Now();
            return null;
        }

        internal override RunStep Step(ulong at)
        {
            double now = at > startNs ? (at - startNs) / 1e9 : 0;
            if (!(now < settle && client.State != ClientState.Gone))
            {
                Console.WriteLine($"net: {name} stops — {client.State}{(client.Reason is { } why ? ", " + why : "")}");
                client.Dispose();
                return RunStep.Done(0);
            }
            float dt = (float)Math.Min(now - last, 0.1);
            last = now;
            client.Poll(dt);
            sim?.Step(dt);
            if (talk.Current is { Waiting: true, Picked: false, Offered.Count: > 0 } asked && place is not null)
                asked.Pick(0, place.Milliseconds);
            if (client.State == ClientState.Playing && place?.Player is { } me && client.Avatar is { } body)
            {
                if (now >= nextMove)
                {
                    nextMove = now + 3 + rng.NextDouble() * 4;
                    if (Hostile(place, body) is { } foe && body.Actor?.Weapon is { } weapon)
                    {
                        var r = Actions.Attack(body, foe, weapon, 0, (int)(now * 1000));
                        Console.WriteLine($"  bot: attacking {Engine.Diagnostics.Watch.Name(foe)} — {r}");
                    }
                    else if (Wander(place, body.Position, rng) is { } spot)
                    {
                        me.WalkTo(spot, place.Walk!.Find(spot, 256f), run: rng.Next(3) == 0);
                        Console.WriteLine($"  bot: walking to {spot.X:0},{spot.Y:0},{spot.Z:0}; now at {body.Position.X:0},{body.Position.Y:0}");
                    }
                }
                if (now >= nextSay)
                {
                    nextSay = now + 15 + rng.NextDouble() * 15;
                    client.Say(lines[rng.Next(lines.Length)]);
                }
            }
            // The turn's end: 15 ms before the next (a sleep here until then; the host's wait).
            return RunStep.After(host.Now() + BotWaitNs);
        }
    }

    /// <summary>The nearest living thing within 400 units that is fair game (not dead, not to be talked to, not neutral).</summary>
    static WorldThing? Hostile(Location place, WorldThing me)
    {
        const uint spare = Actor.Flag.Dead | Actor.Flag.TalkTo | Actor.Flag.Neutral;
        return place.Things
            .Where(t => !ReferenceEquals(t, me) && t.Type == ThingType.Actor && t.Actor is { } a && (a.Flags & spare) == 0
                        && Vector3.Distance(t.Position, me.Position) < 400f)
            .OrderBy(t => Vector3.Distance(t.Position, me.Position)).FirstOrDefault();
    }

    /// <summary>A floor point a few hundred units away in a random direction.</summary>
    static Vector3? Wander(Location place, Vector3 from, Random rng)
    {
        if (place.Walk is not { } map) return null;
        for (int tries = 0; tries < 12; tries++)
        {
            float a = (float)(rng.NextDouble() * Math.PI * 2), d = 150f + (float)rng.NextDouble() * 300f;
            var p = from + new Vector3(MathF.Cos(a) * d, MathF.Sin(a) * d, 0);
            int node = map.Find(p, 256f);
            if (map.IsGround(node) && map.Nodes[node].Contains(new Vector2(p.X, p.Y)))
                return p with { Z = map.Nodes[node].Height(p.X, p.Y) };
        }
        return null;
    }
}
