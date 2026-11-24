// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Bringing a game over from 1.1: the party and the story out of `!main.nsg`, as a Vitomir save.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Save;

/// <summary>
/// **1.1's save, read into Vitomir's** (roadmap item 6, its second half — the first part of it).
/// What `!main.nsg` holds comes across whole: the party (who, where they stood, their type, stats,
/// soaks, health, experience, every item in the pack, the strip, the vault and on the body, the
/// live effects, the disciplines with their levels and quick slots), the purse, the quest log and
/// the story (the chronicle's flags, every location's scene and exits, which location the party is
/// in). And every place 1.1 kept a `location_&lt;n&gt;.nsw` for comes across **as it was left**
/// (<see cref="NswImport"/>): the dead where they fell, the opened open, the taken gone,
/// the things made in play made again, each script's own words, timers and captures. A place with no
/// file was never visited and is built fresh when the party gets there, as in 1.1. The shops are not
/// brought over, which costs nothing a load in 1.1 does not already cost: the first visit to a shop
/// after a load stocks it afresh there too.
///
/// It works by doing what the engine does: the place is built, the party is put in it and given
/// 1.1's numbers, and <see cref="SaveCapture.Take"/> writes the save — so the result is exactly a
/// save Vitomir could have written itself, and <see cref="SaveApply"/> reads it with nothing special.
/// </summary>
public static class SaveImport
{
    /// <summary>
    /// The save for <paramref name="nsg"/>, or null (with the reason in <paramref name="notes"/>)
    /// when its chronicle or location is not in the install. <paramref name="source"/> is where it
    /// came from, for the save's own note — and, when it is the save's folder or its `!main.nsg`,
    /// where its `.nsw` files are looked for; <paramref name="folder"/> says so outright. The files
    /// are only read.
    /// </summary>
    public static SaveGame? FromOriginal(GameFiles files, ChronicleSet chronicles, NsgSave nsg, string source,
                                         List<string> notes, Func<string, string>? describe = null, string? folder = null)
    {
        folder ??= Directory.Exists(source) ? source : File.Exists(source) ? Path.GetDirectoryName(Path.GetFullPath(source)) : null;
        var places = NswPlaces.Read(folder, notes);
        string chronicleName = Path.GetFileNameWithoutExtension(nsg.Chronicle.Name);
        if (chronicles[chronicleName] is not { } chronicle) { notes.Add($"no chronicle called {chronicleName}"); return null; }
        int current = nsg.Chronicle.Fields.Length > 0 ? (int)nsg.Chronicle.Fields[0] : -1;
        if (chronicle.At(current) is not { } here || current >= nsg.Chronicle.Locations.Length)
        {
            notes.Add($"the save is in location {current}, which {chronicle.Name} does not have");
            return null;
        }

        Story(chronicle, nsg.Chronicle, current);
        Words(chronicle, nsg);
        var state = nsg.Chronicle.Locations[current];
        string level = Strip(state.Level);
        string? scene = Strip(state.Scene) is { Length: > 0 } s && !s.Equals("null", StringComparison.OrdinalIgnoreCase) ? s : null;
        var players = nsg.Party.Players.Where(p => p.Actor is not null).ToArray();
        // A network game's save has no party (the writer writes none while a network game is up, `NsgSave.HasParty`): each
        // player brings their own character to it (`NetSaves`), so it comes across with nobody in the party, the place as it was.
        bool network = !nsg.HasParty;
        if (players.Length == 0 && !network) { notes.Add("the save has nobody in the party"); return null; }

        // Built as a load builds: no number taken while 1.1's are handed back, the leader's body
        // 1.1's leader's, and the counter 1.1's own after (SaveGuids.cs).
        var place = Location.Load(files, level, sky: false, scene: scene, player: network ? null : players[0].Name,
                                  guids: new ThingGuids { Next = (int)nsg.ThingCounter, Loading = places.ContainsKey(current),
                                                          Arriving = network ? 0 : (int)players[0].Guid });
        var leader = place.Player?.Thing;
        if (!network && leader is null) { notes.Add($"{level} has no start for {players[0].Name}"); return null; }
        long clock = nsg.Clock.Length > 11 ? nsg.Clock[11] : 0;
        place.Milliseconds = (int)clock;
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var session = new Session { Here = place, Chronicle = chronicle };
        place.Session = session;

        // The party's running disciplines' scripts: their words, by script key, and their GUIDs, so the
        // place's file can name them in its timers.
        var partyScripts = new Dictionary<string, ScriptSave>(StringComparer.Ordinal);
        var partyKeys = new Dictionary<uint, string>();
        for (int i = 0; i < players.Length; i++)
        {
            var p = players[i];
            var (at, yaw) = Placement(p.Transform);
            WorldThing? t = i == 0 ? leader : place.Place(p.Name, at, new Vector3(0f, 0f, yaw), leader!.Sector, (int)p.Guid);
            if (t?.Actor is null) { notes.Add($"{p.Name} could not be made from its template"); continue; }
            if (i == 0) { place.Move(t, at); t.Orientation = new Vector3(0f, 0f, yaw); }
            Kit(files, t.Actor);
            Member(place, t, p, effects, powers, notes, partyScripts, partyKeys);
        }
        if (place.Thing((int)nsg.Party.CurrentPlayerGuid) is { } steered) place.SetSteered(steered);

        session.Gold = (int)nsg.Party.Gold;
        // The place the party is in, as its `.nsw` has it — laid over the place once the party is in it.
        var hereScripts = places.TryGetValue(current, out var hereFile)
            ? NswImport.Apply(place, hereFile, clock, effects, powers, notes, partyKeys) : null;
        place.Guids.Loading = false;
        place.Guids.Arriving = 0;
        int counter = Math.Max(1, (int)nsg.ThingCounter);
        foreach (var t in place.Things) if (!ThingGuids.IsPlaceholder(t.Guid)) counter = Math.Max(counter, ThingGuids.Raw(t.Guid) + 1);
        place.Guids.Resume(counter);
        var game = SaveCapture.Take(place, session, campaign: null, describe);
        if (network) game.Party = new PartySave();
        foreach (var member in game.Party.Members)
        {
            string memberId = ThingIds.Party + (member.Actor?.CastId ?? member.Template);
            foreach (var e in member.Effects ?? [])
                if (partyScripts.TryGetValue(ScriptKeys.OfEffect(memberId, e.Guid), out var script)) e.Script = script;
        }
        if (hereScripts is not null) NswImport.Into(game.World[game.Header.Location], hereScripts, clock);
        // A place with no `.nsw` is not kept: a load builds it as it was first made — a journey into
        // it, with its scripts told `created` and `beginscene` — rather than bring back a copy that
        // never was.
        else game.World.Remove(game.Header.Location);
        // Every other place 1.1 remembers, each as its file has it.
        foreach (var (index, file) in places)
            if (index != current && NswPlaces.Place(files, chronicle, nsg, index, file, clock, effects, powers, notes) is { } kept)
                game.World[kept.Name] = kept.Save;
        game.Header.Description = nsg.Description.Length > 0 ? nsg.Description : game.Header.Description;
        game.Header.ClockMs = clock;
        // 1.1 has no switches: the game in its save was played with 1.1's rules, whatever is in force here
        // (ours) — and the load that follows says what this run does differently.
        game.Header.Profile = Features.FeatureRegistry.Faithful;
        game.Header.Rules = [];
        game.Header.Notes = network ? $"Imported from 1.1's {source}, a network game's save: the story is 1.1's, and it has no party, as 1.1 writes none in a network game " +
                                      "(each player brings their own character); " + (places.Count > 0 ? $"the {places.Count} places it remembers are 1.1's too." : "the places are as they were first built.")
                          : $"Imported from 1.1's {source}. The party and the story are 1.1's; " +
                            (places.Count > 0 ? $"so are the {places.Count} places it remembers." : "the places are as they were first built — it has no .nsw files.");
        game.Session.Quests = [.. nsg.Chronicle.Quests.Select(Quest)];
        if (nsg.Chronicle.Text1Af88.Length > 0 && game.Chronicle is not null)
            game.Chronicle.Weather = nsg.Chronicle.Text1Af88;                  // London's drizzle in a London save
        Teleports(game, chronicle, nsg, notes);
        game.Session.TreasureCounter = (int)nsg.TreasureCounter;
        game.Session.EffectCounter = (int)nsg.EffectCounter;
        notes.Add($"{nsg.Description}: {chronicle.Name}, {here.Name} ({level}{(scene is null ? "" : "/" + scene)}), " +
                  (network ? "a network game's save (no party: 1.1 writes none in a network game), " : $"{players.Length} in the party, {nsg.Party.Gold} gold, ") +
                  $"{nsg.Chronicle.Quests.Length} quests, " +
                  $"{places.Count} places as they were left");
        return game;
    }

