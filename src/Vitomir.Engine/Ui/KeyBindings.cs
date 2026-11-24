// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Key remapping: a quality-of-life switch decided
//  and built. Nothing here is 1.1's: 1.1 tests every key where it is
// handled — the key-state array indexed by DirectInput scan code,
// the mouse's three bindings — and has no table to remap. What this file holds
// is that table, with 1.1's own keys as its defaults, so that the defaults reproduce 1.1 exactly — but
// the keypad's + and −, which are the quick save's and load's (`KeyBindingsOurs.cs`).
using System.Globalization;
using System.Text;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>The three keys a binding may be chorded with (ours). Space is an ordinary key.</summary>
[Flags]
public enum KeyMods
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

/// <summary>
/// One key or mouse button, alone or with Shift, Ctrl or Alt held (ours). A key is its USB HID usage
/// number — which is SDL's scan code, and like 1.1's DirectInput scan code names a *position* on
/// the keyboard rather than the letter printed on it — and a mouse button is
/// <see cref="MouseBase"/> plus SDL's button number (1 left, 2 middle, 3 right, 4 and 5 the side
/// buttons).
/// </summary>
public readonly record struct KeyChord(int Input, KeyMods Mods = KeyMods.None)
{
    /// <summary>Where mouse buttons start (ours), well clear of every scan code.</summary>
    public const int MouseBase = 0x1000;

    public static KeyChord Mouse(int button, KeyMods mods = KeyMods.None) => new(MouseBase + button, mods);
    public bool IsMouse => Input > MouseBase;
    public int Button => IsMouse ? Input - MouseBase : 0;

    /// <summary>The name a person reads and the ini holds: <c>Shift+R</c>, <c>Keypad 4</c>, <c>Left Click</c>.</summary>
    public override string ToString() => KeyNames.Format(this);
    /// <summary>The same, shortened to fit a cell of the key page (<c>Kp 4</c>, <c>L Shift</c>).</summary>
    public string Short => KeyNames.Format(this, brief: true);

    /// <summary>Reads what <see cref="ToString"/> writes, case-insensitively; null when it is not a key.</summary>
    public static KeyChord? Parse(string text)
    {
        var mods = KeyMods.None;
        string rest = text.Trim();
        while (true)
        {
            int plus = rest.IndexOf('+');
            // A lone "+" (or "Keypad +") is a key, not a separator.
            if (plus <= 0 || plus == rest.Length - 1) break;
            var head = rest[..plus].Trim();
            KeyMods m = head.ToLowerInvariant() switch { "shift" => KeyMods.Shift, "ctrl" or "control" => KeyMods.Ctrl, "alt" => KeyMods.Alt, _ => KeyMods.None };
            if (m == KeyMods.None) break;
            mods |= m;
            rest = rest[(plus + 1)..].Trim();
        }
        return KeyNames.Input(rest) is { } input ? new KeyChord(input, mods) : null;
    }
}

/// <summary>What a command is, which decides how its key is read.</summary>
public enum CommandKind
{
    /// <summary>A click at the pointer: the world's and the interface's binding 1 or 2.</summary>
    Click,
    /// <summary>Held while clicking: one of the four bits the mouse's input folds into the modifier mask.</summary>
    Flag,
    /// <summary>Read while held, every frame: the camera's keys, first person, driving.</summary>
    Held,
    /// <summary>Acts when its key goes down (and, for the swivel, again when it comes up).</summary>
    Press,
    /// <summary>
    /// The free camera's: read while held, and **only while the free camera is up**
    /// (camera mode 3). While it is, a chord one of them has is theirs and nothing else's, as 1.1's
    /// mode 3 reads no other key; while it is not, they are nothing and take no key
    /// from anybody — they are never handed on, swallowed or polled for the game.
    /// </summary>
    Fly,
}

/// <summary>Every command a key or button can be bound to (ours).</summary>
public enum KeyCommand
{
    // The mouse's own: its three bindings, the left, right and middle buttons, and three of ours.
    Act, Cast, SecondaryClick, PartyClick, AltClick, HoldClick,
    // The four modifier flags (`InputModifier`).
    Secondary, Party, Alt, Hold,
    // The camera.
    TurnLeft, TurnRight, TiltUp, TiltDown, ZoomOut, ZoomIn, FirstPerson, Swivel,
    // The conversation.
    SkipLine,
    // The coterie.
    Regroup, Ungroup, Group, StanceAttack, StanceNeutral, StanceDefense, Pause,
    // The panes.
    CharacterSheet, Inventory, Quests, Disciplines, Map, ClosePanes, StatusBar,
    // Who you steer.
    NextCharacter, Character1, Character2, Character3, Character4,
    // Quick items and disciplines.
    Quick1, Quick2, Quick3, Quick4, Quick5,
    Discipline1, Discipline2, Discipline3, Discipline4, Discipline5, Discipline6, Feed, Heal,
    // The rest.
    Screenshot, Console,
    // Driving: `[qol] driving`, ours.
    Forward, Back, StepLeft, StepRight, Run,
    // The camera's own: `[qol] camera`, ours; the swivel lock.
    DragSwivel, SwivelLock,
    // The quick save and load (ours).
    QuickSave, QuickLoad,
    // Held, every object on screen glows as under the pointer (ours, KeyBindingsShow.cs).
    ShowObjects,
    // Window and full screen, anywhere (ours, FullScreen.cs).
    FullScreen,
    // The Storyteller's: the seat's ground move, 1.1's Alt and the right button (the Storyteller's first case).
    StoryMove,
    // The free camera's own: its six moves, four turns, the roll and the level, and the developer's way in and out.
    FlyForward, FlyBack, FlyLeft, FlyRight, FlyUp, FlyDown, FlyTurnLeft, FlyTurnRight, FlyLookDown, FlyLookUp,
    FlyRollLeft, FlyRollRight, FlyLevel, FlyCamera,
    // The free camera's, ours and only with `[qol] driving`: the view levelled, and the middle button's look.
    FlyLevelView, FlyDrag,
}

