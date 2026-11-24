// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game run's input: one event of a frame, heard (the keys, the buttons, the wheel, the pointer).
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
    // One event of the frame's, as the bindings handed it on (the poll's body): the network game's and the
    // menus' first, then the frame's mouse movement, then the keys, the buttons, the wheel and the pointer.
    void Hear(LoopInput e)
    {
        windowActive.Hear(e);                                  // WM_ACTIVATE
        if (e.Kind == LoopInputKind.Wheel) dumpWheel += e.DY > 0 ? 1 : e.DY < 0 ? -1 : 0;   // for `--frame-dump-log` (ours)
        if (e.Kind == LoopInputKind.ButtonDown && e.Code is > 0 and <= 32) dumpClicks |= 1u << (e.Code - 1);
        host.Heard(e);                                         // moved or resized: kept (ours)
        // While the swivel lock holds the pointer at the middle, every click is a click there (CentreLook).
        if (e.Kind is LoopInputKind.ButtonDown or LoopInputKind.ButtonUp)
        {
            var (cx, cy) = CentreLook.Click(lockAt, e.X, e.Y);
            e = e with { X = cx, Y = cy };
        }
        // A network game's chat line and the Storyteller's panes take theirs first (`NetPlay`).
        if (!KeyInput.WorldRelease && net?.Event(e, host) == true) return;
        // **A menu that is up has every key and every click** — the escape menu and the two
        // dialogs are modal, and the save and load screens cover the screen. The
        // window's own close still closes it.
        if (menus.Up && e.Kind != LoopInputKind.Quit && !KeyInput.WorldRelease && MenuInput(menus, windowed, e))   // a key let go under it is the game's too
        {
            if (e.Kind == LoopInputKind.PointerMove) (pointerX, pointerY) = (e.X, e.Y);
            return;
        }
        // Every movement counts towards the frame's, whatever else it does.
        if (e.Kind == LoopInputKind.PointerMove)
        {
            frameMouseX += e.DX;
            frameMouseY += e.DY;
            int pw0, ph0; host.PictureSize(&pw0, &ph0);
            var (mx0, my0) = StatusBar.FromWindow(e.X, e.Y, pw0, ph0);
            menus.PointerAt(mx0, my0);
            menuPixel = (e.X, e.Y);
            // The message box's slider, while it is held, follows the pointer
            // whatever else the movement does.
            if (hud.Bar.Messages is { Dragging: true } held)
                held.DragTo(my0);
            if (readPass.Pane is { Dragging: true } reading)    // and the read dialog's
                reading.DragTo(my0);
        }
        switch (e.Kind)
        {
            case LoopInputKind.PointerLeave: pointerOut = true; atEdge = false; break;
            case LoopInputKind.PointerEnter: pointerOut = false; break;
            // The swivel: X, or the keypad's 5, held. The game camera only.
            case LoopInputKind.KeyDown when !e.Repeat && follow && worldCam.Mode == CameraMode.Game
                    && e.Code is LoopScancode.X or LoopScancode.Keypad5:
                StartSwivel(); break;
            case LoopInputKind.KeyUp when looking && worldCam.Mode == CameraMode.Game && swivelHold.ReleaseEnds   // not while locked
                    && e.Code is LoopScancode.X or LoopScancode.Keypad5:
                StopSwivel(); break;
            // And the fifth mouse button, which the engine reads for the swivel too.
            case LoopInputKind.ButtonDown when e.Code == LoopButton.X2
                    && follow && worldCam.Mode == CameraMode.Game:
                StartSwivel(); break;
            case LoopInputKind.ButtonUp when e.Code == LoopButton.X2
                    && looking && worldCam.Mode == CameraMode.Game && swivelHold.ReleaseEnds:
                StopSwivel(); break;
            case LoopInputKind.Quit: running = false; break;
            case LoopInputKind.KeyDown:
                // Advancing, the keyboard is Enter (or the keypad's) for done and Esc for revert,
                // and nothing else (the game's own key handler is not called).
                if (advancement is not null)
                {
                    if (e.Key is LoopKey.Return or LoopKey.KeypadEnter) AdvanceDone();
                    else if (e.Key == LoopKey.Escape) AdvanceRevert();
                    break;
                }
                // **A shop takes the keyboard.** Escape closes it, silently, through the pane's
                // own key hook, and nothing else the game binds does
                // anything: that hook returns before its hotkeys, and state 4 runs no world keys.
                // Ctrl+F1 to F3 are the harness's and still answer (with Ctrl only, HarnessKeys).
                if (session.Shops.Active is not null && !KeyInput.HarnessPasses(e))
                {
                    if (e.Key == LoopKey.Escape)
                    {
                        session.Shops.End();
                        Console.WriteLine("  shop: closed (escape)");
                    }
                    break;
                }
                // **So does the vault** (a comparison with 1.1): its pane's key hook is the same
                // The pane's key handler, whose vault arm closes it on Escape and returns before
                // the game's keys. Vitomir had left Escape doing nothing with the vault up.
                if (vaultPass.Enabled && !KeyInput.HarnessPasses(e))
                {
                    if (e.Key == LoopKey.Escape && !e.Repeat) ShowVault(false);
                    break;
                }
                // Space skips the line being spoken, as in play, and which
                // is the conversation's own. Escape is *not* bound to the conversation: the original's abort
                // is gated on a flag no shipped script sets, so escape does nothing at all
                // during one. Here it still closes the viewer.
                // In the game camera, a camera path and a cutscene; not in the look or the free
                // camera (`WorldCamera.SpaceSkipsLine`).
                // Space in the game camera also stops the character you steer, in a conversation
                // or not (`World/StopKey.cs`).
                if (e.Key == LoopKey.Space && !e.Repeat && worldCam.Mode == CameraMode.Game
                    && !vaultPass.Enabled && !readPass.Enabled && !menus.Up)
                    StopKey.Press(location);
                if (e.Key == LoopKey.Space && talk.Current is { Running: true } speaking
                    && worldCam.SpaceSkipsLine)
                { speaking.Skip(frame.Sim.Milliseconds); break; }
                // T restarts it. A harness key: the original re-enters a conversation through
                // the use verb on the character, which is whatever its script does on `clicked`.
                if (e.Key == LoopKey.T && talk.Current is not { Running: true } && restart is not null)
                { restart(frame.Sim.Milliseconds); break; }
                // **Escape opens the escape menu**, on 1.1's own terms: the
                // game camera with no vault up plays the button press and opens it —
                // unless a conversation is running, whose abort does nothing in the shipped game;
                // the free camera opens it silently; the first-person look, a camera
                // path and a cutscene do not. A dialog up (a book) takes the key itself.
                if (e.Key == LoopKey.Escape && !e.Repeat && !readPass.Enabled)
                {
                    if (worldCam.Mode == CameraMode.Game && !vaultPass.Enabled)
                    {
                        Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonDepress);
                        if (talk.Current is null) OpenEscapeMenu();
                    }
                    else if (worldCam.Mode == CameraMode.Free && talk.Current is null) OpenEscapeMenu();
                    break;
                }
                // 1.1's F1-F5 use the quick slots, whatever is held; the
                // harness's own are on Ctrl with --debug (HarnessKeys). F12 is a screenshot. A / N / E
                // set the whole party's stance without a word —
                // with `[qol] driving` off, as D is, because driving has A and E.
                if (!e.Repeat && !KeyInput.IsHarness(e) && worldCam.Mode == CameraMode.Game
                    && e.Key is LoopKey.F1 or LoopKey.F2 or LoopKey.F3
                                 or LoopKey.F4 or LoopKey.F5 && location.Steered is not null)
                {
                    int quickSlot = e.Key - LoopKey.F1;
                    if (quickSlot < hud.Bar.QuickUse.Count && hud.Bar.QuickUse[quickSlot].Item is { } quickItem && hud.Bar.UseQuick is { } useQuick)
                        Console.WriteLine($"  quick {quickSlot + 1}: {useQuick(quickItem)}");
                    break;
                }
                if (e.Key == LoopKey.F12 && !e.Repeat)
                {
                    int shotW, shotH; host.PictureSize(&shotW, &shotH);
                    Console.WriteLine(Screenshots.Take(shotW, shotH, Screenshots.Rgba(Still(frame, shotW, shotH), gpu.ColourIsBgra)) is { } shot
                        ? $"  screenshot: {shot}" : "  screenshot: none written");
                }
                // (Driving takes A, N and E off together while any of them is a driving key: `KeyBindings.Pressed`.)
                if (!e.Repeat && worldCam.Mode == CameraMode.Game && location.Player is not null
                    && e.Key is LoopKey.A or LoopKey.N or LoopKey.E)
                    Console.WriteLine("  " + hud.Bar.StanceKey(e.Key == LoopKey.A ? PartyStance.Attack
                        : e.Key == LoopKey.N ? PartyStance.Neutral : PartyStance.Defense));
                // Pause (`FrontEnd.PauseKey`): the game clock stops or starts again and the
                // message box says which — in the game camera with no vault up (the key handler's start; the
                // shop and the advancement screen took the key above), in single player. Anywhere
                // else nothing reads it, and the console says so (ours: a keyboard check).
                // The chat's keys in single player: 1.1 plays the interface sound 0 before it asks for a
                // network game's bar (Net.ChatKeys) — the network game's are NetPlay's.
                if (net is null && !e.Repeat && Vitomir.Engine.Net.ChatKeys.Of(e.Code == LoopScancode.Return, e.Code == LoopScancode.Semicolon,
                        e.Code == LoopScancode.Slash, (e.Mods & LoopMods.Shift) != 0) != Vitomir.Engine.Net.ChatKey.None
                    && Vitomir.Engine.Net.ChatKeys.Read(worldCam.Mode == CameraMode.Game, readPass.Enabled, vaultPass.Enabled, advancement is not null, buyPass.Enabled, typing: false))
                    audio.Ui(Vitomir.Engine.Net.ChatKeys.Sound);
                if (e.Key == LoopKey.Pause && !e.Repeat)
                {
                    if (worldCam.Mode == CameraMode.Game && !vaultPass.Enabled && menus.PauseKey() is { } pauseLine)
                        session.Messages.Add(strings[pauseLine]);
                    else Console.WriteLine($"  pause: not read here (the {worldCam.Mode} camera{(vaultPass.Enabled ? ", the vault up" : "")}{(menus.SinglePlayer ? "" : ", a network game")})");
                    break;
                }
                if (KeyInput.Harness(e, Vitomir.Engine.Ui.HarnessKey.Overlay)) watch.Enabled = !watch.Enabled;   // Ctrl+F2 (HarnessKeys)
                // F3 cycles the overlay's size, because what is readable depends on the window.
                if (KeyInput.Harness(e, Vitomir.Engine.Ui.HarnessKey.OverlaySize)) overlay.Scale = overlay.Scale >= 3 ? 1 : overlay.Scale + 1;
                // F11 is the original's own ("Toggle Status Bar Display", manual p17) and the `statusbar`
                // binding's: read where 1.1 reads it, once a press, with the interface sound 0
                // (Engine.Ui.StatusBarKey, F4's alias is gone).
                // What the bar's toggle sets is what the player wants; whether it is on screen
                // also depends on the camera (a cutscene takes it down), so the bar and
                // everything that belongs to it follow once a frame, below.
                if (e.Key == LoopKey.F11 && StatusBarKey.NotRead(worldCam.Mode.ToString(), e.Repeat, worldCam.Mode == CameraMode.Game, readPass.Enabled) is var notRead)
                {
                    if (notRead is null && StatusBarKey.Reads(e.Repeat, worldCam.Mode == CameraMode.Game, readPass.Enabled))
                    {
                        audio.Ui(StatusBarKey.Sound);
                        var toggle = StatusBarKey.Toggle(hudWanted, inFront: menus.Layers.Count <= 1);   // the in-front test
                        if (toggle != StatusBarKey.BarToggle.Nothing) hudWanted = toggle == StatusBarKey.BarToggle.Show;
                        Console.WriteLine($"  status bar: {(toggle == StatusBarKey.BarToggle.Nothing ? "not in front, left as it is" : hudWanted ? "up" : "down")}");
                    }
                    else if (!e.Repeat) Console.WriteLine($"  status bar: F11 not read here ({notRead})");
                }
                if (KeyInput.Harness(e, Vitomir.Engine.Ui.HarnessKey.Sheet)) ShowCharacter(!charPass.Enabled);
                // C and I are the game's own keys for the character sheet and the pack;
                // F5 stays as the harness's second key for the sheet.
                if (e.Key == LoopKey.C && location.Steered is not null) ShowCharacter(!charPass.Enabled);
                if (e.Key == LoopKey.I && location.Steered is not null) ShowInventory(!invPass.Enabled);
                // **Taking over another party member**: F6 to F9 are the party's four slots,
                // Tab the next one round,
                // with the interface's click and, now and then, the new one saying so.
                if (worldCam.Mode == CameraMode.Game && !readPass.Enabled && talk.Current is null && !menus.Up)
                {
                    int slot = e.Key switch
                    {
                        LoopKey.F6 => 0, LoopKey.F7 => 1,
                        LoopKey.F8 => 2, LoopKey.F9 => 3, _ => -1,
                    };
                    if (slot >= 0 && !location.SteerSlot(slot))
                        Console.WriteLine($"  steer: nobody to take over in slot {slot + 1}");
                    else if (slot >= 0 && net?.View?.Selected(location.Steered) == true)
                        Console.WriteLine("  story: F6 — the carried body let go, the camera back");
                    if (e.Key == LoopKey.Tab)
                    {
                        // Steering a possessed body, Tab ends the possession instead.
                        bool possessed = location.Steered?.Mind is { } pm
                                         && (pm.Flags & Vitomir.Engine.Ai.Mind.FlagPossessed) != 0 && net is null;
                        net?.Story?.Tab();                                   // the seat's arm
                        location.SteerTab(() => minds?.Random.Unit() ?? 1f, network: net is not null);
                        if (possessed) Console.WriteLine("  steer: Tab ends the possession");
                    }
                }
                // Q is the original's quest pane key (the manual's control list). With nobody
                // to drive it is the free camera's "down", so it is only the pane's with a player.
                if (e.Key == LoopKey.Q && location.Player is not null) ShowQuests(!questPass.Enabled);
                // The key left of 1: the developer console, where there is one.
                if (e.Key == LoopKey.Grave && consoleKey && menus.DevConsole is { } devConsole) devConsole.Open();
                // M, the map (BUTTONDEPRESS, then the map).
                if (e.Key == LoopKey.M && location.Player is not null && worldCam.Mode == CameraMode.Game)
                {
                    audio.Ui(FoleyEvent.ButtonDepress);
                    OptionsHost.OpenMap(menus, session.Chronicle, files, location.Name);
                }

                // D, the discipline pane (BUTTONDEPRESS, then the button's own toggle) —
                // only with `[qol] driving` off. With it on, D is the driving keys' side-step,
                // and the pane is the status bar's button until keys can be remapped (WASD kept for
                // driving, and what they collide with unmapped).
                if (e.Key == LoopKey.D && location.Player is not null && advancement is null)
                {
                    Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonDepress);
                    ShowDisciplines(!discPass.Enabled);
                }
                // Backspace takes every pane down.
                if (e.Key == LoopKey.Backspace && advancement is null)
                {
                    ShowQuests(false); ShowInventory(false); ShowDisciplines(false); ShowCharacter(false);
                }
                // A dialog swallows the keyboard: Enter, the keypad's Enter or Escape shuts it, the arrows scroll it.
                if (readPass.Enabled)
                {
                    if (e.Key is LoopKey.Escape or LoopKey.Return or LoopKey.KeypadEnter && !e.Repeat) { readPass.Pane!.Open(null); break; }   // ReadPaneOpen.CloseKeys
                    if (e.Key == LoopKey.Down) { readPass.Pane!.ScrollBy(readPass.Pane.LineHeight); break; }    // ours: a line
                    if (e.Key == LoopKey.Up) { readPass.Pane!.ScrollBy(-readPass.Pane.LineHeight); break; }
                    if (e.Key == LoopKey.PageDown) { readPass.Pane!.ScrollBy(readPass.Pane.Page); break; }      // ours: a page
                    if (e.Key == LoopKey.PageUp) { readPass.Pane!.ScrollBy(-readPass.Pane.Page); break; }
                }
                // The game's own coterie keys. R regroups and shift+R ungroups all — both from
                // the 1.1 patch readme's control addendum — and G toggles the steered character.
                // A, N and E are the game's stance keys and are *not* bound: A and E are the
                // developer walk keys, which the original does not have at all (it is
                // click-to-move only), so they land when the harness keyboard goes.
                if (e.Key == LoopKey.R)
                    Console.WriteLine($"  ui: {((e.Mods & LoopMods.Shift) != 0 ? hud.Bar.Disperse() : hud.Bar.Regroup())}");
                if (e.Key == LoopKey.G && hud.Bar.Selected is { } toggled)
                    Console.WriteLine($"  ui: {hud.Bar.Toggle(toggled)}");
                // The discipline keys: 1 to 6 pick the quick slot, F Feed, H Blood
                // Healing or Heal. Only in play — not in a cutscene, a document or a reply list.
                if (worldCam.Mode == CameraMode.Game && !readPass.Enabled && talk.Current is not { Waiting: true }
                    && location.Steered is { } chooser)
                {
                    char? pressed = e.Key switch
                    {
                        LoopKey.Digit1 => '1', LoopKey.Digit2 => '2', LoopKey.Digit3 => '3',
                        LoopKey.Digit4 => '4', LoopKey.Digit5 => '5', LoopKey.Digit6 => '6',
                        LoopKey.F => 'F', LoopKey.H => 'H',
                        _ => null,
                    };
                    if (pressed is { } k && Vitomir.Engine.Rpg.CastInput.Key(chooser, k))
                        Console.WriteLine($"  discipline: {chooser.Actor?.Disciplines.Current?.Name} selected");
                }
                if (KeyInput.Harness(e, Vitomir.Engine.Ui.HarnessKey.Where))                        // Ctrl+F1 (HarnessKeys)
                    Console.WriteLine(player is null
                        ? $"--pos {cam.Position.X:0},{cam.Position.Y:0},{cam.Position.Z:0} --yaw {cam.Yaw:0} --pitch {cam.Pitch:0}"
                        : $"player at {player.Position.X:0},{player.Position.Y:0},{player.Position.Z:0} " +
                          $"facing {player.Heading:0}, node {player.Node}; camera {worldCam.Mode}, " +
                          $"{worldCam.Distance:0} back, pitch {WorldCamera.Signed(worldCam.Angles.X):0}, " +
                          $"yaw {worldCam.Angles.Z:0}, zoom {worldCam.ZoomScale:0.00}, fov {worldCam.Fov:0}");
                break;
            // **The wheel zooms the game camera** — `[qol] camera`. The original zooms on − and
            // = (and the keypad's − and +) only; the wheel is ours, and it moves the same
            // zoom through the same 0.1..1 the keys do, a tenth a notch.
            // The shop's list takes the wheel while the pointer is on it; nothing
            // else does while a shop is open, the camera's zoom included.
            case LoopInputKind.Wheel when buyPass.Enabled:
            {
                int sw2, sh2; host.PictureSize(&sw2, &sh2);
                var (wx2, wy2) = StatusBar.PaneFromWindow(pointerX, pointerY, sw2, sh2);
                if (buyPass.Pane!.Over(Vitomir.Engine.Ui.BuySellPane.List, wx2, wy2))
                    buyPass.Pane.Wheel(e.DY > 0 ? 1 : e.DY < 0 ? -1 : 0);
                break;
            }
            // The read dialog is modal: the wheel scrolls its body a page while the pointer is
            // on it, and nothing under the dialog hears it.
            case LoopInputKind.Wheel when readPass.Enabled:
            {
                int rw2, rh2; host.PictureSize(&rw2, &rh2);
                var (wx3, wy3) = StatusBar.FromWindow(pointerX, pointerY, rw2, rh2);
                if (readPass.Pane!.OverBody(wx3, wy3))
                {
                    readPass.Pane.Wheel(e.DY > 0 ? 1 : e.DY < 0 ? -1 : 0);
                    Console.WriteLine($"  ui: {readPass.Pane.Where()}");
                }
                break;
            }
            // The message box takes the wheel while the pointer is on it: a page
            // a turn. That comes before the camera's zoom, which the original has not got.
            case LoopInputKind.Wheel when OnMessageBox() is { } scrolled:
                Console.WriteLine($"  ui: {scrolled.Wheel(e.DY > 0 ? 1 : e.DY < 0 ? -1 : 0)}");
                break;
            // So does a conversation's reply list, which pages a box's height a turn.
            case LoopInputKind.Wheel when talkPass.Pane is { Offering: true } paging
                    && paging.Over(StatusBar.FromWindow(pointerX, pointerY, winW, winH)):
                paging.Page(talkPass.Font, e.DY > 0 ? -1 : e.DY < 0 ? 1 : 0, StatusBar.FromWindow(pointerX, pointerY, winW, winH));
                break;
            case LoopInputKind.Wheel when follow && qol.Camera && worldCam.Mode == CameraMode.Game:
                worldCam.Zoom(-e.DY * 0.1f); break;
            // The original's own control, and it is two things rather than one. A click first
            // asks what is *under the cursor* — the cursor's choice turns a thing into a
            // verb, and the click acts on it — and only when that comes to
            // nothing does it fall through to the floor, where the one test is that the
            // surface is walkable.
            // A reply gets the click before anything else does, the way a pane does in the
            // original: the conversation is modal while it is offering (`dialog=1`).
            case LoopInputKind.ButtonDown when e.Code == LoopButton.Left
                    && talkPass.Pane is { Offering: true } picking && talk.Current is { Waiting: true } answering:
                int pw, ph; host.PictureSize(&pw, &ph);
                var (px2, py2) = StatusBar.FromWindow(e.X, e.Y, pw, ph);
                if (picking.RowAt(talkPass.Font, px2, py2) is var chose && chose >= 0)
                {
                    answering.Pick(chose, frame.Sim.Milliseconds);
                    // Choosing a reply takes the list down and the pointer with it;
                    // the next list, or the camera's release, brings it back.
                    worldCam.CursorShown = false;
                }
                break;
            case LoopInputKind.ButtonDown when e.Code == LoopButton.Left:
            {
                int ww, wh; host.PictureSize(&ww, &wh);
                // The interface gets the click first, and it gets it whether or not there is a
                // control under the cursor: the original decides that at the *pane* level
                // (the pane the cursor is on is found first, and the world runs only
                // when there is none), so anywhere on the bar's own artwork swallows the click.
                var mods = Modifiers();
                var (lx, ly) = StatusBar.FromWindow(e.X, e.Y, ww, wh);
                // And in the space of the panes above the bar (UiLayout.Panes): every pane's test below takes this.
                var (plx, ply) = StatusBar.PaneFromWindow(e.X, e.Y, ww, wh);
                discDrag.Press(e.X, e.Y);          // the drag's motion sums start
                leftPressAt = (e.X, e.Y);          // and a shop's drag of an item (InventoryHands.Press)
                // The newest dialog takes every click, the status bar up or hidden (`DialogClicks`).
                if (readPass.Enabled) { ReadDialogPress(lx, ly); break; }
                // A direct flag click (1.1's middle button, its third binding) is the world's alone.
                if (KeyInput.WorldOnly && (OverPane(plx, ply) || hud.Enabled && hud.Bar.Covers(lx, ly) || advancement is not null)) break;
                if (buyPass.Enabled && ShopPress(plx, ply)) break;
                // The advancement screen is modal: its bar's two buttons and the sheet's raise
                // buttons, on the release over the one pressed.
                if (advancement is not null)
                {
                    if (advBarPass.ButtonAt(lx, ly) is { } advButton)
                    {
                        advBarPass.Pressed = advButton;
                        Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonDepress);
                        break;
                    }
                    raisePress = charPass.Enabled ? charPass.Pane!.RaiseAt(plx, ply) : -1;
                    if (raisePress >= 0)
                    {
                        Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonDepress);
                        break;
                    }
                }
                // The discipline pane: a tab acts on the press (it is a checkbox);
                // everything else on the release over what was pressed. A drag's start
                // over a row lifts its discipline (`DisciplineDrag`).
                if (discPass.Enabled && discPass.Pane!.Covers(plx, ply))
                {
                    var dp = discPass.Pane;
                    discPress = dp.At(plx, ply);
                    if (discPress.Part != Vitomir.Engine.Ui.DisciplinePane.Part.None
                        && discPress.Part is not (Vitomir.Engine.Ui.DisciplinePane.Part.Text or Vitomir.Engine.Ui.DisciplinePane.Part.Dots))
                        Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonDepress);
                    if (discPress.Part == Vitomir.Engine.Ui.DisciplinePane.Part.Tab)
                    {
                        dp.ClickTab(discPress.Index);
                        hud.Bar.Update();
                        Console.WriteLine($"  ui: disciplines, {dp.GroupName}");
                    }
                    break;
                }
                if (advancement is not null) break;               // nothing else takes a click while advancing
                // The vault's OK: pressed here, and on the release over it the vault goes.
                if (vaultPass.Enabled && vaultPass.Pane!.ButtonAt(plx, ply) is { } vaultButton)
                {
                    vaultPass.Pressed = vaultButton;
                    Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonDepress);
                    break;
                }
                // The pack's coin button (on the release over it); greyed, it is deaf.
                if (invPass.Enabled && !invPass.GoldGreyed && Vitomir.Engine.Ui.DropCash.At(invPass.Pane!, plx, ply))
                {
                    invPass.Pressed = Vitomir.Engine.Ui.DropCash.Button;
                    Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonDepress);
                    break;
                }
                if (hud.Enabled)
                {
                    var (ux, uy) = StatusBar.FromWindow(e.X, e.Y, ww, wh);
                    // A quick slot: the press only remembers it (a drag's start lifts it,
                    // `DisciplineDrag`); the release over it selects.
                    barPress = hud.Bar.PowerSlotAt(ux, uy);
                    if (barPress >= 0)
                        Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonDepress);
                    // A button or a check box — the inventory, character, quest and discipline
                    // buttons and the three stances are check boxes (`statusBarSP.nui`): the pane
                    // hook every pane is given sounds both on the press and
                    // on the release (the check boxes had no click).
                    if (hud.Bar.At(ux, uy) is { Type: "button" or "checkbox" } held)
                    {
                        held.Pressed = true;
                        audio.Control(held.Type, release: false);     // BUTTONDEPRESS
                    }
                    // The message box's scroll bar and slider make the press too, and only it.
                    else if (hud.Bar.At(ux, uy) is { Type: "scrollbar" or "scrollslider" } track)
                        audio.Control(track.Type, release: false);
                    // A companion's portrait waits for a drag or the release (ours, `partyorder`).
                    if (PartyPress(e.X, e.Y, ux, uy, mods)) break;
                    if (hud.Bar.Click(ux, uy, mods) is { Consumed: true } took)
                    {
                        Console.WriteLine($"  ui: {took.Did}");
                        break;
                    }
                }
                // The inventory is above the world and below the bar, which is the order
                // the panes are drawn in and therefore the order a click has to try them —
                // with the bar off too (`--no-hud`), or a pane's click fell through to the world.
                if (hands is { } hand && OverPane(plx, ply))
                {
                    // A press, which in a shop may wait to be a drag (ours, `quickequip`); else the click at once.
                    Console.WriteLine($"  inv: {hand.Press(plx, ply, e.X, e.Y).Say}");
                    break;
                }
                // The Storyteller's view of another place: the click is the carried body's there (StoryView).
                // Off the world that view draws the click is nobody's, as on the host's own picture below.
                if (net?.View is { } seeing && seeing.Click(cam, e.X, e.Y, ww, wh, mods)) break;
                if (player is null || buyPass.Enabled) break;         // state 4: no world clicks
                if (KeyInput.InterfaceOnly) break;                    // the left button moved off "act": the panes only
                // **The game camera and the free camera take orders** (the camera modes 1 and 3);
                // in a cutscene, on a camera path and in the first-person look the world never
                // hears of a click. The free camera dispatches it twice (`FreeCameraClicks`).
                int dispatches = FreeCameraClicks.Dispatches(worldCam.Mode);
                if (dispatches == 0) break;
                // **A click off the world's view is not the world's** (`WorldHas`).
                // No pane took it, and the picture shows no world there — the black beside an open pane on a picture
                // wider than the sheet, or above one on a picture taller than it; a pane's own art between its grids —
                // so it is nobody's: no pick, no order, no ring, as the hover there points at nothing.
                if (!WorldHas("click", e.X, e.Y, ww, wh)) break;
                dispatch:
                // The engine passes the click's modifier straight through as the attack index,
                // and bit 0 of that mask is **shift** — the same shift that forces a
                // run on the ground, because the engine picks by what is under the cursor rather
                // than by the key. `Vitomir.Engine.World.InputModifier` has the derivation.
                bool alt = mods.HasFlag(InputModifier.Shift);
                // The same viewport the frame drew, or the click lands on a different place
                // from the one under the cursor.
                var (o, d) = Pick.Ray(cam, e.X, e.Y, ww, wh, frame.WorldRows(wh));
                var hit = WorldSweep.Pointer(location, o, d);
                // **The click acts on whatever the pointer was already resting on**, not on a
                // fresh pick of its own: the click takes the rollover pass's
                // own answer — straight. So the cursor and the click can never
                // disagree, and everything the pointer refuses (a neutral civilian, a horse, a
                // thing marked NO HIGHLIGHT) the click refuses too. Vitomir picked again here
                // with no filter at all, so a click could act on something the cursor had just
                // declined to offer anything for.
                bool blocked = false;
                // Out of the game camera nothing is picked, and the swallow is the last pick's.
                var onThing = player.Thing.Actor?.Dead == true || !FreeCameraClicks.Picks(worldCam.Mode) ? null
                    : Pick.NearestThing(location.Things, o, d, out blocked,
                                        hit?.Distance ?? float.MaxValue, player.Thing,
                                        t => Vitomir.Engine.Ui.Highlight.Offers(player.Thing, t));
                blocked = FreeCameraClicks.Swallowed(worldCam.Mode, blocked, swallowed);

                if (player.Thing.Actor?.Dead == true) Console.WriteLine("  click: dead characters do not act");
                // A frenzy, a mesmerism, a daze or a fear has the controls: every order from the
                // mouse is refused with the red cross (both clicks test it first).
                else if (Vitomir.Engine.Rpg.CastInput.Refused(player.Thing))
                {
                    cursor.Refuse(frame.Sim.Milliseconds);
                    Console.WriteLine($"  click: {Watch.Name(player.Thing)} is not answering to orders ({player.Thing.Mind?.Mode})");
                }
                // A BLOCKSELECT thing under the pointer eats the click: no verb, no walk, no
                // order. That is how the raising doors in the monastery are meant to behave —
                // they open from their switch and not from being clicked.
                else if (blocked) Console.WriteLine("  click: swallowed by something marked BLOCKSELECT");
                else if (onThing is { } on && Verb(player.Thing, on.Thing, frame.Sim.Milliseconds, alt, mods)) { }
                // Alt with a grenade on the pointer throws it at the floor.
                else if (hit is { } thrownAt && ClickOrders.ThrowsFromPointer(player.Thing, mods))
                    Console.WriteLine($"  click: throwing at the ground: {ClickOrders.ThrowFromPointer(player.Thing, thrownAt.Point, mods, frame.Sim.Milliseconds, location.Motions)}");
                // Alt with a ranged weapon fires at the floor, walkable or not.
                else if (hit is { } aimed && ClickOrders.FiresAtGround(player.Thing, mods))
                    Console.WriteLine($"  click: firing at the ground: {ClickOrders.AttackPoint(player.Thing, aimed.Point, ClickOrders.AttackIndex(mods), frame.Sim.Milliseconds, location.Motions, mods)}");
                // **A click on the ground with something in hand puts it down there**, which is
                // the base game's behaviour and the walk order's: walk
                // to within fifty units of the point and drop it, or — with space held — drop it
                // at once, clamped to that same reach. **Before the walkable test**,
                // so a table top or a ledge takes it too — and **its answer is the click's**: a
                // refusal is the red cross and no walk (ClickAnswer).
                else if (hit is { } dropAt && player.Thing.Actor?.Held is { } carrying)
                    PutDown(player.Thing, carrying, dropAt, frame.Sim.Milliseconds, mods);
                // Nothing under the pointer or no walk flag answers 1: the red cross.
                else if (hit is not { Walkable: true } spot)
                {
                    cursor.Refuse(frame.Sim.Milliseconds);
                    Console.WriteLine("  click: not walkable ground");
                }
                // A click on the floor is a new order, so it ends whatever fight was running.
                else if (Ordered(player.Thing)) { }
                // Ctrl: the manual's "Party Regroup to Location". Everybody keeping
                // formation drops what they were doing first, then the walk is ordered as usual,
                // so the coterie ends up following him to the spot. (Never taken: it only goes first.)
                else if (mods.HasFlag(InputModifier.Ctrl) && RegroupFirst()) { }
                // Space turns in place (the move order's bit-8 arm), before the move order cancels anything.
                else if (ClickOrders.Turns(mods))
                    Console.WriteLine($"  click: turning in place ({ClickOrders.TurnInPlace(player.Thing, spot.Point, frame.Sim.Milliseconds)})");
                // The move order's own gate: not while busy, and a feed or a swing is
                // cancelled first — which for a feed plays the release and leaves him busy for it.
                // Somebody standing there refuses it before the cancel: the click's
                // flags never carry 0x100 (Rpg.MoveOrder.Head).
                else if (Vitomir.Engine.Rpg.MoveOrder.Head(player.Thing, player, spot.Point) == 1)
                {
                    cursor.Refuse(frame.Sim.Milliseconds);      // 1: the red cross (ClickAnswer)
                    Console.WriteLine($"  click: {player.Occupant!.Template} is standing there");
                }
                else if (!Actions.ReadyToMove(player.Thing))
                    Console.WriteLine($"  click: {Watch.Name(player.Thing)} is {Busy(player.Thing)}, not now");
                // The move order: the walk within 300 units unless shift is held, the
                // run otherwise — and either at the character's MOVESPEED as it stands at the
                // click, which is what Celerity and the wound limp change.
                else if (GroundOrders.Walks(mods, Vector3.Distance(player.Thing.Position, spot.Point)) is var walks
                         && !player.WalkTo(spot.Point, spot.PathNode, run: !walks,
                                           speed: (walks ? player.WalkSpeed : player.RunSpeed)
                                                  * (player.Thing.Actor?.MoveSpeedScale ?? 1f)))
                {
                    cursor.Refuse(frame.Sim.Milliseconds);      // no route answers 1: the red cross
                    Console.WriteLine(player.Occupant is { } sitting
                        ? $"  click: {sitting.Template} is standing there"
                        : $"  click: no route to {spot.Point.X:0},{spot.Point.Y:0},{spot.Point.Z:0} (node {spot.PathNode})");
                }
                else
                {
                    Vitomir.Engine.Rpg.MoveOrder.Taken(player.Thing);     // ACTION 1
                    // **The ring.** The move order places it at the very end of a successful move
                    // order, and only for the character the player is steering — an AI walk
                    // never shows one. Its plane is the clicked surface's own, which is what
                    // lays it flat on a ramp.
                    frame.Sim.WalkTarget.Set(spot.Point, Pick.NormalAt(location.Level, spot));
                    Console.WriteLine($"  click: {(player.Running ? "running" : "walking")} to " +
                                      $"{spot.Point.X:0},{spot.Point.Y:0},{spot.Point.Z:0} (node {spot.PathNode}) " +
                                      $"by {player.Route.Length} waypoints");
                    Journal.Current?.Write(frame.Sim.Milliseconds, JournalKind.Order,
                        ("what", "walk"), ("who", Watch.Name(player.Thing)),
                        ("node", spot.PathNode), ("waypoints", player.Route.Length),
                        ("run", player.Running));
                }
                if (--dispatches > 0) goto dispatch;                  // the free camera's second dispatch
                break;
            }
            // Letting go is what stops a character swinging, which is the other half of the
            // hold-to-attack rule.
            case LoopInputKind.ButtonUp when e.Code == LoopButton.Left:
            {
                foreach (var control in hud.Bar.Controls)
                {
                    if (control.Pressed) audio.Control(control.Type, release: true);   // BUTTONRELEASE
                    control.Pressed = false;
                }
                if (player?.Thing.Actor is { } letGo) letGo.Repeating = null;
                int uw, uh; host.PictureSize(&uw, &uh);
                var (rx, ry) = StatusBar.FromWindow(e.X, e.Y, uw, uh);
                var (prx, pry) = StatusBar.PaneFromWindow(e.X, e.Y, uw, uh);
                if (buyPass.Enabled) ShopRelease(prx, pry);
                if (hands is { ShopHeld: true } shopHand) ShopHandRelease(shopHand, rx, ry, prx, pry);
                hud.Bar.Messages?.Release();
                readPass.Pane?.Release();
                Release(rx, ry, prx, pry);
                break;
            }
            case LoopInputKind.ButtonDown when e.Code == LoopButton.Right:
            {
                // The advancement screen is modal and the game is paused under it: the right
                // button does nothing there, in the world or on the panes.
                if (advancement is not null) break;
                if (readPass.Enabled) break;                          // the dialog's, and it has no right click
                // The interface takes the right button too, and on the same pane-level rule:
                // the world's right click only runs with no pane under the pointer,
                // whether or not the status bar is up.
                int rw, rh; host.PictureSize(&rw, &rh);
                var (rux, ruy) = StatusBar.FromWindow(e.X, e.Y, rw, rh);
                var (prux, pruy) = StatusBar.PaneFromWindow(e.X, e.Y, rw, rh);
                if (OverPane(prux, pruy))
                {
                    // A right click on a shelf row buys it at once (ours, always on; `BuySellPane.QuickBuy`).
                    if (buyPass.Enabled && buyPass.Pane is { } quick && quick.Over(Vitomir.Engine.Ui.BuySellPane.List, prux, pruy))
                    {
                        quick.HoverAt(prux, pruy);
                        if (quick.Hovered >= 0)
                        {
                            Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonDepress);
                            string what = quick.Rows[quick.Hovered].Treasure.Template;
                            var bought = quick.QuickBuy(quick.Hovered, location, session.Shops);
                            Console.WriteLine($"  shop: buy {what} (right click) -> {bought?.ToString() ?? "not affordable"}, {session.Gold} left");
                        }
                        break;
                    }
                    // And one on an item on the quick-use belt sells it at once, as one in the pack does
                    // (shift held with `quickequip` on is the quick equip's, which sells nothing).
                    if (buyPass.Enabled && hud.Enabled && hud.Bar.QuickAt((int)rux, (int)ruy) is { } beltItem
                        && hands?.QuickSell is { } beltSell && !(Modifiers().HasFlag(InputModifier.Shift) && hands.ShopDrags))
                    {
                        if (!beltSell(beltItem)) hud.Bar.Refuse?.Invoke();
                        break;
                    }
                    if (hands is { } rhand) Console.WriteLine($"  inv: {rhand.RightClick(prux, pruy, Modifiers()).Say}");
                    break;
                }
                if (hud.Enabled)
                {
                    var (ux, uy) = (rux, ruy);
                    if (hud.Bar.Click(ux, uy, Modifiers(), right: true) is { Consumed: true } took)
                    {
                        Console.WriteLine($"  ui: {took.Did}");
                        break;
                    }
                }
                if (KeyInput.InterfaceOnly) break;                    // the right button moved off "cast": the panes only
                // **The right button casts, on the way down, always** — 1.1's (the binding
                // 2). Until then `[qol] camera` made a right press that dragged more than four
                // pixels a swivel; the camera's drag is the middle button's now,
                // handed on as X by the bindings (`KeyBindings.DragSwivel`). With
                // no player the right button still turns the free camera, the harness's.
                if (follow && worldCam.Mode == CameraMode.Game) { if (Picking() || net?.View?.Place is not null) RightCast(e.X, e.Y); break; }
                // The free camera casts at the ground, twice (camera mode 3, `FreeCameraClicks`).
                if (follow && worldCam.Mode == CameraMode.Free) { for (int n = FreeCameraClicks.Dispatches(worldCam.Mode); n > 0; n--) RightCast(e.X, e.Y); break; }
                if (follow) break;
                StartSwivel();
                rightTurns = true;
                break;
            }
            case LoopInputKind.ButtonUp when e.Code == LoopButton.Right && rightTurns:
                rightTurns = false;
                StopSwivel();
                break;
            // In the first-person look the camera's own tick turns by the frame's movement;
            // otherwise a swivel turns the game camera, or the free camera with no player.
            case LoopInputKind.PointerMove when looking:
                if (!follow) { if (rightTurns) cam.Turn(-e.DX * 0.15f, -e.DY * 0.15f); }
                else if (worldCam.Mode == CameraMode.Game) worldCam.Swivel(e.DX, e.DY);
                break;
            case LoopInputKind.PointerMove when swallowMotion > 0:
                swallowMotion--; break;
            case LoopInputKind.PointerMove when talkPass.Pane is { Offering: true } hover:
                int hw, hh; host.PictureSize(&hw, &hh);
                (pointerX, pointerY, winW, winH) = (e.X, e.Y, hw, hh);
                var (hx, hy) = StatusBar.FromWindow(e.X, e.Y, hw, hh);
                hover.Hovered = hover.RowAt(talkPass.Font, hx, hy);
                Pointer(e.X, e.Y, hw, hh);
                break;
            // The pointer moved with nothing modal in the way: the cursor follows it, and the
            // pick under it is what decides which of the eight it is.
            case LoopInputKind.PointerMove:
                int mw, mh; host.PictureSize(&mw, &mh);
                (pointerX, pointerY, winW, winH) = (e.X, e.Y, mw, mh);
                if (shopDrag && buyPass.Enabled)
                    buyPass.Pane!.DragTo(StatusBar.PaneFromWindow(e.X, e.Y, mw, mh).Y);
                // A press on an item in a shop that moves far enough lifts it (ours, `quickequip`).
                if (hands is { ShopHeld: true } dragging && dragging.Drag(e.X, e.Y) is { What: not Vitomir.Engine.Ui.HandAction.Nothing } shopLift)
                    Console.WriteLine($"  inv: {shopLift.Say}");
                Pointer(e.X, e.Y, mw, mh);
                // A held press becomes a drag once the pointer has moved three pixels either
                // way, and a discipline slot under it then goes on the cursor.
                PartyMove(e.X, e.Y, mw, mh);                     // a carried portrait (ours, `partyorder`)
                if (discDrag.Move(e.X, e.Y, (wx, wy) => DisciplineUnder(wx, wy),
                                  advancement is not null, location.Steered?.Actor?.Held is not null) is { } lifted)
                    cursor.Dragging = lifted.Definition.Icon;
                break;
        }
    }
}
