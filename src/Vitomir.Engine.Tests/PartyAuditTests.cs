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
/// Six differences a comparison with 1.1
/// found between 1.1's party and ped AI and Vitomir's, built: the sidestep of a companion or
/// pet with no clear shot, which is not the enemy's manoeuvre; the rally's one pick for
/// the whole loop and its draw from the whole flood; a companion out of ammo going on to
/// follow; the gate in front of the nurse; the manoeuvre's fallback
/// walk on the target's node and its roll after the too-close test; and the
/// peds' ready test that keeps a restrained wanderer from rolling a spot.
///
/// Where a difference is only in how many numbers come off the AI's one stream, the test counts
/// them (<see cref="Rng.Draws"/>) — the order of every roll after depends on it.
/// </summary>
public class PartyAuditTests
{
    /// <summary>Christof and Wilhem in the university, both in the party and in formation.</summary>
    static (Location Place, Minds Minds, Mind Wilhem, Mind Christof) Companions(GameFiles files)
    {
        var place = Location.Load(files, "P1_UNIV", things: true, sky: false, scene: "UNIV_7_7",
                                  player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var christof = minds.All.First(m => ReferenceEquals(m.Thing, place.Player!.Thing));
        var wilhem = minds.All.First(m => !ReferenceEquals(m, christof) && m.Thing.Type == ThingType.Player);
        foreach (var m in new[] { christof, wilhem })
        {
            m.Thing.Actor!.Flags |= Actor.Flag.Party;
            m.Thing.Actor.Flags2 |= Actor.Flag2.InFormation;
            m.Thing.Actor.Stance = PartyStance.Neutral;
            m.Enter(AiMode.Party);
        }
        return (place, minds, wilhem, christof);
    }

    /// <summary>Somebody nobody's friend and attackable, a character with a mind of its own.</summary>
    static Mind Stranger(Minds minds, params Mind[] not)
    {
        var them = minds.All.First(m => !not.Contains(m) && m.Thing.Actor is not null
                                        && m.Thing.Type is ThingType.Actor or ThingType.Player
                                        && !m.Thing.Actor.Has(Actor.Flag.Party));
        them.Thing.Actor!.Flags &= ~Actions.Unattackable;
        them.Thing.Actor.Team = Actor.Teams.EnemyFirst;
        return them;
    }

    /// <summary>Only <paramref name="who"/> thinks on the next tick, and nobody fidgets.</summary>
    static void Alone(Minds minds, Mind who)
    {
        foreach (var m in minds.All) { m.NextThink = int.MaxValue; m.NextIdle = int.MaxValue; }
        who.NextThink = 0;
    }

    static float Degrees(Vector3 a, Vector3 b)
    {
        a.Z = 0f; b.Z = 0f;
        float cross = a.X * b.Y - a.Y * b.X, dot = Vector3.Dot(a, b);
        return MathF.Atan2(cross, dot) * 180f / MathF.PI;
    }

    // --- the sidestep -------------------------------------------------------------------

    /// <summary>
    /// **A blocked companion sidesteps 40–90° off the line and 50–80 units, at a walk**.
    /// The enemy's manoeuvre, which Vitomir ran here until then, turned ±30° and stepped out to the
    /// class's <c>chaseDist</c> at the attack flags' pace — so every angle below 40 would have been it.
    /// </summary>
    [Fact]
    public void A_companion_with_no_clear_shot_sidesteps_forty_to_ninety_degrees_and_fifty_to_eighty_units()
    {
        using var files = Install.Open();
        var (place, minds, wilhem, christof) = Companions(files);
        var enemy = Stranger(minds, wilhem, christof).Thing;
        var start = wilhem.Thing.Position;
        place.Move(enemy, start + new Vector3(300f, 0f, 0f));
        var line = enemy.Position - start;

        int walked = 0, left = 0, right = 0;
        for (int i = 0; i < 16; i++)
        {
            place.Move(wilhem.Thing, start);
            wilhem.Walker.Stop();
            long before = minds.Random.Draws;
            minds.Sidestep(wilhem, enemy);
            Assert.Equal(before + 4, minds.Random.Draws);
            if (!wilhem.Walker.Walking) continue;

            walked++;
            var step = wilhem.Walker.Destination - start;
            float turn = Degrees(line, step);
            float length = new Vector2(step.X, step.Y).Length();
            Assert.InRange(MathF.Abs(turn), 38f, 92f);
            Assert.InRange(length, 46f, 82f);
            Assert.False(wilhem.Walker.Running, "the sidestep walks (flags 0x20)");
            Assert.Equal(minds.Now + Minds.SidestepThink, wilhem.NextThink);
            if (turn > 0f) left++; else right++;
        }
        Assert.True(walked >= 8, $"only {walked} of 16 sidesteps walked");
        Assert.True(left > 0 && right > 0, $"{left} to the left and {right} to the right");
    }

    /// <summary>
    /// Touching its target, it neither steps nor rolls,
    /// and thinks again in half a second.
    /// </summary>
    [Fact]
    public void A_companion_touching_its_target_does_not_sidestep_or_roll()
    {
        using var files = Install.Open();
        var (place, minds, wilhem, christof) = Companions(files);
        var enemy = Stranger(minds, wilhem, christof).Thing;
        place.Move(enemy, wilhem.Thing.Position + new Vector3(wilhem.Thing.Radius + enemy.Radius - 4f, 0f, 0f));
        wilhem.Walker.Stop();

        long before = minds.Random.Draws;
        minds.Sidestep(wilhem, enemy);

        Assert.Equal(before, minds.Random.Draws);
        Assert.False(wilhem.Walker.Walking);
        Assert.Equal(minds.Now + Minds.SidestepThink, wilhem.NextThink);
    }

    /// <summary>A dead target is dropped and the mind goes back to its base mode.</summary>
    [Fact]
    public void A_sidestep_at_a_dead_target_drops_it()
    {
        using var files = Install.Open();
        var (place, minds, wilhem, christof) = Companions(files);
        var enemy = Stranger(minds, wilhem, christof).Thing;
        place.Move(enemy, wilhem.Thing.Position + new Vector3(300f, 0f, 0f));
        wilhem.Aim(enemy, minds.Now);
        enemy.Actor!.Flags |= Actor.Flag.Dead;

        minds.Sidestep(wilhem, enemy);

        Assert.Null(wilhem.Target);
        Assert.Equal(AiMode.Party, wilhem.Mode);
    }

    // --- out of ammo ------------------------------------------------------

    /// <summary>
    /// **Out of arrows, a companion keeps following**: the party's combat
    /// decision returns without marking the think spent on the fight, so the party think goes on to
    /// the follow. Earlier, Vitomir stood it still for as long as its target lived.
    /// </summary>
    [Fact]
    public void A_companion_out_of_ammo_keeps_following_its_leader()
    {
        using var files = Install.Open();
        var (place, minds, wilhem, christof) = Companions(files);
        var enemy = Stranger(minds, wilhem, christof);
        // Somewhere somebody stands, and so walkable, far enough from Christof to want to follow.
        var far = minds.All.Where(m => m.Thing.Actor is not null && !ReferenceEquals(m, wilhem)
                                       && !ReferenceEquals(m, christof) && !ReferenceEquals(m, enemy))
                           .Select(m => m.Thing.Position)
                           .First(p => Vector3.Distance(p, christof.Thing.Position) is > 400f and < 1200f);
        place.Move(wilhem.Thing, far + new Vector3(0f, 0f, 1f));
        wilhem.Walker.Ground();
        wilhem.Walker.Stop();

        var me = wilhem.Thing.Actor!;
        var bow = new Weapon { Name = "empty bow" };
        bow.Attacks[0].Flags |= WeaponFlag.Ranged;
        bow.Attacks[0].AmmoType = 99; bow.Attacks[0].MaxAmmo = 10; bow.Attacks[0].Ammo = 0; bow.Attacks[0].AmmoUse = 1;
        me.Weapon = bow;
        foreach (var item in me.Inventory.UsableWeapons().ToList()) me.Inventory.Remove(item);
        minds.MainFlags |= AiMainFlag.NoPartyCast;
        // No fight leash, which at the engine's default 500 would drop the target first.
        wilhem.Class.BreakDist = 0f;
        wilhem.Aim(enemy.Thing, minds.Now);

        Alone(minds, wilhem);
        minds.Tick(0.001f);

        Assert.Same(enemy.Thing, wilhem.Target);
        Assert.True(wilhem.Walker.Walking, "out of ammo, it should have gone on to follow Christof");
    }

    // --- the nurse's gate -----------------------------------------------------------

    static (Location Place, Minds Minds, Mind Member, Actor Me, Item Vial) Nursing(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var member = minds.All.First(m => !ReferenceEquals(m.Thing, minds.Steered) && m.Thing.Actor is not null);
        var me = member.Thing.Actor!;
        me.Flags |= Actor.Flag.Party;
        me.Flags2 |= Actor.Flag2.InFormation;
        // A companion is a player-type thing: the party think's chain past the fight — hunt, follow,
        // nurse — is a type 7's only (`PartyGates`).
        me.IsPlayerThing = true;
        me.Stance = PartyStance.Neutral;
        member.Enter(AiMode.Party);
        me.Type = ActorType.Vampire;
        var vial = new Item { Name = "vial of vitae", Location = Slot.Inventory, Amount = 5,
                              Effects = [new ItemEffect("ef_increaseblood", 0, null, 0)] };
        me.Inventory.Add(vial);
        // Badly hurt and nearly dry: the nurse's last rung, the vial, is what it reaches for.
        me.Health = me.MaxHealth * 0.05f;
        me.Stat[(int)ActorStat.BloodPool] = 20f;
        me.Stat[(int)ActorStat.Blood] = 1f;
        return (place, minds, member, me, vial);
    }

    /// <summary>
    /// **Out of formation, a member still nurses itself** — the chain goes straight past the follow
    /// to the nurse when `InParty` is false. Earlier, Vitomir returned first, so a
    /// companion told to stay put never healed itself.
    /// </summary>
    [Fact]
    public void A_companion_out_of_formation_still_nurses_itself()
    {
        using var files = Install.Open();
        var (_, minds, member, me, vial) = Nursing(files);
        me.Flags2 &= ~Actor.Flag2.InFormation;

        Alone(minds, member);
        minds.Tick(0.001f);

        Assert.Equal(AiReach.BloodItem, minds.LastReach.Reach);
        Assert.Same(vial, minds.LastReach.Item);
    }

    /// <summary>
    /// **A member that walked after its leader does not nurse on that think** (only a
    /// follow that did nothing lets the chain on). It nurses once it has caught up.
    /// </summary>
    [Fact]
    public void A_companion_that_walks_after_its_leader_does_not_nurse_on_that_think()
    {
        using var files = Install.Open();
        var (place, minds, member, me, _) = Nursing(files);
        Assert.True(Bodies.Gap(member.Thing, place.Player!.Thing) > Minds.FollowReach,
                    "the member should start out of reach of its leader");
        member.Walker.Stop();

        Alone(minds, member);
        minds.Tick(0.001f);

        Assert.True(member.Walker.Walking, "the follow should have walked");
        Assert.NotEqual(AiReach.BloodItem, minds.LastReach.Reach);
    }

    /// <summary>
    /// **Busy, in formation, it neither follows nor nurses** (the busy stamp is part of
    /// the same clause as the follow). Free, standing by its leader, it nurses.
    /// </summary>
    [Fact]
    public void A_busy_companion_in_formation_does_not_nurse()
    {
        using var files = Install.Open();
        var (place, minds, member, me, _) = Nursing(files);
        place.Move(member.Thing, place.Player!.Thing.Position + new Vector3(60f, 0f, 0f));
        member.Walker.Ground();

        me.BusyUntil = minds.Now + 10_000;
        Alone(minds, member);
        minds.Tick(0.001f);
        Assert.NotEqual(AiReach.BloodItem, minds.LastReach.Reach);

        me.BusyUntil = 0;
        Alone(minds, member);
        minds.Tick(0.001f);
        Assert.Equal(AiReach.BloodItem, minds.LastReach.Reach);
    }

    // --- the rally ------------------------------------------------------------------

    /// <summary>
    /// The attacker (a searcher by its current mode but not by its home one, so drawing it moves
    /// nothing), one real searcher beside it, and nobody else a searcher at heart.
    /// </summary>
    static (Mind Attacker, Mind Other) Brawl(Location place, Minds minds, Mind christof, params Mind[] party)
    {
        var not = party.Append(christof).ToArray();
        foreach (var m in minds.All.Where(m => !not.Contains(m))) m.Enter(AiMode.PedWander);
        var attacker = Stranger(minds, not);
        var other = Stranger(minds, [.. not, attacker]);
        attacker.Enter(AiMode.EnemySearch, andBase: false);
        other.Enter(AiMode.EnemySearch);
        place.Move(attacker.Thing, christof.Thing.Position + new Vector3(150f, 0f, 0f));
        place.Move(other.Thing, attacker.Thing.Position + new Vector3(0f, 100f, 0f));
        return (attacker, other);
    }

    /// <summary>
    /// **"Somebody else" is a random entry of the whole flood** round the pick (the draw at
    /// The pick moves only when that entry is a searcher at heart. With one
    /// searcher among everything within 750, it moves far less often than the two times in three
    /// Vitomir's draw among the searchers alone gave.
    /// </summary>
    [Fact]
    public void Somebody_else_is_drawn_from_everything_near_the_pick_not_from_the_searchers_alone()
    {
        using var files = Install.Open();
        var (place, minds, wilhem, christof) = Companions(files);
        place.Move(wilhem.Thing, christof.Thing.Position + new Vector3(-90f, 0f, 0f));
        var (attacker, other) = Brawl(place, minds, christof, wilhem);
        int crowd = Explosions.ExplosionDamage.Gather(place, attacker.Thing.Position, Minds.RallyNearby,
                                                      attacker.Thing.Sector).Count;

        const int trials = 300;
        int moved = 0;
        for (int i = 0; i < trials; i++)
        {
            wilhem.Aim(null, minds.Now);
            minds.Rally(christof, attacker.Thing);
            if (ReferenceEquals(wilhem.Target, other.Thing)) moved++;
        }

        Assert.True(crowd >= 4, $"the flood round the attacker holds {crowd}");
        Assert.True(moved > 0, $"never drawn from {crowd}");
        Assert.True(moved < trials * 0.45f, $"moved {moved} times in {trials} with {crowd} in the flood");
    }

    /// <summary>
    /// **One pick for the whole rally** (the rally's pick): once a member is sent after somebody
    /// else, the members after it are offered that one — never the attacker again unless the victim
    /// cannot see it. Earlier, each member started from the attacker.
    /// </summary>
    [Fact]
    public void The_rally_carries_one_pick_from_member_to_member()
    {
        using var files = Install.Open();
        var (place, minds, christofThing, _) = OldTown(files);
        var christof = christofThing.Mind!;
        var members = minds.All.Where(m => m.Thing.Actor is not null && !ReferenceEquals(m, christof)).Take(2).ToArray();
        foreach (var m in members.Append(christof))
        {
            m.Thing.Actor!.Flags |= Actor.Flag.Party;
            m.Thing.Actor.Flags2 |= Actor.Flag2.InFormation;
            m.Thing.Actor.Stance = PartyStance.Neutral;
            m.Enter(AiMode.Party);
        }
        place.Move(members[0].Thing, christofThing.Position + new Vector3(-90f, 0f, 0f));
        place.Move(members[1].Thing, christofThing.Position + new Vector3(0f, -90f, 0f));
        var (attacker, other) = Brawl(place, minds, christof, members);

        var order = place.PartyMembers().Select(t => t.Mind!).Where(m => !ReferenceEquals(m, christof)).ToList();
        Assert.Equal(2, order.Count);
        var (first, second) = (order[0], order[1]);

        int carried = 0;
        for (int i = 0; i < 400; i++)
        {
            first.Aim(null, minds.Now);
            second.Aim(null, minds.Now);
            minds.Rally(christof, attacker.Thing);
            if (!ReferenceEquals(first.Target, other.Thing)) continue;
            carried++;
            Assert.Same(other.Thing, second.Target);
        }
        Assert.True(carried > 0, "the first member never went after the other one");
    }

    // --- the manoeuvre (the enemy mode's tail) -------------------------------------------------------

    static (Location Place, Minds Minds, WorldThing Christof, Mind Mind) OldTown(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var christof = place.Steered!;
        var mind = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null
                                        && m.Class.MindFlags == 0 && (m.Flags & Mind.FlagUnaffected) == 0);
        return (place, minds, christof, mind);
    }

