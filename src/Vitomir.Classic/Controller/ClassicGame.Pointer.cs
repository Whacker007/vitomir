// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game run's pointer: the pick, the clicks on the world and the panes, the casts.
using System.Globalization;
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Scene;
using Vitomir.Classic;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Engine.Video;
using Vitomir.Formats;
using Vitomir.Platform.Audio;
using Vitomir.Scripting;
using Vitomir;
using Vitomir.Platform.Modules;

namespace Vitomir.Classic;

unsafe sealed partial class ClassicGame
{

    void Pointer(float winX, float winY, int width, int height)
    {
        var (ux, uy) = StatusBar.FromWindow(winX, winY, width, height);
        // The same pixel in the space of the panes above the bar, which is not the bar's where they are centred above
        // it (UiLayout.Panes): the panes' hit tests take this one, the bar's and the boxes' the sheet's.
        var (px, py) = StatusBar.PaneFromWindow(winX, winY, width, height);
        cursorPass.X = ux;
        cursorPass.Y = uy;
        invPass.Pane!.Pointer = vaultPass.Pane!.Pointer = menus.Up ? null : (px, py);   // the hover outline
        hud.Pointer = menus.Up ? null : (ux, uy);
        rolloverPass.User = location.Steered;
        hovering = null;
        pointerGround = null;
        rolloverPass.Hovered = null;
        net?.View?.Point(null);                                  // the Storyteller's view's pointer
        // A quest row under the pointer fills both description boxes at once.
        rolloverPass.Custom = questPass.Enabled && questPass.Pane!.RowAt(px, py) is var row and >= 0
            ? questPass.Pane.Describe(row) : null;
        // A discipline's or a raise button's — written when the
        // hover changes, as 1.1 writes one (IconRollover.OnChange), or kept current
        // under the Fix `liverollover` (LiveRollover.Keep).
        rolloverPass.Lines = Vitomir.Engine.Ui.LiveRollover.Keep(rolloverPass.Lines, PowerRollover(ux, uy, px, py));
        // A control's own `hintstring` is what the boxes show while the pointer is on the
        // interface, and it comes from the pane rather than from the world.
        rolloverPass.Control = hud.Enabled ? hud.Bar.At((int)ux, (int)uy)?.Element : null;
        // The advancement bar's OK and Revert carry hints too (statusBarADV.nui; AdvancementRollover).
        if (advancement is not null && advBarPass.Enabled && Vitomir.Engine.Ui.AdvancementRollover.Hint(advBarPass.Pane, ux, uy) is { } advHint)
            rolloverPass.Control = advHint;
        // The shop's Buy and Sell carry hints ("Click here to buy item"), withheld while greyed
        // (a button in state 3 shows none).
        if (buyPass.Enabled && buyPass.Pane is { } hintPane)
            foreach (var (name, live) in new[] { (Vitomir.Engine.Ui.BuySellPane.BuyButton, hintPane.BuyEnabled),
                                                 (Vitomir.Engine.Ui.BuySellPane.SellButton, hintPane.SellEnabled) })
                if (hintPane.Over(name, px, py)) rolloverPass.Control = live ? hintPane.Element(name) : null;
        // A pane under the pointer swallows the pick, the same way it swallows the click.
        if (OverPane(px, py)) { SayPointer("hover", winX, winY, "a pane's"); return; }
        if (readPass.Enabled || location.SteeredWalker is not { } who) return;
        // **Only the game camera picks** (the camera's mode must be 1 before a ray is cast):
        // not a cutscene, not a camera path, not the first-person look. Vitomir picked whatever the
        // camera was doing, so things lit up under a pointer the cutscene had hidden: in play,
        // items highlighted under the invisible cursor. The
        // harness's own free camera (no game camera at all) still picks, which is ours.
        // The Storyteller's view of another place: the pointer picks there (cast from the camera's sector; StoryView).
        // Through the rows the view drew, not the host's renderer's (`StoryView.Ray`).
        if (net?.View?.Place is not null && !(hud.Enabled && hud.Bar.Covers((int)ux, (int)uy)) && net.View.OnWorld(winX, winY, width, height))
            net.View.Point(net.View.Ray(cam, winX, winY, width, height));
        if (!Picking()) return;
        // **The bar swallows the hover exactly as it swallows the click**, and for the engine's own
        // reason: the pane the cursor is on is found by testing its quads, and the world runs
        // the world only when there is none. The cursor used to take its verb from whatever
        // geometry lay behind the bar and turn into a sword over the interface. In play the pointer
        // changed its context even on the status bar, when something else lay beneath it.
        //
        // `Covers` is the quads and nothing else, so the top corners of the bar's own row — which
        // have no artwork over them — still reach the ground, which is the engine's behaviour too.
        if (hud.Enabled && hud.Bar.Covers((int)ux, (int)uy)) { SayPointer("hover", winX, winY, "the bar's"); return; }
        // And off the world's view — below its last row, beside its columns — there is nothing drawn at all, so
        // there is nothing to point at even where no pane's artwork reaches: the same test every click asks (`WorldHas`).
        if (!WorldHas("hover", winX, winY, width, height)) return;
        var (o, d) = Pick.Ray(cam, winX, winY, width, height, frame.WorldRows(height));
        var ground = WorldSweep.Pointer(location, o, d);
        pointerGround = ground?.Point;
        // The pointer only lands on a thing the engine would have let it land on, which
        // is what keeps a street full of neutral civilians from drawing a blade on every one.
        // In the Storyteller's seat, anything but the target (Net.StoryPointer).
        var (passed, offers) = Vitomir.Engine.Net.StoryPointer.For(net?.Server?.Storyteller == true, net?.Story?.Powers.Target, who.Thing);
        hovering = Pick.NearestThing(location.Things, o, d, out swallowed,
                                     ground?.Distance ?? float.MaxValue, passed, offers)?.Thing;
        // The description box is the other consumer of the hover, and the engine refreshes it from
        // the same place for the same reason.
        rolloverPass.Hovered = hovering;
    }


