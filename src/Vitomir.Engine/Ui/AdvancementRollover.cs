// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the description boxes say over the advancement screen: the raise buttons', the discipline pane's and the
// bar's two buttons' rollovers (`statusBarADV`).
using Vitomir.Engine.Rpg;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The rollovers over the advancement screen**, as one answer for whoever draws the boxes — the game's own
/// screen and Create's in the front end. 1.1 runs a control's
/// rollover from its UI update when the game is in state 3 or 4 **or the advancement screen
/// is open** (`vuiAdvancement bOpen`), and the status bar's boxes come up for it
/// then whatever the bar is doing (the bar passes on the same flag) — so Create's advancement, in the front end
/// (state 2, whose frame flags 0x19 include the UI update), shows exactly the game's:
/// <list type="bullet">
/// <item>a **raise button**: its price in the small box, white or red (<see cref="CharacterPane.RaiseCost"/>);</item>
/// <item>a **discipline's row** (its icon, name or learn button): the discipline's description in both boxes at
/// once (mode 2), the learn button adding its price — `UI_ADVANCEMENT_DISCCOST`, white or red;</item>
/// <item>a **tab**: its group's name;</item>
/// <item>the bar's **OK** and **Revert**: their hints, `UI_ADVANCEMENT_DONE` and `UI_ADVANCEMENT_REVERTCHAR`
/// (`statusBarADV.nui`'s `hintstring`, which the dispatcher copies to the small box) —
/// <see cref="Hint"/>.</item>
/// </list>
/// Anything else on the discipline pane takes the boxes down. The lines are written when the hover changes, as
/// the hover writes them (<see cref="IconRollover.OnChange"/>), or every frame under the Fix `liverollover`
/// (<see cref="LiveRollover.Keep"/>).
/// </summary>
public static class AdvancementRollover
{
    /// <summary>
    /// The lines for the point (<paramref name="x"/>, <paramref name="y"/>) of the interface's space over the sheet
    /// and the discipline pane while advancing, or null for nothing there.
    /// </summary>
    public static (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? Under(
        CharacterPane sheet, DisciplinePane disciplines, Advancement advancement, DisciplineDefinitions powers,
        float x, float y, Func<string, string?> find)
    {
        string Say(string key) => find(key) ?? key;
        if (disciplines.Covers(x, y))
        {
            var (part, index) = disciplines.At(x, y);
            if (part == DisciplinePane.Part.Tab)
                return (new[] { new RolloverLine(Say(disciplines.Tabs().ElementAt(index).Group.Name), Rollover.White) }, [], true, ("tab", index));
            if (part is DisciplinePane.Part.Slot or DisciplinePane.Part.Learn && disciplines.Rows()[index].Discipline is { } d)
            {
                var (small, large) = DisciplinePane.Describe(d, find);
                if (part == DisciplinePane.Part.Learn && Advancement.DisciplineCost(advancement.Subject, powers, d.Definition) is var cost and >= 0)
                    small.Add(new(Strings.Format(find, "UI_ADVANCEMENT_DISCCOST", $"%s{Say(d.Definition.Name)}%d{cost}"),
                                  advancement.Subject.Actor!.SpendableExperience >= cost ? Rollover.White : DisciplinePane.Red));
                return (small, large, true, d);
            }
            return null;
        }
        if (sheet.RaiseAt(x, y) is var stat and >= 0 && sheet.RaiseCost(stat, find) is { } price)
            return (new[] { price }, [], true, ("raise", stat));
        return null;
    }

    /// <summary>The bar's buttons carry a `hintstring` each; the one under the pointer, for the small box (<see cref="Rollover.Hint"/>).</summary>
    public static NuiElement? Hint(NuiPane bar, float x, float y)
    {
        foreach (var e in bar.Elements)                                     // placed as the bar's buttons are (AdvancementBarPass.ButtonAt)
            if (e.Get("hintstring") is { Length: > 0 } && e.Pos.Length > 1 && e.Size.Length > 1
                && x >= e.Pos[0] && x <= e.Pos[0] + e.Size[0] && y >= e.Pos[1] && y <= e.Pos[1] + e.Size[1])
                return e;
        return null;
    }
}
