// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The one translation point key remapping needs: every input event
// the game reads passes through here first, and a key or button bound to a command comes out as that
// command's 1.1 key — so the frame controller's handlers, which test 1.1's keys, run unchanged, and with
// the default bindings every event comes out exactly as it went in. Neutral events (the
// platform's `LoopInput`); SDL's half is the host's (`SdlInput`, `InputTape`'s live side).
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Platform.Modules;

namespace Vitomir;

/// <summary>
/// The binding layer (ours; on the platform's neutral events,: its SDL half is the
/// host's, <see cref="SdlInput"/> and the tape's live side). <see cref="Poll"/> is the game's loop's poll: a key or
/// button bound to a press command is handed on as the command's first
/// default chord (a key bound to <see cref="KeyCommand.Act"/> as a left click at the pointer, a mouse
/// button bound to the map as M), with the leftover modifiers kept; one of 1.1's keys whose command
/// has been moved away is swallowed; anything bound to nothing is handed on as it came, which is how
/// the harness's own keys keep working. <see cref="State"/> is the keyboard the game polls for the keys
/// it holds (the tape's, <see cref="InputTape.Keyboard"/>, with the bindings' held keys), and
/// <see cref="Modifiers"/> the click's modifier mask.
/// </summary>
static partial class KeyInput
{
    const int Keys = 512;
    static readonly bool[] state = new bool[Keys];
    static readonly Queue<LoopInput> pending = new();
    /// <summary>What each input held down was handed on as, for its release (let go of on a focus change, <see cref="LetGo"/>).</summary>
    static readonly HeldInputs<LoopInput> down = new();

    /// <summary>Whether the event being handled is a raw left or right click that only works the interface — its binding has moved (the left button always works the panes).</summary>
    public static bool InterfaceOnly { get; private set; }
    /// <summary>Whether the event being handled is a direct flag click — 1.1's middle button, its third binding — which is the world's alone (1.1 dispatches it to the world and nowhere else).</summary>
    public static bool WorldOnly { get; private set; }
    /// <summary>
    /// Whether the event being handled is the Storyteller's ground move (<see cref="KeyCommand.StoryMove"/>):
    /// handed on as 1.1's Alt and the right button, and read by the seat as the move rather than by the Alt bit,
    /// so the move goes where it is bound.
    /// </summary>
    public static bool StoryMove { get; private set; }
    /// <summary>The flags the event being handled carries whatever is held: a direct flag click's (<see cref="KeyCommand.SecondaryClick"/> and its fellows).</summary>
    static InputModifier forced;
    /// <summary>The modifiers a chord used up, which are not also the flags they would be held alone.</summary>
    static KeyMods consumed;

    /// <summary>The bindings in force: the player's, or 1.1's own with <c>[qol] keys</c> off.</summary>
    static KeyBindings Bindings => Qol.Current.Keys ? KeyBindings.Current : Faithful;
    static readonly KeyBindings Faithful = KeyBindings.Original();   // the keypad's pair the zoom's again, no quick keys

    /// <summary>
    /// The next event for the game, translated; false when there are no more this frame. A menu up,
    /// or a text field with the keyboard (the chat line, a name), gets every event as it came — and a key
    /// held into it and let go there is let go for the game after (<see cref="WorldRelease"/>).
    /// </summary>
    public static bool Poll(out LoopInput e, FrontEnd menus)
    {
        InterfaceOnly = false;
        WorldOnly = false;
        StoryMove = false;
        forced = InputModifier.None;
        consumed = KeyMods.None;
        while (true)
        {
            if (OwedRelease(out e)) return true;                 // a key let go under a menu, for the game (KeyInputMenu.cs)
            if (pending.TryDequeue(out e)) return true;
            if (!InputTape.Poll(out e)) return false;               // the replay harness's tape
            if (FullScreenKey(e, menus.Options?.Keys is { Capturing: true })) continue;   // Alt+Enter, anywhere (ours)
            if (menus.Up || InputTape.Live.TextInputActive) { UnderMenu(e); return true; }
            if (Translate(e)) return pending.TryDequeue(out e);
        }
    }