    // **Whether the world has a pointer event that no pane took** (ours): whether
    // its pixel is on the world's view, the columns and rows the world is drawn into this frame (`WorldFrame.Shows`,
    // the one test). **Everything of the pointer's that reaches the world asks here first** — the hover's pick, the left
    // button's order, the right button's cast, a held button's walk — so the hover and the click cannot disagree about
    // a place. Off the view the picture shows no world, and the event is nobody's: no pick, no order, no ring.
    // `what` names the asker for `--pointer-log`; null says nothing.
    bool WorldHas(string? what, float winX, float winY, int width, int height)
    {
        bool has = frame.Game.Shows(winX, winY, width, height);
        if (what is not null) SayPointer(what, winX, winY, has ? "the world's" : "nobody's");
        return has;
    }

    // `--pointer-log` (ours): whose each pointer event is — a pane's, the bar's, the world's
    // or nobody's — the hover's said when its answer or its pixel changes, a click's and a cast's every time.
    void SayPointer(string what, float winX, float winY, string whose)
    {
        if (!pointerLog) return;
        var said = (what, winX, winY, whose);
        if (what == "hover" && said == pointerSaid) return;
        if (what == "hover") pointerSaid = said;
        Console.WriteLine($"  pointer: {what} {winX:0},{winY:0} is {whose}");
    }


    // The engine folds the held keys into one mask once a frame and passes it to every
    // click, the interface's and the world's alike; `InputModifier` has which bit is which key and
    // what each does.
    // Which keys stand for the four is the key bindings' (`KeyInput.Modifiers`): Shift, Ctrl, Alt
    // and Space unless they have been moved.
    static InputModifier Modifiers() => KeyInput.Modifiers();


    // Why a move order was turned down, for the log. "Busy (None)" read like a fault in batch
    // a playtest and was not one: an action of None with the busy clock still running is an
    // animation playing out — the feed's release, a swing's recovery — and the order waits for it.
    static string Busy(WorldThing thing)
    {
        if (thing.Actor is not { } a) return "busy";
        if (a.Has(Vitomir.Engine.World.Actor.Flag.Dead)) return "dead";
        if (a.Has2(Vitomir.Engine.World.Actor.Flag2.Restrained)) return "held";
        int left = a.BusyUntil - (thing.Place?.Milliseconds ?? 0);
        return a.Action == Vitomir.Engine.Rpg.ActionMode.None
            ? $"still playing an animation for {left} ms"
            : $"busy ({a.Action}) for {left} ms";
    }


