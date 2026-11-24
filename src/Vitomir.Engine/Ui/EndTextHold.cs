// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What takes the end text down: the main sequence's state changes, a
// film's start and the console's revive. Read in a comparison with 1.1 of
// `Main` and built.
namespace Vitomir.Engine.Ui;

/// <summary>
/// **The end text** — "All coterie members are dead!" or the Humanity line, and
/// "Press Esc to load saved game." under it (<see cref="Captions.EndText"/>, put up when the player dies) —
/// **and everything in 1.1 that takes it down.** It is a floating pane that stays two hours, so it
/// stays until something lets it go, and in 1.1 a good deal does:
/// <list type="bullet">
/// <item>**any state set** (first thing, whatever the state): the load, the
/// end of a film (state 3), a quit to the menus;</item>
/// <item>**the world covered** — the play state 3 going to 4: the advancement screen,
/// the shop, the map, the Options from the escape menu,
/// and a left pane and a right pane up together (when the world's two edges meet);</item>
/// <item>**the world uncovered** — state 4 back to 3, from each of those closing;</item>
/// <item>**a film starting** (after the sounds are stopped);</item>
/// <item>the console's **revive** of a party character;</item>
/// <item>a place entered (dropped with every floating text, or let go at
/// the end of the load) and a new game.</item>
/// </list>
/// The escape menu itself is none of these (it is not a state change), so the words stay up
/// behind it, as its "Press Esc" says they will; but Options from it, and back, and they are gone for
/// good. Earlier, Vitomir took them down only on a journey or a new run.
///
/// The host puts the text up through <see cref="Put"/>, reports the world drawn or covered every frame
/// (<see cref="World"/>: Vitomir has no state word, and the change of the one thing state 4 means is the
/// same moment), and calls <see cref="LetGo"/> for the rest.
/// </summary>
public sealed class EndTextHold(Captions captions)
{
    Caption[]? up;
    bool? drawn;

    /// <summary>Whether the end text is up.</summary>
    public bool Up => up is not null;

    /// <summary>The panes that are up, or null.</summary>
    public IReadOnlyList<Caption>? Text => up;

    /// <summary>
    /// Puts the end text up (every floating text first taken down, then the pane).
    /// An empty set puts nothing up.
    /// </summary>
    public void Put(Caption[] text)
    {
        captions.Clear();
        foreach (var c in text) captions.Add(c);
        up = text.Length > 0 ? text : null;
    }

    /// <summary>Lets the end text go, if it is up (the pane closed and freed, the flag cleared).</summary>
    public void LetGo()
    {
        if (up is null) return;
        foreach (var c in up) captions.Remove(c);
        up = null;
    }

    /// <summary>
    /// A scene starting: with a scene in force the end text's pointer is dropped and
    /// **every** floating text taken down — a chapter title still fading among
    /// them; with none, the end text alone is let go.
    /// </summary>
    public void SceneStarted(bool withScene)
    {
        if (!withScene) { LetGo(); return; }
        up = null;
        captions.Clear();
    }

    /// <summary>
    /// The world is drawn this frame (state 3) or covered (state 4; <c>Pick.View</c>'s two
    /// edges and the four panes that cover the world themselves). A change either way lets the end text go;
    /// the first report only records.
    /// </summary>
    public void World(bool isDrawn)
    {
        if (drawn is { } was && was != isDrawn) LetGo();
        drawn = isDrawn;
    }
}