    /// <summary>
    /// **Too close is tested before the angle is rolled** (the roll comes after): a
    /// character touching its target takes nothing off the stream and thinks again in a second.
    /// </summary>
    [Fact]
    public void A_manoeuvre_touching_its_target_rolls_nothing()
    {
        using var files = Install.Open();
        var (_, minds, christof, mind) = OldTown(files);
        christof.Position = mind.Thing.Position + new Vector3(mind.Thing.Radius + christof.Radius - 4f, 0f, 0f);

        long before = minds.Random.Draws;
        minds.Manoeuvre(mind, christof);

        Assert.Equal(before, minds.Random.Draws);
        Assert.Equal(minds.Now + Minds.EnemyThink, mind.NextThink);
    }

    /// <summary>
    /// **A step rolled under the floor is the floor, not cut to the reach**. With
    /// <c>chaseDist</c> 0 every roll is under it, so a character 20 units short of touching steps 50.
    /// </summary>
    [Fact]
    public void A_manoeuvre_steps_at_least_fifty_even_close_in()
    {
        using var files = Install.Open();
        var (place, minds, wilhem, christof) = Companions(files);
        var enemy = Stranger(minds, wilhem, christof).Thing;
        wilhem.Class.ChaseDist = 0f;
        // Not a body, so the spot beside it is not refused for overlapping it.
        enemy.Flags |= WorldThing.Flag.DisableCollide;
        var start = wilhem.Thing.Position;
        place.Move(enemy, start + new Vector3(wilhem.Thing.Radius + enemy.Radius + 20f, 0f, 0f));

        int walked = 0;
        for (int i = 0; i < 8; i++)
        {
            place.Move(wilhem.Thing, start);
            wilhem.Walker.Stop();
            minds.Manoeuvre(wilhem, enemy);
            if (!wilhem.Walker.Walking) continue;
            walked++;
            var step = wilhem.Walker.Destination - start;
            Assert.InRange(new Vector2(step.X, step.Y).Length(), 44f, 56f);
        }
        Assert.True(walked > 0, "no manoeuvre walked");
    }

