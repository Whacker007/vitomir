// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The front end and the in-game menus: the main menu, the single-player menu, the credits, the
// escape menu, the save and load screens and the two little dialogs they use. Read out of
// the main menu, single player, credits,
// escape menu, load and save screens,
// dialogs and the main sequence's request.
//
// This is the interface half only. What a request *does* — start a new game, load one, write one,
// tear the game down for the main menu — belongs to whoever runs the game, and it is handed over
// through <see cref="FrontEnd.TakeRequest"/> once a frame, where the engine takes its own
// (at the end of the frame).
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>The main sequence's requests the menus make.</summary>
public enum MenuRequest
{
    None = 0,
    /// <summary>1: back to the main menu (the escape menu's Quit, and the end of the game).</summary>
    MainMenu = 1,
    /// <summary>2: the save screen.</summary>
    SaveMenu = 2,
    /// <summary>3: the load screen.</summary>
    LoadMenu = 3,
    /// <summary>8: a new game.</summary>
    NewGame = 8,
    /// <summary>9: load the slot in <see cref="FrontEnd.RequestSlot"/>.</summary>
    Restore = 9,
    /// <summary>10: write the slot in <see cref="FrontEnd.RequestSlot"/>.</summary>
    Save = 10,
    /// <summary>The main menu's Quit: the window is closed (`WM_CLOSE`), with no question asked.</summary>
    Quit = 100,
    /// <summary>
    /// Ours: bring 1.1's save in <see cref="FrontEnd.RequestSlot"/> (its `SaveGames\SP&lt;n&gt;`) across
    /// and play it — asked for from the load screen, after the warning.
    /// </summary>
    Import = 101,
}

/// <summary>The keys the menus' key hooks read: Escape and Enter, and M, which the map's also does (DIK_M).</summary>
public enum MenuKey { Escape, Enter, Map }

/// <summary>The save slots as the menus see them: slot 0 is the autosave, 1 to 29 the player's.</summary>
public interface ISaveSlots
{
    /// <summary>Whether the slot has a save in it (the folder test).</summary>
    bool Exists(int slot);
    /// <summary>The save's description, or null when it will not open.</summary>
    string? Description(int slot);
}

/// <summary>One entry of `credits.nls`, in the file's own order.</summary>
public sealed record CreditLine(string Label, string Text);

/// <summary>
/// The front end's state machine and every menu in it. Coordinates are the interface's own:
/// 640 x 480, counting up from the bottom left.
/// </summary>
public sealed class FrontEnd
{
    /// <summary>
    /// The main sequence's states this cares about: 2 the front end, 3 the game,
    /// and 4 the game not drawn — what the Options screen sets while it is up in a game.
    /// </summary>
    public const int StateFrontEnd = 2, StateGame = 3, StateOptions = 4;

    /// <summary>The panes, by file (`ui/*.nui`).</summary>
    public const string MainMenuFile = "MainMenu", SpFile = "sp", CreditsPaneFile = "Credits", EscapeFile = "escapeMenu",
                        LoadFile = "spLoad", SaveFile = "spSave", MessageBoxFile = "messageBox", InputBoxFile = "inputBox",
                        LoadScreenFile = "LoadScreen", TitleFile = "TitleScreen";

    /// <summary>How many save slots there are (`SAVE_MAXSPSLOTS`), the autosave included.</summary>
    public const int Slots = 30;
    /// <summary>The name box's limit (0x78); what is kept of it is one fewer.</summary>
    public const int NameLimit = 120;
    /// <summary>What a slot's row says when its save will not open.</summary>
    public const string Unreadable = "ERROR: Could not open main save file!";
    /// <summary>Ours: the head of a load-list row that is 1.1's own save, and the warning before it is brought across.</summary>
    public const string OriginalLabel = "Original Game (1.1) Save, Slot", OriginalAutosave = "Original Game (1.1) Autosave",
                        ImportWarning = "This save was made by the original game, Vampire 1.1. It will be imported into " +
                                        "Vitomir and played from there; the original save is not changed.  OK to continue?";
    /// <summary>
    /// What the main menu announces. 1.1's line is the string table's <c>UI_VERSION</c> and its own
    /// number — "Version 1.1" — and Vitomir once showed exactly that. It changed because
    /// *"it's accurate enough … to the point now where I can legitimately get confused if I'm
    /// playing our engine or the base game."* So the line names this engine and its build instead:
    /// "Vitomir" and the build's release version (<see cref="Vitomir.Platform.ReleaseVersion"/>). A difference by request,
    /// not a fix. This is the engine's own line; the main menu shows the running program's
    /// (<see cref="Net.NetIdentity.ProgramName"/>: the platform program's own version line under it).
    /// </summary>
    public static readonly string Version = Vitomir.Platform.ReleaseVersion.Line("Vitomir");

    readonly Func<string, NuiPane> panes;
    readonly Func<string, NfdFont> fonts;
    readonly Func<string, string?> find;
    readonly Dictionary<string, MenuPane> loaded = new(StringComparer.OrdinalIgnoreCase);
    readonly List<List<MenuPane>> layers = [[]];
    readonly List<MenuPane> onTop = [];
    /// <summary>The first list's layers, bottom first, and the on-top list, for a reader of which panes are up.</summary>
    public IReadOnlyList<IReadOnlyList<MenuPane>> Layers => layers;
    public IReadOnlyList<MenuPane> OnTop => onTop;

    public FrontEnd(Func<string, NuiPane> panes, Func<string, NfdFont> fonts, Func<string, string?> strings)
    {
        this.panes = panes;
        this.fonts = fonts;
        find = strings;
    }

    // ---- what the host supplies ----------------------------------------------------------------

    /// <summary>The save slots, for the lists and the two overwrite warnings.</summary>
    public ISaveSlots? SaveSlots { get; set; }
    /// <summary>
    /// Ours: 1.1's own saves in the install (<c>Save.OriginalSaves</c>), by slot, with the description
    /// each carries — offered at the foot of the load screen, to be imported (<see cref="MenuRequest.Import"/>).
    /// </summary>
    public Func<IReadOnlyList<(int Slot, string Description)>>? OriginalSaves { get; set; }
    /// <summary>Where the load list's rows for 1.1's saves start numbering (ours): row data is this plus the slot.</summary>
    public const int OriginalRow = 1000;
    /// <summary>An interface sound (`ui.nag`'s slot).</summary>
    public Action<int>? Sound { get; set; }