    /// <summary>
    /// **The chronicle's own words the story leaves in a save**, beyond its flags and locations:
    /// <list type="bullet">
    /// <item>**Where Walk the Abyss leads and where a character revives** — the save's chronicle words 1 to 4, which `SetPortalLocation`
    /// and `SetReviveLocation` write. A West London save has the
    /// portal at London's location 9, which a script put there.</item>
    /// <item>**Where the next advance goes** — the header's three words after the version
    /// (the location, the entrance and the scene), which a script's `SetupAdvance`
    /// sets and the advance itself spends (its state 0xf puts the scene on the location
    /// and clears it; state 0x10 makes the journey and sets both words back to −1). A save
    /// made between the two carries them — a University Haven save holds
    /// location 20, entrance 0, `ANZK_14_1.nsd` — and 1.1's load reads them straight back,
    /// so the advance still goes where the script said.</item>
    /// </list>
    /// Before then the import left all three behind, taking them for a mod's concern.
    /// </summary>
    static void Words(Chronicle c, NsgSave nsg)
    {
        int Word(int i) => nsg.Chronicle.Fields.Length > i && nsg.Chronicle.Fields[i] != uint.MaxValue ? (int)nsg.Chronicle.Fields[i] : -1;
        if (Word(1) >= 0 && Word(1) < c.Count) c.Portal = (Word(1), Word(2));
        if (Word(3) >= 0 && Word(3) < c.Count) c.Revive = (Word(3), Word(4));
        int at = nsg.FieldE4 == uint.MaxValue ? -1 : (int)nsg.FieldE4;
        if (at >= c.Count) at = -1;
        int entrance = nsg.Field108 == uint.MaxValue ? -1 : (int)nsg.Field108;
        string scene = nsg.TextE8.Length > 31 ? nsg.TextE8[..31] : nsg.TextE8;
        c.AdvanceTo = (at, scene, entrance);
    }

