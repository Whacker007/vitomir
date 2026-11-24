// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Options screen's hand on the running program: what each setting does to the game, the mixer,
// the window and the renderer. The screen itself is `Engine.Ui.OptionsMenu` and knows none of these.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Platform.Windowing;

namespace Vitomir;

/// <summary>
/// Gives a <see cref="FrontEnd"/> its Options screen and says what each setting does here.
/// Every setting, and when it applies:
///
/// - **taken up as it moves**: brightness and both volumes;
/// - **taken up on OK**: the video mode (the full-screen mode while full screen, the window's size while not), no blood, subtitles, the party AI's boxes (folded into the
///   AI flags as 1.1 does), the Fixes page's switches and the key page's keys — and mouse pitch,
///   camera slewing, repeat attack and the quick-slot autoload, which the game reads as it needs them;
/// - **kept and not used**, because Vitomir has nothing they could change: the device, the speaker
///   type and the provider;
/// - **kept and inert by decision**: the detail level and the Advanced screen — Vitomir draws 1.1
///   maxed out whatever they say (<c>DetailPresets.Maxed</c>). The Display screen now does not show them
///   (<c>OptionsMenu.InertDisplayControls</c>: not drawn, not hit), so the Advanced screen is not reached from it.
///
/// Every choice is also kept for the session (<see cref="MenuChoices"/>), so the next run keeps it.
/// </summary>
static class OptionsHost
{
    /// <summary>The one provider Vitomir's mixer is (ours: 1.1 lists what Miles finds on the machine).</summary>
    public const string Provider = "Vitomir software mixer";

    /// <summary>
    /// The screen, attached to <paramref name="menus"/>. <paramref name="window"/> (the host's window modes) may be
    /// null (a headless run), in which case the mode list is the one size the run was asked for.
    /// </summary>
    public static OptionsMenu Attach(FrontEnd menus, Func<string, string?> strings, GameAudio sound,
                                     IWindowModes? window, int width, int height, string gpuDriver)
    {
        var options = new OptionsMenu(menus, strings, GameOptions.Current);
        menus.Options = options;
        var win = window;
        options.MusicVolume = v => sound.SetMusicVolume(v);
        options.EffectsVolume = v => sound.Mixer.SetEffectsVolume(v);
        // The device list is the four window modes (ours): the row in use, the
        // video modes each row offers, and OK makes the row and the mode the window's at once. A
        // headless run has the one row it always had.
        options.Devices = () => win is not null ? FullScreen.WindowModes : [$"Vitomir ({gpuDriver})"];
        if (win is not null)
        {
            options.CurrentDevice = win.Mode;
            options.ModesFor = row => [.. win.ModesFor(row).Select(m => new VideoMode(m.Width, m.Height, 32))];
            options.CurrentModeFor = row => { var m = win.CurrentFor(row); return new VideoMode(m.Width, m.Height, 32); };
            options.SetDisplay = (row, mode) => win.SetDisplay(row, mode.Width, mode.Height);
        }
        // Full screen, the display's own modes and the mode in use (FullScreenHost); a window, the window's sizes.
        options.CurrentMode = () =>
        {
            if (win is not null && win.IsFull) { var m = win.Current(); return new VideoMode(m.Width, m.Height, 32); }
            int w = width, h = height;
            if (win is not null) (w, h) = win.WindowSize();
            return new VideoMode(w, h, 32);
        };
        options.Modes = () =>
        {
            // Borderless is at the desktop's own size and no other: one row, so that picking a
            // mode there cannot quietly turn it into the other kind of full screen.
            if (win is not null && win.Mode() == FullScreen.ModeBorderless && win.Desktop() is { } desk)
                return [new VideoMode(desk.Width, desk.Height, 32)];
            if (win is not null && win.FullModes() is { } full) return [.. full.Select(m => new VideoMode(m.Width, m.Height, 32))];
            var now = options.CurrentMode!();
            var modes = new List<VideoMode>(StandardModes);
            if (!modes.Contains(now)) modes.Add(now);
            return [.. modes.OrderBy(m => m.Width).ThenBy(m => m.Height)];
        };
        options.SetMode = mode =>
        {
            if (win is null) return true;
            int kind = win.Mode();
            if (kind == FullScreen.ModeBorderless) return true;                     // the desktop's size: nothing to set
            if (kind == FullScreen.ModeFull) return win.SetFullMode(mode.Width, mode.Height);
            return win.SetWindowSize(mode.Width, mode.Height);
        };
        options.Fixes.Changed += (fixes, qol) =>
        {
            // Into the registers already installed, not new ones: the program holds on to those.
            foreach (var f in fixes.All) Fixes.Current[f.Name] = f.On;
            foreach (var q in qol.All) Qol.Current[q.Name] = q.On;
            MenuChoices.Session.Chose(fixes, qol);              // and kept over the next run's command line (ours)
            Vitomir.Engine.Net.HostSwitches.Changed();          // in someone else's game, theirs again over these
            Console.WriteLine($"fixes: {Fixes.Current}; quality of life: {Qol.Current}");
            Vitomir.Engine.Diagnostics.Diag.InForce("the Fixes page");
        };
        // Streamer mode: the platform's setting, in force at once and kept in the platform's file (a replay keeps nothing).
        options.Fixes.StreamerChanged += on =>
        {
            if (InputTape.Replaying) Vitomir.Platform.Presentation.StreamerMode.On = on;
            else Vitomir.Platform.Presentation.StreamerMode.Set(on);
            Console.WriteLine($"streamer: {(on ? "on" : "off")} (the Fixes page)");
        };
        options.Keys.Changed += keys =>
        {
            KeyBindings.Current = keys;
            MenuChoices.Session.Chose(keys);
            Console.WriteLine($"keys: {keys}");
        };
        options.Applied += o => Console.WriteLine($"options: {OptionsMenu.Describe(o)}");
        options.Applied += Vitomir.Engine.Ai.MainFlagsWord.Applied;          // the word between games
        return options;
    }

