// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The parts of the skeleton death a comparison with 1.1 found
/// unbuilt: the smoke off the body's bones (one roll a bone past the root, on the shared stream),
/// the body's shadows turned off, the dust declining with blood off, and none of it for a vampire
/// killed in mid-air. Every vampire that is not a party member dies this way, so the rolls matter:
/// the smoke's come inside the death effects, before the drop's.
///
/// One test turns the global blood switch off, so the class runs on its own.
/// </summary>
[Collection(nameof(DustTests))]
[CollectionDefinition(nameof(DustTests), DisableParallelization = true)]
public class DustTests
{
    static (Location Place, Anim.MotionSets Motions, WorldThing Victim) Monastery(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_MON2", things: true, sky: false, scene: "MON2_8_2");
        var motions = new Anim.MotionSets(files);
        var victim = place.Things.First(t => t.Actor is { } a && a.SkeletonId == 0
                                             && Damage.Dusts(t, id => place.Templates!.Has(Damage.SkeletonTemplate(id)))
                                             && t.Animation is { BoneCount: > 1 });
        return (place, motions, victim);
    }

    [Fact]
    public void A_dusting_vampire_smokes_from_a_third_of_its_bones_for_a_second()
    {
        using var files = Install.Open();
        var (place, motions, victim) = Monastery(files);
        Assert.SkipUnless(place.Templates!.Has(Damage.DustSmoke), "no vampDeathSmoke in this install");
        int bones = victim.Animation!.BoneCount;
        int before = place.Things.Count;

        // A blow of one point throws no chunk (the death sprays start over 5), so the smoke's rolls
        // are the first the kill draws: one for each bone past the root.
        Damage.Kill(victim, killer: null, HitKind.Melee, DamageType.Lethal, nowMs: 1_000,
                    damage: 1f, motions: motions, rng: new Rng(7));

        var replay = new Rng(7);
        var expected = Enumerable.Range(1, bones - 1).Where(_ => replay.Unit() < Damage.DustSmokeChance).ToList();
        var smoke = place.Things.Skip(before).Where(t => t.Template == Damage.DustSmoke).ToList();
        Assert.Equal(expected.Count, smoke.Count);
        Assert.NotEmpty(smoke);
        Assert.All(smoke, s =>
        {
            Assert.Same(victim, s.AttachedTo);
            Assert.Equal(1_000 + Damage.Corpse.SmokeMs, s.ExpireAt);
        });
        // Hung on exactly the bones whose roll came in under 0.33, the root never (the parent's list
        // is pushed at the front, so it reads backwards).
        Assert.Equal(expected, victim.Attachments.Where(a => a.Child.Template == Damage.DustSmoke)
                                                 .Select(a => a.Bone).Order());
        Assert.Equal(WoundEffects.AttachFlags, victim.Attachments.First(a => a.Child.Template == Damage.DustSmoke).Flags);
    }

    [Fact]
    public void A_dusting_body_casts_no_shadow()
    {
        using var files = Install.Open();
        var (_, motions, victim) = Monastery(files);
        victim.RenderFlags |= Shadows.DropShadow | Shadows.FullShadow;
        Damage.Kill(victim, killer: null, HitKind.Melee, DamageType.Lethal, nowMs: 1_000, motions: motions, rng: new Rng(1));
        Assert.True(victim.Actor!.Dusted);
        Assert.Equal(0u, victim.RenderFlags & (Shadows.DropShadow | Shadows.FullShadow));
    }

    /// <summary>
    /// **A dusted body stays a `TARGET DEAD` target for thirty seconds**: the dust zeroes
    /// the fade clock inside the death effects, and the kill sets it again after them —
    /// so the invisible body keeps its death time, which is what a `TARGET DEAD` discipline asks.
    /// When the clock comes round the fade it starts is from nothing to nothing, and the body is not
    /// drawn again. Vitomir left the clock at zero (a comparison with 1.1's gap).
    /// </summary>
    [Fact]
    public void A_dusted_body_stays_a_target_dead_target_for_thirty_seconds()
    {
        using var files = Install.Open();
        var (place, motions, victim) = Monastery(files);
        Damage.Kill(victim, killer: null, HitKind.Melee, DamageType.Lethal, nowMs: 0, motions: motions, rng: new Rng(1));
        Assert.True(victim.Actor!.Dusted);
        Assert.Equal(Damage.Corpse.FadeMs, victim.Actor.FadeAt);
        Assert.True(Damage.CanBeRaised(victim));

        // The corpse step at the thirty seconds: the clock goes, and the body is still nothing to look
        // at — its fade began with the dust and is not started over.
        var sim = new Simulation(place);
        sim.Advance(2f, 1f / 10f);
        int began = victim.Actor.FadingFrom;
        Assert.NotEqual(0, began);
        Assert.True(Damage.CanBeRaised(victim));
        sim.Advance(30f, 1f / 10f);
        Assert.Equal(0, victim.Actor.FadeAt);
        Assert.False(Damage.CanBeRaised(victim));
        Assert.Equal(began, victim.Actor.FadingFrom);
        Assert.Equal(0f, victim.ModelAlpha);
        Assert.Equal(0f, victim.Actor.Opacity(sim.Milliseconds));
    }

    [Fact]
    public void With_blood_off_a_vampire_leaves_a_body()
    {
        using var files = Install.Open();
        var (place, motions, victim) = Monastery(files);
        int before = place.Things.Count;
        Gore.NoBlood = true;
        try
        {
            Damage.Kill(victim, killer: null, HitKind.Melee, DamageType.Lethal, nowMs: 1_000, motions: motions, rng: new Rng(1));
        }
        finally { Gore.NoBlood = false; }

        // The dust declines, and the death effects go on as for anybody: an ordinary
        // corpse, with its fade clock, and no skeleton.
        Assert.True(victim.Actor!.Dead);
        Assert.False(victim.Actor.Dusted);
        Assert.Equal(1_000 + Damage.Corpse.FadeMs, victim.Actor.FadeAt);
        Assert.DoesNotContain(place.Things.Skip(before), t => t.Template.StartsWith("skeleton_") || t.Template == Damage.DustSmoke);
    }

    [Fact]
    public void A_vampire_killed_in_mid_air_does_not_turn_to_dust()
    {
        using var files = Install.Open();
        var (place, motions, victim) = Monastery(files);
        victim.Actor!.Action = ActionMode.Thrown;
        int before = place.Things.Count;

        Damage.Kill(victim, killer: null, HitKind.Melee, DamageType.Lethal, nowMs: 1_000, motions: motions, rng: new Rng(1));

        // The death effects end at once for a THROWN character.
        Assert.False(victim.Actor.Dusted);
        Assert.DoesNotContain(place.Things.Skip(before), t => t.Template.StartsWith("skeleton_"));
    }
}
