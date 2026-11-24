// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A 1.1 save's places brought across as they were left** (`NswImport`). Every
/// `location_&lt;n&gt;.nsw` in the install's own `SaveGames` is laid over its place, written as a
/// Vitomir save and read back — and each place then holds what its file says: every record at its
/// thing, where the file has it; every placed thing the file no longer has, gone. And the place the
/// party is in is loaded the way the game loads a save, and checked against its file thing by thing:
/// the dead lying where they fell, the chests that were opened still open. The saves are a player's own and
/// are only read; the Vitomir saves are written to the temporary folder and deleted.
/// </summary>
public class NswImportTests
{
    static string[] Slots(GameFiles files)
    {
        string dir = Path.Combine(files.Root, "SaveGames");
        Assert.SkipUnless(Directory.Exists(dir), $"no SaveGames folder in {files.Root}");
        var slots = Directory.EnumerateDirectories(dir)
                             .Where(d => File.Exists(Path.Combine(d, "!main.nsg")) && Directory.EnumerateFiles(d, "location_*.nsw").Any())
                             .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.SkipWhen(slots.Length == 0, $"no 1.1 save with its .nsw files in {dir}");
        return slots;
    }

    /// <summary>The save imported from its folder, through a file and back.</summary>
    static (SaveGame Game, NsgSave Nsg, SortedDictionary<int, NswWorld> Files) Import(GameFiles files, string slot)
    {
        var nsg = NsgSave.ReadFile(Path.Combine(slot, "!main.nsg"));
        var notes = new List<string>();
        var made = SaveImport.FromOriginal(files, ChronicleSet.Load(files), nsg, Path.GetFileName(slot), notes, folder: slot);
        Assert.True(made is not null, $"{slot}: {string.Join("; ", notes)}");
        string path = Path.Combine(Path.GetTempPath(), $"vitomir-nsw-{Guid.NewGuid():N}.vsave");
        try { SaveArchive.Write(made!, path); return (SaveArchive.Read(path), nsg, NswPlaces.Read(slot, [])); }
        finally { File.Delete(path); }
    }

    static string? PlacedId(NsgThing r, LocationSave place) =>
        r.Scene == NswWorld.OwnScene ? $"{place.Level}#{(int)r.Field28}"
        : r.Scene >= 0 && place.Scene.Length > 0 ? $"{place.Scene}#{(int)r.Field28}" : null;

    static Vector3 At(byte[] m) => new(BitConverter.ToSingle(m, 36), BitConverter.ToSingle(m, 40), BitConverter.ToSingle(m, 44));

    [Fact]
    public void Every_place_a_1_1_save_remembers_comes_across_as_its_file_left_it()
    {
        using var files = Install.Open();
        var chronicles = ChronicleSet.Load(files);
        foreach (var slot in Slots(files))
        {
            var (game, nsg, places) = Import(files, slot);
            var story = chronicles[Path.GetFileNameWithoutExtension(nsg.Chronicle.Name)]!;
            foreach (var (index, file) in places)
            {
                string name = story.At(index)!.Name;
                Assert.True(game.World.TryGetValue(name, out var place), $"{slot}: {name} did not come across");
                var placed = new HashSet<string>(StringComparer.Ordinal);
                int made = 0;
                foreach (var r in file.Things)
                {
                    string where = $"{Path.GetFileName(slot)} {name} {r.Name} (1.1's guid {r.Guid})";
                    if (PlacedId(r, place!) is not { } id) { made++; continue; }
                    placed.Add(id);
                    Assert.True(place!.Things.TryGetValue(id, out var t), $"{where}: no {id}");
                    Assert.Equal(r.Name, t!.Template, ignoreCase: true);
                    Assert.True(Vector3.Distance(At(r.Transform), t.Position) < 0.01f, $"{where} stands at {t.Position}, not {At(r.Transform)}");
                    Assert.Equal(r.Flags, t.Flags);
                    if (r.Actor is { } a)
                    {
                        Assert.Equal(a.Health, t.Actor!.Health);
                        Assert.Equal(a.Field1F4, t.Actor.Flags);
                    }
                    if (r.Type == NsgThingType.Container) Assert.Equal(r.TypeFields[0], t.State);
                }
                // The things made in play are made again, and nothing the scenes placed is in the
                // file twice or left out of the removed list.
                Assert.Equal(made, place!.Things.Keys.Count(k => ThingIds.ParseMade(k) is not null));
                Assert.All(place.Removed, id => Assert.DoesNotContain(id, placed));
                Assert.Equal(placed.Count, place.Things.Count - made);
            }
        }
    }

