// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The front end: the title screen, the opening films and the main menu (state 2).
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

/// <summary>The front end (state 2): the title, the opening films, the main menu and the multiplayer screens, until a game is asked for.</summary>
/// <remarks>
/// The pushed frame: stepped a frame at a time (<see cref="ClassicRun.Step"/>). Its start (<see cref="Begin"/>) is what
/// <c>Run</c> did before its films; then each frame is one turn of the loop it was in — an opening film's (<c>PlayFilm</c>'s, a
/// state of its own until the film ends, the next one opened in the same frame as 1.1's phase 0 opens it), the menus' (with
/// their request, and the new game's film inside it, after which the rest of that turn is done) — cut at the top of the loop:
/// its condition and the harness's step for the next turn are said at the end of the turn before.
/// </remarks>
unsafe sealed class ClassicFrontEnd : ClassicRun
{
    internal ClassicFrontEnd(IClassicHost host, RunStart start, FrameInput input) : base(host, start, input) { }

    /// <summary>What the front end is doing this frame: its opening films, its menus, or the new game's film from them.</summary>
    enum State { Films, Menus, NewGameFilm }

    State state;
    bool windowed;
    UiCanvas ui = null!;
    IClassicInterface uiOut = null!;
    Vitomir.Engine.Ui.Strings text = null!;
    GameAudio sound = null!;
    SoundTape? tape;
    AudioOutput speakers = null!;
    FrontEnd menus = null!;
    MenuPass menuPass = null!;
    CursorPass cursorPass = null!;
    MpMenus mp = null!;
    PortraitPass portraitPass = null!;
    CreateAdvancement createAdvance = null!;
    ScreenFrame screen = null!;
    FFmpegTool? ffmpeg;
    VideoPass films = null!;
    IFrameLayer filmsDrawn = null!;
    int filmIndex;
    // The menus' loop (its locals until then).
    bool running;
    IScriptedInput? menuScripted;
    ulong started, last;
    int maxFrames, frames;
    // The turn of the menus whose request started the new game's film: its step, for the rest of that turn after the film.
    float newGameDt;