/// <summary>
/// The key bindings (ours): up to <see cref="MaxBindings"/> chords a command, read from the
/// <c>[keys]</c> section of each <c>vitomir.ini</c> over 1.1's own keys, and written back by the key
/// page. Every command is remappable; **Escape is not a command** and always brings up the menu.
/// The engine's handlers still see 1.1's keys and modifier flags: the host turns a chord into the
/// command's first default and hands that on (<c>src/Vitomir.Classic/Input/KeyInput.cs</c>), so with the defaults
/// nothing behaves differently.
/// </summary>
public sealed partial class KeyBindings
{
    /// <summary>
    /// How many chords a command takes (ours). The doc planned two; 1.1's own swivel answers to three
    /// — X, the keypad's 5 and the fifth mouse button — so it is three.
    /// </summary>
    public const int MaxBindings = 3;

    /// <summary>The ini section.</summary>
    public const string Section = "keys";

    /// <summary>What is in force for the running game.</summary>
    public static KeyBindings Current { get => current ??= new(); set => current = value; }
    static KeyBindings? current;

    /// <summary>One command's name, kind, what it does, 1.1's keys for it and where they were read.</summary>
    public readonly record struct Info(KeyCommand Command, string Id, string Name, CommandKind Kind, string What, KeyChord[] Defaults);

    static KeyChord K(int scan, KeyMods mods = KeyMods.None) => new(scan, mods);
    static KeyChord M(int button) => KeyChord.Mouse(button);

