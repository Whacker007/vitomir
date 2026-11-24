// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Treasure and what a death or an opening leaves (`rpgActor::Kill`'s drop,
/// the container's open).
/// </summary>
public class LootTests
{
    static Location Open(GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");

    [Fact]
    public void Game_ntt_loads_what_the_file_defines_and_nothing_else()
    {
        using var files = Install.Open();
        var t = Open(files).Treasure!;
        Assert.True(t.Loaded);
        Assert.Equal(17, t.Groups.Count);
        Assert.Equal(24, t.Alters.Count);
        Assert.Equal(203, t.Categories.Count);
        Assert.Equal(160, t.ClassCount);
        Assert.Equal(529, Enumerable.Range(0, Treasure.Classes).Sum(c => t.Class(c).Count));
        // Every category name resolves, all to items, weapons and armour.
        Assert.All(t.Categories.Values, c => Assert.Equal(c.Templates.Length, c.Found));
        // The gather buffer is sixteen long and class 199 has eighteen lines, which it keeps.
        Assert.Equal(18, t.Class(199).Count);
        // The chance is stored as the engine stores it: (float)(atof(pct) × 0.01).
        Assert.Equal((float)(50 * 0.01), t.Class(2)[0].Chance);
    }

    [Fact]
    public void The_generator_is_the_engines_and_never_reaches_one()
    {
        var rng = new TreasureRng(0);
        Assert.Equal(0x3c6ef35fu, rng.Step());                         // 0 × a + c
        Assert.Equal(unchecked(0x3c6ef35fu * 0x19660du + 0x3c6ef35fu), rng.Step());
        // A unit draw is the next state's low 23 bits over 2^23 — the float trick, exactly.
        rng.State = 123;
        uint next = unchecked(123u * 0x19660du + 0x3c6ef35fu);
        Assert.Equal((next & 0x7fffff) / 8388608f, rng.Unit());
        // So the largest it can be is one step short of 1, and the chance test `r <= p` takes a
        // line of 100 every time.
        Assert.True(0x7fffff / 8388608f < 1f);
        // And the clock seed is milliseconds plus a thousand times the seconds into the hour.
        Assert.Equal(234u + 1000u * (56u + 60u * 7u), TreasureRng.Clock(new DateTime(2026, 9, 21, 22, 7, 56, 234)));
    }

    /// <summary>
    /// Class 2 — a small chest — is `baseloot 50` then `baseitems 90`. **A draw per line** makes
    /// that 50% loot, 45% items and 5% nothing; one draw walked down a cumulative ladder would make
    /// it 50%, 40% and 10%.
    /// </summary>
    [Fact]
    public void Each_line_of_a_class_takes_a_draw_of_its_own()
    {
        using var files = Install.Open();
        var t = Open(files).Treasure!;
        t.Random.State = 12345;
        var loot = t.Categories["baseloot"].Templates.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int n = 40_000, none = 0, fromLoot = 0;
        for (int i = 0; i < n; i++)
        {
            if (t.Roll(2) is not { } r) none++;
            else if (loot.Contains(r.Template)) fromLoot++;
        }
        Assert.InRange(none / (double)n, 0.04, 0.06);
        Assert.InRange(fromLoot / (double)n, 0.485, 0.515);
    }

    [Fact]
    public void An_undefined_class_gives_nothing_and_draws_nothing()
    {
        using var files = Install.Open();
        var t = Open(files).Treasure!;
        Assert.Empty(t.Class(87));                     // 62 townsfolk templates carry it
        t.Random.State = 99;
        Assert.Null(t.Roll(87));
        Assert.Equal(99u, t.Random.State);
        Assert.Null(t.Roll(300));                      // past the table
    }

    [Fact]
    public void Cash_from_a_line_with_an_alteration_takes_its_amount()
    {
        using var files = Install.Open();
        var t = Open(files).Treasure!;
        t.Random.State = 7;
        var seen = new List<int>();
        for (int i = 0; i < 4000 && seen.Count < 50; i++)
            if (t.Roll(2) is { Template: var name, Item: { } item } && (item.Flags & ItemFlags.InstantCash) != 0)
                seen.Add(item.Amount);
        Assert.NotEmpty(seen);
        // `baseloot cash0`: 1 to 5 — not the silver coins' own 10.
        Assert.All(seen, a => Assert.InRange(a, 1, 5));
        // A forced roll always takes the first line, and still costs its draw.
        uint before = t.Random.State = 5;
        var forced = t.Roll(2, force: true);
        Assert.NotNull(forced);
        Assert.Contains(forced.Template, t.Categories["baseloot"].Templates);
        Assert.NotEqual(before, t.Random.State);
    }

    [Fact]
    public void A_scenes_treasure_class_overrides_its_templates_and_only_a_character_or_container_keeps_one()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var placed = NsdThing.Placed("container_smallChest", Vector3.Zero, Vector3.Zero);
        var chest = Location.Build(new NsdThing { Tags = [.. placed.Tags, NodTag.Of(Tags.TreasureClass, 44u)] }, templates, 0, 1);
        Assert.Equal(44, chest.TreasureClass);
        Assert.Equal(2, Location.Build(placed, templates, 1, 2).TreasureClass);   // the template's is 2, not 3
        var item = NsdThing.Placed("broadSword", Vector3.Zero, Vector3.Zero);
        Assert.Equal(0, Location.Build(new NsdThing { Tags = [.. item.Tags, NodTag.Of(Tags.TreasureClass, 44u)] }, templates, 2, 3).TreasureClass);
    }

