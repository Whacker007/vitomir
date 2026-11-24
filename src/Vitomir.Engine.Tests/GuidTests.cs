// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// One GUID counter for the whole game (<see cref="ThingGuids"/>). 1.1 numbers every thing it
/// makes from one global (`nextGUID`), so Christof is 5 in every place of every game and
/// a script may keep a GUID across places and saves; Vitomir numbered each place on its own, and
/// Christof was 68, 66, 42 and 122 in four places of the run. These pin the numbers of a
/// new game against 1.1's new game and save, a journey keeping them, a save and a load giving them
/// back, and a save of the old format converted.
/// </summary>
public class GuidTests
{
    /// <summary>The convent at the new game's start, as `--new-game` builds it: the chronicle's counter, Christof first.</summary>
    static (Location Place, Chronicle Prague) NewGame(GameFiles files)
    {
        var prague = Chronicle.Load(files, "Prague");
        var guids = Preloads.NewGame(prague, files);
        var place = Location.Load(files, "P1_CNVT", sky: false, scene: "CNVT_3_1", player: "Christof", entrance: 0, guids: guids);
        return (place, prague);
    }

    static WorldThing Only(Location place, string template, int nth = 0) =>
        place.Things.Where(t => t.Template.Equals(template, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.Index).ElementAt(nth);

    [Fact]
    public void A_new_game_numbers_its_things_as_1_1_does()
    {
        using var files = Install.Open();
        var (convent, _) = NewGame(files);
        var christof = convent.Player!.Thing;

        // A new game in 1.1: Christof 5, and the convent's people.
        Assert.Equal(5, christof.Guid);
        Assert.Equal(97, Only(convent, "anezka_torch").Guid);
        Assert.Equal(99, Only(convent, "szlachtaconvent", 0).Guid);
        Assert.Equal(101, Only(convent, "szlachtaconvent", 1).Guid);
        Assert.Equal(103, Only(convent, "nun").Guid);
        // And a save of it (SaveGames\SP5): the convent's first thing is 35, the 29 between being
        // the haven's, which the chronicle loads first (flags 0xb4 — 0x80).
        Assert.Equal(35, convent.SceneGuids.First);
        Assert.Equal(35, convent.Things.Where(t => t != christof).Min(t => t.Guid));
        Assert.Equal(6, convent.Guids.Reserved[Location.PlaceKey("P1_HAVN", "HAVN_7_7")]);

        // The kit takes the numbers his making held for it: the letter 1 and the broadsword 2, as in SP5.
        var npc = NpcFile.Read(files.Read(NpcFile.PathFor(christof.Actor!.RpgStats)));
        StartingKit.Give(christof, npc, convent.Templates!, new EffectDefinitions(files));
        var items = christof.Actor.Inventory.Items.Where(i => i.Thing is not null).ToDictionary(i => i.Thing!.Template, i => i.Thing!.Guid, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, items["CuthbertLetter"]);
        Assert.Equal(2, items["broadsword"]);
        // What was held and not taken is spent, as in 1.1: the next thing made takes the counter's.
        Assert.Equal(0, convent.Guids.Held);
        Assert.True(convent.Make("vitaerat")!.Guid >= convent.SceneGuids.End);
    }

    [Fact]
    public void A_journey_keeps_every_number_and_the_next_place_carries_on_the_count()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var chronicle = where.Chronicle;
        chronicle.Current = chronicle.Find(where.Entry!.Name);
        // A campaign whose Build is not handed the counter: the campaign's own numbering has to hold.
        Location Build(string name, string? scene, int entrance) =>
            Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
        var monastery = Build("P1_MON1", where.Entry.SceneName, -1);
        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = monastery };

        var leader = monastery.Player!.Thing;
        int christof = leader.Guid;
        var sword = monastery.Make("broadsword")!;
        sword.Owner = leader;
        leader.Actor!.Inventory.Add(sword.Item!);
        int swordGuid = sword.Guid;
        int highest = monastery.Things.Max(t => t.Guid);

        Assert.True(campaign.Chronicle.TakeExit(0));
        Assert.NotNull(campaign.Travel());
        var hill = campaign.Here;