    /// <summary>Every command, in the key page's order, with 1.1's keys.</summary>
    public static readonly Info[] All =
    [
        new(KeyCommand.Act, "act", "Act", CommandKind.Click, "Act on what is under the pointer: walk, attack, talk, pick up, use. Held, it keeps swinging.", [M(1)]),   // binding 1
        new(KeyCommand.Cast, "cast", "Cast", CommandKind.Click, "Cast the current discipline or use what is held.", [M(3)]),   // binding 2
        new(KeyCommand.SecondaryClick, "secondaryclick", "Secondary Click", CommandKind.Click, "A click with the secondary flag of its own: secondary attack, or run.", [M(2)]),   // binding 3: the middle button
        new(KeyCommand.PartyClick, "partyclick", "Party Click", CommandKind.Click, "A click with the party flag of its own: party attack, or regroup there (ours).", []),
        new(KeyCommand.AltClick, "altclick", "Alt Click", CommandKind.Click, "A click with the alt flag of its own: fire at the ground, or pick up to the cursor (ours).", []),
        new(KeyCommand.HoldClick, "holdclick", "Hold Click", CommandKind.Click, "A click with the hold flag of its own: hold the attack, turn in place, drop here (ours).", []),
        new(KeyCommand.Secondary, "secondary", "Secondary", CommandKind.Flag, "Held with a click: secondary attack on a target, run on the ground (bit 0x1 of the click's mask).", [K(KeyNames.LShift), K(KeyNames.RShift)]),
        new(KeyCommand.Party, "party", "Party", CommandKind.Flag, "Held with a click: party attack, or the party regroups there (bit 0x2).", [K(KeyNames.LCtrl), K(KeyNames.RCtrl)]),
        new(KeyCommand.Alt, "alt", "Alt", CommandKind.Flag, "Held with a click: fire at the ground, or pick up to the cursor (bit 0x4).", [K(KeyNames.LAlt), K(KeyNames.RAlt)]),
        new(KeyCommand.Hold, "hold", "Hold", CommandKind.Flag, "Held with a click: hold the attack, turn in place, drop at once (bit 0x8).", [K(KeyNames.Space)]),
        new(KeyCommand.TurnLeft, "turnleft", "Turn Left", CommandKind.Held, "Turn the camera left.", [K(KeyNames.Keypad(4))]),
        new(KeyCommand.TurnRight, "turnright", "Turn Right", CommandKind.Held, "Turn the camera right.", [K(KeyNames.Keypad(6))]),
        new(KeyCommand.TiltUp, "tiltup", "Tilt Up", CommandKind.Held, "Tilt the camera.", [K(KeyNames.Keypad(8))]),
        new(KeyCommand.TiltDown, "tiltdown", "Tilt Down", CommandKind.Held, "Tilt the camera the other way.", [K(KeyNames.Keypad(2))]),
        new(KeyCommand.ZoomOut, "zoomout", "Zoom Out", CommandKind.Held, "Back the camera off. 1.1's keypad - too, which is the quick load's by default (ours, KeyBindings.KeypadZoom).", [K(KeyNames.Minus)]),
        new(KeyCommand.ZoomIn, "zoomin", "Zoom in", CommandKind.Held, "Bring the camera in. 1.1's keypad + too, which is the quick save's by default (ours, KeyBindings.KeypadZoom).", [K(KeyNames.EqualsKey)]),
        new(KeyCommand.FirstPerson, "firstperson", "First Person", CommandKind.Held, "Held: look out of the character's eyes.", [K(KeyNames.Letter('Z')), M(4)]),
        new(KeyCommand.Swivel, "swivel", "Swivel", CommandKind.Press, "Held: the mouse swivels the camera.", [K(KeyNames.Letter('X')), K(KeyNames.Keypad(5)), M(5)]),
        new(KeyCommand.DragSwivel, "dragswivel", "Drag Swivel", CommandKind.Press, "Held and dragged: the camera swivels as with X (ours, [qol] camera). The middle button, which is 1.1's secondary click with the switch off.", [M(2)]),
        new(KeyCommand.SwivelLock, "swivellock", "Swivel Lock", CommandKind.Press, "The swivel on without holding anything, or off again (ours, [qol] camera): ends itself in a menu, a cutscene, first person and the free camera.", [KeyChord.Mouse(2, KeyMods.Ctrl)]),
        new(KeyCommand.QuickSave, "quicksave", "Quick Save", CommandKind.Press, "Save into the slot last saved to or loaded from since the program started; the save screen when there is none (ours).", [K(KeyNames.KeypadPlus)]),
        new(KeyCommand.QuickLoad, "quickload", "Quick Load", CommandKind.Press, "Load that slot — asking first only with the Gameplay screen's Confirm Quick Load on; the load screen when there is none (ours; not in a network game).", [K(KeyNames.KeypadMinus)]),
        new(KeyCommand.ShowObjects, "showobjects", "Show Objects", CommandKind.Held, "Held: every object on screen you could use or take glows as it would under the pointer - no characters (ours; [qol] keys).", [K(KeyNames.LAlt)]),
        new(KeyCommand.FullScreen, "fullscreen", "Full Screen", CommandKind.Press, "Full screen or a window, anywhere - the menus, a film, a conversation; kept in vitomir.ini's [window] (ours; 1.1 is full screen unless started with -window).", [K(KeyNames.Return, KeyMods.Alt)]),
        new(KeyCommand.SkipLine, "skipline", "Skip Line", CommandKind.Press, "Skip the line being spoken.", [K(KeyNames.Space)]),
        new(KeyCommand.Regroup, "regroup", "Regroup", CommandKind.Press, "Everybody back into formation.", [K(KeyNames.Letter('R'))]),
        new(KeyCommand.Ungroup, "ungroup", "Ungroup All", CommandKind.Press, "Everybody out of formation.", [K(KeyNames.Letter('R'), KeyMods.Shift)]),   // 1.1 only
        new(KeyCommand.Group, "group", "In or Out", CommandKind.Press, "The character you steer in or out of the group.", [K(KeyNames.Letter('G'))]),
        new(KeyCommand.StanceAttack, "attackstance", "Attack Stance", CommandKind.Press, "The whole party to the attack stance.", [K(KeyNames.Letter('A'))]),
        new(KeyCommand.StanceNeutral, "neutralstance", "Neutral Stance", CommandKind.Press, "The whole party to neutral.", [K(KeyNames.Letter('N'))]),
        new(KeyCommand.StanceDefense, "defencestance", "Defence Stance", CommandKind.Press, "The whole party to the defence stance.", [K(KeyNames.Letter('E'))]),
        new(KeyCommand.Pause, "pause", "Pause", CommandKind.Press, "Pause combat, or carry on.", [K(KeyNames.Pause)]),
        new(KeyCommand.CharacterSheet, "character", "Character", CommandKind.Press, "The character sheet.", [K(KeyNames.Letter('C'))]),
        new(KeyCommand.Inventory, "inventory", "Inventory", CommandKind.Press, "The inventory.", [K(KeyNames.Letter('I'))]),
        new(KeyCommand.Quests, "quests", "Quests", CommandKind.Press, "The quest pane.", [K(KeyNames.Letter('Q'))]),
        new(KeyCommand.Disciplines, "disciplines", "Disciplines", CommandKind.Press, "The discipline pane.", [K(KeyNames.Letter('D'))]),
        new(KeyCommand.Map, "map", "Map", CommandKind.Press, "The map.", [K(KeyNames.Letter('M'))]),
        new(KeyCommand.ClosePanes, "closepanes", "Close Panes", CommandKind.Press, "Every pane down.", [K(KeyNames.Backspace)]),
        new(KeyCommand.StatusBar, "statusbar", "Status Bar", CommandKind.Press, "The status bar up or down, in the game camera.", [K(KeyNames.F(11))]),   // the manual's F11
        new(KeyCommand.NextCharacter, "nextcharacter", "Next Character", CommandKind.Press, "Steer the next member round.", [K(KeyNames.Tab)]),
        new(KeyCommand.Character1, "character1", "Character 1", CommandKind.Press, "Steer the party's first slot.", [K(KeyNames.F(6))]),
        new(KeyCommand.Character2, "character2", "Character 2", CommandKind.Press, "Steer the party's second slot.", [K(KeyNames.F(7))]),
        new(KeyCommand.Character3, "character3", "Character 3", CommandKind.Press, "Steer the party's third slot.", [K(KeyNames.F(8))]),
        new(KeyCommand.Character4, "character4", "Character 4", CommandKind.Press, "Steer the party's fourth slot.", [K(KeyNames.F(9))]),
        new(KeyCommand.Quick1, "quick1", "Quick Item 1", CommandKind.Press, "Use what is in quick slot 1.", [K(KeyNames.F(1))]),
        new(KeyCommand.Quick2, "quick2", "Quick Item 2", CommandKind.Press, "Use what is in quick slot 2.", [K(KeyNames.F(2))]),
        new(KeyCommand.Quick3, "quick3", "Quick Item 3", CommandKind.Press, "Use what is in quick slot 3.", [K(KeyNames.F(3))]),
        new(KeyCommand.Quick4, "quick4", "Quick Item 4", CommandKind.Press, "Use what is in quick slot 4.", [K(KeyNames.F(4))]),
        new(KeyCommand.Quick5, "quick5", "Quick Item 5", CommandKind.Press, "Use what is in quick slot 5.", [K(KeyNames.F(5))]),
        new(KeyCommand.Discipline1, "discipline1", "Discipline 1", CommandKind.Press, "Make quick discipline 1 the one a cast uses.", [K(KeyNames.Digit(1))]),
        new(KeyCommand.Discipline2, "discipline2", "Discipline 2", CommandKind.Press, "Quick discipline 2.", [K(KeyNames.Digit(2))]),
        new(KeyCommand.Discipline3, "discipline3", "Discipline 3", CommandKind.Press, "Quick discipline 3.", [K(KeyNames.Digit(3))]),
        new(KeyCommand.Discipline4, "discipline4", "Discipline 4", CommandKind.Press, "Quick discipline 4.", [K(KeyNames.Digit(4))]),
        new(KeyCommand.Discipline5, "discipline5", "Discipline 5", CommandKind.Press, "Quick discipline 5.", [K(KeyNames.Digit(5))]),
        new(KeyCommand.Discipline6, "discipline6", "Discipline 6", CommandKind.Press, "Quick discipline 6.", [K(KeyNames.Digit(6))]),
        new(KeyCommand.Feed, "feed", "Feed", CommandKind.Press, "Make Feed the one a cast uses.", [K(KeyNames.Letter('F'))]),
        new(KeyCommand.Heal, "heal", "Heal", CommandKind.Press, "Blood Healing, or Heal for a human.", [K(KeyNames.Letter('H'))]),
        new(KeyCommand.Screenshot, "screenshot", "Screenshot", CommandKind.Press, "A screenshot.", [K(KeyNames.F(12))]),
        new(KeyCommand.Console, "console", "Console", CommandKind.Press, "The developer console, where there is one.", [K(KeyNames.Grave)]),
        new(KeyCommand.Forward, "forward", "Walk Forward", CommandKind.Held, "Driving: walk the way the camera looks (ours, [qol] driving).", [K(KeyNames.Letter('W'))]),
        new(KeyCommand.Back, "back", "Walk Back", CommandKind.Held, "Driving: walk back (ours).", [K(KeyNames.Letter('S'))]),
        new(KeyCommand.StepLeft, "stepleft", "Step Left", CommandKind.Held, "Driving: side-step left (ours).", [K(KeyNames.Letter('A'))]),
        new(KeyCommand.StepRight, "stepright", "Step Right", CommandKind.Held, "Driving: side-step right (ours).", [K(KeyNames.Letter('D'))]),
        new(KeyCommand.Run, "run", "Run", CommandKind.Held, "Driving: run rather than walk (ours). The free camera: fly fast, 1.1's either Shift.", [K(KeyNames.LShift), K(KeyNames.RShift)]),
        new(KeyCommand.StoryMove, "storymove", "Storyteller Move", CommandKind.Click, "In the Storyteller's seat, on the ground: the target is put there, or with Space only turned to face it.", [KeyChord.Mouse(3, KeyMods.Alt)]),   // 1.1's Alt and the right button, which is its cast out of the seat
        new(KeyCommand.FlyForward, "flyforward", "Fly Forward", CommandKind.Fly, "The free camera: forward along the view, whatever the pitch.", [K(KeyNames.Letter('W'))]),
        new(KeyCommand.FlyBack, "flyback", "Fly Back", CommandKind.Fly, "The free camera: back.", [K(KeyNames.Letter('S'))]),
        new(KeyCommand.FlyLeft, "flyleft", "Fly Left", CommandKind.Fly, "The free camera: sideways to the left.", [K(KeyNames.Letter('A'))]),
        new(KeyCommand.FlyRight, "flyright", "Fly Right", CommandKind.Fly, "The free camera: sideways to the right.", [K(KeyNames.Letter('D'))]),
        new(KeyCommand.FlyUp, "flyup", "Fly Up", CommandKind.Fly, "The free camera: up along the camera's own up.", [K(KeyNames.Letter('X'))]),
        new(KeyCommand.FlyDown, "flydown", "Fly Down", CommandKind.Fly, "The free camera: down.", [K(KeyNames.Letter('C'))]),
        new(KeyCommand.FlyTurnLeft, "flyturnleft", "Fly Turn Left", CommandKind.Fly, "The free camera: turn left.", [K(KeyNames.Left)]),
        new(KeyCommand.FlyTurnRight, "flyturnright", "Fly Turn Right", CommandKind.Fly, "The free camera: turn right.", [K(KeyNames.Right)]),
        new(KeyCommand.FlyLookDown, "flylookdown", "Fly Look Down", CommandKind.Fly, "The free camera: tip the view down - 1.1's Up arrow, as a plane's stick.", [K(KeyNames.Up)]),
        new(KeyCommand.FlyLookUp, "flylookup", "Fly Look Up", CommandKind.Fly, "The free camera: tip the view up - 1.1's Down arrow.", [K(KeyNames.Down)]),
        new(KeyCommand.FlyRollLeft, "flyrollleft", "Fly Roll Left", CommandKind.Fly, "The free camera: roll to the left.", [K(KeyNames.KeypadDivide)]),
        new(KeyCommand.FlyRollRight, "flyrollright", "Fly Roll Right", CommandKind.Fly, "The free camera: roll to the right.", [K(KeyNames.KeypadMultiply)]),
        new(KeyCommand.FlyLevel, "flylevel", "Fly Level", CommandKind.Fly, "The free camera: the roll back to level.", [K(KeyNames.Home)]),
        new(KeyCommand.FlyCamera, "freecamera", "Free Camera", CommandKind.Fly, "The free camera out, back to the game camera; in, only with the developer switch.", [K(KeyNames.Letter('P'), KeyMods.Ctrl)]),
        new(KeyCommand.FlyLevelView, "flylevelview", "Fly Level View", CommandKind.Fly, "The free camera: the view level with the horizon, the heading kept (ours, [qol] driving; the keypad's 5 is the swivel elsewhere).", [K(KeyNames.Keypad(5))]),
        new(KeyCommand.FlyDrag, "flydraglook", "Fly Drag Look", CommandKind.Fly, "The free camera: held, the mouse turns and tilts the view as the drag swivel turns the game camera (ours, [qol] driving).", [M(2)]),
    ];