    /// <summary>
    /// The modifiers held: a key event's own (<paramref name="at"/>, the state when that key came, the
    /// system's state now is the state after every event this frame has queued, so a quick Shift+R let go within one
    /// frame read as R), else the tape's now; and the harness's held keys, which are neither.
    /// </summary>
    static KeyMods HeldMods(LoopMods? at = null)
    {
        var m = at ?? InputTape.Mods();
        var mods = KeyMods.None;
        if ((m & LoopMods.Shift) != 0) mods |= KeyMods.Shift;
        if ((m & LoopMods.Ctrl) != 0) mods |= KeyMods.Ctrl;
        if ((m & LoopMods.Alt) != 0) mods |= KeyMods.Alt;
        // The harness's held keys (`down:Left Alt`) are not in the system's own modifier state.
        if (HarnessHeld.Contains(KeyNames.LShift) || HarnessHeld.Contains(KeyNames.RShift)) mods |= KeyMods.Shift;
        if (HarnessHeld.Contains(KeyNames.LCtrl) || HarnessHeld.Contains(KeyNames.RCtrl)) mods |= KeyMods.Ctrl;
        if (HarnessHeld.Contains(KeyNames.LAlt) || HarnessHeld.Contains(KeyNames.RAlt)) mods |= KeyMods.Alt;
        return mods;
    }

    static LoopMods ToMods(KeyMods mods)
    {
        LoopMods m = 0;
        if ((mods & KeyMods.Shift) != 0) m |= LoopMods.LeftShift;
        if ((mods & KeyMods.Ctrl) != 0) m |= LoopMods.LeftCtrl;
        if ((mods & KeyMods.Alt) != 0) m |= LoopMods.LeftAlt;
        return m;
    }

    /// <summary>Whether an input is down now, a key or a mouse button.</summary>
    static bool IsDown(int input)
    {
        if (HarnessHeld.Contains(input)) return true;                  // a key or a button the harness holds
        if (input > KeyChord.MouseBase)
        {
            var buttons = InputTape.Buttons(out _, out _);
            int b = input - KeyChord.MouseBase;
            return b is > 0 and <= 32 && (buttons & (1u << (b - 1))) != 0;
        }
        if (HarnessHeld.Contains(input)) return true;
        var raw = InputTape.Keyboard();
        return input >= 0 && input < raw.Length && raw[input];
    }

    /// <summary>
    /// The commands pressed since a frame dump's log last took them (ours, <c>--frame-dump-log</c>): whatever keys the
    /// player has bound them to, so a clip can show the default keys of what was done.
    /// </summary>
    internal static HashSet<KeyCommand> Pressed { get; } = [];

    /// <summary>
    /// Keys the scripted-input harness holds down (<c>--ui-click</c>'s <c>down:</c> and <c>up:</c>,
    /// ours): a pushed event does not reach the system's keyboard state, so a held command would not see it.
    /// </summary>
    public static HashSet<int> HarnessHeld { get; } = [];

    /// <summary>One raw event into what the game hears, queued; false when it hears nothing of it.</summary>
    static bool Translate(in LoopInput e)
    {
        switch (e.Kind)
        {
            case LoopInputKind.KeyDown:
            {
                int input = Scan(e);
                // Escape is never bound: it always brings up the menu.
                if (input == KeyNames.Escape) { pending.Enqueue(e); return true; }
                if (e.Repeat && input != KeyNames.Pause)
                {
                    // A held key repeats as whatever it went down as, if that was a key.
                    if (down.TryGet(input, out var was))
                        foreach (var w in was)
                            if (w.Kind == LoopInputKind.KeyDown) pending.Enqueue(w with { Repeat = true });
                    return pending.Count > 0;
                }
                return Press(input, HeldMods(e.Mods), e);
            }
            case LoopInputKind.KeyUp:
                return Lift(Scan(e), e);
            case LoopInputKind.ButtonDown:
                return Press(KeyChord.MouseBase + e.Code, HeldMods(), e);
            case LoopInputKind.ButtonUp:
                return Lift(KeyChord.MouseBase + e.Code, e);
        }
        pending.Enqueue(e);
        return true;
    }

