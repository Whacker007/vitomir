// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The carried torch, script and engine together**: the
/// engine hangs the torch on Christof's hand when it is worn, and the shipped
/// `ItemUtilityTorch` hears `worn` and hangs its `torchFlame_nosave` — a flame and a light — on the
/// torch's own `USER0` bone; taking the torch off, its `unworn` takes the flame away. The
/// flashlight's script is the same shape with `flashlightEffect`, a steady cyan light and no flame.
/// </summary>
[Collection("scripts")]
public class TorchScriptTests(Scripts s)
{
    static Item Carried(Place m, string template)
    {
        var thing = m.Where.Make(template)!;
        m.Christof.Actor!.Inventory.Add(thing.Item!);
        thing.Owner = m.Christof;
        return thing.Item!;
    }

    [Fact]
    public void The_torch_is_lit_when_it_is_worn_and_put_out_when_it_is_taken_off()
    {
        using var m = Place.Enter(s, "P1_MON1");
        var torch = Carried(m, "torchU");
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(m.Christof, torch, Slot.None, m.Effects, 0));

        var body = torch.Thing!;
        Assert.Same(m.Christof, body.AttachedTo);
        var flame = Assert.Single(body.Attachments).Child;
        Assert.Equal("torchFlame_nosave", flame.Template, ignoreCase: true);
        Assert.True(flame.InWorld);
        var light = m.Where.Lights.Of(flame);
        Assert.NotNull(light);
        Assert.Equal(270f, light!.Current);                                 // DYNA_INTENSITY
        Assert.Equal(1, light.Style);                                       // the low-turbulence flicker