    /// <summary>A command's entry.</summary>
    public static Info Of(KeyCommand c) => All[Index[c]];

    static readonly Dictionary<KeyCommand, int> Index = All.Select((info, i) => (info.Command, i)).ToDictionary(p => p.Command, p => p.i);

    /// <summary>The four flag commands and the bit each sets in the modifier mask.</summary>
    public static readonly (KeyCommand Command, InputModifier Flag)[] Flags =
    [
        (KeyCommand.Secondary, InputModifier.Shift), (KeyCommand.Party, InputModifier.Ctrl),
        (KeyCommand.Alt, InputModifier.Alt), (KeyCommand.Hold, InputModifier.Space),
    ];

    /// <summary>The four direct clicks and the flag each adds.</summary>
    public static readonly (KeyCommand Command, InputModifier Flag)[] FlagClicks =
    [
        (KeyCommand.SecondaryClick, InputModifier.Shift), (KeyCommand.PartyClick, InputModifier.Ctrl),
        (KeyCommand.AltClick, InputModifier.Alt), (KeyCommand.HoldClick, InputModifier.Space),
    ];

    /// <summary>The driving commands (ours), live only under <c>[qol] driving</c>.</summary>
    public static readonly KeyCommand[] Driving = [KeyCommand.Forward, KeyCommand.Back, KeyCommand.StepLeft, KeyCommand.StepRight, KeyCommand.Run];

