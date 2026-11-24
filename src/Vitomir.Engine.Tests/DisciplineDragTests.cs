// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// A discipline to a quick slot, 1.1's way: a **drag and only a drag**. A held left
/// button that moves three either way becomes a drag; the drag's start lifts the
/// discipline in the slot the pointer was on as it began (event 6); letting go drops it (event 7),
/// where a quick slot takes it and anywhere else it goes back. Nothing in 1.1 carries a
/// discipline past the release, so a click never picks one up and the next click never puts one
/// down — the note that said so is corrected here, and these tests pin it.
/// </summary>
public class DisciplineDragTests
{
    static Discipline Power(string name) => new()
    {
        Definition = new Vitomir.Formats.NddDiscipline(name, "Group", name + "Icon", "", "", 0, default, 0f, 0f, 0f, 0, 0,
                                                       [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], []),
    };

    [Fact]
    public void A_press_that_moves_less_than_three_pixels_lifts_nothing()
    {
        var drag = new DisciplineDrag();
        var awe = Power("Awe");
        drag.Press(100, 100);
        Assert.Null(drag.Move(102.9f, 97.1f, (_, _) => awe, advancing: false, cursorBusy: false));
        Assert.False(drag.Moving);
        Assert.Null(drag.Carried);
        Assert.Null(drag.Release());                       // a click drops nothing
    }

    [Fact]
    public void The_move_that_reaches_three_either_way_begins_the_drag()
    {
        var drag = new DisciplineDrag();
        var awe = Power("Awe");
        drag.Press(100, 100);
        Assert.Same(awe, drag.Move(100, 97, (_, _) => awe, advancing: false, cursorBusy: false));   // three up is enough
        Assert.True(drag.Moving);
        Assert.Same(awe, drag.Carried);
    }

    /// <summary>
    /// The drag's start goes to the control hovered as of the last frame — 1.1 polls and dispatches
    /// before the interface's frame moves the hover — so a flick that leaves the slot in
    /// one move still lifts the slot it left, and a slot the flick lands on is not lifted.
    /// </summary>
    [Fact]
    public void A_flick_lifts_the_slot_the_pointer_was_on_before_the_move()
    {
        var awe = Power("Awe");
        Discipline? Slot(float x, float y) => x < 110 ? awe : null;   // a slot from x 90 to 110

        var drag = new DisciplineDrag();
        drag.Press(100, 100);
        drag.Move(101, 100, Slot, advancing: false, cursorBusy: false);
        Assert.Same(awe, drag.Move(180, 100, Slot, advancing: false, cursorBusy: false));
        drag.Release();

        drag.Press(60, 100);                               // pressed beside the slot, flicked onto it
        Assert.Null(drag.Move(100, 100, (x, _) => x > 90 && x < 110 ? awe : null, advancing: false, cursorBusy: false));
        Assert.Null(drag.Release());
    }

    /// <summary>
    /// Event 6 is raised once a drag (the drag flag is on for one poll): a drag that began over
    /// nothing does not pick up the slot it later crosses, and one that began over a slot keeps what
    /// it lifted.
    /// </summary>
    [Fact]
    public void The_drag_begins_once_so_a_slot_reached_later_is_not_lifted()
    {
        var drag = new DisciplineDrag();
        var awe = Power("Awe");
        drag.Press(100, 100);
        Assert.Null(drag.Move(104, 100, (_, _) => null, advancing: false, cursorBusy: false));
        Assert.True(drag.Moving);
        Assert.Null(drag.Move(140, 100, (_, _) => awe, advancing: false, cursorBusy: false));
        Assert.Null(drag.Carried);
        Assert.Null(drag.Release());
    }

    /// <summary>The drag start's refusals: the advancement screen up, an icon already on the cursor, an empty slot.</summary>
    [Fact]
    public void Nothing_lifts_while_advancing_with_an_icon_on_the_cursor_or_from_an_empty_slot()
    {
        var awe = Power("Awe");
        foreach (var (advancing, busy, under) in new[] { (true, false, awe), (false, true, awe), (false, false, (Discipline?)null) })
        {
            var drag = new DisciplineDrag();
            drag.Press(0, 0);
            Assert.Null(drag.Move(5, 5, (_, _) => under, advancing, busy));
            Assert.True(drag.Moving);
            Assert.Null(drag.Release());
        }
    }

    [Fact]
    public void The_release_drops_what_was_lifted_and_the_cursor_is_empty_again()
    {
        var drag = new DisciplineDrag();
        var awe = Power("Awe");
        drag.Press(10, 10);
        drag.Move(20, 10, (_, _) => awe, advancing: false, cursorBusy: false);
        Assert.Same(awe, drag.Release());
        Assert.Null(drag.Carried);
        Assert.False(drag.Moving);
    }

