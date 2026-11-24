// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Platform.Control;
using Vitomir.Platform.Interface;
using static SDL.SDL3;

namespace Vitomir.Platform.Host;

/// <summary>What the menus ask of the program after a frame's events.</summary>
public enum MenuRequest { None, Resume, Quit }

/// <summary>
/// The program's menus (ours): Escape (the action <c>menu</c>) opens the pause menu — Resume, Controls,
/// Quit — over the picture; Controls opens the Controls page (<see cref="ControlsPage"/>) over it, and its OK or Cancel
/// comes back to the pause menu. While a menu is open the program steps no ticks, frees and shows the pointer, and
/// the window's events come here instead of to the input: the pointer (in the window's coordinates, turned into the
/// picture's pixels), the keys the screens answer, and — while a key cell waits — the next key, mouse button or
/// wheel turn, which is bound there. OK hands the page's values to <see cref="Accepted"/>.
/// </summary>
public sealed class Menus
{
    readonly ScreenLoader loader;
    readonly PlatformSettings settings;
    readonly DrawList drawList = new();
    Screen? pause;
    ControlsPage? page;

    public Menus(PlatformSettings settings, Action<string>? say = null)
    {
        this.settings = settings;
        loader = new ScreenLoader(say);
    }

    /// <summary>Whether a menu is open (the simulation waits).</summary>
    public bool Open => pause is not null;

    /// <summary>The Controls page while it is open.</summary>
    public ControlsPage? Page => page;

    /// <summary>The screen on top: the Controls page's, else the pause menu's; null when none is open.</summary>
    public Screen? Top => page?.Screen ?? pause;

    /// <summary>Called with the page's values when the player presses OK.</summary>
    public Action<ControlValues>? Accepted { get; set; }

    /// <summary>A line on the Controls page about the view's settings (when they take effect).</summary>
    public string ViewNote { get; set; } = "";

    /// <summary>The bindings in force, so the <c>menu</c> action's own key (when it is not Escape) closes the pause menu too.</summary>
    public BindingTable? Table { get; set; }

    public void OpenPause()
    {
        pause = loader.LoadEmbedded("pause");
        page = null;
    }

    public void OpenControls()
    {
        pause ??= loader.LoadEmbedded("pause");
        page = new ControlsPage(loader, Values(settings), Values(new PlatformSettings()), SdlKeys.Code, SdlKeys.Name, ViewNote);
    }

    public void Close()
    {
        pause = null;
        page = null;
    }

    /// <summary>The Controls page's values from the settings.</summary>
    public static ControlValues Values(PlatformSettings s) => new(s.Bindings, s.MouseSensitivity, s.InvertY, s.ThirdPersonFacing, s.AlwaysRun,
                                                                  s.HeadMotion, s.FieldOfView, s.FieldOfViewAxis, s.FieldOfViewHorizontal,
                                                                  s.DoubleTapDodge, PlatformSettings.TapWindow(s.DoubleTapSeconds),
                                                                  Math.Clamp(s.SwingArcOpacity, 0f, 100f),
                                                                  // Melee framing, the crosshair in melee, the combat log, the attack
                                                                  // direction (read by its name), the two camera settings
                                                                  s.ThirdPersonMeleeFraming, Math.Clamp(s.MeleeReticleOpacity, 0f, 100f), s.ShowCombatLog, s.CombatLogCorner,
                                                                  s.Named(AttackDirectionSetting) ?? ControlValues.DefaultAttackDirection,   // the keys by default
                                                                  Math.Clamp(s.FirstPersonActionSway, 0f, 100f), Math.Clamp(s.ThirdPersonFollow, 0f, 0.3f));

    /// <summary>Ce-direction's setting for how a blow's direction is chosen (<c>automatic</c>, <c>mouse</c>, <c>keys</c>), read and written by its name.</summary>
    public const string AttackDirectionSetting = "attackDirection";

    /// <summary>The page's values into the settings.</summary>
    public static void Store(ControlValues v, PlatformSettings s)
    {
        s.Bindings = v.Copy().Bindings;
        s.MouseSensitivity = v.MouseSensitivity;
        s.InvertY = v.InvertY;
        s.ThirdPersonFacing = v.Facing;
        s.AlwaysRun = v.AlwaysRun;
        s.HeadMotion = v.HeadMotion;
        s.FieldOfView = v.FieldOfView;
        s.FieldOfViewAxis = v.FieldOfViewAxis;     // the field up the picture or across it
        s.FieldOfViewHorizontal = v.FieldOfViewHorizontal;
        s.DoubleTapDodge = v.DoubleTapDodge;       // double tap to dodge
        s.DoubleTapSeconds = PlatformSettings.TapWindow(v.DoubleTapSeconds);
        s.SwingArcOpacity = v.SwingArcOpacity;     // the swing's arc's opacity
        // The page's new rows.
        s.ThirdPersonMeleeFraming = v.MeleeFraming;
        s.MeleeReticleOpacity = v.MeleeReticleOpacity;
        s.ShowCombatLog = v.ShowCombatLog;
        s.CombatLogCorner = v.CombatLogCorner;
        s.SetNamed(AttackDirectionSetting, v.AttackDirection);
        s.FirstPersonActionSway = v.ActionSway;
        s.ThirdPersonFollow = v.ThirdPersonFollow;
    }

