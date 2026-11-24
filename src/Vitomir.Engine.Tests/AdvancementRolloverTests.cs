// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The rollovers over Create's advancement screen**
/// (<see cref="AdvancementRollover"/>). 1.1's UI update runs a control's rollover in the front end while the
/// advancement screen is open, so the new character's screen says what the
/// game's does: a raise button its price, a discipline's learn button its description and price,
/// a tab its group's name, and the bar's OK and Revert their hints. Driven on a character
/// made as Create makes one (<see cref="MpMenus.NewBody"/>), over the shipped panes, with the pointer walked
/// over the interface's 640 × 480.
/// </summary>
public class AdvancementRolloverTests
{
    static void OnNew(Action<CharacterPane, DisciplinePane, Advancement, DisciplineDefinitions, Strings, UiAssets> test)
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        string folder = Path.Combine(Path.GetTempPath(), "vitomir-crypt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var mp = new MpMenus(menus, files, text.Find, folder) { Port = 0, Upnp = false };
            var powers = new DisciplineDefinitions(files);
            var body = mp.NewBody(new NetCharacter { Name = "Anna", Template = "MP_Serena", Type = ActorType.Vampire, Clan = "Brujah" }, powers)!;
            var advancement = new Advancement(body, [body], powers);
            var sheet = new CharacterPane(assets.Pane(CharacterPane.PaneName)) { Subject = body.Actor, Advancing = true, Strings = k => text.Find(k) ?? k };
            var disciplines = new DisciplinePane(assets.Pane(DisciplinePane.PaneName)) { Powers = powers, Text = text.Find, Advancing = true };
            disciplines.Rebuild(body, true);
            test(sheet, disciplines, advancement, powers, text, assets);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The first point of the screen where <paramref name="hit"/> holds.</summary>
    static (float X, float Y) Find(Func<float, float, bool> hit)
    {
        for (int y = 0; y < 480; y += 2)
            for (int x = 0; x < 640; x += 2)
                if (hit(x, y)) return (x, y);
        throw new Xunit.Sdk.XunitException("nothing there");
    }

    [Fact]
    public void A_raise_button_says_its_price_in_the_small_box()
    {
        OnNew((sheet, disciplines, advancement, powers, text, _) =>
        {
            var (x, y) = Find((x, y) => sheet.RaiseAt(x, y) >= 0 && !disciplines.Covers(x, y));
            int stat = sheet.RaiseAt(x, y);
            var lines = AdvancementRollover.Under(sheet, disciplines, advancement, powers, x, y, text.Find);
            Assert.NotNull(lines);
            var price = Assert.Single(lines!.Value.Small);
            Assert.Equal(sheet.RaiseCost(stat, text.Find), price);
            Assert.Contains(Advancement.StatCost(advancement.Subject.Actor!, stat).ToString(), price.Text);
            Assert.Empty(lines.Value.Large);
            Assert.True(lines.Value.LargeNow);
        });
    }

    [Fact]
    public void A_learn_button_describes_the_discipline_and_says_its_price()
    {
        OnNew((sheet, disciplines, advancement, powers, text, _) =>
        {
            var (x, y) = Find((x, y) => disciplines.Covers(x, y) && disciplines.At(x, y) is (DisciplinePane.Part.Learn, var i)
                                        && disciplines.Rows()[i].Discipline is { } d
                                        && Advancement.DisciplineCost(advancement.Subject, powers, d.Definition) >= 0);
            var d = disciplines.Rows()[disciplines.At(x, y).Index].Discipline!;
            int cost = Advancement.DisciplineCost(advancement.Subject, powers, d.Definition);
            var lines = AdvancementRollover.Under(sheet, disciplines, advancement, powers, x, y, text.Find)!.Value;
            var (small, large) = DisciplinePane.Describe(d, text.Find);
            Assert.Equal(small, lines.Small.Take(small.Count));
            Assert.Equal(large, lines.Large);
            var last = lines.Small[^1];
            Assert.Equal(Strings.Format(text.Find, "UI_ADVANCEMENT_DISCCOST", $"%s{text.Find(d.Definition.Name) ?? d.Definition.Name}%d{cost}"), last.Text);
            Assert.Equal(advancement.Subject.Actor!.SpendableExperience >= cost ? Rollover.White : DisciplinePane.Red, last.Colour);
            Assert.True(lines.LargeNow);                                         // both boxes at once (mode 2)
        });
    }

    [Fact]
    public void The_bar_s_ok_and_revert_carry_their_hints()
    {
        OnNew((sheet, disciplines, advancement, powers, text, assets) =>
        {
            var bar = assets.Pane("statusBarADV");
            foreach (var (name, key) in new[] { ("ADVokButton", "UI_ADVANCEMENT_DONE"), ("ADVresetButton", "UI_ADVANCEMENT_REVERTCHAR") })
            {
                var e = bar.Elements.Single(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                float x = e.Pos[0] + e.Size[0] / 2f, y = e.Pos[1] + e.Size[1] / 2f;
                var hinted = AdvancementRollover.Hint(bar, x, y);
                Assert.Same(e, hinted);
                Assert.Equal(new RolloverLine(text.Find(key) ?? key, Rollover.White), Rollover.Hint(hinted, text));
            }
            Assert.Null(AdvancementRollover.Hint(bar, 2, 2));
        });
    }
}
