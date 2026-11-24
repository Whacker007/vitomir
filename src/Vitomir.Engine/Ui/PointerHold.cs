// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The pointer held inside the screen, as 1.1's own pointer is,
// so that a push against the edge keeps turning the camera.
namespace Vitomir.Engine.Ui;

/// <summary>
/// **Why the edge turn needs the pointer held.** 1.1's pointer is its own: every frame it moves by
/// the mouse's mickeys, as DirectInput reports them, and is then held
/// inside a limit rectangle, the screen. So a pointer pushed against
/// the side of the screen stays there, the mouse keeps
/// reporting its movement, and the edge strip keeps adding it to the turn rate: push
/// harder, turn faster; ease off, and the turn goes on at the rate the push built up (the rates are
/// only zeroed with the pointer away from the edge).
///
/// A window's pointer is the system's, and pushed against the window's side it simply leaves — or,
/// held in, stops reporting movement. So while the game camera has the mouse, Vitomir **confines**
/// the pointer to the window (SDL's mouse grab), and while it is in an edge strip it **holds** it
/// there in relative mode — which reports every mickey however hard the mouse is pushed, and moves
/// the pointer by them clamped to the window, exactly as 1.1 does. When the pointer comes away
/// from the edge the hold lets go and the system pointer is put where the game's is. The game draws
/// its own pointer (the system one is hidden), so none of this shows.
///
/// This decides; the host makes the calls (<see cref="Change"/>). The swivel (X held) has relative
/// mode of its own, and the hold stands aside for it.
/// </summary>
public sealed class PointerHold
{
    /// <summary>Whether the pointer is kept inside the window (SDL's mouse grab).</summary>
    public bool Confined { get; private set; }

    /// <summary>Whether it is held at the edge in relative mode.</summary>
    public bool Holding { get; private set; }

    /// <summary>
    /// What the host has to do this frame: <see cref="Confine"/> the grab on or off, and
    /// <see cref="Hold"/> relative mode on (true) or off (false) — off with the system pointer put
    /// back where the game's is. Null leaves each as it is.
    /// </summary>
    public readonly record struct Change(bool? Confine, bool? Hold);

    /// <summary>
    /// One frame's decision. <paramref name="worldHasMouse"/>: the game camera is up and taking the
    /// world's input (no menu, no shop, not paused), which is when the edge turn is read at all.
    /// <paramref name="atEdge"/>: the pointer was in one of the four edge strips
    /// (<c>WorldCamera.EdgeTurn</c>). <paramref name="swivelling"/>: the swivel has relative mode.
    /// </summary>
    public Change Frame(bool worldHasMouse, bool atEdge, bool swivelling)
    {
        bool? confine = null, hold = null;
        if (worldHasMouse != Confined)
        {
            Confined = worldHasMouse;
            confine = worldHasMouse;
        }
        bool want = worldHasMouse && atEdge && !swivelling;
        if (want != Holding)
        {
            Holding = want;
            // The swivel took relative mode over: it lets go of it itself, back where it began.
            if (!(swivelling && !want)) hold = want;
        }
        return new Change(confine, hold);
    }

    /// <summary>Everything let go at once — the window closing, or a journey starting.</summary>
    public Change Release()
    {
        var change = new Change(Confined ? false : null, Holding ? false : null);
        Confined = Holding = false;
        return change;
    }
}
