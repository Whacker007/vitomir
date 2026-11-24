// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game run: the session's state, and the start of its place.
using System.Globalization;
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Scene;
using Vitomir.Classic;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Engine.Video;
using Vitomir.Formats;
using Vitomir.Platform.Audio;
using Vitomir.Scripting;
using Vitomir;
using Vitomir.Platform.Modules;

namespace Vitomir.Classic;

/// <summary>
/// **The game run**, 1.1's frame controller: the session's state as
/// fields, the start of its place, its harness's stills and its frames. Its parts are by what they are for.
/// </summary>
unsafe sealed partial class ClassicGame : ClassicRun
{
    // The chronicle a new game starts (1.1 sets `prague.nsc` for a default command line).
    const string NewGameChronicle = "prague";
    global::Vitomir.DriveSnags driveSnags = null!;
    global::Vitomir.Engine.World.Qol qol = null!;
    string levelName = null!;
    float? wasPulled;
    global::Vitomir.Engine.World.ChronicleSet chronicles = null!;
    global::Vitomir.Engine.Save.SaveGame? loaded;
    global::Vitomir.Engine.Save.LocationSave? loadedPlace;
    global::Vitomir.Engine.World.Chronicle? loadedChronicle;
    string? freshScene;
    bool playing;
    bool autosave;
    global::Vitomir.Engine.World.ChronicleWhere? newStart;
    global::Vitomir.NetPlay? net;
    global::Vitomir.Engine.World.ChronicleWhere? owner;
    string? playerTemplate;
    global::Vitomir.Engine.World.Location location = null!;
    global::Vitomir.Engine.Rpg.EffectDefinitions effects = null!;
    global::Vitomir.Engine.Rpg.DisciplineDefinitions powers = null!;
    global::Vitomir.Engine.Save.ScriptRestore? loadRestore;
    global::Vitomir.Formats.NpcFile? startingKit;
    global::System.Collections.Generic.List<global::Vitomir.Engine.World.WorldThing> companionsPlaced = null!;
    global::Vitomir.Engine.Ai.Minds? minds;
    global::Vitomir.Engine.Audio.GameAudio audio = null!;
    global::Vitomir.Engine.Session session = null!;
    global::Vitomir.Engine.Scene.MaterialCache materials = null!;
    global::Vitomir.Engine.Scene.SceneMesh sceneMesh = null!;
    global::Vitomir.Engine.World.Sky? sky;
    global::Vitomir.Engine.World.FreeCamera cam = null!;
    global::Vitomir.Scripting.CodexScenes? scenes;
    bool windowed;
    global::Vitomir.SoundTape? tape;
    global::Vitomir.Platform.Audio.AudioOutput speakers = null!;
    global::Vitomir.Platform.Render.Classic.IClassicRenderer gpu = null!;
    global::Vitomir.Classic.PlaceFrame frame = null!;
    global::Vitomir.Classic.Overlay overlay = null!;
    global::Vitomir.Platform.Render.Classic.IClassicOverlay overlayOut = null!;
    global::Vitomir.Classic.WatchLayer watch = null!;
    global::Vitomir.Engine.Ui.UiCanvas ui = null!;
    global::Vitomir.Platform.Render.Classic.IClassicInterface uiOut = null!;
    global::Vitomir.Engine.Ui.StatusBarPass hud = null!;
    global::Vitomir.Engine.Ui.ConversationPass talkPass = null!;
    int standaloneUntil;
    global::Vitomir.Formats.NvoStrings words = null!;
    global::Vitomir.Engine.Rpg.Conversations talk = null!;
    global::Vitomir.Engine.Ui.CharacterPass charPass = null!;
    global::Vitomir.Engine.Ui.Strings strings = null!;
    global::Vitomir.Engine.Ui.FrontEnd menus = null!;
    global::Vitomir.Engine.Ui.MenuPass menuPass = null!;
    global::Vitomir.Engine.Ui.QuestPass questPass = null!;
    global::Vitomir.Engine.Ui.BuySellPass buyPass = null!;
    bool shopDrag;
    global::Vitomir.Engine.Ui.DisciplinePass discPass = null!;
    global::Vitomir.Engine.Rpg.Advancement? advancement;
    bool barAsked;
    global::Vitomir.Engine.Ui.AdvancementBarPass advBarPass = null!;
    (global::Vitomir.Engine.Ui.DisciplinePane.Part Part, int Index) discPress;
    int barPress;
    int raisePress;
    global::Vitomir.Engine.Ui.DisciplineDrag discDrag = null!;
    /// <summary>Where the left button last went down, in window pixels: a shop's drag of an item starts from it (ours).</summary>
    (float X, float Y) leftPressAt;
    bool readOkPressed;
    bool advanceFinished;
    global::System.Action<int>? restart;
    global::Vitomir.Engine.Ui.InventoryPass invPass = null!;
    global::Vitomir.Engine.Ui.InventoryPass vaultPass = null!;
    global::Vitomir.Engine.Ui.Documents documents = null!;
    global::Vitomir.Engine.Ui.ReadPass readPass = null!;
    global::Vitomir.Engine.Ui.InventoryHands? hands;
    global::Vitomir.Engine.Ui.CursorState cursor = null!;
    global::Vitomir.Engine.Ui.RolloverPass rolloverPass = null!;
    global::Vitomir.Engine.Ui.CursorPass cursorPass = null!;
    global::Vitomir.Engine.World.WorldThing? hovering;
    global::System.Numerics.Vector3? pointerGround;
    global::Vitomir.Engine.Ui.HoverGlow glow = null!;
    bool swallowed;
    bool pointerLog;
    (string What, float X, float Y, string Whose) pointerSaid;
    global::Vitomir.Engine.Ui.HeadsPass headsPass = null!;
    global::Vitomir.Platform.Render.Classic.IClassicEffects effectsPass = null!;
    global::Vitomir.Engine.Ui.Captions captions = null!;
    global::Vitomir.Engine.Ui.CaptionPass captionPass = null!;
    global::Vitomir.Engine.Ui.EndTextHold endText = null!;
    ulong last;
    float filmClock;
    double uiClock;
    global::Vitomir.Engine.Ui.EffectIconsPass effectIconsPass = null!;
    global::Vitomir.Engine.Ui.FeedBar feedBar = null!;
    global::Vitomir.Engine.Ui.NetPass netPass = null!;
    string[] shopHides = null!;
    global::System.Func<bool> Picking = null!;
    global::Vitomir.Platform.Render.Classic.IClassicRing markerRenderer = null!;
    bool gameCamera;
    global::Vitomir.Engine.World.WorldCamera worldCam = null!;
    bool hudWanted;
    bool wasCutscene;
    global::Vitomir.Engine.Ui.StatusBarHooks barHooks = null!;
    bool cursorWanted;
    global::Vitomir.Engine.World.CameraMode cameraWas;
    global::Vitomir.Classic.FilmScreen filmScreen = null!;
    global::Vitomir.Engine.World.Campaign? campaign;
    int autoAnswer;
    global::Vitomir.HarnessOrders.Casts? castOrder;
    global::Vitomir.HarnessOrders.Use? useOrder;
    global::Vitomir.HarnessJourney? journey;
    global::Vitomir.Engine.World.Player? player;
    bool follow;
    bool consoleKey;
    ulong fpsStart;
    bool running;
    bool looking;
    float lookAnchorX;
    float lookAnchorY;
    int swallowMotion;
    bool rightTurns;
    global::Vitomir.Engine.Ui.SwivelHold swivelHold = null!;
    global::Vitomir.Engine.Ui.CentreLook centreLook = null!;
    global::Vitomir.Engine.Ui.CentreLook.Point? lockAt;
    float frameMouseX;
    float frameMouseY;
    float pointerX;
    float pointerY;
    int winW;
    int winH;
    bool atEdge;
    bool pointerOut;
    global::Vitomir.Engine.Ui.PointerHold pointerHold = null!;
    (bool WinKey, bool AltTab)? systemKeys;
    int frames;
    int fpsFrames;
    global::Vitomir.Platform.Render.Classic.IScriptedInput? scripted;
    float harnessStep;
    global::Vitomir.ConsoleRun? consoleRun;
    global::Vitomir.Engine.Ui.WindowActive windowActive = null!;
    bool interactive;
    global::Vitomir.Engine.Diagnostics.SlowFrames slowFrames = null!;
    int texturesBefore;
    int rebuildsBefore;
    long readsBefore;
    long readTicksBefore;
    long servedBefore;
    global::Vitomir.FrameLogHost? frameLog;
    global::Vitomir.Engine.Diagnostics.FrameTimes? frameTimes;
    // `--frame-dump` with a window (a replay above all) and its `--frame-dump-log`, and the wheel's notches and the
    // buttons pressed since the last picture (ours, a harness: FrameDump, FrameDumpLog).
    global::Vitomir.FrameDump? playDump;
    global::Vitomir.Engine.Diagnostics.FrameDumpLog? playDumpLog;
    int dumpWheel;
    uint dumpClicks;

