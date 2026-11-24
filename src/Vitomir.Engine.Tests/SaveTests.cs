// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Vitomir's own save (`Engine.Save`): taken, written, read and put back. The test
/// that matters most is the round trip — a game saved, loaded into a freshly built place and saved
/// again comes out **identical, part for part** — because it catches a field that is written and
/// never read back, and one that is read back into the wrong place, which a reader alone never does.
/// </summary>
public partial class SaveTests
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
        session.Quests.Read = name => files.TryRead($"quests/{Path.GetFileNameWithoutExtension(name)}.nqd", out var d) ? d : null;
        place.Session = session;
        return new Game(place, session, chronicle, effects, powers);
    }

    /// <summary>A place brought back from a save the way a load does it, minds woken after.</summary>
    static Game Load(GameFiles files, SaveGame save)
    {
        var fresh = Start(files, awaken: false);
        SaveApply.Chronicle(fresh.Chronicle, save.Chronicle!);
        var world = save.World[save.Header.Location];
        var place = Location.Load(files, world.Level, things: true, sky: false,
                                  scene: world.Scene.Length > 0 ? world.Scene : null, player: "Christof", entrance: world.Entrance);
        var restore = SaveApply.Place(place, world, fresh.Effects, fresh.Powers);
        var steered = SaveApply.Party(place, save.Party, restore, fresh.Effects, fresh.Powers);
        var minds = place.Awaken(new AiClasses(files), 1, fresh.Effects, fresh.Powers, fromSave: true);
        if (steered is not null) minds.Steered = steered;
        var session = new Session { Here = place, Chronicle = fresh.Chronicle };
        session.Quests.Read = fresh.Session.Quests.Read;
        place.Session = session;
        SaveApply.Session(session, save.Session, place);
        place.Milliseconds = (int)save.Header.ClockMs;
        return new Game(place, session, fresh.Chronicle, fresh.Effects, fresh.Powers);
    }

    /// <summary>Every part's text, the save time left out (it is the clock on the wall, not the game's).</summary>
    static Dictionary<string, string> Texts(SaveGame save)
    {
        save.Header.Saved = "";
        return SaveArchive.Parts(save).ToDictionary(p => p.Name, p => p.Text);
    }

    [Fact]
    public void A_game_saved_loaded_and_saved_again_comes_out_the_same()
    {
        using var files = Install.Open();
        var g = Start(files);
        var place = g.Place;
        var christof = place.Player!.Thing;
        var me = christof.Actor!;

        // A game with things in it: gold and a quest; a flag and a shut exit; a wound, a learnt
        // discipline and a sword in hand; a barrel opened, a townsman killed, a thing moved, a thing
        // made and a thing taken away.
        g.Session.Gold = 321;
        Assert.NotNull(g.Session.Quests.Load("L1_FindHeart"));
        g.Chronicle.SetFlag(120);
        g.Chronicle.At("OldTownDay")!.Exits[1].Closed = true;
        me.Health = me.MaxHealth * 0.5f;
        Assert.NotNull(DisciplineBook.SetLevel(christof, g.Powers, "Celerity", 2));
        var sword = place.Make("broadsword")!;
        me.Inventory.Add(sword.Item!);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(christof, sword.Item!, Slot.None, g.Effects, 0));
        var barrel = place.Things.First(t => t.Type == ThingType.Container);
        Assert.True(Container.Open(barrel, christof));
        var victim = place.Things.First(t => t.Actor is { } a && !ReferenceEquals(t, christof) && !a.Has(Actor.Flag.Party));
        // Poison: a killing blow whose wound switch hangs nothing. A lethal one hangs
        // its 300 ms blood on the body, which the clock set below has already expired by the load.
        Damage.Kill(victim, christof, HitKind.Melee, DamageType.Poison, 1000);
        var moved = place.Things.First(t => t.Type == ThingType.Prop);
        place.Move(moved, moved.Position + new Vector3(10, 0, 0));
        var made = place.Place("potionHealth", christof.Position + new Vector3(30, 0, 0), Vector3.Zero, christof.Sector)!;
        var gone = place.Things.Last(t => t.Type == ThingType.Prop && !ReferenceEquals(t, moved) && ThingIds.Placed(place, t) is not null);
        place.Remove(gone, drop: false);
        place.Milliseconds = 123_456;

        var first = SaveCapture.Take(place, g.Session, now: new DateTime(2026, 9, 22, 12, 0, 0));
        string zip = Path.Combine(Path.GetTempPath(), $"vitomir-save-{Guid.NewGuid():n}{SaveArchive.Extension}");
        try
        {
            SaveArchive.Write(first, zip);
            var read = SaveArchive.Read(zip);
            Assert.Equal(Texts(first), Texts(read));

            var loaded = Load(files, read);
            var second = SaveCapture.Take(loaded.Place, loaded.Session, now: new DateTime(2026, 9, 22, 12, 0, 0));
            // The one thing a load changes: the thing list comes back turned round, as 1.1's does
            // (the save writes it head first and the load links each in at the head; SaveOrder).
            foreach (var (key, again) in second.World)
            {
                Assert.Equal(Enumerable.Reverse(first.World[key].ListOrder!), again.ListOrder);
                again.ListOrder = first.World[key].ListOrder;
            }
            var a = Texts(first);
            var b = Texts(second);
            Assert.Equal(a.Keys.Order(), b.Keys.Order());
            foreach (var (name, text) in a) Same(name, text, b[name]);

            // And what the game sees is what was there.
            var back = loaded.Place;
            var him = back.Player!.Thing.Actor!;
            Assert.Equal(321, loaded.Session.Gold);
            Assert.NotNull(loaded.Session.Quests.Find("L1_FindHeart"));
            Assert.True(loaded.Chronicle.GetFlag(120));
            Assert.True(loaded.Chronicle.At("OldTownDay")!.Exits[1].Closed);
            Assert.Equal(me.Health, him.Health);
            Assert.Equal(2, him.Disciplines["Celerity"]!.Level);
            Assert.Same(him.Weapon, him.Inventory.Items.First(i => i.Name == "broadsword").Weapon);
            string barrelId = ThingIds.Of(place, barrel)!;
            var barrelBack = back.Things.FirstOrDefault(t => ThingIds.Of(back, t) == barrelId);
            if (barrelBack is not null) Assert.Equal(barrel.State, barrelBack.State);
            string victimId = ThingIds.Of(place, victim)!;
            Assert.True(back.Things.First(t => ThingIds.Of(back, t) == victimId).Actor!.Dead);
            string madeId = ThingIds.Of(place, made)!;
            var madeBack = back.Things.First(t => ThingIds.Of(back, t) == madeId);
            Assert.Equal(made.Guid, madeBack.Guid);
            Assert.Equal(made.Position, madeBack.Position);
            string goneId = ThingIds.Of(place, gone)!;
            Assert.DoesNotContain(back.Things, t => ThingIds.Of(back, t) == goneId);
            Assert.Contains(goneId, read.World[read.Header.Location].Removed);
        }
        finally { File.Delete(zip); }
    }

    /// <summary>
    /// The worry: items can be dropped on the ground so that they
    /// overlap perfectly. A made thing is named by the place and a count, never by where it lies,
    /// so two made on exactly the same spot are two things in the file and two things after a load.
    /// </summary>
    [Fact]
    public void Two_things_dropped_on_the_same_spot_stay_two_things()
    {
        using var files = Install.Open();
        var g = Start(files);
        var christof = g.Place.Player!.Thing;
        var spot = christof.Position + new Vector3(40, 0, 0);
        var one = g.Place.Place("potionHealth", spot, Vector3.Zero, christof.Sector)!;
        var two = g.Place.Place("potionHealth", spot, Vector3.Zero, christof.Sector)!;
        Assert.Equal(one.Position, two.Position);
        Assert.NotEqual(ThingIds.Of(g.Place, one), ThingIds.Of(g.Place, two));

        var save = SaveCapture.Take(g.Place, g.Session);
        var world = save.World[save.Header.Location];
        Assert.Contains(ThingIds.Of(g.Place, one)!, world.Things.Keys);
        Assert.Contains(ThingIds.Of(g.Place, two)!, world.Things.Keys);

        var back = Load(files, SaveArchive.Read(Write(save))).Place;
        Assert.Equal(2, back.Things.Count(t => t.Template == "potionHealth" && t.Position == spot));
    }

    /// <summary>A save's text is JSON with comments, and a hand edit that adds a comment or a trailing comma still loads.</summary>
    [Fact]
    public void A_hand_edited_part_with_comments_and_trailing_commas_still_reads()
    {
        using var files = Install.Open();
        var g = Start(files);
        g.Session.Gold = 5;
        var save = SaveCapture.Take(g.Place, g.Session);
        string dir = Path.Combine(Path.GetTempPath(), $"vitomir-plain-{Guid.NewGuid():n}");
        try
        {
            SaveArchive.WritePlain(save, dir);
            string session = Path.Combine(dir, SaveArchive.SessionPart);
            string text = File.ReadAllText(session);
            Assert.Contains("// What belongs to no one place", text);
            // A person's edit: more gold, a comment of their own, and a trailing comma.
            text = text.Replace("\"gold\": 5,", "\"gold\": 5000, // cheated in\n");
            text = text.Replace("\"effectCounter\"", "/* bump */ \"effectCounter\"");
            File.WriteAllText(session, text);
            var edited = SaveArchive.Read(dir);
            Assert.Equal(5000, edited.Session.Gold);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Peek_writes_a_plain_save_when_asked()
    {
        string? to = Environment.GetEnvironmentVariable("VITOMIR_SAVE_PEEK");
        Assert.SkipWhen(to is null, "set VITOMIR_SAVE_PEEK to a folder to see a save");
        using var files = Install.Open();
        var g = Start(files);
        SaveArchive.WritePlain(SaveCapture.Take(g.Place, g.Session), to!);
    }

    [Fact]
    public void The_description_is_1_1s_place_and_clock()
    {
        Assert.Equal("The Convent (00:24:25)", SaveCapture.Describe("The Convent", 1_465_958));
    }

    [Fact]
    public void The_flags_are_named_beside_the_number()
    {
        using var files = Install.Open();
        var g = Start(files);
        var save = SaveCapture.Take(g.Place, g.Session);
        string party = SaveArchive.Parts(save).First(p => p.Name == SaveArchive.PartyPart).Text;
        Assert.Contains("PARTY", party);                   // Christof's actor flags carry the party bit
        Assert.Contains("// Christof", party);             // and his record says who he is
    }

    /// <summary>Two texts equal, or a failure that shows the lines around the first difference.</summary>
    static void Same(string name, string expected, string actual)
    {
        if (expected == actual) return;
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        int i = 0;
        while (i < e.Length && i < a.Length && e[i] == a[i]) i++;
        int from = Math.Max(0, i - 25);
        string Around(string[] lines) => string.Join("\n", lines.Skip(from).Take(i - from + 4));
        Assert.Fail($"{name} differs at line {i + 1}:\n--- saved first\n{Around(e)}\n--- saved after the load\n{Around(a)}");
    }

    static string Write(SaveGame save)
    {
        string zip = Path.Combine(Path.GetTempPath(), $"vitomir-save-{Guid.NewGuid():n}{SaveArchive.Extension}");
        SaveArchive.Write(save, zip);
        return zip;
    }
}
