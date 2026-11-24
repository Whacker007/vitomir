// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// 1.1's front-end menus in our lettering (ours): the main menu, single player, load and save, the
// escape menus, the message and input boxes, the credits, the multiplayer menus and the loading plaques. A button's
// word is found where 1.1 draws it — in the button's own pictures, or in the tiles under a button that has only a
// rollover — taken off, and written again in ours with its ink's middle where 1.1's was; the panes' other words are
// labels (MenuLetteringScreens.cs).
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

public static partial class MenuLettering
{
    /// <summary>
    /// One of 1.1's buttons and the word on it, in one line or two (<paramref name="Second"/>): *create* over *game*, as
    /// 1.1 sets its two-word buttons in the bottom row.
    /// </summary>
    public readonly record struct ButtonWord(string Button, string Word, string? Second = null);

    /// <summary>Where a word was found on 1.1's art: its ink's box, in the picture's columns and rows (top row first).</summary>
    public readonly record struct Found(string Button, int Left, int Right, int Top, int Bottom)
    {
        public float X => (Left + Right + 1) / 2f;
        public float Middle => UiAssets.Height - (Top + Bottom + 1) / 2f;
        public int Height => Bottom - Top + 1;
    }

    /// <summary>
    /// **1.1's button words taken off and ours put in their places.** For each button: its word found where its
    /// picture differs from its rollover, or (a button with only a rollover, its word in the tiles) where the rollover
    /// differs from the tiles; the picture laid on the tiles; every word, and a little round it
    /// (<paramref name="grow"/>), filled from round it (<paramref name="upright"/>: how much the fill leans on the pixels
    /// above and below); the button left without pictures; and our word set with its ink's middle on 1.1's, at
    /// <paramref name="em"/> — or, with none given, at the em that makes our words as tall as 1.1's (the middle of the
    /// pane's words, so one pane's words are one size). Answers where each was found.
    /// </summary>
    static List<Found> ButtonWords(UiAssets assets, LiftedArt art, NuiPane nui, IReadOnlyList<ButtonWord> words, string style,
                                   float? em = null, int grow = 4, float upright = 1f)
    {
        var letters = art.Mask();
        var found = new List<(ButtonWord Word, NuiElement Button, Found At)>();
        foreach (var w in words)
        {
            if (Find(nui, w.Button) is not { } e || e.Pos.Length < 2 || e.Size.Length < 2) continue;
            int x = art.OriginX + e.Pos[0], y = art.OriginY + e.Pos[1], bw = e.Size[0], bh = e.Size[1];
            // `one`: everything to take off (the picture's words where it differs from the tiles, and the letters its
            // rollover lights); `lit`: the letters alone, which say where the word is.
            var one = art.Mask();
            var lit = art.Mask();
            var normal = e.Get("bitmap") is { } nb ? assets.Texture(nb) : null;
            var over = e.Get("rollover") is { } ob ? assets.Texture(ob) : null;
            if (normal is not null)
            {
                art.DifferFromArt(one, normal, x, y, bw, bh);
                art.Paste(normal, x, y, bw, bh);
                if (over is not null) art.Differ(lit, normal, over, x, y, bw, bh);
                else lit = (bool[])one.Clone();
            }
            else if (over is not null) art.DifferFromArt(lit, over, x, y, bw, bh);
            for (int i = 0; i < one.Length; i++) one[i] |= lit[i];
            if ((Box(art, lit, w.Button) ?? Box(art, one, w.Button)) is { } at)
            {
                found.Add((w, e, at));
                for (int i = 0; i < one.Length; i++) letters[i] |= one[i];
            }
        }
        art.Fill(art.Grown(letters, grow), upright, 200);
        float size = em ?? Fitted(found.Select(f => (f.Word.Word, f.At.Height)));
        foreach (var (w, e, at) in found)
        {
            Replace(nui, e, Changed(e, Pictures));
            float x = at.X - art.OriginX, middle = at.Middle - art.OriginY;
            if (w.Second is null)
                nui.Elements.Add(Boxed(w.Button + "words", w.Word, x, middle, style, size, w.Button));
            else
            {
                // Two lines as 1.1 sets them: 14 apart, the pair's middle on 1.1's (the Display screen's advanced options).
                nui.Elements.Add(Words(w.Button + "words", w.Word, x, middle + TwoLineRise, style, size, w.Button));
                nui.Elements.Add(Words(w.Button + "words2", w.Second, x, middle + TwoLineRise - TwoLinePitch, style, size, w.Button));
            }
        }
        return [.. found.Select(f => f.At)];
    }

