// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Nothing held survives a focus change (`HeldInputs`): the binding layer remembers what each held
/// key or button was handed on as, and a focus lost or gained lets go of all of it, once, in the order it
/// went down — so a key whose release went to another window, or to a menu, or that only the harness held,
/// does not stay down for the swivel, the flags or the free camera. 1.1 keeps such a key down until its
/// next DirectInput event; SDL lets go of its own on a focus loss, and this keeps
/// Vitomir's layer with SDL's.
/// </summary>
public class KeyFocusTests
{
    [Fact]
    public void A_focus_loss_mid_press_lets_go_of_every_held_key_once()
    {
        var held = new HeldInputs<string>();
        held.Press(KeyNames.LAlt, []);                          // a flag: handed on as nothing
        held.Press(KeyNames.Letter('X'), ["swivel down"]);      // the swivel, held
        held.Press(KeyChord.MouseBase + 2, ["drag down"]);      // the middle button's drag
        var let = held.LetGoAll();
        Assert.Equal([KeyNames.LAlt, KeyNames.Letter('X'), KeyChord.MouseBase + 2], let.Select(l => l.Input));
        Assert.Equal(["swivel down"], let[1].Made);
        Assert.Equal(0, held.Count);
        // The key-up that comes after (SDL's own, or the hand's) finds nothing to release a second time.
        Assert.False(held.Lift(KeyNames.Letter('X'), out _));
        Assert.Empty(held.LetGoAll());
    }

    [Fact]
    public void A_key_held_again_is_handed_on_as_its_new_press()
    {
        var held = new HeldInputs<string>();
        held.Press(KeyNames.F(11), ["F11"]);
        held.Press(KeyNames.F(11), ["F11 again"]);
        Assert.Equal(1, held.Count);
        Assert.True(held.TryGet(KeyNames.F(11), out var made));
        Assert.Equal(["F11 again"], made);
        Assert.True(held.Lift(KeyNames.F(11), out _));
        Assert.False(held.TryGet(KeyNames.F(11), out _));
    }
}