    /// <summary>
    /// **The teleport table** (tag 8): each slot in use — its creator word
    /// set — with its two ends found again among the imported places' things by 1.1's GUID,
    /// and its creator among the party. An end in a place with no `.nsw` is not there to find (the place
    /// is built as it was first made, portal-less), and is left unnamed. Three Prague saves
    /// (SP1–SP3) each hold one pair open, which the import dropped until then.
    /// </summary>
    static void Teleports(SaveGame game, Chronicle c, NsgSave nsg, List<string> notes)
    {
        string? IdIn(string? location, uint guid)
        {
            if (location is null || guid == 0 || !game.World.TryGetValue(location, out var place)) return null;
            foreach (var (id, t) in place.Things) if (t.Guid == (int)guid) return id;
            return null;
        }
        for (int i = 0; i < nsg.Teleports.Length && i < World.Teleports.Max; i++)
        {
            var t = nsg.Teleports[i];
            if (t.F == 0) continue;
            var creator = game.Party.Members.FirstOrDefault(m => m.Guid == (int)t.F);
            if (creator is null) { notes.Add($"teleport slot {i}: its creator, GUID {t.F}, is not in the party; left out"); continue; }
            string? inAt = c.NameOf((int)t.A), outAt = c.NameOf((int)t.C);
            var slot = new TeleportSave
            {
                Slot = i, InLocation = inAt, InThing = IdIn(inAt, t.B), OutLocation = outAt, OutThing = IdIn(outAt, t.D),
                Flags = t.E, Creator = ThingIds.Party + (creator.Actor?.CastId ?? creator.Template), ExpireMs = t.G,
            };
            if (slot.InThing is null || slot.OutThing is null)
                notes.Add($"teleport slot {i}: {(slot.InThing is null ? $"the far end (GUID {t.B}) in {inAt}" : "")}" +
                          $"{(slot.InThing is null && slot.OutThing is null ? " and " : "")}{(slot.OutThing is null ? $"the near end (GUID {t.D}) in {outAt}" : "")} not found among the places it remembers");
            game.Session.Teleports.RemoveAll(x => x.Slot == i);
            game.Session.Teleports.Add(slot);
        }
    }