        // One counter, and the party's numbers are the party's.
        Assert.Same(monastery.Guids, hill.Guids);
        Assert.Equal(christof, hill.Player!.Thing.Guid);
        Assert.Same(hill.Player.Thing, hill.Thing(christof));
        Assert.Equal(swordGuid, sword.Item!.Thing!.Guid);
        Assert.NotSame(sword, sword.Item.Thing);
        Assert.Same(sword.Item.Thing, hill.Thing(swordGuid));
        // Every other thing in the new place has a number the first place never used.
        Assert.All(hill.Things.Where(t => t != hill.Player.Thing), t => Assert.True(t.Guid > highest, $"{t.Template} is {t.Guid}"));
        Assert.Equal(hill.Things.Count, hill.Things.Select(t => t.Guid).Distinct().Count());
        // And the way back: the same numbers again, nothing new spent on the party.
        int next = hill.Guids.Next;
        Assert.True(campaign.Chronicle.TakeExit(0) || campaign.Chronicle.Jump(chronicle.NameOf(chronicle.Find(where.Entry.Name)), 0));
        Assert.NotNull(campaign.Travel());
        Assert.Equal(christof, campaign.Here.Player!.Thing.Guid);
        Assert.Equal(swordGuid, sword.Item.Thing!.Guid);
        Assert.True(campaign.Here.Guids.Next >= next);
    }

    [Fact]
    public void A_place_readied_without_the_party_is_numbered_from_the_game_s_counter()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var chronicle = where.Chronicle;
        chronicle.Current = chronicle.Find(where.Entry!.Name);
        Location Build(string name, string? scene, int entrance) =>
            Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
        var monastery = Build("P1_MON1", where.Entry.SceneName, -1);
        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = monastery };
        int highest = monastery.Guids.Next;

        // A teleporter's far end (CampaignTeleport.cs): the haven made ready while the party is away.
        var haven = campaign.Load(chronicle.Find("Haven"))!;

        Assert.Same(monastery.Guids, haven.Guids);
        Assert.Equal(monastery.Player!.Thing.Guid, haven.Player!.Thing.Guid);  // its stand-in spends nothing
        Assert.All(haven.Things, t => Assert.True(t.Guid >= highest, $"{t.Template} is {t.Guid}"));
    }

    [Fact]
    public void A_save_and_a_load_give_every_thing_its_number_and_the_counter_back()
    {
        using var files = Install.Open();
        var (convent, prague) = NewGame(files);
        var christof = convent.Player!.Thing;
        var rat = convent.Spawn("petRat", christof)!;
        var session = new Session { Here = convent, Chronicle = prague };
        convent.Session = session;
        prague.Current = prague.Find("Convent");
        var save = SaveCapture.Take(convent, session);
        Assert.Equal(SaveArchive.FormatVersion, save.Header.Format);
        Assert.Equal(convent.Guids.Next, save.Session.ThingCounter);
        var numbers = convent.Things.Where(t => ThingIds.Of(convent, t) is not null).ToDictionary(t => ThingIds.Of(convent, t)!, t => t.Guid);

        // Read back through the file, as a load does (Program.cs: SaveGuids.ForLoad, then Loaded).
        var dir = Directory.CreateTempSubdirectory("vitomir-guids");
        try
        {
            string path = Path.Combine(dir.FullName, "g.vsave");
            SaveArchive.Write(save, path);
            var read = SaveArchive.Read(path);
            var record = read.World[read.Header.Location];
            var back = Location.Load(files, record.Level, sky: false, scene: record.Scene, player: "Christof",
                                     entrance: record.Entrance, guids: SaveGuids.ForLoad(read));
            var restore = SaveApply.Place(back, record, new EffectDefinitions(files), new DisciplineDefinitions(files));
            SaveApply.Party(back, read.Party, restore);
            SaveGuids.Loaded(back, read);

            Assert.Equal(5, back.Player!.Thing.Guid);
            int compared = 0;
            foreach (var t in back.Things)
                if (ThingIds.Of(back, t) is { } id && numbers.TryGetValue(id, out int was)) { Assert.Equal(was, t.Guid); compared++; }
            Assert.Equal(numbers.Count, compared);
            Assert.Contains(back.Things, t => t.Guid == rat.Guid && t.Template == rat.Template);
            Assert.Equal(save.Session.ThingCounter, back.Guids.Next);
            Assert.Equal(convent.SceneGuids, back.SceneGuids);                 // what a network host would tell a joiner
            Assert.DoesNotContain(back.Things, t => ThingGuids.IsPlaceholder(t.Guid));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void A_remembered_place_comes_back_from_a_save_on_its_own_numbers()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var chronicle = where.Chronicle;
        chronicle.Current = chronicle.Find(where.Entry!.Name);
        int monasteryAt = chronicle.Current;
        Location Build(string name, string? scene, int entrance) =>
            Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
        var monastery = Build("P1_MON1", where.Entry.SceneName, -1);
        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = monastery };
        var numbers = monastery.Things.Where(t => ThingIds.Placed(monastery, t) is not null).ToDictionary(t => ThingIds.Placed(monastery, t)!, t => t.Guid);
        Assert.True(chronicle.TakeExit(0));
        Assert.NotNull(campaign.Travel());
        var session = new Session { Here = campaign.Here, Chronicle = chronicle };
        campaign.Here.Session = session;
        var save = SaveCapture.Take(campaign.Here, session, campaign);
        string monasteryName = chronicle.NameOf(monasteryAt);
        Assert.True(save.World.ContainsKey(monasteryName));

        // The load: the place the party is in from its record, the monastery remembered for later.
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var record = save.World[save.Header.Location];
        var here = Location.Load(files, record.Level, sky: false, scene: record.Scene.Length > 0 ? record.Scene : null,
                                 player: "Christof", entrance: record.Entrance, guids: SaveGuids.ForLoad(save));
        SaveApply.Party(here, save.Party, SaveApply.Place(here, record, effects, powers), effects, powers);
        SaveGuids.Loaded(here, save);
        var again = new Campaign
        {
            Chronicle = chronicle, Build = Build, Here = here,
            BuildSaved = (r, entrance) =>
            {
                var place = Location.Load(files, r.Level, sky: false, scene: r.Scene.Length > 0 ? r.Scene : null, player: "Christof", entrance: entrance);
                SaveApply.Place(place, r, effects, powers);
                return place;
            },
        };
        again.Remember(monasteryAt, save.World[monasteryName]);
        int counter = here.Guids.Next;
        Assert.True(chronicle.Jump(monasteryName, 0));
        var arrival = again.Travel();
        Assert.True(arrival is { Restored: true });

        var back = again.Here;
        foreach (var t in back.Things)
            if (ThingIds.Placed(back, t) is { } id && numbers.TryGetValue(id, out int was)) Assert.Equal(was, t.Guid);
        Assert.Equal(counter, back.Guids.Next);                                // the read-back spent nothing
        Assert.DoesNotContain(back.Things, t => ThingGuids.IsPlaceholder(t.Guid));
        Assert.Equal(save.Party.Members[0].Guid, back.Player!.Thing.Guid);
    }

    [Fact]
    public void A_place_built_for_a_load_spends_no_number()
    {
        using var files = Install.Open();
        // 1.1 takes no number while a save or a saved place is read.
        var guids = new ThingGuids { Next = 500, Loading = true };
        var place = Location.Load(files, "P1_MON1", sky: false, player: "Christof", guids: guids);
        Assert.All(place.Things, t => Assert.True(ThingGuids.IsPlaceholder(t.Guid)));
        guids.Loading = false;
        Assert.Equal(500, guids.Next);
        Assert.Equal(500, place.Make("vitaerat")!.Guid);
    }

    [Fact]
    public void A_format_1_save_keeps_its_place_and_party_numbers_and_renumbers_the_places_it_remembers()
    {
        var old = new SaveGame
        {
            Header = new SaveHeader { Format = 1, Location = "Monastery1" },
            Party = new PartySave { Members = [new ThingSave { Template = "Christof", Guid = 122 }] },
        };
        old.World["Monastery1"] = new LocationSave { Level = "P1_MON1", NextGuid = 130 };
        old.World["Monastery1"].Things["P1_MON1#4"] = new ThingSave { Template = "rat", Guid = 5 };
        old.World["PetrinHill"] = new LocationSave { Level = "P1_HILL", NextGuid = 90 };
        old.World["PetrinHill"].Things["P1_HILL#2"] = new ThingSave { Template = "barrel", Guid = 3 };

        var notes = SaveGuids.Convert(old);

        Assert.Equal(SaveArchive.FormatVersion, old.Header.Format);
        Assert.Equal(122, old.Party.Members[0].Guid);                         // the party's, kept
        Assert.Equal(5, old.World["Monastery1"].Things["P1_MON1#4"].Guid);    // the place it is in, kept
        Assert.Equal(0, old.World["PetrinHill"].Things["P1_HILL#2"].Guid);    // another place's, let go
        Assert.Equal(130, old.Session.ThingCounter);                          // past every number kept
        Assert.Single(notes, n => n.StartsWith("PetrinHill"));
        // A second read changes nothing.
        Assert.Empty(SaveGuids.Convert(old));
    }

    [Fact]
    public void A_format_1_save_loads_with_its_party_on_their_own_numbers()
    {
        // Slot 03 where the game keeps it (%LOCALAPPDATA%\Vitomir\Saves on Windows), or a
        // copy under VITOMIR_NPF_SAMPLES. Read only.
        string sp3 = new[] { Path.Combine(Diag.UserDataFolder, "Saves", "SP3.vsave"),
                             Path.Combine(Environment.GetEnvironmentVariable("VITOMIR_NPF_SAMPLES") ?? "", "Vitomir", "Saves", "SP3.vsave") }.FirstOrDefault(File.Exists) ?? "";
        Assert.SkipUnless(sp3.Length > 0, "no SP3.vsave");
        using var files = Install.Open();
        var read = SaveArchive.Read(sp3);                                     // read only
        Assert.Equal(SaveArchive.FormatVersion, read.Header.Format);
        int christof = read.Party.Members[0].Guid;
        Assert.True(christof > 0);
        var set = ChronicleSet.Load(files);
        var story = set[read.Header.Chronicle]!;
        SaveApply.Chronicle(story, read.Chronicle!);
        var at = story.At(story.Current)!;
        var place = Location.Load(files, at.LevelName, sky: false, scene: at.SceneName.Length > 0 ? at.SceneName : null,
                                  player: read.Party.Members[0].Template, guids: SaveGuids.ForLoad(read));
        var restore = read.World.TryGetValue(read.Header.Location, out var record)
            ? SaveApply.Place(place, record, new EffectDefinitions(files), new DisciplineDefinitions(files)) : new ScriptRestore();
        SaveApply.Party(place, read.Party, restore);
        SaveGuids.Loaded(place, read);

        Assert.Equal(christof, place.Player!.Thing.Guid);
        Assert.Same(place.Player.Thing, place.Thing(christof));
        foreach (var m in read.Party.Members.Skip(1))
            Assert.Equal(m.Template, place.Thing(m.Guid)?.Template, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(place.Things.Count, place.Things.Select(t => t.Guid).Distinct().Count());
        Assert.True(place.Guids.Next > place.Things.Max(t => t.Guid));
    }

    [Fact]
    public void An_imported_1_1_save_keeps_1_1_s_own_numbers()
    {
        using var files = Install.Open();
        string slot = Path.Combine(files.Root, "SaveGames", "SP4");
        Assert.SkipUnless(File.Exists(Path.Combine(slot, "!main.nsg")), "no SaveGames\\SP4");
        var nsg = NsgSave.ReadFile(Path.Combine(slot, "!main.nsg"));
        var notes = new List<string>();
        var game = SaveImport.FromOriginal(files, ChronicleSet.Load(files), nsg, "SP4", notes, folder: slot)!;
        Assert.DoesNotContain(notes, n => n.Contains("cannot have 1.1's GUID"));

        Assert.Equal((int)nsg.ThingCounter, game.Session.ThingCounter);
        Assert.Equal(5, game.Party.Members[0].Guid);                           // Christof, 5 in every 1.1 save
        Assert.Equal((int)nsg.Party.Players[1].Guid, game.Party.Members[1].Guid);
        // Every thing of every place is on the number its .nsw gives it.
        foreach (var (index, file) in NswPlaces.Read(slot, []))
        {
            var byGuid = file.Things.GroupBy(t => (int)t.Guid).ToDictionary(g => g.Key, g => g.First().Name);
            var place = game.World.Values.FirstOrDefault(w => w.Things.Count > 0 && w.Things.Values.All(t => t.Guid == 0 || byGuid.ContainsKey(t.Guid)));
            Assert.NotNull(place);
            foreach (var t in place!.Things.Values.Where(t => t.Guid > 0))
                Assert.Equal(byGuid[t.Guid], t.Template, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_joining_machine_puts_its_copy_of_a_place_on_the_host_s_numbers()
    {
        using var files = Install.Open();
        var (host, _) = NewGame(files);
        var copy = Location.Load(files, "P1_CNVT", sky: false, scene: "CNVT_3_1", player: "Christof", entrance: 0);
        Assert.NotEqual(host.SceneGuids.First, copy.SceneGuids.First);

        copy.RenumberScene(host.SceneGuids.First);

        Assert.Equal(host.SceneGuids, copy.SceneGuids);
        foreach (var t in host.Things.Where(t => t.Index >= 0))
            Assert.Equal(t.Template, copy.Thing(t.Guid)?.Template);
    }
}