    /// <summary>
    /// **The menus' trail** (ours, <c>Diagnostics.MenuTrail</c>): each step through the menus, said in a few words —
    /// <c>show</c> and <c>hide</c> a screen, <c>click</c> a button, <c>check</c> a box, <c>pick</c> a row, <c>bar</c> a
    /// step of a bar — for a recording's digest to keep and a replay's check to compare.
    /// </summary>
    public Action<string>? Trail { get; set; }
    /// <summary>
    /// A pane's music: the named track, when it is not already the one playing. The
    /// host plays it through the game's own music channel.
    /// </summary>
    public Action<string>? Music { get; set; }
    /// <summary>`credits.nls` in its file order (<see cref="ParseCredits"/>).</summary>
    public Func<IReadOnlyList<CreditLine>>? Credits { get; set; }
    /// <summary>
    /// What the name box is filled with: "the location (hh:mm:ss)" (`%s (%2.2d:…)`).
    /// </summary>
    public Func<string>? Describe { get; set; }
    /// <summary>The game's own panes closed before the save or load screen opens.</summary>
    public Action? CloseGamePanes { get; set; }
    /// <summary>The escape menu's show hook: every screen effect in progress cancelled.</summary>
    public Action? EscapeShown { get; set; }
    /// <summary>A line for the status bar's message log — "Game saved.".</summary>
    public event Action<string>? Posted;
    /// <summary>A button whose screen is not written (Options, Multiplayer), by name.</summary>
    public event Action<string>? NotWritten;
    /// <summary>
    /// The main menu's Multiplayer (request 11 → `mpSelect`), when somebody has written it;
    /// without it the button says it is not written.
    /// </summary>
    public Action? Multiplayer { get; set; }
    /// <summary>
    /// The panes the front end does not know — multiplayer's, the Storyteller's — get their button and
    /// check-box releases here first; true when the click was theirs.
    /// </summary>
    public Func<MenuPane, NuiElement, bool>? OtherClick { get; set; }
    /// <summary>The same for the two keys.</summary>
    public Func<MenuPane, MenuKey, bool>? OtherKey { get; set; }
    /// <summary>Which escape menu the game opens: `escapeMenu`, or `escapeMenuMP` in a network game.</summary>
    public string EscapeMenu { get; set; } = EscapeFile;

    // ---- state ---------------------------------------------------------------------------------

    /// <summary><see cref="StateFrontEnd"/> or <see cref="StateGame"/>.</summary>
    public int State { get; set; } = StateFrontEnd;
    /// <summary>Single player: only then do the menus pause the game.</summary>
    public bool SinglePlayer { get; set; } = true;
    /// <summary>The game pause: the game clock stops while it is set.</summary>
    public bool Paused { get; set; }
    /// <summary>The interface clock the fades and the credits run on, in ms.</summary>
    public int NowMs { get; private set; }
    /// <summary>The floating texts: the version line and the credits.</summary>
    public Captions Floating { get; } = new();

    /// <summary>
    /// The tip to show by the pointer (ours, <see cref="MenuTips"/>): a tipped control rested on for
    /// <see cref="MenuTips.DelayMs"/> with no button held, its words and where the pointer is; else null.
    /// </summary>
    public (string Text, float X, float Y)? Tip =>
        tipText is { } text && tipOn is not null && NowMs - tipSince >= MenuTips.DelayMs ? (text, PointerX, PointerY) : null;

    string? tipOn, tipText;
    int tipSince;

    public float PointerX { get; private set; }
    public float PointerY { get; private set; }
    /// <summary>Whether the left button is down, which the button update reads.</summary>
    public bool LeftHeld { get; set; }

    /// <summary>The request waiting for the end of the frame, and the slot it is for.</summary>
    public MenuRequest Request { get; private set; }
    public int RequestSlot { get; private set; }
    /// <summary>What the player named the save; empty for the automatic description.</summary>
    public string SaveName { get; private set; } = "";
    /// <summary>Whether the main menu is to open with the credits already rolling.</summary>
    public bool CreditsOnMainMenu { get; private set; }

    // ---- panes and layers ----------------------------------------------------------------------

    /// <summary>
    /// **Our lettering on 1.1's screens** (ours, <see cref="MenuLettering"/>): what puts a pane in it, given by the
    /// host — <c>(pane, file) =&gt; MenuLettering.Dress(assets, pane, file)</c> — when the player has it (the default); null
    /// for 1.1's own pictured words. A pane is dressed once, on a copy, as it is first built.
    /// </summary>
    public Func<NuiPane, string, NuiPane>? Dress { get; set; }

    /// <summary>Whether 1.1's menus wear our lettering (<see cref="Dress"/> given).</summary>
    public bool Lettered => Dress is not null;

    NuiPane Dressed(NuiPane nui, string file) =>
        Dress is { } dress && MenuLettering.Dresses(file) ? dress(nui.Copy(), file) : nui;

    /// <summary>A pane, loaded the first time it is asked for (every menu keeps its one copy).</summary>
    public MenuPane Pane(string file)
    {
        if (loaded.TryGetValue(file, out var p)) return p;
        // Our controls on 1.1's panes that a host's menus always have: the create screen's most players.
        p = new MenuPane(MostPlayersOption.Add(Dressed(panes(file), file), file), file, fonts);
        loaded[file] = p;
        if (Options is not null) OptionsMenu.AddOurButtons(p);
        return p;
    }

    /// <summary>
    /// A pane of Vitomir's own, laid out in code rather than read from the install (ours: the Fixes
    /// page and the key bindings), built the first time it is asked for and kept like any other.
    /// </summary>
    public MenuPane Pane(string file, Func<NuiPane> build)
    {
        if (loaded.TryGetValue(file, out var p)) return p;
        p = new MenuPane(Dressed(build(), file), file, fonts);
        loaded[file] = p;
        return p;
    }

    /// <summary>The Options screen and Vitomir's pages off it, when the host has given it one.</summary>
    public OptionsMenu? Options { get; set; }
    /// <summary>The map, when the host has given it one (a game does).</summary>
    public MapScreen? Map { get; set; }
    /// <summary>The developer console, when the host has given it one (a game with the diagnostics on does).</summary>
    public DevConsole? DevConsole { get; set; }

    /// <summary>Show: on the on-top list or the top layer, its music started, its key hook held off until drawn.</summary>
    public void Show(MenuPane pane)
    {
        if (pane.Shown) return;
        (pane.OnTopList ? onTop : layers[^1]).Add(pane);
        pane.Shown = true;
        Trail?.Invoke($"show {pane.File}");
        pane.JustShown = true;
        // The keyboard goes to the first control in the focus ring: an edit field or a `key` control (NuiKeys).
        pane.Focus = NuiKeys.Ring(pane.Nui).FirstOrDefault()?.Name;
        if (ReferenceEquals(pane, loaded.GetValueOrDefault(EscapeMenu))) EscapeShown?.Invoke();
        if (pane.Nui.Music.Length > 0) Music?.Invoke(pane.Nui.Music);
    }