        Assert.True(Equipment.Unequip(m.Christof, torch, m.Effects));
        Assert.False(flame.InWorld);                                        // `unworn` → DestroyTorch → Remove
        Assert.False(body.InWorld);
    }

    /// <summary>
    /// **Fix `shapetorch`** with the torch's own script: the torch leaves the hand as the wolf's
    /// shape goes on — its `unworn` told first, so `DestroyTorch` takes the flame and its light
    /// while they are still in the world — and when the shape ends it is set on the hand again and
    /// told `worn`, which hangs **one** flame on it: not none (a torch set back with no script told
    /// is dark), and not two (the old flame, had it been left riding the torch out of the world).
    /// </summary>
    [Fact]
    public void With_the_shapetorch_fix_the_flame_goes_out_in_the_shape_and_exactly_one_comes_back()
    {
        using var m = Place.Enter(s, "P1_MON1");
        var torch = Carried(m, "torchU");
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(m.Christof, torch, Slot.None, m.Effects, 0));
        int Flames() => m.Where.Things.Count(t => t.InWorld && t.Template.Equals("torchFlame_nosave", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, Flames());
        var first = Assert.Single(torch.Thing!.Attachments).Child;

        Assert.True(ShapeShift.Shift(m.Christof, "wolf_Grey", 0, fixes: new Fixes { ShapeTorch = true }));
        Assert.Equal(0, Flames());
        Assert.True(first.Removed);                                         // destroyed, not only withdrawn
        Assert.False(first.InWorld);                                        // so no light: only a thing in the world shines
        Assert.False(torch.Thing.InWorld);
        Assert.Equal(0, (int)torch.Flags & Equipment.EquippedBit);          // a scene start leaves it dark
        Assert.Contains(torch, m.Christof.Actor!.Inventory.Items);

        Assert.True(ShapeShift.End(m.Christof));
        Assert.Equal(1, Flames());
        var again = Assert.Single(torch.Thing.Attachments).Child;
        Assert.NotSame(first, again);
        Assert.True(again.InWorld);
        Assert.NotNull(m.Where.Lights.Of(again));
        Assert.Same(m.Christof, torch.Thing.AttachedTo);
    }

    /// <summary>
    /// **Fix `shapetorch` on the path a player takes** (after a play-test): the fix
    /// on from the command line over a <c>vitomir.ini</c> whose <c>[fixes]</c> section does not name
    /// it, parsed as the program parses it, and read by the shape change itself when the shipped
    /// script calls it — not a switch handed to the shape change as the tests above do (the place
    /// carries it, <see cref="Location.Fixes"/>, so the test installs no process-wide register); the
    /// torch taken from the pack and put in the hand
    /// through the pack's own clicks, which light it; Shape of the Beast made the current discipline
    /// and cast by the right button on the ground; and the shape put on by the shipped
    /// `EffectDiscShapeOfTheBeast` when its crouch is done. The wolf holds nothing, no flame burns,
    /// and the torch is still worn.
    /// </summary>
    [Fact]
    public void With_the_shapetorch_fix_on_the_command_line_the_torch_lit_from_the_pack_is_out_of_the_wolf_s_hand()
    {
        var dir = Directory.CreateTempSubdirectory("vitomir-ini-").FullName;
        File.WriteAllText(Path.Combine(dir, "vitomir.ini"),
            "[fixes]\nmovespeed = off\nmatchedgait = off\nbodycollision = off\nglowcolours = off\n");
        try
        {
            var fixes = Fixes.Parse(["--load", "SP3", "--fix", "shapetorch", "--give", "torchU"], installedDir: dir);
            Assert.True(fixes.ShapeTorch);
            using var m = Place.Enter(s, "P1_MON1");
            m.Where.Fixes = fixes;
            var me = m.Christof.Actor!;
            me.Type = ActorType.Vampire;
            me.Stat[(int)ActorStat.BloodPool] = 100f;
            me.SetBaseStat((int)ActorStat.Blood, 100f);
            me.Stat[(int)ActorStat.Blood] = 100f;
            int Flames() => m.Where.Things.Count(t => t.InWorld && t.Template.Equals("torchFlame_nosave", StringComparison.OrdinalIgnoreCase));

            // The pack: the torch lifted out of it and dropped on the off hand.
            var torch = Carried(m, "torchU");
            Assert.True(Stow.Place(me, torch, Slot.Inventory));
            var pane = new Engine.Ui.InventoryPane(new Engine.Ui.UiAssets(m.Files).Pane(Engine.Ui.InventoryPane.PaneName)) { Subject = me };
            var hands = new Engine.Ui.InventoryHands(pane, m.Christof, m.Effects);
            var pack = pane.Grids().First(g => g.Slot == Slot.Inventory);
            var offHand = pane.Grids().First(g => g.Slot == Slot.LeftWeapon);
            Assert.Equal(Engine.Ui.HandAction.Lifted, hands.ClickIn(pack, torch.Cell.X, torch.Cell.Y).What);
            Assert.Equal(Engine.Ui.HandAction.Equipped, hands.ClickIn(offHand, 0, 0).What);
            Assert.Same(torch, me.Utility);
            Assert.Equal(1, Flames());

            // The discipline: made current (a click on its row) and cast by a right click on the ground.
            var beast = DisciplineBook.SetLevel(m.Christof, m.Powers, "ShapeOfTheBeast", 1);
            Assert.NotNull(beast);
            me.Disciplines.Current = beast;
            Assert.Equal((int)OrderResult.Started, CastInput.OnGround(m.Christof, m.Christof.Position, InputModifier.None));
            Assert.True(m.RunUntil(() => ShapeShift.IsShifted(me), 10f));
            m.Run(1f);

            Assert.Null(me.Utility);
            Assert.False(torch.Thing!.InWorld);
            Assert.Equal(0, Flames());
            Assert.Equal(Slot.LeftWeapon, torch.Location);
            Assert.Contains(torch, me.Inventory.Items);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_flashlight_carries_a_steady_cyan_light_and_no_flame()
    {
        using var m = Place.Enter(s, "P1_MON1");
        var flashlight = Carried(m, "flashlight");
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(m.Christof, flashlight, Slot.None, m.Effects, 0));
        Assert.True(Utility.Stance(m.Christof.Actor));                      // 0x800, CARRY SPECIAL

        var effect = Assert.Single(flashlight.Thing!.Attachments).Child;
        Assert.Equal("flashlightEffect", effect.Template, ignoreCase: true);
        var light = m.Where.Lights.Of(effect)!;
        Assert.Equal(0u, light.Type);                                       // a point light: never a cone
        Assert.Equal(0, light.Style);
        Assert.Equal(new System.Numerics.Vector3(39, 219, 239), light.Color);
    }
}