    /// <summary>
    /// **A monastery save, loaded as the game loads it** — the equivalence check of an
    /// import: the place the party is in, built from its level and scene and given the imported
    /// record (<see cref="SaveApply.Place"/>), then held against its own `.nsw`. The two corpses lie
    /// where they fell, dead; the two chests he opened are open; the nine things he killed or took are
    /// not there; and what was dropped in play is on the floor again.
    /// </summary>
    [Fact]
    public void The_place_the_party_is_in_loads_with_the_dead_where_they_fell_and_the_chests_open()
    {
        using var files = Install.Open();
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var chronicles = ChronicleSet.Load(files);
        int checkedPlaces = 0;
        foreach (var slot in Slots(files))
        {
            var (game, nsg, places) = Import(files, slot);
            int current = (int)nsg.Chronicle.Fields[0];
            if (!places.TryGetValue(current, out var file)) continue;
            var story = chronicles[game.Chronicle!.Name]!;
            SaveApply.Chronicle(story, game.Chronicle);
            var record = game.World[game.Header.Location];
            var place = Location.Load(files, record.Level, sky: false, scene: record.Scene.Length > 0 ? record.Scene : null,
                                      player: game.Party.Members[0].Template);
            var restore = SaveApply.Place(place, record, effects, powers);
            SaveApply.Party(place, game.Party, restore, effects, powers);

            var byId = place.Things.Where(t => ThingIds.Of(place, t) is not null).ToDictionary(t => ThingIds.Of(place, t)!);
            foreach (var r in file.Things)
            {
                string where = $"{Path.GetFileName(slot)} {r.Name} (1.1's guid {r.Guid})";
                var t = PlacedId(r, record) is { } id ? byId.GetValueOrDefault(id)
                    : place.Things.FirstOrDefault(x => x.Guid == (int)r.Guid && ThingIds.ParseMade(ThingIds.Of(place, x) ?? "") is not null);
                Assert.True(t is not null, $"{where} is not in the loaded place");
                Assert.True(t!.InWorld, $"{where} is not in the world");
                Assert.True(Vector3.Distance(At(r.Transform), t.Position) < 0.01f, $"{where} stands at {t.Position}, not {At(r.Transform)}");
                if (r.Actor is { } a)
                {
                    Assert.Equal((a.Field1F4 & Actor.Flag.Dead) != 0, t.Actor!.Dead);
                    Assert.Equal(a.Health, t.Actor.Health);
                }
                if (r.Type == NsgThingType.Container) Assert.Equal((r.TypeFields[0] & 1) != 0, (t.State & 1) != 0);
            }
            // What the scenes placed and the file does not have is gone — and keeps its GUID for the
            // scripts whose saved parameters still name it (Location.Gone): `MON1_Main` names a
            // Cappadocian killed before the save, and constructs only if it is not handed a null.
            foreach (var id in record.Removed) Assert.False(byId.ContainsKey(id), $"{id} is back");
            Assert.True(place.Gone.Count >= record.Removed.Count, $"{place.Gone.Count} gone, {record.Removed.Count} removed");
            Assert.Equal(file.Things.Length, byId.Count(kv => kv.Value.InWorld && !kv.Key.StartsWith(ThingIds.Party, StringComparison.Ordinal)));
            checkedPlaces++;
        }
        Assert.SkipWhen(checkedPlaces == 0, "no 1.1 save has a .nsw for the place its party is in");
    }