    /// <summary>Hide.</summary>
    public void Hide(MenuPane pane)
    {
        if (!pane.Shown) return;
        onTop.Remove(pane);
        foreach (var layer in layers) layer.Remove(pane);
        pane.Shown = false;
        Trail?.Invoke($"hide {pane.File}");
        pane.JustShown = false;
    }

    /// <summary>Whether a pane is up: on the top layer or the on-top list.</summary>
    public bool IsUp(MenuPane pane) => pane.Shown && (onTop.Contains(pane) || layers[^1].Contains(pane));

    /// <summary>A new, empty top layer: what was showing stays shown but is not drawn.</summary>
    public void PushLayer() => layers.Add([]);

    /// <summary>
    /// The top layer taken down, and the music of the first pane in the layer it uncovers
    /// that names one started — which is how the main menu's theme comes back after the credits.
    /// </summary>
    public void PopLayer()
    {
        if (layers.Count == 0) return;
        foreach (var pane in layers[^1].ToList()) Hide(pane);
        if (layers.Count > 1) layers.RemoveAt(layers.Count - 1);
        if (layers[^1].FirstOrDefault(p => p.Nui.Music.Length > 0) is { } music) Music?.Invoke(music.Nui.Music);
    }

    /// <summary>Every layer cleared.</summary>
    public void ClearLayers()
    {
        foreach (var layer in layers)
            foreach (var pane in layer.ToList()) Hide(pane);
        layers.Clear();
        layers.Add([]);
    }

    /// <summary>What is drawn, bottom to top: the top layer, then the on-top list.</summary>
    public IEnumerable<MenuPane> Visible() => layers[^1].Concat(onTop);

    /// <summary>Whether any of the menus is up at all — while one is, it has the keyboard and the mouse.</summary>
    public bool Up => onTop.Count > 0 || layers.Any(l => l.Count > 0);

    /// <summary>
    /// Whether **a screen that is the whole sheet** is among what is drawn (ours, <see cref="MenuPane.WholeSheet"/>):
    /// the Options screen or a page of it, the save or the load screen, the map — with or without a message box over it —
    /// and not the escape menu, a box or a plaque by itself. It hides the world behind it, so a game's frame seats a sheet
    /// shorter than the picture in the middle while it is up (<see cref="UiLayout.InGame"/>).
    /// </summary>
    public bool WholeSheet => Visible().Any(p => p.WholeSheet);

    /// <summary>
    /// Whether **a dialog whose art runs up to the sheet's top edge** is drawn (ours,
    /// <see cref="MenuPane.AgainstTop"/>): the escape menu, the network game's, the console. While one is up on a sheet
    /// seated at the bottom of a taller picture, the rows above the sheet are black (<see cref="MenuPass.TopBand"/>), so
    /// the cut at the top of its art lands on a black edge, as on 1.1's screen.
    /// </summary>
    public bool DialogAgainstTop => Visible().Any(p => p.Dialog && p.AgainstTop);

    /// <summary>
    /// The pane that takes the keys and the clicks: the newest dialog on the on-top list,
    /// else an on-top pane under the pointer, else the newest dialog in the top layer, else a pane of
    /// the top layer under the pointer.
    /// </summary>
    public MenuPane? Active()
    {
        for (int i = onTop.Count - 1; i >= 0; i--) if (onTop[i].Dialog) return onTop[i];
        for (int i = onTop.Count - 1; i >= 0; i--) if (onTop[i].Covers(PointerX, PointerY)) return onTop[i];
        var top = layers[^1];
        for (int i = top.Count - 1; i >= 0; i--) if (top[i].Dialog) return top[i];
        for (int i = top.Count - 1; i >= 0; i--) if (top[i].Covers(PointerX, PointerY)) return top[i];
        return null;
    }

    // ---- the screens ---------------------------------------------------------------------------

    /// <summary>
    /// The main menu: every layer cleared, the menu shown with its theme, and the
    /// version line floated in centred near the bottom for ten seconds.
    /// </summary>
    public void OpenMainMenu()
    {
        State = StateFrontEnd;
        ClearLayers();
        Show(Pane(MainMenuFile));
        var font = fonts(VersionFont);
        string line = InFont(font, Vitomir.Platform.ReleaseVersion.Shown(Net.NetIdentity.ProgramName));   // and a package's notice
        VersionCaption = new Caption(line, VersionFont, (640 - font.Measure(line)) / 2, 5, NowMs, 10000, 2000, 2000, Lines: 1);
        Floating.Add(VersionCaption);
    }

    /// <summary>
    /// A line put in what a font of 1.1's has (its 256 glyphs, by the low byte): the dashes as Windows-1252 has them,
    /// and any character the font draws nothing for, or that is past its 256, as a hyphen.
    /// </summary>
    public static string InFont(Vitomir.Formats.NfdFont font, string line) => string.Concat(line.Select(c =>
    {
        char k = c switch { '\u2013' => (char)0x96, '\u2014' => (char)0x97, _ => c };
        return k <= 0xff && (k == ' ' || font[k].Width > 0) ? k : '-';
    }));

    /// <summary>The main menu's version line while it is up (<see cref="OpenMainMenu"/>).</summary>
    public Caption? VersionCaption { get; private set; }

    /// <summary>
    /// Whether a floating caption is drawn now. The version line only while the main menu is up on the top
    /// layer (where 1.1's own sits): ours — our line is longer than 1.1's "Version 1.1", and a screen opened
    /// over the main menu within its ten seconds (the Options screens) had it over its bottom buttons.
    /// </summary>
    public bool CaptionDrawn(Caption caption) =>
        !ReferenceEquals(caption, VersionCaption) || loaded.GetValueOrDefault(MainMenuFile) is { } main && IsUp(main);

    /// <summary>The default floating font (slot 2): `DefaultFont15`.</summary>
    public const string VersionFont = "DefaultFont15";

    /// <summary>The single-player menu, in the layer the main menu left.</summary>
    public void OpenSp() => Show(Pane(SpFile));

