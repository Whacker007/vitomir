// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Where every bindable command is read (ours): the sweep of `KeyBindings.All` against the host,
// made a table so that a command declared with nothing to read it fails a test (`KeyReaderTests`).
namespace Vitomir.Engine.Ui;

/// <summary>How the host reads a command's key.</summary>
public enum KeyReading
{
    /// <summary>The key handler tests the command's handed-on 1.1 key when it comes down (or up).</summary>
    Pressed,
    /// <summary>Polled each frame from the keyboard <c>KeyInput.State</c> builds, or asked with <c>KeyInput.Held</c>.</summary>
    Polled,
    /// <summary>One of the four modifier bits, in the mask <c>KeyInput.Modifiers</c> builds.</summary>
    Flag,
    /// <summary>A button, as <see cref="KeyBindings.ClickAs"/> hands it on.</summary>
    Click,
    /// <summary>The free camera's, polled by <c>FreeCameraInput</c> while it is up.</summary>
    Fly,
}

/// <summary>One command's reader: how, in which of the host's files, the text there that reads it, and what reads it in 1.1.</summary>
public readonly record struct KeyReader(KeyCommand Command, KeyReading How, string Host, string Token, string Where);

/// <summary>
/// **Every command the key bindings declare, and what reads it** (ours). The bindings hand a command
/// on as its 1.1 key (<see cref="KeyBindings.HandedOn"/>) and the host's handlers test 1.1's keys, so a
/// command is live only when some handler tests its handed-on key; one declared with nothing to test it
/// would sit on the key page, remappable, and do nothing. <c>KeyReaderTests</c> fails when a command has
/// no row here, or when its row's <see cref="KeyReader.Token"/> is not in its host file.
/// <para>
/// The sweep found every command read; what it found wrong was **keys read beside them** — a
/// status bar alias on F4 (a quick item's key), the harness's F1–F5 without Ctrl, run read off the
/// secondary flag's Shift, and Shift+regroup read as ungroup wherever ungroup had gone (<see cref="HarnessKeys"/>,
/// <see cref="KeyHandOn"/>).
/// </para>
/// </summary>
public static class KeyReaders
{
    // The frame controller's classes, a folder (Program.cs's one method until then).
    const string Program = "src/Vitomir.Classic/Controller", Keys = "src/Vitomir.Classic/Input/KeyInput.cs", Fly = "src/Vitomir.Classic/Input/FreeCameraInput.cs",
                 Story = "src/Vitomir.Classic/Net/StoryView.cs";

    static KeyReader P(KeyCommand c, string token, string where) => new(c, KeyReading.Pressed, Program, token, where);
    static KeyReader H(KeyCommand c, string token, string where) => new(c, KeyReading.Polled, Program, token, where);
    static KeyReader F(KeyCommand c) => new(c, KeyReading.Fly, Fly, "KeyCommand." + c, "the free camera's keys");

