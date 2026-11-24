// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The smaller AI items: the party's refused swing still shouts, a ranged
/// attacker's clear shot is blocked by bodies and not by walls, the party-AI options go
/// into the one AI flags word at start-up and a load's teardown zeroes it, and
/// a quick slot the steered character empties is refilled from the pack when `BackpackAutoLoad` is
/// on.
/// </summary>
public class PartyOptionsTests
{
    static (Location Place, Minds Minds, Mind Member, Mind Enemy) Recruit(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var leader = place.Player!.Thing;
        var member = minds.All.First(m => m.Mode == AiMode.PedWander);
        member.Thing.Actor!.Flags |= Actor.Flag.Party;
        member.Thing.Actor.Flags2 |= Actor.Flag2.InFormation;
        member.Enter(AiMode.Party);
        member.Thing.Position = leader.Position;
        member.Walker.Ground();
        var enemy = minds.All.First(m => !ReferenceEquals(m, member) && !ReferenceEquals(m.Thing, leader)
                                         && m.Thing.Actor is not null && m.Mode != AiMode.PedFly);
        enemy.Thing.Actor!.Flags &= ~Minds.LeaveAloneFlags;
        enemy.Thing.Actor.Team = member.Thing.Actor.Team + 3;
        enemy.Thing.Position = leader.Position + new Vector3(60f, 0f, 0f);
        enemy.Walker.Ground();
        return (place, minds, member, enemy);
    }

    /// <summary>
    /// **A refused swing still shouts** (the order's answer unread): a
    /// companion still busy with its last blow is refused the new one, and the turn is still spent on
    /// the fight — the engagement stamped and the mind's engaged field set, so it does not go on to
    /// follow the leader on the same think.
    /// </summary>
    [Fact]
    public void A_companions_refused_swing_still_counts_as_the_fight()
    {
        using var files = Install.Open();
        var (_, minds, member, enemy) = Recruit(files);
        member.Aim(enemy.Thing, minds.Now);
        member.Thing.Actor!.BusyUntil = minds.Now + 5_000;          // still in its last swing
        member.EngagedAt = 0;
        member.NextThink = minds.Now;
        minds.Tick(0.1f);
        Assert.Same(enemy.Thing, member.Target);
        Assert.Equal(minds.Now, member.EngagedAt);
    }

    /// <summary>
    /// **Bodies block a clear shot, walls do not** (a things-only query, the type mask
    /// `0xffffff7e` letting through only characters, the shooter excluded, the dead not). Somebody
    /// standing in the line blocks it — alive or dead — unless it carries `DISABLECOLLIDE`; the target
    /// itself being first is clear.
    /// </summary>
    [Fact]
    public void A_body_in_the_line_blocks_a_clear_shot()
    {
        using var files = Install.Open();
        var (place, _, member, enemy) = Recruit(files);
        var shooter = member.Thing;
        var target = enemy.Thing;
        place.Player!.Thing.Position = shooter.Position + new Vector3(0f, -400f, 0f);   // Christof out of the line
        target.Position = shooter.Position + new Vector3(300f, 0f, 0f);
        Assert.True(ClearShot.Clear(shooter, target));

        var between = place.Things.First(t => t.Actor is not null && t.Collide != CollideShape.None
                                              && !ReferenceEquals(t, shooter) && !ReferenceEquals(t, target)
                                              && !ReferenceEquals(t, place.Player!.Thing));
        between.Position = shooter.Position + new Vector3(150f, 0f, 0f);
        Assert.False(ClearShot.Clear(shooter, target));
        between.Actor!.Flags |= Actor.Flag.Dead;                      // a corpse still blocks
        Assert.False(ClearShot.Clear(shooter, target));
        between.Flags |= WorldThing.Flag.DisableCollide;              // the query's last test
        Assert.True(ClearShot.Clear(shooter, target));
    }