    /// <summary>
    /// The escape menu: the game paused in single player and the menu up. Whether it may
    /// open at all — the camera, the shop, the conversation — is the game's to decide.
    /// </summary>
    public void OpenEscape()
    {
        var menu = Pane(EscapeMenu);
        if (SinglePlayer) Paused = true;
        Show(menu);
    }

    /// <summary>The message box line the Pause key posts on pausing: "Game paused."</summary>
    public const string PausedLine = "UI_STSBPAUSE";
    /// <summary>The message box line the Pause key posts on carrying on: "Game resumed."</summary>
    public const string ResumedLine = "UI_STSBUNPAUSE";

    /// <summary>
    /// **The Pause key**: the key's own DirectInput code, `DIK_PAUSE` (0xC5 —
    /// the key's down-stamp), pressed this frame, and in single player
    /// only. The game pause flips: unset it is set and
    /// <see cref="PausedLine"/> goes to the message box; set, it is
    /// cleared and <see cref="ResumedLine"/> goes. It is the
    /// same flag the escape menu, the save and load screens and the advancement screen set, so
    /// closing any of them carries on a game the key had paused, and the key carries on a game they
    /// left paused. Returns the line's string name, or null in a network game, where the key does
    /// nothing (the host's pause is the Storyteller's button). Whether the key is read at all — the
    /// game camera, no dialog up, no vault, advancement, chat line or shop (the head of
    /// the key handling) — is the caller's.
    /// </summary>
    public string? PauseKey()
    {
        if (!SinglePlayer) return null;
        Paused = !Paused;
        return Paused ? PausedLine : ResumedLine;
    }

    /// <summary>
    /// The escape menu's own first step for every button, and its Escape key: unpaused and put away,
    /// only while it is up and on top.
    /// </summary>
    public void PutEscapeAway()
    {
        var menu = Pane(EscapeMenu);
        if (!IsUp(menu)) return;
        if (SinglePlayer) Paused = false;
        Hide(menu);
    }

    /// <summary>The load screen: the saves there are, the first chosen, the game paused.</summary>
    public void OpenLoad()
    {
        var menu = Pane(LoadFile);
        CloseGamePanes?.Invoke();
        var list = menu.Lists["spLoadList"];
        list.Clear();
        for (int slot = 0; slot < Slots; slot++)
        {
            if (SaveSlots?.Exists(slot) != true) continue;
            string head = slot == 0 ? Text("UI_SPLOADSAVE_AUTOSAVE") : SlotName(slot);
            list.Add($"{head}\n{SaveSlots.Description(slot) ?? Unreadable}", slot);
        }
        // Then 1.1's own, plainly marked as 1.1's (ours: 1.1 lists only its own slots).
        foreach (var (slot, description) in OriginalSaves?.Invoke() ?? [])
            list.Add($"{(slot == 0 ? OriginalAutosave : $"{OriginalLabel} {slot}")}\n{description}", OriginalRow + slot);
        list.Select(0);
        if (SinglePlayer) Paused = true;
        PushLayer();
        Show(menu);
    }

    /// <summary>The save screen: every slot but the autosave, slot 1 chosen, the game paused.</summary>
    public void OpenSave()
    {
        var menu = Pane(SaveFile);
        if (IsUp(menu)) return;
        CloseGamePanes?.Invoke();
        var list = menu.Lists["spSaveList"];
        list.Clear();
        for (int slot = 1; slot < Slots; slot++)
        {
            string said = SaveSlots?.Exists(slot) == true
                ? SaveSlots.Description(slot) ?? Unreadable
                : Text("UI_SPLOADSAVE_UNUSEDSLOT");
            list.Add($"{SlotName(slot)}\n{said}", slot);
        }
        list.Select(0);
        if (SinglePlayer) Paused = true;
        PushLayer();
        Show(menu);
    }

    /// <summary>"Slot %d" through the string table's own formatter.</summary>
    string SlotName(int slot) => Strings.Format(find, "UI_SPLOADSAVE_SLOT", $"%d{slot}");

    string Text(string key) => find(key) ?? key;

    /// <summary>
    /// The message box (built from ui/messageBox.nui): a title, the text, OK
    /// and Cancel as asked. Only one at a time — a second is refused. OK is 1 and Cancel 2, and the
    /// callback hears it before the box goes.
    /// </summary>
    public bool Message(string title, string text, bool ok, bool cancel, Action<int>? then = null)
    {
        var box = Pane(MessageBoxFile);
        if (box.Shown) return false;
        box.Texts["mbTitle"] = title;
        box.Texts["mbTextBox"] = text;
        box.Hidden.Clear();
        if (!ok) box.Hidden.Add("mbOkButton");
        if (!cancel) box.Hidden.Add("mbCancelButton");
        foreach (var b in box.Buttons.Keys.ToList()) box.Buttons[b] = ButtonState.Normal;
        messageThen = then;
        Show(box);
        return true;
    }

    Action<int>? messageThen;
    Action<int, string>? inputThen;

    /// <summary>
    /// The input box (whose arguments are the title, OK, Cancel and the
    /// callback): a title and an edit field with the keyboard. OK hides the box, then the callback
    /// hears 1 and the text; Cancel, 2. A button not asked for is hidden, and then its
    /// key does nothing either. The field takes anything until the caller says otherwise
    /// (<paramref name="accepts"/> — the box is built afresh each time, so 0 from the
    /// field's constructor), and shows what is typed as itself unless it is a
    /// <paramref name="password"/> (the edit's star bit, which the join's password box sets), or an
    /// <paramref name="address"/> (ours, dots while streamer mode is on).
    /// </summary>
    public bool Input(string title, string text, int limit, Action<int, string>? then = null,
                      bool ok = true, bool cancel = true, EditAccepts accepts = EditAccepts.Any, bool password = false, bool address = false)
    {
        var box = Pane(InputBoxFile);
        if (box.Shown) return false;
        box.Texts["ibTitle"] = title;
        var edit = box.Edits["ibEdit"];
        edit.Accepts = accepts;
        edit.Password = password;
        edit.Address = address;                     // dots on a stream (the join screen's address box; streamer mode)
        edit.Limit(limit);
        edit.Set(text);
        box.Hidden.Clear();
        if (!ok) box.Hidden.Add(InputOk);
        if (!cancel) box.Hidden.Add(InputCancel);
        foreach (var b in box.Buttons.Keys.ToList()) box.Buttons[b] = ButtonState.Normal;
        inputThen = then;
        Show(box);
        return true;
    }

    /// <summary>The input box's two buttons (found by these names).</summary>
    public const string InputOk = "ibOkButton", InputCancel = "ibCancelButton";

    // ---- the credits ---------------------------------------------------------------------------

