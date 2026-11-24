// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What a fight in 1.1 does (the Monastery, Christof
/// and Wilhem against a Cappadocian, with two monks wandering the cloister), and the two places
/// Vitomir differed from it.
/// </summary>
public class MonasteryFightTests
{
    static WorldThing Actor(float x, float y) =>
        new()
        {
            Index = 0, Guid = 1, Scene = new Formats.NsdThing(), Template = "t",
            Type = ThingType.Actor, Actor = new World.Actor(), Position = new Vector3(x, y, 0),
        };

    /// <summary>
    /// **A melee swing keeps the striker busy for the attack's RELOADTIME**, divided by its attack
    /// speed — not for the length of the swing animation, which is STRIKETIME. The two
    /// differ on 60 of the 200 melee weapons. A Cappadocian's scythe strikes in 1 second and reloads
    /// in 1.5, and 1.1's Cappadocian is held 1.5 seconds by each swing, one every 1.6 to
    /// 1.8.
    /// </summary>
    [Fact]
    public void A_swing_keeps_the_striker_busy_for_its_reload_time()
    {
        var striker = Actor(0, 0);
        var target = Actor(0, 40);
        var scythe = new Weapon { Name = "scythe" };
        scythe.Attacks[0].StrikeTime = 1f;
        scythe.Attacks[0].ReloadTime = 1500;

        Actions.Strike(striker, target, scythe, nowMs: 10_000);
        Assert.Equal(11_500, striker.Actor!.BusyUntil);
        Assert.Equal(ActionMode.Swing, striker.Actor.Action);

        // Celerity doubles the attack speed and halves the wait; the division truncates.
        striker.Actor.BusyUntil = 0;
        striker.Actor.AttackSpeedScale = 2f;
        Actions.Strike(striker, target, scythe, nowMs: 20_000);
        Assert.Equal(20_750, striker.Actor.BusyUntil);

        // A ranged attack's wait is its reload whatever the attack speed, as it always was.
        var bow = new Weapon { Name = "bow" };
        bow.Attacks[0].Flags = WeaponFlag.Ranged;
        bow.Attacks[0].ReloadTime = 1200;
        striker.Actor.BusyUntil = 0;
        Actions.Strike(striker, target, bow, nowMs: 30_000);
        Assert.Equal(31_200, striker.Actor.BusyUntil);
    }

    /// <summary>
    /// **The engine's own "what is under this point"**, which the enemy's repositioning
    /// asks about the spot it has picked: a ray straight down from 50 units above the point, 140 long.
    /// So ground up to 50 above the point and 90 below it counts, where Vitomir's own window is 16
    /// above and 64 below. On a staircase the difference is whether a spot up the steps is found.
    /// </summary>
    [Fact]
    public void The_ground_under_a_spot_is_looked_for_the_engine_s_way()
    {
        using var files = Install.Open();
        var town = Location.Load(files, "P1_OTDY", things: false, sky: false);
        var walk = town.Walk!;
        var here = new Vector3(1505, 928, 0);
        int node = walk.Find(here);
        Assert.True(node >= 0);
        float floor = walk.Nodes[node].Height(here.X, here.Y);

        Assert.Equal(node, walk.Beneath(here with { Z = floor - 45 }));   // ground 45 above: found
        Assert.Equal(-1, walk.Find(here with { Z = floor - 45 }));          // Vitomir's window: not
        Assert.Equal(node, walk.Beneath(here with { Z = floor + 85 }));   // 85 below: found
        Assert.Equal(-1, walk.Beneath(here with { Z = floor - 55 }));     // 55 above: not
        Assert.Equal(-1, walk.Beneath(here with { Z = floor + 95 }));     // 95 below: not
    }

    /// <summary>
    /// **Event 14 goes out from the one who struck the blow**, not from the one who took it: the
    /// attack handler and the kill handler both load the attacker's own mind as
    /// the sender. So a pedestrian within 2,000 units is afraid of the attacker. 1.1's monks are
    /// afraid of Christof at every one of his swings; Vitomir's were afraid of whoever was being hit.
    /// </summary>
    [Fact]
    public void A_blow_frightens_the_bystanders_of_the_one_who_struck_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1);
        var christof = place.Steered!;
        Assert.NotNull(christof.Mind);
        bool Frightenable(Mind m) => m.BaseMode == AiMode.PedWander && m.Class.MindFlags == 0
                                     && (m.Flags & Mind.FlagUnaffected) == 0 && m.Target is null;
        var near = minds.All.Where(m => Frightenable(m)
                                        && Vector3.Distance(m.Thing.Position, christof.Position) < Minds.AssaultShoutRange)
                            .OrderBy(m => Vector3.Distance(m.Thing.Position, christof.Position)).ToList();
        Assert.True(near.Count >= 2, $"only {near.Count} pedestrians within earshot of Christof");
        var victim = near[0];
        var bystander = near[1];

        minds.AttackedBy(victim, christof);

        Assert.Equal(AiMode.Afraid, victim.Mode);
        Assert.Same(christof, victim.Target);
        Assert.Equal(AiMode.Afraid, bystander.Mode);
        Assert.Same(christof, bystander.Target);           // the attacker, not the one attacked
    }

    /// <summary>
    /// **A shout carries through the sector flood, not across a sphere**. In
    /// 1.1's Monastery a monk up in the cloister, 1,487 units from Christof's blows, flinches
    /// at every one of them; the same monk, 1,416 to 1,462 units from Wilhem's, never does. The flood
    /// out of the sector where Christof stood reaches the monk's sector; the one out of Wilhem's does
    /// not. Distance alone would have had him flinch at both.
    /// </summary>
    [Theory]
    [InlineData(-1356f, -582f, true)]     // where Christof swung from
    [InlineData(-1282f, -590f, false)]    // where Wilhem swung from, a few steps away
    public void A_shout_carries_as_far_as_the_sector_flood_reaches(float x, float y, bool heard)
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", things: true, sky: false, scene: "MON1_8_2", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1);
        var christof = place.Steered!;
        var monk = minds.All.First(m => m.BaseMode == AiMode.PedWander && m.Class.MindFlags == 0
                                        && (m.Flags & Mind.FlagUnaffected) == 0);
        var victim = minds.All.First(m => m.Thing.Template.Equals("cappadocian", StringComparison.OrdinalIgnoreCase));

        void Put(WorldThing thing, Vector3 at)
        {
            int node = place.Walk!.Find(at);
            Assert.True(node >= 0, $"no ground at {at}");
            place.Move(thing, at with { Z = place.Walk.Nodes[node].Height(at.X, at.Y) });
            thing.Walker?.Ground();
        }
        Put(monk.Thing, new Vector3(-680, 728, -54));
        Put(christof, new Vector3(x, y, -252));
        Put(victim.Thing, new Vector3(-1243, -578, -252));
        monk.Walker.Stop();

        minds.AttackedBy(victim, christof);

        Assert.Equal(heard, monk.Mode == AiMode.Afraid);
        if (heard) Assert.Same(christof, monk.Target);
    }
}

