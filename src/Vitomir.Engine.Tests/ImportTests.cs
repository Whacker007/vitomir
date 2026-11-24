// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A 1.1 save brought into Vitomir** (`SaveImport`): every save in the install's own
/// `SaveGames` folder imported, written, read back and loaded the way the game loads a save — and the
/// party that comes out is the party 1.1 wrote, number for number. The saves are the player's, so
/// this runs against whatever the install has and skips when it has none.
/// </summary>
public class ImportTests
{
    static string[] Slots(GameFiles files)
    {
        string dir = Path.Combine(files.Root, "SaveGames");
        Assert.SkipUnless(Directory.Exists(dir), $"no SaveGames folder in {files.Root}");
        var slots = Directory.EnumerateDirectories(dir)
                             .Where(d => File.Exists(Path.Combine(d, "!main.nsg")))
                             .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.SkipWhen(slots.Length == 0, $"no saves in {dir}");
        return slots;
    }

    /// <summary>
    /// 1.1's load of a discipline list by names alone: each record found in place or put on the
    /// front, and a group's next that a record at any level but −1 opens put on the front when not known
    /// yet — written out here apart from `DisciplineLoadOrder`, so the import is checked against the rule
    /// rather than against itself.
    /// </summary>
    static List<string> TurnedRound(IEnumerable<(string Name, int Level)> fileOrder, DisciplineDefinitions defs)
    {
        var list = new List<string>();
        foreach (var (raw, level) in fileOrder)
        {
            string name = raw.ToLowerInvariant();
            if (!list.Contains(name)) list.Insert(0, name);
            if (level != -1 && defs[name] is { } def && defs.NextInGroup(def) is { } next && !list.Contains(next.Name.ToLowerInvariant()))
                list.Insert(0, next.Name.ToLowerInvariant());
        }
        return list;
    }

    [Fact]
    public void Every_1_1_save_comes_across_with_its_party_as_1_1_wrote_it()
    {
        using var files = Install.Open();
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        foreach (var slot in Slots(files))
        {
            string name = Path.GetFileName(slot);
            var nsg = NsgSave.ReadFile(Path.Combine(slot, "!main.nsg"));
            var notes = new List<string>();
            var made = SaveImport.FromOriginal(files, ChronicleSet.Load(files), nsg, name, notes);
            Assert.True(made is not null, $"{name}: {string.Join("; ", notes)}");

            // Through the file and back, then loaded as the game loads a save.
            string path = Path.Combine(Path.GetTempPath(), $"vitomir-import-{Guid.NewGuid():N}.vsave");
            SaveGame game;
            try { SaveArchive.Write(made!, path); game = SaveArchive.Read(path); }
            finally { File.Delete(path); }
            Assert.Equal(nsg.Description, game.Header.Description);
            var story = ChronicleSet.Load(files)[game.Chronicle!.Name]!;
            SaveApply.Chronicle(story, game.Chronicle);
            for (int w = 0; w < nsg.Chronicle.Flags.Length; w++)
                for (int b = 0; b < 32; b++)
                    Assert.True(story.GetFlag(w * 32 + b) == ((nsg.Chronicle.Flags[w] >> b & 1) != 0), $"{name}: story flag {w * 32 + b}");
            Assert.Equal((int)nsg.Chronicle.Fields[0], story.Current);

            // The chronicle's other words come across and through a load (1.1's slots
            // held a pending advance, a moved portal and London's drizzle, which the import had dropped).
            int Word(uint w) => w == uint.MaxValue ? -1 : (int)w;
            Assert.Equal((Word(nsg.FieldE4), nsg.TextE8, Word(nsg.Field108)), story.AdvanceTo);
            Assert.Equal((Word(nsg.Chronicle.Fields[1]), Word(nsg.Chronicle.Fields[2])), story.Portal);
            Assert.Equal((Word(nsg.Chronicle.Fields[3]), Word(nsg.Chronicle.Fields[4])), story.Revive);
            Assert.Equal(nsg.Chronicle.Text1Af88, game.Chronicle.Weather ?? "");
            var open = nsg.Teleports.Select((t, i) => (t, i)).Where(x => x.t.F != 0).ToList();
            Assert.Equal(open.Select(x => x.i), game.Session.Teleports.Select(t => t.Slot).Order());
            foreach (var (t, i) in open)
            {
                var kept = game.Session.Teleports.Single(x => x.Slot == i);
                Assert.Equal((story.NameOf((int)t.A), story.NameOf((int)t.C), t.E), (kept.InLocation, kept.OutLocation, kept.Flags));
                Assert.StartsWith(ThingIds.Party, kept.Creator);
            }

            // The place is not remembered — 1.1 keeps it in the `.nsw`, and this import was given only
            // the `.nsg` — so it is built as it was first made, from the story's own record of where
            // the party is. (With the folder, it comes across as it was left: NswImportTests.)
            Assert.False(game.World.ContainsKey(game.Header.Location));
            var here = story.At(story.Current)!;
            Assert.Equal(game.Header.Location, here.Name);
            var place = Location.Load(files, here.LevelName, sky: false, scene: here.SceneName.Length > 0 ? here.SceneName : null,
                                      player: game.Party.Members[0].Template);
            SaveApply.Party(place, game.Party, new ScriptRestore(), effects, powers);
            var members = SaveCapture.PartyOf(place);
            var players = nsg.Party.Players;
            Assert.Equal(players.Length, members.Count);

            for (int i = 0; i < players.Length; i++)
            {
                var p = players[i];
                var n = p.Actor!;
                var t = members[i];
                var a = t.Actor!;
                string who = $"{name} {p.Name}";
                Assert.Equal((ActorType)n.ActorType, a.Type);
                Assert.Equal(n.Health, a.Health);
                Assert.Equal(n.MaxHealth, a.MaxHealth);
                Assert.Equal(n.Field1F4, a.Flags);
                Assert.Equal(n.Stats, a.Stat[..n.Stats.Length]);
                Assert.Equal(n.BaseStats, a.BaseStat[..n.BaseStats.Length]);
                Assert.Equal(n.Soaks, a.Soak[..n.Soaks.Length]);
                Assert.Equal(p.Player!.Field3EC, (uint)a.Experience);
                float F(int k) => BitConverter.ToSingle(p.Transform, k * 4);
                Assert.True(Vector3.Distance(new Vector3(F(9), F(10), F(11)), t.Position) < 0.01f, $"{who} stands at {t.Position}");

                // Every discipline at its level and in its quick slot, and the one selected — in the order
                // 1.1's own load leaves them (DisciplineLoadOrderTests), not the file's.
                Assert.Equal(n.Disciplines.Select(d => (d.Name.ToLowerInvariant(), Math.Max(-1, d.Level), d.Field18 == uint.MaxValue ? -1 : (int)d.Field18)).Order(),
                             a.Disciplines.All.Select(d => (d.Name.ToLowerInvariant(), d.Level, d.QuickSlot)).Order());
                // The whole order, as 1.1's load leaves it: not simply the file turned round — a group's next
                // opened by an earlier record keeps the place it was opened in (Serena's Plague Wind in
                // an Orsi save, stays behind Black Death).
                Assert.Equal(TurnedRound(n.Disciplines.Select(d => (d.Name, d.Level)), powers), a.Disciplines.All.Select(d => d.Name.ToLowerInvariant()));
                Assert.Equal(n.SelectedDiscipline?.ToLowerInvariant(), a.Disciplines.Current?.Name.ToLowerInvariant());

                // Everything carried, where it was; what is worn, worn — the hand holds the weapon.
                Assert.Equal(n.Inventory.Items.Select(x => (x.Name.ToLowerInvariant(), (int)x.Item!.Location)).OrderBy(x => x),
                             a.Inventory.Items.Select(x => (x.Thing!.Template.ToLowerInvariant(), x.Location)).OrderBy(x => x));
                if (n.Inventory.Items.FirstOrDefault(x => x.Item!.Location == Slot.RightWeapon) is { } hand)
                    Assert.Equal(hand.Name.ToLowerInvariant(), a.Weapon?.Name.ToLowerInvariant());

                // Every effect comes across, each worn item's hanging from it — and a discipline running
                // (an effect with a script) with its script's own words (until then it was left behind).
                var kept = n.Effects;
                var live = t.Effects.All.Where(e => !e.Spent).ToArray();
                Assert.Equal(kept.Select(e => e.Name), live.Select(e => e.Definition.Name));
                foreach (var (e, l) in kept.Zip(live))
                    if ((e.Flags & Effect.Worn) != 0)
                        Assert.True(l.Source?.Thing is { } source && source.Guid > 0, $"{who}: {e.Name} lost its item");
                var savedMember = game.Party.Members[i];
                foreach (var e in kept.Where(e => e.Script is { Record.ScriptState.Length: > 0 }))
                    Assert.Equal(e.Script!.Class, savedMember.Effects!.First(x => x.Guid == (int)e.Id).Script?.Class);
            }
        }
    }