    /// <summary>
    /// The camera's own commands (ours), live only under <c>[qol] camera</c>: the drag swivel, the
    /// middle button by default (<i>"Let's bind middle mouse to the camera move
    /// button, so mouse2 can still remain as normal right click."</i>) — and the swivel lock, Ctrl and the
    /// middle button (<see cref="SwivelHold"/>). While the switch is on, a
    /// chord one of them has is theirs and not 1.1's command beside it — the middle button's secondary
    /// click (the mouse's third binding) — as a driving key's is the driving's; with it off they are
    /// read as nothing and the chord is 1.1's again.
    /// </summary>
    public static readonly KeyCommand[] Camera = [KeyCommand.DragSwivel, KeyCommand.SwivelLock];

    /// <summary>The free camera's commands (<see cref="CommandKind.Fly"/>), live only while it is up.</summary>
    public static readonly KeyCommand[] Flying = [.. All.Where(i => i.Kind == CommandKind.Fly).Select(i => i.Command)];

    /// <summary>The free camera's commands that are ours and live only under <c>[qol] driving</c>: with it off they take no chord, as 1.1's mode 3 has none of them.</summary>
    public static readonly KeyCommand[] FlyingDriven = [KeyCommand.FlyLevelView, KeyCommand.FlyDrag];

    /// <summary>
    /// The 1.1 key a command's chord is handed on as: its own first default — except the drag
    /// swivel, which is handed on as the swivel's (X), so the game's own swivel runs it,
    /// held and let go, pointer pinned, half a degree a mickey.
    /// </summary>
    public static KeyChord HandedOn(KeyCommand c) =>
        c == KeyCommand.DragSwivel ? Of(KeyCommand.Swivel).Defaults[0] : Of(c).Defaults[0];

    /// <summary>
    /// How a click command is handed on (ours): the button 1.1 hears and the flags it carries whatever is
    /// held. Act is the left button and cast the right (the mouse's bindings 1 and 2); the four direct flag
    /// clicks are the left button with their flag (<see cref="FlagClicks"/>, the world's alone); and **the
    /// Storyteller's move** is its own default, **the right button with Alt** (bit 0x4, which the Storyteller's
    /// move tests) — so a move bound elsewhere is still 1.1's Alt and the right button to everything
    /// that reads the click: in the seat it is the move, out of it the cast with the Alt flag as 1.1's.
    /// </summary>
    public static (int Button, InputModifier Flags, bool WorldOnly) ClickAs(KeyCommand c)
    {
        if (c == KeyCommand.StoryMove) return (3, InputModifier.Alt, false);
        foreach (var (command, flag) in FlagClicks) if (command == c) return (1, flag, true);
        return (c == KeyCommand.Cast ? 3 : 1, InputModifier.None, false);
    }

    /// <summary>The three stance keys, which are live together or not at all.</summary>
    public static readonly KeyCommand[] Stances = [KeyCommand.StanceAttack, KeyCommand.StanceNeutral, KeyCommand.StanceDefense];

    readonly Dictionary<KeyCommand, List<KeyChord>> bound = [];