    /// <summary>
    /// **With no ground under the spot, the walk at the target is on the target's node and with the
    /// spot's flags** — so the move order's test of an occupied point applies, and the
    /// target is standing on it. Vitomir passed its own node and skipped the test until then.
    /// </summary>
    [Fact]
    public void A_manoeuvre_with_no_ground_under_its_spot_asks_to_walk_onto_its_target_and_is_refused()
    {
        using var files = Install.Open();
        var (place, minds, christof, mind) = OldTown(files);
        mind.Class.ChaseDist = 100f;
        // High above him: every spot the manoeuvre picks is in the air.
        mind.Thing.Position = christof.Position + new Vector3(30f, 0f, 600f);
        mind.Walker.Stop();

        minds.Manoeuvre(mind, christof);

        Assert.Same(christof, mind.Walker.Occupant);
        Assert.False(mind.Walker.Walking);
        Assert.Equal(minds.Now + mind.Class.MoveRate, mind.NextThink);
    }

    // --- pedwander's ready test --------------------------------------------

    /// <summary>
    /// **A restrained wanderer rolls no spot** (the restraint test comes before the roll): only the
    /// re-think delay comes off the stream. Earlier, it rolled a spot every think and the move
    /// order refused it.
    /// </summary>
    [Fact]
    public void A_restrained_wanderer_rolls_no_spot()
    {
        using var files = Install.Open();
        var (_, minds, _, _) = OldTown(files);
        var ped = minds.All.First(m => m.Mode == AiMode.PedWander && m.SpotCount > 0 && m.Thing.Actor is not null);
        ped.Walker.Stop();
        ped.Thing.Actor!.Flags2 |= Actor.Flag2.Restrained;

        Alone(minds, ped);
        long before = minds.Random.Draws;
        minds.Tick(0.001f);

        Assert.Equal(before + 1, minds.Random.Draws);
        Assert.True(ped.NextThink >= minds.Now + Minds.WanderFloor, "the delay, not the out-of-range wait");
        Assert.False(ped.Moving);

        // Let go, it rolls the spot as well.
        ped.Thing.Actor.Flags2 &= ~Actor.Flag2.Restrained;
        Alone(minds, ped);
        before = minds.Random.Draws;
        minds.Tick(0.001f);
        Assert.True(minds.Random.Draws > before + 1 || ped.NextThink == minds.Now + Minds.OutOfRangeWait,
                    "free, it should have rolled a spot");
    }
}
