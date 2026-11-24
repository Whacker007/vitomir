// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text;
using Vitomir.Platform.Control;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Interface;

/// <summary>
/// What the Controls page sets: the bindings, how the mouse and the view behave, always run, and the first-person
/// view's head motion (per cent, 0 to 100) and field of view: vertical degrees (60 to 110), or across
/// (<see cref="FieldOfViewAxis"/> horizontal, 70 to 140), each kept on its own so switching between them does not
/// drift. (The free-pointer key's held-or-toggled setting gave way to the held and toggled actions.)
/// </summary>
public sealed record ControlValues(Dictionary<string, string[]> Bindings, float MouseSensitivity, bool InvertY, ThirdPersonFacing Facing,
                                   bool AlwaysRun = true, float HeadMotion = 50f, float FieldOfView = 80f,
                                   FieldOfViewAxis FieldOfViewAxis = FieldOfViewAxis.Vertical, float FieldOfViewHorizontal = FirstPersonView.DefaultHorizontalField,
                                   bool DoubleTapDodge = false, float DoubleTapSeconds = 0.25f, float SwingArcOpacity = 25f,
                                   bool MeleeFraming = false, float MeleeReticleOpacity = ReticleLook.DefaultMeleePercent, bool ShowCombatLog = true,
                                   LogCorner CombatLogCorner = LogCorner.TopRight, string AttackDirection = ControlValues.DefaultAttackDirection,
                                   float ActionSway = 4f, float ThirdPersonFollow = 0.08f)
{
    /// <summary>The swing's arc's opacity slider's ends (per cent; 0 hides it).</summary>
    public const float LeastSwingArc = 0f, MostSwingArc = 100f;

    /// <summary>
    /// The page's other ends — the crosshair in melee (per cent), the first-person action sway (cm; the setting
    /// <c>firstPersonActionSway</c>) and the third-person follow (s; <c>thirdPersonFollow</c>, 0 to 0.3).
    /// </summary>
    public const float LeastReticle = 0f, MostReticle = 100f, LeastSway = 0f, MostSway = 100f, LeastFollow = 0f, MostFollow = 0.3f;

    /// <summary>Ce-direction's attack directions, as the setting <c>attackDirection</c> names them, and as the page shows them.</summary>
    public static readonly IReadOnlyList<(string Setting, string Shown)> AttackDirections = [("automatic", "Automatic"), ("mouse", "Mouse"), ("keys", "Keys")];

    /// <summary>The default attack direction — the movement keys, as Mount &amp; Blade has it.</summary>
    public const string DefaultAttackDirection = "keys";

    /// <summary>The combat log's choices on the page: off, or shown in a corner.</summary>
    public static readonly IReadOnlyList<(LogCorner Corner, string Shown)> LogCorners =
        [(LogCorner.TopRight, "Top Right"), (LogCorner.TopLeft, "Top Left"), (LogCorner.BottomRight, "Bottom Right"), (LogCorner.BottomLeft, "Bottom Left")];

    /// <summary>Double tap's window's ends on the page (s).</summary>
    public const float LeastTapSeconds = 0.1f, MostTapSeconds = 0.6f;

    /// <summary>The head motion slider's ends (per cent) and the field of view's (degrees, vertical and across).</summary>
    public const float LeastHeadMotion = 0f, MostHeadMotion = 100f, LeastFieldOfView = 60f, MostFieldOfView = 110f,
                       LeastFieldAcross = FirstPersonView.LeastHorizontalField, MostFieldAcross = FirstPersonView.MostHorizontalField;

    /// <summary>A copy that shares nothing with this one (the page works on a copy until OK).</summary>
    public ControlValues Copy() => this with { Bindings = Bindings.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal) };
}

/// <summary>How the page ended, when it has.</summary>
public enum PageOutcome { Open, Accepted, Cancelled }