    // **The front end** (state 2): the title screen while the engine starts,
    // then the main menu. Nothing of a world is loaded until a game is asked for, and asking for
    // one ends this run with the game's arguments.
    internal override int? Begin(ulong now)
    {
        frameNs = now;
        // The front end draws no world: the sheet is the whole screen, so one shorter than the picture is centred on it,
        // black above and below (ours, UiLayout.Seat). Its films and its loading plaque with it.
        UiLayout.Seat = SheetSeat.Centre;
        windowed = OpenWindow("Vitomir");
        var gpu = OpenGpu();
        var assets = new UiAssets(files);
        ui = new UiCanvas(assets);
        uiOut = gpu.Interface();                                     // the 2D backend, then its model inserts
        text = new Vitomir.Engine.Ui.Strings(files);
        sound = new GameAudio(files);
        sound.Configure(GameOptions.Current.EffectsVolume, GameOptions.Current.MusicVolume);
        tape = Arg("--record-sound") is { } tapePath ? new SoundTape(tapePath) : null;
        speakers = headless is null && !Flag("--mute") ? host.Speakers(Mixer.Output) : new AudioOutput();
        speakers.Tap = tape is null ? null : tape.Take;
        menus = NewMenus(assets, text, sound, FrontEnd.StateFrontEnd);
        menus.Music = track => MenuMusic(sound, track);
        menuPass = new MenuPass(ui, menus) { Title = true };
        var cursor = new Vitomir.Engine.Ui.CursorState { Audio = sound };
        cursorPass = new CursorPass(ui, cursor) { Enabled = !Flag("--no-cursor") };
        if (cursorPass.Enabled && windowed) host.ShowCursor(false);
        // Multiplayer's screens (`MpMenus`), and their check boxes drawn.
        mp = new MpMenus(menus, files, text.Find, Path.Combine(Diag.UserDataFolder, "Crypt")) { SettingsPath = Diag.UserSettingsPath };   // `lastplayer`
        mp.Log += Console.WriteLine;
        if (int.TryParse(Arg("--port"), out var mpPort)) mp.Port = mpPort;
        mp.NetSaves = new SaveSlotFiles(SaveFolder(), SaveSlotFiles.Network);
        // Their check boxes and pictures right after their own pane, so a dialog over it covers them (MenuPass.Pictures):
        // until then a pass of their own after every pane, and mpCreateGame's password box stood over a message box.
        menuPass.Pictures = mp.Checkboxes;
        // The character screens' portrait window: the model, over its pane — the pane's draw hook, so
        // drawn right after that pane and under a dialog over it (the address box).
        portraitPass = new PortraitPass(ui, files)
        {
            Showing = () => mp.PortraitTemplate, Ticks = () => host.Ticks(), Log = Console.WriteLine,
            Keys = () => (host.KeyDown(LoopScancode.Left), host.KeyDown(LoopScancode.Right)),   // straight from SDL, not the tape's
        };
        menuPass.Hook = pane => mp.DrawsPortrait(pane) ? portraitPass : null;
        // The advancement screen after Create, over the front end (`CreateAdvancement`).
        createAdvance = new CreateAdvancement(ui, mp, new Vitomir.Engine.Rpg.DisciplineDefinitions(files), text, () => (menus.PointerX, menus.PointerY));
        // With no model to show it is still run, drawing nothing, so its clock and its "shown last" go on as earlier.
        var portraitIdle = new UiWhen(portraitPass, () => mp.PortraitTemplate is null);
        screen = new ScreenFrame(gpu.Screen()) { After = uiOut.Of(new UiPasses(menuPass, portraitIdle, createAdvance.Passes, cursorPass)) };

        // The films, if the player has an ffmpeg to decode them with. None, and every film is
        // skipped and the game carries on — Vitomir ships no decoder and does not pretend to.
        ffmpeg = Flag("--no-video") || headless is not null ? null : FFmpegTool.Find(Arg("--ffmpeg"));
        films = new VideoPass(ui);
        filmsDrawn = uiOut.Of(films);
        if (ffmpeg is not null) Console.WriteLine($"video: {ffmpeg.Version} at {ffmpeg.FFmpeg}");
        else if (headless is null && !Flag("--no-video")) Console.WriteLine(FFmpegTool.Absent);

        // 1.1 plays its two films first and puts the title screen up after them (the main
        // sequence's phase 0). **Once for the whole process, not once a run**: the entire block is
        // behind a latch that phase 0 sets on its way out and never clears, so
        // returning to the main menu — which is request 1, into state 2 — never comes back
        // through it. Ours is `runs`, which counts the same thing (quitting
        // to the menu played the logo and the trailer all over again).
        // `--skip-intro` is ours, for anybody who has seen them. **A replay skips them too** (ours):
        // they are not on the tape — the film's loop reads no input of the tape's, and nothing in a
        // replay can press Escape — so a replay sat through both at their own pace, and changed nothing by it.
        // They play from the next frame, each frame one turn of its loop (FilmStep). The QoL `skipopening` (ours, the
        // player's own) leaves them out as `--skip-intro` does.
        if (host.Runs == 0 && !Flag("--skip-intro") && !Films.OpeningSkipped(Vitomir.Engine.World.Qol.Current) && !InputTape.Replaying)
        {
            state = State.Films;
            filmIndex = 0;
            return null;
        }
        return TitleAndMenus();
    }

    internal override RunStep Step(ulong now)
    {
        frameNs = now;
        while (true)
            switch (state)
            {
                case State.Films:
                    if (!filmShowing)
                    {
                        // The films done: the title, then the menus (or the headless still), in this frame.
                        if (filmIndex >= Films.Startup.Length) return TitleAndMenus() is { } code ? RunStep.Done(code) : RunStep.On;
                        FilmOpen(Films.Startup[filmIndex++], now);
                        continue;
                    }
                    if (FilmStep(now) is not { } open) return RunStep.On;
                    if (!open) { Finish(); return RunStep.Done(0); }
                    continue;
                case State.NewGameFilm:
                    if (FilmStep(now) is not { } stillOpen) return RunStep.On;
                    // The rest of the turn whose request played the film (until then the film was a loop inside it).
                    state = State.Menus;
                    // Closing the window during the intro means quitting, not starting a game.
                    if (stillOpen) NewGameAsked();
                    else running = false;
                    return TurnEnd(newGameDt);
                default:
                    return MenusTurn(now);
            }
    }