    IReadOnlyList<CreditLine> creditLines = [];
    int creditIndex, creditT0;

    /// <summary>
    /// The credits: the floating texts cleared — the version line with them — a layer
    /// pushed, the pane up with its own music, and the lines read afresh.
    /// </summary>
    public void OpenCredits()
    {
        Floating.Clear();
        PushLayer();
        Show(Pane(CreditsPaneFile));
        creditLines = Credits?.Invoke() ?? [];
        creditIndex = 0;
        creditT0 = NowMs;
    }

    /// <summary>Closing (all three boxes alike): the lines gone, the pane gone, the layer popped.</summary>
    void CloseCredits()
    {
        Floating.Clear();
        PopLayer();
    }

    /// <summary>The fonts the credits use: `morpheus20` for a label with TITLE in it, `morpheus15` otherwise.</summary>
    public const string CreditsTitleFont = "morpheus20", CreditsFont = "morpheus15";

    /// <summary>
    /// One frame of the credits, at most one line a frame: a line every 1.2 s, a `&lt;&gt;`
    /// adding 0.3 s of gap instead of a line, each line centred, stood at y = 40 (plus whatever the
    /// frame was late by) and rising at 20 pixels a second for the rest of its 20 seconds; and 21
    /// seconds after the last, the credits close themselves.
    /// </summary>
    void CreditsFrame()
    {
        if (creditIndex >= creditLines.Count)
        {
            if (NowMs > creditT0 + 21000) CloseCredits();
            return;
        }
        if (NowMs <= creditT0 + 1200) return;
        var line = creditLines[creditIndex];
        int t0 = NowMs;
        if (line.Text.StartsWith("<>", StringComparison.Ordinal)) t0 = NowMs + 300;
        else
        {
            string font = line.Label.Contains("TITLE", StringComparison.Ordinal) ? CreditsTitleFont : CreditsFont;
            int late = NowMs - creditT0;
            if (late < 20000)
            {
                int y = (int)((late - 1200) * 0.02 + 40.0);
                int x = (640 - fonts(font).Measure(line.Text)) / 2;
                // In our lettering the credits are drawn in our faces, centred by their own measure (Caption.Ours).
                bool ours = loaded.TryGetValue(CreditsPaneFile, out var creditsPane) && creditsPane.Lettered;
                Floating.Add(new Caption(line.Text, font, x, y, NowMs, 20000 - late, 2000, 2000, Lines: 1, Rise: 20f, Ours: ours));
            }
        }
        creditIndex++;
        creditT0 = t0;
    }

    /// <summary>
    /// `credits.nls` read the way the credits read it, not the way the string table
    /// does: in file order, keys repeating as they like, at most 512 entries — comments and blank
    /// lines skipped, a label then white space then the text — and **an entry with no text ends it**.
    /// </summary>
    public static List<CreditLine> ParseCredits(string file)
    {
        var lines = new List<CreditLine>();
        foreach (var raw in file.Replace("\r\n", "\n").Split('\n'))
        {
            if (lines.Count >= 0x200) break;
            string line = raw.TrimEnd('\r');
            string trimmed = line.TrimStart(' ', '\t');
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
            int cut = 0;
            while (cut < trimmed.Length && trimmed[cut] is not (' ' or '\t')) cut++;
            string label = trimmed[..cut];
            if (label.Length > 31) label = label[..31];
            string text = trimmed[cut..].TrimStart(' ', '\t');
            if (text.Length > 0x80) text = text[..0x80];
            if (text.Length == 0) break;
            lines.Add(new CreditLine(label, text));
        }
        return lines;
    }

    // ---- input ---------------------------------------------------------------------------------

    MenuPane? pressedPane;
    string? pressedControl;
    bool draggingSlider;
    float sliderGrab;

    /// <summary>The pointer, in the interface's space.</summary>
    public void PointerAt(float x, float y)
    {
        PointerX = x;
        PointerY = y;
    }

    NuiElement? ControlUnderPointer(MenuPane? pane) => pane?.ControlAt(PointerX, PointerY);

    /// <summary>
    /// The left button going down (event 0): the control under the pointer in the active pane is
    /// pressed — a button goes to its held state, a list row is chosen, a scroll bar pages, a slider
    /// is caught — and the pane's sound hook plays the press.
    /// </summary>
    public void Press()
    {
        var pane = Active();
        var control = ControlUnderPointer(pane);
        pressedPane = pane;
        pressedControl = control?.Name;
        if (pane is null || control is null) return;
        if (control.Type is "button" or "checkbox" or "textlistbox" or "scrollbar" or "scrollslider")
            Sound?.Invoke(Audio.FoleyEvent.ButtonDepress);
        if (pane.File == FixesPage.File && Options?.Fixes.Press(control, PointerY - pane.Y) == true) return;   // its scroll bar
        if (pane.File == KeysPage.File && Options?.Keys.Press(control, PointerY - pane.Y) == true) return;     // and the key page's
        switch (control.Type)
        {
            case "button":
                if (pane.Buttons.TryGetValue(control.Name, out var state) && state != ButtonState.Greyed)
                    pane.Buttons[control.Name] = ButtonState.Pressed;
                break;
            case "textlistbox":
                pane.Lists[control.Name].Press();
                Trail?.Invoke($"pick {pane.File} {control.Name} {pane.Lists[control.Name].Current}");
                if (Options?.Owns(pane) == true) Options.Picked(pane, control.Name);
                break;
            case "scrollbar" when ListOf(pane, control) is { } paged && SliderOf(pane, control) is { } slider:
                // Above the slider pages toward the top, below it toward the bottom.
                float at = pane.Y + paged.SliderY(control, slider);
                paged.Page(PointerY > at ? -1 : +1);
                break;
            case "scrollslider" when BarOf(pane, control) is { } bar && ListOf(pane, bar) is { } dragged:
                // Caught only when the list overflows.
                if (dragged.Rows.Count > 0 && dragged.Rows[^1].Y < 0)
                {
                    draggingSlider = true;
                    sliderGrab = PointerY - (pane.Y + dragged.SliderY(bar, control));
                }
                break;
            case "edit":
                pane.Focus = control.Name;
                break;
            case "checkbox" when !pane.Locked.Contains(control.Name):
                // A checkbox acts on the press: toggled, or checked in its radio group.
                pane.ClickCheck(control.Name);
                Trail?.Invoke($"check {pane.File} {control.Name} {(pane.Checks.GetValueOrDefault(control.Name) ? 1 : 0)}");
                if (Options?.Owns(pane) == true) Options.Checked(pane, control.Name);
                break;
            case "scrollslider" when BarOf(pane, control) is { } owner && pane.Bars.TryGetValue(owner.Name, out var valued):
                // Caught where it was pressed, and dragged from there.
                draggingBar = (pane, valued);
                sliderGrab = PointerX - (pane.X + owner.Pos[0] + valued.SliderX);
                break;
        }
    }

