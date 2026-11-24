// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Scene;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Named shapes, hidden and shown per instance** (a hide and a show, with a lookup
/// into the sub-model slots). Pins the lookup's rules — whole name, case ignored, own table
/// first, an index found in a slot applied to the carrier — and the three shipped uses: the arrow on
/// a bow (shown by the `Project` tag, hidden by the shot and by an empty quiver), the fists under a
/// claw and the fists under the talking hands.
/// </summary>
public class NamedShapeTests
{
    /// <summary>A model with these mesh names and one group per name, mesh numbers in order.</summary>
    static NodModel Model(params string[] meshes) => new()
    {
        MeshNames = meshes,
        Bones = [new NodBone(Vector3.Zero, new float[12], -1, -1, -1)],
        Groups = [.. meshes.Select((_, i) => new NodMeshGroup(0, new byte[12], 1, 3, 3, 0, 0, (byte)i))],
    };

    static WorldThing Thing(NodModel body)
    {
        var thing = new WorldThing { Index = 0, Guid = 1, Scene = new NsdThing(), Template = "t", Type = ThingType.Actor };
        NamedShapes.Model(thing, body);
        return thing;
    }

    [Fact]
    public void A_shape_is_found_by_its_whole_name_with_case_ignored()
    {
        // strcmpi on the whole 32-byte name: `FIST_RShape` answers to fist_rshape; a prefixed
        // `Civ1oldman1h_fist_RShape` does not, so most townsfolk have no fists to hide.
        var thing = Thing(Model("TorsoShape", "Civ1oldman1h_fist_RShape", "FIST_RShape"));
        Assert.True(NamedShapes.Hide(thing, NamedShapes.FistRight));
        Assert.True(NamedShapes.IsHidden(thing, NamedShapes.Body, 2));
        Assert.False(NamedShapes.IsHidden(thing, NamedShapes.Body, 1));
        Assert.False(NamedShapes.Hide(thing, NamedShapes.FistLeft));              // no such shape: 0
        Assert.True(NamedShapes.Show(thing, "fist_rshape"));
        Assert.False(NamedShapes.IsHidden(thing, NamedShapes.Body, 2));
    }

    [Fact]
    public void Hiding_is_per_instance_not_per_model()
    {
        // The bit lives in the instance (instance[0x4c]), so two characters sharing a model can
        // show different shapes.
        var model = Model("BowShape", "arrowmeshShape");
        var one = Thing(model);
        var two = Thing(model);
        NamedShapes.Hide(one, NamedShapes.Arrow);
        Assert.True(NamedShapes.IsHidden(one, NamedShapes.Arrow));
        Assert.False(NamedShapes.IsHidden(two, NamedShapes.Arrow));
    }

    [Fact]
    public void A_name_only_a_slot_has_hides_the_body_s_mesh_of_the_same_number()
    {
        // As in 1.1: the index comes from the sword's table, and the loop
        // that writes the bits walks the *carrier's* groups. The sword keeps its shape; the body
        // loses whichever of its meshes has that number. No shipped call reaches this.
        var thing = Thing(Model("TorsoShape", "HeadShape", "CapeShape"));
        var sword = Model("BladeShape", "GuardShape");
        NamedShapes.Fill(thing, HeldModel.WeaponSlot, new object(), sword);
        Assert.True(NamedShapes.Hide(thing, "GuardShape"));
        Assert.True(NamedShapes.IsHidden(thing, NamedShapes.Body, 1));             // the body's HeadShape
        Assert.False(NamedShapes.IsHidden(thing, HeldModel.WeaponSlot, 1));        // not the sword's guard
    }

    [Fact]
    public void A_fresh_weapon_shows_everything_and_hiding_its_picture_keeps_its_shapes()
    {
        // A new instance is zeroed, and one is made every time a slot is filled — so
        // a newly equipped bow has its arrow on. Only a change of weapon makes a new instance.
        var thing = Thing(Model("TorsoShape"));
        var bow = Model("arrowmeshShape", "BowShape1");
        var weapon = new object();
        NamedShapes.Fill(thing, HeldModel.WeaponSlot, weapon, bow);
        NamedShapes.Hide(thing, NamedShapes.Arrow, HeldModel.WeaponSlot);
        NamedShapes.Fill(thing, HeldModel.WeaponSlot, weapon, bow);                // same weapon: kept
        Assert.True(NamedShapes.IsHidden(thing, NamedShapes.Arrow, HeldModel.WeaponSlot));
        NamedShapes.Fill(thing, HeldModel.WeaponSlot, new object(), bow);          // another bow
        Assert.False(NamedShapes.IsHidden(thing, NamedShapes.Arrow, HeldModel.WeaponSlot));
    }