    /// <summary>One row a command, in <see cref="KeyBindings.All"/>'s order.</summary>
    public static readonly KeyReader[] All =
    [
        new(KeyCommand.Act, KeyReading.Click, Program, "e.Code == LoopButton.Left", "the click handler, binding 1"),
        new(KeyCommand.Cast, KeyReading.Click, Program, "e.Code == LoopButton.Right", "the click handler, binding 2"),
        new(KeyCommand.SecondaryClick, KeyReading.Click, Program, "KeyInput.WorldOnly", "the click handler, binding 3"),
        new(KeyCommand.PartyClick, KeyReading.Click, Keys, "KeyBindings.ClickAs(command)", "ours: a left click with bit 0x2"),
        new(KeyCommand.AltClick, KeyReading.Click, Keys, "KeyBindings.ClickAs(command)", "ours: a left click with bit 0x4"),
        new(KeyCommand.HoldClick, KeyReading.Click, Keys, "KeyBindings.ClickAs(command)", "ours: a left click with bit 0x8"),
        new(KeyCommand.Secondary, KeyReading.Flag, Program, "KeyInput.Modifiers()", "the click mask, bit 0x1"),
        new(KeyCommand.Party, KeyReading.Flag, Program, "KeyInput.Modifiers()", "the click mask, bit 0x2"),
        new(KeyCommand.Alt, KeyReading.Flag, Program, "KeyInput.Modifiers()", "the click mask, bit 0x4"),
        new(KeyCommand.Hold, KeyReading.Flag, Program, "KeyInput.Modifiers()", "the click mask, bit 0x8"),
        H(KeyCommand.TurnLeft, "LoopScancode.Keypad4", "the camera's keys"),
        H(KeyCommand.TurnRight, "LoopScancode.Keypad6", "the camera's keys"),
        H(KeyCommand.TiltUp, "LoopScancode.Keypad8", "the camera's keys"),
        H(KeyCommand.TiltDown, "LoopScancode.Keypad2", "the camera's keys"),
        H(KeyCommand.ZoomOut, "LoopScancode.Minus", "the camera's keys"),
        H(KeyCommand.ZoomIn, "LoopScancode.EqualsKey", "the camera's keys"),
        H(KeyCommand.FirstPerson, "LoopScancode.Z", "the camera's keys"),
        P(KeyCommand.Swivel, "LoopScancode.X", "the swivel key"),
        P(KeyCommand.DragSwivel, "LoopScancode.X", "ours: handed on as the swivel's X"),
        P(KeyCommand.SwivelLock, "KeyCommand.SwivelLock", "ours: acted on by the host (KeyBindings.Acted)"),
        P(KeyCommand.QuickSave, "KeyCommand.QuickSave", "ours: acted on by the host"),
        P(KeyCommand.QuickLoad, "KeyCommand.QuickLoad", "ours: acted on by the host"),
        H(KeyCommand.ShowObjects, "KeyInput.Held(KeyCommand.ShowObjects)", "ours: read by the host alone (KeyBindings.HostHeld)"),
        new(KeyCommand.FullScreen, KeyReading.Pressed, "src/Vitomir.Classic/Input/KeyInputFullScreen.cs", "KeyCommand.FullScreen", "ours: taken before anything else hears the event, a menu or a text field included"),
        P(KeyCommand.SkipLine, "LoopKey.Space", "the conversation's key"),
        P(KeyCommand.Regroup, "LoopKey.R", "the game camera's key hook, the regroup"),
        P(KeyCommand.Ungroup, "hud.Bar.Disperse()", "the party bar's disperse"),
        P(KeyCommand.Group, "LoopKey.G", "the game camera's key hook"),
        P(KeyCommand.StanceAttack, "LoopKey.A", "the game camera's key hook"),
        P(KeyCommand.StanceNeutral, "LoopKey.N", "the game camera's key hook"),
        P(KeyCommand.StanceDefense, "LoopKey.E", "the game camera's key hook"),
        P(KeyCommand.Pause, "LoopKey.Pause", "the game camera's key hook"),
        P(KeyCommand.CharacterSheet, "LoopKey.C", "the game camera's key hook"),
        P(KeyCommand.Inventory, "LoopKey.I", "the game camera's key hook"),
        P(KeyCommand.Quests, "LoopKey.Q", "the game camera's key hook"),
        P(KeyCommand.Disciplines, "LoopKey.D", "the game camera's key hook"),
        P(KeyCommand.Map, "LoopKey.M", "the game camera's key hook"),
        P(KeyCommand.ClosePanes, "LoopKey.Backspace", "the panes' close key"),
        P(KeyCommand.StatusBar, "StatusBarKey.Reads(", "the status bar's key"),
        P(KeyCommand.NextCharacter, "LoopKey.Tab", "the game camera's key hook"),
        P(KeyCommand.Character1, "LoopKey.F6", "the game camera's key hook"),
        P(KeyCommand.Character2, "LoopKey.F7", "the game camera's key hook"),
        P(KeyCommand.Character3, "LoopKey.F8", "the game camera's key hook"),
        P(KeyCommand.Character4, "LoopKey.F9", "the game camera's key hook"),
        P(KeyCommand.Quick1, "LoopKey.F1 or", "the game camera's key hook"),
        P(KeyCommand.Quick2, "LoopKey.F2 or", "the game camera's key hook"),
        P(KeyCommand.Quick3, "LoopKey.F3", "the game camera's key hook"),
        P(KeyCommand.Quick4, "LoopKey.F4 or", "the game camera's key hook"),
        P(KeyCommand.Quick5, "LoopKey.F5 &&", "the game camera's key hook"),
        P(KeyCommand.Discipline1, "LoopKey.Digit1 =>", "the game camera's key hook"),
        P(KeyCommand.Discipline2, "LoopKey.Digit2 =>", "the game camera's key hook"),
        P(KeyCommand.Discipline3, "LoopKey.Digit3 =>", "the game camera's key hook"),
        P(KeyCommand.Discipline4, "LoopKey.Digit4 =>", "the game camera's key hook"),
        P(KeyCommand.Discipline5, "LoopKey.Digit5 =>", "the game camera's key hook"),
        P(KeyCommand.Discipline6, "LoopKey.Digit6 =>", "the game camera's key hook"),
        P(KeyCommand.Feed, "LoopKey.F =>", "the game camera's key hook"),
        P(KeyCommand.Heal, "LoopKey.H =>", "the game camera's key hook"),
        P(KeyCommand.Screenshot, "LoopKey.F12", "the game camera's key hook"),
        P(KeyCommand.Console, "LoopKey.Grave", "the game camera's key hook"),
        H(KeyCommand.Forward, "LoopScancode.W", "ours, [qol] driving"),
        H(KeyCommand.Back, "LoopScancode.S", "ours, [qol] driving"),
        H(KeyCommand.StepLeft, "LoopScancode.A", "ours, [qol] driving"),
        H(KeyCommand.StepRight, "LoopScancode.D", "ours, [qol] driving"),
        H(KeyCommand.Run, "KeyInput.Held(KeyCommand.Run)", "ours, [qol] driving"),
        new(KeyCommand.StoryMove, KeyReading.Click, Story, "KeyInput.StoryMove", "the right-click handler"),
        F(KeyCommand.FlyForward), F(KeyCommand.FlyBack), F(KeyCommand.FlyLeft), F(KeyCommand.FlyRight),
        F(KeyCommand.FlyUp), F(KeyCommand.FlyDown), F(KeyCommand.FlyTurnLeft), F(KeyCommand.FlyTurnRight),
        F(KeyCommand.FlyLookDown), F(KeyCommand.FlyLookUp), F(KeyCommand.FlyRollLeft), F(KeyCommand.FlyRollRight),
        F(KeyCommand.FlyLevel), F(KeyCommand.FlyCamera), F(KeyCommand.FlyLevelView), F(KeyCommand.FlyDrag),
    ];

    /// <summary>A command's reader, or null when it has none — which a test fails.</summary>
    public static KeyReader? Of(KeyCommand c)
    {
        foreach (var r in All) if (r.Command == c) return r;
        return null;
    }
}