    /// <summary>
    /// **No carry-and-click.** A plain click on a slot lifts nothing, and a drag that has been let go
    /// has nothing left to put down: the click after it drops nothing.
    /// </summary>
    [Fact]
    public void A_click_neither_picks_a_discipline_up_nor_puts_one_down()
    {
        var drag = new DisciplineDrag();
        var awe = Power("Awe");
        drag.Press(50, 50);
        Assert.Null(drag.Release());                       // clicked on the slot: nothing on the cursor
        Assert.Null(drag.Carried);

        drag.Press(50, 50);
        drag.Move(60, 50, (_, _) => awe, advancing: false, cursorBusy: false);
        Assert.Same(awe, drag.Release());                  // dropped (on nothing, say): back where it was
        drag.Press(300, 400);
        Assert.Null(drag.Release());                       // the next click has nothing to drop
    }

    [Fact]
    public void A_discipline_let_go_while_it_is_on_the_cursor_comes_off_it()
    {
        var drag = new DisciplineDrag();
        var (awe, celerity) = (Power("Awe"), Power("Celerity"));
        drag.Press(0, 0);
        drag.Move(0, 9, (_, _) => awe, advancing: false, cursorBusy: false);
        drag.LetGo(celerity);
        Assert.Same(awe, drag.Carried);
        drag.LetGo(awe);
        Assert.Null(drag.Carried);
        Assert.Null(drag.Release());
    }

    /// <summary>
    /// What the drop does on a quick slot: the discipline there is put out of the bar (its
    /// slot −1), the dropped one goes in — leaving the slot it came from, since a discipline knows
    /// one — and nothing is selected.
    /// </summary>
    [Fact]
    public void A_drop_on_a_quick_slot_puts_the_one_there_out_and_selects_nothing()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var bar = new StatusBar(new UiAssets(files).Pane(StatusBar.PaneName), place);
        var powers = new DisciplineDefinitions(files);
        var book = place.Steered!.Actor!.Disciplines;
        var awe = book.Add(powers["Awe"]!, 1, quickSlot: 0);
        var celerity = book.Add(powers["Celerity"]!, 1, quickSlot: 1);
        var current = book.Current;
        bar.Update();

        bar.Assign(1, awe);                                // dragged from slot 0 onto slot 1
        Assert.Equal(1, awe.QuickSlot);
        Assert.Equal(-1, celerity.QuickSlot);
        Assert.Null(bar.PowerIn(0));
        Assert.Same(awe, bar.PowerIn(1));
        Assert.Same(current, book.Current);

        bar.Assign(4, celerity);                           // from the pane onto an empty slot
        Assert.Same(celerity, bar.PowerIn(4));
        Assert.Same(awe, bar.PowerIn(1));
    }

    static WorldThing Caster() => new()
    {
        Index = -1, Guid = 7, Scene = new Vitomir.Formats.NsdThing(), Template = "t", Type = ThingType.Player,
        Actor = new Actor { Health = 100f, MaxHealth = 100f },
    };

    /// <summary>
    /// A discipline let go — here a temporary one whose cast was cancelled —
    /// while its icon is on the cursor puts the cursor back, so the drag's drop drops
    /// nothing. 1.1 compares the icons, which belong to the definition, so a temporary Awe let go
    /// takes the player's own Awe off the cursor too; another discipline's is left alone.
    /// </summary>
    [Fact]
    public void A_discipline_let_go_while_its_icon_is_on_the_cursor_comes_off_it()
    {
        var awe = Power("Awe");
        var celerity = Power("Celerity");
        var scrollAwe = new Discipline { Definition = awe.Definition, Flags = Discipline.Temporary };
        var scrollCelerity = new Discipline { Definition = celerity.Definition, Flags = Discipline.Temporary };
        var caster = Caster();
        var drag = new DisciplineDrag();
        bool cursorBack = false;
        void Heard(WorldThing _, Discipline d) { if (drag.LetGo(d)) cursorBack = true; }
        DisciplineBook.LetGo += Heard;
        try
        {
            drag.Press(100, 100);
            Assert.Same(awe, drag.Move(110, 100, (_, _) => awe, advancing: false, cursorBusy: false));

            caster.Actor!.PendingDiscipline = scrollCelerity;
            Cast.Discard(caster);                          // another discipline let go: the drag keeps Awe
            Assert.False(cursorBack);
            Assert.Same(awe, drag.Carried);

            caster.Actor.PendingDiscipline = scrollAwe;
            Cast.Discard(caster);                          // the same definition: off the cursor
            Assert.True(cursorBack);
            Assert.Null(drag.Carried);
            Assert.True(drag.Moving);                      // the button is still down, still a drag
            Assert.Null(drag.Release());                   // and the drop has nothing to drop
        }
        finally { DisciplineBook.LetGo -= Heard; }
    }
}