    /// <summary>A two-line word's first baseline over the pair's middle, and the step down to the second.</summary>
    public const float TwoLineRise = 3.5f, TwoLinePitch = 14f;

    /// <summary>The box of a word's mask, or none.</summary>
    static Found? Box(LiftedArt art, bool[] mask, string button)
    {
        int l = int.MaxValue, r = -1, t = int.MaxValue, b = -1;
        for (int y = 0; y < art.Height; y++)
            for (int x = 0; x < art.Width; x++)
                if (mask[y * art.Width + x])
                {
                    l = Math.Min(l, x); r = Math.Max(r, x); t = Math.Min(t, y); b = Math.Max(b, y);
                }
        return r < 0 ? null : new Found(button, l, r, t, b);
    }

    /// <summary>
    /// The em that makes our words as tall as 1.1's: for each, 1.1's ink's height (less a unit for the edge its rollover
    /// lights) over our ink's at an em of one; the middle of them, so a pane's words are one size.
    /// </summary>
    static float Fitted(IEnumerable<(string Word, int Height)> words)
    {
        var ems = words.Select(w =>
        {
            var (bottom, top) = Ink(w.Word, 1f);
            return top - bottom > 0.1f ? (w.Height - 1f) / (top - bottom) : MenuEm;
        }).Order().ToList();
        return ems.Count == 0 ? MenuEm : ems[ems.Count / 2];
    }

    /// <summary>A title plate's words taken off (the plate on every Options sub-screen, the load and save screens and the multiplayer screens: rows 10 to 30) and ours set in it.</summary>
    static void PlateTitle(LiftedArt art, NuiPane nui, string name, string title, float x = 320f)
    {
        art.LabelOff(10, 30, 196, 444);
        nui.Elements.Add(Boxed(name, title, x, PlateCentre, TitleStyle, TitleEm));
    }

    /// <summary>
    /// The mark of a pane in our lettering that has no word of 1.1's to take off (the credits): a word with no text,
    /// which draws nothing and makes the pane's text ours (<see cref="MenuPane.Lettered"/>).
    /// </summary>
    static void Mark(NuiPane nui) => nui.Elements.Add(Words("lettered", "", 0f, 0f, MenuStyle, MenuEm));

    // ---- the menus with words in their windows -------------------------------------------------------------------

    /// <summary>The main menu's five (`mainMenu.nui`): its words are in the tiles and the buttons' pictures, on the ankh's glass.</summary>
    public static readonly ButtonWord[] MainMenuWords =
    [
        new("mmsingleplayerbutton", "single player"), new("mmmultiplayerbutton", "multiplayer"), new("mmoptionsbutton", "options"),
        new("mmcreditsbutton", "credits"), new("mmquitbutton", "quit"),
    ];

    /// <summary>The single-player menu's three (`sp.nui`).</summary>
    public static readonly ButtonWord[] SpWords =
        [new("spnewbutton", "new game"), new("sploadbutton", "load game"), new("spmainmenubutton", "main menu")];

    /// <summary>The multiplayer menu's three (`mpSelect.nui`): 1.1's LAN (the first) and Internet are Host Game and Join Game (ours).</summary>
    public static readonly ButtonWord[] MpSelectWords =
        [new("mpselectlan", "host game"), new("mpselectinternet", "join game"), new("mpselectmainmenu", "main menu")];