    /// <summary>The tools a loaded place hands over to (vitomir-tools' reports), or none.</summary>
    readonly IRunTools? tools;

    internal ClassicGame(IClassicHost host, RunStart start, FrameInput input, IRunTools? tools = null) : base(host, start, input)
    {
        this.tools = tools;
        driveSnags = start.driveSnags;
        qol = start.qol;
        levelName = start.levelName;
        wasPulled = start.wasPulled;
        chronicles = start.chronicles;
        loaded = start.loaded;
        loadedPlace = start.loadedPlace;
        loadedChronicle = start.loadedChronicle;
        freshScene = start.freshScene;
    }

    /// <summary>
    /// The game's start: its place built, its harness's stills (an exit code: the run is over), then its loop begun (null: its frames
    /// follow, <see cref="Step"/>; until then the same method ran them to the end).
    /// </summary>
    internal override int? Begin(ulong now)
    {
        frameNs = now;
        // The game's frames draw a world behind the sheet: a sheet shorter than the picture sits at its bottom, the status
        // bar on the bottom edge (ours, UiLayout.Seat). Each frame says again (SeatFrame: centred while a screen
        // that is the whole sheet is up), and a film's frames while it holds the screen (FilmScreen).
        Vitomir.Engine.Ui.UiLayout.Seat = Vitomir.Engine.Ui.SheetSeat.Bottom;
        if (StartWorld() is { } startWorldExit) return startWorldExit;
        // The reports a run can be once its place is loaded (vitomir-tools', LevelReports: IRunTools.PlaceLoaded).
        if (tools?.PlaceLoaded(args, location, scenes, files, chronicles, levelName) is { } reportExit) return reportExit;
        StartInterface();
        StartPlayer();
        StartCamera();
        StartStory();
        if (StartHarness() is { } startHarnessExit) return startHarnessExit;
        StartShops();
        if (Stills() is { } stillsExit) return stillsExit;
        return PlayBegin();
    }