    (MenuPane Pane, MenuBar Bar)? draggingBar;

    /// <summary>
    /// The left button coming up (event 1): only over the control that was pressed, and a button
    /// acts only if it is still held down — every menu binds its buttons here, so they
    /// all act on the release.
    /// </summary>
    public void Release()
    {
        Options?.Fixes.Release();
        Options?.Keys.Release();
        draggingSlider = false;
        draggingBar = null;
        var pane = Active();
        var control = ControlUnderPointer(pane);
        var (wasPane, wasControl) = (pressedPane, pressedControl);
        pressedPane = null;
        pressedControl = null;
        if (pane is null || control is null || !ReferenceEquals(pane, wasPane)
            || !control.Name.Equals(wasControl, StringComparison.OrdinalIgnoreCase)) return;
        if (control.Type is "button" or "checkbox") Sound?.Invoke(Audio.FoleyEvent.ButtonRelease);
        if (control.Type == "checkbox") { OtherClick?.Invoke(pane, control); return; }
        if (control.Type == "scrollbar" && pane.Bars.TryGetValue(control.Name, out var valueBar)) { BarClicked(pane, control, valueBar); return; }
        if (control.Type == "button" && pane.Buttons.TryGetValue(control.Name, out var state) && state == ButtonState.Pressed)
        {
            pane.Buttons[control.Name] = ButtonState.Rollover;
            Clicked(pane, control);
        }
    }

    /// <summary>
    /// A click on a value bar's track, off its slider (the bar's release; its press
    /// pages only a bar that belongs to a text box): a quantized horizontal bar steps one
    /// stop toward the pointer — up when the pointer is right of the slider, down otherwise
    /// (held to its stops) — and any bar then tells its owner, which on the Advanced Display
    /// screen makes the level Custom whether or not the bar moved.
    /// </summary>
    void BarClicked(MenuPane pane, NuiElement control, MenuBar bar)
    {
        if (bar.Quanta > 0 && control.Get("vertical") != "1")
            bar.Set(bar.Value + (PointerX - pane.X - control.Pos[0] > bar.SliderX ? 1 : -1));
        Trail?.Invoke($"bar {pane.File} {control.Name} {bar.Value}");
        if (Options?.Owns(pane) != true) return;
        if (bar.Quanta == 0) Options.Slid(pane, control.Name, bar.Value);
        else Options.Stepped(pane, control.Name);
    }

    /// <summary>The wheel over a list: a page per notch-burst, up for a positive turn.</summary>
    public void Wheel(int turn)
    {
        var pane = Active();
        if (turn != 0 && pane?.File == FixesPage.File && Options?.Fixes is { } fixes) { fixes.Wheel(turn); return; }
        if (turn != 0 && pane?.File == KeysPage.File && Options?.Keys is { } keys) { keys.Wheel(turn); return; }
        if (turn == 0 || ControlUnderPointer(pane) is not { Type: "textlistbox" } list) return;
        pane!.Lists[list.Name].Page(turn > 0 ? -1 : +1);
    }

    /// <summary>A key, for the active pane's key hook — which does not run on the frame it was shown.</summary>
    public void Key(MenuKey key)
    {
        if (Active() is not { JustShown: false } pane) return;
        // M is the map's key and nobody else's.
        if (key == MenuKey.Map && Map?.Owns(pane) != true) return;
        if (OtherKey?.Invoke(pane, key) == true) return;
        switch (pane.File)
        {
            case SpFile when key == MenuKey.Escape: BackToMainMenu(); break;   // Back to the main menu
            case CreditsPaneFile when key == MenuKey.Escape: CloseCredits(); break;
            case EscapeFile when key == MenuKey.Escape: PutEscapeAway(); break; // Escape closes it
            case LoadFile: if (key == MenuKey.Enter) LoadChosen(); else CloseLoad(); break;
            case SaveFile: if (key == MenuKey.Enter) SaveChosen(); else CloseSave(); break;
            case MessageBoxFile:
                // Each only when its button is on the box.
                if (key == MenuKey.Enter && !pane.Hidden.Contains("mbOkButton")) MessageDone(1);
                if (key == MenuKey.Escape && !pane.Hidden.Contains("mbCancelButton")) MessageDone(2);
                break;
            case InputBoxFile:
                // Enter only while OK is on the box and Escape only while Cancel is (the box's key hook tests
                // each button's hidden bit, 0x200, before it answers).
                if (key == MenuKey.Enter && !pane.Hidden.Contains(InputOk)) InputDone(1);
                else if (key == MenuKey.Escape && !pane.Hidden.Contains(InputCancel)) InputDone(2);
                break;
            default:
                if (Options?.Owns(pane) == true) Options.Key(pane, key);
                else if (Map?.Owns(pane) == true) Map.Key(key);
                else if (DevConsole?.Owns(pane) == true) DevConsole.Key(key);
                break;
        }
    }

    /// <summary>A typed character (`WM_CHAR` → event 9) for the active pane's field with the keyboard.</summary>
    public void Type(char c, bool shift = false)
    {
        // The console's key hook sees a character before its field does.
        if (Active() is { } open && DevConsole?.Owns(open) == true && DevConsole.Char(c)) return;
        if (Active() is { } pane && pane.Focus is { } field && pane.Edits.TryGetValue(field, out var edit))
        {
            // Tab moves the keyboard round the pane's fields (the edit's Tab case), Shift+Tab its odd way.
            if (c == '\t') pane.Tab(shift);
            else edit.Type(c);
        }
    }

    /// <summary>
    /// Ctrl+V in the field with the keyboard (the edit's 0x16 case, <see cref="MenuEdit.Paste"/>). The
    /// console's hook sees the character first, as it sees every other.
    /// </summary>
    public void Paste(string? clip)
    {
        if (Active() is { } open && DevConsole?.Owns(open) == true && DevConsole.Char('\x16')) return;
        if (clip is not null && Active() is { } pane && pane.Focus is { } field && pane.Edits.TryGetValue(field, out var edit))
            edit.Paste(clip);
    }