    /// <summary>
    /// And a wall does not: two characters the level hides from each other (the sight trace, which
    /// is the one that knows about surfaces) still have a clear shot when nobody stands between.
    /// </summary>
    [Fact]
    public void A_wall_does_not_block_a_clear_shot()
    {
        using var files = Install.Open();
        var (place, _, member, _) = Recruit(files);
        var shooter = member.Thing;
        place.Player!.Thing.Position = shooter.Position + new Vector3(0f, -400f, 0f);   // Christof out of the way
        var hidden = place.Things.FirstOrDefault(t => t.Actor is not null && !ReferenceEquals(t, shooter)
                                                      && !Sight.Sees(place.Level, shooter, t)
                                                      && ClearShotLineIsEmpty(place, shooter, t));
        Assert.NotNull(hidden);
        Assert.True(ClearShot.Clear(shooter, hidden!));
    }

    static bool ClearShotLineIsEmpty(Location place, WorldThing from, WorldThing to)
    {
        var a = Projectiles.Aim(from);
        var line = Projectiles.Aim(to) - a;
        float length = line.Length();
        return Pick.Trace(null, place.Things, a, line / length, length, Pick.Tests.Things, ignore: from)
                   .All(c => c.Thing is null || ReferenceEquals(c.Thing, to) || !ClearShot.Blocks(c.Thing));
    }

    /// <summary>
    /// The options go into the AI word at start-up: with the shipped `masquerade.ini`
    /// (`UseQuickDisc=1`, `UseQuickItems=1`, the four `Cast*` on) the two quick-slot filters are set
    /// and no casting is barred. A load's teardown writes the word to zero and nothing
    /// folds the options in again — which Vitomir repairs (`keepcontrols`): the boxes hold.
    /// The shipped `[gameOptions]` are written here, not read from the install: 1.1 writes its own
    /// ini back, so an install that has been played has the player's boxes (a laptop run had
    /// the four casting boxes off).
    /// </summary>
    [Fact]
    public void The_options_are_in_the_word_at_start_up_and_a_load_zeroes_it()
    {
        var dir = Directory.CreateTempSubdirectory("vitomir-shipped-ini").FullName;
        File.WriteAllText(Path.Combine(dir, "masquerade.ini"),
            "[gameOptions]\nForceSubtitles=1\nNoBlood=0\nRepeatAttack=1\nAutoSlew=0\nMousePitch=1\nBackpackAutoLoad=1\n"
            + "UseQuickDisc=1\nUseQuickItems=1\nCastHealing=1\nCastBuff=1\nCastDamage=1\nCastSpecial=1\n");
        var options = GameOptions.Parse(dir, installedDir: dir, userPath: Path.Combine(dir, "none.ini"));
        MainFlagsWord.Reset();
        uint word = MainFlagsWord.AtStart(options, loaded: false);
        Assert.Equal(AiMainFlag.QuickDisciplinesOnly | AiMainFlag.QuickItemsOnly, word);
        Assert.Equal(0u, MainFlagsWord.AtStartAsShipped(options, loaded: true));      // 1.1: zero after a load
        Assert.Equal(word, MainFlagsWord.AtStart(options, loaded: true));             // repaired: the boxes hold

        MainFlagsWord.Reset();
        options.SetAi(GameOptions.CastDamage, false);
        Assert.NotEqual(0u, MainFlagsWord.AtStart(options, loaded: false) & AiMainFlag.NoPartyCastDamage);
        Assert.Equal(word | AiMainFlag.Paused, MainFlagsWord.Across(word | AiMainFlag.Paused));
        MainFlagsWord.Reset();
    }