    // **The right button casts**: at the thing the pointer is resting
    // on, else at the ground under it — which first offers the click to an
    // effect icon that may be cancelled. A refusal is the red cross and `NEGATIVE`.
    void RightCast(float wx, float wy)
    {
        if (net?.View?.Cast(Modifiers()) is { } viewCast) { Console.WriteLine($"  story: cast: {viewCast}"); return; }   // the carried body's
        if (location.Steered is not { } steered) return;
        var mods = Modifiers();
        int ww, wh; host.PictureSize(&ww, &wh);
        // Off the world's view the right button is nobody's too (`WorldHas`): no cast at ground the picture
        // does not show, and no effect icon's cancel under a pane (that is the world's own handler).
        if (!WorldHas("cast", wx, wy, ww, wh)) return;
        var (ux, uy) = StatusBar.FromWindow(wx, wy, ww, wh);
        int result;
        string what;
        // A BLOCKSELECT thing under the pointer swallows the right button too (for the whole dispatch).
        if (swallowed)
        {
            cursor.Refuse(frame.Sim.Milliseconds);
            Console.WriteLine("  cast: swallowed by something marked BLOCKSELECT");
            return;
        }
        if (hovering is { } on && !ReferenceEquals(on, steered))
        {
            result = Vitomir.Engine.Rpg.CastInput.OnThing(steered, on, mods);
            what = Watch.Name(on);
        }
        else
        {
            var (o, d) = Pick.Ray(cam, wx, wy, ww, wh, frame.WorldRows(wh));
            var sweep = WorldSweep.Pointer(location, o, d);
            var point = sweep?.Point;
            // With `--devmode`, out of the seat: the move command puts the steered character at the spot (the options word's 0x80).
            if (KeyInput.StoryMove && net?.Story?.On != true
                && Vitomir.Engine.Net.StoryPowers.DevMoved(GameOptions.Current.Flags, net?.Client is not null, net?.Server?.Flags, location, steered, point, mods,
                                                           () => Vitomir.Engine.Ui.EffectIcons.Cancel(steered, hud.Enabled, ux, uy)) is { } dev)
            { Console.WriteLine($"  devmode: {dev}"); return; }
            // In the seat, on the ground: the move command puts the target at the spot, the plain button the ghost.
            if (net?.Story?.Powers.Ground(location, sweep, mods, KeyInput.StoryMove, steered, () => Vitomir.Engine.Ui.EffectIcons.Cancel(steered, hud.Enabled, ux, uy)) is { } put)
            { Console.WriteLine($"  story: {put}"); return; }
            result = Vitomir.Engine.Rpg.CastInput.OnGround(steered, point, mods,
                () => Vitomir.Engine.Ui.EffectIcons.Cancel(steered, hud.Enabled, ux, uy));
            what = point is { } p ? $"{p.X:0},{p.Y:0},{p.Z:0}" : "the ground";
        }
        if (result == (int)Vitomir.Engine.Rpg.OrderResult.Refused) cursor.Refuse(frame.Sim.Milliseconds);
        var current = steered.Actor?.Disciplines.Current?.Name ?? "nothing selected";
        Console.WriteLine($"  cast: {current} at {what} -> {(Vitomir.Engine.Rpg.OrderResult)result}");
    }


    // A harness step's `@template`: the pixel that thing is drawn at now (HarnessAim).
    // While the Storyteller's view of another place is up, that place's things as the view draws them (its rows,
    // its own test of the pixel), the carried body among them.
    (float X, float Y)? AimAt(string template)
    {
        StoryView? shown = net?.View is { Place: not null } storyView ? storyView : null;
        var place = shown?.Place ?? location;
        // The carried body itself when it is named (the place may have others of its template: Temesvar has its own drunk).
        var it = shown?.Body is { } body && body.Template.Equals(template, StringComparison.OrdinalIgnoreCase) ? body
            : HarnessAim.Find(place, shown?.Body ?? location.Steered, template);
        if (it is null) return null;
        int ww, wh; host.PictureSize(&ww, &wh);
        int rows = shown?.Rows(wh) ?? frame.WorldRows(wh);
        if (HarnessAim.Screen(cam, HarnessAim.Mark(it), ww, wh, rows) is not { } px) return null;
        if (shown is not null ? !shown.OnWorld(px.X, px.Y, ww, wh) : !WorldHas(null, px.X, px.Y, ww, wh)) return null;
        var (bx, by) = StatusBar.FromWindow(px.X, px.Y, ww, wh);
        return hud.Enabled && hud.Bar.Covers((int)bx, (int)by) ? null : px;
    }