    /// <summary>
    /// `container_barrel2` names a rat as its DROPITEM, and the DROPITEM is asked first — so all 48
    /// placed never give treasure, whatever class a scene gave them.
    /// </summary>
    [Fact]
    public void A_barrel_with_a_rat_in_it_gives_the_rat_beside_it()
    {
        using var files = Install.Open();
        var place = Open(files);
        Assert.SkipWhen(!place.Templates!.Has("container_barrel2"), "no container_barrel2 in the install");
        var barrel = place.Spawn("container_barrel2", place.Player!.Thing)!;
        Assert.Equal("Rat_small_wander", barrel.DropItem, ignoreCase: true);
        int before = place.Things.Count;

        Assert.True(Container.Open(barrel, place.Player!.Thing));

        // A barrel is `THING_FLAG_NOSCATTER` (0x8000, "things spawned from this thing will not be
        // scattered, just dropped in place"), so the rat comes out exactly where the barrel stood.
        var rat = place.Things.Single(t => t.Template.Equals(barrel.DropItem, StringComparison.OrdinalIgnoreCase));
        Assert.True(barrel.Has(WorldThing.Flag.NoScatter));
        Assert.Equal(barrel.Position, rat.Position);
        Assert.Equal(before + 1 - (barrel.Removed ? 1 : 0) + (barrel.DestroyTemplate.Length > 0 ? 1 : 0), place.Things.Count);
    }

    /// <summary>
    /// A chest scatters: what it gives is put just clear of it — the two radii times 1.1 out along
    /// the chest's facing, or the next of the eight directions round with ground under it.
    /// </summary>
    [Fact]
    public void A_chest_puts_what_it_gives_just_clear_of_itself()
    {
        using var files = Install.Open();
        var place = Open(files);
        var chest = place.Spawn("container_smallChest", place.Player!.Thing)!;
        Assert.False(chest.Has(WorldThing.Flag.NoScatter));
        var sword = place.Make("broadSword")!;
        place.PlaceNear(sword, chest, 0f, Loot.PlaceFlags);
        float gap = Vector2.Distance(new(sword.Position.X, sword.Position.Y), new(chest.Position.X, chest.Position.Y));
        Assert.Equal((sword.Radius + chest.Radius) * 1.1f, gap, 0.5f);
        Assert.True(sword.InWorld);
    }