    /// <summary>
    /// The key's position, as the bindings keep keys — with **the Pause key** always the Pause key's
    /// (ours). 1.1 reads it as DirectInput's `DIK_PAUSE` (0xC5), which is the key
    /// whatever the keyboard; a platform may report it at another position (a laptop's Fn chord, a
    /// Break), so the key it names wins. And **it never repeats**: a PC keyboard sends its make and
    /// break codes together on the way down, so a Pause that comes as a repeat is one whose release
    /// the platform lost — the system still thinks it held — and it is a press all the same (see
    /// <see cref="Translate"/>, and <see cref="Capture"/> for the key page).
    /// </summary>
    internal static int Scan(in LoopInput e) =>
        e.Key == LoopKey.Pause ? KeyNames.Pause : e.Code;

    static bool Press(int input, KeyMods held, in LoopInput raw)
    {
        // While the free camera is up its chords are its own, polled by FreeCameraInput.
        var found = Bindings.Pressed(input, held, Qol.Current.Driving, Qol.Current.Camera, FreeCameraInput.Up);
        var made = new List<LoopInput>();
        bool acted = false;
        foreach (var (command, chord) in found)
        {
            Pressed.Add(command);                           // for a frame dump's log (ours, FrameDumpEntry.Pressed)
            // Ours with no 1.1 key (the swivel lock, the quick save and load): the host's to act on (KeyInputActs.cs).
            if (KeyBindings.IsActed(command)) { Act(command); acted = true; consumed |= chord.Mods; continue; }
            var info = KeyBindings.Of(command);
            if (info.Defaults.Length == 0 && info.Kind != CommandKind.Click) continue;
            // What the chord used up is not the flags too; what is held beyond it stays held.
            consumed |= chord.Mods;
            var leftover = held & ~chord.Mods;
            if (info.Kind == CommandKind.Click)
            {
                // The button and flags 1.1 hears (KeyBindings.ClickAs): the Storyteller's move is its Alt and right button.
                var (button, flags, worldOnly) = KeyBindings.ClickAs(command);
                forced |= flags;
                WorldOnly |= worldOnly;
                StoryMove |= command == KeyCommand.StoryMove;
                made.Add(Button(raw, button, pressed: true));
                continue;
            }
            var to = KeyBindings.HandedOn(command);         // the drag swivel as X
            // The leftovers go too, unless with them 1.1 would read another command (Shift+R for a moved regroup, KeyHandOn).
            made.Add(to.IsMouse ? Button(raw, to.Button, pressed: true) : Key(raw, to.Input, KeyHandOn.Mods(command, leftover), pressed: true));
        }
        bool leftRaw = input == KeyChord.MouseBase + 1, rightRaw = input == KeyChord.MouseBase + 3;
        if (made.Count == 0 && !acted)
        {
            // The left button always works the interface (and the right, which the panes read too):
            // when neither is bound to what it was, a click goes on for the panes alone.
            if ((leftRaw && !Bindings[KeyCommand.Act].Any(b => b.Input == input))
                || (rightRaw && !Bindings[KeyCommand.Cast].Any(b => b.Input == input)))
            {
                InterfaceOnly = true;
                made.Add(raw);
            }
            // One of 1.1's keys whose command has moved does nothing; anything else is the harness's.
            else if (!KeyBindings.AnyDefault(input)) made.Add(raw);
            else Swallowed(input, held, raw);          // said on the console
        }
        down.Press(input, made);
        foreach (var m in made) pending.Enqueue(m);
        return made.Count > 0;
    }

