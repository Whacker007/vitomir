// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The map, read: opening it, a marker's hover,
// the pane's per-frame hook, its key hook and the close button.
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// The map pane: **M**, or the status bar's map button, in the game.
///
/// **Which map** is not worked out from where you are standing: every location line of the
/// chronicle names its own pane and its own marker (`location Convent … mapPragueOT.nui mapConvent
/// 0x0020`), and the map simply opens `ui\` + the current location's pane. A location that names
/// none — or names one the install lacks — gets no map, and the message log says
/// "No map is available for this area." (`UI_MAP_NOMAPAVAILABLE`).
///
/// **What it marks** is the chronicle's locations and nothing else: no party, no exits. Every
/// marker (`imagearray`) on the pane starts hidden, and one is shown when a location whose marker
/// has that name carries **flag 0x8** (`SHOWONMAP`) — scripts set and clear it as the story
/// uncovers places, which is what the three "dummy" locations in `Prague.nsc` are for. Its art
/// says which kind it is, in this order: the current location's own marker is **"you are here"**;
/// a location with **0x10** (`HAVEN`) is a haven; one with **0x400000** is the Abyss's; the rest
/// are plain. Each kind is a pair — the art and its `Over` version for the pointer.
///
/// **What the pointer does** is all a marker does: hovered, it lights (its second image) and the
/// place's name goes in the text line at the bottom (the marker's own name through the string
/// table: `mapConvent` → "The Convent"); the close button says "Return to Game". **Clicking a
/// marker does nothing** — no handler is registered on one. The close button, Escape and M close it.
///
/// Opening pauses a single-player game and sets the game state to 4 (the game not drawn), on a
/// layer of its own; closing gives all three back.
/// </summary>
public sealed class MapScreen(FrontEnd menus, Func<string, string?> strings)
{
    /// <summary>The four kinds of marker and their two images each.</summary>
    public static readonly string[][] Indicators =
    [
        ["locationIndicator", "locationIndicatorOver"],
        ["locationIndicatorURhere", "locationIndicatorURhereOver"],
        ["locationIndicatorHaven", "locationIndicatorHavenOver"],
        ["locationIndicatorAbyss", "locationIndicatorAbyssOver"],
    ];

    /// <summary>What a marker is: 0 not shown, then 1 plain, 2 you are here, 3 haven, 4 the Abyss (the open's switch).</summary>
    public const int Hidden = 0, Plain = 1, YouAreHere = 2, Haven = 3, Abyss = 4;

    /// <summary>The location flags the map reads (`NodSDK\Docs\nsc.htm`; 0x400000 is not in it).</summary>
    public const uint ShowOnMap = 0x8, HavenFlag = 0x10, AbyssFlag = 0x400000;

    /// <summary>The text line, the close button (`MapCloseButton`), and the strings they say.</summary>
    public const string TextBox = "mapLocations", CloseButton = "MapCloseButton",
                        NoMap = "UI_MAP_NOMAPAVAILABLE", ReturnToGame = "UI_MAP_RETURNTOGAME";

    /// <summary>The pane up, or null.</summary>
    public MenuPane? Pane { get; private set; }
    string? hovered;

    /// <summary>A line for the message log: "No map is available for this area."</summary>
    public event Action<string>? Posted;

    /// <summary>
    /// What a marker shows: the first location whose marker has this name and is shown on
    /// the map decides — the current location's marker first of all, then haven, then Abyss.
    /// </summary>
    public static int Kind(string marker, IEnumerable<NscLocation> locations, string? currentMarker)
    {
        foreach (var loc in locations)
        {
            if (!loc.MapMarker.Equals(marker, StringComparison.OrdinalIgnoreCase) || (loc.Flags & ShowOnMap) == 0) continue;
            if (currentMarker is not null && currentMarker.Equals(loc.MapMarker, StringComparison.OrdinalIgnoreCase)) return YouAreHere;
            if ((loc.Flags & HavenFlag) != 0) return Haven;
            return (loc.Flags & AbyssFlag) != 0 ? Abyss : Plain;
        }
        return Hidden;
    }

    /// <summary>
    /// Opens the map for <paramref name="here"/> among <paramref name="locations"/>. A
    /// pane that is already up is left as it is. <paramref name="load"/> answers whether the install
    /// has the pane at all. Answers whether a map went up.
    /// </summary>
    public bool Open(NscLocation? here, IEnumerable<NscLocation> locations, Func<string, bool> load)
    {
        if (Pane is not null) return true;
        menus.CloseGamePanes?.Invoke();
        string file = here?.MapFile ?? "";
        if (file.Length == 0 || !load(file))
        {
            Posted?.Invoke(strings(NoMap) ?? NoMap);
            return false;
        }
        var pane = menus.Pane(file);
        pane.Texts[TextBox] = "";
        var all = locations.ToList();
        foreach (var e in pane.Nui.Elements.Where(e => e.Type == "imagearray"))
        {
            int kind = Kind(e.Name, all, here?.MapMarker);
            if (kind == Hidden) { pane.Hidden.Add(e.Name); continue; }
            pane.Hidden.Remove(e.Name);
            pane.Images[e.Name] = new MenuImages(Indicators[kind - 1], 0);
        }
        Pane = pane;
        hovered = null;
        if (menus.SinglePlayer) menus.Paused = true;
        if (menus.State == FrontEnd.StateGame) menus.State = FrontEnd.StateOptions;
        menus.PushLayer();
        menus.Show(pane);
        return true;
    }

    /// <summary>Close: unpaused, state 3, the layer popped.</summary>
    public void Close()
    {
        if (Pane is null) return;
        if (menus.SinglePlayer) menus.Paused = false;
        if (menus.State == FrontEnd.StateOptions) menus.State = FrontEnd.StateGame;
        menus.PopLayer();
        Pane = null;
        hovered = null;
        Closed?.Invoke();
    }

    /// <summary>The map gone: the host puts the cursor back.</summary>
    public event Action? Closed;

    public bool Owns(MenuPane pane) => ReferenceEquals(pane, Pane);

    /// <summary>The close button's release (binding 1). Nothing else on the pane has a click.</summary>
    public void Clicked(NuiElement button)
    {
        if (button.Name.Equals(CloseButton, StringComparison.OrdinalIgnoreCase)) Close();
    }

    /// <summary>The key hook: Escape or M closes the map.</summary>
    public void Key(MenuKey key)
    {
        if (key is MenuKey.Escape or MenuKey.Map) Close();
    }

    /// <summary>
    /// The pointer, each frame: over a marker, the marker lights and the text line names the place;
    /// over the close button, "Return to Game"; over nothing, the line is cleared.
    /// A marker the pointer has left goes back to its first image.
    /// </summary>
    public void Hover(NuiElement? on)
    {
        if (Pane is not { } pane) return;
        string? now = null;
        if (on is { Type: "imagearray" })
        {
            pane.Texts[TextBox] = strings(on.Name) ?? on.Name;
            now = on.Name;
        }
        else if (on is not null && on.Name.Equals(CloseButton, StringComparison.OrdinalIgnoreCase))
            pane.Texts[TextBox] = strings(ReturnToGame) ?? ReturnToGame;
        else if (on is null)
            pane.Texts[TextBox] = "";
        if (hovered is not null && hovered != now && pane.Images.TryGetValue(hovered, out var was))
            pane.Images[hovered] = was with { Current = 0 };
        if (now is not null && pane.Images.TryGetValue(now, out var lit))
            pane.Images[now] = lit with { Current = 1 };
        hovered = now;
    }
}