    /// <summary>
    /// The text of the field with the keyboard, or null when no field has it — what Ctrl+C copies
    /// (ours, <see cref="TypedLine"/>; 1.1's edit field has no copy).
    /// </summary>
    public string? FocusedText
    {
        get
        {
            // (Not `field`: inside an accessor that is C# 14's keyword for the property's own backing field.)
            if (Active() is not { } pane || pane.Focus is not { } focused) return null;
            return pane.Edits.TryGetValue(focused, out var edit) ? edit.Text : null;
        }
    }

    /// <summary>What Ctrl+C copies from the menus: the focused field's text, or — in the developer console, its line empty — the console's last answer.</summary>
    public string? CopyText =>
        TypedLine.Copied(FocusedText, Active() is { } open && DevConsole?.Owns(open) == true ? DevConsole.LastOutput : null);

    /// <summary>
    /// One frame of the interface: the clock, the buttons' and lists' hover, the caret's blink, a
    /// dragged slider, the credits, and the floating texts' clean-up.
    /// </summary>
    public void Frame(int nowMs)
    {
        NowMs = nowMs;
        var active = Active();
        var on = ControlUnderPointer(active);
        // The tip under a resting pointer (ours, MenuTips): the same control since tipSince, no button held.
        var tipped = active is not null && !LeftHeld ? MenuTips.At(active, PointerX, PointerY) : null;
        string? tipKey = tipped is { } t ? active!.File + "/" + t.Control : null;
        if (tipKey != tipOn) { tipOn = tipKey; tipSince = nowMs; }
        tipText = tipped?.Tip;
        foreach (var pane in Visible().ToList())
        {
            bool mine = ReferenceEquals(pane, active);
            pane.UpdateButtons(mine ? on?.Name : null, LeftHeld);
            foreach (var (name, list) in pane.Lists)
                list.HoverAt(mine && on?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) == true
                    ? PointerY - (pane.Y + list.Element.Pos[1]) : null);
            foreach (var (name, edit) in pane.Edits)
                edit.Blink(nowMs, name.Equals(pane.Focus, StringComparison.OrdinalIgnoreCase)
                                  || (mine && name.Equals(on?.Name, StringComparison.OrdinalIgnoreCase)));
        }
        if (draggingSlider && active is not null)
            foreach (var slider in active.Nui.Elements.Where(e => e.Type == "scrollslider"))
                if (BarOf(active, slider) is { } bar && ListOf(active, bar) is { } list && list.Rows.Count > 0)
                {
                    int barY = bar.Pos[1], travel = bar.Size[1] - slider.Size[1];
                    float y = Math.Clamp(PointerY - sliderGrab - active.Y, barY, barY + travel);
                    int min = list.Rows[^1].Y - list.Font.LineHeight / 4;
                    int value = (int)((y - barY) / travel * (0 - min) + min);
                    list.ScrollTo(-value);
                }
        if (draggingBar is { } held && IsUp(held.Pane))
        {
            var (pane, bar) = held;
            if (bar.Drag(PointerX - pane.X - bar.Element.Pos[0], sliderGrab))
            {
                if (bar.Quanta == 0) Options?.Slid(pane, bar.Element.Name, bar.Value);
                else Options?.Stepped(pane, bar.Element.Name);
            }
        }
        if (Options?.Fixes is { } fixes && active is not null && active.File == FixesPage.File)
        {
            fixes.Drag(PointerY - active.Y);                                // a held slider
            fixes.Hover(active, on?.Name);
        }
        if (Options?.Keys is { } keys && active is not null && active.File == KeysPage.File)
        {
            keys.Drag(PointerY - active.Y);                                 // a held slider
            keys.Hover(active, on?.Name);
        }
        if (Map is { Pane: { } map } && ReferenceEquals(map, active)) Map.Hover(on);
        if (loaded.TryGetValue(CreditsPaneFile, out var credits) && IsUp(credits)) CreditsFrame();
        Floating.Prune(nowMs);
    }

    /// <summary>After the frame is drawn: every pane up has now been drawn once (flag 0x800 cleared).</summary>
    public void Drawn()
    {
        foreach (var pane in Visible()) pane.JustShown = false;
    }

    /// <summary>
    /// The request, taken at the end of the frame. The two that only open a screen are
    /// done here; the rest — a new game, a load, a save, back to the main menu from a game, the
    /// window closing — are the host's, and come back.
    /// </summary>
    public (MenuRequest Request, int Slot) TakeRequest()
    {
        var (request, slot) = (Request, RequestSlot);
        Request = MenuRequest.None;
        switch (request)
        {
            case MenuRequest.SaveMenu: OpenSave(); return (MenuRequest.None, 0);
            case MenuRequest.LoadMenu: OpenLoad(); return (MenuRequest.None, 0);
            case MenuRequest.MainMenu when State == StateFrontEnd:
                OpenMainMenu();
                if (CreditsOnMainMenu) OpenCredits();
                CreditsOnMainMenu = false;
                return (MenuRequest.None, 0);
        }
        return (request, slot);
    }

    /// <summary>Asks for the main menu, with the credits rolling when the game has ended.</summary>
    public void AskMainMenu(bool withCredits)
    {
        CreditsOnMainMenu = withCredits;
        Ask(MenuRequest.MainMenu);
    }

    /// <summary>Sets the request — one at a time; a later one in the same frame wins.</summary>
    public void Ask(MenuRequest request, int slot = 0)
    {
        Request = request;
        RequestSlot = slot;
        QuickSave.Asked(request, slot, network: !SinglePlayer);     // the quick keys' slot (ours)
    }

    // ---- what each button does ----------------------------------------------------------------

    void Clicked(MenuPane pane, NuiElement button)
    {
        Trail?.Invoke($"click {pane.File} {button.Name}");
        if (OtherClick?.Invoke(pane, button) == true) return;
        switch (pane.File)
        {
            case MainMenuFile:
                // One callback for every button, by its `buttonvalue`.
                switch (button.Get("buttonvalue"))
                {
                    case "1": Hide(pane); OpenSp(); break;
                    case "2": if (Multiplayer is { } mp) { Hide(pane); mp(); } else NotWritten?.Invoke("multiplayer"); break;
                    case "3": if (Options is not null) Options.Open(); else NotWritten?.Invoke("options"); break;
                    case "4": OpenCredits(); break;
                    case "5": Ask(MenuRequest.Quit); break;
                }
                break;
            case SpFile:
                switch (button.Name.ToLowerInvariant())
                {
                    case "spnewbutton": NewGame(); break;
                    case "sploadbutton": Ask(MenuRequest.LoadMenu); break;
                    case "spmainmenubutton": BackToMainMenu(); break;
                }
                break;
            case CreditsPaneFile:
                if (button.Name.Equals("closeButton", StringComparison.OrdinalIgnoreCase)) CloseCredits();
                break;
            case EscapeFile:
                PutEscapeAway();
                switch (button.Name.ToLowerInvariant())
                {
                    case "emsavebutton": Ask(MenuRequest.SaveMenu); break;
                    case "emloadbutton": Ask(MenuRequest.LoadMenu); break;
                    // Options opens its own screen and comes back to the game.
                    case "emoptionsbutton": if (Options is not null) Options.Open(); else NotWritten?.Invoke("options"); break;
                    case "emquitbutton": AskMainMenu(withCredits: false); break;
                }
                break;
            case LoadFile:
                if (button.Name.Equals("spLoadLoadButton", StringComparison.OrdinalIgnoreCase)) LoadChosen();
                else CloseLoad();
                break;
            case SaveFile:
                if (button.Name.Equals("spSaveSaveButton", StringComparison.OrdinalIgnoreCase)) SaveChosen();
                else CloseSave();   // Cancel
                break;
            case MessageBoxFile:
                MessageDone(button.Name.Equals("mbOkButton", StringComparison.OrdinalIgnoreCase) ? 1 : 2);
                break;
            case InputBoxFile:
                InputDone(button.Name.Equals("ibOkButton", StringComparison.OrdinalIgnoreCase) ? 1 : 2);
                break;
            default:
                if (Options?.Owns(pane) == true) Options.Clicked(pane, button);
                else if (Map?.Owns(pane) == true) Map.Clicked(button);
                break;
        }
    }

    // Back: to the main menu.
    void BackToMainMenu()
    {
        Hide(Pane(SpFile));
        OpenMainMenu();
    }

    /// <summary>
    /// New: with an autosave there, the warning first — OK starts the game, Cancel goes
    /// back to the **main** menu, not this one.
    /// </summary>
    void NewGame()
    {
        if (SaveSlots?.Exists(0) == true)
        {
            Message(Text("UI_WARNING"), Text("UI_SPLOADSAVE_OVERWRITEAUTOSAVE"), ok: true, cancel: true, result =>
            {
                Hide(Pane(SpFile));
                if (result == 1) Ask(MenuRequest.NewGame);
                else OpenMainMenu();
            });
            return;
        }
        Ask(MenuRequest.NewGame);
        Hide(Pane(SpFile));
    }

    /// <summary>Load: nothing with no row chosen; otherwise no question asked.</summary>
    void LoadChosen()
    {
        int slot = Pane(LoadFile).Lists["spLoadList"].CurrentData;
        if (slot < 0) return;
        // A 1.1 save is brought across only on the player's say-so, told plainly what it is (ours:
        // explicit, with a warning, the original untouched).
        if (slot >= OriginalRow)
        {
            Message(Text("UI_WARNING"), ImportWarning, ok: true, cancel: true, result =>
            {
                if (result != 1) return;
                Ask(MenuRequest.Import, slot - OriginalRow);
                PopLayer();
                if (SinglePlayer) Paused = false;
            });
            return;
        }
        Ask(MenuRequest.Restore, slot);
        PopLayer();
        if (SinglePlayer) Paused = false;
    }

    // Cancel: closed, unpaused in single player, cursor back.
    void CloseLoad()
    {
        PopLayer();
        if (SinglePlayer) Paused = false;
    }

    // Closes the save screen: unpaused in single player, cursor back.
    void CloseSave()
    {
        PopLayer();
        if (SinglePlayer) Paused = false;
    }

    /// <summary>
    /// Save: an occupied slot warns first; then the name box, filled with the place and
    /// the time played. An empty name is refused with an error; anything else is the save.
    /// </summary>
    void SaveChosen()
    {
        int slot = Pane(SaveFile).Lists["spSaveList"].CurrentData;
        if (slot < 0) return;
        SaveName = "";
        if (SaveSlots?.Exists(slot) == true)
            // After the overwrite warning's OK: the name box, filled with "place (hh:mm:ss)".
            Message(Text("UI_WARNING"), Text("UI_SPLOADSAVE_OVERWRITESAVE"), ok: true, cancel: true,
                    result => { if (result == 1) AskName(slot); });
        else AskName(slot);
    }

    void AskName(int slot)
    {
        Input(Text("UI_SPLOADSAVE_ENTERNAME"), Describe?.Invoke() ?? "<unknown scene> (00:00:00)", NameLimit, (result, text) =>
        {
            if (result != 1) return;
            string name = text.Length > NameLimit - 1 ? text[..(NameLimit - 1)] : text;
            if (name.Length == 0)
            {
                Message(Text("UI_GENERAL_ERROR"), Text("UI_NET_PLEASEENTERNAME"), ok: true, cancel: false);
                return;
            }
            Ask(MenuRequest.Save, slot);
            SaveName = name;
            // Posted before the save is written, and whatever becomes of it.
            Posted?.Invoke(Text("SPLOADSAVE_GAMESAVED"));
            var menu = Pane(SaveFile);
            if (menu.Shown && IsUp(menu))
            {
                PopLayer();
                if (SinglePlayer) Paused = false;
            }
        });
    }

    // OK (result 1) and Cancel (result 2): the caller's callback, then the box closed.
    void MessageDone(int result)
    {
        var box = Pane(MessageBoxFile);
        var then = messageThen;
        messageThen = null;
        then?.Invoke(result);
        Hide(box);
    }

    // OK (result 1) and Cancel (result 2): the callback, then the box closed.
    void InputDone(int result)
    {
        var box = Pane(InputBoxFile);
        string text = box.Edits["ibEdit"].Text;
        var then = inputThen;
        inputThen = null;
        Hide(box);
        then?.Invoke(result, text);
    }

    // ---- the scroll bar's partners --------------------------------------------------------------

    static MenuList? ListOf(MenuPane pane, NuiElement bar) =>
        pane.Lists.Values.FirstOrDefault(l => bar.Name.Equals(l.Element.Get("vscrollbar"), StringComparison.OrdinalIgnoreCase));

    static NuiElement? SliderOf(MenuPane pane, NuiElement bar) =>
        bar.Get("slider") is { } name ? pane.Element(name) : null;

    static NuiElement? BarOf(MenuPane pane, NuiElement slider) =>
        pane.Nui.Elements.FirstOrDefault(e => e.Type == "scrollbar"
            && slider.Name.Equals(e.Get("slider"), StringComparison.OrdinalIgnoreCase));
}
