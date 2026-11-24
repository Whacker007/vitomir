// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a `.nui` file can do with the keyboard ("In v1.1 it just changed the status
// bar") to see whether a pane's data could bind a key.
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The one keyboard attribute a <c>.nui</c> control has: <c>key</c>** (among the common
/// attributes the control parser compares). Its value is read with <c>%i</c>; zero leaves the
/// control as it was, anything else hands it to the ring's add, which puts it in its pane's **focus ring** — a circle kept
/// on the pane, linked both ways, each control once (flag 0x100000).
/// An edit field joins the same ring by itself as it is made, so the ring is the pane's edit
/// fields and <c>key</c> controls **in the order the file declares them**, and showing the pane gives the ring's
/// first the keyboard. It is **not a shortcut**: nothing about a key is stored, and
/// the ring only decides who gets **typed characters** — <c>WM_CHAR</c>, which the window procedure
/// hands to the interface as event 9, for the control with the keyboard or else the input pane's first. An edit field types it
/// (Tab and Shift+Tab walk the ring, <see cref="MenuPane.Tab"/>); every other control class has the base
/// handler, which passes the character to the game's event-9 binding — and 1.1 binds event 9
/// only on edit fields (the console, the create screen's name, the network status bar's chat). So
/// a <c>key</c> button would hold the keyboard, swallow what is typed, and keep it (a
/// Tab it does not understand either).
/// <para>
/// **No shipped pane uses it** — none of the 69 <c>ui/*.nui</c> files has a <c>key</c> attribute — and no key
/// without a character (F1–F12, the arrows) reaches a control at all. **1.1's keys are all fixed**: the
/// DirectInput buffer fills the key-state array by scan code, and every reader tests a fixed
/// entry; the only other key paths are <c>WM_CHAR</c> above and the window procedure's own messages. They are not
/// rebindable in 1.1 (no key page among the Options, <c>optionsControls.nui</c>; no key section in
/// <c>masquerade.ini</c>); Vitomir's <c>[keys]</c> and its key page are ours (<see cref="KeyBindings"/>).
/// </para>
/// </summary>
public static class NuiKeys
{
    /// <summary>The attribute.</summary>
    public const string Attribute = "key";

    /// <summary>
    /// Whether a control's <c>key</c> puts it in the focus ring: present and nonzero read as <c>%i</c>. (A
    /// value <c>%i</c> cannot read makes the parser answer 0 for the line, which drops the control —
    /// 1.1's; <see cref="NuiPane"/> keeps such a control, ours.)
    /// </summary>
    public static bool Keyed(NuiElement e) => e.Get(Attribute) is { } v && NuiPane.CInt(v) != 0;

    /// <summary>Whether a control is in its pane's focus ring: an edit field, or a <see cref="Keyed"/> control.</summary>
    public static bool InRing(NuiElement e) => e.Type == "edit" || Keyed(e);

    /// <summary>The focus ring, in the file's order (each control joins as its line is read and the ring's add appends it).</summary>
    public static IEnumerable<NuiElement> Ring(NuiPane pane) => pane.Elements.Where(InRing);

    /// <summary>The controls a pane's data gives the keyboard to that are not edit fields — none in any shipped pane.</summary>
    public static IEnumerable<NuiElement> KeyedControls(NuiPane pane) => pane.Elements.Where(e => e.Type != "edit" && Keyed(e));
}
