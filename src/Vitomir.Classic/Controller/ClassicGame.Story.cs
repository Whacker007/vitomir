// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game run's story: the campaign, journeys and level changes, the main sequence's requests, the end text.
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

unsafe sealed partial class ClassicGame
{
    void StartStory()
    {

        // **The campaign, and the other half of `TakeExit`.** The native records which exit was taken
        // and sets the sequence's state to 4; something else has to notice and
        // do the work, and in the original that something is the main loop. It is the main loop here
        // too. `Campaign` owns the engine half — build the destination, carry the party into it, stand
        // them on the entrance the exit named — and everything below is the harness half: the renderer
        // and the panes have to be pointed at the new place.
        campaign = owner?.Chronicle is { } storyRunning
            ? new Campaign
            {
                Chronicle = storyRunning,
                Here = location,
                Build = (levelFile, sceneName, entrance) =>
                {
                    var next = Location.Load(files, levelFile, things: !Flag("--no-things"),
                                             sky: !Flag("--no-sky"), scene: sceneName,
                                             player: playerTemplate, entrance: entrance);
                    if (!Flag("--no-ai"))
                        next.Awaken(new AiClasses(files), 1, effects, powers);          // the launch's AI stream, carried on
                    return next;
                },
                // A place a loaded save remembers: built from its level and scene, given back what the
                // save kept, woken after it, and its scripts armed to be restored when it is entered.
                BuildSaved = (record, entrance) =>
                {
                    var next = Location.Load(files, record.Level, things: !Flag("--no-things"),
                                             sky: !Flag("--no-sky"), scene: record.Scene.Length > 0 ? record.Scene : null,
                                             player: playerTemplate, entrance: entrance);
                    var restore = SaveApply.Place(next, record, effects, powers);
                    if (!Flag("--no-ai"))
                        next.Awaken(new AiClasses(files), 1, effects, powers, fromSave: true);   // the launch's AI stream, carried on
                    scenes?.Arm(next, restore);
                    return next;
                },
            }
            : null;
        // A network game's host keeps eleven places loaded, not six.
        if (campaign is not null && net?.IsHost == true) campaign.Host();          // and each place remembered by its file (CampaignFiles.cs)
        // Every other place the save remembers waits in the campaign until the party goes back to it,
        // which is when 1.1 reads its `.nsw`; the one the party is in is already built.
        if (loaded is not null && campaign is not null)
        {
            foreach (var (name, record) in loaded.World)
            {
                int index = campaign.Chronicle.Find(name);
                if (index < 0) { Console.WriteLine($"load: the chronicle has no {name}; its record is dropped"); continue; }
                if (index == campaign.Chronicle.Current) campaign.Keep(index, location);
                else campaign.Remember(index, record);
            }
        }
        // The teleport table reaches the other places through the campaign (`World.Teleports`). And the
        // place the game starts in — a new game, a load — is arrived in like any other: on the map.
        if (campaign is not null) session.Teleports.Campaign = campaign;
        // A new game loads the chronicle's early places — Prague's haven — before its first
        // (CampaignPreloads.cs). A network game's does not: the lobby loads the chronicle alone
        // and the host its first place alone.
        if (campaign is not null && newStart is not null && loaded is null)
        {
            var took = System.Diagnostics.Stopwatch.StartNew();
            foreach (var early in campaign.Preload(campaign.Chronicle.Current))
                Console.WriteLine($"load: {early.Name}/{early.SceneFile} loaded before the first place, {early.Things.Count} things " +
                                  $"numbered {early.SceneGuids.First}..{early.SceneGuids.End - 1} ({took.ElapsedMilliseconds} ms)");
        }
        if (campaign?.Chronicle.At(campaign.Chronicle.Current) is { } startedIn) startedIn.Flags |= Campaign.ShowInMap;
        // The name box's filling: the location's name through the string table, and the time.
        menus.Describe = () => SaveCapture.DescribeNow(location, session, campaign, PlaceWords);

        // The level's sound starts before its scripts do (1.1 plays the music and the ambient,
        // then begins the scene).
        audio.Enter(location);
        Vitomir.Engine.Levels.Cells.CellLevel.Arrived(location, campaign?.Chronicle);   // ours: a cell level's exits and areas (CellLevel.Play.cs)
        ChapterTitle();                                                         // before the scripts
        if (scenes is null && autosave && loaded is null) Autosave();
        // A network game is one before its scene begins (1.1's network-game flag, set as the game is made), so the
        // menus' pause is never the single player's in it. Earlier, the network session said so only once the scripts ran
        // (NetPlay.Attach, below): a chronicle's opening document (MP_CurseDarkness's) paused the host as a single player's
        // read pane does, and its close, by then in a network game, never took the pause off — the host's own
        // place stood still and the game camera with it (the Storyteller's carried body seen from the inn's camera).
        if (net is not null) menus.SinglePlayer = false;
        if (scenes is not null)
        {
            frame.Scripts = scenes;
            // A load's scripts are made and then given back what they kept, before the scene begins.
            // A place the save did not remember was built fresh, and starts fresh.
            if (loaded is not null && loadRestore is not null && loadedPlace is not null)
            {
                if (loaded.Chronicle is { } keptStory)
                {
                    scenes.ArmChronicle(keptStory.Scripts);
                    scenes.WeatherEffect = keptStory.Weather ?? "";                    // the load reads it back
                }
                scenes.Arm(location, loadRestore);
            }
            // The autosave at every scene start, before its scripts hear of it — never for a load.
            scenes.SceneStarting = (_, restored) => { if (autosave && !restored) Autosave(); };
            foreach (var early in campaign?.TakePreloaded() ?? []) scenes.Preload(early, campaign!.Chronicle);   // made and told `created`, not begun
            scenes.Enter(location);
            var live = scenes.Running.ToArray();
            Console.WriteLine($"scripts: {live.Length} running" +
                              (scenes.ChronicleName is { } c ? $", {c}" : "") +
                              (live.Length == 0 ? "" : ": " + string.Join(", ",
                                  live.GroupBy(n => n).OrderByDescending(g => g.Count())
                                      .Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : g.Key).Take(8))));
            if (scenes.Moving > 0) Console.WriteLine($"scripts: {scenes.Moving} things moving");
            if (scenes.Engine.Missing.Count > 0)
                Console.WriteLine($"scripts: {scenes.Engine.Missing.Count} natives asked for and not implemented, " +
                                  $"{scenes.Engine.Missing.Values.Sum()} calls");
        }

