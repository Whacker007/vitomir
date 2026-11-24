// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Items, the inventory list, and the ammo a ranged weapon spends out of it. Every rule asserted
/// here follow the original's spend and reload and the gate in front of
/// them in the AI's attack.
/// </summary>
public class AmmoTests
{
    static Item Ammo(int id, int amount, string name = "rounds") =>
        new() { Name = name, Type = ItemType.Ammo, ItemId = id, Amount = amount };

    static Weapon Gun(int ammoType = 7, int max = 10, int ammo = 10, int use = 1, bool ranged = true)
    {
        var w = new Weapon { Name = "gun" };
        var a = w.Attacks[0];
        a.AmmoType = ammoType; a.MaxAmmo = max; a.Ammo = ammo; a.AmmoUse = use;
        if (ranged) a.Flags |= WeaponFlag.Ranged;
        // The second record is real but uses nothing, which is the shipped shape for most weapons.
        w.Attacks[1].MaxAmmo = 0;
        return w;
    }

    [Fact]
    public void An_inventory_is_in_reverse_order_of_acquisition()
    {
        // The inventory pushes on the front. It matters: the weapon switch takes the
        // first candidate it meets, so the most recently picked up wins.
        var inv = new Inventory();
        var first = Ammo(1, 5, "first");
        var second = Ammo(1, 5, "second");
        inv.Add(first);
        inv.Add(second);

        Assert.Equal(["second", "first"], inv.Items.Select(i => i.Name));
    }

    [Fact]
    public void Reloading_takes_the_smallest_matching_stack()
    {
        // The reload keeps the candidate with the fewest rounds. Spending part-used stacks first
        // is what stops a full clip being poured into a half-empty magazine.
        var inv = new Inventory();
        inv.Add(Ammo(7, 10, "full"));
        inv.Add(Ammo(7, 3, "partial"));
        inv.Add(Ammo(7, 8, "middling"));
        inv.Add(Ammo(99, 1, "wrong type"));

        var gun = Gun(ammoType: 7, max: 10, ammo: 0);
        Assert.True(gun.Reload(inv));

        Assert.Equal(3, gun.Current.Ammo);
        Assert.DoesNotContain(inv.Items, i => i.Name == "partial");
        Assert.Equal(3, inv.Count);
    }

    [Fact]
    public void Reloading_fills_every_record_that_shares_the_ammo_type()
    {
        // The engine walks both of its two records unconditionally and clamps each at its own
        // capacity, so a weapon whose two attacks share a magazine gets both loaded. The pistol and
        // the revolver are the shipped examples: AMMOTYPE1 and AMMOTYPE2 are the same number.
        var w = new Weapon { Name = "revolver" };
        foreach (var a in w.Attacks) { a.AmmoType = 23; a.MaxAmmo = 6; a.Ammo = 0; a.AmmoUse = 1; }
        w.Attacks[1].MaxAmmo = 4;   // a deliberately different capacity, to prove the clamp is per-record

        var inv = new Inventory();
        inv.Add(Ammo(23, 6));
        Assert.True(w.Reload(inv));

        Assert.Equal(6, w.Attacks[0].Ammo);
        Assert.Equal(4, w.Attacks[1].Ammo);
    }

    [Fact]
    public void Reloading_a_part_full_magazine_throws_the_rest_of_the_stack_away()
    {
        // Faithful, and almost certainly intended: the stack is consumed whole and the magazine
        // clamps. It is the ordinary "reload discards the partial clip" rule.
        var inv = new Inventory();
        inv.Add(Ammo(7, 10));
        var gun = Gun(ammoType: 7, max: 10, ammo: 6);

        Assert.True(gun.Reload(inv));
        Assert.Equal(10, gun.Current.Ammo);   // not 16
        Assert.Equal(0, inv.Count);           // and the whole stack is gone
    }

    [Fact]
    public void A_full_magazine_is_not_reloaded_and_a_weapon_with_no_magazine_never_is()
    {
        var inv = new Inventory();
        inv.Add(Ammo(7, 10));

        Assert.False(Gun(ammoType: 7, max: 10, ammo: 10).Reload(inv));

        // MaxAmmo 0 is the engine's "this attack does not use ammo". Note that a melee weapon
        // usually does *not* reach zero this way: it keeps the constructor's default of 20 and
        // simply never spends, because only the ranged path calls Spend.
        var sword = Gun(ammoType: 0, max: 0, ammo: 0, ranged: false);
        Assert.False(sword.Reload(inv));
        Assert.False(sword.OutOfAmmo);

        Assert.Equal(1, inv.Count);
    }

    [Fact]
    public void Only_the_playable_cast_ever_spends_ammo()
    {
        // The spend tests the holder's thing type before touching a round. This is the single
        // biggest consequence of the whole file: an enemy crossbowman fires for ever.
        var enemy = Gun(ammoType: 7, max: 10, ammo: 10);
        enemy.Spend(holderIsPlayer: false, inventory: null);
        Assert.Equal(10, enemy.Current.Ammo);

        var player = Gun(ammoType: 7, max: 10, ammo: 10);
        player.Spend(holderIsPlayer: true, inventory: new Inventory());
        Assert.Equal(9, player.Current.Ammo);
    }

    [Fact]
    public void Spending_uses_the_records_own_ammo_use_and_clamps_at_zero()
    {
        // The chaingun is the shipped example: AMMOUSE1 is 3.
        var gun = Gun(ammoType: 33, max: 100, ammo: 4, use: 3);
        var empty = new Inventory();

        gun.Spend(true, empty);
        Assert.Equal(1, gun.Current.Ammo);
        gun.Spend(true, empty);
        Assert.Equal(0, gun.Current.Ammo);   // 1 - 3 clamps rather than going negative
    }