    /// <summary>The escape menu's five (`escapeMenu.nui`): its words are the pane's art, its buttons only rollovers.</summary>
    public static readonly ButtonWord[] EscapeWords =
    [
        new("emresumebutton", "resume"), new("emloadbutton", "load"), new("emsavebutton", "save"),
        new("emoptionsbutton", "options"), new("emquitbutton", "quit"),
    ];

    /// <summary>The multiplayer escape menu's five (`escapeMenuMP.nui`).</summary>
    public static readonly ButtonWord[] EscapeMpWords =
    [
        new("emresumebutton", "resume"), new("emmpsavebutton", "save"), new("emmprevivebutton", "revive"),
        new("emoptionsbutton", "options"), new("emquitbutton", "quit"),
    ];

    static void WindowMenu(UiAssets assets, NuiPane nui, IReadOnlyList<ButtonWord> words, string commit, float upright)
    {
        var art = LiftedArt.Of(assets, nui);
        ButtonWords(assets, art, nui, words, MenuStyle, grow: 10, upright: upright);
        art.Commit("lettered/" + commit);
    }

    // ---- the load and save screens -------------------------------------------------------------------------------

    static void LoadOrSave(UiAssets assets, NuiPane nui, string title, string commit, IReadOnlyList<ButtonWord> words)
    {
        var art = LiftedArt.Of(assets, nui);
        PlateTitle(art, nui, commit + "titlewords", title);
        ButtonWords(assets, art, nui, words, MenuStyle, em: ButtonEm, grow: 2);
        art.Commit("lettered/" + commit);
    }

    // ---- the message and input boxes -------------------------------------------------------------------------------

    /// <summary>A dialog's ok and cancel (`messageBox.nui`, `inputBox.nui`): small words in the buttons' own pictures, at the box's foot.</summary>
    static void Dialog(UiAssets assets, NuiPane nui, string ok, string cancel, string commit)
    {
        var art = LiftedArt.Of(assets, nui);
        ButtonWords(assets, art, nui, [new(ok, "ok"), new(cancel, "cancel")], MenuStyle, grow: 1);
        art.Commit("lettered/" + commit);
    }

    // ---- the multiplayer screens -------------------------------------------------------------------------------------

    /// <summary>`mpCreateGame.nui`'s labels: the game's name, its rules' four switches, its password, the chronicles.</summary>
    public static readonly Label[] CreateGameLabels =
    [
        new("game name", 136, 211, 48, 58), new("chronicles", 437, 503, 45, 56),
        new("cooperative play", 115, 221, 91, 104), new("track humanity", 119, 216, 107, 120),
        new("ST moderation", 122, 214, 124, 134), new("automatic storyteller", 107, 234, 139, 152),
        new("password protect", 116, 228, 182, 193), new("password", 79, 141, 208, 219), new("confirm", 83, 136, 224, 236),
    ];

    /// <summary>The character's six, on the multiplayer character screens' left (`mpPlayerCreate.nui`, `mpPlayerSelect.nui`).</summary>
    static readonly Label[] CharacterLabels =
    [
        new("name", 91, 130, 293, 301), new("type", 95, 126, 307, 319), new("clan", 97, 124, 323, 334),
        new("rank", 95, 127, 339, 350), new("generation", 77, 145, 355, 367), new("experience", 76, 145, 371, 382),
    ];

    /// <summary>`mpPlayerCreate.nui`'s labels: the kinds of character, the model, the clan, and the character's six.</summary>
    public static readonly Label[] PlayerCreateLabels =
    [
        new("type", 454, 485, 44, 56), new("human", 436, 481, 65, 75), new("ghoul", 438, 477, 80, 93),
        new("kindred", 432, 483, 97, 108), new("model", 448, 491, 119, 130), new("clan", 457, 484, 261, 272),
        .. CharacterLabels,
    ];

