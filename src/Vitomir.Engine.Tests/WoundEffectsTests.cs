// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The wound sparks** (the damage reaction's switch on the kind of damage): electric hangs
/// `syssfx_electricdamage` and `syssfx_electricsparks` on about a quarter of the victim's bones, fire
/// `syssfx_firedamage` on a quarter and `syssfx_firedynalight` on the root, both gone 80 ms a point
/// later, cold `syssfx_colddamage` the same, faith, poison and disease nothing, and an ordinary wound
/// a `blood0` for 300 ms hung on the root for a character's blow. Against
/// Christof in the monastery, whose skeleton the engine's templates hang them on.
/// </summary>
public class WoundEffectsTests
{
    static Location Monastery(Vitomir.Engine.Vfs.GameFiles files) =>
        Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");

    [Fact]
    public void Lightning_hangs_sparks_on_about_a_quarter_of_the_bones()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        int bones = christof.Animation!.BoneCount;
        Assert.True(bones > 8);
        var made = WoundEffects.Show(christof, null, DamageType.Electric, 10f, new Rng(7), 1000);
        Assert.NotEmpty(made);
        Assert.Equal(made.Count(t => t.Template == WoundEffects.ElectricDamage), made.Count(t => t.Template == WoundEffects.ElectricSparks));
        int pairs = made.Count / 2;
        Assert.InRange(pairs, 1, bones - 1);
        Assert.All(made, t => Assert.Same(christof, t.AttachedTo));
        // As long as their templates say (the switch sets no time on them): not the blow's 80 ms a point.
        Assert.All(made, t => Assert.NotEqual(1000 + 800, t.ExpireAt));
    }

    [Fact]
    public void Fire_burns_for_eighty_ms_a_point_with_a_light_on_the_root()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        var made = WoundEffects.Show(christof, null, DamageType.Fire, 12.5f, new Rng(3), 2000);
        var light = Assert.Single(made, t => t.Template == WoundEffects.FireLight);
        Assert.Equal(2000 + 1000, light.ExpireAt);                           // 12.5 × 80
        Assert.All(made, t => Assert.Equal(2000 + 1000, t.ExpireAt));
        Assert.Contains(made, t => t.Template == WoundEffects.FireDamage);
        Assert.Empty(WoundEffects.Show(christof, null, DamageType.Poison, 12.5f, new Rng(3), 2000));
        Assert.All(WoundEffects.Show(christof, null, DamageType.Cold, 5f, new Rng(3), 0),
                   t => { Assert.Equal(WoundEffects.ColdDamage, t.Template); Assert.Equal(400, t.ExpireAt); });
    }

    /// <summary>
    /// An ordinary wound bleeds for 300 ms, hung on the root for a character's blow — and the damage
    /// itself makes it (Damage.Apply → the damage reaction), drawing nothing from the fight's dice.
    /// </summary>
    [Fact]
    public void An_ordinary_wound_bleeds_for_a_moment_and_takes_no_roll()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        var monk = place.Drawn.First(t => t is { Actor: not null } && !ReferenceEquals(t, christof));
        Assert.Equal(0, WoundEffects.BoneFor(christof, monk));
        var rng = new Rng(11);
        var made = WoundEffects.Show(christof, monk, DamageType.Lethal, 4f, rng, 500);
        var blood = Assert.Single(made);
        Assert.Equal(WoundEffects.Blood, blood.Template);
        Assert.Equal(800, blood.ExpireAt);
        Assert.Same(christof, blood.AttachedTo);
        Assert.Equal(new Rng(11).Next(), rng.Next());                        // no roll was taken

        int before = place.Drawn.Count(t => t is { Template: WoundEffects.Blood, InWorld: true });
        Damage.Apply(christof, monk, HitKind.Melee, DamageType.Normal, 2f, new Rng(5), 1000, skipSoak: true);
        Assert.True(place.Drawn.Count(t => t is { Template: WoundEffects.Blood, InWorld: true }) > before);
    }


}