    /// <summary>
    /// **The equivalence check of an import, on the save it was made for**: the
    /// `SP4`, Monastery 1 at 01:28:05, the save an early import brought across without its places. Loaded the way
    /// the game loads a save, the monastery holds what 1.1 left in `location_32.nsw` — its two half
    /// corpses dead and lying in their fall, the two chests he opened open, the nine things the chapter
    /// placed that he killed or took gone, the coins dropped in play on the floor — and its main
    /// script has what it needs to run.
    /// </summary>
    [Fact]
    public void A_played_monastery_comes_across_as_1_1_left_it()
    {
        using var files = Install.Open();
        string slot = Path.Combine(files.Root, "SaveGames", "SP4");
        Assert.SkipUnless(File.Exists(Path.Combine(slot, "location_32.nsw")), "the London save SP4 is not in this install");
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var (game, nsg, _) = Import(files, slot);
        Assert.SkipUnless(nsg.Description.StartsWith("Monastery 1 (01:28:05)", StringComparison.Ordinal), "SP4 is another save now");
        Assert.Equal("Monastery", game.Header.Location);
        var record = game.World["Monastery"];
        Assert.Equal(9, record.Removed.Count);
        Assert.All(record.Removed, id => Assert.StartsWith("MON1_8_2#", id));

        var story = ChronicleSet.Load(files)[game.Chronicle!.Name]!;
        SaveApply.Chronicle(story, game.Chronicle);
        var place = Location.Load(files, record.Level, sky: false, scene: record.Scene, player: game.Party.Members[0].Template);
        var restore = SaveApply.Place(place, record, effects, powers);
        SaveApply.Party(place, game.Party, restore, effects, powers);
        place.Awaken(new AiClasses(files), 1, effects, powers, fromSave: true);

        var dead = place.Things.Where(t => t.Actor is { Dead: true } && t.InWorld).ToArray();
        Assert.Equal(2, dead.Length);
        Assert.All(dead, t => Assert.Equal("halfcorpse", t.Template, ignoreCase: true));
        Assert.All(dead, t => Assert.True(t.Animation?.Action is { Held: true } fall
                                          && fall.Mode.File.Equals("halfCorpse_death.nad", StringComparison.OrdinalIgnoreCase),
                                          $"{t.Template} is not lying in its fall"));
        var opened = place.Things.Where(t => t.Type == ThingType.Container && (t.State & 1) != 0).Select(t => ThingIds.Of(place, t) ?? "").Order().ToArray();
        Assert.Equal(["MON1_8_2#15", "MON1_8_2#54"], opened);
        foreach (var id in record.Removed) Assert.DoesNotContain(place.Things, t => ThingIds.Of(place, t) == id && t.InWorld);
        var coins = place.Things.Where(t => ThingIds.ParseMade(ThingIds.Of(place, t) ?? "") is not null && t.Item is not null && t.InWorld)
                                .Select(t => t.Template.ToLowerInvariant()).Order().ToArray();
        Assert.Equal(["goldcoins", "silvercoins"], coins);
        // `MON1_Main` names a Cappadocian that is gone; the load keeps its GUID for it (Location.Gone).
        Assert.Contains(record.Removed, id => place.Gone.ContainsKey(place.ChapterScene is null ? -1
            : (place.WorldScene?.Things.Length ?? 0) + ThingIds.ParsePlaced(id)!.Value.Index));
    }

    /// <summary>
    /// The scripts' half (tags 0x19 and 0x1a, and each record's own `save()` words): what the file
    /// has comes across named the Vitomir way — a timer by its script's thing, a capture on the thing
    /// captured — and a door's saved word is its word.
    /// </summary>
    [Fact]
    public void The_scripts_words_timers_and_captures_come_across()
    {
        using var files = Install.Open();
        var chronicles = ChronicleSet.Load(files);
        int timers = 0, captures = 0, words = 0;
        foreach (var slot in Slots(files))
        {
            var (game, nsg, places) = Import(files, slot);
            var story = chronicles[Path.GetFileNameWithoutExtension(nsg.Chronicle.Name)]!;
            foreach (var (index, file) in places)
            {
                var place = game.World[story.At(index)!.Name];
                var scripts = file.Things.Where(r => r.Script is not null).ToDictionary(r => r.Script!.Guid);
                int wantTimers = file.Timers.Count(t => scripts.ContainsKey(t.CodexGuid));
                Assert.Equal(wantTimers, place.Timers.Count);
                timers += wantTimers;
                var guids = file.Things.Select(r => r.Guid).ToHashSet();
                int wantCaptures = file.Captures.Count(c => guids.Contains(c.ThingGuid) && scripts.ContainsKey(c.CodexGuid));
                Assert.Equal(wantCaptures, place.Things.Values.Sum(t => t.Captures?.Count ?? 0));
                captures += wantCaptures;
                foreach (var r in file.Things)
                {
                    if (r.Script is not { Record.ScriptState.Length: > 0 } s || PlacedId(r, place) is not { } id) continue;
                    var saved = place.Things[id].Script;
                    Assert.True(saved is not null, $"{id}'s {s.Class} lost its words");
                    Assert.Equal(s.Class, saved!.Class);
                    Assert.Equal(NsgScriptLayouts.Decode(s.Class, s.Record.ScriptState)!.Count, saved.Values.Count);
                    words += saved.Values.Count;
                }
            }
        }
        Assert.True(timers + captures + words > 0, "no script state in any .nsw to check");
    }
}
