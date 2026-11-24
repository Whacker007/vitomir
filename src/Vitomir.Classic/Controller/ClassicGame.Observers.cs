// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The run's observers: the host program's additions told what the run does (Harness/RunAdditions.cs).
using Vitomir;

namespace Vitomir.Classic;

unsafe sealed partial class ClassicGame
{
    /// <summary>The observers of this run (<see cref="RunAddition.Open"/>), opened when its place is up; none until then.</summary>
    List<IRunObserver> observers = [];

    RunView? view;

    /// <summary>This run as its observers see it.</summary>
    RunView View => view ??= new RunView(this);

    /// <summary>Asks each addition for an observer of this run, then tells them the place is up.</summary>
    void OpenObservers()
    {
        observers = [.. RunAdditions.All.Select(a => a.Open(View)).OfType<IRunObserver>()];
        foreach (var o in observers) o.PlaceReady(View);
    }

    /// <summary>Tells every observer of this run something.</summary>
    void Tell(Action<IRunObserver, RunView> what)
    {
        foreach (var o in observers) what(o, View);
    }

    internal IReadOnlyList<string> RunArgs => args;
    internal string? RunArg(string name) => Arg(name);
    internal bool RunFlag(string name) => Flag(name);
    internal string RunLevel => levelName;
    internal bool RunSettleOrStill => Arg("--settle") is not null || headless is not null;
    internal global::Vitomir.Engine.World.Location RunPlace => location;
    internal int RunClockMs => frame.Sim.Milliseconds;
    internal global::Vitomir.Engine.World.WorldCamera RunCamera => worldCam;
    internal global::Vitomir.Engine.Audio.Mixer? RunMixer => audio.Mixer;
    internal global::Vitomir.Engine.Ui.FrontEnd RunMenus => menus;
    internal global::Vitomir.Engine.Ui.CursorState RunCursor => cursor;
    internal bool RunMultiplayer => net is not null;
    internal (int X, int Y) RunPanePointer => PanePointer();
    internal RunPasses RunPasses => new(hud, invPass, vaultPass, charPass, discPass, questPass, buyPass, feedBar, readPass, talk, talkPass,
                                        rolloverPass, headsPass, hands);
}
