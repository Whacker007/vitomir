// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The frame's input as the host handed it in (the pushed frame).
using Vitomir.Platform.Modules;
using Vitomir.Platform.Render.Classic;

namespace Vitomir.Classic;

/// <summary>
/// **The input the host handed in, as the controller reads it**: the tape's live side (<see cref="InputTape.Live"/>) and the
/// opening films' own reading. Earlier, each poll pulled the host's next event (SDL's queue, through the picture); the
/// host gathers the events since the last frame before it runs the frame (<see cref="HostFrame.Input"/>) and they wait here, oldest
/// first, until the frame reads them. A frame that reads none (a run's start, the title drawn once) leaves them for the next, as
/// SDL's queue kept them. What is not an event — the buttons and keys held, the modifiers, the picture's size, the clipboard, a
/// replay's window put to its recorded size — is still the host's, asked as before.
/// </summary>
sealed class FrameInput(IInputHost host) : IInputHost
{
    readonly Queue<LoopInput> waiting = new();

    /// <summary>The frame's events, after any the last frames left unread.</summary>
    public void Hand(IReadOnlyList<LoopInput> input)
    {
        foreach (var e in input) waiting.Enqueue(e);
    }

    /// <summary>The next event the game is to hear (the tape's live side; and the opening films', which are never on a tape).</summary>
    public bool Poll(out LoopInput e) => waiting.TryDequeue(out e);

    /// <summary>A replay's turn: the host's events thrown away; true when the player closed the window among them.</summary>
    public bool Closed()
    {
        bool closed = false;
        while (waiting.TryDequeue(out var e))
            if (e.Kind == LoopInputKind.Quit) closed = true;
        return closed;
    }

    public uint Buttons(out float x, out float y) => host.Buttons(out x, out y);

    public LoopMods Mods
    {
        get => host.Mods;
        set => host.Mods = value;
    }

    public ReadOnlySpan<bool> Keyboard() => host.Keyboard();
    public void ResetKeyboard() => host.ResetKeyboard();
    public bool TextInputActive => host.TextInputActive;
    public void ToggleFullScreen() => host.ToggleFullScreen();
    public bool HasWindow => host.HasWindow;
    public (int Width, int Height) PictureSize() => host.PictureSize();
    public void Resize(int width, int height) => host.Resize(width, height);
    public string? Clipboard() => host.Clipboard();
    public void SetClipboard(string text) => host.SetClipboard(text);
}
