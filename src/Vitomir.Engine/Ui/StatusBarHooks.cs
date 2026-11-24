// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The status bar's show and hide hooks.
namespace Vitomir.Engine.Ui;

/// <summary>
/// **The status bar's show and hide hooks** — what 1.1 does when the bar goes up or down, by F11 or by a
/// cutscene (it is taken down on the way in and put back
/// on the way out). Each bar's create gives its pane a show
/// hook and a hide hook: the single-player bar's, the multiplayer bar's and
/// the Storyteller's (statusBarST) — and the three hides are one body.
/// <para>
/// **The hide** (all three alike) runs <b>the panes' close</b> — every pane down: the quest pane
/// (vuiQuest), the inventory (vuiInv), the discipline pane
/// (vuiDiscipline) and the character sheet (vuiChar), and in the
/// Storyteller's seat the seat's five panes too (actor, object, controls, location, scene) —
/// then resets the world's view; and then <b>the hover's clear with 1</b>: the bar's
/// hover cleared and both description boxes taken down at once (the 1 is "no
/// fade"; 0 fades them over 0.3 s). The panes' close is also Backspace's.
/// </para>
/// <para>
/// **The show** resets the world's view and lays the rows out again.
/// </para>
/// <para>
/// **Only on a real change**: the pane's hide runs the hide hook only for a pane that was shown, and
/// the pane's show's whole body — the show hook with it — is under "not shown". So a hidden bar hidden again, or a shown
/// one shown again, runs nothing; <see cref="Step"/> is that edge.
/// </para>
/// </summary>
public sealed class StatusBarHooks
{
    /// <summary>Which hook a change of the bar runs.</summary>
    public enum Hook
    {
        /// <summary>No change: a hide on a hidden pane, a show on a shown one.</summary>
        None,
        /// <summary>The bar came up: its show hook.</summary>
        Show,
        /// <summary>The bar went down: its hide hook.</summary>
        Hide,
    }

    /// <summary>The panes the close takes down, in its order.</summary>
    public enum Pane
    {
        /// <summary>The quest pane.</summary>
        Quests,
        /// <summary>The inventory pane.</summary>
        Inventory,
        /// <summary>The discipline pane.</summary>
        Disciplines,
        /// <summary>The character sheet.</summary>
        Character,
        /// <summary>In the seat: the Storyteller's actor pane.</summary>
        StoryActor,
        /// <summary>In the seat: the Storyteller's object pane.</summary>
        StoryObject,
        /// <summary>In the seat: the Storyteller's controls pane.</summary>
        StoryControls,
        /// <summary>In the seat: the Storyteller's location pane.</summary>
        StoryLocation,
        /// <summary>In the seat: the Storyteller's scene pane.</summary>
        StoryScene,
    }

    static readonly Pane[] PlayerPanes = [Pane.Quests, Pane.Inventory, Pane.Disciplines, Pane.Character];
    static readonly Pane[] SeatPanes = [.. PlayerPanes, Pane.StoryActor, Pane.StoryObject, Pane.StoryControls,
                                        Pane.StoryLocation, Pane.StoryScene];

    /// <summary>
    /// What the close takes down: the player's four panes, and in the Storyteller's seat
    /// the seat's five after them.
    /// </summary>
    public static IReadOnlyList<Pane> Down(bool seat) => seat ? SeatPanes : PlayerPanes;

    /// <summary>
    /// Whether the hide takes both description boxes down **at once** — the hover clear's argument 1 — rather than
    /// fading them (0 is a hover that ended, over 0.3 s).
    /// </summary>
    public const bool BoxesAtOnce = true;

    bool shown;

    /// <param name="shown">Whether the bar is up to begin with (no hook runs for it).</param>
    public StatusBarHooks(bool shown) => this.shown = shown;

    /// <summary>Whether the bar is up, as of the last <see cref="Step"/>.</summary>
    public bool Shown => shown;

    /// <summary>
    /// The bar's state this frame — F11's wish and no cutscene — and which hook that runs: <see cref="Hook.Hide"/>
    /// when it was up and is not, <see cref="Hook.Show"/> when it was down and is up,
    /// otherwise nothing.
    /// </summary>
    public Hook Step(bool up)
    {
        if (up == shown) return Hook.None;
        shown = up;
        return up ? Hook.Show : Hook.Hide;
    }
}
