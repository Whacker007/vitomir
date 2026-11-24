// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Interface.Tests;

/// <summary>
/// A screen's file and its data: a list grows when its data grows, words
/// name the data's values, and what a file gets wrong is said once and drawn as nothing while the rest still works.
/// </summary>
public sealed class ScreenFileTests
{
    const string Roster = """
        {
          "veil": "#00000080",
          "root": { "type": "anchor", "at": "top-left", "margin": 20,
            "child": { "type": "column", "id": "col", "gap": 4, "children": [
              { "type": "text", "id": "heading", "text": "{title} ({count})", "style": "heading" },
              { "type": "list", "id": "rows", "items": "people", "template":
                { "type": "button", "text": "{name} of {title}", "action": "pick", "arg": "{name}", "height": 50 } }
            ] }
          }
        }
        """;

    [Fact]
    public void A_list_bound_to_data_grows_with_the_data()
    {
        var s = new ScreenLoader().Parse(Roster, "roster");
        s.Data.Set("title", "Prague").Set("count", 2);
        s.Data.Add("people").Set("name", "Christof");
        s.Data.Add("people").Set("name", "Wilhelm");
        s.Layout(new UiMetrics(2560, 1440));
        var rows = s.Find("rows")!;
        Assert.Equal(2, rows.Children.Count);
        Assert.Equal(2 * 50f, rows.Rect.Height);

        s.Data.Add("people").Set("name", "Serena");
        s.Data.Set("count", 3);
        s.Layout(new UiMetrics(2560, 1440));
        Assert.Equal(3, rows.Children.Count);
        Assert.Equal(3 * 50f, rows.Rect.Height);
        Assert.Equal(new UiRect(20f, 20f + rows.Rect.Y - 20f, rows.Rect.Width, 150f), rows.Rect);

        // Each row's words name its item, and the screen's data where the item has nothing.
        var list = new DrawList();
        s.Draw(list);
        var words = string.Concat(list.Texts());
        Assert.Contains("Serena", words);
        Assert.Contains("Prague", words);
        Assert.Contains("(3)", words);

        // And each row's action carries its item.
        Fixtures.Click(s, rows.Children[2]);
        var picked = Assert.Single(s.TakeActions());
        Assert.Equal("pick", picked.Name);
        Assert.Equal("Serena", picked.Argument);
    }

    [Fact]
    public void An_unknown_piece_or_field_is_said_once_and_drawn_as_nothing()
    {
        const string json = """
            {
              "root": { "type": "column", "children": [
                { "type": "gizmo", "id": "g1", "width": 400, "height": 400 },
                { "type": "gizmo", "id": "g2" },
                { "type": "text", "id": "t", "text": "Still Here", "colour": "#ff0000" },
                { "type": "list", "items": "rows", "template": { "type": "text", "text": "{n}", "sparkle": true } }
              ] }
            }
            """;
        var said = new List<string>();
        var loader = new ScreenLoader(said.Add);
        var s = loader.Parse(json, "broken");
        for (int i = 0; i < 3; i++) s.Data.Add("rows").Set("n", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        s.Layout(new UiMetrics(2560, 1440));
        s.Layout(new UiMetrics(1920, 1080));

        Assert.Equal(3, loader.Problems.Count);
        Assert.Single(loader.Problems, p => p.Contains("no piece \"gizmo\""));
        Assert.Single(loader.Problems, p => p.Contains("text has no field \"colour\""));
        Assert.Single(loader.Problems, p => p.Contains("text has no field \"sparkle\""));
        Assert.Equal(loader.Problems.Select(p => "interface: " + p), said);

        // The unknown pieces take no room and draw nothing; the rest is laid out and drawn as if they were not there.
        Assert.IsType<Nothing>(s.Find("g1") ?? s.Root.Children[0]);
        Assert.Equal(0f, s.Root.Children[0].Rect.Height);
        Assert.Equal(0f, s.Find("t")!.Rect.Y);
        var list = new DrawList();
        s.Draw(list);
        Assert.Equal(["S", "till ", "H", "ere", "0", "1", "2"], list.Texts());
    }

    [Fact]
    public void Words_fill_in_the_datas_names_and_a_name_not_set_gives_nothing()
    {
        var d = new ScreenData().Set("a", "one").Set("n", 2.5f).Set("b", true);
        var item = new ScreenData(d).Set("a", "two");
        Assert.Equal("one, 2.5, On, {x}", d.Fill("{a}, {n}, {b}, {{x}"));
        Assert.Equal("two 2.5 []", item.Fill("{a} {n} [{missing}]"));
    }

    [Fact]
    public void Both_of_the_programs_screens_are_read_without_a_word_said()
    {
        var loader = new ScreenLoader();
        loader.LoadEmbedded("pause");
        Fixtures.Page().Screen.Layout(new UiMetrics(2560, 1440));
        new ControlsPage(loader, Fixtures.Defaults(), Fixtures.Defaults(), Fixtures.Code, Fixtures.Name).Screen.Layout(new UiMetrics(2560, 1440));
        Assert.Empty(loader.Problems);
    }

    [Fact]
    public void The_interface_references_the_platform_only_and_nothing_of_SDL_or_the_old_engine()
    {
        string[] forbidden = ["Vitomir.Engine", "Vitomir.Render", "Vitomir.Scripting", "Vitomir.Platform.Render", "Vitomir.Modules", "SDL3"];
        var refs = typeof(Screen).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();
        Assert.Contains("Vitomir.Platform", refs);
        foreach (var name in refs)
            Assert.DoesNotContain(forbidden, f => name.StartsWith(f, StringComparison.OrdinalIgnoreCase));
    }
}
