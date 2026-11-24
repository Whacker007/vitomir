// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// 1.1's post-load fix-ups (<see cref="PostLoad"/>) through a real save and load: a weapon
/// swap comes back with what it put aside, Earthmeld and Mist Form never survive, and every live
/// effect is pushed into the character again — the stat's setter it goes through is
/// what would put a frenzy back.
/// </summary>
public class PostLoadTests
{
    sealed record Game(Location Place, Session Session, Chronicle Chronicle, EffectDefinitions Effects, DisciplineDefinitions Powers);

    static Game Start(GameFiles files, bool awaken = true)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        if (awaken) place.Awaken(new AiClasses(files), 1, effects, powers);
        var chronicle = Chronicle.Load(files, "Prague");
        chronicle.Current = chronicle.Find("OldTownDay");
        var session = new Session { Here = place, Chronicle = chronicle };
        place.Session = session;
        return new Game(place, session, chronicle, effects, powers);
    }

    /// <summary>Saved, written, read and loaded into a freshly built place, as a load does it.</summary>
    static Location RoundTrip(GameFiles files, Game g)
    {
        string path = Path.Combine(Path.GetTempPath(), $"vitomir-postload-{Guid.NewGuid():N}.vsave");
        SaveGame save;
        try
        {
            SaveArchive.Write(SaveCapture.Take(g.Place, g.Session), path);
            save = SaveArchive.Read(path);
        }
        finally { File.Delete(path); }
        var fresh = Start(files, awaken: false);
        SaveApply.Chronicle(fresh.Chronicle, save.Chronicle!);
        var world = save.World[save.Header.Location];
        var place = Location.Load(files, world.Level, things: true, sky: false,
                                  scene: world.Scene.Length > 0 ? world.Scene : null, player: "Christof", entrance: world.Entrance);
        var restore = SaveApply.Place(place, world, fresh.Effects, fresh.Powers);
        SaveApply.Party(place, save.Party, restore, fresh.Effects, fresh.Powers);
        place.Awaken(new AiClasses(files), 1, fresh.Effects, fresh.Powers, fromSave: true);
        return place;
    }

    [Fact]
    public void Claws_worn_at_the_save_are_in_the_hand_after_the_load_and_the_sword_is_still_put_aside()
    {
        using var files = Install.Open();
        var g = Start(files);
        var christof = g.Place.Player!.Thing;
        var swordThing = g.Place.Make("broadSword")!;
        christof.Actor!.Inventory.Add(swordThing.Item!);
        swordThing.Owner = christof;
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(christof, swordThing.Item!, Slot.None, g.Effects, 0));
        var templates = g.Place.Templates!;
        Assert.True(WeaponOverride.Override(christof, Weapon.From("Claw02", templates.Resolve("Claw02"))!, clearOffhand: true, hideFists: true));

        var back = RoundTrip(files, g);
        var me = back.Player!.Thing.Actor!;
        Assert.Equal("Claw02", me.Weapon?.Name);
        Assert.True(me.Has2(Actor.Flag2.WeaponOverride));
        var sword = me.Inventory.Items.Single(i => i.Name.Equals("broadSword", StringComparison.OrdinalIgnoreCase));
        Assert.Same(sword.Weapon, me.PrevWeapon);

        // And when the swap ends, the sword is back in the hand.
        WeaponOverride.Cancel(back.Player.Thing);
        Assert.Same(sword.Weapon, me.Weapon);
    }

    [Fact]
    public void Earthmeld_and_mist_form_never_survive_a_load()
    {
        using var files = Install.Open();
        var g = Start(files);
        var christof = g.Place.Player!.Thing;
        foreach (var name in new[] { PostLoad.Earthmeld, PostLoad.MistForm, "ef_increasebloodot" })
            Assert.NotNull(EffectRuntime.Apply(christof, g.Effects[name]!, 0, g.Place.Milliseconds, 600_000));

        var back = RoundTrip(files, g).Player!.Thing;
        Assert.Null(back.Effects.Find(PostLoad.Earthmeld));
        Assert.Null(back.Effects.Find(PostLoad.MistForm));
        Assert.NotNull(back.Effects.Find("ef_increasebloodot"));
    }

    [Fact]
    public void Every_live_effect_is_pushed_into_the_character_again()
    {
        using var files = Install.Open();
        var g = Start(files);
        var christof = g.Place.Player!.Thing;
        var me = christof.Actor!;
        // A modified-stat effect (kind 3) on Strength, live, and a row that does not show it — as
        // a row can stand before the recompute has pushed every kind back in.
        var strength = g.Effects.File!.All.First(e => e.Kind == EffectKind.Stat && e.Subtype == (int)ActorStat.Strength
                                                          && (e.Flags & EffectFlags.Negator) == 0);
        Assert.NotNull(EffectRuntime.Apply(christof, strength, 0, g.Place.Milliseconds, 600_000));
        float shown = me.Stat[(int)ActorStat.Strength];
        Assert.NotEqual(me.BaseStat[(int)ActorStat.Strength], shown);
        me.Stat[(int)ActorStat.Strength] = me.BaseStat[(int)ActorStat.Strength];
        PostLoad.FixUp(christof);
        Assert.Equal(shown, me.Stat[(int)ActorStat.Strength]);
    }
}
