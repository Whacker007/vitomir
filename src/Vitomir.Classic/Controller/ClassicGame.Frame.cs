// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game run's frames: the loop, a frame's step, its sound, the menus' requests, the end of the run.
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
    // The game's loop begins (what Play did before its loop, in the frame the run started in; each turn of the loop is a
    // frame of its own since, Step).
    int? PlayBegin()
    {

        last = host.Now();
        fpsStart = last;

        running = true; looking = false;
        // Where the pointer was when a swivel started, and how many absolute motions to ignore after
        // it ends. See the right-button cases below.
        lookAnchorX = 0; lookAnchorY = 0;
        swallowMotion = 0;
        // Whether the right button is turning the free camera (the harness's, with no player).
        rightTurns = false;
        // The swivel's hold kept honest across a cutscene, and the swivel lock (ours, Engine.Ui.SwivelHold).
        swivelHold = new SwivelHold();
        // Its centred look (ours, Engine.Ui.CentreLook): where the pointer is held while it is locked, and what opened.
        centreLook = new CentreLook();
        lockAt = null;
        // This frame's mouse movement, where the pointer
        // last was in the window and whether it has left it, and whether it was against the edge of
        // the screen last frame — all for the game camera's own controls.
        frameMouseX = 0; frameMouseY = 0; pointerX = 0; pointerY = 0;
        winW = width; winH = height;
        atEdge = false; pointerOut = false;
        pointerHold = new Vitomir.Engine.Ui.PointerHold();
        // QoL `winkey` and `alttab`: the system keys held while the window has the
        // focus, and Alt+Tab let through. Applied when either switch changes (the Fixes page).
        systemKeys = null;
        // The held-button walk (`Rpg.MoveOrder`) asks what walkable ground is under the
        // pointer while the left button is down on the world.
        Vitomir.Engine.Rpg.MoveOrder.Pointer = _ => HeldGround();
        maxFrames = int.TryParse(Arg("--frames"), out var mf) ? mf : int.MaxValue; frames = 0; fpsFrames = 0;
        // `--menu-open` with a window: that menu up from the first frame (a smoke test's start).
        if (Arg("--menu-open") is not null) MenuOpen(menus, strings);
        scripted = host.Script(Arg("--ui-click"), Arg("--ui-type"), ScriptedKeys.Keys);   // harness: clicks through SDL's own queue
        if (scripted is not null)                                                       // `@template` and `shot:` steps
        {
            scripted.Aim = template => AimAt(template);
            scripted.Shoot = path => Png.Write(path, width, height, Screenshots.Rgba(Still(net?.View?.Frame ?? frame, width, height), gpu.ColourIsBgra));   // the place the window shows (StoryView)
        }
        // `--frame-ms <n>` (a harness): every windowed frame steps the game n ms, whatever the
        // wall clock did — a scripted run at a player's sixty frames a second on a machine that draws ten.
        harnessStep = float.TryParse(Arg("--frame-ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var stepMs) ? stepMs / 1000f : 0f;
        // `--console-run` with a window: its lines once the place is up, as if typed then.
        consoleRun = ConsoleRun.From(Arg("--console-run"));
        // The host program's observers of the run (RunAdditions): the window's loop begins.
        Tell((o, run) => o.WindowOpened(run));
        // A harness's scripted or counted run is never held by the window's focus (ours).
        windowActive = new Vitomir.Engine.Ui.WindowActive();
        interactive = InputTape.Interactive(scripted is null && consoleRun is null && maxFrames == int.MaxValue && harnessStep == 0f);
        // Each frame's phases timed, and a slow one logged with where its time went (SlowFrames).
        slowFrames = new Vitomir.Engine.Diagnostics.SlowFrames("input", "wait", "step+draw", "submit", "menus", "sound", "camera", "requests", "tape", "title");
        texturesBefore = frame.World.TexturesMade; rebuildsBefore = frame.World.Rebuilds;   // the slow frames' detail
        readsBefore = Vitomir.Engine.Vfs.GameFiles.Reads; readTicksBefore = Vitomir.Engine.Vfs.GameFiles.ReadTicks;   // and the files read
        servedBefore = Vitomir.Engine.Vfs.GameFiles.Served;                                                       // or read ahead
        frameLog = FrameLogHost.Open(Arg("--frame-log"), () => frame);   // `--frame-log <file.csv>`: a row a second (ours)
        // `--frame-times <file.tsv>`: a row for every frame (ours).
        frameTimes = Vitomir.Engine.Diagnostics.FrameTimes.Open(Arg("--frame-times"), slowFrames.Phases, Console.WriteLine);
        // `--frame-dump <folder>` with a window, a replay of a tape above all: the run's pictures at a steady rate of its
        // clock, and with `--frame-dump-log <file.jsonl>` what was pressed and where the camera was at each, for the web
        // site's clips (ours, a harness: FrameDump, FrameDumpLog, DumpFrame below).
        playDump = FrameDump.From(Arg("--frame-dump"), Arg("--frame-dump-fps"));
        playDumpLog = playDump is null ? null : Vitomir.Engine.Diagnostics.FrameDumpLog.Open(Arg("--frame-dump-log"));
        if (playDump is not null) host.KeepPictures();   // every frame through the picture's own target, to be read back
        dumpWheel = 0; dumpClicks = 0;
        if (Next()) return null;
        return PlayEnd();
    }

    /// <summary>
    /// **One frame of the game** (a turn of its loop): the frame, then the top of the next — or the end of the run. What the
    /// host waits before the next frame is 1.1's: 10 ms behind a window without the focus, and the console's
    /// <c>maxfps</c> held from this frame's clock.
    /// </summary>
    internal override RunStep Step(ulong now)
    {
        frameNs = now;
        idleUntil = 0;
        Frame();
        if (!Next()) return RunStep.Done(PlayEnd());
        ulong at = idleUntil;
        // The console's `maxfps`: the next frame waited out to its length from this one's clock, as 1.1's main loop ends
        // 1.1's (the host's wait). Not a film's frames, which 1.1's state 1 runs at its own pace.
        if (!filmScreen.Playing && Vitomir.Engine.Ui.ConsoleCommands.FrameCapMs is > 0 and var capMs) at = Math.Max(at, last + (ulong)capMs * 1_000_000UL);
        return RunStep.After(at);
    }

    /// <summary>The frames' limit (<c>--frames</c>), and the host clock the next frame waits for behind a window without the focus.</summary>
    int maxFrames;
    ulong idleUntil;

    // The top of the game's loop, said at the end of the frame before: whether there is another frame (a replay ends with its
    // tape), and what the old loop did at the top of a frame before it read the frame's input — the window's keeping, the
    // harness's camera, its scripted step (pushed into the host's queue before the host gathers the frame's input) and its
    // console lines.
    bool Next()
    {
        if (!(running && frames++ < maxFrames && InputTape.Frame())) return false;
        slowFrames.Begin();
        host.KeepPlace();                                              // the window's place written once it stays put (ours)
        host.KeepFrame();                                              // the grip, and a dragged size put back (ours)
        WorldFrame.WorldSize = host.WorldSize();                       // borderless full screen's own size for the world (ours)
        // `--camera-turn <degrees>` (a harness switch, ours): the game camera swivelled round its subject, as
        // holding X and moving the mouse does, a second in — after a place's opening shot has handed
        // the camera back — for a picture of the character from the front.
        if (frames == 30 && worldCam.Mode == CameraMode.Game && Arg("--camera-turn") is { } turn) worldCam.Swivel(-2f * F(turn), 0f);
        // `--camera-yaw <deg>` and `--camera-pitch <deg>` (ours): the game camera's own angles set outright at the same
        // moment, in 1.1's own terms (its yaw and pitch, in degrees), for a picture from 1.1's viewpoint.
        if (frames == 30 && worldCam.Mode == CameraMode.Game)
        {
            if (Arg("--camera-yaw") is { } cy) worldCam.Angles.Z = F(cy);
            if (Arg("--camera-pitch") is { } cp) worldCam.Angles.X = F(cp);
            // And `--camera-zoom <scale>` (ours): the zoom set outright, eased out to as the keys would leave it.
            if (Arg("--camera-zoom") is { } cz) { worldCam.ZoomScale = WorldCamera.ZoomMin; worldCam.Zoom(F(cz) - WorldCamera.ZoomMin); }
        }
        scripted?.Pump(frames);
        consoleRun?.Pump(frames, filmScreen.Playing, menus.DevConsole);
        return true;
    }

    // The game's loop over (what Play did after its loop): its counts, the last frame's still, the save asked for, the run
    // torn down.
    int PlayEnd()
    {
        frameLog?.Dispose();
        slowFrames.Summary(location.Name);
        Console.WriteLine($"{frames - 1} frames");
        // `--shot <png>`: the last frame of a windowed run (with `--frames`), menus and all (harness).
        if (Arg("--shot") is { } lastShot) Png.Write(lastShot, width, height, Screenshots.Rgba(Still(net?.View?.Frame ?? frame, width, height), gpu.ColourIsBgra));
        if (playDump is not null) Console.WriteLine($"frame dump: {playDump.Written} pictures at {playDump.Fps:0.##} a second");
        playDumpLog?.Flush();   // not closed: a run after a load (in the same process) carries the log on
        SaveAsAsked();
        Shutdown();
        frameTimes?.Dispose();
        Tell((o, run) => o.WindowClosed(run));
        return 0;
    }

    // A frame of the game, from its input on (the top of the old frame, up to its scripted step and console lines, is said
    // at the end of the frame before, Next).
    void Frame()
    {
        // **A film holds the screen** (state 1): it is the whole frame and nothing of
        // the game's runs — no world, no interface, no input but its own Escape and Space.
        if (filmScreen.Playing)
        {
            running = filmScreen.Frame(frameNs);
            // **The game's clock runs through a film** (it is stepped every frame whatever the state;
            // only Pause holds it): state 1 updates no world, but when the film ends every deadline on the
            // clock — an effect's end, a busy time, a script's timer, a mind's next think — is that much
            // nearer or past. As in 1.1, where Stephansdom's `Sunrise.bik` takes
            // 22.0 s of game clock over 23.2 s of film, not a thing moving. So the time is carried and the
            // place's clock put forward by it as the film ends, each frame's share held to 1.1's 1 ms..1 s
            // (`FrameStep`); nothing is stepped through it.
            ulong filmNow = frameNs;
            filmClock += FrameStep.Seconds(filmNow - last);
            last = filmNow;
            if (!filmScreen.Playing && filmClock > 0f)
            {
                frame.Sim.Resume(frame.Sim.Seconds + filmClock);
                filmClock = 0f;
            }
            return;
        }
        // Every event through the key bindings first (`KeyInput`): with 1.1's keys, as it came.
        while (KeyInput.Poll(out var e, menus)) Hear(e);
        // Behind an inactive window a single-player game runs no frame at all (WindowActive).
        // The host waits 10 ms before the next (the loop's own SDL_Delay until then).
        if (interactive && !windowActive.Runs(net is not null)) { frames--; frameLog?.Away(); idleUntil = frameNs + 10_000_000UL; return; }
        SeatFrame();
        // The console's `maxfps` is the host's wait before this frame (Step), so the frame's clock is past it.
        ulong now = frameNs;
        float dt = FrameStep.Seconds(now - last);                     // 1 ms to 1 s, 1.1's bounds
        if (harnessStep > 0f) dt = harnessStep;                       // `--frame-ms`: a harness's fixed step
        dt = InputTape.Step(dt);                                      // a tape's step: written, or the recorded one
        last = now;

        // **Changing places**, once a frame and before anything this frame reads the world. The
        // engine's own state 4 is consumed in exactly this position — after the scripts have run
        // and before the next draw — because a script asks to leave in the middle of a click and
        // the world must not change underneath the rest of that click.
        // The main sequence's request, once a frame, before anything this frame reads the world.
        AutoAnswer();
        Sequence();
        net?.Frame(dt, frame.Sim.Milliseconds);
        // The host moved the game: this run ends and the next loads the new place, the session carried.
        if (net?.WantsReload == true) { NetPlay.Carried = net; host.NextRun = [.. RunSettings.Settings(args), "--mp-game", .. NetPlaces.StillArgs(args)]; running = false; }
        // Mirror what the conversation has on offer; it steps inside the world's own tick
        // (`Simulation.Talk`). `Offer` is idempotent so this does not eat the row the mouse is on.
        // A list going up brings the pointer with it.
        if (standaloneUntil != 0 && talk.Current is not { Running: true } && frame.Sim.Milliseconds >= standaloneUntil)
        {
            talkPass.Pane!.Reset();
            standaloneUntil = 0;
        }
        bool offering = talk.Current is { Running: true, Waiting: true };
        if (offering && !talkPass.Pane!.Offering) worldCam.CursorShown = true;
        talkPass.Pane!.Offer(offering ? talk.Current!.Offered.Select(a => a.Words) : []);
        // The hover glow: what the pointer settled on last frame, brightening or fading.
        glow.Point(hovering, player?.Thing);
        // Ours: the show-objects key held lights every object on screen (`Engine.Ui.ShowObjects`, `KeyBindingsShow.cs`).
        glow.Show(KeyInput.Held(KeyCommand.ShowObjects) && windowActive.Active && !menus.Up && !worldCam.StatusBarHidden
                  && player is not null && frame.ViewProjection is { } shownView
            ? Vitomir.Engine.Ui.ShowObjects.Find(location.Things, player.Thing, shownView, location.Particles.View) : null);
        glow.Step(dt);
        bool[] keys = KeyInput.State();
        bool Down(int s) => keys[s];

        // **The game camera's own controls**,
        // in the engine's order. The turn rates are zeroed first — unless the pointer was at the
        // edge of the screen, which is what keeps an edge push turning — then the keys
        // and the edge add to them, and the camera's own tick inside the world step spends them.
        float mdx = frameMouseX, mdy = frameMouseY;
        frameMouseX = frameMouseY = 0f;
        worldCam.MouseDx = mdx;
        worldCam.MouseDy = mdy;
        bool lookHeld = Down(LoopScancode.Z);   // the fourth mouse button too, through the bindings
        // A shop's state 4 runs none of the world's input — no camera keys, no edge turn —
        // and nor does anything while a menu has the screen or the game is paused.
        bool frozen = menus.Paused || menus.Up;
        // **Except the camera under the Pause key**: the key stops the game's clock, not
        // the key handler, so the keypad and the screen edge still turn the view — by the interface's
        // clock (`WorldCamera.InterfaceTick`) — and only the zoom keys, on the game's, stand still.
        bool cameraKeys = !menus.Up && advancement is null && net?.Paused != true;
        if (!cameraKeys) worldCam.StopTurning();        // vui's frame puts the rates back regardless
        float zoomDt = frozen ? 0f : dt;                // the zoom keys step by the game's clock
        while (KeyInput.TakeAct() is { } act) OurKey(act);
        swivelHold.Relock = qol.Relock;                 // the QoL `relock`: a menu or a pane sets the lock aside, not off
        Swivel(swivelHold.Frame(SwivelCamera(), worldCam.Mode == CameraMode.Look, looking,
                                KeyInput.Held(KeyCommand.Swivel) || qol.Camera && KeyInput.Held(KeyCommand.DragSwivel),
                                aside: follow && worldCam.Mode == CameraMode.Game));
        if (follow && player is not null && !buyPass.Enabled && cameraKeys)
        {
            if (!atEdge) worldCam.StopTurning();
            atEdge = false;
            if (worldCam.Mode == CameraMode.Game)
            {
                // Numpad 4 and 6 turn, 8 and 2 tilt, at 180° a second. The
                // arrows are not the game's — DirectInput tells them apart from the keypad — and
                // do the same here only under `[qol] camera`.
                bool arrows = qol.Camera;
                if (Down(LoopScancode.Keypad6) || arrows && Down(LoopScancode.Right)) worldCam.TurnYaw(-180f);
                if (Down(LoopScancode.Keypad4) || arrows && Down(LoopScancode.Left)) worldCam.TurnYaw(180f);
                if (Down(LoopScancode.Keypad8) || arrows && Down(LoopScancode.Up)) worldCam.TurnPitch(-180f);
                if (Down(LoopScancode.Keypad2) || arrows && Down(LoopScancode.Down)) worldCam.TurnPitch(180f);
                // − backs off, = comes in; 1.1's keypad − and + are the quick load's and
                // save's by default and reach here as − and = when bound to the zoom (KeyBindings.KeypadZoom).
                if (Down(LoopScancode.Minus)) worldCam.Zoom(zoomDt * 0.5f);
                if (Down(LoopScancode.EqualsKey)) worldCam.Zoom(-zoomDt * 0.5f);
                // Z held — or the fourth mouse button — looks out of the character's eyes.
                if (lookHeld && !readPass.Enabled && worldCam.EnterLook())     // not under a dialog
                {
                    StartSwivel();
                    Console.WriteLine("  camera: first person (hold Z)");
                }
                // The pointer against the edge of the screen turns the camera — not
                // while the swivel has it pinned.
                else if (!looking && !pointerOut)
                    atEdge = worldCam.EdgeTurn((int)(pointerX * 640f / Math.Max(1, winW)), (int)(pointerY * 480f / Math.Max(1, winH)),
                                               mdx, mdy, GameOptions.Current.Has(GameOptions.MousePitch));
            }
            // Letting go of Z, or dying with it held, is the way back.
            else if (worldCam.Mode == CameraMode.Look && (!lookHeld || player.Thing.Actor?.Dead == true))
            {
                worldCam.BackToGame(player.Thing);
                StopSwivel();
            }
        }
        // The pointer kept in the window, and held at its edge while it turns the view, as 1.1's
        // own pointer is — `Engine.Ui.PointerHold`.
        if (windowed && (Qol.Current.WinKey, Qol.Current.AltTab) is var keysWanted && keysWanted != systemKeys)
        {
            host.SystemKeys(keysWanted.WinKey, keysWanted.AltTab);   // SDL holds the keyboard only while the window has the focus
            systemKeys = keysWanted;
        }
        if (windowed && pointerHold.Frame(follow && player is not null && !buyPass.Enabled && cameraKeys
                && worldCam.Mode == CameraMode.Game, atEdge, looking) is var hold)
        {
            if (hold.Confine is { } grab) host.ConfineMouse(grab);
            if (hold.Hold == true) host.RelativeMouse(true);
            else if (hold.Hold == false) { host.RelativeMouse(false); host.Warp(pointerX, pointerY); }
        }
        ScreenState();

        // With **no** player there is no character to steer, and the free camera's keys fly the camera
        // (`FreeCameraInput`, below); with one, they steer him, and that is ours — and only while the
        // game camera has the screen, because nothing else takes orders.
        bool steer = !frozen && player is not null && qol.Driving && worldCam.Mode == CameraMode.Game && !buyPass.Enabled;
        int ahead = !steer ? 0 : (Down(LoopScancode.W) ? 1 : 0) - (Down(LoopScancode.S) ? 1 : 0);
        // A and D are also the game's own keys — D the discipline pane, A the attack
        // stance — and with driving on they side-step instead; the pane keeps its status-bar
        // button.
        int side = !steer ? 0 : (Down(LoopScancode.D) ? 1 : 0) - (Down(LoopScancode.A) ? 1 : 0);
        // **The free camera's own keys**, only while it has the picture: with nobody to
        // steer (the harness's fly-through, folded in), or the game camera gone free (Ctrl+P, `FreeCameraInput`).
        FreeCameraInput.Frame(cam, worldCam, up: worldCam.Mode == CameraMode.Free && (gameCamera || player is null),
                              reading: !menus.Up && !host.TextInputActive, seconds: frozen || net?.Paused == true ? 0f : dt,
                              qol.Driving, location.Steered, mouse: new(mdx, mdy),       // Ctrl+P in: [qol] freecamera or --devmode
                              open: FreeCameraGate.Opens(Qol.Current, GameOptions.Current.Flags, net?.Client is not null, net?.Server?.Flags));
        // Its drag look pins the pointer as the swivel does.
        if (FreeCameraInput.DragChanged) { if (FreeCameraInput.Dragging) StartSwivel(); else if (!rightTurns) StopSwivel(); }
        if (player is not null)
        {
            // The character walks the way the camera is looking, which is what "forward" means in
            // a third-person game; shift runs.
            var flat = new Vector2(cam.Forward.X, cam.Forward.Y);
            if (flat.LengthSquared() > 1e-6f) flat = Vector2.Normalize(flat);
            var right = new Vector2(flat.Y, -flat.X);
            var push = flat * ahead + right * side;
            // A dead character keeps the camera but takes no orders: nothing in the engine steers
            // one, and without this the body slides about in its death pose.
            // A player's machine sends its push to the host, which walks the character there
            // (`GameClient.Drive`, ours: 1.1 has no driving); the world comes back in the snapshots.
            if (!frozen && net?.View?.Drive(push, AlwaysRunOption.Runs(KeyInput.Held(KeyCommand.Run), GameOptions.Current), dt) == true) { }   // the carried body's (StoryView)
            // And moves her here at once with the same walk (Engine.Net.OwnWalk.Drive), the host's snapshots correcting her; a
            // menu over the world lets go of the push (her machine stands her; the host's would walk her on until the push went stale).
            else if (net?.Client is { } driver) driver.Drive(frozen ? Vector2.Zero : push, AlwaysRunOption.Runs(KeyInput.Held(KeyCommand.Run), GameOptions.Current), player, frozen ? 0f : dt);
            else if (player.Thing.Actor?.Dead == true || frozen) { }
            // Driving asks what a click asks: nothing moves while he is busy or held, and
            // pushing off cancels what he was doing — a feed lets go and plays its release first.
            else if (push.LengthSquared() > 1e-6f && !Actions.ReadyToMove(player.Thing)) player.Move(Vector2.Zero, dt);
            else if (push.LengthSquared() > 1e-6f)
            {
                player.Running = AlwaysRunOption.Runs(KeyInput.Held(KeyCommand.Run), GameOptions.Current);   // run's own keys, not the secondary flag's Shift; always run inverts them
                var stoodAt = player.Thing.Position;
                player.Move(push, dt);              // the keyboard cancels a route
                driveSnags.Pushed(location, player.Thing, stoodAt, push, dt);   // where he would not go (ours, the log's)
            }
            else if (player.Walking) { }            // the world walks a route (SteeredWalk)
            else player.Move(Vector2.Zero, dt);
        }

        // The cursor, once per frame and in the engine's own order of precedence: a refusal still
        // running, then whatever is in the actor's hand, then the thing under the pointer.
        if (hands is not null) hands.NowMs = frame.Sim.Milliseconds;
        rolloverPass.User = location.Steered;      // whom an item's name asks
        worldCam.AutoSlew = GameOptions.Current.Has(GameOptions.AutoSlew);   // the Controls screen's box
        // Whether the player is still asking for another swing, as 1.1 reads it (`SwingRepeat`).
        // The middle button is not the secondary click while it is the camera's drag (`HeldClick`), and
        // while a click is held the pick is re-run each frame as the hover runs it (`HeldPick`).
        bool leftHeld = KeyInput.HeldClick(KeyCommand.Act), middleHeld = KeyInput.HeldClick(KeyCommand.SecondaryClick);
        if (HeldPick.Wanted(leftHeld || middleHeld, menus.Up, follow && worldCam.Mode == CameraMode.Game))
            Pointer(pointerX, pointerY, winW, winH);
        CentredLook();                                  // the swivel lock's
        if (player?.Thing.Actor is { } asking && !menus.Up)
            asking.Repeating = net?.View is not null ? null   // 1.1 asks the control alone — the carried body while the view is up
                : SwingRepeat.Ask(GameOptions.Current.Has(GameOptions.RepeatAttack),
                !Vitomir.Engine.Rpg.CastInput.Refused(player.Thing), hovering, leftHeld, middleHeld, Modifiers(),
                heldShift: (player.Thing.Place?.Qol ?? Vitomir.Engine.World.Qol.Current).HeldShift);
        if (!menus.Up) net?.View?.Repeat(GameOptions.Current.Has(GameOptions.RepeatAttack),       // the carried body's (StoryView)
            leftHeld, middleHeld, Modifiers());
        // And what held automatic fire reads at an attack's `Repeat` tag (`HeldFire`): the
        // left or middle button, what the pointer rests on, and the floor under it.
        if (location.Steered?.Actor is { } pointing)
            pointing.Pointing = menus.Up ? null : new HeldFire.Pointer(leftHeld || middleHeld, hovering, pointerGround);
        // The large description box arms itself 1.5 s into a hover, so it needs the clock.
        rolloverPass.NowMs = frame.Sim.Milliseconds;
        // Fix `liverollover`: a control's lines rebuilt every frame while the pointer rests,
        // not only when it moves (the world hover's are the pass's own, `WorldHover`).
        if (Fixes.Current.LiveRollover && !menus.Up)
        {
            var (lx, ly) = StatusBar.FromWindow(pointerX, pointerY, winW, winH);
            var (plx, ply) = StatusBar.PaneFromWindow(pointerX, pointerY, winW, winH);
            rolloverPass.Lines = Vitomir.Engine.Ui.LiveRollover.Keep(rolloverPass.Lines, PowerRollover(lx, ly, plx, ply));
        }
        // The purse is the party's; the pack shows it — in a network game the steered character's
        // own. And the bar's controls blink by the frame.
        invPass.Pane!.Gold = net is not null && location.Steered is { } purseOf ? Vitomir.Engine.Rpg.Shops.CashOf(purseOf) : session.Gold;
        // Opening a shop let go of the first-person look; the swivel's hold on
        // the mouse goes with it.
        if ((buyPass.Enabled || readPass.Enabled) && looking) StopSwivel();      // and a document's
        // A new party member picked while the shop is up becomes the trader and the shelf is priced
        // again.
        if (session.Shops.Active is { } trading && !ReferenceEquals(trading.Trader, location.Steered))
            session.Shops.SwitchTrader(location.Steered);
        foreach (var control in hud.Bar.Controls) control.TickBlink(frame.Sim.Milliseconds, (int)(dt * 1000f));
        // From the same per-frame interface walk as the pointer.
        feedBar.Update(worldCam.Mode == CameraMode.Game ? location.Steered : null, hud.Enabled, audio);
        cursor.Update(frame.Sim.Milliseconds, location.Steered?.Actor?.Held,
                      net?.View?.Hovered is { } viewed ? Vitomir.Engine.Ui.Highlight.Cursor(viewed, net.View.Body)    // (StoryView)
                      : Vitomir.Engine.Ui.Highlight.Cursor(hovering, location.Steered));
        // The menus' frame (the interface's own walk): their hover, the caret, the
        // credits. Over a menu the pointer is the plain arrow, shown whatever the camera says.
        ScriptedClick(menus, frames);
        menus.Frame((int)(uiClock * 1000));
        TextInput(menus, windowed);
        if (menus.Up)
        {
            cursor.Set(Vitomir.Engine.Ui.CursorId.Default);
            cursorPass.X = menus.PointerX;
            cursorPass.Y = menus.PointerY;
            cursorPass.Enabled = cursorWanted;
        }

        Tell((o, run) => o.WindowFrame(run));   // the host program's observers (RunAdditions)

        // Everybody else thinks and walks inside the frame's own step, below.
        slowFrames.Mark(0);
        var picture = host.Acquire();
        slowFrames.Mark(1);
        if (InputTape.Drawn(picture.Acquired))   // the world steps in the draw (a tape says)
        {
            // The advancement screen pauses the game: nothing moves under it.
            // So does a menu, or the Pause key: the game clock stops, the interface's does not.
            float frameSeconds = advancement is not null || menus.Paused || net?.Paused == true ? 0f
                         : FrameStep.Scaled(dt, Vitomir.Engine.Ui.ConsoleCommands.TimeScale);   // the console's `timescale`, scaled then bounded
            // With the game's clock stopped the game camera still runs on the interface's.
            if (frameSeconds == 0f && net is null) worldCam.InterfaceTick(dt);
            // The Storyteller's camera gone to another place: that place drawn, this one stepped (StoryView).
            if (!(net?.View is { } storyView && picture.Draw(storyView, cam, frameSeconds)))
            {
                // The world's step and what the frame does around its picture, then the picture (the renderer
                // only reads). The interface's clock goes on in a pause (DrawClocks).
                Prepare(frame, picture.Width, picture.Height, frameSeconds, dt);
                picture.Draw(frame, cam);
            }
        }
        slowFrames.Mark(2);
        host.Submit(picture);
        DumpFrame(dt);                                   // `--frame-dump`: the frame's picture, when one is due (a harness)
        if (host.Stopped is not null) running = false;   // the host's renderer could not draw the level (Direct3D 12): the run ends (ours)
        slowFrames.Mark(3);
        menus.Drawn();
        slowFrames.Mark(4);
        Listen(dt);
        slowFrames.Mark(5);
        CameraPull();
        slowFrames.Mark(6);
        MenuRequests();
        slowFrames.Mark(7);
        InputTape.FrameEnd(location, frame.Sim.Milliseconds, () => scenes?.ReplayDigest());   // the frame's digest
        slowFrames.Mark(8);
        if (++fpsFrames == 120)
        {
            double secs = (frameNs - fpsStart) / 1e9;
            host.Title($"Vitomir — {levelName} — {fpsFrames / secs:0} fps");
            fpsStart = frameNs; fpsFrames = 0;
        }
        slowFrames.Mark(9);
        // The step's own share of step+draw, and what the draw made for the first time.
        // (A journey builds a new renderer, whose counts start again from nothing.)
        int texturesNow = frame.World.TexturesMade, rebuildsNow = frame.World.Rebuilds;
        int texturesMade = texturesNow >= texturesBefore ? texturesNow - texturesBefore : texturesNow;
        int rebuilt = rebuildsNow >= rebuildsBefore ? rebuildsNow - rebuildsBefore : rebuildsNow;
        long readsNow = Vitomir.Engine.Vfs.GameFiles.Reads, readTicksNow = Vitomir.Engine.Vfs.GameFiles.ReadTicks;
        long readsMade = readsNow - readsBefore, servedNow = Vitomir.Engine.Vfs.GameFiles.Served, served = servedNow - servedBefore;
        double readMs = (readTicksNow - readTicksBefore) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        slowFrames.End(frames, location.Name, () =>
            $"of step+draw the step {frame.LastStepMs:0.0}, the draw: animated {frame.LastDrawMs.Animated:0.0} ({frame.World.LastAnimated.Posed} posed, {frame.World.LastAnimated.Passed} out of view), shadows {frame.LastDrawMs.Shadows:0.0}, mirrors {frame.LastDrawMs.Mirrors:0.0}, world {frame.LastDrawMs.World:0.0}, interface {frame.LastDrawMs.Interface:0.0}; textures made {texturesMade}, buffers rebuilt {rebuilt}; files read {readsMade} ({readMs:0.0} ms), {served} from the cache");
        frameTimes?.Frame(frames, location.Name, slowFrames, new Vitomir.Engine.Diagnostics.FrameTimes.Detail(
            frame.Sim.Milliseconds, frame.LastStepMs, frame.LastDrawMs.Animated, frame.LastDrawMs.Shadows, frame.LastDrawMs.Mirrors,
            frame.LastDrawMs.World, frame.LastDrawMs.Interface, frame.World.LastAnimated.Posed, frame.World.LastAnimated.Passed,
            location.Things.Count, location.Minds?.All.Length ?? 0, texturesMade, rebuilt, readsMade, readMs, windowActive.Active));
        (texturesBefore, rebuildsBefore, readsBefore, readTicksBefore, servedBefore) = (texturesNow, rebuildsNow, readsNow, readTicksNow, servedNow);
        audio.Prefetch?.Report();                                          // what the read-ahead finished, logged on this thread
        frameLog?.Frame(windowActive.Active);                              // the frame's time into this second's row (ours), and the window's focus
    }

    // **Where a sheet shorter than the picture sits this frame** (ours, UiLayout.Seat, InGame): centred
    // on black while a screen that is the whole sheet is up (the menus' own fact, FrontEnd.WholeSheet), at the bottom
    // wherever the world shows. **Once a frame, after the frame's input and before anything of the frame maps the
    // pointer or draws**: the events just heard were made on the last picture and were mapped with the seat it was drawn
    // with; everything from here to the end of the frame, its picture included, is in this one; so are the next frame's
    // events. A screen opened later in the frame (the menus' request after the draw: the save and load screens) is
    // seated by the next frame, before it is first drawn. A replay hears the same events on the same frames and so says
    // the same seat. Where the map moves under a pointer that has not, the menus are told where it now is.
    void SeatFrame()
    {
        int w = width, h = height;
        if (windowed) host.PictureSize(&w, &h);
        var was = UiLayout.On(w, h);
        UiLayout.Seat = UiLayout.InGame(menus.WholeSheet);
        // And the bar the panes stand above (UiLayout.BarTop, Panes): the advancement screen's own
        // is taller than the status bar, and its panes are centred in the rows above it.
        UiLayout.BarTop = advancement is not null ? advBarPass.Pane.Quads.Max(q => (float)(q.Pos[1] + q.Size[1])) : UiLayout.PaneBottom;
        if (menuPixel is not { } at || UiLayout.On(w, h) == was) return;
        var (ux, uy) = StatusBar.FromWindow(at.X, at.Y, w, h);
        menus.PointerAt(ux, uy);
    }

    // The pointer as the menus were last told it, in the space of the panes above the bar.
    (int X, int Y) PanePointer() => menuPixel is { } at ? StatusBar.PaneFromWindow(at.X, at.Y, winW, winH) : ((int)menus.PointerX, (int)menus.PointerY);

    void ChapterTitle()
    {
        if (readPass.Enabled) return;                                       // not with a book open
        if (Vitomir.Engine.Ui.Captions.ChapterTitle(location.ChapterScene is not null ? location.SceneFile : null,
                key => strings[key], (font, text) => UiCanvas.Measure(ui.Assets.Font(font), text),
                captionPass.NowMs) is { } title)
        {
            captions.Add(title);
            Console.WriteLine($"  title: {title.Text}");
        }
    }


    // **What a frame does to the game before its picture**: the
    // renderer only reads, so whoever draws runs these first — the world's step and what the old renderer did around
    // it (WorldFrame.Frame: the walk, the mirrors, the sky's and the ring's clocks, the poses), then the party heads'
    // own pass (HeadModels.Frame: before anything is drawn). `shown` is the renderer about to draw: the
    // window's own, or the Storyteller's view of another place (StoryView, which steps nothing of its own).
    void Prepare(PlaceFrame shown, int w, int h, float seconds, float? interfaceSeconds)
    {
        shown.Game.Frame(cam, w, h, seconds, interfaceSeconds);
        HeadsBeforeDraw(shown);
    }

    void HeadsBeforeDraw(PlaceFrame shown) => headsPass.Models.Frame(headsPass.Enabled, shown.Sim);

    // `--frame-dump` in the window's loop (ours, a harness): when the dump's clock says a picture is due, the frame the
    // screen just got read back and written (nothing drawn again, so a replay checks the same with a dump or without), and
    // with `--frame-dump-log` its line (FrameDumpEntry): the keys and buttons down, the clicks and the wheel since the last picture, the
    // pointer, the camera's angles, zoom and limits, the swivel lock. The frame's own step, not the game's clock, sets the
    // pace, so a menu or a pause is filmed as it was seen.
    void DumpFrame(float dt)
    {
        if (playDump is null || !playDump.Due(dt)) return;
        if (host.LastPicture(out int pw, out int ph) is not { } pixels) return;
        int n = playDump.Written;
        playDump.Write(pixels, pw, ph);
        if (playDumpLog is null) { dumpWheel = 0; dumpClicks = 0; return; }
        var raw = InputTape.Keyboard();
        var keys = new List<int>();
        for (int i = 0; i < raw.Length; i++) if (raw[i] || KeyInput.HarnessHeld.Contains(i)) keys.Add(i);
        var held = new List<string>();
        // The commands read while held (the camera's, driving's, the flags, the clicks, and the two swivels held down); a
        // command that acts on its press is in `pressed` instead, so a key a driving player's A shares with the attack
        // stance is not shown as that too.
        foreach (var info in KeyBindings.All)
        {
            bool heldKind = info.Kind is CommandKind.Held or CommandKind.Flag or CommandKind.Click
                            || info.Command is KeyCommand.Swivel or KeyCommand.DragSwivel;
            if (heldKind && (info.Kind == CommandKind.Click ? KeyInput.HeldClick(info.Command) : KeyInput.Held(info.Command)))
                held.Add(info.Command.ToString());
        }
        string[] pressed = [.. KeyInput.Pressed.Select(c => c.ToString())];
        KeyInput.Pressed.Clear();
        static float S(float d) => Vitomir.Engine.Diagnostics.FrameDumpEntry.Signed(d);
        playDumpLog.Write(new Vitomir.Engine.Diagnostics.FrameDumpEntry(
            n, frames, frame.Sim.Milliseconds, pw, ph, [.. keys], [.. held], pressed, InputTape.Buttons(out _, out _), dumpClicks, dumpWheel,
            pointerX, pointerY, winW, winH, cursorPass.Enabled,
            worldCam.Mode.ToString(), S(worldCam.Angles.Z), S(worldCam.Angles.X), S(worldCam.PitchLow), S(worldCam.PitchTop),
            worldCam.ZoomScale, worldCam.ZoomOut, swivelHold.Locked, looking,
            qol.Driving && player is not null && worldCam.Mode == CameraMode.Game && !menus.Up, menus.Up));
        dumpWheel = 0; dumpClicks = 0;
    }

    // A still: the frame's game side with no time (as the renderer's own still did until then), then its picture.
    byte[] Still(PlaceFrame shown, int w, int h)
    {
        Prepare(shown, w, h, 0f, null);
        return shown.RenderToImage(w, h, cam);
    }

    // The keys of ours the host acts on itself (KeyBindings.Acted): the swivel lock, and the quick
    // save and load on the escape menu's own terms (Engine.Ui.QuickSave).
    void OurKey(KeyCommand act)
    {
        if (act == KeyCommand.SwivelLock)
        {
            Swivel(swivelHold.Toggle(SwivelCamera() && (swivelHold.Locked || Pick.View.Drawn)));   // not with no world to look at
            Console.WriteLine($"  keys: swivel lock {(swivelHold.Locked ? "on" : "off")}");
            return;
        }
        if (act is not (KeyCommand.QuickSave or KeyCommand.QuickLoad)) return;
        bool escapeOpens = (worldCam.Mode == CameraMode.Game && !vaultPass.Enabled || worldCam.Mode == CameraMode.Free)
                           && talk.Current is null && !readPass.Enabled && !buyPass.Enabled && advancement is null && !menus.Up;
        var did = QuickSave.Decide(act, escapeOpens, NetPlay.Active, net?.IsHost == true, s => menus.SaveSlots?.Exists(s) == true);
        Console.WriteLine($"  keys: {KeyBindings.Of(act).Name}: {did}");
        int slot = QuickSave.Slot(NetPlay.Active) ?? 0;
        switch (did)
        {
            case QuickAct.Save:
                // As the save screen's: "Game saved." first, whatever becomes of the save; the name the box would offer.
                session.Messages.Add(strings.Find("SPLOADSAVE_GAMESAVED") ?? "Game saved.");
                SaveNow(SlotPath(slot), plain: false, menus.Describe?.Invoke());
                break;
            case QuickAct.Load: StopSwivel(); QuickLoadConfirm.Load(menus, slot, GameOptions.Current, strings.Find); break;   // MenuRequests loads it, as the load screen's; asked first under the option
            case QuickAct.SaveScreen: StopSwivel(); menus.Ask(MenuRequest.SaveMenu); break;
            case QuickAct.LoadScreen: StopSwivel(); menus.Ask(MenuRequest.LoadMenu); break;
        }
    }


    // The listener: distance from the steered character, left and right from the
    // camera's own facing; then the frame's channel pass, and the speakers (or the tape) fed.
    void Listen(float seconds)
    {
        // The interface's own clock, which the captions fade on: it
        // runs whichever place the party is in, where each place's world clock is its own.
        uiClock += seconds;
        captionPass.NowMs = (int)(uiClock * 1000f);
        var heard = location.Steered;       // 1.1 listens from the steered character
        audio.Frame(seconds, frame.Sim.Milliseconds, heard?.Position ?? cam.Position, cam.Forward, cam.Right,
                    heard?.Sector ?? location.At(cam.Position, 0));
        // The platform's output: with a device, its queue topped up; with none, a tape's frames mixed as
        // recorded; the amount hook writes or answers each frame's number. Neither: the file renders its own.
        if (speakers.Live || InputTape.On) speakers.Pump(audio.Mixer, InputTape.On ? InputTape.AudioFrames : null);
        else tape?.Render(audio.Mixer, seconds);
    }


    // The escape menu, with the swivel's hold on the mouse let go first.
    void OpenEscapeMenu()
    {
        StopSwivel();
        menus.OpenEscape();
        net?.EscapeShown(location.Steered);
        Console.WriteLine("  menu: the escape menu");
    }


    // The menus' request, once a frame after it is drawn (the main sequence's). A save is
    // written here; a load and the main menu end this run with the next one's arguments.
    void MenuRequests()
    {
        var (asked, slot) = menus.TakeRequest();
        switch (asked)
        {
            case MenuRequest.Save:
                SaveNow(SlotPath(slot), plain: false, menus.SaveName);
                break;
            case MenuRequest.Restore:
                // One ordinary frame has been shown; the loading plaque goes up over it.
                menuPass.Loading = true;
                PresentOnce();
                host.NextRun = [.. RunSettings.Settings(args), "--load", SlotPath(slot), "--play"];
                running = false;
                break;
            case MenuRequest.Import:
                menuPass.Loading = true;
                PresentOnce();
                host.NextRun = [.. RunSettings.Settings(args), "--import", OriginalSaves.For(files.Root).PathOf(slot), "--play"];
                running = false;
                break;
            case MenuRequest.MainMenu:
                Vitomir.Engine.Ai.MainFlagsWord.TornDown();                     // the main loop's teardown
                host.NextRun = menus.CreditsOnMainMenu ? [.. RunSettings.Settings(args), "--menu", "--credits"] : [.. RunSettings.Settings(args), "--menu"];
                running = false;
                break;
        }
    }


    // One more frame of the game as it stands, with the world held still.
    void PresentOnce()
    {
        if (!windowed) return;
        var picture = host.Acquire();
        if (picture.Acquired)
        {
            Prepare(frame, picture.Width, picture.Height, 0f, null);
            picture.Draw(frame, cam);
        }
        host.Submit(picture);
    }


    void Shutdown()
    {
        net?.Dispose();
        if (Journal.Current is { } journal)
        {
            // A closing tally, so the question a play-test is usually asked — did anybody hit
            // anybody, and who died — is answered without reading 131 lines.
            var blows = frame.Sim.Blows;
            var me = location.Player?.Thing;
            journal.Write(frame.Sim.Milliseconds, JournalKind.Run,
                ("ended", true), ("seconds", frame.Sim.Seconds),
                ("blows", blows.Count), ("hits", blows.Count(b => b.Blow.Hit)),
                ("byPlayer", me is null ? null : blows.Count(b => ReferenceEquals(b.Attacker, me))),
                ("atPlayer", me is null ? null : blows.Count(b => ReferenceEquals(b.Target, me))),
                ("dealt", me is null ? null : blows.Where(b => ReferenceEquals(b.Attacker, me)).Sum(b => b.Harm.Taken)),
                ("suffered", me is null ? null : blows.Where(b => ReferenceEquals(b.Target, me)).Sum(b => b.Harm.Taken)),
                ("playerHp", me?.Actor?.Health), ("playerDead", me?.Actor?.Dead),
                ("dead", string.Join(',', location.Things.Where(t => t.Actor is { Dead: true })
                                                  .Select(Watch.Name).DefaultIfEmpty("none"))));
            journal.Dispose();
            Console.WriteLine($"journal: {journal.Lines} events in {System.IO.Path.GetFullPath(journal.Path)}");
            Journal.Current = null;
        }
        // What the run made on the GPU goes with it; the GPU, the window and SDL are the process's
        // and outlive it (see the top of the file).
        filmScreen.Dispose();
        overlayOut.Dispose();
        speakers.Dispose();
        tape?.Dispose();
        markerRenderer.Dispose();
        uiOut.DisposeInserts();
        effectsPass.Dispose();
        frame.Dispose();
        uiOut.Dispose();
        // The pointer comes back only when the program is finishing: a run that hands over to
        // another (a new game, a load) keeps it hidden, or the system arrow flashes over the
        // loading plaque while the next world is built.
        if (windowed && host.NextRun is null) host.ShowCursor(true);
    }
}
