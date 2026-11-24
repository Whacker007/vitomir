// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game run's camera: the game camera, what the screen shows with it, the swivel and its lock.
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
    void StartCamera()
    {
        // **The game's own camera**. With a player in the world it
        // follows them the way 1.1 does, with 1.1's own numbers; with none, or with `--pos`
        // pinning the view, the free camera keeps the screen until a script takes the camera for a
        // cutscene. It has to exist before the scripts start, because a scene's `beginscene` may open
        // with one — the monastery's does.
        gameCamera = location.Player is not null && Arg("--pos") is null;
        worldCam = new WorldCamera
        {
            // How far up it turns and whether it is smoothed on steps (ours, CameraChoices: the settings, or a recording's own).
            UpLimit = CameraChoices.Up,
            SmoothSteps = CameraChoices.Steps,
            // How far out it zooms, and whether what is in its way is seen through rather than pulled in front of (ours too).
            ZoomOut = CameraChoices.Out,
            SeeThrough = CameraChoices.SeeThrough,
            // The cutscene eye's pull-in: onto the first wall between the shot's subject and the eye.
            SightLine = (who, lookAt, eye) => CameraSweep.CutsceneEye(location,
                CameraSweep.StartSector(location, lookAt, who.Sector), lookAt, eye),
        };
        // The game camera's: how far back the eye can sit before something is in the way —
        // the engine's fat ray, from the sector of the target (`CameraSweep`).
        // (Cast into the place the camera is in: the Storyteller's may be in another, `StoryView`.)
        worldCam.Obstruction = (from, towards, want) => CameraSweep.GameCamera(net?.View?.Place ?? location,
            CameraSweep.StartSector(net?.View?.Place ?? location, from, worldCam.Subject?.Sector ?? -1), from, towards, want);
        Picking = () => (!gameCamera || worldCam.Mode == CameraMode.Game) && net?.View?.Place is null;
        // On whoever is steered (the steered character): a load's steered member, not always the
        // party's leader — earlier, a quick load with Wilhem steered framed Christof while the keys drove Wilhem.
        worldCam.Start(gameCamera ? location.Steered ?? location.Player!.Thing : null);
        frame.Sim.Camera = worldCam;
        frame.Sim.Talk = talk;
        frame.Effects = worldCam.Effects;                                       // the snapshot carries them to the effects pass
        // The escape menu's show hook: every fade, flash and tint in progress gone.
        menus.EscapeShown = () => worldCam.Effects.ClearAll();
        // As the save and load screens open: the game's modal panes shut first.
        menus.CloseGamePanes = () =>
        {
            if (session.Shops.Active is not null) session.Shops.End();
            ShowVault(false);
        };
        // The flashes the game itself puts up — a frenzy's red one — go to the same screen.
        session.Screen = worldCam.Effects;
        session.Cursor = cursor;                 // the game's own refusals: a put-down gone wrong
        // The bar's own refusals — a portrait with nothing to cast at it — are the same red cross.
        hud.Bar.Refuse = () => cursor.Refuse(frame.Sim.Milliseconds);
        // The icons go while a pane, a shop or a conversation has the screen, and with the
        // rest of the interface in a cutscene.
        effectIconsPass.Hidden = () => invPass.Enabled || discPass.Enabled || talk.Current is not null
                                       || worldCam.Mode != CameraMode.Game;
        // The status bar the player asked for. A cutscene takes it down and its release puts it back
        // up — unconditionally, as 1.1 does, whether or not F11 had it down before.
        hudWanted = hud.Enabled; wasCutscene = false;
        barHooks = new Vitomir.Engine.Ui.StatusBarHooks(hud.Enabled);   // the bar's show and hide hooks
        // Whether the pointer is drawn at all (`--no-cursor`), before the camera has its say.
        cursorWanted = cursorPass.Enabled;
        // The status bar and everything that belongs to it — the heads in its middle, the party rows,
        // the description boxes that are panes of its own family — are up when the player wants them
        // and the camera is not in a cutscene (the cutscene's start takes them down). The release
        // puts the bar back whatever F11 said. The pointer is the camera's to show or hide.
        cameraWas = worldCam.Mode;
        // The cutscene's start shuts the character sheet, the inventory, the quest and the discipline panes on
        // the way into a cutscene, and nothing opens them again.
        worldCam.CutsceneBegan += () => { charPass.Enabled = false; invPass.Enabled = false; };
        frame.Game.Aim = Aim;
        // The story's films: shown in the game's own film state, the game held.
        // Headless, only for `--film-at`, a still of one.
        filmScreen = new FilmScreen(host, gpu, uiOut, ui, files, Flag("--no-video") || headless is not null && Arg("--film-at") is null
            ? null : FFmpegTool.Find(Arg("--ffmpeg")), audio, speakers, tape);
        filmScreen.Networked = () => net is not null;                          // `skipstoryfilms` is single player's
        filmScreen.Film.Opened = endText.LetGo;                                 // a film's start lets the end text go
        if (scenes is not null)
        {
            scenes.PlayFilm = filmScreen.Start;
            scenes.OpenMaterialFilm = MaterialFilmHost.Opener(files, Flag("--no-video"), Arg("--ffmpeg"));   // a mod's MaterialPlayback
            filmScreen.Film.Ended = scenes.FilmEnded;
            scenes.Camera = worldCam;
            scenes.Talk = talk;
            // `RPG_NEWRANKGAINED` goes to the message log in yellow; the console is ours.
            // A new rank goes to the message log in yellow with the interface's bell —
            // `Session.RankGained`, which a quest's experience reaches too.
            session.Ranked = (who, rank) => Console.WriteLine($"  rank: {Watch.Name(who)} reached rank {rank}");
            scenes.RankGained = session.RankGained;
            // The haven vault is opened by a script (`HavenVault` calls `Vault`), not by a key.
            // In the Storyteller's seat the seat is left first: the multiplayer bar back.
            // A player's character that is not the control is its machine's to open (`StoryVault.Hand`).
            scenes.Vault = guid =>
            {
                if (Vitomir.Engine.Net.StoryVault.Hand(net?.Server, net?.Client is not null, guid, location.Steered)) return;
                Vitomir.Engine.Net.StoryVault.Open(location.Steered, vaultPass.Enabled, net?.Story?.On == true,
                                                   () => net!.Story!.Leave(), () => ShowVault(true));   // the vault, shown
            };
            scenes.SaveRequested = () => { if (menus.SinglePlayer) menus.Ask(MenuRequest.SaveMenu); };
            // `PlayVoice`'s words, and the subtitle it puts up for a line with nothing to hear.
            scenes.VoiceWords = id => words[id] ?? id;
            // The string tables, for a thing's name and the discipline messages (`FormatNLS`).
            scenes.Nls = strings.Find;
            // `ExecuteText`: the read pane, unless it is already up or there is no such document.
            scenes.ReadText = name =>
            {
                Vitomir.Engine.Ui.DocumentOpen.Before(captions, worldCam, location.Steered);   // captions down, the look left
                if (readPass.Enabled || documents.Find(name) is not { } doc) return false;
                Vitomir.Engine.Ui.ReadPaneOpen.Accepted(worldCam, location.Steered, menus.CloseGamePanes, menus);   // the modal panes shut first: any camera mode
                readPass.Pane!.Open(doc);
                return true;
            };
            // Closing it tells the script that opened it (`textended`).
            if (readPass.Pane is { } readClosing) readClosing.Closed += scenes.TextClosed;
            scenes.Subtitle = (text, ms) =>
            {
                talkPass.Pane!.Reset();
                talkPass.Pane.Say(text);
                standaloneUntil = frame.Sim.Milliseconds + ms;
                Console.WriteLine($"  voice: {text}");
            };
            scenes.ExperienceGiven = (who, amount) =>
            {
                Console.WriteLine($"  xp: {Watch.Name(who)} +{amount}, now {who.Actor?.SpendableExperience} to spend"
                                + $" of {who.Actor?.Experience} earned");
                Journal.Current?.Write(frame.Sim.Milliseconds, JournalKind.Story, ("what", "experience"),
                    ("who", Watch.Name(who)), ("amount", amount), ("earned", who.Actor?.Experience));
            };
        }
        // A player's machine told to open the vault, scripts or none.
        if (net?.Client is { } asked) asked.VaultAsked += () => Vitomir.Engine.Net.StoryVault.Asked(location.Steered, vaultPass.Enabled, () => ShowVault(true));   // the host's vault message
        frame.World.Sky(Arg("--sky-only"), Arg("--sky-hide"));
        Console.WriteLine($"{location.Summary} ({frame.World.MissingTextures} missing textures); GPU {gpu.Driver} ({gpu.ShaderFormatName})");
        if (Flag("--verbose"))
        {
            foreach (var s in sceneMesh.Skipped) Console.WriteLine("  skipped " + s);
            foreach (var m in materials.Items.Where(m => m.Image is null))
                Console.WriteLine($"  no texture for {m.Name} (textures: {string.Join(", ", m.Nam.Textures)}; passes: {string.Join(", ", m.Nam.Passes.Select(p => p.Texture))})");
        }
    }

    void ScreenState()
    {
        if (worldCam.Mode != cameraWas)
        {
            Console.WriteLine($"  camera: {cameraWas} -> {worldCam.Mode}");
            Journal.Current?.Write(frame.Sim.Milliseconds, JournalKind.Story, ("what", "camera"),
                ("from", cameraWas.ToString()), ("to", worldCam.Mode.ToString()));
            cameraWas = worldCam.Mode;
        }
        bool cutscene = worldCam.StatusBarHidden;
        if (wasCutscene && !cutscene) hudWanted = true;
        if (barAsked) { hudWanted = true; barAsked = false; }
        wasCutscene = cutscene;
        // The bar's hooks on F11's or a cutscene's change (`StatusBarHooks`): the hide takes every pane and both
        // description boxes down; the show lays the rows out.
        switch (barHooks.Step(hudWanted && !cutscene))
        {
            case Vitomir.Engine.Ui.StatusBarHooks.Hook.Hide when advancement is null:
                ShowQuests(false); ShowInventory(false); ShowDisciplines(false); ShowCharacter(false);
                rolloverPass.TakeDown();
                break;
            case Vitomir.Engine.Ui.StatusBarHooks.Hook.Show: hud.Bar.Update(); break;
        }
        // The advancement screen puts its own bar where the status bar was.
        hud.Enabled = hudWanted && !cutscene && advancement is null;
        // The advancement screen has a head of its own, the subject's, in its own canvas (the heads' mode 2).
        headsPass.Enabled = hud.Enabled || advancement is not null;
        headsPass.Mode = advancement is not null ? Vitomir.Engine.Ui.PartyHeads.Advancement
                       : net is null ? Vitomir.Engine.Ui.PartyHeads.SinglePlayer : Vitomir.Engine.Ui.PartyHeads.Narrow;
        headsPass.Alone = advancement?.Subject;
        rolloverPass.Enabled = (hud.Enabled || advancement is not null) && !Flag("--no-rollover");
        cursorPass.Enabled = cursorWanted && worldCam.CursorShown;
        // The world's columns while a pane is up (`WorldView`) — and none in state 4: the
        // shop, the advancement screen, the map, the Options.
        Pick.View = buyPass.Enabled || advancement is not null || menus.State == FrontEnd.StateOptions
                    || menus.Map?.Pane is { } mapUp && menus.IsUp(mapUp)
            ? new ViewColumns(0f, 0f, 1f)
            : WorldView.Columns(hud.Enabled, charPass.Enabled || questPass.Enabled || vaultPass.Enabled,
                                invPass.Enabled || discPass.Enabled, cam.HorizontalFov43);
        endText.World(Pick.View.Drawn);                                     // state 3 <-> 4 lets the end text go
        // And what the pointer was over is let go the moment the game camera stops picking, rather
        // than on the next mouse move (see `Pointer`).
        if (gameCamera && worldCam.Mode != CameraMode.Game && hovering is not null)
        {
            hovering = null;
            rolloverPass.Hovered = null;
        }
    }

    // Whether the frame is drawn from the game camera or from the harness's own free camera.
    bool CameraDrives() => !Flag("--hold-camera")                 // ours: the harness's --pos view kept through cutscenes and camera paths
                           && (gameCamera ? worldCam.Mode != CameraMode.Free
                                          : worldCam.Mode is CameraMode.Cutscene or CameraMode.Path);

    // The camera the frame is drawn from, pointed where the game camera has just worked out.
    void Aim(FreeCamera view)
    {
        if (!CameraDrives()) return;
        var f = worldCam.Forward;
        view.Position = worldCam.Frame.Translation;
        view.Yaw = MathF.Atan2(f.Y, f.X) * (180f / MathF.PI);
        view.Pitch = MathF.Asin(Math.Clamp(f.Z, -1f, 1f)) * (180f / MathF.PI);
        view.HorizontalFov43 = worldCam.Fov;
    }

    // The game camera's frame worked out now rather than at the next step: for a still, and for
    // anything that has just moved the player without the world stepping.
    // `--camera-yaw`, `--camera-pitch` and `--camera-zoom <scale>` for a headless still (harness, ours): the game camera's
    // angles and zoom set outright before the still, and its distance taken where it is heading at once (the still has no
    // time for it to ease out). The zoom is held to what the run lets it reach (0.1 to `--camera-out`).
    void HarnessCamera()
    {
        if (worldCam.Mode != CameraMode.Game) return;
        bool asked = false;
        if (Arg("--camera-yaw") is { } cy) { worldCam.Angles.Z = F(cy); asked = true; }
        if (Arg("--camera-pitch") is { } cp) { worldCam.Angles.X = F(cp); asked = true; }
        if (Arg("--camera-zoom") is { } cz) { worldCam.ZoomScale = WorldCamera.ZoomMin; worldCam.Zoom(F(cz) - WorldCamera.ZoomMin); asked = true; }
        if (!asked) return;
        AimNow();
        worldCam.Distance = worldCam.DistanceTarget;
        AimNow();
    }

    void AimNow() { worldCam.Tick(0f, frame.Sim.Milliseconds); Aim(cam); }

    // **The player takes over somebody else** (the take-over and what it does): the mouse and the
    // keys drive the new one's walker, the camera slides over to it, the panes and
    // the hand follow it, the bar lights its row, and the walk ring goes
    // where it is walking, or away. The one let go is its party AI's again.
    void OnSteered(WorldThing who)
    {
        // The one let go asks for no more swings: the swing repeats only for the steered
        // character, and the per-frame ask below is the new one's.
        if (player?.Thing is { } was && was != who && was.Actor is { } wasActor) wasActor.Repeating = null;
        player = location.SteeredWalker;
        worldCam.SetSubject(who);
        charPass.Pane!.Subject = who.Actor;
        invPass.Pane!.Subject = who.Actor;
        vaultPass.Pane!.Subject = Vitomir.Engine.Ui.InventoryPane.VaultSubject(who);   // the player's own only
        // And the discipline pane is built again for the new one (the take-over rebuilds it between the
        // inventory and the vault). Earlier, it kept the last one's list while it stayed open
        // (in play, the disciplines page did not change with the party member).
        RebuildDisciplines();
        if (hands is not null) hands.Owner = who;
        if (player is { Walking: true } going) frame.Sim.WalkTarget.Set(going.Destination, Vector3.UnitZ);
        else frame.Sim.WalkTarget.Clear();
        hud.Bar.Update();
        Console.WriteLine($"  steer: now {Watch.Name(who)}");
    }

    // The Control menu's "Enable Mouse Pitch", from the install's own `masquerade.ini` — off when
    // the file does not say, because the engine's options word starts at zero.

    // **The pointer is pinned for the whole swivel.** Relative mode gives the camera its deltas and
    // stops the absolute position reaching `Pointer`, so the drawn cursor holds still while the
    // swivel is on — but the *system* pointer has been travelling the whole time, and the first
    // motion after the release carries wherever it got to. Play found it with an item on the
    // cursor: hold right, sweep across, let go, and the sword jumps to the edge of the screen or
    // off it. So we remember where it was and put it back.
    void StartSwivel()
    {
        if (looking) return;
        InputTape.Buttons(out float ax, out float ay);
        looking = true;
        lookAnchorX = ax; lookAnchorY = ay;
        host.RelativeMouse(true);
    }

    void StopSwivel()
    {
        if (!looking) return;
        looking = false;
        lockAt = null;                                  // the centred look's pointer is free again where it was drawn
        host.RelativeMouse(false);
        host.Warp(lookAnchorX, lookAnchorY);
        // The warp's own motion event, and anything the platform had queued at the old position,
        // would move the cursor right back again.
        swallowMotion = 1;
        int uw, uh; host.PictureSize(&uw, &uh);
        // And the pick is re-run at the anchor, because the camera has turned under a pointer that
        // has not moved: what is beneath it is not what was beneath it.
        Pointer(lookAnchorX, lookAnchorY, uw, uh);
    }

    // The swivel lock's centred look (ours, Engine.Ui.CentreLook): anything opened ends it; while it
    // holds, the pointer, the pick and the cursor's point are the middle of the world's view, every frame.
    void CentredLook()
    {
        var open = (charPass.Enabled ? OpenPanes.Character : 0) | (invPass.Enabled ? OpenPanes.Inventory : 0)
                 | (questPass.Enabled ? OpenPanes.Quests : 0) | (discPass.Enabled ? OpenPanes.Disciplines : 0)
                 | (vaultPass.Enabled ? OpenPanes.Vault : 0) | (menus.Map?.Pane is { } mapPane && menus.IsUp(mapPane) ? OpenPanes.Map : 0)
                 | (buyPass.Enabled ? OpenPanes.Shop : 0) | (readPass.Enabled ? OpenPanes.Document : 0)
                 | (talkPass.Pane is { Offering: true } ? OpenPanes.Answers : 0) | (advancement is not null ? OpenPanes.Advancement : 0)
                 | (menus.Up ? OpenPanes.Menu : 0);
        if (centreLook.Opened(open) && swivelHold.Locked)
        {
            Swivel(swivelHold.SetAside());
            Console.WriteLine($"  keys: swivel lock {(swivelHold.Waiting ? "set aside" : "off")} ({open} opened)");
        }
        // The QoL `relock`: a lock set aside comes back once all that was opened is closed again.
        if (swivelHold.Waiting && swivelHold.Resume(open == OpenPanes.None && SwivelCamera() && Pick.View.Drawn) is var back && back != SwivelStep.Nothing)
        {
            Swivel(back);
            Console.WriteLine("  keys: swivel lock back on (everything opened is closed)");
        }
        if (!swivelHold.Locked || !looking) { lockAt = null; return; }
        int lw = 0, lh = 0; host.PictureSize(&lw, &lh);
        if (CentreLook.At(lw, lh, frame.WorldRows(lh), Pick.View) is not { } at) { Swivel(swivelHold.End()); return; }
        lockAt = at;
        (lookAnchorX, lookAnchorY) = (at.X, at.Y);      // where the pointer is left when the lock ends
        (pointerX, pointerY, winW, winH) = (at.X, at.Y, lw, lh);
        Pointer(at.X, at.Y, lw, lh);
        (cursorPass.X, cursorPass.Y) = (at.TipX, at.TipY);
    }

    void Swivel(SwivelStep step) { if (step == SwivelStep.Start) StartSwivel(); else if (step == SwivelStep.Stop) StopSwivel(); }

    // Whether the game camera has the mouse: the swivel's and its lock's (SwivelHold).
    bool SwivelCamera() => follow && worldCam.Mode == CameraMode.Game && !menus.Up && !buyPass.Enabled && !readPass.Enabled;


    // The game as it stands, written as a save. A zip unless `plain`, when the parts
    // go into a folder of their own.
    // **When the camera's pull-in changes its mind**, which is what a jitter is made of: the
    // level letting the eye out and snatching it back within a frame or two. Journalled so a
    // play-test's rows say where it bites — the answer is the engine's own sweep
    // (`CameraSweep`), no longer the stand-in ray (`Pick.ChaseDistance`).
    void CameraPull()
    {
        if (Journal.Current is not { } log || worldCam.Mode != CameraMode.Game) return;
        var now = worldCam.PulledTo;
        bool changed = (now is null) != (wasPulled is null)
                    || (now is { } a && wasPulled is { } b && MathF.Abs(a - b) > 20f);
        if (changed && frames > 2)
            log.Write(frame.Sim.Milliseconds, JournalKind.Story, ("what", "camera"),
                ("pull", now), ("was", wasPulled), ("distance", worldCam.Distance),
                ("where", player is null ? null : $"{player.Position.X:0},{player.Position.Y:0},{player.Position.Z:0}"),
                ("yaw", worldCam.Angles.Z), ("pitch", WorldCamera.Signed(worldCam.Angles.X)));
        wasPulled = now;
    }
}