    /// <summary>
    /// **A quit to the main menu zeroes the word too**, and a new game begun
    /// from the menu starts from that zero — the Control Options boxes ignored — until the Controls page
    /// is applied at the menu, which folds them into it. Only a run's first new
    /// game gets the start-up fold. Vitomir repairs it (`keepcontrols`): the boxes are folded back in.
    /// </summary>
    [Fact]
    public void A_new_game_after_a_quit_to_the_menu_starts_from_zero_until_the_controls_are_applied()
    {
        Assert.SkipUnless(GameFiles.Locate() is not null, "no game install");
        var options = GameOptions.Parse(GameFiles.Locate(), installedDir: Path.GetTempPath(), userPath: Path.Combine(Path.GetTempPath(), "none.ini"));
        options.SetAi(GameOptions.CastHealing, false);
        MainFlagsWord.Reset();
        uint folded = options.FoldInto(0u);
        Assert.Equal(folded, MainFlagsWord.AtStart(options, loaded: false));    // the run's first game
        MainFlagsWord.TornDown();                                               // quit to the menu
        Assert.Equal(0u, MainFlagsWord.AtStartAsShipped(options, loaded: false));   // 1.1: zero
        Assert.Equal(folded, MainFlagsWord.AtStart(options, loaded: false));    // repaired (`keepcontrols`)
        Assert.Equal(folded, MainFlagsWord.AtStart(options, loaded: false));    // and every game after
        MainFlagsWord.TornDown();
        MainFlagsWord.Applied(options);                                         // the Controls page, at the menu
        Assert.Equal(folded, MainFlagsWord.AtStart(options, loaded: false));
        Assert.NotEqual(0u, folded & AiMainFlag.NoPartyCastHeal);
        MainFlagsWord.Reset();
        MainFlagsWord.Applied(options);                                         // before any teardown: still the start-up fold
        Assert.Equal(folded, MainFlagsWord.AtStart(options, loaded: false));
        MainFlagsWord.Reset();
    }

    static Item Potion(int cellX, int location) => new()
    {
        Name = "healthPotion", Type = ItemType.General, Amount = 1,
        Flags = ItemFlags.DestroyedAtZeroCharges, Location = location, Cell = (cellX, 0),
    };

    /// <summary>
    /// **The refill**: the last charge of a quick-slot item used up, and the next one of
    /// the same name comes out of the pack into the same place — with `BackpackAutoLoad` on, and only
    /// when the slot was emptied.
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]        // the option off
    [InlineData(true, false, false)]        // charges left: the slot was not emptied
    public void A_quick_slot_emptied_is_refilled_from_the_pack(bool autoload, bool lastCharge, bool refilled)
    {
        var me = new Actor { Health = 100f, MaxHealth = 100f };
        var user = new WorldThing { Index = -1, Guid = 3, Scene = new Formats.NsdThing(), Template = "t", Type = ThingType.Player, Actor = me };
        var used = Potion(2, Slot.QuickUse);
        var spare = Potion(4, Slot.Inventory);
        me.Inventory.Add(spare);
        me.Inventory.Add(used);

        QuickSlots.Use(user, used, autoload, item => { if (lastCharge) item.Destroyed = true; return "used"; });
        Assert.Equal(refilled ? Slot.QuickUse : Slot.Inventory, spare.Location);
        if (refilled) Assert.Equal((2, 0), spare.Cell);
    }

    /// <summary>
    /// **A dead character's quick slot does nothing at all** (the slot's use returns before the slot is
    /// looked up): no use, no refill. Vitomir skipped the refill and still used the item until then.
    /// </summary>
    [Fact]
    public void A_dead_character_s_quick_slot_does_nothing()
    {
        var me = new Actor { Health = 0f, MaxHealth = 100f, Flags = Actor.Flag.Dead };
        var user = new WorldThing { Index = -1, Guid = 3, Scene = new Formats.NsdThing(), Template = "t", Type = ThingType.Player, Actor = me };
        var used = Potion(2, Slot.QuickUse);
        var spare = Potion(4, Slot.Inventory);
        me.Inventory.Add(spare);
        me.Inventory.Add(used);
        bool asked = false;

        Assert.Equal(QuickSlots.DeadSays, QuickSlots.Use(user, used, autoload: true, item => { asked = true; item.Destroyed = true; return "used"; }));
        Assert.False(asked);
        Assert.False(used.Destroyed);
        Assert.Equal(Slot.Inventory, spare.Location);
    }
}