    static Location Monastery(Vitomir.Engine.Vfs.GameFiles files) =>
        Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");

    [Fact]
    public void The_batches_of_a_held_bow_know_their_slot_and_mesh()
    {
        // What the renderer tests: every batch of a thing names its instance and mesh number.
        using var files = Install.Open();
        var place = Monastery(files);
        var archer = place.Things.First(t => t.Template.Equals("skel_bow", StringComparison.OrdinalIgnoreCase));
        int at = place.Drawn.ToList().IndexOf(archer);
        var bow = archer.Shapes!.Slots[HeldModel.WeaponSlot].Model!;
        int arrow = Array.FindIndex(bow.MeshNames, n => n.Equals(NamedShapes.Arrow, StringComparison.OrdinalIgnoreCase));
        Assert.True(arrow >= 0);
        Assert.Contains(place.Mesh.Batches, b => b.Thing == at && b.Slot == HeldModel.WeaponSlot && b.Mesh == arrow);
        Assert.Contains(place.Mesh.Batches, b => b.Thing == at && b.Slot == NamedShapes.Body);
    }

    /// <summary>Puts the target on the first ground the archer can see at a bowman's range (as ArcherTests).</summary>
    static void InSight(Location place, WorldThing archer, WorldThing target)
    {
        foreach (var node in place.Walk!.Nodes)
        {
            if (node.Outline.Length == 0) continue;
            var c = (node.Min + node.Max) * 0.5f;
            if (!node.Contains(c)) continue;
            var at = new Vector3(c.X, c.Y, node.Height(c.X, c.Y));
            float d = Vector3.Distance(at, archer.Position);
            if (d < 300f || d > 450f || MathF.Abs(at.Z - archer.Position.Z) > 40f) continue;
            target.Position = at;
            target.Sector = node.Sector;
            if (Sight.Sees(place.Level, archer, target)) return;
        }
        throw new InvalidOperationException("no spot in the archer's sight");
    }

    [Fact]
    public void An_archer_s_arrow_goes_with_each_shot_and_comes_back_with_the_draw()
    {
        // The shot hides it, the next draw's `Project` tag shows it.
        using var files = Install.Open();
        var place = Monastery(files);
        var minds = place.Awaken(new AiClasses(files));
        var archer = place.Things.First(t => t.Template.Equals("skel_bow", StringComparison.OrdinalIgnoreCase));
        var christof = place.Player!.Thing;
        InSight(place, archer, christof);
        var sim = new Simulation(place);
        minds.Aggravate(minds.All.First(m => ReferenceEquals(m.Thing, archer)), christof);

        Assert.False(NamedShapes.IsHidden(archer, NamedShapes.Arrow, HeldModel.WeaponSlot));   // fresh
        int shots = 0, hiddenAfterShot = 0, nocked = 0;
        bool wasHidden = false;
        for (int frame = 0; frame < 30 * 12; frame++)
        {
            int before = sim.Shots.Count;
            sim.Step(1f / 30f);
            bool hidden = NamedShapes.IsHidden(archer, NamedShapes.Arrow, HeldModel.WeaponSlot);
            if (sim.Shots.Skip(before).Any(s => ReferenceEquals(s.Attacker, archer)))
            {
                shots++;
                if (hidden) hiddenAfterShot++;
            }
            if (wasHidden && !hidden) nocked++;
            wasHidden = hidden;
        }
        Assert.True(shots >= 2, $"only {shots} shots");
        Assert.Equal(shots, hiddenAfterShot);
        Assert.True(nocked >= shots - 1, $"{shots} shots but the arrow came back {nocked} times");
    }

    [Fact]
    public void An_empty_quiver_takes_the_arrow_off()
    {
        // Out of ammunition, nothing to reload with — `outofammo`, and the
        // arrow is hidden.
        using var files = Install.Open();
        var place = Monastery(files);
        var archer = place.Things.First(t => t.Template.Equals("skel_bow", StringComparison.OrdinalIgnoreCase));
        var weapon = archer.Actor!.Weapon!;
        weapon.Current.MaxAmmo = 1;
        weapon.Current.Ammo = 0;
        Assert.False(Projectiles.Loaded(archer, weapon));
        Assert.True(NamedShapes.IsHidden(archer, NamedShapes.Arrow, HeldModel.WeaponSlot));
    }