    /// <summary>1.1's keys.</summary>
    public KeyBindings()
    {
        foreach (var info in All) bound[info.Command] = [.. info.Defaults];
    }

    /// <summary>A command's chords, in their order.</summary>
    public IReadOnlyList<KeyChord> this[KeyCommand c] => bound[c];

    /// <summary>Replaces a command's chords (at most <see cref="MaxBindings"/>, duplicates dropped).</summary>
    public void Set(KeyCommand c, IEnumerable<KeyChord> chords) => bound[c] = [.. chords.Distinct().Take(MaxBindings)];

    /// <summary>A copy, for the key page to edit.</summary>
    public KeyBindings Clone()
    {
        var copy = new KeyBindings();
        foreach (var (c, list) in bound) copy.bound[c] = [.. list];
        return copy;
    }

    /// <summary>Whether every command has 1.1's own keys.</summary>
    public bool AreDefaults => All.All(i => bound[i.Command].SequenceEqual(i.Defaults));

    /// <summary>Every command a chord is bound to, exactly.</summary>
    public IEnumerable<KeyCommand> BoundTo(KeyChord chord) =>
        All.Where(i => bound[i.Command].Contains(chord)).Select(i => i.Command);

    // ---- reading a key ---------------------------------------------------------------------------

    /// <summary>
    /// The commands a chord pressed now acts as: every <see cref="CommandKind.Press"/> or
    /// <see cref="CommandKind.Click"/> command with a binding on that input whose modifiers are all
    /// held, the most specific first — so with R on regroup and Shift+R on ungroup, Shift+R is
    /// ungroup and Ctrl+R is still regroup, as in 1.1, which tests the letter and then the shift.
    /// While <paramref name="driving"/>, a chord a driving command has goes to the driving (it is
    /// read as held) and not to the command beside it, and the three stance keys are off together
    /// when any of them shares a key with the driving — which is what Vitomir did with A, N, E and D
    /// before the keys could be moved. While <paramref name="camera"/>, a
    /// chord a <see cref="Camera"/> command has goes to it alone (the middle button to the drag
    /// swivel, not the secondary click); without, those commands are read as nothing. While
    /// <paramref name="flying"/> — the free camera is up — a chord one of its commands has is
    /// the free camera's and acts as nothing here (the host polls it), as 1.1's mode 3 reads only
    /// The camera's keys; a free-camera command is never one this returns.
    /// </summary>
    public List<(KeyCommand Command, KeyChord Chord)> Pressed(int input, KeyMods held, bool driving, bool camera = false, bool flying = false)
    {
        var found = new List<(KeyCommand, KeyChord)>();
        if (flying && Flies(input, held, driving)) return found;
        int best = -1;
        bool drivingHas = driving && Driving.Any(d => bound[d].Any(b => b.Input == input && (b.Mods & ~held) == 0));
        bool cameraHas = camera && Camera.Any(c => bound[c].Any(b => b.Input == input && (b.Mods & ~held) == 0));
        bool stancesOff = driving && StancesCollide();
        foreach (var info in All)
        {
            if (info.Kind is not (CommandKind.Press or CommandKind.Click) || IsHostFirst(info.Command)) continue;
            if (Camera.Contains(info.Command) != cameraHas) continue;
            if (drivingHas) continue;
            if (stancesOff && Stances.Contains(info.Command)) continue;
            foreach (var b in bound[info.Command])
            {
                if (b.Input != input || (b.Mods & ~held) != 0) continue;
                int score = System.Numerics.BitOperations.PopCount((uint)b.Mods);
                if (score > best) { best = score; found.Clear(); }
                if (score == best) found.Add((info.Command, b));
                break;
            }
        }
        return found;
    }

    /// <summary>Whether a chord pressed now is one of the free camera's (<see cref="Flying"/>; <see cref="FlyingDriven"/> only while <paramref name="driving"/>).</summary>
    public bool Flies(int input, KeyMods held, bool driving = true) =>
        Flying.Any(c => (driving || !FlyingDriven.Contains(c)) && bound[c].Any(b => b.Input == input && (b.Mods & ~held) == 0));

    /// <summary>Whether any stance key is also a driving key, which takes all three off while driving.</summary>
    public bool StancesCollide() =>
        Stances.Any(s => bound[s].Any(b => Driving.Any(d => bound[d].Contains(b))));

    /// <summary>Whether a held command is down: any of its chords with its input down and its modifiers held.</summary>
    public bool Held(KeyCommand c, Func<int, bool> down, KeyMods held) =>
        bound[c].Any(b => down(b.Input) && (b.Mods & ~held) == 0);

    /// <summary>
    /// Whether a click is held **as that click** — what the end of a swing and held
    /// automatic fire read as the button still down: as <see cref="Held"/>, less any chord
    /// whose input the camera's drag has taken while <c>[qol] camera</c> is on. The middle button is
    /// 1.1's secondary click (the mouse's third binding), but with the switch on it is the
    /// drag swivel, 1.1's X handed on, and holding X asks 1.1 for no swing. Vitomir counted
    /// the drag as the middle button held until then, so a swivel in a fight swung the second attack.
    /// </summary>
    public bool HeldClick(KeyCommand c, Func<int, bool> down, KeyMods held, bool camera) =>
        bound[c].Any(b => down(b.Input) && (b.Mods & ~held) == 0
                          && !(camera && Camera.Any(k => bound[k].Any(x => x.Input == b.Input && (x.Mods & ~held) == 0))));

