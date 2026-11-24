// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The process's loop of runs (once vitomir.exe's top-level statements; now the Classic module's library, any host's, a frame at a time).
using Vitomir.Classic;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Platform.Modules;
using Vitomir.Scripting;

namespace Vitomir;

/// <summary>
/// **The tools a run's start hands over to**: what <c>vitomir-tools</c> runs besides the game, each answering an exit code
/// when the command line asked for it, or null to go on. The start calls them at the points the old program ran them, in the
/// same order, so a tool's run is what it was. They are <c>vitomir-tools</c>'; the Classic module under the
/// platform's program has none.
/// </summary>
interface IRunTools
{
    /// <summary>Before the install is looked for: the data, export, relayout, probes, place, cook, convert, stream-fly and glTF commands (a cook may rewrite the arguments).</summary>
    int? Commands(ref string[] args);
    /// <summary>With the install's files: the interface art tool, <c>--sweep</c>, <c>--story-sweep</c>.</summary>
    int? WithFiles(string[] args, GameFiles files);
    /// <summary><c>--list-saves</c>.</summary>
    int? ListSaves(string[] args);
    /// <summary>After a save is read: <c>--chronicles</c>, <c>--saves</c>, <c>--gait</c>.</summary>
    int? Reports(string[] args, ChronicleSet chronicles, string gameDir, GameFiles files);
    /// <summary>Once the place is loaded: <c>--things</c>, <c>--scripts-all</c>, <c>--storm</c>, <c>--sky-groups</c>, <c>--modes</c>, <c>--find</c>.</summary>
    int? PlaceLoaded(string[] args, Location location, CodexScenes? scenes, GameFiles files, ChronicleSet chronicles, string levelName);
}

/// <summary>
/// **The process's runs, a frame at a time** (what <c>vitomir.exe</c>'s
/// top-level statements were, any host's; the pushed frame). The front end and a game are
/// each one run of a <see cref="RunStart"/> and a <see cref="ClassicFrontEnd"/> or a <see cref="ClassicGame"/>, and moving between
/// them — a new game, a load, back to the main menu — ends the run with the next one's arguments in
/// <see cref="IClassicHost.NextRun"/>. The window, the GPU and the host outlive the runs, so the picture never goes away; everything
/// the world owns is built afresh, which is what 1.1 does too (it tears the game down before a load and before the main
/// menu). The flags that only set a run up, which a run started from the menus inherits from the one that started it, are
/// <see cref="RunSettings"/>'s. A run given nothing else is the front end.
/// <para><b>The host steps it</b> (<see cref="Frame"/>, the platform's <c>IGameLoop.Frame</c>, once per frame): the
/// host gathers the frame's input and its time and hands them in; the frame is a run's start (its own frame: the arguments, the
/// install, a place loaded, nothing drawn) or one step of the run under way (the front end's, the opening films', the game's
/// with its film screen), and answers <see cref="LoopResult.Continue"/> (with the host's wait, when 1.1 waits), a restart when
/// the run ended and the next begins (started on the next frame), or <see cref="LoopResult.Quit"/> with the process's exit code
/// when none follows. Each loop the controller ran until then (the front end's, <c>PlayFilm</c>'s, the game's, this one's) is a
/// state of the one frame, cut at the top of its loop: everything it did from one poll of its input to the next is one frame,
/// in the same order.</para>
/// </summary>
sealed class ClassicProgram
{
    readonly IClassicHost host;
    readonly IRunTools? tools;
    readonly FrameInput input;
    string[] launch;
    RunStart? start;
    ClassicRun? run;

    /// <summary>
    /// The runs from <paramref name="args"/>, on <paramref name="host"/>: the tape reads its launch (<c>--record</c>, <c>--replay</c>,
    /// <c>--replay-check</c>) and its live side is the input the host hands in. Nothing runs until the first frame.
    /// </summary>
    public ClassicProgram(string[] args, IClassicHost host, IRunTools? tools)
    {
        launch = InputTape.Launch(args);                          // `--record`, `--replay`, `--replay-check` (InputTape)
        this.host = host;
        this.tools = tools;
        input = new FrameInput(host.Input);
        InputTape.Live = input;                                   // the tape's live side is the host's input, handed in
    }

    /// <summary>
    /// One frame of the process: the host's input since the last frame queued for the frame to read, then the run's start (a frame
    /// of its own) or the run's step; what the host does next.
    /// </summary>
    public LoopResult Frame(in HostFrame frame)
    {
        input.Hand(frame.Input);
        if (run is null)
        {
            host.NextRun = null;
            // The run's start (its arguments, its settings, a tool's or a report's exit, the install, a save), then the front end's
            // or the game's own start; the install's files are closed with the run.
            start = new RunStart(host, launch, tools);
            int? ended = start.Prepare();
            if (ended is null)
            {
                // Multiplayer's windowless runs (a host nobody plays on, a player that plays itself) are a run of their own.
                run = start.windowless ? NetRuns.Run(host, start, input)
                    : RunSettings.FrontEndWanted(start.args) ? new ClassicFrontEnd(host, start, input) : new ClassicGame(host, start, input, tools);
                ended = run.Begin((ulong)frame.TimeNs);
            }
            return ended is { } code ? End(code) : LoopResult.Continue;
        }
        var step = run.Step((ulong)frame.TimeNs);
        if (step.Ended is { } exit) return End(exit);
        return step.NextFrameNs > 0 ? LoopResult.ContinueAt((long)step.NextFrameNs) : LoopResult.Continue;
    }

    /// <summary>
    /// A run ended with <paramref name="code"/>: the next run's arguments, or the end of the process (its exit code: a check's answer).
    /// The host closes itself (the GPU, the window's place, the window and SDL) once the frames are over.
    /// </summary>
    LoopResult End(int code)
    {
        start?.Dispose();
        start = null;
        run = null;
        host.Runs++;
        // A level the host's renderer could not draw (ours: Direct3D 12's sampler heap): said once, and the process ends with its code.
        if (host.Stopped is not null) { host.NextRun = null; code = IClassicHost.StoppedExitCode; }
        if (host.NextRun is null) return LoopResult.Quit(InputTape.Exit(code));
        launch = host.NextRun;
        return LoopResult.Restart(launch, "the next run");
    }
}

/// <summary>
/// **One step of a run**: the run goes on (with the host clock its next frame begins no sooner than, or 0), or it ended with
/// the exit code given.
/// </summary>
readonly record struct RunStep(int? Ended, ulong NextFrameNs = 0)
{
    public static RunStep On => new(null);
    public static RunStep After(ulong timeNs) => new(null, timeNs);
    public static RunStep Done(int code) => new(code);
}