/// <summary>
/// The Controls page (ours, so that the player can remap
/// keys): every action with two key cells — each two-state action twice, held and toggled (the
/// toggled rows empty while unbound) —, the mouse's sensitivity, inverting the view, the first-person head motion and
/// field of view (vertical or across), always run on or off, and which way the body faces in third person;
/// Defaults, Cancel and OK; free look (held and toggled) and double tap to dodge with its window. the quick slots' keys in a group of their own under the other actions. It works on a copy of the
/// player's values: a click (or Enter) on a cell makes it wait for the next key, mouse button or wheel turn
/// (<see cref="Press"/>), which is bound there — and (keys shared) when another action already has it the page asks
/// whether to share it, move it (taken off the other, <see cref="BindingTable.Rebind"/>, which is lit and named in a line) or
/// cancel —; Escape while a cell waits clears that cell (<see cref="ClearWaiting"/>).
/// The host gives it the input while it waits and takes <see cref="Values"/> on OK. No SDL here: a key's name comes
/// from the host's <c>keyCode</c> and <c>keyName</c>.
/// </summary>
public sealed class ControlsPage
{
    /// <summary>The actions in the order the page lists them, with the words it shows (Title Case, 2a); an action not here follows, named from its identifier.</summary>
    public static readonly IReadOnlyList<(string Action, string Label)> Order =
    [
        ("forward", "Move Forward"), ("back", "Move Back"), ("left", "Move Left"), ("right", "Move Right"),
        ("jump", "Jump"), ("crouch", "Crouch"), ("crouchToggle", "Crouch (Toggle)"), ("cancel", "Cancel"),
        ("sprint", "Sprint"), ("walk", "Walk"), ("walkToggle", "Walk (Toggle)"),
        ("use", "Use"), ("attack", "Attack"), ("block", "Block"), ("dodge", "Dodge"),
        ("swapWeapon", "Swap Weapon"), ("aim", "Aim"),   // the sword and the bow; the bow raised undrawn (on Q, "Aim")
        ("aimToggle", "Aim (Toggle)"),   // aim mode latched (unbound)
        ("freeLook", "Free Look"), ("freeLookToggle", "Free Look (Toggle)"),
        ("orbitCamera", "Orbit Camera"),   // hold and move the mouse to swing the third-person camera round the player; double tap: back behind
        ("masquerade", "Masquerade Mode"), ("stealth", "Stealth"), ("reach", "Reach Highlight"), ("awareness", "Awareness Overlay"), ("swingTrail", "Swing Trail"), ("swingArc", "Swing Arc"),
        ("showImpact", "Bow Impact Point"), ("showPath", "Bow Arrow Path"),   // the bow's developer aids (unbound)
        ("combatLog", "Combat Log"), ("hud", "HUD"),
        ("switchView", "Switch View"), ("freePointer", "Free the Pointer"),   // no Swap Shoulder row (retired)
        ("freePointerToggle", "Free the Pointer (Toggle)"),
        ("pointerClick", "Click the Pointer"), ("screenshot", "Screenshot"), ("menu", "Menu"),
    ];

    /// <summary>
    /// The group <b>Quick Slots</b>: the eight slots' keys, listed under their own heading after the
    /// other actions (the list scrolls to them), in the same form — two cells each.
    /// </summary>
    public static readonly IReadOnlyList<(string Action, string Label)> QuickSlotRows =
    [
        ("slot1", "Quick Slot 1"), ("slot2", "Quick Slot 2"), ("slot3", "Quick Slot 3"), ("slot4", "Quick Slot 4"),
        ("slot5", "Quick Slot 5"), ("slot6", "Quick Slot 6"), ("slot7", "Quick Slot 7"), ("slot8", "Quick Slot 8"),
    ];

    /// <summary>
    /// Actions the page does not list: <c>switchMode</c>, click mode's, which the enhanced game does not have (DI);
    /// a file's key on it is kept and does nothing, and a key bound here elsewhere is still taken from it.
    /// <c>hitVolumes</c>, the developer's hit volumes overlay, bound with <c>--bind hitVolumes=&lt;key&gt;</c> (a file's key
    /// on it works): the page's rows are full at the design size.
    /// </summary>
    public static readonly IReadOnlyList<string> NotListed = ["switchMode", "hitVolumes"];