    [Fact]
    public void Claws_hide_the_fists_and_giving_the_hand_back_shows_them()
    {
        // The weapon swap with its last argument set hides fist_lshape and fist_rshape on the body (and
        // on slot 0 when filled); the swap's end shows them again unless the talking hands are on.
        using var files = Install.Open();
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        var actor = christof.Actor!;
        var own = actor.Weapon;
        var claw = new Weapon { Name = "claws", Model = "claws" };
        claw.Attacks[0].Flags = WeaponFlag.SpecificModels;

        WeaponOverride.Override(christof, claw, clearOffhand: true, hideFists: true);
        Assert.Same(claw, actor.Weapon);
        Assert.True(actor.Has2(World.Actor.Flag2.WeaponOverride));
        Assert.True(NamedShapes.IsHidden(christof, NamedShapes.FistLeft));
        Assert.True(NamedShapes.IsHidden(christof, NamedShapes.FistRight));

        // A second override while one is on does nothing (flags2 & 4).
        WeaponOverride.Override(christof, new Weapon { Name = "other" }, false, false);
        Assert.Same(claw, actor.Weapon);

        WeaponOverride.Cancel(christof);
        Assert.Same(own, actor.Weapon);
        Assert.False(actor.Has2(World.Actor.Flag2.WeaponOverride));
        Assert.False(NamedShapes.IsHidden(christof, NamedShapes.FistLeft));
        Assert.False(NamedShapes.IsHidden(christof, NamedShapes.FistRight));
    }

    [Fact]
    public void An_ordinary_weapon_leaves_the_fists_alone()
    {
        // SetWeapon touches no named shape but the bow's arrow: a sword is drawn over
        // the fist that holds it.
        using var files = Install.Open();
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        christof.Actor!.Weapon = new Weapon { Name = "sword", Model = "broadsword" };
        var sim = new Simulation(place);
        sim.Step(1f / 30f);
        Assert.False(NamedShapes.IsHidden(christof, NamedShapes.FistLeft));
        Assert.False(NamedShapes.IsHidden(christof, NamedShapes.FistRight));
    }

    [Fact]
    public void Talking_hands_go_on_in_place_of_the_fists_and_come_off_again()
    {
        // `christof` + `_F` into slot 3, fists hidden, THING_AF2_USINGHANDS; and back.
        using var files = Install.Open();
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        var actor = christof.Actor!;
        Assert.Equal("christof_F", TalkingHands.Name(christof, actor), ignoreCase: true);

        Assert.True(TalkingHands.On(christof));
        Assert.True(actor.Has2(World.Actor.Flag2.UsingHands));
        Assert.NotNull(christof.Shapes!.Slots[TalkingHands.Slot].Model);
        Assert.True(NamedShapes.IsHidden(christof, NamedShapes.FistLeft));
        Assert.True(NamedShapes.IsHidden(christof, NamedShapes.FistRight));
        Assert.False(TalkingHands.On(christof));                                   // already on

        // The mesh takes the hands on at the next frame, in their own slot.
        var sim = new Simulation(place);
        sim.Step(1f / 30f);
        int at = place.Drawn.ToList().IndexOf(christof);
        Assert.Contains(place.Mesh.Batches, b => b.Thing == at && b.Slot == TalkingHands.Slot);

        TalkingHands.Off(christof);
        Assert.False(actor.Has2(World.Actor.Flag2.UsingHands));
        Assert.Null(christof.Shapes!.Slots[TalkingHands.Slot].Model);
        Assert.False(NamedShapes.IsHidden(christof, NamedShapes.FistLeft));
    }

    [Fact]
    public void A_cancel_while_talking_leaves_the_fists_to_the_hands()
    {
        // The swap's end shows the fists only when THING_AF2_USINGHANDS is clear.
        using var files = Install.Open();
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        WeaponOverride.Override(christof, new Weapon { Name = "claws" }, true, true);
        Assert.True(TalkingHands.On(christof));
        WeaponOverride.Cancel(christof);
        Assert.True(NamedShapes.IsHidden(christof, NamedShapes.FistLeft));
    }

    [Fact]
    public void Every_bow_and_crossbow_the_game_ships_has_an_arrow_to_nock()
    {
        // The shape the `Project` tag names, in every held bow model — and in `arrow` and `heart_1`.
        using var files = Install.Open();
        var names = files.List("3d/models/").Where(n => n.EndsWith(".nod", StringComparison.OrdinalIgnoreCase)).ToList();
        int bows = 0;
        foreach (var name in names)
        {
            string stem = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
            // The held copies, one per skeleton (`bow_0` … `CrossbowMD_4`); `crossbow` alone is the
            // one lying on the ground, and has no arrow.
            if (!System.Text.RegularExpressions.Regex.IsMatch(stem, "^(bow|compoundbow|crossbow|crossbowmd)_[0-9]$")) continue;
            var model = NodModel.Read(files.Read(name), name);
            Assert.Contains(model.MeshNames, n => n.Equals(NamedShapes.Arrow, StringComparison.OrdinalIgnoreCase));
            bows++;
        }
        Assert.Equal(16, bows);
    }
}