    /// <summary>The window sizes the Display screen offers a window beside the one in use (ours: 1.1 lists the card's modes, as a full screen's list does).</summary>
    static readonly VideoMode[] StandardModes =
    [
        new(640, 480, 32), new(800, 600, 32), new(1024, 768, 32), new(1280, 720, 32), new(1280, 1024, 32),
        new(1600, 900, 32), new(1920, 1080, 32),
    ];

    /// <summary>
    /// A game's share: the settings put in force now, and again on every OK — no blood, subtitles, and
    /// the party AI flags of whatever place is current (<paramref name="minds"/> is asked each time,
    /// because a journey builds new ones).
    /// </summary>
    public static void InGame(FrontEnd menus, Action<float> gamma, Conversations talk, Func<Minds?> minds)
    {
        void Take(GameOptions o)
        {
            Gore.NoBlood = o.Has(GameOptions.NoBlood);
            talk.Subtitles = o.Has(GameOptions.ForceSubtitles);
            if (talk.Current is { } now) now.Subtitles = talk.Subtitles;      // a conversation under way too, from its next line
            if (minds() is { } m) m.MainFlags = o.FoldInto(m.MainFlags);
        }
        Take(GameOptions.Current);
        if (menus.Options is not { } options) return;
        options.Gamma = gamma;
        options.Applied += Take;
    }

    /// <summary>
    /// `--menu-open options` and its pages, for a headless still: the Options screen, then the
    /// screen named on top of it. Answers false for anything else, which the caller's own list opens.
    /// </summary>
    public static bool Open(FrontEnd menus, string? what)
    {
        if (menus.Options is not { } options || what is null) return false;
        switch (what.ToLowerInvariant())
        {
            case "options": options.Open(); break;
            case "gameplay": options.Open(); options.OpenGameplay(); break;
            case "controls": options.Open(); options.OpenControls(); break;
            case "display": options.Open(); options.OpenDisplay(); break;
            case "advanced": options.Open(); options.OpenDisplay(); options.OpenAdvanced(); break;
            case "sound": options.Open(); options.OpenSound(); break;
            case "fixes": options.Open(); options.Fixes.Open(); break;
            case "keys":                       // where its way in is: the Options screen's keybinds row in our
                options.Open();                // lettering, the Controls screen's Keys in 1.1's
                if (MenuLettering.Mode != MenuLetteringMode.Ours) options.OpenControls();
                options.Keys.Open();
                break;
            default: return false;
        }
        return true;
    }

    /// <summary>A game's map (<see cref="MapScreen"/>), with "no map" going to the message log.</summary>
    public static void AttachMap(FrontEnd menus, Func<string, string?> strings, Action<string> post)
    {
        menus.Map = new MapScreen(menus, strings);
        menus.Map.Posted += post;
    }

    /// <summary>
    /// M, or the map button: the map of the place the party is in. The chronicle's own
    /// record of where it is decides; a harness run started at a level the chronicle was not told
    /// about is matched by the level's name instead (ours).
    /// </summary>
    public static bool OpenMap(FrontEnd menus, Chronicle? chronicle, Vitomir.Engine.Vfs.GameFiles files, string levelName)
    {
        if (menus.Map is not { } map || menus.Up && map.Pane is null) return false;
        var here = chronicle is { Current: >= 0 } c ? c.At(c.Current)
                 : chronicle?.Data.Locations.FirstOrDefault(l =>
                       Path.GetFileNameWithoutExtension(l.Level).Equals(levelName, StringComparison.OrdinalIgnoreCase));
        bool up = map.Open(here, chronicle?.Data.Locations ?? [], file => files.TryRead("ui/" + file, out _));
        Console.WriteLine(up ? $"  map: {here?.MapFile}, {here?.MapMarker} is here" : "  map: none for this place");
        return up;
    }

    /// <summary>The party AI flags of a newly built place, with the options folded in (1.1 does it once at start-up, over a word that outlives every place).</summary>
    public static void Fold(Minds? minds)
    {
        if (minds is not null) minds.MainFlags = GameOptions.Current.FoldInto(minds.MainFlags);
    }
}
