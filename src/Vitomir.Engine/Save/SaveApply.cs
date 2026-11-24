// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Loading a save: a `SaveGame` put back onto a game. The order is 1.1's:
//
//   1. the chronicle — flags, scenes, exits — before anything is built, because which scene a
//      place is built with is the chronicle's to say;
//   2. the place the party is in, built fresh from its level and scene, and then every thing in it
//      given back what it had — a placed thing kept, carried off or destroyed, a made thing made
//      again under its own GUID;
//   3. the party, onto the leader's thing and into things made for the rest;
//   4. the purse, the quests, the shops;
//   5. and the scripts — made, `restore`d, their timers and captures put back, told `restored` —
//      which the caller does last, by entering the place with the `ScriptRestore` this hands back.
//
// Every other place the save remembers is parked in the campaign and brought back the same way the
// first time the party walks into it, which is when 1.1 reads its `.nsw`.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Save;

public static class SaveApply
{
    static readonly ILogger log = Diag.For(typeof(SaveApply).FullName!);

    // ---- the chronicle ---------------------------------------------------------------------------

    /// <summary>The story as the save left it. Locations the save does not know keep what the chronicle file says.</summary>
    public static void Chronicle(Chronicle c, ChronicleSave save)
    {
        c.ClearAllFlags();
        foreach (int f in save.Flags) c.SetFlag(f);
        foreach (var (name, entry) in save.Locations)
        {
            if (c.At(name) is not { } loc) { log.LogWarning("load: the chronicle has no location {Name}", name); continue; }
            loc.Scene = entry.Scene;
            loc.Flags = entry.Flags;
            loc.ExitCount = Math.Min(entry.Exits.Count, loc.Exits.Length);
            for (int e = 0; e < loc.Exits.Length; e++)
            {
                var x = loc.Exits[e];
                if (e < entry.Exits.Count)
                {
                    x.To = entry.Exits[e].To is { } to ? c.Find(to) : -1;
                    x.Entrance = entry.Exits[e].Entrance;
                    x.Closed = entry.Exits[e].Closed;
                }
                else { x.To = -1; x.Entrance = 0; x.Closed = false; }
            }
        }
        if (save.Portal is { } p) c.Portal = (p.Location is { } pl ? c.Find(pl) : -1, p.Entrance);
        if (save.Revive is { } r) c.Revive = (r.Location is { } rl ? c.Find(rl) : -1, r.Entrance);
        c.AdvanceTo = save.Advance is { } a ? (a.Location is { } al ? c.Find(al) : -1, a.Scene, a.Entrance) : (-1, "", -1);
        c.Current = c.Find(save.Current);
    }

    // ---- what belongs to no place ----------------------------------------------------------------

    /// <summary>The purse, the quest log, the shops and the two counters.</summary>
    public static void Session(Session session, SessionSave save, Location here)
    {
        session.Gold = save.Gold;
        session.Quests.Restore(save.Quests.Select(q =>
        {
            var quest = new Quest { Slot = q.Slot, Name = q.Name, Flags = q.Flags, Xp = q.Xp, ShortKey = q.ShortKey, LongKey = q.LongKey, Icon = q.Icon };
            quest.Items = [.. q.Items.Select(i => new QuestItem
            {
                Parent = quest, Flags = i.Flags, Steps = i.Steps, Step = i.Step, ShortKey = i.ShortKey,
                LongKey = i.LongKey, Icon = i.Icon, Xp = i.Xp,
            })];
            return quest;
        }));
        var stores = new List<Store>();
        foreach (var s in save.Stores)
        {
            var store = new Store
            {
                OwnerGuid = s.Owner is { } owner && Find(here, owner) is { } found ? found.Guid : s.OwnerGuid,
                DescriptionId = s.DescriptionId, File = "", Flags = s.Flags, BuyRate = s.BuyRate,
                SellRate = s.SellRate, SellMask = s.SellMask,
            };
            foreach (var line in s.Lines)
            {
                var l = new StockLine
                {
                    Remaining = line.Remaining, Copies = line.Copies, Direct = line.Template is not null,
                    Template = line.Template ?? "", Class = line.Class,
                };
                if (line.Stock is { } stock && here.Templates is { } templates && templates.Has(stock.Template)
                    && Item.From(stock.Template, templates.Resolve(stock.Template)) is { } item)
                {
                    ApplyItem(item, stock.Item);
                    l.Treasure = new TreasureRecord { Id = stock.Id, Template = stock.Template, Item = item };
                }
                store.Lines.Add(l);
            }
            store.Name = here.Session?.Shops.Text?.Invoke(store.DescriptionId) ?? store.DescriptionId;
            stores.Add(store);
        }
        session.Shops.Restore(stores);
        if (here.Treasure is { } t) t.Counter = Math.Max(1, save.TreasureCounter);
        if (save.EffectCounter > 0) EffectRuntime.NextGuid = save.EffectCounter;
        TeleportSaves.Apply(session.Teleports, save.Teleports, session.Chronicle);
    }

