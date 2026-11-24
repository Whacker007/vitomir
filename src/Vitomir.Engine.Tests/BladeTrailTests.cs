// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Particles;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A weapon's blade trail**: `SetWeapon` hangs the
/// weapon's `WEAPON_TRAIL` particle thing on the hand — its trail emitter given the
/// blade's two ends — and takes it off again when the weapon leaves the hand; the trail
/// lays its segments only through a `STRIKETRAIL` swing, which on most swords is only the second
/// attack; and a load makes none (the load flag). Christof's broadsword in his haven.
/// </summary>
public class BladeTrailTests
{
    /// <summary>The haven with Christof, his broadsword in his hand (as his `.npc` gives it; the loader alone leaves him his fists).</summary>
    static Location Haven(Vitomir.Engine.Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_HAVN", sky: false, player: "Christof");
        place.Player!.Thing.Actor!.Weapon = Weapon.From("broadsword", place.Templates!.Resolve("broadsword"));
        return place;
    }

    /// <summary>`WEAPON_BLADESTART`, `WEAPON_BLADEEND` and `WEAPON_TRAIL` (240–242) as a broadsword ships them.</summary>
    [Fact]
    public void A_broadsword_names_its_trail_and_its_blades_two_ends()
    {
        using var files = Install.Open();
        var place = Haven(files);
        var sword = place.Player!.Thing.Actor!.Weapon!;
        Assert.Equal("broadsword", sword.Name, ignoreCase: true);
        Assert.Equal("weapon_trail", sword.Trail);
        Assert.Equal(new Vector3(-6f, 10f, 0f), sword.BladeStart);
        Assert.Equal(new Vector3(-12f, 60f, 0f), sword.BladeEnd);
        Assert.True(sword.Attacks[0].Is(WeaponFlag.BladeTrail));
        Assert.False(sword.Attacks[0].Is(WeaponFlag.StrikeTrail));
        Assert.True(sword.Attacks[1].Is(WeaponFlag.StrikeTrail));
    }

    /// <summary>
    /// The trail thing is made and hung on the weapon hand (motion-set tag 0x1a) with
    /// flags 0xc001, its trail emitter's `startpos`/`endpos` the blade's ends over the `bastard`
    /// prototype's (0,20,0)/(0,100,0), and its GUID kept on the weapon.
    /// </summary>
    [Fact]
    public void A_sword_in_the_hand_has_its_trail_hung_on_the_hand()
    {
        using var files = Install.Open();
        var place = Haven(files);
        var sim = new Simulation(place);
        sim.Step(0.01f);
        var christof = place.Player!.Thing;
        var sword = christof.Actor!.Weapon!;
        var trail = place.Thing(sword.TrailGuid);
        Assert.NotNull(trail);
        Assert.Equal("weapon_trail", trail!.Template, ignoreCase: true);
        Assert.Same(christof, trail.AttachedTo);
        var hung = christof.Attachments.Single(a => ReferenceEquals(a.Child, trail));
        Assert.Equal(christof.Animation!.TagBone(BladeTrails.HandTag), hung.Bone);
        Assert.Equal(0xc001u, hung.Flags);
        var emitter = Assert.IsType<TrailEmitter>(place.Particles.Of(trail));
        Assert.Equal(sword.BladeStart, emitter.Settings.StartPos);
        Assert.Equal(sword.BladeEnd, emitter.Settings.EndPos);
        Assert.Equal(new Vector3(0f, 20f, 0f), place.Particles.Prototype("bastard")!.StartPos);   // the prototype is untouched
    }

    /// <summary>
    /// The trail lays a segment a frame only while its holder swings an attack marked
    /// `STRIKETRAIL`: the broadsword's first attack leaves nothing, its second a ribbon
    /// across the blade.
    /// </summary>
    [Fact]
    public void Only_the_striketrail_attack_leaves_a_ribbon()
    {
        using var files = Install.Open();
        var place = Haven(files);
        var sim = new Simulation(place);
        sim.Step(0.01f);
        var christof = place.Player!.Thing;
        var sword = christof.Actor!.Weapon!;
        var emitter = (TrailEmitter)place.Particles.Of(place.Thing(sword.TrailGuid)!)!;
        var ahead = christof.Position + new Vector3(0f, 100f, 0f);

        sword.Select(0);
        Actions.StrikeAt(christof, ahead, sword, sim.Milliseconds, place.Motions);
        for (int i = 0; i < 20; i++) sim.Step(0.01f);
        Assert.DoesNotContain(emitter.Segments, s => s.Life > 0f);

        Actions.Stop(christof.Actor!);
        sword.Select(1);
        Actions.StrikeAt(christof, ahead, sword, sim.Milliseconds, place.Motions);
        for (int i = 0; i < 20; i++) sim.Step(0.01f);
        var live = emitter.Segments.Where(s => s.Life > 0f).ToList();
        Assert.NotEmpty(live);
        // Each segment runs the blade's length: from 10 to 60 up the hand, 50.4 long.
        Assert.All(live, s => Assert.Equal(new Vector2(-6f, 50f).Length(), Vector3.Distance(s.Start, s.End), 1));
    }

    /// <summary>
    /// The weapon leaving the hand takes its trail with it (let go and, hung with
    /// auto-remove, destroyed); the weapon coming back hangs a new one.
    /// </summary>
    [Fact]
    public void Putting_the_sword_away_takes_its_trail_with_it()
    {
        using var files = Install.Open();
        var place = Haven(files);
        var sim = new Simulation(place);
        sim.Step(0.01f);
        var actor = place.Player!.Thing.Actor!;
        var sword = actor.Weapon!;
        var first = place.Thing(sword.TrailGuid)!;
        actor.Weapon = new Weapon { Name = "fists" };
        sim.Step(0.01f);
        Assert.True(first.Removed);
        actor.Weapon = sword;
        sim.Step(0.01f);
        var second = place.Thing(sword.TrailGuid);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.False(second!.Removed);
    }

    /// <summary>
    /// **A load hangs no trail** (the load sets the weapon again while its loading flag is up, so
    /// the trail returns at once): a character put back by a load loses the trail it had and gets
    /// none until its weapon changes.
    /// </summary>
    [Fact]
    public void A_character_put_back_by_a_load_has_no_trail_until_its_weapon_changes()
    {
        using var files = Install.Open();
        var place = Haven(files);
        var sim = new Simulation(place);
        sim.Step(0.01f);
        var christof = place.Player!.Thing;
        var sword = christof.Actor!.Weapon!;
        var before = place.Thing(sword.TrailGuid)!;
        Vitomir.Engine.Save.PostLoad.FixUp(christof);
        sim.Step(0.01f);
        Assert.True(before.Removed);
        Assert.DoesNotContain(christof.Attachments, a => a.Child.Template.Equals("weapon_trail", StringComparison.OrdinalIgnoreCase));
        christof.Actor!.Weapon = new Weapon { Name = "fists" };
        sim.Step(0.01f);
        christof.Actor!.Weapon = sword;
        sim.Step(0.01f);
        Assert.Contains(christof.Attachments, a => a.Child.Template.Equals("weapon_trail", StringComparison.OrdinalIgnoreCase));
    }
}