    [Fact]
    public void Firing_the_last_round_reloads_straight_away()
    {
        var inv = new Inventory();
        inv.Add(Ammo(7, 10));
        var gun = Gun(ammoType: 7, max: 10, ammo: 1);

        gun.Spend(holderIsPlayer: true, inventory: inv);

        Assert.Equal(10, gun.Current.Ammo);
        Assert.Equal(0, inv.Count);
        Assert.False(gun.OutOfAmmo);
    }

    [Fact]
    public void Out_of_ammo_is_uses_ammo_and_has_none()
    {
        Assert.True(Gun(max: 10, ammo: 0).OutOfAmmo);
        Assert.False(Gun(max: 10, ammo: 1).OutOfAmmo);
        Assert.False(Gun(max: 0, ammo: 0).OutOfAmmo);
    }

    [Fact]
    public void Every_weapon_that_asks_for_ammo_has_ammo_in_the_game_to_match()
    {
        // The corpus check, and the reason the ITEM_ID reading is safe: a weapon's WEAPON_AMMOTYPE
        // is an ITEM_ID, and every one of them resolves to a real item.
        using var files = Install.Open();
        var templates = new TemplateSet(files);

        var ammoIds = new Dictionary<int, string>();
        var wanted = new Dictionary<int, List<string>>();

        foreach (var name in templates.Names)
        {
            var tags = templates.Resolve(name);
            if (Item.From(name, tags) is { Type: ItemType.Ammo, ItemId: > 0 } item)
                ammoIds.TryAdd(item.ItemId, name);
            if (Weapon.From(name, tags) is not { } w) continue;
            for (int i = 0; i < w.AttackCount; i++)
                if (w.Attacks[i].MaxAmmo > 0 && w.Attacks[i].AmmoType > 0)
                    (wanted.TryGetValue(w.Attacks[i].AmmoType, out var l) ? l
                        : wanted[w.Attacks[i].AmmoType] = []).Add(name);
        }

        Assert.NotEmpty(wanted);
        var missing = wanted.Keys.Where(id => !ammoIds.ContainsKey(id)).ToArray();
        Assert.True(missing.Length == 0,
            $"ammo types with no item: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_ammo_type_ships_a_stack_small_enough_to_waste_nothing()
    {
        // The overflow in Reload is real, and the shipped data is built so that it almost never
        // has to happen: ammo comes in **graded stack sizes** - arrows and bolts as 1, 5, 10 and 20,
        // every gun's in a size that divides its magazine - and the engine always reaches for the
        // *smallest* stack it has, so a player carrying the small denominations loses nothing.
        //
        // There is exactly one exception in the whole game, and it looks like an oversight rather
        // than a decision: the **grenade launcher never sets WEAPON_MAXAMMO1**, so it keeps the
        // gameWeapon constructor's default of 20 - and grenades ship only in sixes, which
        // does not divide 20. Every other ranged weapon states its capacity. So a grenade launcher
        // loses rounds on some reload no matter how carefully it is supplied. Asserted by name so a
        // data change either way shows up here.
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var (smallest, largest) = AmmoStacks(templates);

        int pairs = 0;
        var wasteful = new List<string>();
        foreach (var name in templates.Names)
        {
            if (Weapon.From(name, templates.Resolve(name)) is not { } w) continue;
            for (int i = 0; i < w.AttackCount; i++)
            {
                var a = w.Attacks[i];
                if (a.MaxAmmo <= 0 || !smallest.TryGetValue(a.AmmoType, out int small)) continue;
                if (a.MaxAmmo % small != 0) wasteful.Add($"{name}:{a.MaxAmmo}/{small}");
                pairs++;
            }
        }
        Assert.True(pairs >= 20, $"only {pairs} weapon/ammo pairs checked");
        Assert.Equal(["grenadeLauncher:20/6", "weap_grenadeLauncher:20/6"], wasteful);
    }

    [Fact]
    public void But_a_big_enough_stack_still_overflows_a_small_magazine()
    {
        // The other half, so the register entry has a named example rather than a hypothetical.
        // A composite bow holds ten arrows and arrows also ship in twenties, so a player carrying
        // only the big bundles loses half of one every time they reload from empty.
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var (_, largest) = AmmoStacks(templates);

        var bow = Weapon.From("compositeBow", templates.Resolve("compositeBow"));
        Assert.NotNull(bow);
        int cap = bow!.Attacks[0].MaxAmmo;
        Assert.Equal(10, cap);
        Assert.Equal(20, largest[bow.Attacks[0].AmmoType]);

        // And the waste is exactly what the code says it is.
        var inv = new Inventory();
        inv.Add(Ammo(bow.Attacks[0].AmmoType, 20, "arrows_20"));
        bow.Attacks[0].Ammo = 0;
        Assert.True(bow.Reload(inv));
        Assert.Equal(10, bow.Attacks[0].Ammo);
        Assert.Equal(0, inv.Count);
    }

    static (Dictionary<int, int> Smallest, Dictionary<int, int> Largest) AmmoStacks(TemplateSet templates)
    {
        Dictionary<int, int> smallest = [], largest = [];
        foreach (var name in templates.Names)
        {
            if (Item.From(name, templates.Resolve(name)) is not { Type: ItemType.Ammo, ItemId: > 0 } a
                || a.Amount <= 0) continue;
            smallest[a.ItemId] = smallest.TryGetValue(a.ItemId, out int lo) ? Math.Min(lo, a.Amount) : a.Amount;
            largest[a.ItemId] = largest.TryGetValue(a.ItemId, out int hi) ? Math.Max(hi, a.Amount) : a.Amount;
        }
        return (smallest, largest);
    }
}
