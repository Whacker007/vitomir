// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Engine.Diagnostics;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Render.Classic;

namespace Vitomir.Modules.Classic;

/// <summary>
/// **1.1's frame, as a module's loop** (ours). Started with the program's host
/// (<see cref="LoopContext.Host"/>, the platform renderer's <see cref="IClassicHost"/>: <c>vitomir-platform --game classic</c>), the
/// loop is 1.1's frame controller, the module's library <c>Vitomir.Classic</c> — the process's runs, the front end, the game, its
/// films, the network's windowless runs — stepped a frame at a time by the program's frame loop (<c>ClassicProgram.Frame</c>),
/// as <c>vitomir-tools</c>' host steps it, with no tools (those are <c>vitomir-tools</c>'). Its things are in the module's
/// folders.
/// <para><b>Headless</b>: <c>--headless --frames n</c> is the same frame on the same host, made with no
/// window (<c>SdlHost.Windowless</c>: its picture a target of the run's size on the GPU, drawn and never shown), so every frame of a
/// headless run is the one frame a window's is. For a while the headless loop was a copy of its own — the place started by a
/// copy of the start (<c>ClassicPlace</c>), the settings read by a copy of the start's reading (<c>ClassicFolders</c>), a frame with
/// no input and no picture (the story's requests, the steered character asked to stand, the pointer's asks cleared,
/// <c>WorldFrame.Frame</c>, the sound's frame), the status bar's and the camera's state copied from the controller
/// (<c>ScreenState</c>, <c>Aim</c>), a digest of its own; it stopped at the story's first journey. It is gone: the frame is the
/// controller's, the digest the run's own (<c>--digest</c>, <c>--digest-check</c>: <c>InputTape</c>'s, as a recording writes its
/// digest), and the story's journeys are followed headless as in a window.</para>
/// <para>One Classic per process: the engine keeps its launch in statics (<c>Chance</c>, the Fixes, the options).</para>
/// </summary>
internal sealed class ClassicLoop : IGameLoop
{
    /// <summary>The loop plays on the program's host (the windowed game's door, IClassicGame, until then), with a window or without.</summary>
    public bool Windowed => true;

    /// <summary>The controller's runs, once started.</summary>
    global::Vitomir.ClassicProgram? runs;

    /// <summary>The switches that make a run a network game: a headless run refuses them (the dedicated host and the bot open no window without it).</summary>
    internal static readonly string[] NetworkSwitches = ["--host", "--join", "--headless-server", "--net-bot", "--mp-game"];

    /// <summary>The headless copy's snapshot measure (<c>--snapshot</c>), retired with it: said and taken out.</summary>
    internal const string SnapshotSwitch = "--snapshot";

    /// <summary>The tape's switches, which work on a run that shows its picture (the tape's <c>window</c> and <c>drawn</c> lines are a window's).</summary>
    internal static readonly string[] TapeSwitches = ["--record", "--replay", "--replay-check"];

    /// <summary>
    /// 1.1 played on the program's host (stepped by the host's frames): the controller made with 1.1's command line
    /// (<see cref="LoopContext.Args"/>). Its things in the module's folders ( and g): the player's
    /// settings (<c>vitomir.ini</c>), saves, multiplayer characters, logs and screenshots in the module's user folder, the installed
    /// settings layer read from the module's own folder, the scripts' cache in the context's cache folder (<see cref="HostFolders"/>),
    /// never the old program's <c>%LOCALAPPDATA%\Vitomir</c> nor the program's or the module's folder.
    /// </summary>
    public void Start(LoopContext context)
    {
        if (context.Host?.GetService(typeof(IClassicHost)) is not IClassicHost host)
            throw new ClassicStartException("the Classic game is played on the program's host (vitomir-platform --game classic; "
                + "--headless <file.png> for a still, --headless --frames <n> for frames with no window)");
        string[] args = [.. context.Args];
        // The copy's measure of the snapshot: every frame of the one frame fills it, a window's or a headless one's, so the switch
        // is said and taken out.
        if (args.Contains(SnapshotSwitch))
        {
            context.Say($"classic: {SnapshotSwitch}: every frame fills the snapshot (the headless loop's copy, which measured it, is retired)");
            args = [.. args.Where(a => a != SnapshotSwitch)];
        }
        if (context.Headless) args = Headless(args);
        Directory.CreateDirectory(context.UserFolder);
        HostFolders.Use(context.UserFolder, context.ModuleFolder, context.CacheFolder);
        context.Say($"classic: the game's folder {HostFolders.User} (its settings, saves, multiplayer characters, logs and screenshots); "
                  + $"installed settings from {HostFolders.Installed}; scripts' cache {Vitomir.Engine.Vfs.GameData.CodexCache}");
        runs = new global::Vitomir.ClassicProgram(args, host, tools: null);
    }

    /// <summary>
    /// **A headless run's command line**: the program hands the frames' count, their time and the picture's size
    /// (<c>--frames</c>, <c>--frame-ms</c>, <c>--size</c>, its defaults when not given); here the run is made the harness's, as the copy's
    /// was — seed 1 unless <c>--seed</c> (the old program's <c>--headless</c> rule, <c>Chance.IsHarness</c>), no sound and no films —
    /// and a network game or a tape refused in one line, as before.
    /// </summary>
    internal static string[] Headless(string[] args)
    {
        if (NetworkSwitches.FirstOrDefault(args.Contains) is { } network)
            throw new ClassicStartException($"{network}: a network game is not played headless (--headless --frames); "
                + "host or join without --headless (a window), take a still with --headless <file.png> --settle <seconds>, "
                + "or run --headless-server or --net-bot, which open no window");
        if (TapeSwitches.FirstOrDefault(args.Contains) is { } tape)
            throw new ClassicStartException($"{tape}: a tape works on a run that shows its picture: leave out --headless "
                + "(SDL_VIDEO_DRIVER=offscreen runs it with no window on the screen)");
        var made = new List<string>(args);
        if (!made.Contains("--seed") && !made.Contains("--ai-seed"))
            made.AddRange(["--seed", Vitomir.Engine.Ai.Chance.HarnessSeed.ToString(CultureInfo.InvariantCulture)]);
        foreach (var quiet in new[] { "--mute", "--no-video" })
            if (!made.Contains(quiet)) made.Add(quiet);
        return [.. made];
    }

    public LoopResult Frame(in HostFrame host) =>
        (runs ?? throw new InvalidOperationException("the Classic loop was not started")).Frame(host);

    public void Dispose()
    {
        if (runs is null) return;
        runs = null;
        HostFolders.Clear();
    }
}

/// <summary>A start the Classic loop refuses, in one line (the program logs it and ends with exit code 1).</summary>
internal sealed class ClassicStartException(string message) : Exception(message);