    const string IdleHint = "Click a cell, or choose it and press Enter, then press a key, a mouse button or turn the wheel. Escape while a cell waits clears it.";

    readonly Func<string, int?> keyCode;
    readonly Func<int, string> keyName;
    readonly ControlValues defaults;

    public ControlsPage(ScreenLoader loader, ControlValues current, ControlValues defaults, Func<string, int?> keyCode, Func<int, string> keyName,
                        string viewNote = "")
    {
        this.keyCode = keyCode;
        this.keyName = keyName;
        this.defaults = defaults.Copy();
        Values = current.Copy();
        Screen = loader.LoadEmbedded("controls");
        Screen.Data.Set("viewNote", viewNote);
        Refresh();
    }

    public Screen Screen { get; }

    /// <summary>The values as the page has them now (the player's own only after OK).</summary>
    public ControlValues Values { get; private set; }

    /// <summary>The cell waiting for a key: its action and 0 (primary) or 1 (secondary); null when none waits.</summary>
    public (string Action, int Slot)? Waiting { get; private set; }

    /// <summary>The action that last lost a key to another, lit until the next change; or null.</summary>
    public string? Lost { get; private set; }

    /// <summary>
    /// Keys shared: the question the page is asking, when a key pressed for a cell already works other actions —
    /// <b>Share</b> (both work it), <b>Move</b> (taken off the others, as earlier) or <b>Cancel</b> (nothing changes);
    /// null when none. Answered by <see cref="Answer"/> (the screen's <c>share</c>, <c>move</c> and <c>keepKey</c>).
    /// </summary>
    public (string Action, int Slot, InputSource Source, IReadOnlyList<string> Others)? Asking { get; private set; }

    public PageOutcome Outcome { get; private set; }

    /// <summary>The line under the page (what moved, or what was put back), or empty.</summary>
    public string Message { get; private set; } = "";