    /// <summary>The modifier mask 1.1 would build from the flag commands held.</summary>
    public InputModifier Mask(Func<int, bool> down, KeyMods held)
    {
        var mask = InputModifier.None;
        foreach (var (c, flag) in Flags)
            if (Held(c, down, held)) mask |= flag;
        return mask;
    }

    /// <summary>Whether an input is one of 1.1's keys for a command that is read while held — the keys the game polls.</summary>
    public static bool PolledDefault(int input) =>
        All.Any(i => i.Kind is CommandKind.Held or CommandKind.Flag && i.Defaults.Any(d => d.Input == input));

    /// <summary>
    /// Whether an input is one of 1.1's keys for any command at all — but the free camera's, which
    /// take nothing from anybody (the arrows, Home and the keypad's / and * stay the harness's and
    /// the reader's when the free camera is not up), and the full-screen toggle's Enter, which is not a
    /// key of 1.1's (<see cref="HostFirst"/>).
    /// </summary>
    public static bool AnyDefault(int input) => All.Any(i => i.Kind != CommandKind.Fly && !IsHostFirst(i.Command) && i.Defaults.Any(d => d.Input == input));

    // ---- the ini ---------------------------------------------------------------------------------

    /// <summary>
    /// A <c>[keys]</c> section over what is already bound: <c>regroup = R</c>, <c>swivel = X, Keypad 5,
    /// Mouse 5</c>, <c>secondaryclick = none</c>. A name that is not a command, or a chord that is not
    /// a key, is reported and skipped, and the command keeps what it had.
    /// </summary>
    public void Apply(IniFile ini, Action<string>? complain = null)
    {
        foreach (var (key, value) in ini.Section(Section))
        {
            var info = All.FirstOrDefault(i => i.Id.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (info.Id is null) { complain?.Invoke($"[keys] {key}: no such command"); continue; }
            var chords = new List<KeyChord>();
            bool ok = true;
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                if (KeyChord.Parse(part) is { } chord) chords.Add(chord);
                else { complain?.Invoke($"[keys] {key}: \"{part}\" is not a key"); ok = false; }
            }
            if (ok) Set(info.Command, chords);
        }
    }

    /// <summary>What goes into <c>[keys]</c>: every command, so the file says plainly what is bound.</summary>
    public IEnumerable<KeyValuePair<string, string>> Values() =>
        All.Select(i => new KeyValuePair<string, string>(i.Id,
            bound[i.Command].Count == 0 ? "none" : string.Join(", ", bound[i.Command].Select(b => b.ToString()))));

    /// <summary>Reads 1.1's keys, then each <c>vitomir.ini</c>'s <c>[keys]</c> (the one beside the executable, then the player's own). No side effect.</summary>
    public static KeyBindings Parse(string? installedDir = null, string? userPath = null, Action<string>? complain = null)
    {
        var keys = new KeyBindings();
        var installed = Path.Combine(installedDir ?? Diag.InstalledFolder, Diag.FileName);
        keys.Apply(IniFile.Read(installed), complain);
        var user = userPath ?? Diag.UserSettingsPath;
        if (!string.Equals(user, installed, StringComparison.OrdinalIgnoreCase)) keys.Apply(IniFile.Read(user), complain);
        return keys;
    }

    /// <summary>
    /// Reads the chain and installs it as <see cref="Current"/>, the player's file first brought past the
    /// quick keys' arrival (<see cref="KeypadMigration"/>). **Only the program calls this.**
    /// </summary>
    public static KeyBindings Configure(string? installedDir = null, Action<string>? complain = null)
    {
        if (KeypadMigration.Run(Diag.UserSettingsPath) is { } moved) complain?.Invoke(moved);
        return Current = Parse(installedDir, complain: complain);
    }

    /// <summary>Writes <see cref="Values"/> into the player's own <c>vitomir.ini</c> (or <paramref name="path"/>).</summary>
    public void Save(string? path = null) => SettingsFile.Set(path ?? Diag.UserSettingsPath, Section, Values());

    public override string ToString()
    {
        var changed = All.Where(i => !bound[i.Command].SequenceEqual(i.Defaults)).ToList();
        if (changed.Count == 0) return "1.1's own";
        var sb = new StringBuilder();
        foreach (var i in changed)
            sb.Append(sb.Length > 0 ? "; " : "").Append(i.Id).Append(" = ")
              .Append(bound[i.Command].Count == 0 ? "none" : string.Join(", ", bound[i.Command]));
        return sb.ToString();
    }
}

/// <summary>
/// The names of the keys (ours): US names for the HID usage numbers SDL calls scan codes, which is
/// what the ini holds and the key page shows. A key's name is its position's, so on a layout that
/// is not the US one the name is the US key in that place — as 1.1, whose DirectInput scan codes are
/// positions too.
/// </summary>
public static class KeyNames
{
    public const int Return = 40, Escape = 41, Backspace = 42, Tab = 43, Space = 44, Minus = 45, EqualsKey = 46,
                     LeftBracket = 47, RightBracket = 48, Backslash = 49, Semicolon = 51, Apostrophe = 52, Grave = 53,
                     Comma = 54, Period = 55, Slash = 56, CapsLock = 57, PrintScreen = 70, ScrollLock = 71, Pause = 72,
                     Insert = 73, Home = 74, PageUp = 75, Delete = 76, End = 77, PageDown = 78,
                     Right = 79, Left = 80, Down = 81, Up = 82, NumLock = 83,
                     KeypadDivide = 84, KeypadMultiply = 85, KeypadMinus = 86, KeypadPlus = 87, KeypadEnter = 88, KeypadPeriod = 99,
                     LCtrl = 224, LShift = 225, LAlt = 226, LGui = 227, RCtrl = 228, RShift = 229, RAlt = 230, RGui = 231;

