// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The gaps a comparison with 1.1 of the `Rpg` module found and left for later,
/// built: behaviours of 1.1's that
/// Vitomir had taken the other way. Each test names the behaviour it pins.
/// </summary>
public class RpgGapTests
{
    static (Location Place, WorldThing Christof, DisciplineDefinitions Powers) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var powers = new DisciplineDefinitions(files);
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), powers);
        return (place, place.Steered!, powers);
    }

    [Fact]
    public void A_cast_that_fizzles_at_the_fire_leaves_the_caster_to_finish_the_gesture()
    {
        // The fire's "off" is the cancel unforced, and the caster is busy with the cast
        // animation when its Cast tag comes — so the cancel does nothing.
        using var files = Install.Open();
        var (place, christof, powers) = Open(files);
        var me = christof.Actor!;
        var celerity = me.Disciplines.Add(powers["Celerity"]!, 1);
        me.Stat[(int)ActorStat.Blood] = 100;
        place.Milliseconds = 10_000;

        Assert.Equal(OrderResult.Started, Cast.Order(christof, null, null, celerity, InputModifier.None));
        Assert.Equal(ActionMode.Cast, me.Action);
        int busy = me.BusyUntil;
        Assert.True(busy > place.Milliseconds, "the cast plays an animation");

        me.Stat[(int)ActorStat.Blood] = 0;          // spent meanwhile: the blood check at the fire fails
        place.Milliseconds += 100;
        Cast.Fire(christof);

        Assert.Equal(ActionMode.Cast, me.Action);
        Assert.Same(celerity, me.PendingDiscipline);
        Assert.Equal(busy, me.BusyUntil);
        Assert.Equal(0f, me.Stat[(int)ActorStat.Blood]);

        // Once the gesture is over, the next order's cancel finds it free and ends the cast.
        place.Milliseconds = busy + 1;
        Actions.Cancel(christof);
        Assert.Equal(ActionMode.None, me.Action);
    }

    [Fact]
    public void A_fire_with_the_caster_free_does_put_it_back_to_nothing()
    {
        // The same unforced cancel acts on a caster that is not busy — a fire straight from
        // the order, with no animation, or a script's.
        using var files = Install.Open();
        var (place, christof, powers) = Open(files);
        var me = christof.Actor!;
        var celerity = me.Disciplines.Add(powers["Celerity"]!, 1);
        place.Milliseconds = 10_000;
        me.Action = ActionMode.Cast;
        me.PendingDiscipline = celerity;
        me.BusyUntil = 0;
        me.Stat[(int)ActorStat.Blood] = 0;

        Cast.Fire(christof);

        Assert.Equal(ActionMode.None, me.Action);
    }

    [Fact]
    public void A_cast_approach_that_gives_up_leaves_a_busy_caster_walking()
    {
        // The give-up is the cancel unforced — a caster a flinch made busy on the way keeps
        // ACTION 22 until it is free, and the next look gives up then.
        using var files = Install.Open();
        var (place, christof, powers) = Open(files);
        var me = christof.Actor!;
        var celerity = me.Disciplines.Add(powers["Celerity"]!, 1);
        place.Milliseconds = 10_000;
        me.Action = ActionMode.ApproachCast;
        me.PendingDiscipline = celerity;
        me.Target = null;                           // gone: nothing to walk at
        me.ActionRetryAt = 0;
        me.BusyUntil = 10_500;

        Actions.Tick(christof, place.Milliseconds);
        Assert.Equal(ActionMode.ApproachCast, me.Action);
        Assert.Same(celerity, me.PendingDiscipline);

        place.Milliseconds = 10_600;
        Actions.Tick(christof, place.Milliseconds);
        Assert.Equal(ActionMode.None, me.Action);
        Assert.Null(me.PendingDiscipline);
    }

    static Item Flask(Location place, int slot, (int, int) cell)
    {
        var flask = Item.From("holyWater", place.Templates!.Resolve("holyWater"))!;
        flask.Location = slot;
        flask.Cell = cell;
        return flask;
    }

    [Fact]
    public void A_wearable_item_is_never_used_and_says_nothing()
    {
        // The icon's grid mask (by the item's type) has a paperdoll bit — a ring,
        // an amulet, a torch, a sword — and the use answers 0 before anything else. Vitomir used to
        // run a ring's effects and spend a charge.
        using var files = Install.Open();
        var (place, christof, _) = Open(files);
        var me = christof.Actor!;
        foreach (var type in new[] { ItemType.Rings, ItemType.Neckwear, ItemType.Utility, ItemType.Armor, ItemType.Weapon })
        {
            var item = new Item { Name = type.ToString(), Type = type, Amount = 3, Location = Slot.Inventory };
            me.Inventory.Add(item);
            Assert.Equal(UseResult.Refused, Items.Use(christof, item, place.Milliseconds, null));
            Assert.Equal(3, item.Amount);
            Assert.Null(me.Held);
        }
        // A carried-only type still is: a general item spends its charge.
        var bag = new Item { Name = "bag", Type = ItemType.General, Amount = 3, Location = Slot.Inventory };
        me.Inventory.Add(bag);
        Assert.Equal(UseResult.Used, Items.Use(christof, bag, place.Milliseconds, null));
        Assert.Equal(2, bag.Amount);
    }

    [Fact]
    public void A_thrown_weapon_goes_into_an_empty_hand_and_only_an_empty_one()
    {
        // A weapon whose attack is THROWN, with nothing in the hand, goes onto
        // the cursor — out of the pack.
        using var files = Install.Open();
        var (place, christof, _) = Open(files);
        var me = christof.Actor!;
        var flask = Flask(place, Slot.Inventory, (0, 0));
        var second = Flask(place, Slot.Inventory, (1, 0));
        Assert.True(UseGates.Thrown(flask), "holy water is thrown");
        me.Inventory.Add(flask);
        me.Inventory.Add(second);

        Assert.Equal(UseResult.Held, Items.Use(christof, flask, place.Milliseconds, null));
        Assert.Same(flask, me.Held);
        Assert.DoesNotContain(flask, me.Inventory.Items);

        Assert.Equal(UseResult.Refused, Items.Use(christof, second, place.Milliseconds, null));
        Assert.Same(flask, me.Held);
        Assert.Contains(second, me.Inventory.Items);
    }

    [Fact]
    public void A_scroll_goes_to_the_hand_only_when_it_names_a_discipline()
    {
        // The scroll's discipline set, and nothing in the hand.
        using var files = Install.Open();
        var (place, christof, _) = Open(files);
        var me = christof.Actor!;
        var blank = new Item { Name = "blank", Type = ItemType.Scroll, Amount = 1, Location = Slot.Inventory };
        var scroll = new Item { Name = "scroll", Type = ItemType.Scroll, Amount = 1, Location = Slot.Inventory, ScrollDiscipline = "Celerity" };
        me.Inventory.Add(blank);
        me.Inventory.Add(scroll);

        Assert.Equal(UseResult.Refused, Items.Use(christof, blank, place.Milliseconds, null));
        Assert.Null(me.Held);
        Assert.Equal(UseResult.Held, Items.Use(christof, scroll, place.Milliseconds, null));
        Assert.Same(scroll, me.Held);
    }

    [Fact]
    public void A_thrown_weapon_used_from_the_belt_leaves_it_and_the_belt_refills()
    {
        // The belt's use answers 1 when the item left the slot — a thrown weapon taken into
        // the hand — and the refill brings the next one of the same template up.
        using var files = Install.Open();
        var (place, christof, _) = Open(files);
        var me = christof.Actor!;
        var flask = Flask(place, Slot.QuickUse, (2, 0));
        var spare = Flask(place, Slot.Inventory, (0, 0));
        me.Inventory.Add(flask);
        me.Inventory.Add(spare);

        QuickSlots.Use(christof, flask, autoload: true, it => Items.Use(christof, it, place.Milliseconds, null).ToString());

        Assert.Same(flask, me.Held);
        Assert.Equal(Slot.QuickUse, spare.Location);
        Assert.Equal(2, spare.Cell.X);
    }

    [Fact]
    public void A_character_killed_while_walking_is_stopped_through_the_walker()
    {
        // The kill stops a walking walker (movetype 1) itself — whichever walker
        // drives it; Vitomir stopped only a mind's, so a mindless or steered character walked on.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Player!.Thing;
        Assert.Null(christof.Mind);
        Assert.Equal(WorldThing.MoveWalker, christof.MoveType);
        var walker = place.WalkerFor(christof)!;
        var there = christof.Position + new System.Numerics.Vector3(120f, 0f, 0f);
        walker.WalkTo(there, place.Walk!.Find(there));
        new Simulation(place).Step(1f / 30f);
        Assert.True(walker.Walking, "he set off");
        christof.Walking = true;                        // what the frame's footfall edge keeps (it needs audio)

        Assert.True(Damage.Kill(christof, null, HitKind.Melee, DamageType.Lethal, place.Milliseconds));

        Assert.False(walker.Walking);
        Assert.False(christof.Walking);                 // the noises said once, not again by the frame's edge
        Assert.False(WalkerStop.OnDeath(christof));     // and a walker already stopped is left alone
    }

    [Fact]
    public void A_character_out_of_the_world_has_no_hit_reaction()
    {
        // The whole reaction — noises, flinch, beheading, the chunk, the wound — is for a
        // character in the world. A melee blow's chunk roll is the one every such blow
        // draws; out of the world it draws nothing, exactly as a shot, which never rolls for a chunk.
        static WorldThing Body()
        {
            var actor = new Actor { Type = ActorType.Human };
            actor.Health = actor.MaxHealth = 100;
            return new WorldThing
            {
                Index = 0, Guid = 90_010, Scene = new NsdThing(), Template = "test", Type = ThingType.Actor, Actor = actor,
            };
        }
        var melee = new Rng(7);
        var shot = new Rng(7);
        var hit = Body();
        Assert.False(hit.InWorld);
        Damage.Apply(hit, null, HitKind.Melee, DamageType.Normal, 10f, melee, 1_000);
        Damage.Apply(Body(), null, HitKind.Projectile, DamageType.Normal, 10f, shot, 1_000);
        Assert.Equal(90f, hit.Actor!.Health);
        Assert.Equal(shot.Unit(), melee.Unit());
    }

    [Fact]
    public void A_walk_refused_because_somebody_stands_there_leaves_the_walker_and_its_action_alone()
    {
        // The order refuses before the cancel and before the walker is touched. The enemy
        // manoeuvre's fallback walks onto its target's own spot without flag 0x100 and is
        // refused; Vitomir cancelled the character's action and stopped its walk first.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var christof = place.Steered!;
        var mind = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null
                                        && m.Class.MindFlags == 0 && (m.Flags & Mind.FlagUnaffected) == 0);
        mind.Class.ChaseDist = 100f;
        mind.Thing.Position = christof.Position + new System.Numerics.Vector3(30f, 0f, 600f);    // every spot in the air
        var away = mind.Thing.Position + new System.Numerics.Vector3(300f, 0f, 0f);
        Assert.True(mind.Walker.WalkTo(away, place.Walk!.Find(christof.Position), clamp: false));
        var me = mind.Thing.Actor!;
        me.Action = ActionMode.Attack;                  // walking up to swing
        me.Target = christof;
        me.BusyUntil = 0;

        minds.Manoeuvre(mind, christof);

        Assert.Same(christof, mind.Walker.Occupant);
        Assert.True(mind.Walker.Walking, "the last walk goes on");
        Assert.Equal(away, mind.Walker.Destination);
        Assert.Equal(ActionMode.Attack, me.Action);
        Assert.Same(christof, me.Target);
    }

    [Fact]
    public void The_free_spot_test_passes_over_plain_items_and_the_dead()
    {
        // The query's type mask 2 (things of type 1) and its flag 4 (a dead character).
        var item = new WorldThing { Index = 0, Guid = 1, Scene = new NsdThing(), Template = "i", Type = ThingType.Item };
        var weapon = new WorldThing { Index = 0, Guid = 2, Scene = new NsdThing(), Template = "w", Type = ThingType.Weapon };
        var dead = new Actor { Type = ActorType.Human, Flags = Actor.Flag.Dead };
        var body = new WorldThing { Index = 0, Guid = 3, Scene = new NsdThing(), Template = "b", Type = ThingType.Actor, Actor = dead };
        var alive = new WorldThing
        {
            Index = 0, Guid = 4, Scene = new NsdThing(), Template = "a", Type = ThingType.Actor, Actor = new Actor { Type = ActorType.Human },
        };
        Assert.True(FreeSpot.Passed(item));
        Assert.False(FreeSpot.Passed(weapon));          // a dropped sword still stands in the way
        Assert.True(FreeSpot.Passed(body));
        Assert.False(FreeSpot.Passed(alive));
    }
}