    // The title screen, drawn once while the engine starts: it has no timer and no
    // input, and the main menu's first frame replaces it. Then the menus' loop begins (its first turn on the next frame);
    // headless, the still is written and the run is over.
    int? TitleAndMenus()
    {
        if (windowed) Present();
        menuPass.Title = false;
        menus.OpenMainMenu();
        if (Flag("--credits")) menus.OpenCredits();
        if (host.MenuNotice is { } notice) { host.MenuNotice = null; menus.Message("Load", notice, ok: true, cancel: false); }
        Console.WriteLine($"front end: the main menu{(Flag("--credits") ? ", with the credits rolling" : "")}; saves in {SaveFolder()}");

        if (headless is not null)
        {
            menuPass.Title = Arg("--menu-open") == "title";
            MenuOpen(menus, text);
            mp.Shot(Arg("--menu-open"));
            // `--create-advance <template>` (harness): the advancement screen after Create on a new
            // character of that template, for a still.
            if (Arg("--create-advance") is { } newTemplate)
                mp.AdvanceNew?.Invoke(new Vitomir.Engine.Net.NetCharacter { Name = "Anna", Template = newTemplate, Cash = Vitomir.Engine.Rules.GameRules.Current.Items.NewNetCharacterCash },
                                      c => Console.WriteLine($"  advance: {c.Name} kept"));
            mp.Frame(menus.NowMs);
            double settle = double.TryParse(Arg("--settle"), CultureInfo.InvariantCulture, out var s) ? s : 0;
            for (double t = 0; t < settle; t += 1.0 / 30) menus.Frame((int)(t * 1000));
            MenuClicks(menus);
            menuPass.Loading = Arg("--menu-open") == "loading";
            PortraitSpare();
            Png.Write(headless, width, height, screen.RenderToImage(width, height));
            Console.WriteLine($"wrote {headless} ({width}x{height}): the front end");
            Finish();
            return 0;
        }

        state = State.Menus;
        running = true;
        // `--menu-steps` (a harness, ours): `--ui-click`'s steps on the menus, as events through SDL's own queue
        // so a recording keeps them (`--menu-click` presses the menus directly, and is not on a tape) — a recording
        // can then begin at the title screen as a player's does and go through a new game (the baseline
        // `title-new-game`). Only the first run's front end: a game begun from it has its own `--ui-click`.
        menuScripted = host.Runs == 0 ? host.Script(Arg("--menu-steps"), null, ScriptedKeys.Keys) : null;
        InputTape.Window();                                                 // the window's size on the tape; a replay's put to it
        started = InputTape.Clock(host.Now());                          // a tape's clock, read as the menus begin
        last = started;
        maxFrames = int.TryParse(Arg("--frames"), out var mf) ? mf : int.MaxValue;
        frames = 0;
        if (Next()) return null;
        Finish();
        return 0;
    }

    // The top of the menus' loop, said at the end of the turn before: whether there is another turn, and the harness's
    // step for it, pushed into the host's queue before the host gathers the turn's input.
    bool Next()
    {
        if (!(running && frames++ < maxFrames && InputTape.Frame())) return false;
        menuScripted?.Pump(frames);
        return true;
    }

