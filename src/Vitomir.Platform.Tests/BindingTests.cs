// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Control;

namespace Vitomir.Platform.Tests;

public sealed class BindingTests
{
    // A stand-in for SDL's key names: a few keys with made-up scancodes.
    static readonly Dictionary<string, int> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["W"] = 26, ["X"] = 27, ["Space"] = 44, ["Left Ctrl"] = 224, ["Left Shift"] = 225,
    };
    static int? Code(string name) => Keys.TryGetValue(name, out var c) ? c : null;
    static string Name(int code) => Keys.First(k => k.Value == code).Key;

    [Fact]
    public void Mouse2_is_the_right_button_as_Windows_and_Quake_number_them()
    {
        Assert.Equal(InputSource.Mouse(BindingNames.SdlLeft), BindingNames.Parse("Mouse1", Code));
        Assert.Equal(InputSource.Mouse(BindingNames.SdlRight), BindingNames.Parse("Mouse2", Code));
        Assert.Equal(InputSource.Mouse(BindingNames.SdlMiddle), BindingNames.Parse("Mouse3", Code));
        Assert.Equal(InputSource.Mouse(BindingNames.SdlRight), BindingNames.Parse("mouse right", Code));
        Assert.Equal(InputSource.Mouse(BindingNames.SdlX1), BindingNames.Parse("Mouse 4", Code));
        Assert.Equal(InputSource.Wheel(-1), BindingNames.Parse("Wheel Down", Code));
        Assert.Equal(InputSource.Key(224), BindingNames.Parse("Left Ctrl", Code));
        Assert.Null(BindingNames.Parse("Mouse Elbow", Code));
        Assert.Equal("Mouse Right", BindingNames.Name(InputSource.Mouse(BindingNames.SdlRight), Name));
    }

    [Fact]
    public void A_key_bound_to_two_actions_works_both_in_the_order_given_and_is_listed_as_shared()
    {
        // Keys shared ("some commands shouldn't be mutually exclusive to the same key being bound"): no clash.
        var table = BindingTable.Build([new("jump", ["Mouse2"]), new("block", ["Mouse Right"]), new("crouch", ["Elbow"])], Code, Name);
        Assert.True(table.TryGet(InputSource.Mouse(BindingNames.SdlRight), out var action));
        Assert.Equal("jump", action);
        Assert.Equal(["jump", "block"], table.ActionsOf(InputSource.Mouse(BindingNames.SdlRight)));
        var shared = Assert.Single(table.Shared);
        Assert.Equal(InputSource.Mouse(BindingNames.SdlRight), shared.Source);
        Assert.DoesNotContain(table.Problems, p => p.Contains("bound to", StringComparison.Ordinal));
        Assert.Contains(table.Problems, p => p.Contains("crouch = \"Elbow\"", StringComparison.Ordinal));
        Assert.Empty(table.ActionsOf(InputSource.Key(4)));
    }

    [Fact]
    public void Rebinding_moves_the_key_off_the_action_that_had_it_and_writes_its_own_name()
    {
        var b = new Dictionary<string, string[]>
        {
            ["jump"] = ["Space"], ["sprint"] = ["Left Shift"], ["walk"] = ["Left Ctrl"], ["crouch"] = ["X"], ["block"] = ["Mouse Right"],
        };
        var moved = BindingTable.Rebind(b, "sprint", ["Left Ctrl"], Code, Name);
        Assert.Equal(["Left Ctrl taken from walk"], moved);
        BindingTable.Rebind(b, "walk", ["Left Shift"], Code, Name);
        BindingTable.Rebind(b, "jump", ["Mouse2"], Code, Name);
        Assert.Equal(["Mouse Right"], b["jump"]);
        Assert.Empty(b["block"]);
        Assert.Equal(["Left Ctrl"], b["sprint"]);
        Assert.Equal(["Left Shift"], b["walk"]);
        BindingTable.Rebind(b, "block", [], Code, Name);
        Assert.Empty(b["block"]);
    }

    [Fact]
    public void A_wrong_rebinding_changes_nothing()
    {
        var b = new Dictionary<string, string[]> { ["jump"] = ["Space"], ["crouch"] = ["X"] };
        Assert.Throws<ArgumentException>(() => BindingTable.Rebind(b, "jmp", ["X"], Code, Name));
        Assert.Throws<ArgumentException>(() => BindingTable.Rebind(b, "jump", ["X", "Elbow"], Code, Name));
        Assert.Equal(["Space"], b["jump"]);
        Assert.Equal(["X"], b["crouch"]);
    }

    [Fact]
    public void One_key_may_work_crouch_and_cancel_and_crouch_and_jump()
    {
        // X as crouch and cancel: both are accepted, in either order.
        foreach (var order in new[] { new[] { "crouch", "cancel" }, ["cancel", "crouch"] })
        {
            var t = BindingTable.Build(order.Select(a => new KeyValuePair<string, string[]>(a, ["X"])), Code, Name);
            Assert.Empty(t.Problems);
            Assert.True(t.TryGet(InputSource.Key(27), out var action));
            Assert.Equal("crouch", action);
            Assert.True(t.Cancels(InputSource.Key(27)));
            Assert.Equal(2, t.Entries.Count(e => e.Source == InputSource.Key(27)));
        }
        // A key that is cancel's alone works cancel.
        var only = BindingTable.Build([new("cancel", ["W"])], Code, Name);
        Assert.True(only.TryGet(InputSource.Key(26), out var c));
        Assert.Equal(BindingTable.Cancel, c);
        // Crouch and jump on one key: the player's choice, both work it; crouch (the first) is the one TryGet names.
        var both = BindingTable.Build([new("crouch", ["X"]), new("jump", ["X"])], Code, Name);
        Assert.True(both.TryGet(InputSource.Key(27), out var first));
        Assert.Equal("crouch", first);
        Assert.Equal(["crouch", "jump"], both.ActionsOf(InputSource.Key(27)));
        Assert.False(both.Cancels(InputSource.Key(27)));
        Assert.Empty(both.Problems);
    }

    [Fact]
    public void Rebinding_cancel_to_crouch_s_key_leaves_it_on_crouch_and_rebinding_jump_to_it_takes_it_from_crouch_only()
    {
        var b = new Dictionary<string, string[]> { ["jump"] = ["Space"], ["crouch"] = ["X"], ["cancel"] = ["W"] };
        Assert.Empty(BindingTable.Rebind(b, "cancel", ["X"], Code, Name));
        Assert.Equal(["X"], b["crouch"]);
        Assert.Equal(["X"], b["cancel"]);
        var moved = BindingTable.Rebind(b, "jump", ["X"], Code, Name);
        Assert.Equal(["X taken from crouch"], moved);
        Assert.Empty(b["crouch"]);
        Assert.Equal(["X"], b["cancel"]);
    }
}
