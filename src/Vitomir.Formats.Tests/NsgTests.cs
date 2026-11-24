// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// The save reader against real saves. There is no length anywhere in a `.nsg`, so the only thing
/// that proves a sequential reader is a whole file landing exactly on EOF — and then the numbers
/// inside it matching what the player did.
/// </summary>
public class NsgTests
{
    static string[] Slots()
    {
        GameData.RequireInstall();
        string dir = Path.Combine(GameData.Dir!, "SaveGames");
        Assert.SkipUnless(Directory.Exists(dir), $"no SaveGames folder in {GameData.Dir}");
        var slots = Directory.EnumerateDirectories(dir)
                             .Where(d => File.Exists(Path.Combine(d, "!main.nsg")))
                             .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                             .ToArray();
        Assert.SkipWhen(slots.Length == 0, $"no saves in {dir}");
        return slots;
    }

    static NsgSave Read(string slot) =>
        NsgSave.ReadFile(Path.Combine(slot, "!main.nsg"));

    [Fact]
    public void Every_save_parses_to_its_last_byte()
    {
        var failures = new Dictionary<string, string>();
        int parsed = 0;
        foreach (var slot in Slots())
        {
            try { Read(slot); parsed++; }
            catch (NodFormatException e) { failures[Path.GetFileName(slot)] = e.Message; }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(kv => $"{kv.Key}: {kv.Value}")));
        Assert.True(parsed > 0);
    }

    [Fact]
    public void The_header_is_the_same_in_every_save()
    {
        foreach (var slot in Slots())
        {
            var save = Read(slot);
            Assert.Equal("NSG", save.Magic);
            Assert.Equal(5u, save.Version);
            // The header's three words after the version, where `SetupAdvance` said the next advance goes: −1, −1
            // and nothing until a script says, and a save made before the advance spends them keeps them
            // (a University Haven save: location 20, entrance 0, `ANZK_14_1.nsd`).
            if (save.FieldE4 == uint.MaxValue) Assert.Equal("", save.TextE8);
            else Assert.True(save.FieldE4 < (uint)save.Chronicle.Locations.Length, $"advance to location {save.FieldE4}");
            Assert.Equal(NsgSave.TeleportSlots, save.Teleports.Length);
            Assert.Equal(NsgSave.ClockWords, save.Clock.Length);
            Assert.Equal(NsgSave.ChronicleFlagWords, save.Chronicle.Flags.Length);
        }
    }

    /// <summary>
    /// The load screen's "(hh:mm:ss)" is printed from the game clock, and the clock is 18 words
    /// further into the file. If the reader has lost its place anywhere in between, these disagree.
    /// </summary>
    [Fact]
    public void The_clock_agrees_with_the_description_the_writer_printed()
    {
        foreach (var slot in Slots())
        {
            var save = Read(slot);
            var t = save.Elapsed;
            string stamp = $"({t.Hours:00}:{t.Minutes:00}:{t.Seconds:00})";
            Assert.EndsWith(stamp, save.Description.TrimEnd());
        }
    }

    [Fact]
    public void The_chronicle_carries_its_locations_and_the_map_it_is_showing()
    {
        foreach (var slot in Slots())
        {
            var c = Read(slot).Chronicle;
            Assert.EndsWith(".nsc", c.Name, StringComparison.OrdinalIgnoreCase);   // as it was asked for: `NEW_YORK.NSC` in an Orsi save
            Assert.NotEmpty(c.Locations);
            Assert.All(c.Locations, l => Assert.NotEmpty(l.Name));
            Assert.All(c.Locations, l => Assert.True(l.Exits.Length <= NscLocation.MaxExits));
            Assert.EndsWith(".nmf", c.MapFile, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Every_shop_carries_its_own_buy_and_sell_rates()
    {
        foreach (var slot in Slots())
            foreach (var shop in Read(slot).Shops)
            {
                Assert.NotEmpty(shop.Name);
                Assert.NotEmpty(shop.Stock);
                Assert.NotEqual(0u, shop.OwnerGuid);
                // The scripts pass 1.25 and 0.8; the save keeps whatever the shop actually has.
                Assert.Equal(1.25f, BitConverter.UInt32BitsToSingle(shop.BuyRate), 4);
                Assert.Equal(0.8f, BitConverter.UInt32BitsToSingle(shop.SellRate), 4);
            }
    }

    /// <summary>
    /// A shop's own <c>ITEM_SELLFLAGS</c> mask is what it will <em>buy</em> from the player, and it
    /// is a property of the shop rather than of the visit — so every record of the same shop agrees
    /// on it. It does not bound what the shop stocks: Unorna's mask has no plain-weapon bit and she
    /// sells stakes anyway, because the stock comes from her `.nbs` and the mask only gates trade-ins.
    /// </summary>
    [Fact]
    public void A_shops_sell_mask_belongs_to_the_shop_not_the_visit()
    {
        foreach (var slot in Slots())
        {
            var masks = new Dictionary<string, uint>();
            foreach (var shop in Read(slot).Shops)
            {
                Assert.NotEqual(0u, shop.SellFlags);
                if (masks.TryGetValue(shop.Name, out var seen)) Assert.Equal(seen, shop.SellFlags);
                else masks[shop.Name] = shop.SellFlags;
            }
        }
    }

    /// <summary>
    /// A `.nbs` line stays in the save after it is bought out: the count goes to zero and the
    /// instance goes away. An infinite line (`copies 0` in the file, −1 in memory) never runs out.
    /// And every line that still has stock carries a whole flattened item — template, names, icon,
    /// cost — so the stock reads without the templates.
    /// </summary>
    [Fact]
    public void A_bought_out_stock_line_keeps_its_place_with_no_instance()
    {
        foreach (var slot in Slots())
            foreach (var shop in Read(slot).Shops)
                for (int i = 0; i < shop.Stock.Length; i++)
                {
                    var line = shop.Stock[i];
                    Assert.True(line.Kind is 1 or 2);
                    if (line.SoldOut) Assert.Null(line.Treasure);
                    if (line.Infinite) Assert.Equal(0u, line.Remaining);
                    if (line.Treasure is not { } t) continue;
                    Assert.Equal((uint)i, t.Slot);
                    Assert.NotEmpty(t.Template);
                    Assert.NotEmpty(t.DescriptionId);
                    Assert.NotNull(t.Icon);
                    Assert.InRange(t.BaseCost, 1u, 1_000_000u);
                    // Weapons carry weapon numbers; nothing else does.
                    if (t.Type is not (NsgThingType.Weapon or NsgThingType.Actor))
                        Assert.Equal(0u, t.WeaponDamage);
                }
    }

    /// <summary>
    /// The original's shop bug, pinned as a test rather than a note: the save holds one record per
    /// visit, not per shop, and the duplicates share a shopkeeper GUID. If a future reader ever
    /// stops seeing this, something in the walk has changed.
    /// </summary>
    [Fact]
    public void The_save_holds_more_than_one_record_for_the_same_shopkeeper()
    {
        bool sawDuplicate = false;
        foreach (var slot in Slots())
        {
            var byOwner = Read(slot).Shops.GroupBy(s => s.OwnerGuid);
            foreach (var group in byOwner)
            {
                if (group.Count() <= 1) continue;
                sawDuplicate = true;
                // Same shopkeeper, same shop, different stock — each is one visit's roll.
                Assert.Single(group.Select(s => s.Name).Distinct());
            }
        }
        Assert.True(sawDuplicate, "no save had a duplicated shop record");
    }

    /// <summary>
    /// The party section is a **roster**, not a single character. It asserted one player until
    /// When a play went far enough to recruit Wilhelm and the collection came back
    /// with two — the third time in one day that a save-corpus invariant turned out to have been
    /// written from the only kind of save anyone had yet made.
    ///
    /// The engine's own cap is **four** (the character pane asserts `index >= 0 && index < MAX_PARTY`
    /// against 4, and the stance array is four slots), and `CurrentPlayerGuid` names whichever of
    /// them the player is driving — which is the part worth keeping, because it is what decides
    /// whose sheet the character pane shows.
    /// </summary>
    [Fact]
    public void The_party_is_a_roster_of_up_to_four_with_one_of_them_current()
    {
        foreach (var slot in Slots())
        {
            var save = Read(slot);
            Assert.InRange(save.Party.Players.Length, 1, MaxParty);
            foreach (var member in save.Party.Players)
            {
                Assert.Equal(NsgThingType.Player, member.Type);
                Assert.NotNull(member.Actor);
                Assert.NotNull(member.Player);
            }
            // Every guid distinct, and the current one is one of them rather than merely the first.
            Assert.Equal(save.Party.Players.Length,
                         save.Party.Players.Select(p => p.Guid).Distinct().Count());
            Assert.Contains(save.Party.CurrentPlayerGuid, save.Party.Players.Select(p => p.Guid));
            Assert.Equal(MaxParty, save.Party.Junk.Length);
        }
    }

    /// <summary>
    /// `MAX_PARTY`, from the character pane's assert and from the four stance slots. The `Junk`
    /// array beside the roster is four long too, which is suggestive and is not yet read.
    /// </summary>
    const int MaxParty = 4;

    /// <summary>
    /// The sheet the equipment work was checked against: nineteen stats and ten soaks, base and
    /// current, with health inside the same block. If the reader is one field out anywhere in the
    /// actor these stop being plausible numbers.
    /// </summary>
    [Fact]
    public void The_player_sheet_reads_as_a_character_sheet()
    {
        foreach (var slot in Slots())
        {
            var player = Read(slot).Party.Players[0];
            var actor = player.Actor!;
            Assert.Equal(NsgActor.StatCount, actor.Stats.Length);
            Assert.Equal(NsgActor.StatCount, actor.BaseStats.Length);
            Assert.Equal(NsgActor.SoakCount, actor.Soaks.Length);
            Assert.Equal(NsgActor.SoakCount, actor.BaseSoaks.Length);
            Assert.InRange(actor.Health, 0f, actor.MaxHealth);
            Assert.InRange(actor.MaxHealth, 1f, 10_000f);
            Assert.All(actor.Stats, s => Assert.InRange(s, 0f, 1000f));
            Assert.All(actor.Soaks, s => Assert.InRange(s, -1000f, 1000f));
            // Every player is asserted to have an unarmed weapon by the writer itself.
            Assert.NotNull(actor.Inventory.UnarmedWeapon);
        }
    }

    /// <summary>
    /// Carried items are things in their own right, so a wrong turn inside one derails the rest of
    /// the file. Each one must name a template and a DESCRIPTIONID, and each equipped one must
    /// record no parent — the writer zeroes the link and restores the relationship from the slot.
    /// </summary>
    [Fact]
    public void Carried_items_name_their_template_and_equipped_ones_record_no_parent()
    {
        foreach (var slot in Slots())
        {
            var actor = Read(slot).Party.Players[0].Actor!;
            foreach (var item in actor.Inventory.Items)
            {
                Assert.NotEmpty(item.Name);
                Assert.NotEmpty(item.DescriptionId);
                Assert.NotNull(item.Template);
                var rpg = Assert.IsType<NsgItem>(item.Item);
                // Exactly one slot bit, and never both inventory and a worn slot.
                Assert.Equal(1, System.Numerics.BitOperations.PopCount(rpg.Location));
                if (rpg.IsEquipped) Assert.Equal(0u, item.Links.ParentGuid);
            }
        }
    }

    /// <summary>
    /// No chronicle in the shipped game declares <c>save()</c>, so every chronicle record's script
    /// state is empty and the two closing tags sit next to each other.
    /// </summary>
    [Fact]
    public void No_codex_object_in_a_shipped_save_writes_script_state()
    {
        foreach (var slot in Slots())
        {
            var save = Read(slot);
            foreach (var (_, record) in save.Chronicle.Codices)
                Assert.Empty(record.ScriptState);
        }
    }

    /// <summary>
    /// **A discipline running when the game saved** leaves its effect script's own words in the file
    /// — 89 shipped classes declare <c>save</c>, the discipline effects among them — and the reader
    /// steps over them by the class's layout. An autosave has Celerity running on
    /// Christof: `EffectDiscCelerity` saved one word, its trail's GUID, and until then the reader
    /// stopped there. A save with no such effect has nothing to check and passes.
    /// </summary>
    [Fact]
    public void A_running_disciplines_script_state_is_stepped_over()
    {
        foreach (var slot in Slots())
            foreach (var player in Read(slot).Party.Players)
                foreach (var effect in player.Actor!.Effects.Where(e => e.Script is not null))
                {
                    var script = effect.Script!;
                    Assert.True(NsgScriptLayouts.Known.TryGetValue(script.Class, out var layout)
                                || script.Record.ScriptState.Length == 0, $"{script.Class} saved words the table does not know");
                    if (layout is not null && !layout.Contains('s'))
                        Assert.Equal(layout.Length * 4, script.Record.ScriptState.Length);
                }
    }

    /// <summary>
    /// The chain the equipment work was built on, checked in the other direction and from a real
    /// file: an effect a worn item is running names the item, and the item's own copy of it carries
    /// the same instance id. If either end of the reader has drifted these stop lining up.
    /// </summary>
    [Fact]
    public void A_worn_items_effects_are_the_actors_effects()
    {
        int matched = 0;
        foreach (var slot in Slots())
        {
            var actor = Read(slot).Party.Players[0].Actor!;
            var live = actor.Effects.ToDictionary(e => e.Id);
            foreach (var item in actor.Inventory.Items.Where(i => i.Item!.IsEquipped))
                foreach (var effect in item.Item!.Effects)
                {
                    Assert.True(effect.Applied, $"{item.Name} is worn but its {effect.Name} is not running");
                    Assert.True(live.TryGetValue(effect.InstanceId, out var running),
                        $"{item.Name} runs effect {effect.InstanceId} the actor does not have");
                    Assert.Equal(effect.Name, running.Name);
                    Assert.Equal(item.Guid, running.SourceGuid);
                    matched++;
                }
            // Nothing the actor is running comes from an item it is carrying but not wearing. (Not
            // every creator is an item: a discipline names its caster — Christof's own GUID on the
            // Celerity running in an autosave.)
            var worn = actor.Inventory.Items.Where(i => i.Item!.IsEquipped).Select(i => i.Guid).ToHashSet();
            var carried = actor.Inventory.Items.Select(i => i.Guid).ToHashSet();
            foreach (var running in actor.Effects.Where(e => carried.Contains(e.SourceGuid)))
                Assert.Contains(running.SourceGuid, worn);
        }
        Assert.True(matched > 0, "no save had a worn item with an effect");
    }

    /// <summary>
    /// An item that is <em>not</em> worn can still be carrying the id of an effect that is no longer
    /// running — a ring of manipulation sits in the pack in one save with id 375 against an
    /// actor that has no such effect, and comes back as a fresh 376 when it goes on a finger. So the
    /// equip path must mint a new instance rather than trust the number the item is carrying.
    /// </summary>
    [Fact]
    public void An_unworn_item_may_carry_a_stale_effect_id()
    {
        foreach (var slot in Slots())
        {
            var actor = Read(slot).Party.Players[0].Actor!;
            var live = actor.Effects.Select(e => e.Id).ToHashSet();
            foreach (var item in actor.Inventory.Items.Where(i => !i.Item!.IsEquipped))
                foreach (var effect in item.Item!.Effects.Where(e => e.Applied))
                    Assert.DoesNotContain(effect.InstanceId, live);
        }
    }

    /// <summary>
    /// The other half of the same chain: a current stat that differs from its base has to be
    /// explained by the `ef_stat_*` effects on the actor, and a current soak by the `ef_soak_*`
    /// ones. This is the sheet Christof's numbers were rebuilt from, now read straight from a save.
    /// </summary>
    [Fact]
    public void Current_stats_and_soaks_are_the_base_plus_the_live_effects()
    {
        foreach (var slot in Slots())
        {
            var actor = Read(slot).Party.Players[0].Actor!;
            float FromEffects(string prefix) => actor.Effects
                .Where(e => e.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Sum(e => e.ValueLevel < 0 ? e.ValueAsFloat : e.ValueAsFloat);

            float statLift = actor.Stats.Zip(actor.BaseStats, (a, b) => a - b).Sum();
            float soakLift = actor.Soaks.Zip(actor.BaseSoaks, (a, b) => a - b).Sum();
            Assert.Equal(FromEffects("ef_stat_"), statLift, 3);
            Assert.Equal(FromEffects("ef_soak_"), soakLift, 3);
        }
    }

    /// <summary>
    /// Item instances carry their template's numbers, so a save can be read without the templates —
    /// and a template a mod has since changed does not silently rewrite a saved item.
    /// </summary>
    [Fact]
    public void Item_instances_carry_their_templates_cost_and_sell_category()
    {
        // `ITEM_SELLFLAGS` is a **mask**, not a category: the SDK declares it as
        // `[FLAG_ITEM_SELLFLAGS]` with nine bits, and 70 of the 772 shipped templates that carry
        // one set **two** of them -- the three plain-or-magic pairs, `0x0c` BLOOD ITEM | POTION,
        // `0x30` ARMOR | MAGIC ARMOR and `0xc0` WEAPON | MAGIC WEAPON.
        //
        // This test used to assert one bit or none, which was true of every item in the saves it
        // was written against and false about the format. It went red the hour a play picked up a
        // rat's vitae. The invariant that is actually worth asserting is that the reader has not
        // lost its place: a mask outside the nine declared bits means a misread, and so does a
        // two-bit mask that is not one of the three the data uses.
        var odd = new List<string>();
        foreach (var slot in Slots())
            foreach (var item in Read(slot).Party.Players[0].Actor!.Inventory.Items)
            {
                var rpg = item.Item!;
                Assert.InRange(rpg.BaseCost, 0u, 1_000_000u);
                Assert.NotEqual(0u, rpg.Flags & NsgItem.Identified);
                if ((rpg.SellFlags & ~SellFlagBits) != 0
                    || (System.Numerics.BitOperations.PopCount(rpg.SellFlags) > 1
                        && !SellFlagPairs.Contains(rpg.SellFlags)))
                    odd.Add($"{Path.GetFileName(slot)}: {item.Name} sellFlags=0x{rpg.SellFlags:x}");
            }
        Assert.True(odd.Count == 0, string.Join("\n", odd.Distinct()));
    }

    /// <summary>The nine bits `[FLAG_ITEM_SELLFLAGS]` declares; anything outside them is a misread.</summary>
    const uint SellFlagBits = 0x1ff;

    /// <summary>
    /// The only multi-bit masks the shipped data uses, measured over all 2,619 templates: blood
    /// item with potion, plain armour with magic armour, plain weapon with magic weapon.
    /// </summary>
    static readonly HashSet<uint> SellFlagPairs = [0x0c, 0x30, 0xc0];

    /// <summary>
    /// **A network game's save has no party**: 1.1's writer writes the party only outside a network game, so the
    /// saves MP0 and MP1 (a GOG copy's, where they are) end on the effect counter; read whole, with <see cref="NsgSave.HasParty"/> false.
    /// </summary>
    [Fact]
    public void A_network_game_s_save_ends_before_the_party()
    {
        string saves = Path.Combine(Environment.GetEnvironmentVariable("VITOMIR_NPF_SAMPLES") ?? "",
                                    "Games--Vampire The Masquerade - Redemption", "SaveGames");
        var found = new[] { "MP0", "MP1" }.Select(s => Path.Combine(saves, s, "!main.nsg")).Where(File.Exists).ToArray();
        Assert.SkipWhen(found.Length == 0, "no network game's save of 1.1's here (MP0 and MP1)");
        foreach (var main in found)
        {
            var save = NsgSave.ReadFile(main);
            Assert.False(save.HasParty);
            Assert.Empty(save.Party.Players);
            Assert.Equal("MP_CurseDarkness", Path.GetFileNameWithoutExtension(save.Chronicle.Name));
        }
        foreach (var slot in Slots()) Assert.True(Read(slot).HasParty, slot);           // a single-player save always has one
    }
}