        // The network session tied to the world now that it is built and its scripts are running.
        net?.Attach(location, session, location.Player?.Thing, strings, hud, netPass, menus, scenes, ui, files, owner?.Chronicle, () => hovering);
        if (net?.Server is { } viewing) net.View = new StoryView(gpu, () => frame, worldCam, viewing.Places) { Story = () => net.Story, BeforeDraw = HeadsBeforeDraw };        // the camera's subject across places
        if (net?.Server is { } hosting && campaign is not null) NetPlaces.Wire(hosting, campaign, session, scenes, () => talk);   // players in different places
        if (net is not null) net.Busy = () => talk.Current is { Running: true } || !Vitomir.Engine.Net.ChatKeys.Read(worldCam.Mode == CameraMode.Game,   // the key handler's gates
            dialog: readPass.Enabled, vault: vaultPass.Enabled, advancement: advancement is not null, shop: buyPass.Enabled, typing: false);
        net?.Scenes(talk, worldCam, talkPass.Pane!, id => words[id]);
        // What a script on the host shows this player: the read pane, the quests (NetPlay.Texts).
        net?.Texts(name => { Vitomir.Engine.Ui.DocumentOpen.Before(captions, worldCam, location.Steered); if (readPass.Enabled || documents.Find(name) is not { } doc) return false; Vitomir.Engine.Ui.ReadPaneOpen.Accepted(worldCam, location.Steered, menus.CloseGamePanes, menus); readPass.Pane!.Open(doc); return true; },
                   readPass.Pane, session.Quests, powers);
        if (net is not null) net.Advance = OpenAdvancement;

        // Whether the character sheet is standing in for the advancement screen (`Advance`), so that
        // closing it sends the party on.
        // `--answer <n>` — see `AutoAnswer`.
        autoAnswer = int.TryParse(Arg("--answer"), out var answerWith) ? answerWith : -1;