    /// <summary>
    /// The story (1.1's tags 2 and 3): the chronicle's flags — word <c>w</c> bit <c>b</c> is flag
    /// <c>32w + b</c>, so <c>JIRI_TALKEDONCE</c> (120) is word 3's bit 24 — and each location's
    /// scene, flags and exits. The loader clears <c>0x100000</c> from a location's flags (the writer
    /// writes the flags as they stand), so that bit does not come across.
    /// </summary>
    static void Story(Chronicle c, NsgChronicle saved, int current)
    {
        c.ClearAllFlags();
        for (int w = 0; w < saved.Flags.Length; w++)
            for (int b = 0; b < 32; b++)
                if ((saved.Flags[w] & (1u << b)) != 0) c.SetFlag(w * 32 + b);
        for (int i = 0; i < saved.Locations.Length && i < c.Count; i++)
        {
            var from = saved.Locations[i];
            var to = c.At(i)!;
            to.Scene = from.Scene;
            to.Flags = from.Flags & ~0x100000u;
            to.ExitCount = Math.Min(from.Exits.Length, to.Exits.Length);
            for (int e = 0; e < to.Exits.Length; e++)
            {
                var x = to.Exits[e];
                if (e < from.Exits.Length)
                {
                    x.To = from.Exits[e].To < (uint)c.Count ? (int)from.Exits[e].To : -1;
                    x.Entrance = (int)from.Exits[e].Entrance;
                    x.Closed = from.Exits[e].Closed != 0;
                }
                else { x.To = -1; x.Entrance = 0; x.Closed = false; }
            }
        }
        c.Current = current;
    }

    /// <summary>
    /// Where a thing stands and which way it faces, out of its transform: three
    /// rows of rotation and then the position. A character only ever turns about the vertical, so
    /// the first row is <c>(cos, sin, 0)</c> of its heading.
    /// </summary>
    static (Vector3 At, float Yaw) Placement(byte[] m)
    {
        if (m.Length < 48) return (Vector3.Zero, 0f);
        float F(int i) => BitConverter.ToSingle(m, i * 4);
        float yaw = MathF.Atan2(F(1), F(0)) * (180f / MathF.PI);
        if (yaw < 0f) yaw += 360f;
        return (new Vector3(F(9), F(10), F(11)), yaw);
    }

    /// <summary>
    /// The bounds of a player character's stats, which 1.1 does not save: they are its `.NPC`
    /// file's rows, put on when the character is made.
    /// </summary>
    static void Kit(GameFiles files, Actor a)
    {
        if (a.RpgStats.Length == 0 || !files.TryRead(NpcFile.PathFor(a.RpgStats), out var data)) return;
        a.Apply(NpcFile.Read(data, NpcFile.PathFor(a.RpgStats)));
    }