    int? StartWorld()
    {

        // **The game itself**: a run asked for nothing but settings is the front end — the title
        // screen and the main menu, which is how 1.1 starts.

        // **A new game** (the single-player menu's New, request 8): the campaign's first
        // chronicle — `prague.nsc` — from its first location at entrance 0, with the
        // chronicle's own player. A game the menus start (`--play`, which a new game implies) has
        // the status bar up and writes the autosave on every location entered.
        bool newGame = Flag("--new-game") && loaded is null;
        playing = newGame || Flag("--play");
        autosave = playing || Flag("--autosave");
        newStart = null;
        if (newGame)
        {
            if (chronicles[NewGameChronicle] is not { Count: > 0 } campaignStart || campaignStart.At(0) is not { } firstPlace)
            {
                Console.Error.WriteLine($"new game: the install has no {NewGameChronicle} chronicle to start");
                return 2;
            }
            newStart = new ChronicleWhere(campaignStart, 0);
            levelName = firstPlace.LevelName;
            Console.WriteLine($"new game: {campaignStart.Name}, {firstPlace.Name} ({levelName}, {firstPlace.SceneName}) at entrance 0");
        }

        // A network game says where it is played and who you are (`NetPlay`).
        net = NetPlay.Begin(args, files, chronicles);
        if (net is { Failed: { } netFailed }) { Console.Error.WriteLine($"net: {netFailed}"); return 2; }
        // A save hosted as a network game gives slot 0 the character chosen, not its party (NetSaves.ForHost).
        if (net is { IsHost: true } && loaded is not null) loaded = Vitomir.Engine.Net.NetSaves.ForHost(loaded);
        if (net is not null)
        {
            levelName = net.Level;
            // The host has the scripts and the minds; a player's machine shows what it is told, and runs
            // only the effects' scripts, as they arrive (CodexScenes.PlayersMachine).
            args = [.. args, "--hud", "--scene", net.Scene ?? levelName, "--entrance", $"{net.Entrance}",
                    "--player-template", net.Template, "--player",
                    .. (net.Settings.Chronicle.Length > 0 ? new[] { "--chronicle", net.Settings.Chronicle } : []),
                    .. (net.IsClient ? new[] { "--no-ai", "--players-machine" } : [])];
        }

        // The chronicle decides which scene a location starts in; --scene overrides it, --chronicle
        // picks which chronicle owns the level when more than one names it.
        owner = loadedChronicle is { Current: >= 0 } fromSave ? new ChronicleWhere(fromSave, fromSave.Current)
            : newStart is { } startHere ? startHere
            : Arg("--chronicle") is { } forced && chronicles[forced] is { } pick
            ? (pick.Find(levelName) is var byName && byName >= 0 ? new ChronicleWhere(pick, byName)
               : Enumerable.Range(0, pick.Count).Where(i => string.Equals(pick.At(i)!.LevelName, levelName, StringComparison.OrdinalIgnoreCase))
                           .Select(i => (ChronicleWhere?)new ChronicleWhere(pick, i)).FirstOrDefault())
            : chronicles.ForLevel(levelName);
        string? sceneArg = loadedPlace is not null ? (loadedPlace.Scene.Length > 0 ? loadedPlace.Scene : null)
                         : loaded is not null ? freshScene
                         : Arg("--scene") ?? owner?.Entry?.SceneName;

        // The chronicle's metafile says who the player is ("PLAYER Christof"); --player puts them in.
        // Only Prague.nmf names a PLAYER — it is the campaign's start, and the other four chronicles
        // are chained from it, so you arrive as whoever you already were. Their metafiles carry the
        // key with an empty value, which `??` does not catch, so ask for emptiness rather than null.
        playerTemplate = loaded?.Party.Members.FirstOrDefault()?.Template;
        if (playerTemplate is null && (Flag("--player") || newGame))
        {
            playerTemplate = Arg("--player-template");
            if (string.IsNullOrEmpty(playerTemplate)) playerTemplate = owner?.Chronicle.Meta?.Player;
            if (string.IsNullOrEmpty(playerTemplate)) playerTemplate = "Christof";
        }
        // `--entrance N` opens the level at one of its entrances rather than at the first start in the
        // file — the same choice an exit makes when you arrive through it, and the only way to look at
        // an arrival without travelling to it.
        int startEntrance = loadedPlace?.Entrance ?? (newGame ? 0 : int.TryParse(Arg("--entrance"), out var atEntrance) ? atEntrance : -1);
        location = Location.Load(files, levelName, things: !Flag("--no-things"), sky: !Flag("--no-sky"),
                                     scene: sceneArg, player: playerTemplate, entrance: startEntrance,
                                     guids: ThingGuids.For(net?.IsHost == true,                 // a host's bit on every number
                                            loaded is not null ? SaveGuids.ForLoad(loaded)
                                          : newStart is { } started ? Preloads.NewGame(started.Chronicle, files) : null));

        Vitomir.Engine.Levels.Cells.CellSceneReport.Write(location, Arg("--cell-scene-report"));   // a cell level's 1.1 scene, as placed (ours)

        // What every effect and every discipline is (`misc/effects.ned`, `misc/game.ndd` and `game.ndg`).
        // The engine loads them at start-up; a load needs them before the place is woken.
        effects = new Vitomir.Engine.Rpg.EffectDefinitions(files);
        powers = new Vitomir.Engine.Rpg.DisciplineDefinitions(files);

        // The save's place and party, put back before anything wakes (`SaveApply`, 1.1's order).
        loadRestore = null;
        WorldThing? loadSteered = null;
        if (loaded is not null)
        {
            try
            {
                loadRestore = loadedPlace is not null ? SaveApply.Place(location, loadedPlace, effects, powers) : new ScriptRestore();
                loadSteered = SaveApply.Party(location, loaded.Party, loadRestore, effects, powers);
            }
            catch (Exception e) when (Flag("--play") && Arg("--headless") is null)
            {
                // A save that reads but will not go back together: to the main menu with the reason (ours).
                Console.Error.WriteLine($"load: the save could not be put back: {e}");
                host.MenuNotice = $"The save could not be loaded.\n{e.Message}";
                host.NextRun = [.. RunSettings.Settings(args), "--menu"];
                return 0;
            }
            SaveGuids.Loaded(location, loaded);                                  // the game's GUID counter as saved
            location.Milliseconds = (int)loaded.Header.ClockMs;
            Console.WriteLine($"load: {location.Name}{(sceneArg is null ? "" : "/" + sceneArg)}, {location.Things.Count} things in the world");
        }

        // **The template is the shell; the `.NPC` is the character.** A player template's
        // `ACTOR_RPGSTATS` names a file in `misc/` that carries the real stats, their clamp bounds, the
        // starting disciplines and the starting inventory — and without it `--player` builds a Christof
        // with 20 in everything who is a vampire in chapter one, which is what it did until then.
        // (Note, corrected: the shipped scripts call `SetActorType` twice, not once —
        // `CNVT_3_1` makes Christof human and `OLDT_7_4` puts him back at the Embrace.)
        startingKit = null;
        if ((loaded is null || loaded.Party.Members.Count == 0) && location.Player?.Thing.Actor is { RpgStats.Length: > 0 } sheet)
        {
            string npcPath = NpcFile.PathFor(sheet.RpgStats);
            if (!files.TryRead(npcPath, out var npcData))
                Console.WriteLine($"player: no {npcPath}; {sheet.Name} keeps the template's numbers");
            else
            {
                var npc = NpcFile.Read(npcData, npcPath);
                foreach (var warning in npc.Warnings) Console.WriteLine($"  npc: {warning}");
                int taken = Vitomir.Engine.World.CharacterSheets.Read(sheet, npc);     // the reset, then the sheet
                startingKit = npc;
                Console.WriteLine($"player: {npc.Name} from {npcPath} — {taken} stats with their bounds"
                                + $", {npc.Cash} gold"
                                + (npc.Disciplines.Count > 0 ? $", {npc.Disciplines.Count} disciplines" : "")
                                + (npc.Items.Count > 0 ? $", {npc.Items.Count} items" : "")
                                );
            }
        }
        // **--actor-type is a harness knob, and it exists because the shipped game uses one too.**
        // `darkAges.not`'s Christof is `ACTOR_TYPE 0 VAMPIRE` — the *end* state — and the first scene of
        // the game, `CNVT_3_1`, calls `SetActorType(ACTOR_TYPE_HUMAN)` on him under the comment
        // "make him a human"; `OLDT_7_4` makes him a vampire again at the Embrace, and those are the
        // only two `SetActorType` calls in the 465 shipped scripts. So a fresh spawn with no script
        // history and no save is a vampire, correctly, and there is no data anywhere that says
        // otherwise. 1.1's saves carry the answer in the actor's type: HUMAN at Unorna's Shoppe and
        // Prague's Old Town, VAMPIRE at the University. A Vitomir save carries it too, and so does a
        // 1.1 save brought in (`SaveImport`); for a run with neither, this flag lets a play-test stand
        // where the game would have stood.
        if (Arg("--actor-type") is { Length: > 0 } wantType)
        {
            if (!Enum.TryParse<ActorType>(wantType, ignoreCase: true, out var asType))
                Console.WriteLine($"player: --actor-type {wantType} is not one of "
                                + string.Join(", ", Enum.GetNames<ActorType>()).ToLowerInvariant());
            else if (location.Player?.Thing.Actor is { } who)
            {
                who.Type = asType;
                Console.WriteLine($"player: {who.Name} set to {asType} (the shipped game does this from "
                                + "CNVT_3_1, the convent's opening scene)");
            }
        }

        // The people here think and walk unless told not to.
        // The `.NPC`'s starting items, now that both the templates and the effect definitions exist —
        // an equipped item's soak is an effect, so giving one before `effects` would hand out a weapon
        // that protects nobody. Read earlier and parked until the inventory pane could show them.
        if (startingKit is { Items.Count: > 0 } kit && location.Player?.Thing is { } kitOwner
            && location.Templates is { } templateSet)
        {
            var given = Vitomir.Engine.Rpg.StartingKit.Give(kitOwner, kit, templateSet, effects);
            Console.WriteLine("player: " + string.Join(", ", given.Select(g =>
                $"{g.Template} {(g.Placed ? Vitomir.Engine.Rpg.Slot.Name(g.Slot) : "nowhere")}"
                + (g.Note is null ? "" : $" ({g.Note})"))));
        }
        // **--buff** makes the player character strong enough to walk past a fight. It is a harness
        // knob and nothing else — no part of the game reads it, nothing in the original resembles it,
        // and it exists because testing a lever two thousand units into a monastery should not require
        // winning the monastery first: that lever is too far in to reach for a test
        // without artificially strengthening Christof.
        //
        // It raises the **ceiling** before the value, because a base-stat write is clamped into
        // `StatMin`/`StatMax` and the player's bounds are authored in the `.NPC` file — writing 100
        // into a stat the character file caps at 10 would simply store 10.
        if (Flag("--buff") && location.Player?.Thing.Actor is { } strong)
        {
            float want = float.TryParse(Arg("--buff"), out var howMuch) && howMuch > 0 ? howMuch : 100f;
            for (int i = 0; i < Vitomir.Engine.World.Actor.Stats; i++)
            {
                // Not the pools: blood is clamped a second time against BloodPool, and a Generation of
                // 100 is not "strong", it is a different character. Those two keep what they were —
                // and so does **frenzy**, which is a meter that fills and not a strength: 100 of it is
                // a character on the edge of frenzying, not a strong one.
                if (i is (int)ActorStat.Generation or (int)ActorStat.Humanity or (int)ActorStat.Frenzy) continue;
                if (strong.StatMax[i] != Vitomir.Engine.World.Actor.NoStatLimit)
                    strong.StatMax[i] = MathF.Max(strong.StatMax[i], want);
                strong.Stat[i] = strong.SetBaseStat(i, want);
            }
            strong.MaxHealth = MathF.Max(strong.MaxHealth, want * 3f);
            strong.Health = strong.MaxHealth;
            // The pool before the blood, because a blood write is clamped against the pool
            // — the same order the character file's own comment insists on.
            strong.Stat[(int)ActorStat.BloodPool] = strong.SetBaseStat((int)ActorStat.BloodPool, want * 3f);
            strong.Stat[(int)ActorStat.Blood] = strong.SetBaseStat((int)ActorStat.Blood, want * 3f);
            Console.WriteLine($"buff: {strong.Name} at {want:0} in every stat, " +
                              $"{strong.Health:0}/{strong.MaxHealth:0} health, " +
                              $"{strong.Stat[(int)ActorStat.Blood]:0}/{strong.Stat[(int)ActorStat.BloodPool]:0} blood " +
                              "— a harness knob, not a Fix");
        }
        // --give puts one more thing in the player's hands, which is the only way to try a piece of
        // gear the character file does not start with. It goes through the same Equip the starting kit
        // and the inventory pane use, so what it proves is what the game would do.
        if (Arg("--give") is { } giving && !giving.StartsWith("--") && location.Player?.Thing is { Actor: { } taker } recipient
            && location.Templates is { } gifts)
        {
            foreach (var name in giving.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!gifts.Has(name)) { Console.WriteLine($"give: no template called {name}"); continue; }
                if ((location.Make(name)?.Item ?? Vitomir.Engine.Rpg.Item.From(name, gifts.Resolve(name))) is not { } gift)
                { Console.WriteLine($"give: {name} is not an item"); continue; }
                taker.Inventory.Add(gift);
                var put = Vitomir.Engine.Rpg.Equipment.Equip(recipient, gift, Vitomir.Engine.Rpg.Slot.None, effects, 0);
                Console.WriteLine($"give: {name} {(put == Vitomir.Engine.Rpg.EquipResult.Equipped
                    ? Vitomir.Engine.Rpg.Slot.Name(gift.Location) : put.ToString())}");
            }
        }
        // `--companion <template>` brings a party member along the way the campaign would have. The
        // monastery opens on a cutscene between Christof and Wilhem, and no monastery scene places
        // Wilhem, because in the game he walks in with you; without him the conversation plays with
        // one speaker and the camera frames one. Placed before the minds wake, so each gets a mind and
        // starts in the party's own AI mode, as a placed companion does. A harness knob, not the game.
        companionsPlaced = new List<WorldThing>();
        if (Arg("--companion") is { } companions && !companions.StartsWith("--") && location.Player?.Thing is { } lead)
        {
            int n = 0;
            foreach (var name in companions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var beside = lead.Position + new Vector3(96f * ++n, 0f, 0f);
                if (location.Place(name, beside, lead.Orientation, lead.Sector) is not { Actor: { } joined } comp)
                {
                    Console.WriteLine($"companion: no character template called {name}");
                    continue;
                }
                joined.Flags |= Vitomir.Engine.World.Actor.Flag.Party;
                joined.Flags2 |= Vitomir.Engine.World.Actor.Flag2.InFormation;
                companionsPlaced.Add(comp);
                Console.WriteLine($"companion: {comp.Template} (cast \"{joined.CastId}\") joins the party");
            }
        }
        // `--free-ammo`: the console's `freeammo` from the start — nobody runs out. For a
        // test scenario with a gun in it.
        if (Flag("--free-ammo")) Vitomir.Engine.Rpg.Weapon.FreeAmmo = true;
        // `--no-cast`: the place with nobody in it but the party — every other character taken out
        // of the world (not merely undrawn, as `--no-things` leaves them: they would still think and
        // fight unseen). The doors, the furniture and the lights stay. For a test scenario's stage.
        if (Flag("--no-cast"))
        {
            int gone = 0;
            foreach (var resident in location.Things.ToArray())
                if (resident is { Type: ThingType.Actor, Actor: { } cast } && !cast.Has(Vitomir.Engine.World.Actor.Flag.Party)
                    && location.Remove(resident, drop: false)) gone++;
            Console.WriteLine($"no-cast: {gone} characters taken out of {location.Name}");
        }
        // `--spawn <template>@x,y,z[!]` (repeatable; several in one, parted by `;`): a character or a
        // thing stood at a point for a test, facing the player; with `!` it is **held** — the flag a
        // script sets to take a character over (`THING_AF_AIPAUSED`), so it stands where it is put
        // and does nothing. Placed before the minds wake, so each gets one. A harness knob,
        // not the game.
        foreach (var spawnArg in Args("--spawn").SelectMany(a => a.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            bool held = spawnArg.EndsWith('!');
            var bits = spawnArg.TrimEnd('!').Split('@');
            var xyz = bits.Length == 2 ? bits[1].Split(',') : [];
            if (xyz.Length != 3) { Console.WriteLine($"spawn: {spawnArg} is not <template>@x,y,z"); continue; }
            var at = new Vector3(F(xyz[0]), F(xyz[1]), F(xyz[2]));
            var facing = Vector3.Zero;
            if (location.Player?.Thing is { } faced)
                facing = new Vector3(0f, 0f, MathF.Atan2(faced.Position.Y - at.Y, faced.Position.X - at.X) * 180f / MathF.PI - 90f);
            long into = location.At(at + new Vector3(0f, 0f, 40f), (int)(location.Player?.Thing.Sector ?? 0));
            if (location.Place(bits[0], at, facing, into) is not { } stood) { Console.WriteLine($"spawn: no template called {bits[0]}"); continue; }
            if (held && stood.Actor is { } holdMe) holdMe.Flags |= Vitomir.Engine.World.Actor.Flag.AiPaused;
            Console.WriteLine($"spawn: {stood.Template} #{stood.Guid} at {at.X:0},{at.Y:0},{at.Z:0} in sector {stood.Sector}{(held ? ", held" : "")}");
        }
        minds = Flag("--no-ai") ? null
                  : location.Awaken(new AiClasses(files), 1, effects, powers,         // its rolls: the launch's AI stream (Chance)
                                    fromSave: loaded is not null);
        if (minds is not null && loadSteered is not null) minds.Steered = minds.Selected = loadSteered;
        // The party AI switches (folded in at start-up); a load's or a quit's teardown zeroes
        // the word and only the Controls page folds them in again (`Ai.MainFlagsWord`);
        // Vitomir folds them back in after the teardown (repair `keepcontrols`).
        if (minds is not null) minds.MainFlags = Vitomir.Engine.Ai.MainFlagsWord.AtStart(GameOptions.Current, loaded is not null);
        // `--behead <template>`: the first thing made from it loses its head at once — a
        // harness knob for looking at the headless body and the head on the floor, not the game. After the
        // minds wake, which is when the place has its effect table for `ef_beheaded`.
        if (Arg("--behead") is { } beheading && !beheading.StartsWith("--")
            && location.Things.FirstOrDefault(t => t.Template.Equals(beheading, StringComparison.OrdinalIgnoreCase)) is { } neck)
            Console.WriteLine($"behead: {beheading} " + (Vitomir.Engine.Rpg.Beheading.Behead(neck, location.Player?.Thing,
                new Vitomir.Engine.Ai.Rng(3), 0) ? $"beheaded, now drawn as {neck.Actor!.BodyModel}" : "refused"));
        // The `.NPC`'s disciplines, now that there is a table to find them in. Each line is the loader's
        // (the sheet's `discipline` line): the character's instance, found or made; the level
        // set, which opens the next of its group; and the quick slot, when the line gives one.
        if (startingKit is { Disciplines.Count: > 0 } npcSheet && location.Player?.Thing is { } learner)
        {
            int learnt = 0;
            foreach (var line in npcSheet.Disciplines)
            {
                if (powers[line.Name] is not { } def) { Console.WriteLine($"  npc: no discipline called {line.Name}"); continue; }
                if (Vitomir.Engine.Rpg.DisciplineBook.Make(learner, def) is not { } inst) continue;
                Vitomir.Engine.Rpg.DisciplineBook.SetLevel(learner, powers, line.Name, line.Level);
                if (line.Quick >= 0) inst.QuickSlot = line.Quick;
                learnt++;
            }
            Console.WriteLine($"player: {learnt} disciplines from the sheet");
        }

        // **Sound**. One mixer for the whole run, as the original has one: the
        // scene's music and ambience, every thing's foley, the voices. The install's own
        // `masquerade.ini` sets the two volumes, stored the way the game stores them. `--mute` keeps all
        // of it running — foley still draws on the game's rand(), as it would with speakers — and sends
        // nothing out.
        audio = new GameAudio(files);
        audio.Configure(GameOptions.Current.EffectsVolume, GameOptions.Current.MusicVolume);
        if (minds is not null) audio.Random = minds.Random;
        location.Audio = audio;
        // Ours: each place's sounds read ahead on a background thread as it is entered, and each
        // conversation's voices as it starts, so the game's reads find them in memory.
        if (!Flag("--no-prefetch")) audio.Prefetch = new Vitomir.Engine.Audio.SoundPrefetch(files, audio.Bank, audio.Foley);

        // **The game's own state that outlives a level**: the message log the status bar shows, the
        // quest log, and the party's purse (`Engine.Session`). One for the run; every place is handed
        // it. 1.1 clears these piecemeal on a new game or a load — the message box, the chat line, the
        // quest button's blink, a Storyteller back to the multiplayer bar; here a fresh
        // `Session` starts either the same way.
        session = new Vitomir.Engine.Session { Here = location, Chronicle = owner?.Chronicle };
        location.Session = session;
        // Quests are read from `quests/<name>.nqd` (the extension is set, whatever was asked).
        session.Quests.Read = name =>
            files.TryRead($"quests/{System.IO.Path.GetFileNameWithoutExtension(name)}.nqd", out var nqd) ? nqd : null;
        // The console hears every line of the message log, so a run that nobody watched still says
        // what the game told the player.
        session.Messages.Posted += m => Console.WriteLine($"  message: {m.Text}");
        if (loaded is not null) SaveApply.Session(session, loaded.Session, location);
        if (Arg("--gold") is { } goldArg && int.TryParse(goldArg, out var startGold)) session.Gold = startGold;
        if (minds is not null)
        {
            // --party recruits the companions the scene already placed, the way the chronicle's own
            // script would: CodexPlayer.AddToParty sets THING_AF_PARTY and nothing else, because a
            // player-type thing is already started in AiMode.Party by the mode table. Nothing is
            // spawned — this only finds characters the level put there — so it is useful in exactly
            // the four scenes that place one (see --party's own message when it finds nobody).
            if (Flag("--party"))
            {
                var recruited = new List<string>();
                // A stance belongs to a member, and the buttons set every member's — the leader's
                // included (with no player the whole array is walked). So `--party-stance` is the
                // whole party's, which is also what makes the bar's radios show it.
                var wanted = Arg("--party-stance")?.ToLowerInvariant() switch
                {
                    "attack" => PartyStance.Attack,
                    "defense" or "defence" => PartyStance.Defense,
                    _ => PartyStance.Neutral,
                };
                if (minds.Steered?.Actor is { } leader) leader.Stance = wanted;
                foreach (var mind in minds.All)
                {
                    if (ReferenceEquals(mind.Thing, minds.Steered)) continue;
                    if (mind.Thing.Type != ThingType.Player || mind.Thing.Actor is not { } who) continue;
                    // `CodexPlayer.AddToParty` does two things: `AddPlayer`, which is what
                    // sets THING_AF_PARTY, and then the formation bit — so a recruit keeps formation
                    // from the moment it joins, and the status bar's pip is lit.
                    who.Flags |= Vitomir.Engine.World.Actor.Flag.Party;
                    who.Flags2 |= Vitomir.Engine.World.Actor.Flag2.InFormation;
                    who.Stance = wanted;
                    mind.Enter(AiMode.Party);
                    // Put them beside the player, which is what the chronicle does when it adds
                    // somebody (`TeleportPartyToEntrance` exists for exactly this). Without it a recruit
                    // stays where the scene left them — and in ARC3_17_1 that is Erik tied up 1,252
                    // units from a Christof who spawns a floor above, which is 2.5 units outside even an
                    // Attack-stance fight leash. The result is a companion that acquires a target and
                    // has it dropped again every single think. `--party-there` leaves them put.
                    if (!Flag("--party-there") && minds.Steered is { } beside)
                    {
                        // A little to the side rather than inside him, so the formation has somewhere
                        // to put him; fall back to the leader's own spot if that is not floor.
                        var was = mind.Thing.Position;
                        mind.Thing.Position = beside.Position + new Vector3(96f, 0f, 0f);
                        if (!mind.Walker.Ground())
                        {
                            mind.Thing.Position = beside.Position;
                            if (!mind.Walker.Ground()) mind.Thing.Position = was;
                        }
                    }
                    recruited.Add($"{mind.Thing.Template} ({who.Stance})");
                }
                Console.WriteLine(recruited.Count > 0
                    ? $"party: recruited {string.Join(", ", recruited)}"
                    : "party: nobody here to recruit. Only four shipped scenes place a companion — "
                      + "UNIV_7_7 (Wilhem, level P1_UNIV), ARC3_17_1 (Erik, p1_arc3), "
                      + "HILL_13_1 (Serena, P1_HILL) and WAR3_36_1 (WilhemMod, N1_WAR3).");
            }

            Console.WriteLine($"ai: {minds.All.Length} minds ("
                + string.Join(", ", minds.ByMode().Select(e => $"{AiModes.Name(e.Mode)} {e.Count}"))
                + $"), {minds.Spots.All.Length} interest spots");
        }
        if (owner is { } w)
            Console.WriteLine($"chronicle: {w.Chronicle.Name}, location {w.Location} \u201c{w.Name}\u201d, scene {location.SceneFile}");
        var level = location.Level;
        var scene = location.Scene;
        materials = location.Materials;
        sceneMesh = location.Mesh;
        sky = location.Sky;
        var skyWindow = location.SkyWindow;
        if (sky?.Player is { Animation.Duration: > 0 } skyPlayer && Arg("--sky-frame") is { } skyFrame)
            skyPlayer.Frame = F(skyFrame) % skyPlayer.Animation.Duration;

        cam = location.Camera();
        if (Arg("--pos") is { } pos) { var p = pos.Split(',').Select(F).ToArray(); cam.Position = new Vector3(p[0], p[1], p[2]); }
        if (Arg("--yaw") is { } yaw) cam.Yaw = F(yaw);
        if (Arg("--pitch") is { } pitch) cam.Pitch = F(pitch);

        // The game's own Java scripts, compiled and loaded from the player's install. --no-scripts
        // leaves them out, which is what the sweep and the rendering tests do.
        scenes = null;
        if (!Flag("--no-scripts"))
        {
            try
            {
                var engine = new CodexHost { Strict = Flag("--strict-scripts") && Diag.Enabled };
                // The shipped classes (Codex.nob's) and the Codex folder's overlay; --script-sources, a developer's, compiles the sources.
                var compiler = CodexCompiler.For(files, GameData.CodexCache, Flag(CodexCompiler.SourcesSwitch));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var scriptRuntime = ScriptRuntime.Start(compiler, engine, Flag("--rebuild-scripts"));
                ScriptRandom.Once(Chance.Game!.Scripts);    // the scripts' Math.random from the launch's seed, once
                scenes = new CodexScenes(scriptRuntime) { Chronicles = chronicles, PlayersMachine = Flag("--players-machine") };
                if (Arg("--weather") is { } weather) scenes.WeatherEffect = weather;
                var b = scriptRuntime.Build;
                // To the console and to the game's log too: a windowed run has no console, and the log is where a player
                // sees which scripts ran — the shipped classes or the sources, and what the Codex folder's overlay put over them.
                foreach (var line in (string[])[$"scripts: {b.Sources} {(b.FromClasses ? "classes (the shipped ones, bridged)" : "sources")}, {b.Natives} natives, " +
                                                (b.CacheHit ? "cached" : $"compiled in {b.CompileMs} ms") +
                                                $", loaded in {scriptRuntime.LoadMs} ms ({sw.ElapsedMilliseconds} ms total)", .. b.Lines()])
                {
                    Console.WriteLine(line);
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(Diag.For("Vitomir.Scripting"), "{Line}", line.TrimStart());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"scripts: not running ({e.Message.Split('\n')[0]})");
            }
        }
        return null;
    }
}