        // A loaded place thrown away — its scene changed, or its chronicle ended — takes its kept
        // scripts with it.
        if (campaign is not null && scenes is not null) campaign.Discarded += scenes.Forget;
        if (campaign is not null) campaign.Discarded += gone => files.Cache.Release(gone);   // and what was read ahead for it (FileCache.cs)
        if (campaign is not null) campaign.Discarded += Vitomir.Engine.Levels.Cells.CellLevel.Gone;   // ours: a cell level's stream stopped (CellLevel.Play.cs)

        // `--take-exit N` asks for a journey at once, without finding the door first. It is the same
        // request `CodexSequence.TakeExit` records, so what it exercises is the real path and not a
        // shortcut around it.
        if (Arg("--take-exit") is { } exitArg && int.TryParse(exitArg, out int takeExit))
        {
            if (campaign is null) Console.WriteLine("take-exit: no chronicle is running here");
            else if (!campaign.Chronicle.TakeExit(takeExit))
                Console.WriteLine($"take-exit: exit {takeExit} of " +
                                  $"{campaign.Chronicle.NameOf(campaign.Chronicle.Current)} " +
                                  "does not exist, is closed, or leads nowhere");
            else Console.WriteLine($"take-exit: leaving by exit {takeExit}");
        }

        // `--cast <discipline>` and `--use <template>[@seconds]`: HarnessOrders.cs.
        castOrder = HarnessOrders.Casts.Parse(Arg("--cast"));   // several, one after another
        castOrder?.Begin(location);
        useOrder = HarnessOrders.Use.Parse(Arg("--use"));
        journey = HarnessJourney.Parse(Arg("--journey"));       // `--journey "exit 10@1; exit 0@4"`: HarnessJourney.cs (ours)
        // The host program's observers of the run (RunAdditions), told the place is up.
        OpenObservers();

