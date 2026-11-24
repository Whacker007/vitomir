// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// The callbacks the single-player scripts answer that nothing sent earlier, each driven from
/// where the engine sends it and answered by a shipped script: a tome's `used` (and its refusal),
/// Mercurio's journal's `pickup` and `textended`, a poisoned bite's `hit` offered twice as 1.1
/// offers it, a spider sac's `touched`, the Embrace's `videoended`, a `ClickExit`'s
/// `exitstatechanged`, and the console's `advance`.
/// </summary>
[Collection("scripts")]
public class CallbackTests(Scripts s)
{
    static WorldThing Carried(Place m, string template)
    {
        var thing = m.Where.Make(template)!;
        m.Christof.Actor!.Inventory.Add(thing.Item!);
        thing.Owner = m.Christof;
        return thing;
    }

    [Fact]
    public void A_tome_is_read_once_and_the_second_copy_is_refused_by_its_script()
    {
        using var m = Place.Enter(s, "P1_MON1");
        var me = m.Christof.Actor!;
        Assert.Null(me.Disciplines["Celerity"]);
        var first = Carried(m, "TomeCelerity");
        // `ItemTomeCelerity.used` opens the group at −1 and answers true; the tome is spent.
        var used = Items.Use(m.Christof, first.Item!, m.Where.Milliseconds, m.Effects.File);
        Assert.NotEqual(UseResult.Refused, used);
        Assert.Equal(-1, me.Disciplines["Celerity"]?.Level);

        // The second copy: the discipline is begun, `used` answers false, and the use is refused.
        var second = Carried(m, "TomeCelerity");
        int amount = second.Item!.Amount;
        Assert.Equal(UseResult.Refused, Items.Use(m.Christof, second.Item, m.Where.Milliseconds, m.Effects.File));
        Assert.Equal(amount, second.Item.Amount);
        Assert.Contains(me.Inventory.Items, i => ReferenceEquals(i, second.Item));
    }

    [Fact]
    public void Mercurio_s_journal_opens_itself_when_picked_up_and_its_script_hears_it_closed()
    {
        using var m = Place.Enter(s, "P1_MON3");
        var journal = m.Where.Things.Single(t => t.Script == "MON3_MercurioJournal");
        string? open = null;
        m.Scenes.ReadText = name =>
        {
            if (open is not null) return false;                       // the pane is up: refused, as 1.1 refuses
            open = name;
            return true;
        };
        int flag = ChronicleFlag("MON3_MERCURIOJOURNAL");
        var chronicle = m.Scenes.Chronicle!;
        Assert.False(chronicle.GetFlag(flag));

        // The script is the journal's own and captures its own thing, so it hears `pickup` twice
        // (the message's two loops); the first opens the journal, the second finds the pane up.
        var got = Ground.Take(m.Where, m.Christof, journal, InputModifier.None, steering: true, m.Where.Milliseconds,
                              m.Where.ItemScripts);
        Assert.NotEqual(GroundResult.Refused, got);
        Assert.Equal("MercurioJournal", open);
        Assert.False(chronicle.GetFlag(flag));

        // Closed: `textended(readerGuid)` sets the flag and starts Mercurio's conversation.
        m.Scenes.TextClosed();
        Assert.True(chronicle.GetFlag(flag));
    }

    /// <summary>A story flag's number, from the chronicle class the game runs (its constant, so a store copy with no sources has it too).</summary>
    int ChronicleFlag(string name) => s.Runtime!.ClassOf("PragueChronicle")!.getField(name).getInt(null);

