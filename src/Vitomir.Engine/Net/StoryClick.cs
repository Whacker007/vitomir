// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Who hears a left press while the Storyteller's seat is taken: the game's own menu, the
// Storyteller's bar and panes, the seat's click on a thing, or the world. In a play-test
// (a hosted Storyteller game, `--st ","`): quitting through the escape menu took several clicks.
namespace Vitomir.Engine.Net;

/// <summary>Where a left press goes in the Storyteller's seat (<see cref="StoryClick.Route"/>).</summary>
public enum StoryClickTo
{
    /// <summary>The game's own menu that is up — the escape menu, the Options, a save screen.</summary>
    Menu,
    /// <summary>One of the Storyteller's bar or panes under the pointer (`statusBarST`, `st*Pane`).</summary>
    Pane,
    /// <summary>The seat's click on the thing under the pointer (the seat arm, <see cref="StoryPowers.Clicked(World.WorldThing, World.WorldThing?)"/>).</summary>
    Thing,
    /// <summary>The world's own: the ground's move order for whoever is steered.</summary>
    World,
}

/// <summary>
/// The order a left press is offered in while the seat is taken, which is 1.1's interface's own: the
/// newest **dialog** takes every click wherever the pointer is (`escapeMenuMP` is `dialog=1`),
/// then a pane under the pointer (the interface's quad test, the Storyteller's bar and five panes among them),
/// and only with none of those does the world hear it, where the seat's arm takes a
/// thing and the ground walks the control. Vitomir offered the press to the Storyteller first, ahead of the
/// game's menus (`NetPlay.Event`), and the seat asked about **the thing the pointer was over when the menu
/// came up** — the host stops picking while a menu is up, so that answer is kept — so with somebody under
/// the pointer as Escape was pressed, every click on the escape menu's Quit made them the target instead and
/// the menu never heard it (reproduced headless: MP_CurseDarkness's inn, the drunk under the pointer,
/// five clicks on Quit and no quit). Now the menu comes first.
/// </summary>
public static class StoryClick
{
    /// <summary>Where a left press goes: <paramref name="menuUp"/> the game's menus, <paramref name="paneUnder"/> a Storyteller pane at the pointer, <paramref name="thingUnder"/> a thing the pointer offers.</summary>
    public static StoryClickTo Route(bool menuUp, bool paneUnder, bool thingUnder) =>
        menuUp ? StoryClickTo.Menu
        : paneUnder ? StoryClickTo.Pane
        : thingUnder ? StoryClickTo.Thing
        : StoryClickTo.World;
}