    // A left press with the read dialog up: its scroll bar or its OK, held for
    // the release; anywhere else swallowed (`DialogClicks`).
    void ReadDialogPress(float px, float py)
    {
        var took = Vitomir.Engine.Ui.DialogClicks.Left(readPass.Pane!, (int)px, (int)py);
        if (took.Control is { } track) audio.Control(track.Type, release: false);
        if (took.Said is { } said) Console.WriteLine($"  ui: {said}");
        if (took.What != Vitomir.Engine.Ui.DialogClicks.Took.Ok) return;
        readOkPressed = true;
        Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonDepress);
    }


    // The left button's release: a drop, then whichever element was pressed if the pointer is still
    // on it (event 1) — a drag that ends where it began is a click too.
    // `rx`, `ry`: the pointer in the sheet (the bar's, the dialog's); `prx`, `pry`: in the panes above the bar.
    void Release(float rx, float ry, int prx, int pry)
    {
        if (readOkPressed)
        {
            readOkPressed = false;
            if (readPass.Enabled && Vitomir.Engine.Ui.ReadPaneOpen.OkAt(readPass.Pane!.Pane, (int)rx, (int)ry))
            {
                Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonRelease);
                readPass.Pane!.Open(null);                 // the read pane's OK
                Console.WriteLine("  ui: closed the document");
            }
        }
        if (vaultPass.Pressed is { } vaultButton)
        {
            vaultPass.Pressed = null;
            if (vaultPass.Enabled && vaultPass.Pane!.ButtonAt(prx, pry) == vaultButton)
            {
                Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonRelease);
                ShowVault(false);
            }
        }
        if (invPass.Pressed is not null)
        {
            invPass.Pressed = null;
            if (invPass.Enabled && !invPass.GoldGreyed && Vitomir.Engine.Ui.DropCash.At(invPass.Pane!, prx, pry))
            {
                Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonRelease);
                Vitomir.Engine.Ui.DropCash.Ask(menus, location.Steered, strings.Find, m => Console.WriteLine($"  ui: {m}"));
            }
        }
        if (discDrag.Release() is { } dropped)          // event 7 before event 1
        {
            // Only a quick slot takes it; anywhere else it simply goes back.
            int slot = hud.Enabled ? hud.Bar.PowerSlotAt((int)rx, (int)ry) : -1;
            if (slot >= 0) Console.WriteLine($"  ui: {hud.Bar.Assign(slot, dropped)}");
            if (discPass.Enabled) RebuildDisciplines();
        }
        if (advBarPass.Pressed is { } advButton)
        {
            advBarPass.Pressed = null;
            if (advBarPass.ButtonAt(rx, ry) == advButton)
            {
                Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonRelease);
                if (advButton == AdvancementBarPass.Done) AdvanceDone(); else AdvanceRevert(); // OK jumps to "Finished"; Revert button
            }
        }
        if (raisePress >= 0)
        {
            if (advancement is not null && charPass.Pane!.RaiseAt(prx, pry) == raisePress)
            {
                Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonRelease);
                bool bought = advancement.BuyStat(raisePress);   // the + button buys the raise and the prices redraw live
                Console.WriteLine($"  advance: {(Vitomir.Engine.World.ActorStat)raisePress} {(bought ? "raised" : "not raised")}, "
                                + $"{advancement.Subject.Actor?.SpendableExperience} left");
            }
            raisePress = -1;
        }
        if (discPress.Part != Vitomir.Engine.Ui.DisciplinePane.Part.None && discPass.Enabled)
        {
            var dp = discPass.Pane!;
            if (dp.At(prx, pry) == discPress) PaneClick(dp, discPress.Part, discPress.Index);
            discPress = (Vitomir.Engine.Ui.DisciplinePane.Part.None, -1);
        }
        PartyRelease(rx, ry);                           // a carried portrait's drop, or its click (ours, `partyorder`)
        if (barPress >= 0)
        {
            if (hud.Enabled && hud.Bar.PowerSlotAt((int)rx, (int)ry) == barPress)
                Console.WriteLine($"  ui: {hud.Bar.SelectSlot(barPress, advancement is not null)}");
            barPress = -1;
        }
        cursor.Dragging = null;                         // the cursor itself again
        // A purchase ends by re-running the hover (the buy and the raise button's click both re-run
        // it), so the price under the pointer is the next one rather than the one just paid.
        rolloverPass.Lines = PowerRollover(rx, ry, prx, pry);
    }


    // A click on the discipline pane (the pane's handlers).
    void PaneClick(Vitomir.Engine.Ui.DisciplinePane dp, Vitomir.Engine.Ui.DisciplinePane.Part part, int index)
    {
        switch (part)
        {
            case Vitomir.Engine.Ui.DisciplinePane.Part.Slot:
                // Nothing while advancing or on an empty row; else DISCIPLINESELECT and
                // the row's discipline is the steered character's current one.
                if (advancement is not null || dp.Rows()[index].Discipline is not { } d || location.Steered is not { } who) return;
                Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.DisciplineSelect);
                Vitomir.Engine.Rpg.DisciplineBook.Select(who, d);
                hud.Bar.Update();
                Console.WriteLine($"  ui: {d.Name} selected");
                return;
            case Vitomir.Engine.Ui.DisciplinePane.Part.TabLeft when dp.CanTabLeft: dp.ScrollTabs(-1); return;
            case Vitomir.Engine.Ui.DisciplinePane.Part.TabRight when dp.CanTabRight: dp.ScrollTabs(+1); return;
            case Vitomir.Engine.Ui.DisciplinePane.Part.RowUp when dp.CanScrollUp: dp.ScrollRows(dp.FirstRow - 1); return;     // scroll-up arrow
            case Vitomir.Engine.Ui.DisciplinePane.Part.RowDown when dp.CanScrollDown: dp.ScrollRows(dp.FirstRow + 1); return; // scroll-down arrow
            case Vitomir.Engine.Ui.DisciplinePane.Part.Learn:
                // Buy, and keep the row scroll the rebuild would otherwise reset.
                if (advancement is null || dp.Rows()[index].Discipline is not { } learn) return;
                int first = dp.FirstRow;
                bool bought = advancement.BuyDiscipline(learn.Definition);
                RebuildDisciplines();
                dp.ScrollRows(first);
                Console.WriteLine($"  advance: {learn.Name} {(bought ? $"now level {learn.Level + 1}" : "not bought")}, "
                                + $"{advancement.Subject.Actor?.SpendableExperience} left");
                return;
        }
    }


    // What the description boxes say over the discipline pane, a quick slot or a raise button.
    (IReadOnlyList<Vitomir.Engine.Ui.RolloverLine>, IReadOnlyList<Vitomir.Engine.Ui.RolloverLine>, bool, object)? PowerRollover(float ux, float uy, float px, float py)
    {
        // `ux`, `uy`: the pointer in the sheet, for the bar's slots, belt and rows; `px`, `py`: in the panes above the
        // bar, for the shop's, the disciplines', the character sheet's and the pack's (UiLayout.Panes).
        // The Storyteller's object list: the hovered template, or the boxes down.
        bool onObjects = false;
        var objects = net?.Story is { } storyteller ? storyteller.ObjectRollover(out onObjects) : null;
        if (onObjects) return objects;
        // The shop: a shelf row or the display box, both boxes at
        // once (mode 2). Anywhere else on the pane takes them down.
        if (buyPass.Enabled && buyPass.Pane is { } bp)
        {
            bp.HoverAt(px, py);
            var said = bp.Hovered >= 0 ? bp.DescribeHovered()
                     : bp.Over(Vitomir.Engine.Ui.BuySellPane.DisplayBox, px, py) ? bp.DescribeShown() : null;
            // Every line the description hands the box is a row, an empty one blank, as the pack's are.
            if (said is { } d) return (new[] { d.Name }, d.Detail, true, (object)("shop", bp.Hovered, bp.SelectedItem, bp.SelectedTreasure));
            if (bp.Covers(px, py)) return null;
        }
        if (discPass.Enabled && discPass.Pane!.Covers(px, py))
        {
            var dp = discPass.Pane;
            var (part, index) = dp.At(px, py);
            if (part == Vitomir.Engine.Ui.DisciplinePane.Part.Tab)
                return (new[] { new Vitomir.Engine.Ui.RolloverLine(dp.Tabs().ElementAt(index).Group is var g ? strings[g.Name] : "", Vitomir.Engine.Ui.Rollover.White) }, [], true, (object)("tab", index));
            // A row's icon or name: the discipline's description in both boxes at once, else the
            // control's plain hint.
            if (part is Vitomir.Engine.Ui.DisciplinePane.Part.Slot or Vitomir.Engine.Ui.DisciplinePane.Part.Learn
                && dp.Rows()[index].Discipline is { } d)
            {
                var (small, large) = Vitomir.Engine.Ui.DisciplinePane.Describe(d, strings.Find);
                // The learn button adds its price, white or red by what can be spent.
                if (part == Vitomir.Engine.Ui.DisciplinePane.Part.Learn && advancement is not null
                    && Vitomir.Engine.Rpg.Advancement.DisciplineCost(advancement.Subject, powers, d.Definition) is var cost and >= 0)
                    small.Add(new(Vitomir.Engine.Ui.Strings.Format(strings.Find, "UI_ADVANCEMENT_DISCCOST", $"%s{strings[d.Definition.Name]}%d{cost}"),
                                  advancement.Subject.Actor!.SpendableExperience >= cost ? Vitomir.Engine.Ui.Rollover.White : Vitomir.Engine.Ui.DisciplinePane.Red));
                return (small, large, true, d);
            }
            return null;
        }
        if (advancement is not null && charPass.Enabled && charPass.Pane!.RaiseAt(px, py) is var stat and >= 0
            && charPass.Pane.RaiseCost(stat, strings.Find) is { } price)
            return (new[] { price }, [], true, (object)("raise", stat));
        // A quick slot: the small box now and the large one 1.5 s later (mode 3).
        if (hud.Enabled && hud.Bar.PowerSlotAt((int)ux, (int)uy) is var s and >= 0 && hud.Bar.PowerIn(s) is { } sd)
        {
            var (small, large) = Vitomir.Engine.Ui.DisciplinePane.Describe(sd, strings.Find);
            return (small, large, false, sd);
        }
        // An item icon or a party row: an open grid's first, in the panes' space, else the bar's.
        return hands is { } held && held.Over((int)px, (int)py)
            ? Vitomir.Engine.Ui.IconRollover.Under(held, null, (int)px, (int)py, location.Steered, rolloverPass.Text, rolloverPass.Effects)
            : Vitomir.Engine.Ui.IconRollover.Under(null, hud.Enabled ? hud.Bar : null, (int)ux, (int)uy,
                                                    location.Steered, rolloverPass.Text, rolloverPass.Effects);
    }

    // The status bar's message box when the pointer is on it and the bar is up — the one control
    // on the bar that the wheel reaches (it must be the control under the pointer).
    // The discipline in the slot under a point of the bar's space, for a drag's start (event 6, the
    // drag's): a row of the discipline pane or one of the six quick slots, never the large
    // current one; nothing under the read dialog or a menu, which take the pointer.
    // (A window pixel: the pane's rows through the panes' map, the bar's slots through the sheet's)
    Vitomir.Engine.Rpg.Discipline? DisciplineUnder(float wx, float wy)
    {
        var (x, y) = StatusBar.FromWindow(wx, wy, winW, winH);
        var (px, py) = StatusBar.PaneFromWindow(wx, wy, winW, winH);
        if (readPass.Enabled || menus.Up) return null;
        if (discPass.Enabled && discPass.Pane!.Covers(px, py))
            return discPass.Pane.At(px, py) is (Vitomir.Engine.Ui.DisciplinePane.Part.Slot, var row) ? discPass.Pane.Rows()[row].Discipline : null;
        return hud.Enabled && hud.Bar.PowerSlotAt(x, y) is >= 0 and var slot ? hud.Bar.PowerIn(slot) : null;
    }

    Vitomir.Engine.Ui.MessageBox? OnMessageBox()
    {
        if (!hud.Enabled || hud.Bar.Messages is not { } box || !box.Box.Shown) return null;
        int w, h; host.PictureSize(&w, &h);
        var (x, y) = StatusBar.FromWindow(pointerX, pointerY, w, h);
        return hud.Bar.At(x, y) == box.Box ? box : null;
    }

    Vitomir.Engine.Rpg.MoveOrder.Held? HeldGround()
    {
        if (pointerOut || worldCam.Mode != CameraMode.Game || menus.Up || buyPass.Enabled || advancement is not null) return null;
        if ((InputTape.Buttons(out _, out _) & LoopButton.Mask(LoopButton.Left)) == 0) return null;
        int hw, hh; host.PictureSize(&hw, &hh);
        var (hx, hy) = StatusBar.FromWindow(pointerX, pointerY, hw, hh);
        var (phx, phy) = StatusBar.PaneFromWindow(pointerX, pointerY, hw, hh);
        if (hud.Enabled && hud.Bar.At(hx, hy) is not null || OverPane(phx, phy)) return null;
        if (!WorldHas(null, pointerX, pointerY, hw, hh)) return null;   // off the world's view: nobody's ground
        var (ho, hd) = Pick.Ray(cam, pointerX, pointerY, hw, hh, frame.WorldRows(hh));
        return WorldSweep.Pointer(location, ho, hd) is { Walkable: true } under
            ? new(under.Point, under.PathNode, (InputTape.Mods() & LoopMods.Shift) != 0) : null;
    }


    // What a click on a thing comes to. The verb is the engine's and so
    // is the switch on it; only the attack is written, because taking,
    // using and unstaking all need machinery that does not exist yet. Returns whether the click was
    // spent — a verb of None falls through to the floor, which is what the engine does too.
    // The drop order, and the whole of the ground click while something is held (the
    // click returns its answer): a refusal is the red cross (ClickAnswer) and **never a walk**
    // (Vitomir had fallen through to the ordinary walk until then).
    void PutDown(WorldThing who, Vitomir.Engine.Rpg.Item item, Hit at, int nowMs, InputModifier mods)
    {
        // The struck surface; a put-down that walks leaves the ring at the point.
        var got = Vitomir.Engine.Rpg.PutDownOrder.Click(frame.Sim, who, item, at, nowMs, mods);

        if (Vitomir.Engine.Rpg.ClickAnswer.RedCross(got)) cursor.Refuse(nowMs);
        switch (got)
        {
            case OrderResult.Started:
                Console.WriteLine($"  click: carrying {item.Name} over to put it down");
                break;
            case OrderResult.Ignored:                     // space held: it is already on the floor
                Console.WriteLine($"  click: dropped {item.Name}");
                break;
            case OrderResult.Busy:
                Console.WriteLine($"  click: still busy, not putting {item.Name} down yet");
                break;
            default:
                Console.WriteLine($"  click: cannot put {item.Name} down there");
                break;
        }
    }


    bool Verb(WorldThing me, WorldThing thing, int nowMs, bool alt, InputModifier mods)
    {
        var verb = Cursor.Verb(me, thing);
        string name = Watch.Name(thing);
        Journal.Current?.Write(nowMs, JournalKind.Click,
            ("on", name), ("type", thing.Type), ("verb", verb), ("alt", alt),
            ("gap", Bodies.Gap(me, thing)));
        switch (verb)
        {
            case CursorVerb.Attack:
            {
                ClickOrders.TakeThrownInHand(me);   // a grenade on the pointer goes in hand first
                var weapon = me.Actor?.Weapon;
                int which = alt && weapon is { AttackCount: > 1 } ? 1 : 0;
                // Holding the button is what keeps a character swinging,
                // so the order and the standing ask are set together and the mouse-up clears it.
                // Space holds the attack: the strike plays where he stands, with no closing in
                // (the attack's bit-8 arm: the swing).
                bool hold = mods.HasFlag(InputModifier.Space);
                if (me.Actor is { } actor) actor.Repeating = new Actions.Asking(thing, which, hold);
                // Ctrl rides along: it is what lets the party in on a townsperson or a guard.
                var result = Actions.Attack(me, thing, weapon, which, nowMs, strikeNow: hold, motions: location.Motions,
                                            mods: mods);
                Console.WriteLine(result switch
                {
                    OrderResult.Started => $"  click: attacking {name}"
                                           + (which == 1 ? " with its second attack" : "")
                                           + (weapon is null ? "" : $" ({weapon.Name})"),
                    OrderResult.Busy => $"  click: still busy, not attacking {name} yet",
                    OrderResult.Ignored => $"  click: already attacking {name}",
                    _ => $"  click: cannot attack {name}"
                          + (me.Actor?.Weapon is null ? " — nothing to fight with" : ""),
                });
                return true;
            }
            case CursorVerb.Take:
            {
                // Walk over, then take. The modifiers ride along on the order, because
                // **alt** decides between the hand and the pack when the character arrives.
                var got = Actions.Fetch(me, thing, nowMs, mods);
                Console.WriteLine(got switch
                {
                    OrderResult.Started => $"  click: going to pick up {name}"
                                           + (mods.HasFlag(InputModifier.Alt) ? " into the hand" : ""),
                    OrderResult.Busy => $"  click: still busy, not picking up {name} yet",
                    _ => $"  click: cannot pick up {name}",
                });
                Journal.Current?.Write(nowMs, JournalKind.Order,
                    ("what", "take"), ("who", Watch.Name(me)), ("at", name), ("result", got));
                return true;
            }
            case CursorVerb.Use:
            {
                // **Talking is using.** The classifier gives a TALKTO character the use verb,
                // and the use order does the rest the way it does for a lever: walk up,
                // stop, and `clicked` to whatever script answers for the character — its own, or
                // the scene's that captured it — which is what calls `ExecuteConversation`.
                var used = Actions.Use(me, thing, nowMs, location.Motions, mods);   // space: from here
                // **What answers for a thing is not always its own script.** A lever usually has
                // none and is worked through a capture — the door's script holds it — so "no
                // script" was a lie that made a working lever look broken. What actually heard the
                // click is reported by the script layer when it arrives, which is after the walk.
                string answers = thing.Script.Length > 0 ? $" ({thing.Script})"
                               : thing.Captures.Count > 0 ? $" (held by {thing.Captures.Count} script(s))"
                               : thing.Type == ThingType.Container ? " (a container: opening is a death here)"
                               : ", which has nothing to answer";
                Console.WriteLine(used switch
                {
                    OrderResult.Started => $"  click: using {name}{answers}",
                    OrderResult.Busy => $"  click: still busy, not using {name} yet",
                    OrderResult.Ignored => $"  click: already using {name}",
                    _ => $"  click: cannot use {name} — nowhere to stand to reach it",
                });
                Journal.Current?.Write(nowMs, JournalKind.Order,
                    ("what", "use"), ("who", Watch.Name(me)), ("at", name), ("result", used));
                return true;
            }
            case CursorVerb.Unstake:
            {
                var pulled = Vitomir.Engine.Rpg.Stake.Order(me, thing, nowMs);     // the stake's order
                Console.WriteLine($"  click: pulling the stake out of {name}: {pulled}");
                Journal.Current?.Write(nowMs, JournalKind.Order,
                    ("what", "unstake"), ("who", Watch.Name(me)), ("at", name), ("result", pulled));
                return true;
            }
            default:
                return false;
        }
    }


    // A walk order ends the hold-to-attack. Returns false always: it only clears, and the move order's
    // own gate (`Actions.ReadyToMove`), which cancels whatever the character was doing the engine's
    // way, is the next clause.
    bool Ordered(WorldThing me)
    {
        if (me.Actor is { } a) a.Repeating = null;
        return false;
    }


    // The regroup half of Ctrl + left click on the ground: it happens and the click goes on.
    bool RegroupFirst()
    {
        Console.WriteLine($"  click: {hud.Bar.RegroupHere()}");
        return false;
    }
}