    /// <summary>
    /// **Where a chest's loot lands** (from play: *"there's a standard spot I'm sure for
    /// all the chests"*). The open hands the item it made to the spawn-near, which runs the placing on the
    /// chest with distance 0 and flags 0xb. The spot is the chest's position plus its **forward** —
    /// its matrix's second row (z dropped), which is the model's own +y since a
    /// vector goes through the matrix as a row — times the two radii (each counted only
    /// with a collide shape, its radius and height) times 1.1, lowered onto the floor from
    /// 60 up; the item faces out along it (the direction goes in its
    /// second row). So each chest has one spot a little in front of it, which moves only with the
    /// item's own radius — and an item whose model is longer than its radius reaches back into the
    /// chest, as in 1.1. All three chests in the monastery, opened as a click opens them,
    /// put what they give there, on the floor, and none on itself.
    /// </summary>
    [Fact]
    public void A_monastery_chest_opened_puts_its_loot_straight_in_front_of_it_on_the_floor()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", things: true, sky: false, scene: "MON1_8_2", player: "Christof");
        var chests = place.Things.Where(t => t.Type == ThingType.Container && !t.Has(WorldThing.Flag.NoScatter)).ToArray();
        Assert.Equal(3, chests.Length);
        foreach (var chest in chests)
        {
            uint seed = 1;
            for (; seed < 1000; seed++)
            {
                place.Treasure!.Random.State = seed;
                if (place.Treasure.Roll(chest.TreasureClass) is not null) break;
            }
            place.Treasure!.Random.State = seed;
            Assert.True(Container.Open(chest, place.Player!.Thing));
            var loot = place.Things.Last();
            Assert.NotNull(loot.Item);

            float yaw = chest.Orientation.Z * (MathF.PI / 180f);
            var forward = new Vector2(-MathF.Sin(yaw), MathF.Cos(yaw));
            float reach = ((loot.Collide != CollideShape.None ? loot.Radius : 0f)
                           + (chest.Collide != CollideShape.None ? chest.Radius : 0f)) * 1.1f;
            var spot = new Vector2(chest.Position.X, chest.Position.Y) + forward * reach;
            Assert.Equal(spot.X, loot.Position.X, 0.01f);
            Assert.Equal(spot.Y, loot.Position.Y, 0.01f);
            Assert.True(reach > chest.Radius, $"{chest.Template}'s loot is {reach} out, inside its radius {chest.Radius}");
            var floor = place.FloorUnder(new Vector3(spot, chest.Position.Z + 60f), chest);
            Assert.NotNull(floor);
            Assert.Equal(floor!.Value.Z, loot.Position.Z, 0.01f);
            Assert.True(MathF.Abs(MathF.IEEERemainder(loot.Orientation.Z - chest.Orientation.Z, 360f)) < 0.5f,
                        $"{loot.Template} faces {loot.Orientation.Z}, the chest {chest.Orientation.Z}");
        }
    }

    [Fact]
    public void A_chest_is_filled_once_with_one_roll_of_its_class()
    {
        using var files = Install.Open();
        var place = Open(files);
        var chest = place.Spawn("container_smallChest", place.Player!.Thing)!;
        Assert.Equal(2, chest.TreasureClass);
        // A seed whose roll gives something, found the way the roll would find it.
        uint seed = 1;
        for (; seed < 1000; seed++)
        {
            place.Treasure!.Random.State = seed;
            if (place.Treasure.Roll(2) is not null) break;
        }
        place.Treasure!.Random.State = seed;
        var expected = place.Treasure.Roll(2)!.Template;
        place.Treasure.Random.State = seed;
        int before = place.Things.Count;

        Assert.True(Container.Open(chest, null));                       // broken open, by nobody

        Assert.Equal(before + 1, place.Things.Count);
        var made = place.Things.Last();
        Assert.Equal(expected, made.Template, ignoreCase: true);
        Assert.NotNull(made.Item);
        Assert.True(made.InWorld);
        // Opened is opened: no second fill.
        Assert.False(Container.Open(chest, place.Player!.Thing));
        Assert.Equal(before + 1, place.Things.Count);
    }

    /// <summary>
    /// A character's DROPITEM comes out **at its death**, on the floor beside the body — not when
    /// the body is taken away, which is where Vitomir used to put it — and a body going drops
    /// nothing at all.
    /// </summary>
    [Fact]
    public void A_death_drops_its_dropitem_beside_the_body_at_once_and_the_body_going_drops_nothing()
    {
        using var files = Install.Open();
        var place = Open(files);
        Assert.SkipWhen(!place.Templates!.Has("blackKnight"), "no blackKnight in the install");
        var knight = place.Spawn("blackKnight", place.Player!.Thing)!;
        Assert.Equal("battleaxeExq", knight.DropItem, ignoreCase: true);
        Assert.Equal(141, knight.TreasureClass);                        // inert: the DROPITEM wins
        // The killing blow's own blood is not what this counts.
        int Count() => place.Things.Count(t => t.Template != WoundEffects.Blood);
        int before = Count();

        Assert.True(Damage.Kill(knight, place.Player!.Thing, HitKind.Script, DamageType.Normal, 0));

        var axe = place.Things.Single(t => t.Template.Equals("battleaxeExq", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(knight.Position, axe.Position);
        int after = Count();
        Assert.Equal(before + 1, after);

        place.Remove(knight);
        Assert.Equal(after - 1 + (knight.DestroyTemplate.Length > 0 ? 1 : 0), Count());
        Assert.DoesNotContain(place.Things, t => ReferenceEquals(t, knight));
        Assert.Single(place.Things, t => t.Template.Equals("battleaxeExq", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_death_with_no_dropitem_rolls_the_characters_class()
    {
        using var files = Install.Open();
        var place = Open(files);
        var templates = place.Templates!;
        // A character of class 141 with no DROPITEM of its own.
        string? who = templates.Names.FirstOrDefault(n =>
            templates.Resolve(n) is var tags
            && tags.TryGetValue(Tags.Type, out var ty) && ty.AsUInt() == (uint)ThingType.Actor
            && tags.TryGetValue(Tags.TreasureClass, out var tc) && tc.AsUInt() == 141
            && !(tags.TryGetValue(Tags.DropItem, out var di) && di.AsString().Length > 0));
        Assert.SkipWhen(who is null, "no class-141 character without a DROPITEM");
        uint seed = 1;
        for (; seed < 2000; seed++)
        {
            place.Treasure!.Random.State = seed;
            if (place.Treasure.Roll(141) is not null) break;
        }
        place.Treasure!.Random.State = seed;
        var expected = place.Treasure.Roll(141)!.Template;
        var body = place.Spawn(who!, place.Player!.Thing)!;
        place.Treasure.Random.State = seed;

        Damage.Kill(body, null, HitKind.Script, DamageType.Normal, 0);

        Assert.Contains(place.Things, t => t.Template.Equals(expected, StringComparison.OrdinalIgnoreCase) && t.Item is not null);
    }

    [Fact]
    public void A_script_remove_drops_nothing_but_its_destroy_template()
    {
        using var files = Install.Open();
        var place = Open(files);
        var knight = place.Spawn("blackKnight", place.Player!.Thing);
        Assert.SkipWhen(knight is null, "no blackKnight in the install");
        int count = place.Things.Count;
        place.Remove(knight!);
        Assert.DoesNotContain(place.Things, t => t.Template.Equals("battleaxeExq", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(count - 1 + (knight!.DestroyTemplate.Length > 0 ? 1 : 0), place.Things.Count);
    }
}
