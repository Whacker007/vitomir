// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Options screen and its
// five sub-screens, (Gameplay, Controls, Display,
// Advanced and Sound), read on
// — and a page of Vitomir's own that hangs off it, the Fixes page
// (`FixesPage`).
using System.Globalization;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>One display mode the Display screen can offer: `%d x %d %d BPP`.</summary>
public readonly record struct VideoMode(int Width, int Height, int Bits)
{
    public override string ToString() => $"{Width} x {Height} {Bits} BPP";
}

/// <summary>
/// The Options screen. Everything it edits is a <see cref="GameOptions"/>; what the game does with
/// each setting is the host's (<see cref="Applied"/> and the live hooks), because the screen itself
/// is the same whether it was opened from the main menu or from the escape menu.
/// </summary>
public sealed class OptionsMenu
{
    /// <summary>The panes, as the engine names them (`ui\Options.nui`, and so on).</summary>
    public const string OptionsFile = "Options", GameplayFile = "OptionsGameplay", ControlsFile = "OptionsControls",
                        DisplayFile = "OptionsDisplay", AdvancedFile = "optionsDisplayAdv", SoundFile = "OptionsSound";

    /// <summary>The detail names, in the order of the engine's table.</summary>
    public static readonly string[] DetailNames = ["UI_DETAIL_VERYLOW", "UI_DETAIL_LOW", "UI_DETAIL_NORMAL", "UI_DETAIL_HIGH", "UI_DETAIL_CUSTOM"];

    /// <summary>The four speaker radios in the order the mixer numbers them.</summary>
    public static readonly string[] SpeakerBoxes = ["opso2speakers", "opso4speakers", "opsoHeadphones", "opsoSurround"];

    /// <summary>The Controls screen's ten boxes: its control, and the word and bit each stands for.</summary>
    public static readonly (string Box, bool AiWord, uint Bit)[] ControlBoxes =
    [
        ("opConMousePitch", false, GameOptions.MousePitch), ("opConCameraSlew", false, GameOptions.AutoSlew),
        ("opConRepeatAttack", false, GameOptions.RepeatAttack), ("opConAutoQuickSlot", false, GameOptions.BackpackAutoLoad),
        ("opConQuickDisc", true, GameOptions.UseQuickDisc), ("opConQuickItems", true, GameOptions.UseQuickItems),
        ("opConHealing", true, GameOptions.CastHealing), ("opConBuff", true, GameOptions.CastBuff),
        ("opConDamage", true, GameOptions.CastDamage), ("opConSpecial", true, GameOptions.CastSpecial),
    ];

    readonly FrontEnd menus;
    readonly Func<string, string?> find;

    public OptionsMenu(FrontEnd menus, Func<string, string?> strings, GameOptions? options = null)
    {
        this.menus = menus;
        find = strings;
        Options = options ?? GameOptions.Current;
        Fixes = new FixesPage(menus);
        Keys = new KeysPage(menus);
    }

    /// <summary>The settings the screens show and change. OK writes into this one.</summary>
    public GameOptions Options { get; set; }
    /// <summary>Vitomir's own Fixes page, opened from the Options screen.</summary>
    public FixesPage Fixes { get; }
    /// <summary>Vitomir's own key page, opened from the Controls screen.</summary>
    public KeysPage Keys { get; }

    // ---- what the host supplies ----------------------------------------------------------------