    static bool Lift(int input, in LoopInput raw)
    {
        if (!down.Lift(input, out var was)) { if (!KeyBindings.AnyDefault(input)) pending.Enqueue(raw); return pending.Count > 0; }
        Release(was, raw);
        return pending.Count > 0;
    }

    /// <summary>The releases of what an input was handed on as, queued (<paramref name="raw"/>: the release that came, or none on a focus change).</summary>
    static void Release(List<LoopInput> was, LoopInput? raw)
    {
        foreach (var w in was)
        {
            LoopInput up;
            if (w.Kind == LoopInputKind.KeyDown) up = w with { Kind = LoopInputKind.KeyUp, Repeat = false };
            else if (w.Kind == LoopInputKind.ButtonDown)
            {
                InputTape.Buttons(out float x, out float y);
                // Where the button came up, when it is a button that came up (a harness's pushed
                // release is not where the system's own pointer is, and a drop needs the release's place).
                up = raw is { Kind: LoopInputKind.ButtonUp } r ? w with { Kind = LoopInputKind.ButtonUp, X = r.X, Y = r.Y }
                                                               : w with { Kind = LoopInputKind.ButtonUp, X = x, Y = y };
            }
            else continue;
            pending.Enqueue(up);
        }
    }

    /// <summary>A key event for one of 1.1's keys: its scan code, the US keycode the handlers test, and the modifiers.</summary>
    static LoopInput Key(in LoopInput from, int scancode, KeyMods mods, bool pressed) =>
        new(pressed ? LoopInputKind.KeyDown : LoopInputKind.KeyUp)
        {
            TimeNs = from.TimeNs, Code = scancode, Key = UsKey(scancode), Mods = ToMods(mods),
        };

    /// <summary>A button event at the pointer.</summary>
    static LoopInput Button(in LoopInput from, int button, bool pressed)
    {
        if (from.IsButton && from.Code == button) return from;
        InputTape.Buttons(out float x, out float y);
        return new(pressed ? LoopInputKind.ButtonDown : LoopInputKind.ButtonUp) { TimeNs = from.TimeNs, Code = button, X = x, Y = y };
    }

    /// <summary>The key code a US keyboard gives a scan code (<see cref="LoopKey"/>), which is what the handlers were written against.</summary>
    internal static int UsKey(int scancode)
    {
        if (scancode is >= 4 and <= 29) return 'a' + scancode - 4;
        if (scancode is >= 30 and <= 38) return '1' + scancode - 30;
        return scancode switch
        {
            39 => '0',
            KeyNames.Return => LoopKey.Return,
            KeyNames.Escape => LoopKey.Escape,
            KeyNames.Backspace => LoopKey.Backspace,
            KeyNames.Tab => LoopKey.Tab,
            KeyNames.Space => LoopKey.Space,
            KeyNames.Minus => '-',
            KeyNames.EqualsKey => '=',
            KeyNames.LeftBracket => '[',
            KeyNames.RightBracket => ']',
            KeyNames.Backslash => '\\',
            KeyNames.Semicolon => ';',
            KeyNames.Apostrophe => '\'',
            KeyNames.Grave => LoopKey.Grave,
            KeyNames.Comma => ',',
            KeyNames.Period => '.',
            KeyNames.Slash => '/',
            _ => scancode | LoopKey.NoCharacter,   // every key that types nothing
        };
    }

    /// <summary>
    /// The keyboard as the game polls it: each of 1.1's keys for a command read while held (the
    /// camera's, first person, the modifier flags, driving) is down while any chord of that command
    /// is — so a turn moved to J holds the keypad's 4 down for the camera — and every other key is
    /// the keyboard's own, which is what the free camera and the harness read.
    /// </summary>
    public static bool[] State()
    {
        var raw = InputTape.Keyboard();
        int n = Math.Min(raw.Length, Keys);
        for (int i = 0; i < n; i++) state[i] = raw[i];
        var held = HeldMods();
        foreach (var info in KeyBindings.All)
        {
            if (info.Kind is not (CommandKind.Held or CommandKind.Flag) || !KeyBindings.HandsOn(info.Command)) continue;
            foreach (var d in info.Defaults)
                if (!d.IsMouse && d.Input < n) state[d.Input] = false;
        }
        foreach (var info in KeyBindings.All)
        {
            if (info.Kind is not (CommandKind.Held or CommandKind.Flag) || !KeyBindings.HandsOn(info.Command)) continue;
            if (!Bindings.Held(info.Command, IsDown, held)) continue;
            foreach (var d in info.Defaults)
                if (!d.IsMouse && d.Input < n) state[d.Input] = true;
        }
        return state;
    }

