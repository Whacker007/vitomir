// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The two description boxes, drawn. What they say belongs to `Vitomir.Engine.Ui.Rollover`; this puts
// it on screen and runs the timing.
//
// `ui/descSmall.nui` is the name — 313 x 42 at 160,93, `morpheus15`, centred. `ui/descLargeSP.nui`
// is the detail block — 320 x 88 at 160,2, `defaultFont` — and it appears **1.5 seconds** into a
// hover and **only over an item**, which is the engine's mode 3 (the bar arms its timer at
// `now + 1500`; the hover picks mode 3 only for thing types 1, 5 and 6). `.nui` y counts up from the
// bottom, so the small box sits just above the status bar and the large one fills the hole in the
// middle of it — the same rectangle the party heads use.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the name of whatever the pointer is on, and — for an item — what it is.</summary>
public sealed class RolloverPass : IUiPass
{
    readonly UiCanvas ui;

    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    public NuiPane Pane { get; }
    public NuiPane Large { get; }
    public NfdFont Font { get; }
    public NfdFont LargeFont { get; }

    /// <summary>What the pointer is on, as the hover walk left it. Null hides both boxes.</summary>
    public WorldThing? Hovered { get; set; }

    /// <summary>The character being steered, whom an item's name asks whether it can be used.</summary>
    public WorldThing? User { get; set; }

    /// <summary>The string table, so a `DESCRIPTIONID` reads as words rather than as its key.</summary>
    public Strings? Text { get; set; }
    /// <summary>`misc/effects.ned`, which is where an item's effect lines get their numbers.</summary>
    public NedFile? Effects { get; set; }

    /// <summary>The clock the 1.5-second arming runs on. Whoever owns the frame keeps it fed.</summary>
    public int NowMs { get; set; }

    public bool Enabled { get; set; } = true;

    WorldThing? watching;
    int armedAt = int.MaxValue;

    public RolloverPass(UiCanvas ui)
    {
        this.ui = ui;
        Pane = ui.Assets.Pane(Rollover.SmallPane);
        Large = ui.Assets.Pane(Rollover.LargePane);
        Font = ui.Assets.Font(BoxOf(Pane)?.Get("font") is { Length: > 0 } small ? small : "morpheus15");
        LargeFont = ui.Assets.Font(BoxOf(Large)?.Get("font") is { Length: > 0 } big ? big : "defaultFont");
    }

