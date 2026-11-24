// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One run of the program, the front end's or a game's: what both start from and the menus as both drive them.
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
/// One run of the program: the front end (<see cref="ClassicFrontEnd"/>)
/// or a game (<see cref="ClassicGame"/>). What both start from — the arguments, the install, the window's size — the
/// window and the GPU they ask the host for, and the menus as both states drive them.
/// </summary>
unsafe abstract class ClassicRun
{
    protected readonly IClassicHost host;
    /// <summary>The input the host handed in, waiting to be read: the tape's live side, and the opening films' own.</summary>
    protected readonly FrameInput input;
    /// <summary>The host's clock when this frame began (<see cref="HostFrame.TimeNs"/>): what the frame's time is read from.</summary>
    protected ulong frameNs;
    protected string[] args;
    protected string? headless;
    protected global::System.Collections.Generic.List<float[]> clickQueue = null!;
    protected bool clickDown;
    protected int width;
    protected int height;
    protected global::Vitomir.Engine.Vfs.GameFiles files = null!;

    protected ClassicRun(IClassicHost host, RunStart start, FrameInput input)
    {
        this.host = host;
        this.input = input;
        args = start.args;
        headless = start.headless;
        clickQueue = start.clickQueue;
        clickDown = start.clickDown;
        width = start.width;
        height = start.height;
        files = start.files;
    }

    /// <summary>
    /// **The run's own start**, in the frame its <see cref="RunStart"/> prepared it in (a frame of its own: nothing of the run
    /// is stepped in it): what the run's loop did before its first turn. Answers an exit code when the run is over already (a
    /// still, a tool's report), or null to go on to <see cref="Step"/> on the next frames. <paramref name="now"/> is the frame's time.
    /// </summary>
    internal abstract int? Begin(ulong now);

    /// <summary>
    /// **One frame of the run**: one turn of the loop the run ran until then, from one poll of its input to the next, in the
    /// same order; <paramref name="now"/> is the frame's time, which the frame's clock readings are.
    /// </summary>
    internal abstract RunStep Step(ulong now);

    protected string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

    protected bool Flag(string name) => Array.IndexOf(args, name) >= 0;

    /// <summary>The window pixel of the last pointer event the menus were given (<see cref="MenuInput"/>); null before the first.</summary>
    protected (float X, float Y)? menuPixel;

    protected IEnumerable<string> Args(string name) => args.Select((a, i) => (a, i)).Where(t => t.a == name && t.i + 1 < args.Length).Select(t => args[t.i + 1]);

