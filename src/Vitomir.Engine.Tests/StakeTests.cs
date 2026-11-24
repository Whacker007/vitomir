// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The stake**: a staking critical on a vampire (melee or shot) rolls
/// against `STAKEPROTECTION` and stakes instead of harming; the stake comes out by itself
/// after twenty seconds; and a friend can pull it out sooner
/// (its tick and the pull). Earlier, a staking critical simply landed doubled
/// and nothing in Vitomir could stake anybody.
/// </summary>
public class StakeTests
{
    static (Location Place, WorldThing Christof, WorldThing Other) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Steered!;
        var other = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null).Thing;
        other.Actor!.Type = ActorType.Vampire;
        other.Actor.Flags |= Actor.Flag.AiPaused;
        return (place, christof, other);
    }

    /// <summary>A seed whose first roll out of a hundred is at least <paramref name="least"/>.</summary>
    static int SeedRolling(int least)
    {
        for (int seed = 1; ; seed++)
            if ((int)(new Rng(seed).Unit() * Stake.RollScale) >= least) return seed;
    }

    static WorldThing Body(ActorType kind) => new()
    {
        Index = -1, Guid = 99, Scene = new NsdThing(), Template = "t", Type = ThingType.Actor,
        Collide = CollideShape.Cylinder, Radius = 32f, Height = 72f,
        Actor = new Actor { Type = kind, Health = 100f, MaxHealth = 100f },
    };

    static WorldThing Arrow(uint flags) => new()
    {
        Index = -1, Guid = 7, Scene = new NsdThing(), Template = "arrow",
        Type = ThingType.Projectile, MoveType = WorldThing.MovePhysics,
        ProjectileFlags = flags, ProjectileDamage = 40,
    };

    /// <summary>
    /// A shot: an arrow's critical on a vampire stakes it and does no harm; on a human it is the
    /// doubled blow it always was; and a shot without `PROJ_STAKING` never stakes.
    /// </summary>
    [Fact]
    public void A_staking_shots_critical_stakes_a_vampire_and_does_no_harm()
    {
        int seed = SeedRolling(50);
        var vampire = Body(ActorType.Vampire);
        Assert.True(Stake.Shot(Arrow(Projectiles.Flag.Staking), null, vampire, critical: true, new Rng(seed)));
        Assert.True(vampire.Actor!.Has(Actor.Flag.Staked));
        Assert.Equal(ActionMode.Staked, vampire.Actor.Action);
        Assert.Equal(100f, vampire.Actor.Health);

        Assert.False(Stake.Shot(Arrow(Projectiles.Flag.Staking), null, Body(ActorType.Human), critical: true, new Rng(seed)));
        Assert.False(Stake.Shot(Arrow(0), null, Body(ActorType.Vampire), critical: true, new Rng(seed)));
        Assert.False(Stake.Shot(Arrow(Projectiles.Flag.Staking), null, Body(ActorType.Vampire), critical: false, new Rng(seed)));
    }

    /// <summary>
    /// The roll is a whole number out of a hundred, and it must be **above** the protection
    /// (strictly): a roll of 0 never stakes, and a protection of 100 is
    /// proof against everything.
    /// </summary>
    [Fact]
    public void The_roll_must_beat_the_protection()
    {
        using var files = Install.Open();
        var (_, _, other) = Open(files);
        int seed = SeedRolling(50);
        int roll = (int)(new Rng(seed).Unit() * Stake.RollScale);
        Assert.True(Stake.Rolls(other, new Rng(seed)));
        EffectRuntime.Apply(other, "ef_stakeprotection", 0, value: roll);
        Assert.Equal(roll, other.Effects.Value(EffectKind.StakeProtection));
        Assert.False(Stake.Rolls(other, new Rng(seed)));         // equal is not above
    }

    /// <summary>
    /// A melee stake is left in the victim: the attacker's stake leaves its pack and its hand goes
    /// back to its own weapon (the weapon set to nothing).
    /// </summary>
    [Fact]
    public void A_melee_stake_is_left_in_the_victim()
    {
        using var files = Install.Open();
        var (_, christof, other) = Open(files);
        var templates = new TemplateSet(files);
        var stake = Weapon.From("stake", templates.Resolve("stake"));
        Assert.NotNull(stake);
        Assert.True(stake!.Current.Is(WeaponFlag.CanStake));
        var me = christof.Actor!;
        var item = new Item { Name = "stake", Weapon = stake };
        me.Inventory.Add(item);
        me.Weapon = stake;

        var crit = new Blow(true, 61, 10f, 1f, true);
        Assert.True(Stake.Melee(christof, other, stake, crit, new Rng(SeedRolling(50))));
        Assert.True(other.Actor!.Has(Actor.Flag.Staked));
        Assert.DoesNotContain(item, me.Inventory.Items);
        Assert.NotSame(stake, me.Weapon);

        // Not a critical, no stake.
        Assert.False(Stake.Melee(christof, Body(ActorType.Vampire), stake, crit with { Critical = false }, new Rng(1)));
    }

    /// <summary>
    /// Staked: restrained (`ef_restrain`), `ef_staked`, action 0x13, facing whoever did it — and
    /// twenty seconds later it comes out by itself.
    /// </summary>
    [Fact]
    public void A_stake_comes_out_by_itself_after_twenty_seconds()
    {
        using var files = Install.Open();
        var (place, christof, other) = Open(files);
        var sim = new Simulation(place);
        Stake.In(other, christof);
        var them = other.Actor!;
        Assert.True(them.Has(Actor.Flag.Staked));
        Assert.True(them.Has2(Actor.Flag2.Restrained));
        Assert.NotNull(other.Effects.Find("ef_staked"));
        Assert.Equal(ActionMode.Staked, them.Action);
        // Facing Christof: his direction from the victim is the victim's forward.
        var forward = Vector3.TransformNormal(Vector3.UnitY, other.Rotation);
        var toHim = Vector3.Normalize((christof.Position - other.Position) with { Z = 0f });
        Assert.True(Vector3.Dot(forward, toHim) > 0.99f);

        int began = place.Milliseconds;
        while (place.Milliseconds - began <= Stake.StakedForMs - 100) sim.Step(1f / 10f);
        Assert.True(them.Has(Actor.Flag.Staked));
        for (int i = 0; i < 5; i++) sim.Step(1f / 10f);
        Assert.False(them.Has(Actor.Flag.Staked));
        Assert.Equal(ActionMode.None, them.Action);
        Assert.Null(other.Effects.Find("ef_staked"));
        Assert.False(them.Has2(Actor.Flag2.Restrained));
    }

    /// <summary>
    /// **Pulling it out**: the friend walks up, is put 60 in front
    /// of the victim, and the stake comes out at once. Refused on somebody not staked (1) and on
    /// oneself (0).
    /// </summary>
    [Fact]
    public void A_friend_walks_up_and_pulls_the_stake_out()
    {
        using var files = Install.Open();
        var (place, christof, other) = Open(files);
        var sim = new Simulation(place);
        Assert.Equal(OrderResult.Refused, Stake.Order(christof, other, place.Milliseconds));
        Stake.In(other, null);
        Assert.Equal(OrderResult.Ignored, Stake.Order(christof, christof, place.Milliseconds));
        Assert.Equal(OrderResult.Started, Stake.Order(christof, other, place.Milliseconds));
        Assert.Equal(ActionMode.Unstake, christof.Actor!.Action);

        for (int i = 0; i < 30 * 30 && other.Actor!.Has(Actor.Flag.Staked); i++) sim.Step(1f / 30f);
        Assert.False(other.Actor!.Has(Actor.Flag.Staked));
        var forward = Vector3.TransformNormal(Vector3.UnitY, other.Rotation);
        var expected = other.Position + forward * Stake.UnstakeReach;
        Assert.True(Vector2.Distance(new(christof.Position.X, christof.Position.Y), new(expected.X, expected.Y)) < 1f,
                    $"Christof at {christof.Position}, the pull puts him at {expected}");
    }

    /// <summary>
    /// A pedestrian revived gets up where it was first placed (the spawn transform over the
    /// live one); anybody else gets up where it fell.
    /// </summary>
    [Fact]
    public void A_revived_pedestrian_goes_home()
    {
        using var files = Install.Open();
        var (place, _, _) = Open(files);
        var ped = place.Things.First(t => t.Mind is { Mode: AiMode.PedWander } && t.Actor is not null);
        var home = ped.SpawnPosition;
        ped.Position = home + new Vector3(40f, 0f, 0f);
        Damage.Kill(ped, null, HitKind.Melee, DamageType.Lethal, place.Milliseconds);
        Assert.True(ped.Actor!.Dead);
        Damage.Revive(ped);
        Assert.False(ped.Actor.Dead);
        Assert.Equal(home, ped.Position);
    }
}
