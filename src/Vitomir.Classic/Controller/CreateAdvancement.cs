// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The advancement screen after Create, in the front end:
// a new multiplayer character spends its starting experience before it is written. The screen is the
// game's own (the character sheet, the discipline pane and
// `statusBarADV`), over the front end rather than a world; Finished writes the character when not in a
// network game and goes back. The rules are `Rpg.Advancement`'s.
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Classic;
using Vitomir.Platform.Modules;

namespace Vitomir;

sealed unsafe class CreateAdvancement
{
    readonly MpMenus mp;
    readonly DisciplineDefinitions powers;
    readonly CharacterPass sheet;
    readonly DisciplinePass disciplines;
    readonly AdvancementBarPass bar;
    readonly RolloverPass rollover;
    readonly Func<string, string?> find;
    readonly Func<(float X, float Y)> pointer;
    Advancement? advancement;
    NetCharacter? made;
    Action<NetCharacter>? done;
    int raisePress = -1;
    (DisciplinePane.Part Part, int Index) discPress = (DisciplinePane.Part.None, -1);

    public CreateAdvancement(UiCanvas ui, MpMenus mp, DisciplineDefinitions powers, Strings text, Func<(float X, float Y)> pointer)
    {
        this.mp = mp;
        this.powers = powers;
        this.pointer = pointer;
        find = text.Find;
        sheet = new CharacterPass(ui) { Pane = CharacterPass.Load(ui.Assets) };
        sheet.Pane!.Strings = key => find(key) ?? key;
        disciplines = new DisciplinePass(ui) { Pane = DisciplinePass.Load(ui.Assets) };
        disciplines.Pane!.Powers = powers;
        disciplines.Pane.Text = find;
        bar = new AdvancementBarPass(ui);
        rollover = new RolloverPass(ui) { Text = text, Enabled = false };
        Passes = new UiPasses(sheet, disciplines, bar, new Hovering(this));
        mp.AdvanceNew = Open;
    }

    /// <summary>The four passes — the sheet, the discipline pane, the bar and the description boxes — drawn over the front end's panes while the screen is up.</summary>
    public IUiPass Passes { get; }

    /// <summary>Whether the screen is up.</summary>
    public bool Up => advancement is not null;

    /// <summary>
    /// The advancement screen on the character Create made: its body built (<see cref="MpMenus.NewBody"/>), the
    /// snapshot taken, and the sheet and the discipline pane up in advancing mode.
    /// </summary>
    void Open(NetCharacter character, Action<NetCharacter> then)
    {
        if (mp.NewBody(character, powers) is not { } body)
        {
            Console.WriteLine($"  advance: no body for {character.Template}; written as it is");
            then(character);
            return;
        }
        made = character;
        done = then;
        advancement = new Advancement(body, [body], powers);
        sheet.Pane!.Subject = body.Actor;
        sheet.Pane.Advancing = true;
        sheet.Enabled = disciplines.Enabled = bar.Enabled = rollover.Enabled = true;
        Rebuild();
        Console.WriteLine($"  advance: {character.Name} has {body.Actor!.SpendableExperience} experience to spend");
    }

    void Rebuild()
    {
        var pane = disciplines.Pane!;
        pane.Advancing = advancement is not null;
        pane.Rebuild(advancement?.Subject, true);
    }

    /// <summary>
    /// Finished: the character as it now stands written — its sheet over the
    /// template's kit, which the host dresses it in — and the front end goes on.
    /// </summary>
    void Finish()
    {
        if (advancement is not { Subject: var body } || made is null) return;
        var sheetNow = NetSheet.From(body) with { Kit = true, Effects = null };
        var final = made with { Saved = sheetNow };
        Close();
        Console.WriteLine($"  advance: done, {sheetNow.Spendable} left");
        done?.Invoke(final);
        done = null;
    }

    /// <summary>"Revert character stats", and Escape: everything back as the screen opened it.</summary>
    void Revert()
    {
        if (advancement is null) return;
        advancement.Reset();
        Rebuild();
        Console.WriteLine("  advance: put back as they were");
    }

    void Close()
    {
        advancement = null;
        made = null;
        sheet.Pane!.Advancing = false;
        sheet.Pane.Subject = null;
        sheet.Enabled = disciplines.Enabled = bar.Enabled = rollover.Enabled = false;
        rollover.Lines = null;
        rollover.Control = null;
        bar.Pressed = null;
        raisePress = -1;
        discPress = (DisciplinePane.Part.None, -1);
    }

