// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game run's party portraits dragged along the bar to put the party in a new order (ours, `partyorder`; the gesture
// itself is `Vitomir.Engine.Ui.PartyDrag`).
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Classic;

unsafe sealed partial class ClassicGame
{
    readonly PartyDrag partyDrag = new();

    /// <summary>Whether a portrait can be carried at all here: the switch on, a single-player game.</summary>
    bool PartyOrderOn => net is null && (location.Qol ?? Qol.Current).PartyOrder;

    /// <summary>
    /// The left button went down at window pixel (<paramref name="wx"/>, <paramref name="wy"/>), bar point
    /// (<paramref name="ux"/>, <paramref name="uy"/>). On a companion's portrait that may be carried, the press only waits —
    /// for a drag, or for the release that makes it a click — and this answers true; anything else is the bar's own click.
    /// </summary>
    bool PartyPress(float wx, float wy, int ux, int uy, InputModifier mods)
    {
        partyDrag.Clear();
        if (!PartyOrderOn || !hud.Enabled) return false;
        int row = hud.Bar.HeadAt(ux, uy);
        if (row < 0) return false;
        var party = location.PartyMembers().ToList();
        if (!PartyDrag.Lifts(row, hud.Bar.PartyThings, party, location.Steered?.Actor?.Held is not null, mods.HasFlag(InputModifier.Ctrl)))
            return false;
        partyDrag.Press(hud.Bar.PartyThings[row], row, wx, wy, ux, uy);
        pressMods = mods;
        return true;
    }

    InputModifier pressMods;

    /// <summary>The pointer moved, with the left button held on a portrait: the drag begins, and the bar shows where it would land.</summary>
    void PartyMove(float wx, float wy, int ww, int wh)
    {
        if (!partyDrag.Held) return;
        partyDrag.Move(wx, wy);
        if (!partyDrag.Moving) return;
        var (ux, uy) = StatusBar.FromWindow(wx, wy, ww, wh);
        PartyPreview(ux, uy);
    }

    /// <summary>The bar's preview for the carried portrait at bar point (<paramref name="ux"/>, <paramref name="uy"/>).</summary>
    void PartyPreview(int ux, int uy)
    {
        var bar = hud.Bar;
        var now = location.PartyMembers().ToList();
        if (partyDrag.Carried is not { } carried || now.IndexOf(carried) is var from && from < 1)
        {
            PartyDropped();                             // it left the party under the drag
            return;
        }
        var drop = PartyDrag.At(bar.HeadSlots, from, ux, uy);
        var order = PartyDrag.Order(now, from, drop);
        bar.Carrying = carried;
        bar.CarriedLands = drop.Kind != PartyDropKind.None;
        bar.Preview = bar.CarriedLands ? order : null;
        bar.Update();
    }

    /// <summary>The drag is over, one way or the other: the bar's own order again, and no ring.</summary>
    void PartyDropped()
    {
        partyDrag.Clear();
        hud.Bar.Preview = null;
        hud.Bar.Carrying = null;
        hud.Bar.CarriedLands = false;
        hud.Bar.Update();
    }

    /// <summary>
    /// The left button let go at bar point (<paramref name="rx"/>, <paramref name="ry"/>): a drag puts the party in the
    /// order its last place showed, or does nothing; a press that never moved is the portrait's click, where it was pressed.
    /// </summary>
    void PartyRelease(float rx, float ry)
    {
        if (!partyDrag.Held) return;
        if (!partyDrag.Moving)
        {
            var (px, py) = partyDrag.PressedAt;
            partyDrag.Clear();
            if (hud.Bar.Click(px, py, pressMods) is { Consumed: true } took) Console.WriteLine($"  ui: {took.Did}");
            return;
        }
        var now = location.PartyMembers().ToList();
        string said = "the portrait put back";
        if (partyDrag.Carried is { } carried && now.IndexOf(carried) is var from && from >= 1)
        {
            var drop = PartyDrag.At(hud.Bar.HeadSlots, from, (int)rx, (int)ry);
            if (drop.Kind != PartyDropKind.None && location.ReorderParty(PartyDrag.Order(now, from, drop)))
                said = $"party order {string.Join(", ", location.PartyMembers().Select(Vitomir.Engine.Diagnostics.Watch.Name))}";
        }
        PartyDropped();
        Console.WriteLine($"  ui: {said}");
    }
}