    /// <summary>A thing by its id, among the placed and made things here.</summary>
    static WorldThing? Find(Location place, string id)
    {
        foreach (var t in place.Things) if (ThingIds.Of(place, t) == id) return t;
        return null;
    }

    // ---- a place ------------------------------------------------------------------------------

    /// <summary>
    /// Gives a freshly built place — its level and scene loaded, **its minds not yet woken** —
    /// everything the save says about it. The minds are woken after (<see cref="Location.Awaken"/>
    /// with <c>fromSave</c>), because 1.1 makes a restored character's mind from the restored
    /// character: one that had got up is not laid down again. Answers what the script layer needs to rebuild its scripts; the caller
    /// hands that to <see cref="IScriptPersistence.Arm"/> before entering the place.
    /// </summary>
    public static ScriptRestore Place(Location place, LocationSave save, EffectDefinitions? effects,
                                      DisciplineDefinitions? powers, ScriptRestore? into = null)
    {
        place.Effects ??= effects;
        place.Powers ??= powers;
        var restore = into ?? new ScriptRestore();
        var built = new Dictionary<string, WorldThing>(StringComparer.Ordinal);
        foreach (var t in place.Things)
            if (ThingIds.Placed(place, t) is { } id && !ReferenceEquals(t, place.Player?.Thing)) built[id] = t;

        var carried = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in save.Things.Values) Carried(t, carried);

        // Every placed thing: kept, carried off, or gone.
        var byId = new Dictionary<string, WorldThing>(StringComparer.Ordinal);
        foreach (var (id, thing) in built)
        {
            if (save.Things.ContainsKey(id)) { byId[id] = thing; continue; }
            // A thing gone from the world keeps its GUID for the scripts that name it (Location.Gone).
            place.Gone[thing.Index] = thing.Guid;
            if (carried.Contains(id)) { place.Withdraw(thing); byId[id] = thing; continue; }
            place.Remove(thing, drop: false);
        }
        // Each placed thing given back the GUID it was saved with, as 1.1's load reads it, and every
        // saved number made free first for the thing it belongs to (SaveGuids.cs).
        var records = new Dictionary<string, ThingSave>(StringComparer.Ordinal);
        foreach (var (id, s) in save.Things) Records(id, s, records);
        var own = new Dictionary<WorldThing, int>(ReferenceEqualityComparer.Instance);
        foreach (var (id, thing) in byId) if (records.TryGetValue(id, out var r) && r.Guid > 0) own[thing] = r.Guid;
        var builtGuids = byId.ToDictionary(kv => kv.Key, kv => kv.Value.Guid, StringComparer.Ordinal);
        place.MakeWay(records.Values.Select(r => r.Guid).Where(g => g > 0).ToHashSet(), own);
        int? shift = null;
        foreach (var (id, thing) in byId)
            if (records.TryGetValue(id, out var saved))
            {
                int was = builtGuids[id];
                GiveBack(place, thing, saved.Guid);
                if (shift is null && saved.Guid > 0 && place.FromScene(was)) shift = thing.Guid - was;
            }
        // The scene's range moves with its numbers: a place numbered in one pass comes back a shift of
        // how it was built (what a network host tells a joining machine; LocationGuids.cs).
        if (shift is { } by) place.ShiftScene(by);
        // Everything made while the game ran, and any placed thing a changed scene no longer has.
        foreach (var (id, s) in save.Things)
        {
            if (byId.ContainsKey(id)) continue;
            if (place.Restore(s.Template, s.Guid, id) is not { } made)
            {
                log.LogWarning("load: {Place} {Id} is a {Template}, which there is no template for; left out", place.Name, id, s.Template);
                continue;
            }
            if (ThingIds.ParsePlaced(id) is not null)
                log.LogWarning("load: {Place} has no placed {Id} any more (its scene changed?); made again from {Template}", place.Name, id, s.Template);
            byId[id] = made;
        }