    public static int Letter(char c) => 4 + (char.ToUpperInvariant(c) - 'A');
    /// <summary>The row of digits: 1 to 9, then 0.</summary>
    public static int Digit(int d) => d == 0 ? 39 : 29 + d;
    public static int F(int n) => n <= 12 ? 57 + n : 104 + (n - 13);
    /// <summary>The keypad's digits: 1 to 9 are 89 to 97, 0 is 98.</summary>
    public static int Keypad(int d) => d == 0 ? 98 : 88 + d;

    /// <summary>Whether an input is Shift, Ctrl or Alt itself.</summary>
    public static bool IsModifier(int input) => input is LCtrl or LShift or LAlt or RCtrl or RShift or RAlt;

    static readonly Dictionary<int, (string Name, string Short)> Named = Build();

    static Dictionary<int, (string, string)> Build()
    {
        var d = new Dictionary<int, (string, string)>();
        for (char c = 'A'; c <= 'Z'; c++) d[Letter(c)] = (c.ToString(), c.ToString());
        for (int i = 0; i <= 9; i++) d[Digit(i)] = (i.ToString(CultureInfo.InvariantCulture), i.ToString(CultureInfo.InvariantCulture));
        for (int i = 1; i <= 24; i++) d[F(i)] = ($"F{i}", $"F{i}");
        for (int i = 0; i <= 9; i++) d[Keypad(i)] = ($"Keypad {i}", $"Kp {i}");
        void N(int k, string name, string brief) => d[k] = (name, brief);
        N(Return, "Enter", "Enter"); N(Escape, "Escape", "Esc"); N(Backspace, "Backspace", "Bksp"); N(Tab, "Tab", "Tab");
        N(Space, "Space", "Space"); N(Minus, "-", "-"); N(EqualsKey, "=", "="); N(LeftBracket, "[", "["); N(RightBracket, "]", "]");
        N(Backslash, "\\", "\\"); N(Semicolon, ";", ";"); N(Apostrophe, "'", "'"); N(Grave, "`", "`"); N(Comma, "Comma", ",");
        N(Period, ".", "."); N(Slash, "/", "/"); N(CapsLock, "Caps Lock", "Caps"); N(PrintScreen, "Print Screen", "PrtSc");
        N(ScrollLock, "Scroll Lock", "ScrLk"); N(Pause, "Pause", "Pause"); N(Insert, "Insert", "Ins"); N(Home, "Home", "Home");
        N(PageUp, "Page Up", "PgUp"); N(Delete, "Delete", "Del"); N(End, "End", "End"); N(PageDown, "Page Down", "PgDn");
        N(Right, "Right", "Right"); N(Left, "Left", "Left"); N(Down, "Down", "Down"); N(Up, "Up", "Up"); N(NumLock, "Num Lock", "NumLk");
        N(KeypadDivide, "Keypad /", "Kp /"); N(KeypadMultiply, "Keypad *", "Kp *"); N(KeypadMinus, "Keypad -", "Kp -");
        N(KeypadPlus, "Keypad +", "Kp +"); N(KeypadEnter, "Keypad Enter", "Kp Ent"); N(KeypadPeriod, "Keypad .", "Kp .");
        N(LCtrl, "Left Ctrl", "L Ctrl"); N(LShift, "Left Shift", "L Shift"); N(LAlt, "Left Alt", "L Alt"); N(LGui, "Left Win", "L Win");
        N(RCtrl, "Right Ctrl", "R Ctrl"); N(RShift, "Right Shift", "R Shift"); N(RAlt, "Right Alt", "R Alt"); N(RGui, "Right Win", "R Win");
        N(KeyChord.MouseBase + 1, "Left Click", "L Click"); N(KeyChord.MouseBase + 2, "Middle Click", "M Click");
        N(KeyChord.MouseBase + 3, "Right Click", "R Click"); N(KeyChord.MouseBase + 4, "Mouse 4", "Mouse 4");
        N(KeyChord.MouseBase + 5, "Mouse 5", "Mouse 5");
        return d;
    }

    /// <summary>A chord's name: its modifiers, then its key; an unnamed key is <c>Key 123</c>.</summary>
    public static string Format(KeyChord chord, bool brief = false)
    {
        string key = Named.TryGetValue(chord.Input, out var n) ? (brief ? n.Short : n.Name)
                   : chord.IsMouse ? $"Mouse {chord.Button}" : $"Key {chord.Input}";
        var sb = new StringBuilder();
        if ((chord.Mods & KeyMods.Ctrl) != 0) sb.Append("Ctrl+");
        if ((chord.Mods & KeyMods.Alt) != 0) sb.Append("Alt+");
        if ((chord.Mods & KeyMods.Shift) != 0) sb.Append("Shift+");
        return sb.Append(key).ToString();
    }

    /// <summary>The input a name stands for, the long or the short one, ignoring case; null when it names nothing.</summary>
    public static int? Input(string name)
    {
        name = name.Trim();
        foreach (var (k, (full, brief)) in Named)
            if (full.Equals(name, StringComparison.OrdinalIgnoreCase) || brief.Equals(name, StringComparison.OrdinalIgnoreCase)) return k;
        if (name.StartsWith("Mouse ", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[6..], out int b) && b is > 0 and < 32)
            return KeyChord.MouseBase + b;
        if (name.StartsWith("Key ", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[4..], out int s) && s is > 0 and < 512)
            return s;
        return null;
    }
}