    // One turn of the menus' loop.
    RunStep MenusTurn(ulong now)
    {
        while (InputTape.Poll(out var e))                                        // a tape's events
        {
            if (e.Kind == LoopInputKind.Quit) { running = false; break; }
            // Alt+Enter on the menus as in a game (ours, "anywhere"; the front end's menus never read it):
            // the same toggle (FullScreenHost.Toggle), the same state written, a replay staying a window.
            if (KeyInput.FullScreenKey(e, menus.Options?.Keys is { Capturing: true })) continue;
            if (createAdvance.Up)
            {
                int aw = width, ah = height;
                if (windowed) host.PictureSize(&aw, &ah);
                if (createAdvance.Input(e, aw, ah)) continue;
            }
            MenuInput(menus, windowed, e);
        }
        ulong clock = InputTape.Clock(now);
        float dt = FrameStep.Seconds(clock - last);                 // the menus' frame too
        // A turn that came long after the last is said, with what was up (ours): a stall here holds the network too.
        if (FrameStall.Line(clock - last, () => FrameStall.Panes(menus)) is { } stall) Console.WriteLine(stall);
        last = clock;
        host.KeepOnTop();                                         // a borderless window on top while it has the focus (ours)
        ScriptedClick(menus, frames);
        menus.Frame((int)((clock - started) / 1_000_000));
        mp.Frame(menus.NowMs);
        // A network game starting: the lobby pane closes
        // and hands its session to the game run (`NetPlay`), with the network load screen up
        // over the frame while it builds.
        if (mp.TakeHandoff() is { } handoff)
        {
            NetPlay.Carried = NetPlay.From(handoff);
            menus.ClearLayers();
            menus.Show(menus.Pane(MpMenus.LoadFile));
            Present();
            host.NextRun = [.. RunSettings.Settings(args), "--mp-game", .. (handoff.LoadPath is { } saved ? new[] { "--load", saved } : [])];
            running = false;
            Finish();
            return RunStep.Done(0);
        }
        TextInput(menus, windowed);
        var (request, slot) = menus.TakeRequest();
        // The main sequence hides the main menu when it asks to start the game, from
        // the main loop; here the front-end process simply ends (`running = false`)
        // and a fresh process opens the game, which the same menu never draws into again.
        switch (request)
        {
            case MenuRequest.NewGame:
                // The music stops, and the plaque goes up over the frame that was
                // there — which is the menu you came from, because the engine draws it into
                // a back buffer it has not cleared (as in 1.1, where a
                // load from the front end plaques over the single-player menu). The menu was
                // hidden by the button, so it is put back for this one frame.
                sound.StopMusic();
                // Closing the window during the intro means quitting, not starting a game. Nor the new game's film, in a replay.
                // The film plays from this frame on, a turn of its own each frame, and the rest of this turn after it (Step).
                // The QoL `skipstoryfilms` (ours) leaves it out with the story's other films (FilmScreen.Start).
                if (!Flag("--skip-intro") && !Films.StorySkipped(Vitomir.Engine.World.Qol.Current, networked: false) && !InputTape.Replaying && FilmOpen(Films.NewGame, now))
                {
                    newGameDt = dt;
                    state = State.NewGameFilm;
                    return Step(now);
                }
                NewGameAsked();
                break;
            case MenuRequest.Restore:
                menuPass.Loading = true;
                host.NextRun = [.. RunSettings.Settings(args), "--load", SlotPath(slot), "--play"];
                running = false;
                break;
            case MenuRequest.Import:
                menuPass.Loading = true;
                host.NextRun = [.. RunSettings.Settings(args), "--import", OriginalSaves.For(files.Root).PathOf(slot), "--play"];
                running = false;
                break;
            case MenuRequest.Quit:
                running = false;
                break;
        }
        return TurnEnd(dt);
    }

    // The new game asked for (and its film over, or none): the single-player menu put back under the plaque, and the game's run next.
    void NewGameAsked()
    {
        menus.Show(menus.Pane(FrontEnd.SpFile));
        menuPass.Loading = true;
        host.NextRun = [.. RunSettings.Settings(args), "--new-game"];
        running = false;
    }

    // The end of a turn of the menus: the picture, the sound, and the top of the next turn (or the end of the run).
    RunStep TurnEnd(float dt)
    {
        Present();
        sound.Frame(dt, menus.NowMs, Vector3.Zero, Vector3.UnitY, Vector3.UnitX, -1);
        if (speakers.Live) speakers.Pump(sound.Mixer);
        else tape?.Render(sound.Mixer, dt);
        if (Next()) return RunStep.On;
        Finish();
        return RunStep.Done(0);
    }

    // The portrait's renderer is made at the model's first showing, with the spare light styles it used to draw
    // itself from the lights stream: drawn here, in the frame it is made.
    void PortraitSpare()
    {
        if (portraitPass.WantsSpare) portraitPass.Styles = Vitomir.Engine.World.LightStyles.DrawTheRenderersSpare();
    }

    void Present()
    {
        host.KeepFrame();                                          // the grip, and a dragged size put back (ours)
        cursorPass.X = menus.PointerX;
        cursorPass.Y = menus.PointerY;
        var picture = host.Acquire();
        if (picture.Acquired)
        {
            PortraitSpare();
            picture.Draw(screen);
        }
        host.Submit(picture);
        menus.Drawn();
    }

    void Finish()
    {
        if (windowed) { host.TextInput(false); host.Typing = false; if (host.NextRun is null) host.ShowCursor(true); }
        films.Dispose();
        speakers.Dispose();
        tape?.Dispose();
        uiOut.Dispose();
        uiOut.DisposeInserts();
    }