        var context = new Context(place, byId, restore);
        foreach (var (id, s) in save.Things)
            if (byId.TryGetValue(id, out var thing)) context.Apply(thing, id, s, inWorld: true);
        context.Link(save.Things);
        context.FixUp();
        SaveOrder.Apply(place, save, byId);                             // each linked in at the head as it is read

        place.ResumeGuids(save.NextGuid);
        place.Guids.Loading = false;                                    // the file's numbers are given; what is made from here takes the counter's
        place.Made = Math.Max(place.Made, save.Made);
        place.Entrance = save.Entrance;
        restore.ClockMs = save.ScriptClockMs;
        restore.Timers.AddRange(save.Timers);
        restore.ThingOf ??= key => context.ThingOf(key);
        restore.LooseOf ??= key => context.LooseOf(key);
        restore.Loose ??= obj => context.LooseSave(obj);
        return restore;
    }

    /// <summary>Every record by its id, the packs' contents included.</summary>
    static void Records(string id, ThingSave t, Dictionary<string, ThingSave> into)
    {
        into[id] = t;
        foreach (var (itemId, item) in t.Actor?.Inventory ?? []) Records(itemId, item, into);
    }

    /// <summary>
    /// A thing built for a load given its saved GUID (made with the file's number); one saved
    /// without a number — a format-1 save's other places (<see cref="SaveGuids.Convert"/>) — gets
    /// the game counter's next in place of the load's placeholder.
    /// </summary>
    static void GiveBack(Location place, WorldThing thing, int guid)
    {
        if (guid > 0)
        {
            if (!place.Rekey(thing, guid))
                log.LogWarning("load: {Place} {Template} cannot have GUID {Guid} back, which another thing holds; it keeps {Now}",
                               place.Name, thing.Template, guid, thing.Guid);
        }
        else if (ThingGuids.IsPlaceholder(thing.Guid)) place.Rekey(thing, place.Guids.Fresh(g => place.Thing(g) is not null));
    }

    /// <summary>A placed id's index in <paramref name="place"/>'s joined list, or null when it is not one of this place's scenes'.</summary>
    static int? IndexOf(Location place, string placedId)
    {
        if (ThingIds.ParsePlaced(placedId) is not var (scene, i)) return null;
        int worldCount = place.WorldScene?.Things.Length ?? 0;
        if (place.WorldScene is not null && string.Equals(scene, place.Name, StringComparison.OrdinalIgnoreCase) && i < worldCount) return i;
        if (place.ChapterScene is { } chapter && string.Equals(scene, place.SceneFile, StringComparison.OrdinalIgnoreCase) && i < chapter.Things.Length)
            return worldCount + i;
        return null;
    }

    static void Carried(ThingSave t, HashSet<string> into)
    {
        if (t.Actor is null) return;
        foreach (var (id, item) in t.Actor.Inventory) { into.Add(id); Carried(item, into); }
    }

    // ---- the party ------------------------------------------------------------------------------

    /// <summary>
    /// The party, into the place it is in: the leader onto the place's own player thing, and every
    /// other member into a thing made from its template, as a journey would carry them. Adds to
    /// <paramref name="restore"/>, which must be the place's.
    /// </summary>
    public static WorldThing? Party(Location place, PartySave save, ScriptRestore restore,
                                    EffectDefinitions? effects = null, DisciplineDefinitions? powers = null)
    {
        place.Effects ??= effects;
        place.Powers ??= powers;
        var byId = new Dictionary<string, WorldThing>(StringComparer.Ordinal);
        var context = new Context(place, byId, restore);
        // The party's saved numbers made free first, the leader's body keeping its own (SaveGuids.cs).
        var partyRecords = new Dictionary<string, ThingSave>(StringComparer.Ordinal);
        for (int i = 0; i < save.Members.Count; i++) Records($"{i}", save.Members[i], partyRecords);
        var leaderOwn = new Dictionary<WorldThing, int>(ReferenceEqualityComparer.Instance);
        if (save.Members.Count > 0 && place.Player?.Thing is { } first && save.Members[0].Guid > 0) leaderOwn[first] = save.Members[0].Guid;
        place.MakeWay(partyRecords.Values.Select(r => r.Guid).Where(g => g > 0).ToHashSet(), leaderOwn);
        for (int i = 0; i < save.Members.Count; i++)
        {
            var s = save.Members[i];
            string id = ThingIds.Party + (s.Actor?.CastId ?? s.Template);
            WorldThing? thing;
            if (i == 0 && place.Player?.Thing is { } leader) { thing = leader; GiveBack(place, leader, s.Guid); }
            else
            {
                thing = place.Restore(s.Template, s.Guid, null);
                if (thing is null) { log.LogWarning("load: no template {Template} for a party member", s.Template); continue; }
                thing.Position = s.Position;
                thing.Orientation = s.Orientation;
                thing.Sector = s.Sector;
                thing.Rehome(s.Home ?? s.Position, s.HomeOrientation ?? s.Orientation);
                place.Enter(thing);
                // A member standing in the place whose scene placed it takes that placement back, so
                // the scripts that name it get the member (1.1's saved parameters keep its GUID) and
                // not the stand-in the build gave the placed thing the load took away.
                if (s.Placed is { } placed && IndexOf(place, placed) is int index)
                {
                    thing.Index = index;
                    place.Gone[index] = thing.Guid;
                }
            }
            byId[id] = thing;
        }
        for (int i = 0; i < save.Members.Count; i++)
        {
            var s = save.Members[i];
            string id = ThingIds.Party + (s.Actor?.CastId ?? s.Template);
            if (byId.TryGetValue(id, out var thing)) context.Apply(thing, id, s, inWorld: true);
        }
        context.Link(save.Members.Select(m => (ThingIds.Party + (m.Actor?.CastId ?? m.Template), m)));
        context.FixUp();
        // Then the scene start's last loop: each member's utility item set again
        // on the motion set it has now — a wolf's, if the post-load step made a shape again.
        foreach (var member in byId.Values) Rpg.Utility.Rejoin(member);
        // The party's own scripts — its effects' and its disciplines' — are found through the same
        // restore as the place's (until then only the place's context answered, so a party
        // member's running discipline came back from a load with none of what its script had saved).
        var placeLoose = restore.Loose;
        var placeLooseOf = restore.LooseOf;
        var placeThingOf = restore.ThingOf;
        restore.Loose = obj => context.LooseSave(obj) ?? placeLoose?.Invoke(obj);
        restore.LooseOf = key => context.LooseOf(key) ?? placeLooseOf?.Invoke(key);
        restore.ThingOf = key => context.ThingOf(key) is var guid and not 0 ? guid : placeThingOf?.Invoke(key) ?? 0;
        // Its walk record found again, where it stands (with its keep-the-height argument): a load
        // puts the leader back exactly where it was saved, as 1.1's reader enters it (the reader looks for
        // no ground) — the convent's morning has Christof 14 units up, in his bed, which a drop onto the
        // walk map sank into the mattress (Player.Ground). Until then the load dropped him.
        place.Player?.Ground(snap: false);
        // Who the player steers — the caller hands it to the minds once they are awake.
        return save.Steered is { } steered && byId.TryGetValue(steered, out var who) ? who : place.Player?.Thing;
    }

    // ---- one thing ------------------------------------------------------------------------------

    /// <summary>
    /// The work of putting things back, for one place: the things by id as they are made, and the
    /// script state gathered for the script layer as they go.
    /// </summary>
    sealed class Context(Location place, Dictionary<string, WorldThing> byId, ScriptRestore restore)
    {
        readonly Dictionary<object, ScriptSave> looseSaves = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<string, (WorldThing Owner, Func<object?> Script)> looseByKey = new(StringComparer.Ordinal);
        readonly List<(Effect Effect, string SourceId)> sources = [];
        readonly List<(WorldThing Thing, ThingSave Save)> applied = [];

        public int ThingOf(string key) => byId.TryGetValue(key, out var t) ? t.Guid : 0;
        public object? LooseOf(string key) => looseByKey.TryGetValue(key, out var l) ? l.Script() : null;
        public ScriptSave? LooseSave(object obj)
        {
            foreach (var (key, (_, script)) in looseByKey)
                if (ReferenceEquals(script(), obj) && looseSaves.TryGetValue(key, out var s)) return s;
            return null;
        }

        public void Apply(WorldThing t, string id, ThingSave s, bool inWorld)
        {
            applied.Add((t, s));
            if (inWorld)
            {
                t.Orientation = s.Orientation;
                if (t.InWorld) { if (t.Position != s.Position) place.Move(t, s.Position); }
                else t.Position = s.Position;
                if (!t.InWorld) t.Sector = s.Sector;
                t.Rehome(s.Home ?? s.Position, s.HomeOrientation ?? s.Orientation);
                if (!t.InWorld) place.Enter(t);
                place.Resector(t, s.Sector);
                // A character made again needs a mind, as one made in play gets on arrival.
                if (t.Actor is not null && t.Mind is null) place.Minds?.Adopt(t);
            }
            else if (s.Unfinalized != true)
            {
                t.Rehome(t.Position, t.Orientation);
            }
            // (An item never yet in the world stays unfinalized: its first entry finalizes it and its
            // script hears `created` then, as 1.1's kept 0x10000000 bit has it.)
            t.Flags = s.Flags;
            t.RenderFlags = s.RenderFlags;
            if (s.DescriptionId is not null) t.DescriptionId = s.DescriptionId;
            if (s.Model is not null && !string.Equals(s.Model, t.Model, StringComparison.OrdinalIgnoreCase)) place.SetModel(t, s.Model);
            if (s.Foley is not null) t.Foley = s.Foley;
            t.MoveType = s.MoveType;
            t.PhysicsFlags = s.PhysicsFlags;
            t.Velocity = s.Velocity ?? Vector3.Zero;
            t.RotVelocity = s.Spin ?? Vector3.Zero;
            if (s.ContainerHealth is { } health) t.ContainerHealth = health;
            t.State = s.State;
            t.ExpireAt = s.ExpireAt ?? 0;
            t.Frame = s.Frame;
            t.SpawnedBy = s.SpawnedBy;
            if (s.Keyframes is { } keys) t.Keyframes = [.. keys.Select(k => new NsdKeyframe(k.Orientation, k.Position))];
            if (s.Alpha is { } alpha) t.Model3D.SetAlpha(alpha, 0f);
            if (s.Scale is { } scale) t.Model3D.SetScale(scale, 0f);
            if (s.Item is { } item && t.Item is { } it) ApplyItem(it, item);
            if (s.Actor is { } actor && t.Actor is { } a) ApplyActor(t, a, id, actor);
            ApplyEffects(t, id, s.Effects ?? []);
            if (s.Script is { } script) restore.ByThing[t.Guid] = script;
            foreach (var c in s.Captures ?? []) restore.Captures.Add((t.Guid, c));
            if (s.Pose is { } pose) Poses.Put(place, t, pose);                   // a body lies where it fell
            if (s.Collide is { } collide && collide < 4) t.Collide = (CollideShape)collide;
            if (s.RegionFlags is { } regionFlags && t.Type == ThingType.Region) t.RegionFlags = regionFlags;
            if (s.PropOwner is { } propOwner && t.Type == ThingType.Prop) t.PropOwner = propOwner;
            if (s.Track is { } track)                                             // the mover's 0x90 bytes
            {
                if (track.Angles is { } angles) t.TrackerAngles = angles;
                if (track.Move is { } m)
                    t.ResumeMotion(new ThingMotion(m.Frame, m.FromPosition, m.ToPosition, m.FromOrientation, m.ToOrientation,
                                                   m.Pivot, m.Duration) { Elapsed = m.Elapsed, Rate = m.Rate });
            }
        }

        void ApplyActor(WorldThing t, Actor a, string id, ActorSave s)
        {
            a.Name = s.Name;
            a.Flags = s.Flags;
            a.Flags2 = s.Flags2;
            if (Enum.TryParse<ActorType>(s.Type, true, out var type)) a.Type = type;
            a.Team = s.Team;
            a.OriginalTeam = s.OriginalTeam;
            a.Perception = s.Perception;
            a.Stealth = s.Stealth;
            a.Clan = s.Clan;
            if (place.Files is { } files) a.ClanRecord = Clans.For(files)[a.Clan];
            a.Aura = s.Aura;
            a.AuraName = s.AuraName ?? "";
            // No more than the maximum: a save made earlier may hold a character a script revived at a
            // hundred times its health (`Damage.Revive`), which 1.1's setter never allows.
            a.Health = s.MaxHealth > 0f ? Math.Min(s.Health, s.MaxHealth) : s.Health;
            a.MaxHealth = s.MaxHealth;
            a.Experience = s.Experience;
            a.SpendableExperience = s.Unspent;
            a.Cash = s.Cash;
            a.Xp = s.Xp;
            a.GibClass = s.GibClass;
            a.SkeletonId = s.SkeletonId;
            a.MoveSpeedScale = s.MoveSpeed;
            a.AttackSpeedScale = s.AttackSpeed;
            a.WalkSpeed = s.WalkSpeed;
            a.RunSpeed = s.RunSpeed;
            if (Enum.TryParse<Ai.PartyStance>(s.Stance, true, out var stance)) a.Stance = stance;
            a.HeadModel = s.Head ?? "";
            a.WeaponHidden = s.WeaponHidden;
            a.OffhandHidden = s.OffhandHidden;
            a.NoItemArmor = s.NoItemArmor ?? "";
            a.IsPlayerThing = s.PlayerThing;
            a.DisciplineTab = (byte)s.DisciplineTab;
            a.DisciplineTabOffset = (byte)s.DisciplineTabOffset;
            a.FadeAt = s.FadeAt;
            a.FadingFrom = s.FadingFrom;
            a.Dusted = s.Dusted;
            for (int i = 0; i < a.Stat.Length; i++)
                if (s.Stats.TryGetValue(SaveCapture.StatName(i), out var st))
                {
                    a.Stat[i] = st.Now; a.BaseStat[i] = st.Base; a.StatMin[i] = st.Min; a.StatMax[i] = st.Max;
                }
            for (int i = 0; i < a.Soak.Length; i++)
                if (s.Soaks.TryGetValue(SaveCapture.SoakName(i), out var so)) { a.Soak[i] = so.Now; a.BaseSoak[i] = so.Base; }

            // Nothing it was doing survives a load (1.1 writes no order, no target, no pose).
            a.Action = ActionMode.None;
            a.Target = null;
            a.PendingDiscipline = null;
            a.BusyUntil = 0;

            // What it carries: every item it had on it is let go, and the save's are made again.
            foreach (var old in a.Inventory.Items.ToArray())
            {
                a.Inventory.Remove(old);
                if (old.Thing is { } body && !byId.ContainsValue(body)) place.Remove(body, drop: false);
            }
            a.Held = null;
            a.Weapon = a.DefaultWeapon;
            a.Offhand = null;
            a.Armor = null;
            a.Utility = null;
            var startedWith = new Dictionary<Item, int>();
            foreach (var (itemId, entry) in s.Inventory.Reverse())
            {
                var body = byId.GetValueOrDefault(itemId) ?? place.Restore(entry.Template, entry.Guid, ThingIds.ParseMade(itemId) is not null ? itemId : null);
                if (body?.Item is not { } item) { log.LogWarning("load: {Owner} carried a {Template} that cannot be made", id, entry.Template); continue; }
                startedWith[item] = item.Amount;                    // the template's, before the record's
                byId[itemId] = body;
                Apply(body, itemId, entry, inWorld: false);
                body.Owner = t;
                item.Thing = body;
                if (itemId == s.Held) a.Held = item;
                else a.Inventory.Add(item);
            }
            // Repair `packcells`: no used-up consumable made again, no two items on one cell (LoadedPack).
            foreach (var change in LoadedPack.Mend(a, item => startedWith.GetValueOrDefault(item, 1),
                         item => { if (item.Thing is { } gone) place.Remove(gone, drop: false); }))
                log.LogInformation("load: {Owner}: {Change} ({Repair})", id, change.What, Repairs.PackCells);
            foreach (var item in a.Inventory.Items) Equipment.Restore(t, item);
            // A weapon swap in force: the swapped-in weapon back in the hand, and what it put aside
            // kept aside (the post-load fix-up resolves the two kept GUIDs and sets the hand again).
            if (s.Override is { } o) PostLoad.Override(t, a, o, itemId => byId.GetValueOrDefault(itemId)?.Item);
            // A shape change in force: its name and flags only; the post-load step makes it again.
            a.ShapeChange = ShapeShift.Loaded(s.Shape);

            // The disciplines, exactly as they were; their scripts are made when the place is entered.
            var list = new List<Discipline>();
            foreach (var d in s.Disciplines)
            {
                if (place.Powers?[d.Name] is not { } def) { log.LogWarning("load: {Owner} knew {Name}, which game.ndd does not have", id, d.Name); continue; }
                var made = new Discipline
                {
                    Definition = def, Owner = t, Level = d.Level, QuickSlot = d.QuickSlot, Flags = d.Flags,
                    CastCount = d.CastCount, RecastUntil = d.RecastUntil, Active = d.Active,
                };
                list.Add(made);
                string key = ScriptKeys.OfDiscipline(id, d.Name);
                looseByKey[key] = (t, () => made.Script);
                if (d.Script is { } ds) looseSaves[key] = ds;
            }
            foreach (var old in a.Disciplines.All) if (old.Script is { } os) place.ScriptObjects?.Free(os);
            a.Disciplines.Replace(DisciplineLoadOrder.Rebuild(list, place.Powers, t));   // as 1.1 reads it back
            a.Disciplines.Current = s.Discipline is { } current ? a.Disciplines[current] : null;
        }

        void ApplyEffects(WorldThing t, string id, List<EffectSave> saved)
        {
            foreach (var old in t.Effects.All) if (old.Script is { } os) place.ScriptObjects?.Free(os);
            t.Effects.Clear();
            foreach (var e in saved)
            {
                if (place.Effects?[e.Name] is not { } def) { log.LogWarning("load: {Owner} had {Name}, which effects.ned does not have", id, e.Name); continue; }
                var effect = new Effect
                {
                    Definition = def, Level = e.Level, Value = e.Value, Until = e.Until, NextTick = e.NextTick,
                    Fade = e.Fade, Guid = e.Guid, CreatorGuid = e.Creator, Flags = e.Flags, IntParam = e.IntParam,
                    FloatParam = e.FloatParam,
                };
                t.Effects.Append(effect);
                if (e.Source is { } src) sources.Add((effect, src));
                LoadedEffects.Loaded(place, t, effect, e);              // the repair `loadedeffects`: what its script had made, for Remake
                string key = ScriptKeys.OfEffect(id, e.Guid);
                looseByKey[key] = (t, () => effect.Script);
                if (e.Script is { } es) looseSaves[key] = es;
            }
        }

        /// <summary>The links that need everything made first: attachments, owners, holders, worn gear's effects.</summary>
        public void Link(IEnumerable<(string Id, ThingSave Save)> saves)
        {
            foreach (var (id, s) in saves)
            {
                if (!byId.TryGetValue(id, out var t)) continue;
                if (s.Owner is { } o && byId.GetValueOrDefault(o) is { } owner) t.Owner = owner;
                if (s.Holder is { } h && byId.GetValueOrDefault(h) is { } holder) t.Holder = holder;
                foreach (var a in s.Attachments ?? [])
                {
                    if (!byId.TryGetValue(a.Child, out var child) || a.Relative.Length != 12) continue;
                    if (child.AttachedTo is not null) continue;       // the attach refuses a rider: the torch the re-equip hung on the hand (Rpg.Utility)
                    var r = a.Relative;
                    var m = new Matrix4x4(r[0], r[1], r[2], 0, r[3], r[4], r[5], 0, r[6], r[7], r[8], 0, r[9], r[10], r[11], 1);
                    t.Attachments.Add(new Attachment(child, a.Flags, a.Bone, m));
                    child.AttachedTo = t;
                }
            }
            foreach (var (effect, sourceId) in sources)
                if (byId.GetValueOrDefault(sourceId)?.Item is { } item) SetSource(effect, item);
            sources.Clear();
        }

        public void Link(Dictionary<string, ThingSave> saves) => Link(saves.Select(kv => (kv.Key, kv.Value)));

        /// <summary>1.1's post-load pass over every character put back (<see cref="PostLoad"/>).</summary>
        public void FixUp()
        {
            foreach (var (t, _) in applied)
                if (t.Actor is not null) PostLoad.FixUp(t);
            LoadedEffects.Remake(place);                                // ours, after 1.1's pass: the repair `loadedeffects`
        }
    }

    static void SetSource(Effect effect, Item item) => effect.Source = item;

    static void ApplyItem(Item item, ItemSave s)
    {
        item.Flags = (ItemFlags)s.Flags;
        item.Amount = s.Amount;
        item.Location = s.Location;
        item.Cell = s.Cell is [var x, var y] ? (x, y) : default;
        item.BaseCost = s.BaseCost;
        item.SellFlags = s.SellFlags;
        item.ItemId = s.ItemId;
        if (s.Effects is { } effects)
            item.Effects = [.. effects.Select(e => new ItemEffect(e.Name, e.Level, e.Value, e.TimeMs))];
        if (s.Weapon is { } w && item.Weapon is { } weapon)
        {
            weapon.CurrentAttack = Math.Clamp(w.Attack, 0, weapon.Attacks.Length - 1);
            for (int i = 0; i < weapon.Attacks.Length && i < w.Ammo.Count; i++) weapon.Attacks[i].Ammo = w.Ammo[i];
        }
    }
}