    /// <summary>A sub-screen's OK: the settings changed, for the game to take up and to be saved.</summary>
    public event Action<GameOptions>? Applied;
    /// <summary>The brightness, live while the slider moves and again on Cancel (the renderer's ramp).</summary>
    public Action<float>? Gamma { get; set; }
    /// <summary>The music volume, live.</summary>
    public Action<float>? MusicVolume { get; set; }
    /// <summary>The effects volume, live.</summary>
    public Action<float>? EffectsVolume { get; set; }
    /// <summary>The display devices the Display screen lists (the renderer's device table).</summary>
    public Func<IReadOnlyList<string>>? Devices { get; set; }
    /// <summary>
    /// **Ours:** the device list as a chooser. Vitomir's rows are its window modes
    /// (<see cref="FullScreen.WindowModes"/>): <see cref="CurrentDevice"/> is the one in use;
    /// <see cref="ModesFor"/> the video modes a row offers and <see cref="CurrentModeFor"/> the one
    /// it would open at, so the list beside it follows the row picked; <see cref="SetDisplay"/> makes
    /// a row and a mode the window's when OK is pressed (false: refused). 1.1's rows were the
    /// machine's display devices, and a change of one wanted a restart — still what happens when no
    /// <see cref="SetDisplay"/> is given.
    /// </summary>
    public Func<int>? CurrentDevice { get; set; }
    public Func<int, IReadOnlyList<VideoMode>>? ModesFor { get; set; }
    public Func<int, VideoMode>? CurrentModeFor { get; set; }
    public Func<int, VideoMode, bool>? SetDisplay { get; set; }
    /// <summary>The modes the Display screen lists (every mode of the device that is not 8 bits).</summary>
    public Func<IReadOnlyList<VideoMode>>? Modes { get; set; }
    /// <summary>The mode in use.</summary>
    public Func<VideoMode>? CurrentMode { get; set; }
    /// <summary>A new mode chosen and OK pressed: answers whether the display took it.</summary>
    public Func<VideoMode, bool>? SetMode { get; set; }
    /// <summary>What the Advanced Display screen's driver box says: the device, the driver, the texture memory.</summary>
    public Func<(string Device, string Driver, long Memory)>? Driver { get; set; }
    /// <summary>The Sound screen's providers (the mixer's table), and which is in use.</summary>
    public Func<(IReadOnlyList<string> Names, int Current)>? Providers { get; set; }

    // ---- open and close ------------------------------------------------------------------------

    /// <summary>Whether the Options screen is up at all.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Options: the game's own panes closed, the game paused in single
    /// player, the game state to 4 — the game not drawn — a layer pushed and the pane up.
    /// </summary>
    public void Open()
    {
        menus.CloseGamePanes?.Invoke();
        if (menus.SinglePlayer) menus.Paused = true;
        if (menus.State == FrontEnd.StateGame) menus.State = FrontEnd.StateOptions;
        IsOpen = true;
        menus.PushLayer();
        menus.Show(menus.Pane(OptionsFile));
    }