    /// <summary>Whether any chord of a command is held now: the left button for <see cref="KeyCommand.Act"/> unless it has moved.</summary>
    public static bool Held(KeyCommand command) => Bindings.Held(command, IsDown, HeldMods());

    /// <summary>Whether a click is held as that click: not the middle button while it is the camera's drag (<see cref="KeyBindings.HeldClick"/>).</summary>
    public static bool HeldClick(KeyCommand command) => Bindings.HeldClick(command, IsDown, HeldMods(), Qol.Current.Camera);

    /// <summary>
    /// The click's modifier mask from the flag commands held, less any a chord just used up (Shift+T bound to
    /// act is an act, not a secondary one), plus what a direct flag click carries.
    /// </summary>
    public static InputModifier Modifiers()
    {
        var mask = Bindings.Mask(IsDown, HeldMods());
        foreach (var (c, flag) in KeyBindings.Flags)
            if (Bindings[c].All(b => KeyNames.IsModifier(b.Input) && Covers(consumed, b.Input))) mask &= ~flag;
        return mask | forced;
    }

    static bool Covers(KeyMods mods, int modifierKey) => modifierKey switch
    {
        KeyNames.LShift or KeyNames.RShift => (mods & KeyMods.Shift) != 0,
        KeyNames.LCtrl or KeyNames.RCtrl => (mods & KeyMods.Ctrl) != 0,
        KeyNames.LAlt or KeyNames.RAlt => (mods & KeyMods.Alt) != 0,
        _ => false,
    };

    // ---- the key page's wait for a key ------------------------------------------------------------

    static int modifierAlone = -1;
    static bool ateLeft;

    /// <summary>
    /// While the key page waits for a key, the menus' share of the events comes here first: a key
    /// (with the modifiers held), a modifier on its own when it is let go with nothing pressed
    /// beside it, or a button — the left one only off the page's own controls, so the page's
    /// buttons still work. Escape unmaps the waiting cell. True when the event was the page's.
    /// </summary>
    public static bool Capture(KeysPage page, FrontEnd menus, in LoopInput e)
    {
        switch (e.Kind)
        {
            case LoopInputKind.KeyDown:
            {
                int input = Scan(e);
                if (e.Repeat && input != KeyNames.Pause) return true;
                if (input == KeyNames.Escape) { modifierAlone = -1; page.Unmap(); return true; }      // unmaps the waiting cell
                if (KeyNames.IsModifier(input)) { modifierAlone = input; return true; }
                modifierAlone = -1;
                page.Capture(new KeyChord(input, HeldMods(e.Mods)));
                return true;
            }
            case LoopInputKind.KeyUp:
                if (Scan(e) == modifierAlone) page.Capture(new KeyChord(modifierAlone));
                modifierAlone = -1;
                return true;
            case LoopInputKind.Text:
                return true;
            case LoopInputKind.ButtonDown:
                if (e.Code == 1 && page.Pane.ControlAt(menus.PointerX, menus.PointerY) is not null) return false;
                ateLeft = e.Code == 1;
                modifierAlone = -1;
                page.Capture(KeyChord.Mouse(e.Code, HeldMods()));
                return true;
            case LoopInputKind.ButtonUp when e.Code == 1 && ateLeft:
                ateLeft = false;
                return true;
        }
        return false;
    }
}
