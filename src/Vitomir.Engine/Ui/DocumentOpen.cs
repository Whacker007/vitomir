// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a script's `ExecuteText` does on the reader's machine before the read pane (the text
// opener, before the pane's own open): the floating captions down, and the look camera let go. Compared with
// 1.1 and built.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **Reading a document takes the floating captions down and leaves the look camera**.
///
/// <para>A script's <c>ExecuteText</c> — and the console's <c>read</c>
/// — works on the machine whose character reads: in single player always; on a host for its own avatar (a remote reader's goes out as
/// message 0x26 and the host opens nothing); on a player's machine for the message, with
/// its "from the net" argument. There, before it asks for the pane, it takes
/// every floating caption down (the chapter title, the end text, whatever a script put
/// up) and, <b>when the camera is in mode 2</b>, the first-person look Z holds, puts the game camera
/// back. Both come before the pane is asked for, so they happen even when the pane
/// refuses — one already up, or no such document (the pane's two early returns). Then, in single
/// player, the music and the ambient are halved (<see cref="ReadPane.Open"/>).</para>
///
/// <para>With the look let go and Z still held, the look does not come straight back: the keys that
/// enter it are read only in the game camera with no dialog up (the frame's key handling),
/// and the pane is a dialog. The front end owns the swivel that goes with
/// the look, and lets it go (the shop's open does the same).</para>
/// </summary>
public static class DocumentOpen
{
    /// <summary>
    /// The opener's work before the pane: the captions down, and the look camera left for
    /// the game camera (only from mode 2). Answers whether the look was left.
    /// </summary>
    public static bool Before(Captions captions, WorldCamera? camera, WorldThing? steered)
    {
        captions.Clear();
        return camera is { Mode: CameraMode.Look } && camera.BackToGame(steered);   // mode 2: the look camera back to the game
    }
}