    protected float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);


    // ---- saving ------------------------------------------------------------------------------------

    // Where the player's saves live: beside their settings and logs, never in the install — or, in a replay
    // of a tape that carries its saves, the replay's own scratch copy (InputTape.SaveFolder, TapeSaves).
    internal static string SaveFolder() => InputTape.SaveFolder();


    // A save named on the command line: a path as given, or a bare name looked for in the save folder.
    internal static string ResolveSave(string name)
    {
        if (File.Exists(name) || Directory.Exists(name)) return name;
        if (name.IndexOfAny(['/', '\\']) >= 0) return name;
        return Path.Combine(SaveFolder(), name.EndsWith(SaveArchive.Extension, StringComparison.OrdinalIgnoreCase)
            ? name : name + SaveArchive.Extension);
    }


    // SDL, the window and the GPU: the host's, made by the first run that needs them and kept by the rest.
    protected bool OpenWindow(string title) =>
        host.OpenWindow(title, new WindowAsk(headless is not null, width, height, Arg("--size") is not null, Flag("--window"), Flag("--fullscreen"),
            Harness: Arg("--ui-click") is not null || Arg("--frames") is not null || Arg("--frame-ms") is not null || Arg("--shot") is not null,
            Borderless: Arg("--borderless"))     // ours: `--borderless WxH`, `[window] borderless` (Borderless)
        {
            // What the host's window code asked of the tape, the settings' file and the log itself until then.
            Replaying = InputTape.Replaying,
            Unwatched = InputTape.Unwatched,
            ReplayFull = InputTape.Full,
            ReplaySlot = InputTape.Slot,
            Settings = WindowKept(),
            Log = WindowLog,
        });

    /// <summary>
    /// Where the window keeps <c>[window]</c>: the host's when it keeps them (the platform's program, per game: ),
    /// never written in a replay; else the player's <c>vitomir.ini</c>, as always (<c>vitomir-tools</c>' runs).
    /// </summary>
    Vitomir.Platform.Windowing.IWindowSettings WindowKept() =>
        host.WindowSettings is { } hosts
            ? (InputTape.Replaying ? new Vitomir.Platform.Windowing.ReadOnlyWindowSettings(hosts) : hosts)
            : new IniWindowSettings();

    /// <summary>The window's lines in the game's log, as the window code wrote them there itself until then.</summary>
    static void WindowLog(string line) =>
        Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(Diag.For("Vitomir.Window"), "{Line}", line);

    // `--gpu` names a driver (ours: GpuDrivers); `--gpu-debug` its validation, `--no-vsync` no wait for the screen.
    // The validation only with the log on (the host asked Diag.Enabled itself until then).
    protected IClassicRenderer OpenGpu() => host.OpenGpu(new GpuAsk(Arg("--gpu"), Flag("--gpu-debug") && Diag.Enabled, Flag("--no-vsync"),
        Unwatched: InputTape.Unwatched ? (InputTape.Checking ? "replay-check" : "replay") : null));   // a check runs on its tape's steps


    // ---- the menus -----------------------------------------------------------------------------

    // The menus as either state has them: the panes out of the install, the strings, the save
    // slots in the player's save folder, and the interface's own sounds.
    protected FrontEnd NewMenus(UiAssets assets, Vitomir.Engine.Ui.Strings text, GameAudio sound, int state)
    {
        var made = new FrontEnd(name => assets.Pane(name), name => assets.Font(name), text.Find)
        {
            State = state,
            Dress = MenuLettering.Dresser(assets),                     // our lettering on 1.1's screens
            Trail = InputTape.Menu,                                    // each step through the menus, for a tape's check
            SaveSlots = new SaveSlotFiles(SaveFolder(), NetPlay.Active ? SaveSlotFiles.Network : SaveSlotFiles.Single),
            // 1.1's own saves are offered on the load screen only when asked (`--offer-imports`):
            // the screen lists Vitomir's saves, and an import is its own act (`--import <save>`).
            OriginalSaves = NetPlay.Active || !Flag("--offer-imports") ? null : OriginalSaves.For(files.Root).List,
            Sound = slot => sound.Ui(slot),
            Credits = () => files.TryRead(NlsFile.PathFor("credits"), out var data)
                ? FrontEnd.ParseCredits(System.Text.Encoding.Latin1.GetString(data)) : [],
        };
        made.NotWritten += what => Console.WriteLine($"  menu: the {what} screen is not written yet");
        OptionsHost.Attach(made, text.Find, sound, host.Modes, width, height, host.GpuDriver);
        return made;
    }


    // A pane's music: the track found, and started unless it is the one already playing.
    protected void MenuMusic(GameAudio sound, string track)
    {
        if (sound.Bank.Load(track, stream: true) is not { } music) return;
        if (ReferenceEquals(sound.Music, music) && sound.MusicChannel is not null) { sound.Bank.Release(music); return; }
        sound.StartMusic(music);
    }


    // Slot n's file, for a load or a save the menus asked for.
    protected string SlotPath(int slot) => new SaveSlotFiles(SaveFolder(), NetPlay.Active ? SaveSlotFiles.Network : SaveSlotFiles.Single).PathOf(slot);


    // The menus' share of the input events: the mouse, the two keys their
    // hooks read, and the typing (`WM_CHAR`). True when the event was theirs — which, while one of
    // them is up, is every one of these.
    protected bool MenuInput(FrontEnd menus, bool windowed, in LoopInput e)
    {
        int w = width, h = height;
        if (windowed) host.PictureSize(&w, &h);
        // Where the pointer was last told to the menus, in the window's pixels: the sheet's seat can change under a
        // pointer that has not moved (ClassicGame.SeatFrame).
        if (e.Kind is LoopInputKind.PointerMove || (e.Kind is LoopInputKind.ButtonDown or LoopInputKind.ButtonUp && e.Code == LoopButton.Left))
            menuPixel = (e.X, e.Y);
        // The key page waiting for a key takes the next one, whatever it is (`KeyInput.Capture`).
        if (menus.Options?.Keys is { Capturing: true } keysPage && KeyInput.Capture(keysPage, menus, e)) return true;
        switch (e.Kind)
        {
            case LoopInputKind.PointerMove:
            {
                var (mx, my) = StatusBar.FromWindow(e.X, e.Y, w, h);
                menus.PointerAt(mx, my);
                return true;
            }
            case LoopInputKind.ButtonDown or LoopInputKind.ButtonUp:
            {
                if (e.Code != LoopButton.Left) return true;
                var (bx, by) = StatusBar.FromWindow(e.X, e.Y, w, h);
                menus.PointerAt(bx, by);
                menus.LeftHeld = e.Kind == LoopInputKind.ButtonDown;
                if (menus.LeftHeld) menus.Press(); else menus.Release();
                return true;
            }
            case LoopInputKind.Wheel:
                menus.Wheel(e.DY > 0 ? 1 : e.DY < 0 ? -1 : 0);
                return true;
            case LoopInputKind.KeyDown:
                // Escape and Enter are read as "pressed this frame" and never repeat; backspace is
                // a typed character, and repeats as typing does.
                if (e.Key == LoopKey.Backspace) menus.Type('\b');
                else if (e.Repeat) return true;
                else if (e.Key == LoopKey.Escape) menus.Key(MenuKey.Escape);
                else if (e.Key is LoopKey.Return or LoopKey.KeypadEnter) menus.Key(MenuKey.Enter);
                else if (e.Key == LoopKey.M) menus.Key(MenuKey.Map);
                else if (e.Key == LoopKey.Tab) menus.Type('\t', (e.Mods & LoopMods.Shift) != 0);
                else if (e.Key == LoopKey.V && (e.Mods & LoopMods.Ctrl) != 0)
                    menus.Paste(InputTape.Clipboard(host.Clipboard));      // Ctrl+V, character 0x16
                else if (e.Key == LoopKey.C && (e.Mods & LoopMods.Ctrl) != 0)
                    ClipboardKeys.Copy(menus.CopyText);                                          // Ctrl+C, ours
                return true;
            case LoopInputKind.KeyUp:
                return true;
            case LoopInputKind.Text:
                foreach (char c in e.Text) menus.Type(c);
                return true;
        }
        return false;
    }


    // The keyboard's text only while a field has it, so a game key is never a typed letter.
    protected void TextInput(FrontEnd menus, bool windowed)
    {
        bool want = menus.Active() is { Focus: not null } pane && pane.Edits.Count > 0;
        if (!windowed || want == host.Typing) return;
        host.Typing = want;
        host.TextInput(want);
    }


    // `--menu-click` with a window: the clicks made one a frame, press then release, once the
    // run has drawn a few frames — a smoke test of the menus' own path, restarts included.
    protected void ScriptedClick(FrontEnd menus, int frame)
    {
        if (headless is not null || clickQueue.Count == 0 || frame < 10) return;
        var (ux, uy) = StatusBar.FromWindow(clickQueue[0][0], clickQueue[0][1], width, height);
        menus.PointerAt(ux, uy);
        menus.LeftHeld = !clickDown;
        if (!clickDown) menus.Press();
        else
        {
            menus.Release();
            Console.WriteLine($"  menu: clicked {clickQueue[0][0]:0},{clickQueue[0][1]:0}");
            clickQueue.RemoveAt(0);
        }
        clickDown = !clickDown;
    }


    // `--menu-open`, then `--menu-click` and `--pointer`, for a headless still of the menus.
    protected void MenuOpen(FrontEnd menus, Vitomir.Engine.Ui.Strings strings0)
    {
        string? opening = Arg("--menu-open") ?? (Flag("--options") ? "options" : null);
        if (OptionsHost.Open(menus, opening)) { menus.Frame(menus.NowMs); menus.Drawn(); return; }
        switch (opening?.ToLowerInvariant())
        {
            case "title": menus.ClearLayers(); menus.Floating.Clear(); break;
            case "sp": menus.Hide(menus.Pane(FrontEnd.MainMenuFile)); menus.OpenSp(); break;
            case "credits": menus.OpenCredits(); break;
            case "load": menus.OpenLoad(); break;
            case "save": menus.OpenSave(); break;
            case "escape": menus.OpenEscape(); break;
            case "message":
                menus.Message(strings0.Find("UI_WARNING") ?? "", strings0.Find("UI_SPLOADSAVE_OVERWRITESAVE") ?? "", true, true);
                break;
            case "input":
                menus.Input(strings0.Find("UI_SPLOADSAVE_ENTERNAME") ?? "", menus.Describe?.Invoke() ?? "", FrontEnd.NameLimit);
                break;
            case "dropcash": Vitomir.Engine.Ui.DropCash.Ask(menus, null, strings0.Find); break;   // the coin button's box
            // `pane:<file>` (ours): any pane of the install shown by itself, for a still of it (the multiplayer
            // screens, the loading plaque), without the screen's own wiring.
            case { } named when named.StartsWith("pane:", StringComparison.Ordinal):
                menus.ClearLayers();
                menus.Show(menus.Pane(opening![5..]));
                break;
        }
        menus.Frame(menus.NowMs);
        menus.Drawn();
    }

    protected void MenuClicks(FrontEnd menus)
    {
        if (Arg("--menu-click") is { } clicks)
            foreach (var at in clicks.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = at.Split(',').Select(F).ToArray();
                var (ux, uy) = StatusBar.FromWindow(p[0], p[1], width, height);
                menus.PointerAt(ux, uy);
                menus.Frame(menus.NowMs);
                menus.LeftHeld = true;
                menus.Press();
                menus.LeftHeld = false;
                menus.Release();
                menus.Frame(menus.NowMs);
                menus.Drawn();
                Console.WriteLine($"  menu: clicked {p[0]:0},{p[1]:0}");
            }
        if (Arg("--pointer") is { } rest)
        {
            var p = rest.Split(',').Select(F).ToArray();
            var (ux, uy) = StatusBar.FromWindow(p[0], p[1], width, height);
            menus.PointerAt(ux, uy);
        }
        menus.Frame(menus.NowMs);
    }
}