    /// <summary>
    /// **A real case**: a last 1.1 save in the monastery, vampire Christof with
    /// Possession at 4 and Wilhem with his bow, brought over whole — so that what was tried in 1.1
    /// can be tried in Vitomir from the same place. The save is a player's own, so this runs
    /// only where it is.
    /// </summary>
    [Fact]
    public void A_vampire_Christof_comes_across_with_his_disciplines_and_his_sword()
    {
        using var files = Install.Open();
        var slot = Slots(files).Select(s => (Slot: s, Save: NsgSave.ReadFile(Path.Combine(s, "!main.nsg"))))
                               .FirstOrDefault(x => x.Save.Party.Players.Any(p => p.Actor!.ActorType == (byte)ActorType.Vampire
                                                                                  && p.Actor.Disciplines.Any(d => d.Name == "possession" && d.Level > 0)
                                                                                  && p.Actor.Inventory.Items.Any(i => i.Item!.Location == Slot.RightWeapon)));
        // With a weapon in his hand: a save just after a jump to another city has him with nothing but a
        // pistol in his pack (an Orsi save), which is right, and not this case.
        Assert.SkipWhen(slot.Save is null, "no 1.1 save with a vampire who has Possession and a weapon in hand");
        var game = SaveImport.FromOriginal(files, ChronicleSet.Load(files), slot.Save!, Path.GetFileName(slot.Slot), [])!;
        // 1.1 has no switches: its game was played with 1.1's rules, whatever is in force here.
        Assert.Equal("faithful", game.Header.Profile);
        Assert.Empty(game.Header.Rules);
        var christof = game.Party.Members[0].Actor!;
        Assert.Equal("Vampire", christof.Type);
        Assert.Equal("playerchristof.nag", game.Party.Members[0].Foley, ignoreCase: true);
        // Possession as the save has it, level and quick slot alike (the slot is whatever he last gave it:
        // a laptop run found it off the quick bar in a newer save).
        var saved = slot.Save!.Party.Players[0].Actor!.Disciplines.First(d => d.Name == "possession");
        Assert.Contains(christof.Disciplines, d => d.Name == "Possession" && d.Level == saved.Level
                                                   && d.QuickSlot == (saved.Field18 == uint.MaxValue ? -1 : (int)saved.Field18));
        Assert.Contains(christof.Inventory.Values, i => i.Item!.Location == Slot.RightWeapon);
        Assert.Contains(game.Party.Members[0].Effects!, e => e.Source is not null);
    }
}
