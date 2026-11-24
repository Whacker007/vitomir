// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// A companion's `.NPC` sheet, read as 1.1 reads it (a comparison with 1.1's gaps 1..3): the reader
/// case 0x3b reads the sheet of every player-type thing as it is made — outside a load, a `.nsw` read
/// and a player's machine — after a reset of the sheet's bounds and values; the kit's items take their
/// numbers before the companion's own; the sheet's cash goes to the purse; and `totalexperience` writes
/// the lifetime figure alone. Earlier, a fresh Wilhem at the University had 20 in every stat, a 10,000
/// ceiling, no disciplines and an empty pack. Pinned against `WilhemDa.NPC` and 1.1's saves (SP3's
/// Wilhem, SP4's numbers).
/// </summary>
public class CharacterSheetTests
{
    static (Location Place, WorldThing Wilhem) University(GameFiles files, bool awake = true)
    {
        var place = Location.Load(files, "P1_UNIV", things: true, sky: false, scene: "UNIV_7_7", player: "Christof");
        if (awake) place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        return (place, place.Things.Single(t => t.Template.Equals("Wilhem", StringComparison.OrdinalIgnoreCase)));
    }

    static float S(WorldThing t, ActorStat s) => t.Actor!.Stat[(int)s];

    [Fact]
    public void Wilhem_at_the_University_is_made_with_his_sheet()
    {
        using var files = Install.Open();
        var (_, wilhem) = University(files);
        var a = wilhem.Actor!;
        // WilhemDa.NPC's stats and bounds — and SP3, a 1.1 save, agrees: 35, 40 and 30.
        Assert.Equal(35f, S(wilhem, ActorStat.Strength));
        Assert.Equal(40f, S(wilhem, ActorStat.Dexterity));
        Assert.Equal(30f, S(wilhem, ActorStat.Stamina));
        Assert.Equal(120f, S(wilhem, ActorStat.BloodPool));
        Assert.Equal(100f, a.StatMax[(int)ActorStat.Strength]);
        Assert.Equal(1000f, a.StatMax[(int)ActorStat.Blood]);
        Assert.Equal("Wilhem", a.Name);
        Assert.Equal(120f, a.MaxHealth);
        // His sheet's seven disciplines, with their quick slots (Potence in slot 3); the levels set open
        // the next of each group and his AI file adds its own, so he knows more than seven.
        foreach (var line in NpcFile.Read(files.Read(NpcFile.PathFor(a.RpgStats))).Disciplines)
            Assert.Contains(a.Disciplines.All, d => d.Name.Equals(line.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(a.Disciplines.All, d => d.Name.Equals("Potence", StringComparison.OrdinalIgnoreCase) && d.QuickSlot == 3);
        // His kit, in the sheet's order: a halberd, a short bow, four vitae (on his belt), arrows, two scrolls.
        Assert.Equal(["halberd", "shortbow", "vitaered", "vitaered", "vitaered", "vitaered", "arrows_20",
                      "discscroll_awaken_1", "discscroll_awaken_1"], SheetOrder(a));
    }

    /// <summary>The kit's templates in the order of their numbers, which is the order the sheet made them.</summary>
    static List<string> SheetOrder(Actor a) =>
        [.. a.Inventory.Items.Where(i => i.Thing is not null).OrderBy(i => i.Thing!.Guid).Select(i => i.Thing!.Template.ToLowerInvariant())];

    [Fact]
    public void Wilhem_s_kit_takes_the_numbers_before_his_own_as_in_SP4()
    {
        using var files = Install.Open();
        var (place, wilhem) = University(files);
        var numbers = wilhem.Actor!.Inventory.Items.Where(i => i.Thing is not null).Select(i => i.Thing!.Guid).Order().ToList();
        // SP4: the University's first thing 2,012, the kit 2,045..2,053, the fallback weapon 2,054, Wilhem 2,055.
        Assert.Equal(Enumerable.Range(wilhem.Guid - 10, 9), numbers);
        Assert.Equal(2055 - 2012, wilhem.Guid - place.SceneGuids.First);
    }

    [Fact]
    public void A_sheet_with_only_totalexperience_gives_nothing_to_spend()
    {
        using var files = Install.Open();
        var (_, wilhem) = University(files);
        // The `totalexperience` line writes the lifetime figure alone: SP3 has Wilhem at 325 unspent of 6,325.
        Assert.Equal(6000, wilhem.Actor!.Experience);
        Assert.Equal(0, wilhem.Actor.SpendableExperience);
        // A multiplayer sheet names both, which is what the network create screen spends from.
        var mp = new Actor();
        mp.Apply(NpcFile.Read(files.Read(NpcFile.PathFor("MP_Wilhem"))));
        Assert.Equal(5000, mp.Experience);
        Assert.Equal(5000, mp.SpendableExperience);
        // And a sheet without either line leaves both as they were.
        var none = new Actor { Experience = 7, SpendableExperience = 3 };
        none.Apply(NpcFile.Parse("name Nobody\n"));
        Assert.Equal((7L, 3L), (none.Experience, none.SpendableExperience));
    }

    [Fact]
    public void The_reset_bounds_what_the_sheet_does_not_name()
    {
        using var files = Install.Open();
        var (_, wilhem) = University(files);
        var a = wilhem.Actor!;
        // No shipped companion's sheet names mana or its pool: the reset's bounds stand, 0..100 and 0..1,000.
        Assert.Equal((0f, 100f), (a.StatMin[(int)ActorStat.Mana], a.StatMax[(int)ActorStat.Mana]));
        Assert.Equal((0f, 1000f), (a.StatMin[(int)ActorStat.ManaPool], a.StatMax[(int)ActorStat.ManaPool]));

        // The reset on its own: the name, the bounds and the values.
        var fresh = new Actor { IsPlayerThing = true };
        CharacterSheets.Reset(fresh);
        Assert.Equal("Fumpfblumen", fresh.Name);
        Assert.Equal(20f, fresh.Stat[(int)ActorStat.Strength]);
        Assert.Equal(13f, fresh.Stat[(int)ActorStat.Generation]);
        Assert.Equal(1f, fresh.Stat[(int)ActorStat.BloodRating]);
        Assert.Equal(100f, fresh.Stat[(int)ActorStat.Blood]);
        Assert.Equal(100f, fresh.StatMax[(int)ActorStat.Blood]);
        Assert.Equal(1000f, fresh.StatMax[(int)ActorStat.BloodPool]);
    }

    [Fact]
    public void A_place_not_awake_holds_the_sheet_and_a_load_reads_none()
    {
        using var files = Install.Open();
        var (place, wilhem) = University(files, awake: false);
        Assert.True(CharacterSheets.Waiting(wilhem));
        Assert.Empty(wilhem.Actor!.Inventory.Items);
        Assert.NotEqual(35f, S(wilhem, ActorStat.Strength));
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        Assert.False(CharacterSheets.Waiting(wilhem));
        Assert.Equal(35f, S(wilhem, ActorStat.Strength));

        // A place built for a load numbers nothing and reads no sheet.
        var guids = new ThingGuids { Loading = true };
        var loaded = Location.Load(files, "P1_UNIV", things: true, sky: false, scene: "UNIV_7_7", player: "Christof", guids: guids);
        var his = loaded.Things.Single(t => t.Template.Equals("Wilhem", StringComparison.OrdinalIgnoreCase));
        Assert.False(CharacterSheets.Waiting(his));
    }

    [Fact]
    public void A_sheet_s_cash_goes_to_the_purse()
    {
        using var files = Install.Open();
        var (place, _) = University(files, awake: false);
        // Lily's sheet carries 50 (the purse set to itself plus the line's figure), placed
        // before the place is awake and given with the rest at the waking.
        var start = place.Player!.Thing;
        var lily = place.Place("Lily", start.Position, start.Orientation, start.Sector);
        Assert.NotNull(lily);
        Assert.True(CharacterSheets.Waiting(lily!));
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        Assert.Equal(50, CharacterSheets.Owed(place));
        var session = new Session { Gold = 10 };
        place.Session = session;
        Assert.Equal(60, session.Gold);
        Assert.Equal(0, CharacterSheets.Owed(place));
        // Her kit was numbered before her: six items, the one below her own the fallback weapon's or hers.
        var numbers = lily!.Actor!.Inventory.Items.Where(i => i.Thing is not null).Select(i => i.Thing!.Guid).ToList();
        Assert.NotEmpty(numbers);
        Assert.All(numbers, n => Assert.True(n < lily.Guid));
    }
    [Fact]
    public void A_row_of_zeros_is_a_row()
    {
        using var files = Install.Open();
        // Samuel's `appearance 0 0 0` is a Nosferatu's appearance held at 0 (the reader writes what a row
        // says); the reader took an all-zero row for a missing one until then.
        Assert.True(NpcFile.Parse("appearance 0 0 0\n").Stats[(int)ActorStat.Appearance].Authored);
        Assert.False(NpcFile.Parse("name Nobody\n").Stats[(int)ActorStat.Appearance].Authored);
        var samuel = new Actor { IsPlayerThing = true };
        CharacterSheets.Read(samuel, NpcFile.Read(files.Read(NpcFile.PathFor("Samuel"))));
        Assert.Equal((0f, 0f), (samuel.StatMax[(int)ActorStat.Appearance], samuel.Stat[(int)ActorStat.Appearance]));
    }

    [Fact]
    public void The_numbers_set_aside_for_a_place_count_its_companions_kits()
    {
        using var files = Install.Open();
        var (place, _) = University(files, awake: false);
        // What a preloaded place is given (Location.SceneGuidCount) is what reading its scene takes,
        // Wilhem's nine kit numbers with the rest.
        Assert.Equal(place.SceneGuids.End - place.SceneGuids.First, Location.SceneGuidCount(files, "P1_UNIV", "UNIV_7_7"));
    }

    [Fact]
    public void Serena_is_made_holding_her_dagger_on_numbers_before_her_own()
    {
        using var files = Install.Open();
        var (place, _) = University(files);
        var start = place.Player!.Thing;
        // Made while the game runs, in a place awake: given her sheet at once.
        var serena = place.Place("Serena", start.Position, start.Orientation, start.Sector)!;
        Assert.False(CharacterSheets.Waiting(serena));
        var a = serena.Actor!;
        Assert.Equal("dagger", a.Weapon?.Name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(25f, a.StatMax[(int)ActorStat.Appearance]);
        var numbers = a.Inventory.Items.Where(i => i.Thing is not null).Select(i => i.Thing!.Guid).Order().ToList();
        Assert.Equal(5, numbers.Count);                                     // the dagger and four vitae
        Assert.All(numbers, n => Assert.True(n < serena.Guid));
        // A blade trail her dagger hangs takes the number held after it, which is not the counter's.
        if (a.Weapon is { TrailGuid: > 0 } w) Assert.True(w.TrailGuid < serena.Guid);
    }
    /// <summary>
    /// **Serena as 1.1 makes her** (as she is
    /// made for her joining): the nineteen stats in `ACTOR_STAT_*` order — blood 100, not the
    /// sheet's 120 — the dagger worn, four vitae on the belt, and the ten disciplines with their levels.
    /// </summary>
    [Fact]
    public void Serena_s_sheet_is_the_one_1_1_gave_her()
    {
        using var files = Install.Open();
        var (place, _) = University(files);
        var start = place.Player!.Thing;
        var a = place.Place("Serena", start.Position, start.Orientation, start.Sector)!.Actor!;
        int[] seen = [15, 20, 30, 35, 40, 40, 100, 50, 0, 10, 25, 25, 25, 25, 50, 1, 120, 20, 20];
        Assert.Equal(string.Join(",", seen), string.Join(",", a.Stat.Select(v => (int)MathF.Round(v))));
        Assert.Equal("dagger", a.Weapon?.Name, StringComparer.OrdinalIgnoreCase);
        var pack = a.Inventory.Items.Select(i => i.Name.ToLowerInvariant()).Order().ToArray();
        Assert.Equal(["dagger", "vitaered", "vitaered", "vitaered", "vitaered"], pack);
        string[] disciplines = ["summonsoul:-1", "vigormortis:0", "shamblinghordes:1", "fortitude:1", "spiritstouch:-1",
                                "auraperception:0", "heightenedsenses:1", "bloodstrength:-1", "bloodhealing:1", "feed:1"];
        Assert.Equal(disciplines.Order(), a.Disciplines.All.Select(d => $"{d.Name.ToLowerInvariant()}:{d.Level}").Order());
    }

    [Fact]
    public void A_chapter_swapped_in_gives_its_companion_his_sheet()
    {
        using var files = Install.Open();
        // The University as the chronicle first builds it, awake, and then the chapter Wilhem stands in —
        // which is how 1.1 meets him: his kit numbered before him from the running counter.
        var at = ChronicleSet.Load(files).ForLevel("P1_UNIV")!.Value.Entry!;
        var place = Location.Load(files, "P1_UNIV", things: true, sky: false,
                                  scene: at.SceneName.Length > 0 ? at.SceneName : null, player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var swap = place.SwapChapter("UNIV_7_7");
        Assert.NotNull(swap);
        var wilhem = swap!.Arrived.Single(t => t.Template.Equals("Wilhem", StringComparison.OrdinalIgnoreCase));
        Assert.False(CharacterSheets.Waiting(wilhem));
        Assert.Equal(35f, S(wilhem, ActorStat.Strength));
        var numbers = wilhem.Actor!.Inventory.Items.Where(i => i.Thing is not null).Select(i => i.Thing!.Guid).Order().ToList();
        Assert.Equal(Enumerable.Range(wilhem.Guid - 10, 9), numbers);
    }
    [Fact]
    public void A_network_player_is_dressed_once_as_it_is_made()
    {
        using var files = Install.Open();
        var (place, _) = University(files);
        Assert.SkipUnless(place.Templates!.Has("MP_Erik"), "no MP_Erik template here");
        var start = place.Player!.Thing;
        // The host makes a player's character (GameServer.SpawnFor → Location.Place): its sheet is read as
        // it is made; Outfit, after, finds it done and reads nothing twice. No multiplayer sheet has a
        // kit, so a network player's number is where it was.
        var erik = place.Place("MP_Erik", start.Position, start.Orientation, start.Sector)!;
        var a = erik.Actor!;
        Assert.NotNull(CharacterSheets.Given(erik));
        Assert.Equal(CharacterSheets.Given(erik), Net.Outfit.Dress(place, erik));
        // Its `cash 200` goes to the host's party purse, as 1.1 puts it there whoever the sheet is for.
        Assert.Equal(200, CharacterSheets.Owed(place));
        // A multiplayer sheet names both experience figures: 5,000 to spend on the create screen's terms.
        Assert.Equal((5000L, 5000L), (a.Experience, a.SpendableExperience));
    }
    [Fact]
    public void A_character_moved_to_another_place_reads_no_sheet_there()
    {
        using var files = Install.Open();
        var (place, _) = University(files);
        var start = place.Player!.Thing;
        int next = place.Guids.Peek;
        // The Storyteller's move of one character builds a fresh body and hands it the old one's actor
        // (Net.HostPlaces.Carry); in 1.1 the thing itself moves, so nothing is read or numbered.
        WorldThing? lily;
        using (CharacterSheets.Moving()) lily = place.Place("Lily", start.Position, start.Orientation, start.Sector);
        Assert.NotNull(lily);
        Assert.Null(CharacterSheets.Given(lily!));
        Assert.False(CharacterSheets.Waiting(lily!));
        Assert.Empty(lily!.Actor!.Inventory.Items);
        Assert.Equal(0, CharacterSheets.Owed(place));
        Assert.True(lily.Guid - next <= 1);                                   // its fallback weapon's and its own, no kit
    }
}
