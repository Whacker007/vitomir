// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The swivel's hold on the mouse, kept honest, and the swivel lock (ours).
namespace Vitomir.Engine.Ui;

/// <summary>What the host does with the swivel's hold on the mouse this time.</summary>
public enum SwivelStep
{
    /// <summary>Leave it as it is.</summary>
    Nothing,
    /// <summary>Start the swivel: the pointer pinned, the mouse turning the game camera.</summary>
    Start,
    /// <summary>Let it go: the pointer back where it was.</summary>
    Stop,
}

/// <summary>
/// **The swivel's hold, and the swivel lock** (ours). 1.1's swivel is read from the key
/// table every frame, so it is on exactly while X, the keypad's 5 or the fifth button is down; Vitomir's
/// host starts and stops it on the key's down and up events, and <c>[qol] camera</c>'s drag swivel (the
/// middle button) is handed on as X. So **a release could be lost**: a conversation's cutscene takes the
/// camera while the button is held, the release comes while the camera is not the game camera and is
/// not read, and when the cutscene ends the swivel is still on with no button held — the mouse turning
/// the camera by itself (seen in play). A repair of our own code, always on:
/// <list type="bullet">
/// <item>the swivel **ends when the game camera loses the mouse** — a cutscene, a camera path, the free
/// camera, a menu, a shop, a document (first person keeps it, as its own look, and lets it go itself);</item>
/// <item>and **when it comes back the swivel is re-checked against the real keys**: still held, it is on
/// again; let go meanwhile, it stays off.</item>
/// </list>
/// What was worth keeping of the bug is the **swivel lock** (<see cref="KeyCommand.SwivelLock"/>, Ctrl and
/// the middle button by default, <c>[qol] camera</c>'s): a press turns the swivel on with nothing held,
/// the next turns it off. While it is locked, letting go of a swivel key does not end it. **It ends
/// itself** wherever the swivel must end — whenever the game camera loses the mouse, as above — and is
/// not turned back on when the camera comes back: a lock is a choice made in the game camera, and a
/// cutscene between is a new start — and a menu or a pane between too, unless the QoL <c>relock</c> is on
/// (<see cref="Relock"/>), when the lock comes back once they are closed. While it is locked the look is **centred**
/// (<see cref="CentreLook"/>): the pointer at the middle of the world's view, clicking there, and
/// a pane opening ends it (<see cref="End"/>).
/// </summary>
public sealed class SwivelHold
{
    bool inGame;

    /// <summary>Whether the swivel lock is on.</summary>
    public bool Locked { get; private set; }

    /// <summary>
    /// **The lock comes back** (the QoL <c>relock</c>, ours): a pane or a menu opening over the game camera only sets the
    /// lock aside (<see cref="SetAside"/>), and it comes back on when everything opened is closed again (<see cref="Resume"/>).
    /// Off, opening one ends it, as before. A cutscene, the first-person look or anything else that takes the game camera
    /// still ends it either way: a new start.
    /// </summary>
    public bool Relock { get; set; }

    /// <summary>Whether a lock set aside by a pane or a menu waits to come back (<see cref="Relock"/>).</summary>
    public bool Waiting { get; private set; }

    /// <summary>Whether a swivel key's release ends the swivel: not while it is locked.</summary>
    public bool ReleaseEnds => !Locked;

    /// <summary>
    /// A press of the swivel lock. Only in the game camera with the mouse its (<paramref name="gameCamera"/>);
    /// on, the swivel starts; off, **it stops**, whatever is held — the lock's own chord is the middle
    /// button, which is down at that moment and whose release will not be handed on.
    /// </summary>
    public SwivelStep Toggle(bool gameCamera)
    {
        if (!gameCamera) return SwivelStep.Nothing;
        Waiting = false;
        Locked = !Locked;
        return Locked ? SwivelStep.Start : SwivelStep.Stop;
    }

    /// <summary>
    /// Ends the lock from outside (<see cref="CentreLook"/>: a pane opening gives the pointer back): off,
    /// and **the swivel stops**, whatever is held. Nothing when it is not locked.
    /// </summary>
    public SwivelStep End()
    {
        Waiting = false;
        if (!Locked) return SwivelStep.Nothing;
        Locked = false;
        return SwivelStep.Stop;
    }

    /// <summary>
    /// A pane or a menu opened over the game camera (<see cref="CentreLook.Opened"/>): the lock is let go and **the
    /// swivel stops**, and with <see cref="Relock"/> on it waits to come back (<see cref="Resume"/>); off, this is
    /// <see cref="End"/>. Nothing when it is not locked.
    /// </summary>
    public SwivelStep SetAside()
    {
        if (!Locked) return SwivelStep.Nothing;
        bool back = Relock;
        var step = End();
        Waiting = back;
        return step;
    }

    /// <summary>
    /// Once a frame: a lock set aside comes back on — the swivel starts again — when <paramref name="clear"/> (nothing
    /// that wants the pointer is open, and the game camera has the mouse with the world drawn). Nothing otherwise, and
    /// nothing with <see cref="Relock"/> turned off since.
    /// </summary>
    public SwivelStep Resume(bool clear)
    {
        if (!Waiting || !clear) return SwivelStep.Nothing;
        Waiting = false;
        if (!Relock) return SwivelStep.Nothing;
        Locked = true;
        return SwivelStep.Start;
    }

    /// <summary>
    /// Once a frame. <paramref name="gameCamera"/>: the game camera has the mouse — it follows a player
    /// and nothing is over it (no menu, shop or document). <paramref name="look"/>: the first-person look
    /// has the camera, whose hold on the mouse is its own. <paramref name="swivelling"/>: the hold is on.
    /// <paramref name="held"/>: one of the swivel's chords is really down now (the swivel's own, or the
    /// drag swivel's under <c>[qol] camera</c>). <paramref name="aside"/>: what took the mouse from the game camera is a
    /// menu, a shop or a document over it, which sets the lock aside (<see cref="Relock"/>) rather than ending it.
    /// </summary>
    public SwivelStep Frame(bool gameCamera, bool look, bool swivelling, bool held, bool aside = false)
    {
        if (!gameCamera)
        {
            // A menu, a shop or a document over the game camera (aside) sets the lock aside, to come back with Relock;
            // anything else that takes the camera ends it, and a lock waiting to come back with it.
            if (Locked && aside) SetAside();
            else if (!aside) Waiting = false;
            Locked = false;
            bool left = inGame;
            inGame = false;
            // Only on the frame the game camera is left: after that the hold is somebody else's (the
            // free camera's drag look, the harness's right button).
            return left && swivelling && !look ? SwivelStep.Stop : SwivelStep.Nothing;
        }
        bool back = !inGame;
        inGame = true;
        if (swivelling) return SwivelStep.Nothing;
        if (Locked || back && held) return SwivelStep.Start;
        return SwivelStep.Nothing;
    }
}
