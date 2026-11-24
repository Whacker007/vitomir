// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the mouse makes of a click's answer (the click's tail),
// built.
namespace Vitomir.Engine.Rpg;

/// <summary>
/// **A click in the world is answered by the order it gave.** Each of the four click handlers
/// (the ground's, a thing's, and their right-button pair) returns its order's own
/// answer, and the mouse shows the refusal cursor for exactly one of them: **1**, refused
/// (the red cross and its noise). **2** — busy, not now — and **4**,
/// ordered, are silence. <see cref="OrderResult"/>'s numbers are the engine's.
///
/// On the ground that makes the red cross the answer to a click on nothing, on a surface
/// with no walk flag, on a put-down with nowhere to walk to
/// (<see cref="PutRoute"/>), on a spot somebody stands on and on a walk with no route;
/// a character mid-swing is refused in silence.
/// </summary>
public static class ClickAnswer
{
    /// <summary>Whether this answer is the red cross (the answer compared with 1).</summary>
    public static bool RedCross(OrderResult answer) => answer == OrderResult.Refused;
}
