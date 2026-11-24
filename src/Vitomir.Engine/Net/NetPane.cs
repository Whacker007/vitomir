// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The one pane a host puts up on the players' machines: 1.1's message 0x48 — the load screen while the host loads a place, the pause menu while the Storyteller pauses.
using Vitomir.Formats;

namespace Vitomir.Engine.Net;

/// <summary>
/// **The host's pane on a player's machine** (1.1's message 0x48: the pane and on or off, to every
/// player, reliably). Two senders: the chronicle's load of a place not yet in memory
/// (on a host only: pane 0 on before the load, off after it — and off at once
/// when the load fails) and the Storyteller's Pause (pane 1, on with `UI_STSBPAUSE`, off with
/// `UI_STSBUNPAUSE`). A player's machine keeps **one** such pane: on, it loads
/// `ui/NetworkLoadScreen.nui` for pane 0 and `ui/pausemenu.nui` for any other and shows it
/// — **only when none is up**; off, it destroys whichever is up, whatever pane the message
/// names. So a load while the game is paused leaves the pause menu up and then takes it down at the load's
/// end, the game still paused; a pause during a load shows nothing. 1.1's, and Vitomir's. Both panes are a
/// plaque of two quads at 240,145 with no buttons and no `dialog` line: the player's machine is not stopped
/// by them (its world freezes for the pause only, <see cref="GameClient.Paused"/>, ours).
/// </summary>
public sealed class NetPane
{
    /// <summary>The load screen's pane number in the message.</summary>
    public const int Load = 0;
    /// <summary>The pause menu's; a player's machine takes any number but 0 as this one.</summary>
    public const int Pause = 1;
    /// <summary>The load screen's file (`ui\NetworkLoadScreen.nui`).</summary>
    public const string LoadFile = "NetworkLoadScreen";
    /// <summary>The pause menu's file (`ui\pausemenu.nui`, shipped as `pauseMenu.nui`).</summary>
    public const string PauseFile = "pausemenu";

    /// <summary>Which pane is up (<see cref="Load"/> or <see cref="Pause"/>), or null.</summary>
    public int? Up { get; private set; }

    /// <summary>The file of the pane up, or null.</summary>
    public string? File => Up switch { null => null, Load => LoadFile, _ => PauseFile };

    /// <summary>
    /// The message taken: off takes down whatever is up; on puts up <paramref name="pane"/> only when
    /// nothing is. Answers whether what is up changed.
    /// </summary>
    public bool Take(int pane, bool on)
    {
        if (!on)
        {
            if (Up is null) return false;
            Up = null;
            return true;
        }
        if (Up is not null) return false;                                        // one pane at a time
        Up = pane == Load ? Load : Pause;
        return true;
    }

    /// <summary>
    /// What to draw for the pane up — each quad of its file at the pane's position, in the 640 x 480 space
    /// (art, x, y, w, h) — through <paramref name="pane"/>, the interface's loader (`UiAssets.Pane`).
    /// </summary>
    public IEnumerable<(string Art, float X, float Y, float W, float H)> Quads(Func<string, NuiPane?> pane)
    {
        if (File is not { } file || pane(file) is not { } nui) yield break;
        float px = nui.Pos.Length > 1 ? nui.Pos[0] : 0, py = nui.Pos.Length > 1 ? nui.Pos[1] : 0;
        foreach (var q in nui.Quads)
            if (q.Pos.Length > 1 && q.Size.Length > 1) yield return (q.Material, px + q.Pos[0], py + q.Pos[1], q.Size[0], q.Size[1]);
    }
}
