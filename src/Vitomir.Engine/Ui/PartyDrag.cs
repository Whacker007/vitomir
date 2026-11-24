// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A party portrait dragged along the bar to put the party in a new order (ours, `partyorder`; 1.1 has no way to change
// the order at all).
//
// The gesture, in the order it happens:
//
//   1. **The press** on a companion's portrait — not the leader's, whose row is always the first — with nothing on the
//      cursor and Ctrl up (Ctrl's click is the group toggle). With the switch on and three or more in the party, the
//      portrait's click waits for the release instead of acting on the press.
//   2. **The drag** begins once the pointer has moved three pixels either way, the discipline drag's threshold. While it
//      lasts, the bar draws its rows, and the heads in them, **in the order the party would take if the button were let
//      go here**, and rings the carried member's row: gold where it would land, grey back in its own place where
//      letting go would change nothing.
//   3. **Where the pointer is** decides it: over the middle of another companion's portrait, the two **swap**; anywhere
//      else between two portraits, the carried one goes **in between** them, and everybody between moves along one;
//      past the last portrait's middle, it goes to the end. Over the leader, left of the leader's middle, or anywhere
//      off the portraits — the rest of the bar, the world — nothing.
//   4. **The release** puts the party in that order (`Location.ReorderParty`), or nothing. A press that never became a
//      drag is the portrait's click after all (take that member over), on the release.
//
// Nobody's panes change: the character sheet, the pack and the disciplines are each a character's own (`Subject`), and
// the drag steers nobody, so whatever is open stays open on whoever it was open on and the lit row goes with them.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>What letting go would do: nothing, swap with a row, or put the carried member in before a row.</summary>
public enum PartyDropKind { None, Swap, Insert }

/// <summary>
/// One answer of <see cref="PartyDrag.At"/>. <see cref="Row"/> is a row of the party's present order: the one swapped with,
/// or the one the carried member goes in front of (the party's count for the end).
/// </summary>
public readonly record struct PartyDrop(PartyDropKind Kind, int Row)
{
    public static readonly PartyDrop None = new(PartyDropKind.None, -1);
}

/// <summary>A portrait's place on the bar, in the bar's space: its hotspot's rectangle.</summary>
public readonly record struct PartySlot(int X, int Y, int Width, int Height)
{
    public float Middle => X + Width / 2f;
}

/// <summary>The left button's drag of a party portrait (ours, <c>partyorder</c>): the press, the threshold and the drop.</summary>
public sealed class PartyDrag
{
    /// <summary>How far the pointer moves, either way, in window pixels, before a held press is a drag (<see cref="DisciplineDrag.Threshold"/>).</summary>
    public const float Threshold = DisciplineDrag.Threshold;

    /// <summary>
    /// How much of a portrait, about its middle, is a <b>swap</b> with it: the middle half of the narrower of its hotspot
    /// and the step between two rows. The rest of the way between two middles puts the carried one in between.
    /// </summary>
    public const float SwapShare = 0.5f;

    float fromX, fromY;

    /// <summary>The member whose portrait was pressed, or null when no portrait press is waiting.</summary>
    public WorldThing? Carried { get; private set; }

    /// <summary>The row it was pressed on, in the party's order at the press.</summary>
    public int From { get; private set; } = -1;

    /// <summary>Where on the bar it was pressed, in the bar's space, for the click a press that never moved still is.</summary>
    public (int X, int Y) PressedAt { get; private set; }

    /// <summary>Whether the press has become a drag: once, until the release.</summary>
    public bool Moving { get; private set; }

    /// <summary>A portrait press is waiting, a drag or not yet.</summary>
    public bool Held => Carried is not null;

    /// <summary>
    /// Whether a press on row <paramref name="row"/> of the bar may lift the portrait: a companion's row (never the first,
    /// the leader's), a party of three or more (with two there is no order to choose), the bar's rows exactly the
    /// party's members, nothing on the cursor and Ctrl up.
    /// </summary>
    public static bool Lifts(int row, IReadOnlyList<WorldThing> rows, IReadOnlyList<WorldThing> party, bool cursorBusy, bool ctrl) =>
        !cursorBusy && !ctrl && party.Count >= 3 && row >= 1 && row < rows.Count && rows.SequenceEqual(party);

    /// <summary>The left button went down on row <paramref name="row"/>'s portrait: window pixel and bar point both kept.</summary>
    public void Press(WorldThing member, int row, float x, float y, int barX, int barY)
    {
        Carried = member;
        From = row;
        Moving = false;
        (fromX, fromY) = (x, y);
        PressedAt = (barX, barY);
    }

    /// <summary>The pointer moved to window pixel (<paramref name="x"/>, <paramref name="y"/>): answers whether this is the move that made the drag.</summary>
    public bool Move(float x, float y)
    {
        if (Carried is null || Moving) return false;
        if (MathF.Abs(x - fromX) < Threshold && MathF.Abs(y - fromY) < Threshold) return false;
        return Moving = true;
    }

    /// <summary>The press is over (the release, or the party changed under it).</summary>
    public void Clear()
    {
        Carried = null;
        From = -1;
        Moving = false;
    }

    /// <summary>
    /// What letting go of row <paramref name="carried"/> at bar point (<paramref name="x"/>, <paramref name="y"/>) would do,
    /// over the portraits <paramref name="slots"/> (one a row, left to right, the leader's first).
    /// </summary>
    public static PartyDrop At(IReadOnlyList<PartySlot> slots, int carried, int x, int y, float step = StatusBar.RowStep)
    {
        int n = slots.Count;
        if (n < 3 || carried < 1 || carried >= n) return PartyDrop.None;
        // Off the portraits altogether: nothing.
        int top = slots.Min(s => s.Y), bottom = slots.Max(s => s.Y + s.Height);
        if (y < top || y > bottom || x < slots[0].X || x > slots[n - 1].X + slots[n - 1].Width) return PartyDrop.None;
        float half = SwapShare * MathF.Min(slots.Min(s => s.Width), step) / 2f;
        for (int i = 0; i < n; i++)
            if (MathF.Abs(x - slots[i].Middle) <= half)
                return i == 0 || i == carried ? PartyDrop.None : new PartyDrop(PartyDropKind.Swap, i);
        int before = 0;
        while (before < n && slots[before].Middle < x) before++;
        if (before == 0) return PartyDrop.None;                       // left of the leader's middle
        // Either side of its own row it lands where it is.
        if (before == carried || before == carried + 1) return PartyDrop.None;
        return new PartyDrop(PartyDropKind.Insert, before);
    }

    /// <summary>The party's order after <paramref name="drop"/>, or <paramref name="now"/> itself when it does nothing.</summary>
    public static IReadOnlyList<WorldThing> Order(IReadOnlyList<WorldThing> now, int carried, PartyDrop drop)
    {
        if (carried < 1 || carried >= now.Count) return now;
        switch (drop.Kind)
        {
            case PartyDropKind.Swap when drop.Row >= 1 && drop.Row < now.Count && drop.Row != carried:
            {
                var order = now.ToList();
                (order[carried], order[drop.Row]) = (order[drop.Row], order[carried]);
                return order;
            }
            case PartyDropKind.Insert when drop.Row >= 1 && drop.Row <= now.Count:
            {
                var order = now.ToList();
                var who = order[carried];
                order.RemoveAt(carried);
                order.Insert(drop.Row > carried ? drop.Row - 1 : drop.Row, who);
                return order;
            }
            default:
                return now;
        }
    }
}