    public static string Label(string action)
    {
        foreach (var (a, label) in Order) if (a == action) return label;
        foreach (var (a, label) in QuickSlotRows) if (a == action) return label;
        // An action of a later batch's or a module's: its identifier in words, each with a capital.
        var sb = new StringBuilder();
        for (int i = 0; i < action.Length; i++)
        {
            char c = action[i];
            if (i == 0) sb.Append(char.ToUpperInvariant(c));
            else if (char.IsUpper(c)) sb.Append(' ').Append(c);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The actions listed, in the page's order: those in <see cref="Order"/> the bindings have, then the rest in the
    /// bindings' order (<see cref="MainActions"/>), then the quick slots' group (<see cref="QuickSlotActions"/>).
    /// </summary>
    public IEnumerable<string> Actions() => MainActions().Concat(QuickSlotActions());

    /// <summary>The actions of the first list (under Key Bindings): every listed action but the quick slots.</summary>
    public IEnumerable<string> MainActions()
    {
        foreach (var (a, _) in Order) if (Values.Bindings.ContainsKey(a)) yield return a;
        foreach (var a in Values.Bindings.Keys)
            if (!Order.Any(o => o.Action == a) && !QuickSlotRows.Any(o => o.Action == a) && !NotListed.Contains(a)) yield return a;
    }

    /// <summary>The quick slots' actions the bindings have, slot 1 first (the group under its heading).</summary>
    public IEnumerable<string> QuickSlotActions()
    {
        foreach (var (a, _) in QuickSlotRows) if (Values.Bindings.ContainsKey(a)) yield return a;
    }

    /// <summary>Answers one of the screen's actions; the page's outcome says whether it closed.</summary>
    public void Handle(UiAction a)
    {
        switch (a.Name)
        {
            case "cell":
                var dot = a.Argument.LastIndexOf('.');
                if (dot > 0 && int.TryParse(a.Argument[(dot + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) && slot is 0 or 1)
                    Wait(a.Argument[..dot], slot);
                break;
            case "changed":
                ReadBack();
                break;
            case "defaults":
                Waiting = null;
                Asking = null;
                Values = defaults.Copy();
                Lost = null;
                Message = "The default controls are back; OK keeps them, Cancel keeps your own.";
                Refresh();
                break;
            case "cancel":
                Outcome = PageOutcome.Cancelled;
                break;
            case "share":       // the question's answers
                Answer(true);
                break;
            case "move":
                Answer(false);
                break;
            case "keepKey":
                Answer(null);
                break;
            case "ok":
                ReadBack();
                // Streamer mode: the platform's own setting, not a control, put in force and kept as OK is pressed.
                if (Streamer != Vitomir.Platform.Presentation.StreamerMode.On) Vitomir.Platform.Presentation.StreamerMode.Set(Streamer);
                Outcome = PageOutcome.Accepted;
                break;
        }
    }

    /// <summary>The key cell of <paramref name="action"/>'s <paramref name="slot"/> as the screen was last laid out, or null.</summary>
    public KeyCell? Cell(string action, int slot)
    {
        var arg = string.Create(CultureInfo.InvariantCulture, $"{action}.{slot}");
        foreach (var p in Screen.Root.Walk())
            if (p is KeyCell k && k.Scope.Fill(k.Argument) == arg) return k;
        return null;
    }

    /// <summary>Makes a cell wait for the next key, mouse button or wheel turn.</summary>
    public void Wait(string action, int slot)
    {
        if (!Values.Bindings.ContainsKey(action)) return;
        Asking = null;      // a new cell chosen drops an unanswered question
        Waiting = (action, slot);
        Refresh();
    }

    /// <summary>
    /// The key, button or wheel turn the waiting cell was waiting for: bound in that cell (the other cell of the same
    /// action keeps its own). when the key already works other actions, the page asks — share it, move it here (taken
    /// off them, lit and named), or cancel (<see cref="Asking"/>, <see cref="Answer"/>); cancel and the freed pointer's click
    /// share a key with one other action without asking.
    /// </summary>
    public void Press(InputSource source)
    {
        if (Waiting is not { } w) return;
        Waiting = null;
        var name = BindingNames.Name(source, keyName);
        var others = BindingTable.Takers(Values.Bindings, w.Action, source, keyCode);
        if (others.Count > 0)
        {
            Asking = (w.Action, w.Slot, source, others);
            Lost = null;
            Message = $"{name} works {Labels(others)}. Share it (both work it), move it to {Label(w.Action)}, or cancel?";
            Refresh();
            return;
        }
        string? sharer = null;
        foreach (var a in Actions())
            if (a != w.Action && Values.Bindings[a].Any(n => BindingNames.Parse(n, keyCode) == source)) { sharer = a; break; }
        Bind(w.Action, w.Slot, source, share: false);
        Lost = null;
        Message = sharer is not null ? $"{name} works {Label(w.Action)} as well as {Label(sharer)}." : $"{name} works {Label(w.Action)}.";
        Refresh();
    }

    /// <summary>
    /// The answer to <see cref="Asking"/>: true shares the key (it works the cell's action and the others), false moves it
    /// (taken off the others, the first lit), null cancels (nothing changes). Escape while asking cancels.
    /// </summary>
    public void Answer(bool? share)
    {
        if (Asking is not { } q) return;
        Asking = null;
        var name = BindingNames.Name(q.Source, keyName);
        if (share is null)
        {
            Lost = null;
            Message = $"{name} was left as it was: it works {Labels(q.Others)}.";
        }
        else if (share.Value)
        {
            Bind(q.Action, q.Slot, q.Source, share: true);
            Lost = null;
            Message = $"{name} now works {Labels([.. q.Others, q.Action])}.";
        }
        else
        {
            Bind(q.Action, q.Slot, q.Source, share: false);
            Lost = q.Others[0];
            Message = $"{name} was taken from {Labels(q.Others)} and now works {Label(q.Action)}.";
        }
        Refresh();
    }

    /// <summary>Puts <paramref name="source"/> in <paramref name="action"/>'s cell <paramref name="slot"/>, shared or moved.</summary>
    void Bind(string action, int slot, InputSource source, bool share)
    {
        var names = Names(action).Where(n => BindingNames.Parse(n, keyCode) is { } s && s != source).ToList();
        int at = Math.Min(slot, names.Count);
        if (at < names.Count) names[at] = BindingNames.Name(source, keyName);
        else names.Add(BindingNames.Name(source, keyName));
        BindingTable.Rebind(Values.Bindings, action, names, keyCode, keyName, share);
    }

    /// <summary>Actions' labels in words: "Crouch", "Crouch and Cancel", "Jump, Use and Dodge".</summary>
    static string Labels(IReadOnlyList<string> actions) =>
        actions.Count <= 1 ? string.Join("", actions.Select(Label))
        : string.Join(", ", actions.Take(actions.Count - 1).Select(Label)) + " and " + Label(actions[^1]);

    /// <summary>Escape while a cell waits: the cell is cleared (an action's second key moves up when its first is cleared).</summary>
    public void ClearWaiting()
    {
        if (Waiting is not { } w) return;
        Waiting = null;
        var names = Names(w.Action);
        if (w.Slot < names.Count)
        {
            var gone = names[w.Slot];
            names.RemoveAt(w.Slot);
            names.RemoveAll(n => BindingNames.Parse(n, keyCode) is null);
            BindingTable.Rebind(Values.Bindings, w.Action, names, keyCode, keyName);
            Message = $"{Shown(gone)} no longer works {Label(w.Action)}.";
        }
        Lost = null;
        Refresh();
    }

    /// <summary>One list's rows (<paramref name="list"/>): an action's name and its two cells each.</summary>
    void Rows(ScreenData d, string list, List<string> actions)
    {
        var rows = d.Items(list);
        if (rows.Count != actions.Count)
        {
            d.Clear(list);
            foreach (var _ in actions) d.Add(list);
        }
        for (int i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            var names = Names(a);
            var row = rows[i];
            row.Set("action", a).Set("label", Label(a))
               .Set("key0", names.Count > 0 ? Shown(names[0]) : "")
               .Set("key1", names.Count > 1 ? Shown(names[1]) + (names.Count > 2 ? $" (+{names.Count - 2})" : "") : "")
               .Set("state0", Waiting is { } w0 && w0.Action == a && w0.Slot == 0 ? "waiting" : "")
               .Set("state1", Waiting is { } w1 && w1.Action == a && w1.Slot == 1 ? "waiting" : "")
               .Set("rowFill", Lost == a ? "lost" : "");
        }
    }

    /// <summary>A cell's words for a binding's name: as the platform names it (<c>Mouse2</c> shows as <c>Mouse Right</c>).</summary>
    string Shown(string name) => BindingNames.Parse(name, keyCode) is { } s ? BindingNames.Name(s, keyName) : name.Trim();

    List<string> Names(string action) =>
        Values.Bindings.TryGetValue(action, out var n) ? n.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList() : [];

    /// <summary>
    /// Streamer mode as its toggle stands (<see cref="Vitomir.Platform.Presentation.StreamerMode"/>): the platform's setting
    /// for every game, shown on this page as the Classic game shows it on its Fixes page; put from the setting as the page opens,
    /// left alone by Defaults (it is no control), and put in force and kept by OK.
    /// </summary>
    public bool Streamer { get; private set; } = Vitomir.Platform.Presentation.StreamerMode.On;

    /// <summary>Takes the slider's and the toggles' values from the screen's data.</summary>
    void ReadBack()
    {
        var d = Screen.Data;
        Streamer = d.Text("streamer") == "On";
        Values = Values with
        {
            MouseSensitivity = d.Number("sensitivity") is var s && s > 0f ? s : Values.MouseSensitivity,
            InvertY = d.Text("invert") == "On",
            AlwaysRun = d.Text("alwaysRun") != "Off",
            HeadMotion = MathF.Round(Math.Clamp(d.Number("headMotion"), ControlValues.LeastHeadMotion, ControlValues.MostHeadMotion)),
            FieldOfView = d.Number("fieldOfView") is var f && f > 0f ? MathF.Round(Math.Clamp(f, ControlValues.LeastFieldOfView, ControlValues.MostFieldOfView)) : Values.FieldOfView,
            // The field up the picture or across it, each kept on its own.
            FieldOfViewAxis = d.Text("fieldOfViewAxis") == "Horizontal" ? FieldOfViewAxis.Horizontal : FieldOfViewAxis.Vertical,
            FieldOfViewHorizontal = d.Number("fieldOfViewHorizontal") is var h && h > 0f
                ? MathF.Round(Math.Clamp(h, ControlValues.LeastFieldAcross, ControlValues.MostFieldAcross)) : Values.FieldOfViewHorizontal,
            Facing = d.Text("facing") == "View" ? ThirdPersonFacing.View : ThirdPersonFacing.Move,
            // Double tap to dodge and its window.
            DoubleTapDodge = d.Text("doubleTap") == "On",
            DoubleTapSeconds = d.Number("doubleTapSeconds") is var t && t > 0f
                ? MathF.Round(Math.Clamp(t, ControlValues.LeastTapSeconds, ControlValues.MostTapSeconds) * 100f) / 100f : Values.DoubleTapSeconds,
            // The swing's arc's opacity ("lighter or darker so I can tune it").
            SwingArcOpacity = MathF.Round(Math.Clamp(d.Number("swingArcOpacity"), ControlValues.LeastSwingArc, ControlValues.MostSwingArc)),
            // Melee framing (off by default), the crosshair in melee, the combat log and its
            // corner, the direction work's attack direction, and the camera work's two camera settings.
            MeleeFraming = d.Text("meleeFraming") == "On",
            MeleeReticleOpacity = MathF.Round(Math.Clamp(d.Number("meleeReticle"), ControlValues.LeastReticle, ControlValues.MostReticle)),
            ShowCombatLog = d.Text("combatLog") != "Off",
            CombatLogCorner = Corner(d.Text("combatLog")) ?? Values.CombatLogCorner,
            AttackDirection = Direction(d.Text("attackDirection")),
            ActionSway = MathF.Round(Math.Clamp(d.Number("actionSway"), ControlValues.LeastSway, ControlValues.MostSway)),
            ThirdPersonFollow = MathF.Round(Math.Clamp(d.Number("follow"), ControlValues.LeastFollow, ControlValues.MostFollow) * 100f) / 100f,
        };
        Refresh();
    }

    /// <summary>The corner a choice of the combat log's toggle names, or null for Off.</summary>
    static LogCorner? Corner(string shown)
    {
        foreach (var (corner, words) in ControlValues.LogCorners) if (words == shown) return corner;
        return null;
    }

    /// <summary>The attack direction's setting for the toggle's words (automatic when unknown).</summary>
    static string Direction(string shown)
    {
        foreach (var (setting, words) in ControlValues.AttackDirections) if (words == shown) return setting;
        return ControlValues.DefaultAttackDirection;   // the default, the keys
    }

    /// <summary>The toggle's words for an attack direction's setting (a setting the page does not know shows as Automatic).</summary>
    static string DirectionShown(string setting)
    {
        foreach (var (s, words) in ControlValues.AttackDirections) if (string.Equals(s, setting, StringComparison.OrdinalIgnoreCase)) return words;
        return DirectionShown(ControlValues.DefaultAttackDirection);   // the default, the keys
    }

    /// <summary>Puts the values into the screen's data: a row for each action, the mouse's and the view's settings, the lines.</summary>
    void Refresh()
    {
        var d = Screen.Data;
        Rows(d, "actions", MainActions().ToList());
        var slots = QuickSlotActions().ToList();
        Rows(d, "slotActions", slots);
        d.Set("hasQuickSlots", slots.Count > 0);   // the group's heading, shown when the bindings have the slots
        d.Set("sensitivity", Values.MouseSensitivity);
        d.Set("sensitivityText", (Values.MouseSensitivity * 1000f).ToString("0.0", CultureInfo.InvariantCulture));
        d.Set("headMotion", Values.HeadMotion);
        d.Set("headMotionText", Values.HeadMotion.ToString("0", CultureInfo.InvariantCulture) + " %");
        d.Set("fieldOfView", Values.FieldOfView);
        bool across = Values.FieldOfViewAxis == FieldOfViewAxis.Horizontal;
        d.Set("fieldOfViewHorizontal", Values.FieldOfViewHorizontal);
        d.Set("fieldOfViewAxis", across ? "Horizontal" : "Vertical");
        d.Set("fovVertical", !across);      // the slider shown: one per axis, each with its own ends and value
        d.Set("fovAcross", across);
        d.Set("fieldOfViewText", (across ? Values.FieldOfViewHorizontal : Values.FieldOfView).ToString("0", CultureInfo.InvariantCulture) + (across ? "° across" : "°"));
        d.Set("invert", Values.InvertY ? "On" : "Off");
        d.Set("streamer", Streamer ? "On" : "Off");
        d.Set("alwaysRun", Values.AlwaysRun ? "On" : "Off");
        d.Set("facing", Values.Facing == ThirdPersonFacing.View ? "View" : "Move");
        d.Set("doubleTap", Values.DoubleTapDodge ? "On" : "Off");
        d.Set("doubleTapSeconds", Values.DoubleTapSeconds);
        d.Set("doubleTapText", Values.DoubleTapSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s");
        d.Set("swingArcOpacity", Values.SwingArcOpacity);
        d.Set("swingArcText", Values.SwingArcOpacity <= 0f ? "Off" : Values.SwingArcOpacity.ToString("0", CultureInfo.InvariantCulture) + " %");
        //
        d.Set("meleeFraming", Values.MeleeFraming ? "On" : "Off");
        d.Set("meleeReticle", Values.MeleeReticleOpacity);
        d.Set("meleeReticleText", Values.MeleeReticleOpacity <= 0f ? "Hidden" : Values.MeleeReticleOpacity >= 100f ? "Full" : Values.MeleeReticleOpacity.ToString("0", CultureInfo.InvariantCulture) + " %");
        string logShown = "Off";
        if (Values.ShowCombatLog) foreach (var (corner, words) in ControlValues.LogCorners) if (corner == Values.CombatLogCorner) logShown = words;
        d.Set("combatLog", logShown);
        d.Set("attackDirection", DirectionShown(Values.AttackDirection));
        d.Set("actionSway", Values.ActionSway);
        d.Set("actionSwayText", Values.ActionSway.ToString("0", CultureInfo.InvariantCulture) + " cm");
        d.Set("follow", Values.ThirdPersonFollow);
        d.Set("followText", Values.ThirdPersonFollow <= 0f ? "Rigid" : Values.ThirdPersonFollow.ToString("0.00", CultureInfo.InvariantCulture) + " s");
        d.Set("asking", Asking is not null);
        d.Set("message", Message);
        d.Set("hint", Waiting is { } w
            ? $"Press the key, mouse button or wheel turn for {Label(w.Action)}. Escape clears this cell."
            : Asking is { } q
            ? $"Share: {Labels([.. q.Others, q.Action])} all work it. Move: {Label(q.Action)} alone. Cancel (or Escape): leave it as it was."
            : IdleHint);
    }
}
