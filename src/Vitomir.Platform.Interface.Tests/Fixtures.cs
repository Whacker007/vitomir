// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Control;

namespace Vitomir.Platform.Interface.Tests;

/// <summary>What the tests share: a stand-in for SDL's key names, and the program's default controls.</summary>
internal static class Fixtures
{
    // A stand-in for SDL's key names: SDL's own scancodes for these keys.
    static readonly Dictionary<string, int> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 4, ["C"] = 6, ["D"] = 7, ["E"] = 8, ["Q"] = 20, ["S"] = 22, ["V"] = 25, ["W"] = 26, ["X"] = 27,
        ["F"] = 9, ["G"] = 10, ["1"] = 30, ["2"] = 31, ["3"] = 32, ["4"] = 33, ["5"] = 34, ["6"] = 35, ["7"] = 36, ["8"] = 37,
        ["Escape"] = 41, ["Tab"] = 43, ["Space"] = 44, ["CapsLock"] = 57, ["F12"] = 69,
        ["Left Ctrl"] = 224, ["Left Shift"] = 225, ["Left Alt"] = 226, ["Right Alt"] = 230,
    };

    public static int? Code(string name) => Keys.TryGetValue(name.Trim(), out var c) ? c : null;
    public static string Name(int code) => Keys.FirstOrDefault(k => k.Value == code).Key ?? $"key {code}";

    /// <summary>The program's default controls (PlatformSettings's).</summary>
    public static ControlValues Defaults() => new(new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["forward"] = ["W"], ["back"] = ["S"], ["left"] = ["A"], ["right"] = ["D"],
        ["jump"] = ["Space"], ["sprint"] = ["Left Shift"], ["walk"] = ["Left Ctrl"], ["crouch"] = ["C"],
        ["use"] = ["E"], ["switchView"] = ["V"], ["freePointer"] = ["Left Alt"],     // no swapShoulder (retired)
        ["switchMode"] = [], ["attack"] = ["Mouse Left"], ["block"] = ["Mouse Right"], ["pointerClick"] = ["Mouse Left"],
        ["screenshot"] = ["F12"], ["menu"] = ["Escape"], ["masquerade"] = ["CapsLock"], ["cancel"] = ["C"], ["reach"] = [], ["awareness"] = [], ["hitVolumes"] = [],
        ["swingTrail"] = [],     // the swing trail's toggle, unbound
        ["swingArc"] = [],       // the swing's arc's toggle, unbound
        ["combatLog"] = [],      // the combat log's toggle, unbound
        ["crouchToggle"] = [], ["walkToggle"] = [], ["freePointerToggle"] = [],     // the toggled halves, unbound
        // The quick slots on 1 to 8, the HUD's toggle unbound.
        ["slot1"] = ["1"], ["slot2"] = ["2"], ["slot3"] = ["3"], ["slot4"] = ["4"], ["slot5"] = ["5"], ["slot6"] = ["6"], ["slot7"] = ["7"], ["slot8"] = ["8"],
        ["hud"] = [],
        ["dodge"] = ["Mouse 4"],     // Dodge (the host's default; Mouse 4, was Q)
        ["freeLook"] = [], ["freeLookToggle"] = [],     // free look, held and toggled, unbound
        ["swapWeapon"] = ["F"], ["aim"] = ["Q"],     // the sword and the bow swapped on F; the bow's aim held (on Q; the host's defaults)
        ["aimToggle"] = [], ["showImpact"] = [], ["showPath"] = [],     // aim's toggle and the bow's two aids, unbound
        ["orbitCamera"] = ["Right Alt"],     // the orbit camera, held (the host's default)
        ["stealth"] = [],     // stealth's toggle, unbound in the host's defaults (C is crouch's and cancel's there)
    }, 0.0022f, false, ThirdPersonFacing.Move);

    public static ControlsPage Page(ControlValues? current = null) =>
        new(new ScreenLoader(), current ?? Defaults(), Defaults(), Code, Name);

    public static ControlsPage LaidPage(int w, int h, float scale = 1f, ControlValues? current = null)
    {
        var p = Page(current);
        p.Screen.Layout(new UiMetrics(w, h, scale));
        return p;
    }

    /// <summary>The middle of a piece, in the picture's pixels.</summary>
    public static (float X, float Y) Middle(Screen s, Piece p)
    {
        var r = p.Rect;
        return ((r.X + r.Width / 2f) * s.Metrics.Pixel, (r.Y + r.Height / 2f) * s.Metrics.Pixel);
    }

    public static void Click(Screen s, Piece p)
    {
        var (x, y) = Middle(s, p);
        s.PointerMove(x, y);
        s.PointerDown();
        s.PointerUp();
    }
}