    /// <summary>
    /// Done, or Escape on the Options screen: every sub-screen destroyed, the
    /// game unpaused, the state back to 3, and the layer popped.
    /// </summary>
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        // Whatever sub-screens are still stacked on it go first: one layer each (the close destroys
        // them all).
        var options = menus.Pane(OptionsFile);
        for (int guard = 0; guard < 8 && options.Shown && !menus.IsUp(options); guard++) menus.PopLayer();
        if (menus.SinglePlayer) menus.Paused = false;
        if (menus.State == FrontEnd.StateOptions) menus.State = FrontEnd.StateGame;
        menus.PopLayer();
        Closed?.Invoke();
    }

    /// <summary>The screen gone: the host puts the cursor back and the world's view.</summary>
    public event Action? Closed;

    /// <summary>A sub-screen: its own layer and the pane shown.</summary>
    void Push(string file)
    {
        menus.PushLayer();
        menus.Show(menus.Pane(file));
    }

    /// <summary>A sub-screen put away: its layer popped, which uncovers the Options screen.</summary>
    void Pop() => menus.PopLayer();

    string Text(string key) => find(key) ?? key;

    // ---- the Options screen --------------------------------------------------------------------

    /// <summary>Whether a pane is one of these screens.</summary>
    public bool Owns(MenuPane pane) => pane.File is OptionsFile or GameplayFile or ControlsFile or DisplayFile
        or AdvancedFile or SoundFile or FixesPage.File or KeysPage.File
        || pane.File.Equals(AdvancedFile, StringComparison.OrdinalIgnoreCase);

    /// <summary>A button released over itself on one of these screens.</summary>
    public void Clicked(MenuPane pane, NuiElement button)
    {
        string name = button.Name.ToLowerInvariant();
        if (pane.File == FixesPage.File) { Fixes.Clicked(name); return; }
        if (pane.File == KeysPage.File) { Keys.Clicked(name); return; }
        switch (pane.File)
        {
            case OptionsFile:
                switch (name)
                {
                    case "opgameplaybutton": OpenGameplay(); break;
                    case "opcontrolsbutton": OpenControls(); break;
                    case "opdisplaybutton": OpenDisplay(); break;
                    case "opsoundbutton": OpenSound(); break;
                    case "opdonebutton": Close(); break;
                    case FixesButton: Fixes.Open(); break;
                    case KeybindsButton: Keys.Open(); break;                // in our lettering
                }
                break;
            case GameplayFile:
                if (name == "opgpokbutton") GameplayOk(pane);
                else if (name == "opgpcancelbutton") Pop();
                break;
            case ControlsFile:
                if (name == "opconokbutton") ControlsOk(pane);
                else if (name == "opconcancelbutton") Pop();
                else if (name == KeysButton) Keys.Open();
                break;
            case DisplayFile:
                switch (name)
                {
                    case "opdisokbutton": DisplayOk(pane); break;
                    case "opdiscancelbutton": DisplayCancel(); break;
                    case "opdisadvancedoptionsbutton": OpenAdvanced(); break;
                    case "opdisdetailmore": StepDetail(pane, "opdisDetailString", +1); break;
                    case "opdisdetailless": StepDetail(pane, "opdisDetailString", -1); break;
                }
                break;
            case SoundFile:
                if (name == "opsoexitbutton") SoundExit();
                break;
            default:
                if (pane.File.Equals(AdvancedFile, StringComparison.OrdinalIgnoreCase))
                    switch (name)
                    {
                        case "opdaokbutton": AdvancedOk(pane); break;
                        case "opdacancelbutton": Pop(); break;
                        case "opdadetailmore": StepDetail(pane, "opdaDetailString", +1); break;
                        case "opdadetailless": StepDetail(pane, "opdaDetailString", -1); break;
                    }
                break;
        }
    }

    /// <summary>
    /// Escape on one of these screens. Every sub-screen's key hook pops its layer — which is Cancel
    /// on the Gameplay and Controls screens, and on the Display screen puts the brightness back
    /// first; on the Sound screen, whose sliders act as they move, it is Exit. The
    /// Options screen's own closes the whole thing. Enter does nothing on any of them.
    /// </summary>
    public void Key(MenuPane pane, MenuKey key)
    {
        if (key != MenuKey.Escape) return;
        if (pane.File == FixesPage.File) { Fixes.Cancel(); return; }
        if (pane.File == KeysPage.File) { Keys.Cancel(); return; }
        // The Advanced screen's key hook: the touched flag back to what it was on the way in, then popped.
        if (pane.File.Equals(AdvancedFile, StringComparison.OrdinalIgnoreCase)) { madeCustom = madeCustomAtOpen; Pop(); return; }
        switch (pane.File)
        {
            case OptionsFile: Close(); break;
            case DisplayFile: DisplayCancel(); break;
            case SoundFile: SoundExit(); break;   // pops as Exit does; the volumes written as Exit writes them (ours: 1.1 writes at shut-down)
            default: Pop(); break;
        }
    }

    /// <summary>A checkbox pressed on one of these screens (after it has toggled).</summary>
    public void Checked(MenuPane pane, string box)
    {
        if (pane.File == FixesPage.File) { Fixes.Checked(pane, box); return; }
        if (pane.File == SoundFile)
        {
            // A speaker radio takes effect at once: the mixer's speaker type.
            int type = Array.FindIndex(SpeakerBoxes, b => b.Equals(box, StringComparison.OrdinalIgnoreCase));
            if (type >= 0) { Options.SpeakerType = type; Save(); }
            return;
        }
        if (pane.File.Equals(AdvancedFile, StringComparison.OrdinalIgnoreCase)) MakeCustom(pane);
    }

    /// <summary>A bar dragged to a new value (its callback).</summary>
    public void Slid(MenuPane pane, string bar, int value)
    {
        switch (bar.ToLowerInvariant())
        {
            case "opdisgammascroll":
                // The stop × 0.1, held to 0.5..2, and the ramp set at once.
                Options.Gamma = Math.Clamp(value * 0.1f, GameOptions.MinGamma, GameOptions.MaxGamma);
                Gamma?.Invoke(Options.Gamma);
                break;
            case "opsomusicscroll":
                // The value × 0.01, straight to the music.
                Options.MusicVolume = Math.Clamp(value * 0.01f, 0f, 1f);
                MusicVolume?.Invoke(Options.MusicVolume);
                break;
            case "opsosoundscroll":
                // The value × 0.01 to the effects, and interface sound 6 as a sample.
                Options.EffectsVolume = Math.Clamp(value * 0.01f, 0f, 1f);
                EffectsVolume?.Invoke(Options.EffectsVolume);
                menus.Sound?.Invoke(SampleSound);
                break;
        }
    }

    /// <summary>What the effects slider plays at each change so the level can be heard (interface sound 6).</summary>
    public const int SampleSound = 6;

    /// <summary>
    /// Vitomir's buttons on 1.1's screens **in 1.1's own lettering** (<see cref="MenuLetteringMode.Original"/>; in ours,
    /// <see cref="MenuLettering"/> puts **keybinds** and **fixes** among the Options screen's rows and the Gameplay
    /// screen's two boxes on bars, and this does nothing): **fixes** on the Options screen, under Done, where the
    /// column of buttons would go on. The screen has no art for it, so it is a caption in the game's own `morpheus20`
    /// that lights like a list row — and **keys** on the Controls screen, in the empty strip
    /// beside Cancel where the Display screen keeps its Advanced button (<see cref="KeysPage"/>). In lower case since
    /// As 1.1's own words beside them (<i>"we can change it back to lowercase for the original v1.1. menu
    /// style, but only for that"</i>); Title Case.
    /// </summary>
    public static void AddOurButtons(MenuPane pane)
    {
        if (pane.Lettered) return;
        NuiElement Button(string name, int x, int y, int w, int h) => new("button", new Dictionary<string, string>
        {
            ["name"] = name, ["pos"] = $"{x},{y}", ["size"] = $"{w},{h}", ["font"] = "morpheus20",
        }, 0);
        if (pane.File.Equals(OptionsFile, StringComparison.OrdinalIgnoreCase) && pane.Element(FixesButton) is null)
        {
            pane.Nui.Elements.Add(Button(FixesButton, FixesX, FixesY, FixesW, FixesH));
            pane.Buttons[FixesButton] = ButtonState.Normal;
            pane.Texts[FixesButton] = FixesCaption;
        }
        if (pane.File.Equals(ControlsFile, StringComparison.OrdinalIgnoreCase) && pane.Element(KeysButton) is null)
        {
            pane.Nui.Elements.Add(Button(KeysButton, KeysX, KeysY, KeysW, KeysH));
            pane.Buttons[KeysButton] = ButtonState.Normal;
            pane.Texts[KeysButton] = KeysCaption;
        }
        AlwaysRunOption.Add(pane);                                   // the Gameplay screen's always run
        QuickLoadConfirm.Add(pane);                                  // and its confirm quick load
    }

    /// <summary>Where the keys button sits on the Controls screen — the Display screen's Advanced slot (129,12 91 x 41) — and what it says (ours).</summary>
    public const int KeysX = 129, KeysY = 20, KeysW = 91, KeysH = 34;
    public const string KeysCaption = "keys";
    /// <summary>The name of the button Vitomir adds to the Controls screen for its key page (ours).</summary>
    public const string KeysButton = "opconkeysbutton";

    /// <summary>Where the Fixes button sits on the Options screen, and what it says (ours).</summary>
    public const int FixesX = 262, FixesY = 138, FixesW = 119, FixesH = 38;
    public const string FixesCaption = "fixes";

    /// <summary>The name of the button Vitomir adds to the Options screen for its Fixes page (ours).</summary>
    public const string FixesButton = "opfixesbutton";

    /// <summary>The name of the Options screen's keybinds row in our lettering (ours), which opens the key page.</summary>
    public const string KeybindsButton = "opkeybindsbutton";

    void Save()
    {
        Choices.Chose(Options);
        try { Options.Save(SavePath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Where the settings are written: the player's own <c>vitomir.ini</c> unless a test says otherwise.</summary>
    public string? SavePath { get; set; }

    /// <summary>Where what is written is also kept for the runs that follow (<see cref="MenuChoices"/>, ours); a test passes its own.</summary>
    public MenuChoices Choices { get; set; } = MenuChoices.Session;

    void Apply()
    {
        Save();
        Applied?.Invoke(Options);
    }

    // ---- Gameplay -------------------------------------------------------------------

    /// <summary>
    /// The Gameplay screen: the blood box **hidden** when the build locks it (bit 0x100), and
    /// the show hook putting both boxes from the word — the blood box is
    /// checked when <b>NoBlood</b> is set, and the subtitles box when ForceSubtitles is.
    /// </summary>
    public void OpenGameplay()
    {
        var pane = menus.Pane(GameplayFile);
        if (Options.Has(GameOptions.BloodLocked)) pane.Hidden.Add("opgpBloodOn");
        pane.Check("opgpBloodOn", Options.Has(GameOptions.NoBlood));
        pane.Check("opgpSubtitling", Options.Has(GameOptions.ForceSubtitles));
        AlwaysRunOption.Show(pane, Options);                        // ours
        QuickLoadConfirm.Show(pane, Options);                       // ours
        Push(GameplayFile);
    }

    /// <summary>OK: the two bits from the two boxes, then the layer popped.</summary>
    void GameplayOk(MenuPane pane)
    {
        Options.Set(GameOptions.NoBlood, pane.Checks.GetValueOrDefault("opgpBloodOn"));
        Options.Set(GameOptions.ForceSubtitles, pane.Checks.GetValueOrDefault("opgpSubtitling"));
        AlwaysRunOption.Take(pane, Options);                        // ours
        QuickLoadConfirm.Take(pane, Options);                       // ours
        Apply();
        Pop();
    }

    // ---- Controls -------------------------------------------------------------------

    /// <summary>The Controls screen, its ten boxes put from the two words.</summary>
    public void OpenControls()
    {
        var pane = menus.Pane(ControlsFile);
        foreach (var (box, ai, bit) in ControlBoxes) pane.Check(box, ai ? Options.HasAi(bit) : Options.Has(bit));
        Push(ControlsFile);
    }

    /// <summary>OK: both words from the boxes, the AI flags refolded, the layer popped.</summary>
    void ControlsOk(MenuPane pane)
    {
        foreach (var (box, ai, bit) in ControlBoxes)
        {
            bool on = pane.Checks.GetValueOrDefault(box);
            if (ai) Options.SetAi(bit, on); else Options.Set(bit, on);
        }
        Apply();
        Pop();
    }

    // ---- Display --------------------------------------------------------------------

    float gammaWas;
    int detailShown;
    int deviceWas;
    VideoMode modeWas;

    /// <summary>
    /// The Display screen (and its show hook): the device list with the one in
    /// use chosen, the modes of that device — every one but the 8-bit ones — with the one in use
    /// chosen, the detail level's name, and the gamma bar at `gamma × 10` on a range of 5 to 20.
    /// The gamma it opened with is kept for Cancel.
    /// </summary>
    public void OpenDisplay()
    {
        var pane = menus.Pane(DisplayFile);
        gammaWas = Options.Gamma;
        madeCustom = false;                  // the Advanced screen's touched flag, cleared here, not there
        detailShown = Options.DetailLevel;
        pane.Texts["opdisDetailString"] = Text(DetailNames[detailShown]);
        HideInert(pane);

        var devices = pane.Lists["opdisDeviceList"];
        devices.Clear();
        var names = Devices?.Invoke() ?? [];
        for (int i = 0; i < names.Count; i++) devices.Add(names[i], i);
        deviceWas = Math.Clamp(CurrentDevice?.Invoke() ?? 0, 0, Math.Max(0, names.Count - 1));
        devices.Select(deviceWas);

        modeWas = FillModes(pane, deviceWas);

        var bar = pane.Bars["opdisGammaScroll"];
        bar.Range(5, 20);
        bar.Set((int)(Options.Gamma * 10f));
        Push(DisplayFile);
    }

    /// <summary>
    /// The Display screen's controls that would do nothing here (inert by decision: Vitomir draws 1.1 maxed out,
    /// <see cref="DetailPresets.Maxed"/>): the detail level — its label, its name and its two arrows — and the
    /// Advanced options button (its two words go with it), so the Advanced screen cannot be reached. Ours, by request
    /// Taken off the screen's drawing and hit-testing only; the `.nui` and the settings are kept as data.
    /// </summary>
    public static readonly string[] InertDisplayControls =
        ["opdisDetailString", "opdisDetailLess", "opdisDetailMore", "opdisAdvancedOptionsButton"];

    static void HideInert(MenuPane pane)
    {
        foreach (var name in InertDisplayControls) pane.Hidden.Add(name);
        // The label is a word of ours (in our lettering): the one that says "detail level".
        foreach (var e in pane.Nui.Elements)
            if (e.Type == MenuLettering.WordsType && e.Get("button") is null
                && MenuLettering.Read(e).Text.Equals("detail level", StringComparison.OrdinalIgnoreCase))
                pane.Hidden.Add(e.Name);
    }

    /// <summary>The window-mode row the video mode list is showing the modes of.</summary>
    int modesOfRow;

    /// <summary>The modes of a device row: the host's for that row, or the one list there is.</summary>
    IEnumerable<VideoMode> ModesOf(int row) =>
        (ModesFor is { } per ? per(row) : Modes?.Invoke() ?? []).Where(m => m.Bits != 8);

    /// <summary>
    /// The video mode list filled for a device row — every mode but the 8-bit ones — with the one in
    /// use (or the one the row would open at) chosen. Answers that one.
    /// </summary>
    VideoMode FillModes(MenuPane pane, int row)
    {
        modesOfRow = row;
        var modes = pane.Lists["opdisVideoModeList"];
        modes.Clear();
        var now = CurrentModeFor is { } per ? per(row) : CurrentMode?.Invoke() ?? default;
        int chosen = 0, at = 0;
        foreach (var m in ModesOf(row))
        {
            modes.Add(m.ToString(), at);
            if (m == now) chosen = at;
            at++;
        }
        modes.Select(chosen);
        return now;
    }

    /// <summary>The mode a row of the Display screen's list stands for.</summary>
    VideoMode? ModeAt(int row)
    {
        int i = 0;
        foreach (var m in ModesOf(modesOfRow))
            if (i++ == row) return m;
        return null;
    }

    /// <summary>
    /// A list row pressed (ours): on the Display screen's device list the video modes beside it
    /// become those of the row picked (*"I don't think the resolution listing
    /// updated when you selected different display modes"*).
    /// </summary>
    public void Picked(MenuPane pane, string list)
    {
        if (pane.File != DisplayFile || !list.Equals("opdisDeviceList", StringComparison.OrdinalIgnoreCase)) return;
        if (ModesFor is null) return;
        int row = pane.Lists["opdisDeviceList"].CurrentData;
        if (row >= 0 && row != modesOfRow) FillModes(pane, row);
    }

    /// <summary>
    /// The detail arrows: one step up or down the four named levels and Custom,
    /// stopping at the ends — and on the Advanced screen the same.
    /// </summary>
    void StepDetail(MenuPane pane, string box, int step)
    {
        int next = Math.Clamp(detailShown + step, 0, 4);
        if (next == detailShown) return;
        detailShown = next;
        pane.Texts[box] = Text(DetailNames[detailShown]);
        // On the Advanced screen both names change and every box and bar follows the preset, and an
        // arrow is not a touch: the flag goes back to what it was when the screen opened.
        if (pane.File.Equals(AdvancedFile, StringComparison.OrdinalIgnoreCase))
        {
            menus.Pane(DisplayFile).Texts["opdisDetailString"] = Text(DetailNames[detailShown]);
            FillAdvanced(pane, detailShown);
            madeCustom = madeCustomAtOpen;                          // back to what it was at the open
        }
    }

    /// <summary>
    /// OK: a new mode is tried and kept only if the display takes it; the detail level is
    /// applied; the gamma kept. A change of device — or of the preset's texture depth, which Vitomir
    /// has no equivalent of — needs a restart, and says so in a message box
    /// (`UI_DETAIL_CUSTOMWARN`); otherwise the screen closes.
    /// </summary>
    void DisplayOk(MenuPane pane)
    {
        bool restart = pane.Lists["opdisDeviceList"].CurrentData is int d && d >= 0 && d != deviceWas;
        // Ours: a host that can change the row at once does (the window mode, with the video mode
        // picked beside it), and no restart is asked for.
        var picked = ModeAt(pane.Lists["opdisVideoModeList"].CurrentData);
        if (SetDisplay is { } display)
        {
            int row = pane.Lists["opdisDeviceList"].CurrentData;
            if (row >= 0 && picked is { } wanted && (row != deviceWas || wanted != modeWas)) display(row, wanted);
            restart = false;
        }
        else if (picked is { } mode && mode != modeWas)
            if (SetMode?.Invoke(mode) == false) SetMode?.Invoke(modeWas);
        Options.DetailLevel = detailShown;
        Choices.ChoseGamma(Options.Gamma);
        Apply();
        if (!restart)
        {
            Gamma?.Invoke(Options.Gamma);
            Pop();
            return;
        }
        menus.Message(Text("UI_WARNING"), Text("UI_DETAIL_CUSTOMWARN"), ok: true, cancel: false, _ => Pop());
    }

    /// <summary>Cancel, or Escape: the brightness the screen opened with, then the layer popped.</summary>
    void DisplayCancel()
    {
        Options.Gamma = Math.Clamp(gammaWas, GameOptions.MinGamma, GameOptions.MaxGamma);
        Gamma?.Invoke(Options.Gamma);
        Pop();
    }

    // ---- Advanced display -----------------------------------------------------------

    /// <summary>The Advanced screen's checkboxes (cached in this order).</summary>
    public static readonly string[] AdvancedBoxes =
    [
        "opdaSLVertex", "opdaSLLightmap", "opdaLightblobs", "opdaStyledLights", "opdaBit16", "opdaBit32",
        "opdaTrilinear", "opdaBilinear", "opdaReflections", "opdaHWtrans", "opda3dHeads",
    ];

    /// <summary>The Advanced screen's bars (the same order).</summary>
    public static readonly string[] AdvancedBars =
    [
        "opdaMipSelectionScroll", "opdaNumDLScroll", "opdaNumShadowsScroll", "opdaShadDetScroll", "opdaDecalsScroll",
        "opdaNumMipsScroll", "opdaModelLODScroll", "opdaHeadLODScroll", "opdaParticleDetailScroll",
    ];

    /// <summary>
    /// The custom preset (level 4) when nothing has been saved on the Advanced screen: the one
    /// `masquerade.ini`'s `[renderCustom]` describes (<see cref="DetailPresets.Custom"/>), else the
    /// copy of normal the renderer starts it as. The host sets it from the game's folder.
    /// </summary>
    public DetailPreset? CustomPreset { get; set; }

    /// <summary>What the Advanced screen shows for a level: a named preset, or the custom one — ours when the player has saved one here, else 1.1's.</summary>
    Dictionary<string, int> Preset(int level)
    {
        var shown = DetailPresets.Controls(DetailPresets.Of(level, CustomPreset ?? Options.RenderCustom));
        if (level == 4)
            foreach (var (name, value) in Options.Advanced) shown[name] = value;
        return shown;
    }

    /// <summary>Every box and bar of the Advanced screen put from a level's preset (the show hook and both arrows).</summary>
    void FillAdvanced(MenuPane pane, int level)
    {
        var shown = Preset(level);
        foreach (var box in AdvancedBoxes) pane.Check(box, shown.GetValueOrDefault(box) != 0);
        foreach (var name in AdvancedBars)
            if (pane.Bars.TryGetValue(name, out var bar)) bar.Set(Math.Clamp(shown.GetValueOrDefault(name), 0, Math.Max(bar.Quanta, 0)));
    }

    /// <summary>
    /// Whether a box or bar was touched — which is what makes OK read them back. It is
    /// cleared when the **Display** screen opens, not the Advanced one: the Advanced screen
    /// only keeps what it was on the way in (<see cref="madeCustomAtOpen"/>),
    /// and Escape and an arrow put that back. Cancel pops and keeps
    /// the touch, so a second visit's OK, untouched, still reads the bars — the Custom preset they
    /// were refilled from (until then Vitomir cleared the flag at every Advanced open).
    /// </summary>
    bool madeCustom;

    /// <summary>The touched flag as the Advanced screen opened.</summary>
    bool madeCustomAtOpen;

    /// <summary>
    /// The Advanced screen: the driver box's three lines — device, driver, texture memory
    /// (`UI_DISPLAY_DEVICENAME`, `UI_DISPLAY_DRIVER`, `UI_DISPLAY_TOTALRAM` … `UI_DISPLAY_BYTES`) —
    /// the detail level's name, and every box and bar as the settings have them.
    /// </summary>
    public void OpenAdvanced()
    {
        var pane = menus.Pane(AdvancedFile);
        madeCustomAtOpen = madeCustom;                              // kept for a Cancel
        pane.Texts["opdaDetailString"] = Text(DetailNames[detailShown]);
        var info = pane.Lists["opdaDriverInfo"];
        info.Clear();
        var (device, driver, memory) = Driver?.Invoke() ?? ("", "", 0);
        info.Add($"{Text("UI_DISPLAY_DEVICENAME")} {device}", 0);
        info.Add($"{Text("UI_DISPLAY_DRIVER")} {driver}", 1);
        info.Add($"{Text("UI_DISPLAY_TOTALRAM")} {memory} {Text("UI_DISPLAY_BYTES")}", 2);
        info.Select(0);
        // The show hook: every box and bar from the preset of the level shown.
        FillAdvanced(pane, detailShown);
        Push(AdvancedFile);
    }

    /// <summary>Any box or bar touched on the Advanced screen (hooked to all three kinds): the level becomes Custom.</summary>
    void MakeCustom(MenuPane pane)
    {
        madeCustom = true;
        detailShown = 4;
        pane.Texts["opdaDetailString"] = Text(DetailNames[detailShown]);
    }

    /// <summary>A quantized bar stepped on the Advanced screen — a drag counts as a touch (ours: the hook is on the control kind).</summary>
    public void Stepped(MenuPane pane, string bar)
    {
        if (pane.File.Equals(AdvancedFile, StringComparison.OrdinalIgnoreCase)) MakeCustom(pane);
    }

    /// <summary>
    /// OK: only when something was touched are the boxes and bars read
    /// back; then the layer popped. The Display screen's detail name follows.
    /// </summary>
    void AdvancedOk(MenuPane pane)
    {
        if (madeCustom)
        {
            foreach (var box in AdvancedBoxes) Options.Advanced[box] = pane.Checks.GetValueOrDefault(box) ? 1 : 0;
            foreach (var name in AdvancedBars)
                if (pane.Bars.TryGetValue(name, out var bar)) Options.Advanced[name] = bar.Value;
            menus.Pane(DisplayFile).Texts["opdisDetailString"] = Text(DetailNames[detailShown]);
        }
        Pop();
    }

    // ---- Sound ----------------------------------------------------------------------

    /// <summary>
    /// The Sound screen (and its show hook): the providers listed with the one in
    /// use chosen, the speaker radio for the mixer's type, and both bars 0..100 at the
    /// volume × 100.
    /// </summary>
    public void OpenSound()
    {
        var pane = menus.Pane(SoundFile);
        var list = pane.Lists["opso3dProviderList"];
        list.Clear();
        var (names, current) = Providers?.Invoke() ?? ([], -1);
        for (int i = 0; i < names.Count; i++) list.Add(names[i], i);
        if (current >= 0) list.Select(current);
        pane.Check(SpeakerBoxes[Math.Clamp(Options.SpeakerType, 0, 3)], true);
        var music = pane.Bars["opsoMusicScroll"];
        music.Range(0, 100);
        music.Set((int)(Options.MusicVolume * 100f));
        var sound = pane.Bars["opsoSoundScroll"];
        sound.Range(0, 100);
        sound.Set((int)(Options.EffectsVolume * 100f));
        Push(SoundFile);
    }

    /// <summary>Exit: the layer popped. The volumes took effect as they moved; Vitomir writes them now (1.1 at shut-down).</summary>
    void SoundExit()
    {
        Save();
        Pop();
    }

    /// <summary>The value of a bar as text, for the harness.</summary>
    public static string Describe(GameOptions o) => string.Create(CultureInfo.InvariantCulture,
        $"flags 0x{o.Flags:x}, ai 0x{o.Ai:x}, gamma {o.Gamma:0.0#}, music {o.MusicVolume:0.##}, effects {o.EffectsVolume:0.##}, speakers {o.SpeakerType}, detail {o.DetailLevel}");
}
