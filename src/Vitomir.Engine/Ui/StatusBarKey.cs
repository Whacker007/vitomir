// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The status bar's key: in play, F10 is the one that minimizes the bar,
// but after an Alt+Tab it becomes F11.
namespace Vitomir.Engine.Ui;

/// <summary>
/// **The status bar's key**: the key bindings' <see cref="KeyCommand.StatusBar"/>, 1.1's **F11** (the
/// manual's "Toggle Status Bar Display"). The key handling tests F11's entry of the key-state array —
/// the frame <c>DIK_F11</c> (0x57) went down — against the frame counter;
/// on the frame it goes down it plays the interface sound 0 and hands the
/// status bar's pane to the toggle, which hides it when it is shown and shows it otherwise.
/// Nothing else in 1.1 touches the bar from the keyboard.
/// <para>
/// **Where it is read** is where the key handling runs: the game camera alone (the input calls it for camera mode 1
/// and no other — not a cutscene, a camera path, the first-person look or the free camera), with no
/// dialog up, and not while the vault, the advancement screen, the chat line or a shop is up (its
/// head returns at once). And **once a press**: the key-state array records the frame a key went down, so
/// a key held down is one toggle — DirectInput sends no repeats.
/// </para>
/// <para>
/// **What the toggle does with the pane** (<see cref="Toggle"/>): it hides the bar only when the bar is
/// shown **and in front** — its list is the first list's
/// top layer or the on-top list. Otherwise it calls the pane's show, whose whole
/// body is under "not shown": a bar shown on a layer below does **not** come to the front — nothing happens
/// but the sound — and a hidden bar goes onto whatever layer is on top. In play the key is only read with
/// the bar in front (see <see cref="Toggle"/>), so that arm is 1.1's structure more than anything a player meets.
/// </para>
/// <para>
/// **F10 is nobody's in 1.1** (and checked again for *"In v1.1 it just changed the
/// status bar"*): nothing in 1.1 answers <c>DIK_F10</c> — the keys are
/// fixed, the same wherever the input fills and clears them, so every key
/// 1.1 answers is fixed; no <c>.nui</c> can bind one (<see cref="NuiKeys"/>: the parser's
/// one keyboard attribute, <c>key</c>, puts a control in its pane's focus ring for typed characters, and F10
/// types none); and the window procedure hands F10's <c>WM_SYSKEYDOWN</c> and the menu command it
/// becomes (<c>SC_KEYMENU</c>) to Windows, which has no menu to open on 1.1's <c>WS_POPUP</c> window
/// (<c>CreateWindowExA</c>, style 0x80000000). So 1.1 does nothing at all on F10, and neither does
/// Vitomir; the harness's vault that was there is the console's <c>vault</c> now (<see cref="HarnessKeys"/>).
/// </para>
/// </summary>
public static class StatusBarKey
{
    /// <summary>The interface sound the key plays.</summary>
    public const int Sound = 0;

    /// <summary>1.1's key: <c>DIK_F11</c>, the entry the key handling tests.</summary>
    public const int Dik = 0x57;

    /// <summary>
    /// Whether a press of the status bar's key does anything here: not a repeat, the game camera, no
    /// dialog, and none of the vault, the advancement screen, a shop or the chat line.
    /// </summary>
    public static bool Reads(bool repeat, bool gameCamera, bool dialog = false, bool vault = false,
                             bool advancement = false, bool shop = false, bool chat = false) =>
        !repeat && gameCamera && !dialog && !vault && !advancement && !shop && !chat;

    /// <summary>What the toggle does to the status bar's pane on the key.</summary>
    public enum BarToggle
    {
        /// <summary>Shown and in front: hidden.</summary>
        Hide,
        /// <summary>Hidden: shown, onto the layer on top.</summary>
        Show,
        /// <summary>Shown but on a layer below the top one: the show on a shown pane, which returns at once.</summary>
        Nothing,
    }

    /// <summary>
    /// The toggle on the status bar's pane: hide it when it is shown and in front, else the show — which
    /// shows a hidden pane and leaves a shown one exactly as it is, in front or not. **When the bar can be shown
    /// but not in front with the key read**: the key hook runs from the input only with no pane taking the input
    /// in state 3 (the game), and from the bar's own hook only when the bar is the
    /// input pane — on the top layer; every layer 1.1 pushes in play (the map, the advancement screen,
    /// the Options) goes with state 4 or a pane that has the input and a key hook of its own
    /// (the map's). So in play it is always in front when the key is read, and the third arm is 1.1's
    /// structure, kept (Vitomir asks its menus' layers, <c>FrontEnd.Layers</c>).
    /// </summary>
    public static BarToggle Toggle(bool shown, bool inFront) =>
        shown && inFront ? BarToggle.Hide : shown ? BarToggle.Nothing : BarToggle.Show;

    /// <summary>
    /// Why a press was not read (ours, for the console's line, as the Pause key's `not read here`): the
    /// camera it came in, or what was up; null when it is read.
    /// </summary>
    public static string? NotRead(string camera, bool repeat, bool gameCamera, bool dialog = false, bool vault = false,
                                  bool advancement = false, bool shop = false, bool chat = false) =>
        Reads(repeat, gameCamera, dialog, vault, advancement, shop, chat) ? null
        : repeat ? "a repeat"
        : !gameCamera ? $"the {camera} camera"
        : dialog ? "a dialog up" : vault ? "the vault up" : advancement ? "the advancement screen up"
        : shop ? "a shop up" : "the chat line open";
}