    /// <summary>
    /// One input event while the screen is up (true: taken, the menus do not see it). Buttons act on
    /// the release over what was pressed, as the game's do; Escape reverts.
    /// </summary>
    public bool Input(in LoopInput e, int width, int height)
    {
        if (!Up) return false;
        switch (e.Kind)
        {
            case LoopInputKind.ButtonDown when e.Code == LoopButton.Left:
            {
                var (x, y) = StatusBar.FromWindow(e.X, e.Y, width, height);
                Press(x, y);
                return true;
            }
            case LoopInputKind.ButtonUp when e.Code == LoopButton.Left:
            {
                var (x, y) = StatusBar.FromWindow(e.X, e.Y, width, height);
                Release(x, y);
                return true;
            }
            case LoopInputKind.KeyDown when e.Key == LoopKey.Escape && !e.Repeat:
                Revert();
                return true;
            case LoopInputKind.ButtonDown or LoopInputKind.ButtonUp
                 or LoopInputKind.KeyDown or LoopInputKind.KeyUp or LoopInputKind.Text:
                return true;
        }
        return false;
    }

    public void Press(float x, float y)
    {
        if (bar.ButtonAt(x, y) is { } button) { bar.Pressed = button; return; }
        raisePress = sheet.Pane!.RaiseAt(x, y);
        if (raisePress >= 0) return;
        if (disciplines.Pane!.Covers(x, y)) discPress = disciplines.Pane.At(x, y);
    }

    public void Release(float x, float y)
    {
        if (bar.Pressed is { } button)
        {
            bar.Pressed = null;
            if (bar.ButtonAt(x, y) == button)
            {
                if (button == AdvancementBarPass.Done) Finish(); else Revert();
            }
            return;
        }
        if (raisePress >= 0)
        {
            if (advancement is not null && sheet.Pane!.RaiseAt(x, y) == raisePress)
            {
                bool bought = advancement.BuyStat(raisePress);
                Console.WriteLine($"  advance: {(ActorStat)raisePress} {(bought ? "raised" : "not raised")}, "
                                + $"{advancement.Subject.Actor?.SpendableExperience} left");
            }
            raisePress = -1;
            return;
        }
        if (discPress.Part != DisciplinePane.Part.None)
        {
            var dp = disciplines.Pane!;
            if (dp.At(x, y) == discPress) Click(dp, discPress.Part, discPress.Index);
            discPress = (DisciplinePane.Part.None, -1);
        }
    }

    /// <summary>
    /// **The rollovers**: 1.1's UI update runs a control's rollover
    /// in the front end while the advancement screen is open, and the status
    /// bar's boxes come up for it — so the raise buttons' prices, the discipline pane's descriptions and
    /// prices, the tabs' names and the bar's two hints show here as in the game (<see cref="AdvancementRollover"/>),
    /// written when the hover changes, or every frame under the Fix `liverollover`. Nothing of a world is hovered.
    /// </summary>
    void Hover()
    {
        if (advancement is not { } a) return;
        var (x, y) = pointer();
        rollover.NowMs = Environment.TickCount;
        rollover.Lines = LiveRollover.Keep(rollover.Lines,
            AdvancementRollover.Under(sheet.Pane!, disciplines.Pane!, a, powers, x, y, find));
        rollover.Control = AdvancementRollover.Hint(bar.Pane, x, y);
    }

    /// <summary>The description boxes' pass, which reads the hover first (the frame's UI update, then its draw).</summary>
    sealed class Hovering(CreateAdvancement owner) : IUiPass
    {
        public UiCanvas Ui => owner.rollover.Ui;

        public void Emit(int width, int height, FreeCamera camera, Simulation sim)
        {
            if (!owner.Up) return;
            owner.Hover();
            owner.rollover.Emit(width, height, camera, sim);
        }
    }

    /// <summary>The discipline pane's controls as the advancement uses them (the pane's handlers).</summary>
    void Click(DisciplinePane dp, DisciplinePane.Part part, int index)
    {
        switch (part)
        {
            case DisciplinePane.Part.TabLeft when dp.CanTabLeft: dp.ScrollTabs(-1); return;
            case DisciplinePane.Part.TabRight when dp.CanTabRight: dp.ScrollTabs(+1); return;
            case DisciplinePane.Part.RowUp when dp.CanScrollUp: dp.ScrollRows(dp.FirstRow - 1); return;
            case DisciplinePane.Part.RowDown when dp.CanScrollDown: dp.ScrollRows(dp.FirstRow + 1); return;
            case DisciplinePane.Part.Learn:
                if (advancement is null || dp.Rows()[index].Discipline is not { } learn) return;
                int first = dp.FirstRow;
                bool bought = advancement.BuyDiscipline(learn.Definition);
                Rebuild();
                dp.ScrollRows(first);
                Console.WriteLine($"  advance: {learn.Name} {(bought ? $"now level {learn.Level + 1}" : "not bought")}, "
                                + $"{advancement.Subject.Actor?.SpendableExperience} left");
                return;
        }
    }
}
