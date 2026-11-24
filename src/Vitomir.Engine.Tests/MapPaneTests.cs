// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The map as 1.1 has it: the pane is the one the current
/// location names, a marker shows only for a place marked SHOWINMAP, its art says you-are-here before
/// haven before Abyss before plain, the pointer lights it and names it, and nothing but the close
/// button, Escape and M does anything. Run on the shipped Prague chronicle and its own map.
/// </summary>
public class MapPaneTests
{
    static (FrontEnd Menus, MapScreen Map, List<string> Posted, Strings Text) Open(GameFiles files)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        text.Add("prague");
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame };
        var map = new MapScreen(menus, text.Find);
        var posted = new List<string>();
        map.Posted += posted.Add;
        menus.Map = map;
        return (menus, map, posted, text);
    }

    static bool Exists(GameFiles files, string file) => files.TryRead("ui/" + file, out _);

    [Fact]
    public void Every_marker_is_hidden_until_the_story_marks_its_place_shown_on_the_map()
    {
        using var files = Install.Open();
        var prague = ChronicleSet.Load(files)["Prague"]!;
        var (menus, map, _, _) = Open(files);
        var haven = prague.At("Haven")!;
        Assert.Equal("mapPragueOT.nui", haven.MapFile, ignoreCase: true);
        Assert.True(map.Open(haven, prague.Data.Locations, f => Exists(files, f)));
        var pane = map.Pane!;
        Assert.All(pane.Nui.Elements.Where(e => e.Type == "imagearray"), e => Assert.Contains(e.Name, pane.Hidden));
        Assert.True(menus.Paused);
        Assert.Equal(FrontEnd.StateOptions, menus.State);
        menus.Drawn();
        menus.Key(MenuKey.Map);                       // M closes it as Escape does
        Assert.Null(map.Pane);
        Assert.False(menus.Paused);
        Assert.Equal(FrontEnd.StateGame, menus.State);
    }

    [Fact]
    public void You_are_here_beats_haven_beats_abyss_beats_plain()
    {
        using var files = Install.Open();
        var locations = ChronicleSet.Load(files)["Prague"]!.Data.Locations;
        foreach (var l in locations) l.Flags |= MapScreen.ShowOnMap;
        var haven = locations.First(l => l.Name == "Haven");
        Assert.Equal(MapScreen.YouAreHere, MapScreen.Kind("mapUniversityHaven", locations, haven.MapMarker));
        Assert.Equal(MapScreen.Haven, MapScreen.Kind("mapUniversityHaven", locations, "mapConvent"));
        Assert.Equal(MapScreen.Plain, MapScreen.Kind("mapSmithy", locations, haven.MapMarker));
        locations.First(l => l.Name == "Smithy").Flags |= MapScreen.AbyssFlag;
        locations.First(l => l.Name == "SmithyDay").Flags |= MapScreen.AbyssFlag;
        Assert.Equal(MapScreen.Abyss, MapScreen.Kind("mapSmithy", locations, haven.MapMarker));
        Assert.Equal(MapScreen.Hidden, MapScreen.Kind("mapNoSuchPlace", locations, haven.MapMarker));
    }

    [Fact]
    public void The_pointer_lights_a_marker_and_names_its_place_and_a_click_on_it_does_nothing()
    {
        using var files = Install.Open();
        var prague = ChronicleSet.Load(files)["Prague"]!;
        foreach (var l in prague.Data.Locations) l.Flags |= MapScreen.ShowOnMap;
        var (menus, map, _, text) = Open(files);
        map.Open(prague.At("Haven"), prague.Data.Locations, f => Exists(files, f));
        var pane = map.Pane!;
        Assert.Equal(["locationIndicatorURhere", "locationIndicatorURhereOver"], pane.Images["mapUniversityHaven"].Images);
        var marker = pane.Element("mapOldTownCenter")!;
        menus.PointerAt(marker.Pos[0] + 8, marker.Pos[1] + 8);
        menus.Frame(0);
        Assert.Equal(1, pane.Images["mapOldTownCenter"].Current);
        Assert.Equal(text.Find("mapOldTownCenter"), pane.Texts[MapScreen.TextBox]);
        menus.LeftHeld = true; menus.Press(); menus.LeftHeld = false; menus.Release();
        Assert.NotNull(map.Pane);                     // no handler on a marker
        var close = pane.Element(MapScreen.CloseButton)!;
        menus.PointerAt(close.Pos[0] + 4, close.Pos[1] + 4);
        menus.Frame(0);
        Assert.Equal(0, pane.Images["mapOldTownCenter"].Current);
        Assert.Equal(text.Find(MapScreen.ReturnToGame), pane.Texts[MapScreen.TextBox]);
        menus.LeftHeld = true; menus.Press(); menus.LeftHeld = false; menus.Release();
        Assert.Null(map.Pane);
    }

    [Fact]
    public void A_place_with_no_map_says_so_in_the_message_log()
    {
        using var files = Install.Open();
        var (_, map, posted, text) = Open(files);
        Assert.False(map.Open(new Vitomir.Formats.NscLocation { Name = "Nowhere" }, [], _ => false));
        Assert.Equal([text.Find(MapScreen.NoMap)!], posted);
    }
}