    /// <summary>`mpPlayerSelect.nui`'s labels: the player, and the character's six.</summary>
    public static readonly Label[] PlayerSelectLabels = [new("player", 447, 490, 46, 59), .. CharacterLabels];

    /// <summary>`mpChat.nui`'s labels: the room's players and chat, the game's state and the games to join.</summary>
    public static readonly Label[] ChatLabels =
    [
        new("players in room", 121, 222, 45, 58), new("game status", 430, 507, 47, 58),
        new("room chat", 140, 205, 172, 182), new("available games", 418, 520, 171, 184),
    ];

    static void MpCreateGame(UiAssets assets, NuiPane nui)
    {
        var art = LiftedArt.Of(assets, nui);
        MostPlayersOption.Row(art);                                  // the password panel a row taller for the most players (ours)
        PlateTitle(art, nui, "mpcgtitlewords", "game setup");      // ours: the host's Game Setup
        Relabel(art, nui, "mpcg", [.. CreateGameLabels, MostPlayersOption.Words]);
        ButtonWords(assets, art, nui,
                    [new("mpcgcancelbutton", "cancel"), new("mpcgcreategamebutton", "ok"), new("mpcgloadsavedbutton", "load", "saved")],
                    MenuStyle, em: ButtonEm, grow: 2);
        art.Commit("lettered/mpcreategame");
    }

    static void MpPlayerCreate(UiAssets assets, NuiPane nui)
    {
        var art = LiftedArt.Of(assets, nui);
        PlateTitle(art, nui, "mppctitlewords", "character creation");
        Relabel(art, nui, "mppc", PlayerCreateLabels);
        ButtonWords(assets, art, nui, [new("mppccreatebutton", "create"), new("mppccancelbutton", "cancel")], MenuStyle, em: ButtonEm, grow: 2);
        art.Commit("lettered/mpplayercreate");
    }

    static void MpPlayerSelect(UiAssets assets, NuiPane nui)
    {
        var art = LiftedArt.Of(assets, nui);
        PlateTitle(art, nui, "mppstitlewords", "character selection");
        Relabel(art, nui, "mpps", PlayerSelectLabels);
        ButtonWords(assets, art, nui, [new("mppscreatenewbutton", "create new", "player"), new("mppsokbutton", "ok")], MenuStyle, em: ButtonEm, grow: 2);
        art.Commit("lettered/mpplayerselect");
    }

    static void MpChat(UiAssets assets, NuiPane nui)
    {
        var art = LiftedArt.Of(assets, nui);
        Relabel(art, nui, "mpchat", ChatLabels);
        ButtonWords(assets, art, nui,
        [
            // Ours: WON's Change Room is By Address…, Log Out is Back, Create Game is the host's Game Setup.
            new("mpchatchangeroombutton", "by", "address"), new("mpchatlogoutbutton", "back"), new("mpchatjoingamebutton", "join", "game"),
            new("mpchatcreategamebutton", "game", "setup"), new("mpchatleavegamebutton", "leave", "game"), new("mpchatstartbutton", "start"),
            new("mpchatplayersetupbutton", "player", "setup"),
        ], MenuStyle, em: ButtonEm, grow: 2);
        art.Commit("lettered/mpchat");
    }

    // ---- the loading plaques ------------------------------------------------------------------------------------

    /// <summary>The loading plaque's word (`LoadScreen.nui`, 160 x 64 at 240,145), on the plaque's own rows.</summary>
    public static readonly Label LoadingLabel = new("loading", 44, 127, 439, 458, 19f, TitleStyle);
    /// <summary>The network game's plaque's word (`NetworkLoadScreen.nui`, the same plaque).</summary>
    public static readonly Label CommunicatingLabel = new("communicating", 21, 143, 444, 457, 12.5f, TitleStyle);

    static void Plaque(UiAssets assets, NuiPane nui, Label word, string commit)
    {
        var art = LiftedArt.Of(assets, nui);
        Relabel(art, nui, commit, [word]);
        art.Commit("lettered/" + commit);
    }
}