    /// <summary>
    /// A pane's one textbox, **case-insensitively** — `NuiPane.Parse` lower-cases every token it
    /// reads, so an element is named `desctext` however the file spells it, and an ordinary `==`
    /// against the file's spelling quietly matches nothing.
    /// </summary>
    static NuiElement? BoxOf(NuiPane pane) =>
        pane.Elements.FirstOrDefault(e => e.Name.Equals(Rollover.Box, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The interface control under the pointer, when there is one. A control with a `hintstring`
    /// puts its hint in the small box instead of anything from the world, and never arms the large
    /// one.
    /// </summary>
    public NuiElement? Control { get; set; }

    /// <summary>
    /// Lines a pane has put in both boxes itself, up at once with no wait — the quest pane's rows
    /// (the status bar's mode 2). They win over everything else while they are set.
    /// </summary>
    public (RolloverLine Small, RolloverLine Large)? Custom { get; set; }

    /// <summary>
    /// Several lines a pane has put in the boxes itself — a discipline's is a name and,
    /// while advancing, a price in the small box, and four lines in the large one. <c>LargeNow</c>
    /// is the status bar's mode 2 (both at once, a pane's slot); false is mode 3 (the large box
    /// 1.5 s later, a status-bar slot). <c>Key</c> is what the hover is on, for that wait.
    /// </summary>
    public (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? Lines { get; set; }

    /// <summary>The line the small box shows, or null when there is nothing to show.</summary>
    public RolloverLine? Line =>
        !Enabled ? null : Lines is { Small.Count: > 0 } l ? l.Small[0]
        : Custom?.Small ?? Rollover.Hint(Control, Text) ?? WorldLines.Name;

    /// <summary>
    /// The world hover's lines as the hover writes them — when the hovered thing changes or is hurt —
    /// or every frame under the Fix <c>liverollover</c> (<see cref="Vitomir.Engine.Ui.WorldHover"/>).
    /// Earlier, they were rebuilt on every draw, which is the fix's behaviour, not 1.1's.
    /// </summary>
    (RolloverLine? Name, IReadOnlyList<RolloverLine> Detail) WorldLines =>
        world.For(Hovered, Text, User, Effects, (Fixes ?? Vitomir.Engine.World.Fixes.Current).LiveRollover);
    readonly Vitomir.Engine.Ui.WorldHover world = new();

    /// <summary>The switches this pass reads; null reads the game's (<c>Fixes.Current</c>).</summary>
    public Vitomir.Engine.World.Fixes? Fixes { get; set; }

    /// <summary>Every line the small box shows.</summary>
    public IReadOnlyList<RolloverLine> SmallLines =>
        Lines is { } l ? l.Small : Line is not { } one ? []
        : Custom is null && Rollover.Hint(Control, Text) is null && world.Owner is { } owner ? [one, owner]   // a prop's owner
        : [one];

    /// <summary>The lines the large box would show, once it is up.</summary>
    public IReadOnlyList<RolloverLine> Detail =>
        !Enabled ? [] : Lines is { } l ? l.Large : Custom is { } c ? [c.Large]
        : Rollover.Hint(Control, Text) is not null ? [] : WorldLines.Detail;

    object? watchingKey;

    /// <summary>
    /// **The bar's hide takes both boxes down at once**: the hover it was watching
    /// cleared, so the large box waits its 1.5 seconds again for whatever is hovered
    /// when the bar comes back. The boxes themselves go with <see cref="Enabled"/>, which the bar's state sets.
    /// </summary>
    public void TakeDown()
    {
        watching = null;
        watchingKey = null;
        armedAt = int.MaxValue;
    }

    /// <summary>
    /// Whether the large box has waited its 1.5 seconds. The engine arms it when the hover
    /// **changes**, not every frame, which is why this watches for a change rather than counting.
    /// </summary>
    public bool LargeUp
    {
        get
        {
            if (Custom is not null) return true;
            if (Lines is { } l)
            {
                if (l.LargeNow) return true;
                if (!ReferenceEquals(watchingKey, l.Key))
                {
                    watchingKey = l.Key;
                    armedAt = l.Large.Count > 0 ? NowMs + Rollover.LargeAfterMs : int.MaxValue;
                }
                return armedAt != int.MaxValue && NowMs >= armedAt;
            }
            watchingKey = null;
            if (!ReferenceEquals(watching, Hovered) || Control is not null)
            {
                watching = Hovered;
                armedAt = Detail.Count > 0 ? NowMs + Rollover.LargeAfterMs : int.MaxValue;
            }
            return armedAt != int.MaxValue && NowMs >= armedAt;
        }
    }

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (Line is not { } line || line.Text.Length == 0 || width <= 0 || height <= 0) return;
        bool big = LargeUp;                                  // asked before drawing, so the arming runs
        ui.Begin(width, height);

        Backdrop(Pane);
        if (BoxOf(Pane) is { } box && box.Size.Length > 1)
        {
            var small = SmallLines;
            if (small.Count <= 1)
            {
                var (tx, ty) = NuiPane.TextBoxOrigin(box, box.Size[0], box.Size[1],
                                                     UiCanvas.Measure(Font, line.Text), Font.LineHeight);
                ui.Text(Font, Pane.Pos[0] + box.Pos[0] + tx, Pane.Pos[1] + box.Pos[1] + ty,
                        line.Text, ReadPass.Colour(line.Colour));
            }
            else
                for (int i = 0; i < small.Count; i++)
                {
                    var (tx, ty) = NuiPane.TextBoxLine(box, box.Size[0], box.Size[1],
                                                       UiCanvas.Measure(Font, small[i].Text), Font.LineHeight, i, small.Count);
                    ui.Text(Font, Pane.Pos[0] + box.Pos[0] + tx, Pane.Pos[1] + box.Pos[1] + ty,
                            small[i].Text, ReadPass.Colour(small[i].Colour));
                }
        }

        if (big && Detail is { Count: > 0 } detail && BoxOf(Large) is { } panel && panel.Size.Length > 1)
        {
            Backdrop(Large);
            // The large box is `0x07`: every line centred across, the block centred down the box
            // (`NuiPane.TextBoxLine`). The rows are laid top to bottom in the
            // order the description gives them, `itemSpacing` apart — the order is matched to the
            // shipped game's own screenshot, the placement is read.
            int spacing = panel.Get("itemspacing") is { } s && int.TryParse(s, out int gap) ? gap : 2;
            // `0x07` has the word-wrap bit too: a line wider than the box is broken between words
            // as it is added. No item line is that wide; a quest's description is.
            var rows = detail.SelectMany(d => ConversationPane.Wrap(LargeFont, d.Text, panel.Size[0])
                                                              .Select(t => new RolloverLine(t, d.Colour))).ToList();
            for (int row = 0; row < rows.Count; row++)
            {
                var (tx, ty) = NuiPane.TextBoxLine(panel, panel.Size[0], panel.Size[1],
                                                   UiCanvas.Measure(LargeFont, rows[row].Text),
                                                   LargeFont.LineHeight, row, rows.Count, spacing);
                ui.Text(LargeFont, Large.Pos[0] + panel.Pos[0] + tx, Large.Pos[1] + panel.Pos[1] + ty,
                        rows[row].Text, ReadPass.Colour(rows[row].Colour));
            }
        }
    }

    void Backdrop(NuiPane pane)
    {
        float px = pane.Pos.Length > 1 ? pane.Pos[0] : 0, py = pane.Pos.Length > 1 ? pane.Pos[1] : 0;
        foreach (var q in pane.Quads) ui.Draw(q.Material, px + q.Pos[0], py + q.Pos[1], q.Size[0], q.Size[1]);
    }
}