    // ---- the window's events ----

    /// <summary>
    /// One of the window's events while a menu is open. <paramref name="toPicture"/> turns the window's coordinates
    /// into the picture's pixels (the picture is fitted in the window).
    /// </summary>
    public void Take(in SDL_Event e, Func<float, float, (float X, float Y)> toPicture)
    {
        var top = Top;
        if (top is null) return;
        // While the page asks share or move, Escape answers Cancel (the key stays where it was), not closing the page.
        if (page?.Asking is not null && (SDL_EventType)e.type == SDL_EventType.SDL_EVENT_KEY_DOWN && e.key.scancode == SDL_Scancode.SDL_SCANCODE_ESCAPE)
        {
            if (!e.key.repeat) page.Answer(null);
            return;
        }
        // A key cell waiting takes the next key, button or wheel turn; Escape clears it.
        if (page?.Waiting is not null)
        {
            if ((SDL_EventType)e.type == SDL_EventType.SDL_EVENT_KEY_DOWN && e.key.scancode == SDL_Scancode.SDL_SCANCODE_ESCAPE)
            {
                if (!e.key.repeat) page.ClearWaiting();
                return;
            }
            if (InputCollector.SourceOf(e) is { } source)
            {
                page.Press(source);
                return;
            }
        }
        switch ((SDL_EventType)e.type)
        {
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
            {
                var (x, y) = toPicture(e.motion.x, e.motion.y);
                top.PointerMove(x, y);
                break;
            }
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN when e.button.button == BindingNames.SdlLeft:
            {
                var (x, y) = toPicture(e.button.x, e.button.y);
                top.PointerMove(x, y);
                top.PointerDown();
                break;
            }
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP when e.button.button == BindingNames.SdlLeft:
                top.PointerUp();
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
            {
                float turn = e.wheel.direction == SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED ? -e.wheel.y : e.wheel.y;
                if (turn != 0f) top.Wheel(turn);
                break;
            }
            case SDL_EventType.SDL_EVENT_KEY_DOWN:
                if (Key(e.key.scancode, (e.key.mod & SDL_Keymod.SDL_KMOD_SHIFT) != 0) is { } key)
                {
                    // A held key repeats to move the focus on, never to back out or to work a piece again.
                    if (!e.key.repeat || key is not (UiKey.Escape or UiKey.Enter)) top.Key(key);
                }
                // The menu's own key, when the player gave it another than Escape, closes the pause menu as it opened it.
                else if (!e.key.repeat && page is null && Table is not null && Table.ActionsOf(InputSource.Key((int)e.key.scancode)).Contains("menu"))   // a shared key works menu too
                    top.Key(UiKey.Escape);
                break;
        }
    }

    static UiKey? Key(SDL_Scancode code, bool shift) => code switch
    {
        SDL_Scancode.SDL_SCANCODE_UP => UiKey.Up,
        SDL_Scancode.SDL_SCANCODE_DOWN => UiKey.Down,
        SDL_Scancode.SDL_SCANCODE_LEFT => UiKey.Left,
        SDL_Scancode.SDL_SCANCODE_RIGHT => UiKey.Right,
        SDL_Scancode.SDL_SCANCODE_TAB => shift ? UiKey.BackTab : UiKey.Tab,
        SDL_Scancode.SDL_SCANCODE_RETURN or SDL_Scancode.SDL_SCANCODE_KP_ENTER => UiKey.Enter,
        SDL_Scancode.SDL_SCANCODE_ESCAPE => UiKey.Escape,
        _ => null,
    };

    /// <summary>Answers what the screens asked for since the last call; says what the program must do.</summary>
    public MenuRequest Update()
    {
        var request = MenuRequest.None;
        if (page is not null)
        {
            foreach (var a in page.Screen.TakeActions()) page.Handle(a);
            if (page.Outcome == PageOutcome.Accepted) Accepted?.Invoke(page.Values);
            if (page.Outcome != PageOutcome.Open)
            {
                page = null;
                pause?.SetFocus(pause.Find("controls"));
            }
        }
        else if (pause is not null)
        {
            foreach (var a in pause.TakeActions())
                switch (a.Name)
                {
                    case "resume": request = MenuRequest.Resume; break;
                    case "quit": request = MenuRequest.Quit; break;
                    case "controls": OpenControls(); break;
                }
        }
        return request;
    }

    /// <summary>The open screen laid out for a picture of <paramref name="width"/> × <paramref name="height"/> and drawn; null when none is open.</summary>
    public DrawList? Draw(int width, int height)
    {
        var top = Top;
        if (top is null || width <= 0 || height <= 0) return null;
        top.Layout(new UiMetrics(width, height, settings.InterfaceScale));
        top.Draw(drawList);
        return drawList;
    }
}