    [Fact]
    public void A_ghoul_spider_s_bite_is_offered_the_poison_twice_as_in_1_1()
    {
        using var m = Place.Enter(s, "P1_MON1");
        var spider = m.Where.Spawn("ghoulSpider", m.Christof)!;
        var bite = spider.Actor!.Weapon!;
        Assert.Equal("bite_ghoulSpider", bite.Name, ignoreCase: true);
        // `bite_ghoulSpider` and `ghoulSpider` both carry ClassPoisonous: the weapon's script hears
        // `hit`, then the wielder's. At 200 damage `Math.random() < 0.05 + 2` always holds.
        s.Engine.Trace = [];
        try
        {
            m.Where.ItemScripts!.Hit!(spider, bite, m.Christof, 200f);
            Assert.Equal(2, s.Engine.Trace.Count(c => c.StartsWith("CodexActor.AddActorEffectByLevel(ef_poison", StringComparison.Ordinal)));
        }
        finally { s.Engine.Trace = null; }
        Assert.NotNull(m.Christof.Effects.Find("ef_poison"));

        // And a poisoned dagger in Christof's hand poisons what it strikes, through the item's script.
        var dagger = Carried(m, "DaggerPoison");
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(m.Christof, dagger.Item!, Slot.None, m.Effects, m.Where.Milliseconds));
        m.Where.ItemScripts!.Hit!(m.Christof, m.Christof.Actor!.Weapon!, spider, 200f);
        Assert.NotNull(spider.Effects.Find("ef_poison"));
    }

    /// <summary>
    /// **The Berserker weapon's strongest frenzy** (1.1's script throws on it; the Fix `berserker`).
    /// At 200 damage `ItemWeaponBerserker.hit` always goes off and always rolls level 5 (a random number
    /// plus 2), and `FRENZY_DURATIONS` has five entries: faithful, the script throws and nothing happens.
    /// With the Fix, the blow ends as written: `ef_item_frenzy` at level 5 for the table's last length,
    /// the struck spider its creator, and the red cloud shell.
    /// </summary>
    [Fact]
    public void The_Berserker_weapon_s_top_frenzy_throws_as_in_1_1_and_takes_hold_under_the_fix()
    {
        using var m = Place.Enter(s, "P1_MON1");
        // A knight swings it, since Christof at the monastery has not the strength a war hammer asks.
        var knight = m.Where.Spawn("knightofstjohn", m.Christof)!;
        var hammer = m.Where.Make("WarHammerBerserker")!;
        knight.Actor!.Inventory.Add(hammer.Item!);
        hammer.Owner = knight;
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(knight, hammer.Item!, Slot.None, m.Effects, m.Where.Milliseconds));
        const string threw = "ItemWeaponBerserker.hit failed: java.lang.ArrayIndexOutOfBoundsException";

        int warned = s.Warnings.Count(w => w.Contains(threw));
        m.Where.ItemScripts!.Hit!(knight, knight.Actor.Weapon!, m.Christof, 200f);
        Assert.Null(knight.Effects.Find("ef_item_frenzy"));                         // faithful: the script threw
        Assert.Null(knight.Fx?.Shell);
        Assert.Equal(warned + 1, s.Warnings.Count(w => w.Contains(threw)));

        m.Where.Fixes = new Fixes { Berserker = true };
        int now = m.Where.Milliseconds;
        m.Where.ItemScripts!.Hit!(knight, knight.Actor.Weapon!, m.Christof, 200f);
        var frenzy = knight.Effects.Find("ef_item_frenzy");
        Assert.NotNull(frenzy);
        Assert.Equal(5, frenzy!.Level);
        Assert.Equal(m.Christof.Guid, frenzy.CreatorGuid);
        Assert.Equal(now + 10500, frenzy.Until);
        Assert.Equal("redCloudShell", knight.Fx?.Shell);
        Assert.Equal(warned + 1, s.Warnings.Count(w => w.Contains(threw)));      // and no second warning
    }

    [Fact]
    public void A_spider_sac_bursts_when_Christof_is_close_once_it_has_armed_itself()
    {
        using var m = Place.Enter(s, "P1_MON1");
        var sac = m.Where.Spawn("spiderSack", m.Christof)!;
        Assert.NotEqual(0u, sac.Flags & WorldThing.Flag.ProximityTrigger);
        int Babies() => m.Where.Things.Count(t => t.Template.Equals("ghoulSpiderBaby", StringComparison.OrdinalIgnoreCase));
        int before = Babies();
        // `created` arms it after 3 s; until then `touched` is heard and ignored.
        m.Run(2f);
        Assert.False(sac.Removed);
        // Then within sixteen frames it finds Christof in its radius, and bursts into five spiders.
        Assert.True(m.RunUntil(() => sac.Removed, 3f));
        Assert.Equal(before + 5, Babies());
    }

    [Fact]
    public void The_Embrace_film_ends_and_the_story_jumps_to_the_haven()
    {
        using var m = Place.Enter(s, "P1_OLDT", "OLDT_7_4");
        var chronicle = m.Scenes.Chronicle!;
        // The script sends Christof running into Ecaterina's region — the one region it captured —
        // and there asks for `embrace.bik`; with no film player the film ends at once, and
        // `videoended` jumps to the haven's entrance 1. The run itself is 3,500 units long and is
        // not what this pins, so he is put straight into the region once the order is given.
        m.Run(1.5f);
        var region = m.Where.Things.Single(t => t.Type == ThingType.Region && t.Captures.Count > 0);
        m.Where.Move(m.Christof, region.Position);
        Assert.True(m.RunUntil(() => chronicle.Pending is { Kind: SequenceKind.Jump }, 5f),
                    "the Embrace never reached its film");
        Assert.Equal(ActorType.Vampire, m.Christof.Actor!.Type);           // the Embrace itself ran first
        Assert.Equal(chronicle.Find("Haven"), chronicle.Pending!.Value.To);
        Assert.Equal(1, chronicle.Pending.Value.Entrance);
    }

    [Fact]
    public void Opening_and_closing_an_exit_tells_every_exit_sign_in_the_place()
    {
        using var m = Place.Enter(s, "P1_OLDT", "OLDT_7_4");
        // OLDT_7_4's own `beginscene` closed exit 4; its signs read locked.
        int Locked() => m.Where.Things.Count(t => t.Script == "ClickExit" && t.DescriptionId.EndsWith("_L", StringComparison.Ordinal));
        Assert.True(m.Scenes.Chronicle!.IsExitClosed("OldTown", 4));
        int locked = Locked();
        Assert.True(locked > 0);
        Place.Call(s, "CodexSequence.OpenExit", null, "OldTown", 4);
        Assert.True(Locked() < locked, "no ClickExit heard `exitstatechanged`");
        Place.Call(s, "CodexSequence.CloseExit", null, "OldTown", 4);
        Assert.Equal(locked, Locked());
    }

    [Fact]
    public void The_console_s_advance_reaches_the_chronicle()
    {
        using var m = Place.Enter(s, "P1_MON1");
        // Every shipped chronicle's `advance` is an empty switch; what is pinned is that it is sent
        // and heard without complaint.
        int warnings = s.Warnings.Count;
        Assert.True(Place.Bln(Place.Call(s, "CodexConsole.Execute", null, "advance 3 1")));
        Assert.Equal(warnings, s.Warnings.Count);
    }
}