        // With a player in the world the camera follows them and the movement keys drive them
        // instead; the mouse still turns the camera, and the player walks the way it is looking.
        // **Whoever is being steered** — Christof, unless a load says otherwise; Tab, F6–F9 and a
        // click on a portrait change it (`OnSteered`, below).
        player = location.SteeredWalker ?? location.Player;
        follow = gameCamera;       // --pos pins the camera instead
        location.SteeredChanged += OnSteered;
        if (player is not null)
        {
            // Put the player somewhere other than the scene's start, and aim them: what a walk test
            // needs to be repeatable.
            if (Arg("--start") is { } start)
            {
                var c = start.Split(',').Select(F).ToArray();
                var at = new Vector3(c[0], c[1], c.Length > 2 ? c[2] : player.Position.Z);
                // Given no height, stand on the ground there — the storey nearest the scene's start if
                // there is more than one — rather than at the start's own height, which left Christof
                // hanging in the air over the monastery's stairs ("Christof has
                // ascended").
                if (c.Length <= 2 && location.Walk?.Nearest(at) is int under and >= 0)
                {
                    at.Z = location.Walk.Nodes[under].Height(at.X, at.Y);
                    Console.WriteLine($"  --start {start}: standing on the ground there, at height {at.Z:0}");
                }
                player.Thing.Position = at;
                if (!player.Ground()) Console.WriteLine($"  --start {start} is not on walkable ground");
                // The companions come too, beside the player as `--companion` put them beside the
                // scene's start — otherwise a start across the level leaves them to walk the whole way.
                int beside = 0;
                foreach (var comp in companionsPlaced)
                {
                    var spot = player.Position + new Vector3(96f * ++beside, 0f, 0f);
                    int node = location.Walk!.Find(spot);
                    if (node < 0) { Console.WriteLine($"  companion {comp.Template}: no ground beside the start, left where it was"); continue; }
                    spot.Z = location.Walk.Nodes[node].Height(spot.X, spot.Y);
                    location.Move(comp, spot);
                    comp.Walker?.Ground();
                }
            }
            if (Arg("--face") is { } face) player.Heading = F(face);
            if (Arg("--face-thing") is { } target && int.TryParse(target, out int guid) && location.Thing(guid) is { } aim)
            {
                var to = aim.Position - player.Position;
                player.Heading = MathF.Atan2(-to.X, to.Y) * (180f / MathF.PI);
                Console.WriteLine($"  facing thing {guid} ({aim.Template}) at {Bodies.Distance2D(player.Position, aim.Position):0} units");
            }
            // The camera faces the way the character does, so a `--face` turns it too —
            // unless a script already has it for a cutscene.
            if (follow && worldCam.Mode == CameraMode.Game) worldCam.Angles.Z = WorldCamera.Wrap(player.Heading + 360f);
            AimNow();
            Console.WriteLine($"player: {playerTemplate} at {player.Position}, " +
                              (player.Node >= 0 ? $"standing on walk node {player.Node}" : "NOT on walkable ground") +
                              $", walk {player.WalkSpeed:0} run {player.RunSpeed:0}, {location.Walk!.Count} pieces of ground");
        }
    }

    // **Arriving somewhere**, whether by an exit, a jump, an advance or a new chronicle: the
    // renderer, the panes, the sound and the scripts are pointed at the new place. `keep` is
    // whether the place left stays loaded, which is whether its scripts are put aside for later.
    // **The end text**: the words 1.1 puts up when the party is lost. Every
    // floating text goes first; a journey takes the end text down, and a
    // load starts a new run, so it goes with that too.
    void EndText(string headline)
    {
        var font = ui.Assets.Font(Vitomir.Engine.Ui.Captions.EndFont);
        var up = Vitomir.Engine.Ui.Captions.EndText(headline, strings["RPG_ENDGAME_PRESSESC"],
                                                    UiCanvas.Measure(font, headline), captionPass.NowMs);
        endText.Put(up);
    }


    void Arrived(Campaign.Arrival arrival, int exitNumber, bool keep)
    {
        if (scenes is not null) scenes.Leave(exitNumber, keep);
        foreach (var early in campaign?.TakePreloaded() ?? []) scenes?.Preload(early, campaign!.Chronicle);   // a new chronicle's early places
        SceneStart.Begin(menus, endText, arrival.To.Scene, ref last, host.Now());   // the clock from now, the pause off, the texts down
        var went = arrival.To;
        // A conversation does not travel: its speakers belong to the place being left, and a
        // script that asked is told it was aborted (after the endscenes above).
        talk.Abandon(location);
        // The renderer is bound to a place: its world renderer, its sky and its simulation all
        // came from the old one, so it is replaced rather than re-pointed.
        var stale = frame;
        _ = Vitomir.Engine.World.LightStyles.DrawTheRenderersSpare();     // the new renderer's three draws, where it made them
        frame = new PlaceFrame(gpu, went)
        {
            WorldView = stale.WorldView, After = stale.After, SkyEverywhere = stale.SkyEverywhere, Effects = stale.Effects,
        };
        frame.Game.Aim = stale.Game.Aim;
        frame.Sim.Camera = worldCam;
        frame.Sim.Talk = talk;
        frame.Sim.Resume(stale.Sim.Seconds);
        Journal.Current?.Write(frame.Sim.Milliseconds, JournalKind.Story, ("what", "travel"),
            ("from", arrival.From.Name), ("to", went.Name), ("exit", exitNumber), ("entrance", arrival.Entrance),
            ("party", arrival.Party), ("returned", arrival.Returned ? 1 : 0));
        if (Flag("--no-fog")) frame.Fog = FogSettings.None;
        frame.World.Gamma = MenuChoices.Session.Brightness(Arg("--gamma") is { } g2 ? F(g2) : null, GameOptions.Current.Gamma);
        frame.World.Sky(Arg("--sky-only"), Arg("--sky-hide"));
        markerRenderer.Dispose();
        markerRenderer = frame.World.NewRing(went.Materials.Get(WalkTarget.Material));
        frame.Marker = markerRenderer;
        glow.Clear();
        hovering = null;
        frame.Glow = glow;
        stale.Dispose();

        // Steering outlasts the journey (the party's selected member is the engine's, not the
        // level's): whoever was being steered is looked for among those who arrived.
        var steeredHere = location.Steered is { } s && !ReferenceEquals(s, location.Player?.Thing) ? s.Template : null;
        location.SteeredChanged -= OnSteered;
        location = went;
        uint word = minds?.MainFlags ?? 0;
        minds = went.Minds;
        if (minds is not null) minds.MainFlags = Vitomir.Engine.Ai.MainFlagsWord.Across(word);   // one global word, as in 1.1
        // The sound goes with them: every channel stops, and the new level's music and
        // ambience start before its scripts do.
        went.Audio = audio;
        if (minds is not null) audio.Random = minds.Random;
        audio.Enter(went);
        Vitomir.Engine.Levels.Cells.CellLevel.Arrived(went, campaign?.Chronicle);   // ours: a cell level's exits and areas (CellLevel.Play.cs)
        player = went.Player;
        went.SteeredChanged += OnSteered;
        // The camera takes up its place in the new level before its scripts begin,
        // for the same reason it does at the start.
        gameCamera = follow = player is not null && Arg("--pos") is null;
        worldCam.Start(gameCamera ? player!.Thing : null);
        // The per-place wiring, which is set on the Location rather than held by the harness:
        // both of these were put on the place being left and have to be put on this one.
        location.InventoryShown = () => invPass.Enabled;
        invPass.Pane!.Subject = player?.Thing.Actor;
        charPass.Pane!.Subject = player?.Thing.Actor;
        if (hands is not null && player is not null) hands.Owner = player.Thing;
        // The game this place is part of: the message log, the quests, the purse.
        went.Session = session;
        session.Here = went;
        // A title still fading from the last place went with every floating text (SceneStart).
        ChapterTitle();
        // **The party stands where it arrives before any script runs** (1.1 places them as
        // the level goes in; the scene start is later). It has to be this way round: a scene's
        // `beginscene` may put the player somewhere of its own — the convent's 3.3 lays Christof
        // in bed with `TeleportPartyToConversation` — and a placement after the scripts would
        // drag him back off it, which is how he came to sleep beside the bed.
        if (player is not null)
        {
            player.Thing.Position = location.Arrivals.Length > 0
                ? location.Arrivals[0].Position : location.StartPosition;
            player.Ground();
            cam.Position = location.StartPosition;
            cam.Yaw = location.StartYaw;
            if (worldCam.Mode == CameraMode.Game) worldCam.Angles.Z = WorldCamera.Wrap(player.Heading + 360f);
            AimNow();
        }
        else cam = location.Camera();
        cam.Far = SceneStart.FarClip(location.Scene, cam.Far);                  // the scene's far clip, or 10,000
        if (scenes is not null)
        {
            frame.Scripts = scenes;
            scenes.Enter(location);
            location.Clicked = scenes.Clicked;
        }
        // Wherever the scene's own script has just put him, the camera is aimed at him again.
        if (player is not null) AimNow();
        watch.Where = location;
        // The status bar is the interface's, not the place's: it follows the party.
        hud.Bar.Place = location;
        if (steeredHere is not null && went.PartyMembers().FirstOrDefault(t =>
                !ReferenceEquals(t, went.Player?.Thing) && t.Template.Equals(steeredHere, StringComparison.OrdinalIgnoreCase)) is { } again)
            went.Steer(again);
        net?.Travelled(went, player?.Thing);
        Console.WriteLine($"  travel: now in {campaign!.Chronicle.NameOf(campaign.Chronicle.Current)} " +
                          $"({location.Name}) at entrance {arrival.Entrance}; " +
                          $"{arrival.Party} carried across with {arrival.Carried} items" +
                          (!arrival.Returned ? "" : campaign.Unentered(campaign.Chronicle.Current)
                              ? " -- into a place loaded at the chronicle's start" : " -- back to a place still as it was left"));
    }


    // **The main sequence's one request**, taken once a frame: the
    // journeys go through `Campaign.Travel`; an advance, a new chronicle and the end are handled
    // here. The engine's own position is after the scripts have run and before the next draw,
    // because a script asks to leave in the middle of a click and the world must not change
    // underneath the rest of that click.
    // `--answer <n>`: whenever a conversation is waiting on a reply, pick the n-th (from 0, or the
    // last one there is). A harness knob for runs nobody is at the keyboard for — the headless
    // checks of a scene that only goes on once the player has answered.
    void AutoAnswer()
    {
        if (autoAnswer < 0 || talk.Current is not { Waiting: true, Offered.Count: > 0 } asking) return;
        int pick = Math.Min(autoAnswer, asking.Offered.Count - 1);
        Console.WriteLine($"  answer: {asking.Offered[pick].Words}");
        asking.Pick(pick, frame.Sim.Milliseconds);
    }


    void Sequence()
    {
        // The end of the frame's chapter swap comes first (then the main loop's request):
        // a ChangeScene on a loaded place swaps its chapter in the live world (ChapterSwap.cs).
        if (campaign?.SwapChapters() is { Count: > 0 } swaps)
            foreach (var swap in swaps)
            {
                Console.WriteLine($"  chapter: {swap.Place.Name} is now {swap.Place.SceneFile} ({swap.Gone.Length} gone, {swap.Arrived.Length} arrived)");
                if (ReferenceEquals(swap.Place, location) && !Flag("--no-fog")) frame.Fog = location.Fog;   // the new chapter's settings
            }
        if (campaign?.Chronicle.Pending is { Kind: not (SequenceKind.Exit or SequenceKind.Jump or SequenceKind.AdvanceGo or SequenceKind.Teleport) } asked)
        {
            campaign.Chronicle.Take();
            switch (asked.Kind)
            {
                case SequenceKind.Advance:
                    // The scene `SetupAdvance` named goes on its location, and a player-type
                    // character gets the advancement screen — the character sheet, here,
                    // until spending experience is written. The journey waits for it to close.
                    campaign.Chronicle.ApplyAdvanceScene();
                    if (location.Thing(asked.Guid) is { Type: Vitomir.Engine.World.ThingType.Player } asking)
                        OpenAdvancement(asking);
                    break;
                case SequenceKind.ChangeChronicle:
                    if (chronicles[System.IO.Path.GetFileNameWithoutExtension(asked.Name)] is { } next)
                    {
                        // A new chronicle ends every quest and every loaded place, and
                        // empties the teleport table.
                        session.Quests.Clear();
                        session.Teleports.Clear();
                        var from = location;
                        if (campaign.ChangeChronicle(next) is { } change)
                        {
                            session.Chronicle = next;
                            DressPanes();
                            strings.AddFrom(next.Meta);
                            documents.Restart(next.Meta);   // the chronicle's documents
                            Console.WriteLine($"  chronicle: {next.Name} begins");
                            Arrived(change, 0, keep: false);
                        }
                        else Console.WriteLine($"  chronicle: {next.Name} has no first location to start at");
                    }
                    break;
                case SequenceKind.EndGame when asked.Reason == Chronicle.EndPartyDead:
                    // End text 2: the words go up over the world, which carries on — unless
                    // an end is already up, which keeps the first.
                    if (endText.Up) break;
                    EndText(strings["RPG_ENDGAME_PARTYDEAD"]);
                    Console.WriteLine("  end: all coterie members are dead -- Esc for the escape menu and a saved game");
                    break;
                case SequenceKind.EndGame when asked.Reason == Chronicle.EndHumanityLost:
                    // End text 3: the whole party marked dead, the lost ones' faces in
                    // frenzy, and their names in the words — which replace any end already up.
                    EndText(session.Messages.Format("RPG_ENDGAME_HUMANITYLOST", location.HumanityGone()));
                    Console.WriteLine($"  end: {endText.Text?.FirstOrDefault()?.Text} -- Esc for the escape menu and a saved game");
                    break;
                case SequenceKind.EndGame:
                    // End text 1 (single player): back to the main menu with the credits
                    // already rolling (request 1).
                    Console.WriteLine("  end: the game is over -- to the main menu and the credits");
                    menus.AskMainMenu(withCredits: true);
                    break;
            }
        }
        // The advancement screen closing is what sends the party on.
        if (advanceFinished)
        {
            advanceFinished = false;
            if (campaign?.Chronicle.AdvanceDone() == true) Console.WriteLine("  advance: on to the next scene");
        }
        // A place not loaded yet is built behind the loading plaque, over the last frame
        // (`Campaign.Builds`).
        bool plaque = campaign?.Builds == true;
        if (plaque) { menuPass.Loading = true; PresentOnce(); }
        net?.Journey(campaign?.Chronicle.Pending);                                   // the host alone, or everybody (NetPlay)
        if (campaign?.Chronicle.Leaving is { } leaving && campaign.Travel() is { } arrival)
            Arrived(arrival, leaving.Exit < 0 ? 0 : leaving.Exit,
                    keep: campaign.Resident.Values.Any(p => ReferenceEquals(p, arrival.From)));
        if (plaque) menuPass.Loading = false;
    }
}
