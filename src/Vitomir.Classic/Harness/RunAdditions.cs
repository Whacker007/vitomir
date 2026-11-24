// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a program that hosts the runs can add to them: switches of its own and an observer of each game run.
using Vitomir.Engine.Audio;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir;

/// <summary>
/// **An addition to the runs** by the program that hosts them: the switches it answers and an observer of each game run
/// (a log written about the run, a harness that drives it). The program adds it before the first run
/// (<see cref="RunAdditions.Add"/>); <c>vitomir-tools</c> adds none. The switches are sorted as the runs' own are: those that
/// only set a run up are carried to the runs the menus start (<see cref="RunSettings"/>), those that name a file a recording
/// writes are left out of its replay (<c>InputTape</c>), and those that make a launch a harness run give it the harness's
/// seed (<see cref="Vitomir.Engine.Ai.Chance.IsHarness"/>).
/// </summary>
public abstract class RunAddition
{
    /// <summary>The switches with a value that only set a run up, carried to the runs the menus start.</summary>
    public virtual IReadOnlyList<string> ValuedSettings => [];

    /// <summary>The switches without a value that only set a run up, carried the same way.</summary>
    public virtual IReadOnlyList<string> BareSettings => [];

    /// <summary>The switches naming a file a recording writes as it is played, which its replay leaves out of the launch.</summary>
    public virtual IReadOnlyList<string> Outputs => [];

    /// <summary>Whether these switches make the launch a harness run (seed 1 unless <c>--seed</c> is given).</summary>
    public virtual bool IsHarness(Func<string, bool> flag) => false;

    /// <summary>An observer of this game run, or null when its switches ask for none; asked once a run, when its place is up.</summary>
    public virtual IRunObserver? Open(RunView run) => null;
}

/// <summary>The additions the host program made (<see cref="RunAddition"/>), in the order it added them.</summary>
public static class RunAdditions
{
    static readonly List<RunAddition> all = [];

    /// <summary>Adds one, before the first run.</summary>
    public static void Add(RunAddition addition)
    {
        ArgumentNullException.ThrowIfNull(addition);
        lock (all) all.Add(addition);
    }

    /// <summary>Every addition made.</summary>
    public static IReadOnlyList<RunAddition> All
    {
        get { lock (all) return [.. all]; }
    }

    /// <summary>Whether any addition names <paramref name="flag"/> among the switches <paramref name="of"/> gives.</summary>
    internal static bool Names(string flag, Func<RunAddition, IReadOnlyList<string>> of) => All.Any(a => of(a).Contains(flag));

    /// <summary>Whether any addition makes a launch with these switches a harness run.</summary>
    public static bool IsHarness(Func<string, bool> flag) => All.Any(a => a.IsHarness(flag));
}

/// <summary>
/// **An observer of one game run** (<see cref="RunAddition.Open"/>): told when the run's place is up, at each step of a settle
/// (<c>--settle</c>) and each frame of a window, and when each ends. Every call has the run as it is then
/// (<see cref="RunView"/>). Each method does nothing unless the observer gives it a body.
/// </summary>
public interface IRunObserver
{
    /// <summary>The place is up and the harness's own orders are read (<c>--cast</c>, <c>--use</c>, <c>--journey</c>).</summary>
    void PlaceReady(RunView run) { }

    /// <summary>A settle of <paramref name="seconds"/> of game time begins.</summary>
    void SettleStarted(RunView run, float seconds) { }

    /// <summary>A settle's step, <paramref name="at"/> seconds in: the world has stepped and the harness's orders are given; the story has not moved on yet.</summary>
    void SettleOrders(RunView run, float at) { }

    /// <summary>A settle's step, <paramref name="at"/> seconds in, done: the story and the network game have moved on too.</summary>
    void SettleStepped(RunView run, float at) { }

    /// <summary>The settle of <paramref name="seconds"/> is over.</summary>
    void SettleEnded(RunView run, float seconds) { }

    /// <summary>The window's loop begins (a run that is not a settle and not a still).</summary>
    void WindowOpened(RunView run) { }

    /// <summary>One frame of the window, once its input is read and before its world steps.</summary>
    void WindowFrame(RunView run) { }

    /// <summary>The window's loop is over.</summary>
    void WindowClosed(RunView run) { }
}

/// <summary>
/// **A game run as an observer sees it** (<see cref="IRunObserver"/>): its switches, its place, its clock, its camera and
/// sound, its menus and pointer, and its interface's passes. Every property is read live: the place changes under a run that
/// takes an exit, the passes come and go as the player opens them.
/// </summary>
public sealed class RunView
{
    readonly Vitomir.Classic.ClassicGame game;

    internal RunView(Vitomir.Classic.ClassicGame game) => this.game = game;

    /// <summary>The run's command line.</summary>
    public IReadOnlyList<string> Args => game.RunArgs;

    /// <summary>The value given after <paramref name="name"/>, or null.</summary>
    public string? Arg(string name) => game.RunArg(name);

    /// <summary>Whether <paramref name="name"/> is given.</summary>
    public bool Flag(string name) => game.RunFlag(name);

    /// <summary>The level's name, as the run opened it.</summary>
    public string Level => game.RunLevel;

    /// <summary>Whether the run is a settle (<c>--settle</c>) or a still (<c>--headless</c>), which open no window's loop.</summary>
    public bool SettleOrStill => game.RunSettleOrStill;

    /// <summary>The place now.</summary>
    public Location Place => game.RunPlace;

    /// <summary>The game clock, in milliseconds.</summary>
    public int ClockMs => game.RunClockMs;

    /// <summary>The camera the world is drawn through.</summary>
    public WorldCamera Camera => game.RunCamera;

    /// <summary>The sound mixer, or null when the run has none.</summary>
    public Mixer? Mixer => game.RunMixer;

    /// <summary>The menus (the front end's, the escape menu, the options), their pointer and whether the game is paused.</summary>
    public FrontEnd Menus => game.RunMenus;

    /// <summary>The pointer's shape.</summary>
    public CursorState Cursor => game.RunCursor;

    /// <summary>Whether the run plays a network game.</summary>
    public bool Multiplayer => game.RunMultiplayer;

    /// <summary>The pointer in the panes' pixels.</summary>
    public (int X, int Y) PanePointer => game.RunPanePointer;

    /// <summary>The interface's passes: the status bar, the panes, the book, the conversation, the description boxes and the heads.</summary>
    public RunPasses Passes => game.RunPasses;
}

/// <summary>A run's interface passes (<see cref="RunView.Passes"/>), each the one the run draws at the moment it is read.</summary>
public sealed record RunPasses(
    StatusBarPass StatusBar, InventoryPass Inventory, InventoryPass Vault, CharacterPass Character, DisciplinePass Disciplines,
    QuestPass Quests, BuySellPass Shop, FeedBar Feed, ReadPass Book, Conversations Talk, ConversationPass Conversation,
    RolloverPass Rollover, HeadsPass Heads, InventoryHands? Hands);