    /// <summary>One party member as the save has them.</summary>
    static void Member(Location place, WorldThing t, NsgThing p, EffectDefinitions effects, DisciplineDefinitions powers,
                       List<string> notes, Dictionary<string, ScriptSave> scripts, Dictionary<uint, string> scriptKeys)
    {
        var a = t.Actor!;
        var n = p.Actor!;
        // Its foley — what it sounds like — is in the record (`playerChristof.nag` for the vampire,
        // where the template says the human's), because a script changes it (`SetFoley`).
        if (p.Template is { } foley && foley.EndsWith(".nag", StringComparison.OrdinalIgnoreCase)) t.Foley = foley;
        a.Type = (ActorType)n.ActorType;
        a.Flags = n.Field1F4;                       // THING_AF_*
        a.Flags2 = n.Flags;                         // THING_AF2_*
        if (n.Text208 is { Length: > 0 } clan) { a.Clan = clan; a.ClanRecord = Clans.For(place.Files!)[clan]; }
        a.Health = n.Health;
        a.MaxHealth = n.MaxHealth;
        a.WalkSpeed = n.WalkSpeed;
        a.RunSpeed = n.RunSpeed;
        a.MoveSpeedScale = n.MoveSpeed;
        a.AttackSpeedScale = n.AttackSpeed;
        if (p.Player is { } pl) { a.SpendableExperience = pl.Field3E8; a.Experience = pl.Field3EC; }

        // What it carries, made again under the GUIDs it had.
        foreach (var old in a.Inventory.Items.ToArray())
        {
            a.Inventory.Remove(old);
            if (old.Thing is { } body) place.Remove(body, drop: false);
        }
        a.Held = null;
        a.Weapon = a.DefaultWeapon;
        a.Offhand = null;
        a.Armor = null;
        var byGuid = new Dictionary<uint, Item>();
        foreach (var saved in n.Inventory.Items)
        {
            if (saved.Item is not { } s || place.Restore(saved.Name, (int)saved.Guid, null) is not { Item: { } item } body)
            {
                notes.Add($"{p.Name} carried a {saved.Name} that cannot be made");
                continue;
            }
            item.Flags = (ItemFlags)s.Flags;
            item.Amount = (int)s.Amount;
            item.Location = (int)s.Location;
            item.Cell = s.Fields2BC is [var x, var y, ..] ? ((int)x, (int)y) : default;
            item.BaseCost = (int)s.BaseCost;
            item.SellFlags = (int)s.SellFlags;
            item.Effects = [.. s.Effects.Select(e => new ItemEffect(e.Name, e.Level, e.Level < 0 ? e.ValueAsFloat : null, (int)e.Time))];
            if (item.Weapon is { } weapon)
            {
                for (int k = 0; k < weapon.Attacks.Length && k < saved.Ammo.Length; k++) weapon.Attacks[k].Ammo = (int)saved.Ammo[k];
                // The byte after the rounds: which attack a swing uses.
                if (saved.AmmoTrailer < weapon.Attacks.Length) weapon.CurrentAttack = saved.AmmoTrailer;
            }
            body.Owner = t;
            item.Thing = body;
            a.Inventory.Add(item);
            byGuid[saved.Guid] = item;
        }
        foreach (var item in a.Inventory.Items) Equipment.Restore(t, item);

        // The live effects, as the file has them:
        // a worn item's own hang from it, so taking it off ends exactly those.
        t.Effects.Clear();
        foreach (var e in n.Effects)
        {
            if (effects[e.Name] is not { } def) { notes.Add($"{p.Name} had {e.Name}, which effects.ned does not have"); continue; }
            // A discipline running: its script's own words come across with it, decoded by the
            // shipped class's layout, and its GUID is noted for the place file's timers.
            if (e.Script is { } es)
            {
                string key = ScriptKeys.OfEffect(ThingIds.Member(t), (int)e.Id);
                scriptKeys[es.Guid] = key;
                if (NswImport.Decode(es) is { } saved) scripts[key] = saved;
            }
            var effect = new Effect
            {
                Definition = def, Level = e.ValueLevel, Value = e.ValueAsFloat, Until = e.Level, NextTick = (int)e.NextAt,
                Fade = BitConverter.UInt32BitsToSingle(e.Field10), Guid = (int)e.Id, CreatorGuid = (int)e.SourceGuid,
                Flags = e.Flags, IntParam = (int)e.Field28, FloatParam = BitConverter.UInt32BitsToSingle(e.Field2C),
            };
            if ((e.Flags & Effect.Worn) != 0 && byGuid.TryGetValue(e.SourceGuid, out var source)) effect.Source = source;
            t.Effects.Append(effect);
        }

        // The disciplines (tag 0x28): the level, the quick slot, the flags
        // and the count of casts.
        var list = new List<Discipline>();
        foreach (var d in n.Disciplines)
        {
            if (powers[d.Name] is not { } def) { notes.Add($"{p.Name} knew {d.Name}, which game.ndd does not have"); continue; }
            list.Add(new Discipline
            {
                Definition = def, Owner = t, Level = Math.Max(-1, d.Level), Flags = d.Field8, CastCount = (int)d.FieldC,
                QuickSlot = d.Field18 == uint.MaxValue ? -1 : (int)d.Field18,
            });
        }
        a.Disciplines.Replace(list);
        a.Disciplines.Current = n.SelectedDiscipline is { } selected ? a.Disciplines[selected] : null;

        // The stats and soaks last, as the file has them: base and current both, so nothing the
        // steps above did to them stands.
        for (int i = 0; i < Actor.Stats && i < n.Stats.Length; i++) { a.BaseStat[i] = n.BaseStats[i]; a.Stat[i] = n.Stats[i]; }
        for (int i = 0; i < Actor.Soaks && i < n.Soaks.Length; i++) { a.BaseSoak[i] = n.BaseSoaks[i]; a.Soak[i] = n.Soaks[i]; }
        // A shape change in force: the actor's last word, its template and flags (`NsgActor.Mind`).
        a.ShapeChange = ShapeShift.Loaded(n.Mind is { Length: > 0 } shape ? new ShapeSave { Template = shape, Flags = n.MindField } : null);
    }

    /// <summary>A quest (tags 5 and 6): the fields are the engine's own, at the offsets the save names them by.</summary>
    static QuestSave Quest(NsgQuest q) => new()
    {
        Slot = q.Index, Name = q.Name, ShortKey = q.Text38, LongKey = q.Text58, Icon = q.Text78,
        Flags = q.Fields.Length > 1 ? q.Fields[1] : 0, Xp = q.Fields.Length > 2 ? q.Fields[2] : 0,
        Items = [.. q.Steps.Select(s => new QuestItemSave
        {
            Flags = s.Field0, Steps = (int)s.Field4, Step = (int)s.Field8, ShortKey = s.Text10, LongKey = s.Text30,
            Icon = s.Text5C, Xp = s.Field58,
        })],
    };

    static string Strip(string s)
    {
        int dot = s.LastIndexOf('.');
        return dot < 0 ? s : s[..dot];
    }
}
