// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A front-end turn that came long after the last one, said in the log (ours).
namespace Vitomir.Engine.Ui;

/// <summary>
/// **A stalled frame, named** (ours): the front end's loop says a turn that came <see cref="AtMs"/> or more after the one
/// before, with the panes that were up. Nothing else moves while the loop is stalled — the network included, whose
/// keep-alive (<c>NetPeer.KeepAliveWhile</c>) is what holds a game's room over one — so the line is how a slow machine's
/// pause shows up in its log at all. A slow machine stalled long enough in the room of a game under way for the host to give
/// the player up, and its log said nothing about it.
/// </summary>
public static class FrameStall
{
    /// <summary>The gap between two turns that is said: two seconds.</summary>
    public const int AtMs = 2000;

    /// <summary>The line for a gap of <paramref name="elapsedNs"/>, or null when it is shorter than <see cref="AtMs"/>.</summary>
    public static string? Line(ulong elapsedNs, Func<string> what)
    {
        ulong ms = elapsedNs / 1_000_000;
        if (ms < AtMs) return null;
        string up = what();
        return $"front end: a frame took {ms / 1000.0:0.0} s{(up.Length == 0 ? "" : $" ({up})")}";
    }

    /// <summary>The panes up, by their files, top last: "nothing up" when none is.</summary>
    public static string Panes(FrontEnd menus)
    {
        var shown = menus.Visible().Where(p => p.Shown).Select(p => p.File).ToList();
        return shown.Count == 0 ? "nothing up" : "up: " + string.Join(", ", shown);
    }
}