    // ---- a film (PlayFilm's loop, a turn a frame) ----------------------------------------------------------------------

    // One film, start to finish or until the player says otherwise. **Escape or Space ends
    // it**, as 1.1's film state does; no other key and no click. It ends false
    // when the window was closed during it, so the caller can stop rather than start the
    // next one.
    //
    // The picture is pulled from FFmpeg by elapsed time and the sound goes through the mixer
    // as an ordinary streaming channel, so the two keep together by both following the
    // clock. A machine that cannot decode fast enough drops frames; it never slows the film
    // down, because the sound will not wait.
    bool filmShowing;
    string filmName = "";
    VideoClip? clip;
    int filmChannel;
    IFrameLayer? filmBehind;
    ulong filmBegin, filmPrevious;
    bool filmSkipped;

    // The film opened (true), or skipped at once (false: no ffmpeg, no window, not in the install, or ffmpeg would not open it).
    bool FilmOpen(string name, ulong now)
    {
        if (ffmpeg is null || !windowed) return false;
        if (Films.Find(files, name) is not { } path)
        {
            Console.WriteLine($"video: no {name} in the install, so it is skipped");
            return false;
        }
        clip = VideoClip.Open(ffmpeg, path);
        if (clip is null) { Console.WriteLine($"video: ffmpeg would not open {name}, so it is skipped"); return false; }
        Console.WriteLine($"video: {name} — {clip.Info.Width}x{clip.Info.Height}, " +
                          $"{clip.Info.Fps:0.##} fps, {clip.Info.Seconds:0.#} s" +
                          $"{(clip.Info.HasSound ? "" : ", silent")}");

        filmName = name;
        filmChannel = clip.Sound() is { } track ? sound.Mixer.Play2D(track, 1f, 0.5f, 0) : 0;
        filmBehind = screen.After;
        screen.After = filmsDrawn;
        films.Enabled = true;
        // At its own size, its top edge on line 25 — or on line 0 for the logo, which 1.1
        // opens with the flag clear (`StoryFilm`).
        films.Top = name.Equals(Films.Startup[0], StringComparison.OrdinalIgnoreCase) ? StoryFilm.UnflaggedTop : StoryFilm.FlaggedTop;
        filmBegin = filmPrevious = now;                            // the frame's time (the clock read as it opened until then)
        filmSkipped = false;
        filmShowing = true;
        return true;
    }

    // One turn of the film's loop: null while it plays on, else it is over and closed, and whether the window is still open.
    bool? FilmStep(ulong now)
    {
        bool open = true;
        while (input.Poll(out var e))                                 // straight from the host, never on a tape
        {
            if (e.Kind == LoopInputKind.Quit) { open = false; break; }
            if (KeyInput.FullScreenKey(e, capturing: false)) continue;   // Alt+Enter over the opening films too (ours)
            // Escape or Space, and nothing else (the two keys the film checks).
            if (e.Kind == LoopInputKind.KeyDown && !e.Repeat
                && e.Code is LoopScancode.Escape or LoopScancode.Space) filmSkipped = true;
        }
        if (open && !filmSkipped && clip!.Advance((int)((now - filmBegin) / 1_000_000)))
        {
            films.Show(clip);
            var picture = host.Acquire();
            if (picture.Acquired)
                picture.Draw(screen);                                // the film alone: no portrait drawn (screen.After)
            host.Submit(picture);
            float dt = FrameStep.Seconds(now - filmPrevious);          // the film's frame too
            filmPrevious = now;
            sound.Frame(dt, (int)((now - filmBegin) / 1_000_000), Vector3.Zero, Vector3.UnitY, Vector3.UnitX, -1);
            if (speakers.Live) speakers.Pump(sound.Mixer);
            else tape?.Render(sound.Mixer, dt);
            return null;
        }
        if (filmChannel != 0) sound.Mixer.Stop(filmChannel);
        films.Enabled = false;
        films.Release();
        screen.After = filmBehind;
        Console.WriteLine($"video: {filmName} {(filmSkipped ? "skipped" : "ended")} after " +
                          $"{(host.Now() - filmBegin) / 1_000_000_000.0:0.#} s");
        clip!.Dispose();
        clip = null;
        filmShowing = false;
        return open;
    }
}
