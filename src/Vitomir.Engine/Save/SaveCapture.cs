// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taking a save: the running game turned into a `SaveGame`. Nothing here changes the game, but for
// naming a thing that had no name yet (`OfPlace`).
//
// What goes in follows 1.1's writers: the chronicle, the purse, the
// quests and the shops; the party, leader first; and every place still remembered, each thing in
// it with its own record. What 1.1 leaves out, this leaves out — a projectile in flight, an
// explosion going off, anything flagged SAVE NEVER, a party member (the party is saved on its own),
// and an equipped item (it is saved inside whoever wears it). A character's AI mode and its current
// order are not saved either, nor its animation unless it is holding one at its end (`Poses`); a
// load starts them afresh, as 1.1's does.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Save;

public static class SaveCapture
{
    static readonly ILogger log = Diag.For(typeof(SaveCapture).FullName!);

    /// <summary>
    /// The whole game as it stands. <paramref name="here"/> is where the party is;
    /// <paramref name="campaign"/> supplies every other place still loaded and every one parked
    /// since a load; <paramref name="describe"/> turns a location's name into the words the load
    /// list shows. The header says what the game is played with (ours): the
    /// switches in force — <paramref name="fixes"/> and <paramref name="qol"/>, the place's own or the
    /// run's when not given — as the feature registry's profile and the names of the rules features on.
    /// </summary>
    public static SaveGame Take(Location here, Session session, Campaign? campaign = null,
                                Func<string, string>? describe = null, DateTime? now = null,
                                Fixes? fixes = null, Qol? qol = null)
    {
        fixes ??= here.Fixes ?? Fixes.Current;
        qol ??= Qol.Current;
        var chronicle = campaign?.Chronicle ?? session.Chronicle;
        var save = new SaveGame();
        string locationName = PlaceName(here, session, campaign);
        long clock = here.Milliseconds;
        save.Header = new SaveHeader
        {
            Build = Ui.FrontEnd.Version,
            Description = Describe(describe?.Invoke(locationName) ?? locationName, clock),
            Saved = (now ?? DateTime.Now).ToString("yyyy-MM-ddTHH:mm:ss"),
            ClockMs = clock,
            Chronicle = chronicle?.Name ?? "",
            Location = locationName,
            Profile = Features.GameFeatures.Registry.Profile(fixes, qol),
            Rules = [.. Features.GameFeatures.Registry.RulesOn(fixes, qol)],
        };
        if (chronicle is not null) save.Chronicle = OfChronicle(chronicle, here.ScriptObjects as IScriptPersistence);

        // The party first, because the places are written around it: a party member is in no place.
        var party = PartyOf(here);
        var ids = new Dictionary<WorldThing, string>(ReferenceEqualityComparer.Instance);
        foreach (var member in party) ids[member] = ThingIds.Member(member);

        save.Session = OfSession(session, here, ids);

        // Every place: the one the party is in, the others still loaded, and the ones parked since a load.
        var places = new List<(string Name, Location Place)>();
        if (chronicle is not null && campaign is not null && campaign.Files is null)   // a host's are its files (below)
        {
            foreach (var (index, place) in campaign.Resident)
                if (!ReferenceEquals(place, here) && !campaign.Unentered(index)) places.Add((chronicle.NameOf(index), place));   // no file yet (CampaignPreloads.cs)
            foreach (var (index, parked) in campaign.Parked)
                save.World[chronicle.NameOf(index)] = parked;
        }
        places.Insert(0, (locationName, here));
        PlaceScripts? liveHere = null;
        foreach (var (name, place) in places)
        {
            bool isHere = ReferenceEquals(place, here);
            save.World[name] = AwayTimers.Taken(OfPlace(place, isHere ? ids : null, out var live), place, here);   // on the game's clock
            if (isHere) liveHere = live;
        }
        campaign?.CopyFiles(save.World, locationName);                          // a host's: every other place as filed (CampaignFiles.cs)

        // And now the party, whose scripts and effects the current place's script layer holds — its
        // members' own scripts' words and what has captured them come out of the same pass as the
        // place's (liveHere), and its effects' and disciplines' timers went in with the place's
        // (1.1's timer section is the whole game's). Until then the party's were
        // left out: a Majesty saved running never ticked again, and Pink forgot she had fed.
        var scripts = here.ScriptObjects as IScriptPersistence;
        var looseKeys = LooseKeys(party, ids);
        foreach (var member in party)
        {
            var record = OfThing(here, member, ids, scripts, liveHere, looseKeys);
            record.Placed = ThingIds.Placed(here, member);                         // its scene's name for it, if this is its scene's place
            save.Party.Members.Add(record);
        }
        if (here.Steered is { } steered && ids.TryGetValue(steered, out var steeredId)) save.Party.Steered = steeredId;
        return save;
    }

    /// <summary>1.1's description: the place and the game's clock, <c>"%s (%2.2d:%2.2d:%2.2d)"</c>.</summary>
    /// <summary>The chronicle's name for where the party is (`Convent`), or the level's when there is no chronicle.</summary>
    public static string PlaceName(Location here, Session session, Campaign? campaign = null)
    {
        var chronicle = campaign?.Chronicle ?? session.Chronicle;
        return chronicle?.NameOf(chronicle.Current) is { } n && n != Chronicle.InvalidLocation ? n : here.Name;
    }

    /// <summary>
    /// The automatic description as 1.1 fills the name box with it: the location's name
    /// through the string table — `Convent` is "The Convent" — and the time played.
    /// </summary>
    public static string DescribeNow(Location here, Session session, Campaign? campaign, Func<string, string>? describe) =>
        PlaceName(here, session, campaign) is var name ? Describe(describe?.Invoke(name) ?? name, here.Milliseconds) : "";

    public static string Describe(string place, long clockMs)
    {
        long s = clockMs / 1000;
        return $"{place} ({s / 3600:00}:{s / 60 % 60:00}:{s % 60:00})";
    }

    /// <summary>The party as 1.1's party section lists it: the leader, then every other member here.</summary>
    public static List<WorldThing> PartyOf(Location here)
    {
        var list = new List<WorldThing>();
        if (here.Player?.Thing is { } leader && leader.Actor is not null) list.Add(leader);
        foreach (var t in here.Things)
            if (t.Actor is { } a && a.Has(Actor.Flag.Party) && !list.Contains(t)) list.Add(t);
        return list;
    }

    // ---- the chronicle -------------------------------------------------------------------------

    static ChronicleSave OfChronicle(Chronicle c, IScriptPersistence? scripts)
    {
        var save = new ChronicleSave { Name = c.Name, Current = c.NameOf(c.Current) };
        save.Flags.AddRange(c.SetFlags());
        if (c.Portal.Location >= 0) save.Portal = new PlaceRef { Location = c.NameOf(c.Portal.Location), Entrance = c.Portal.Entrance };
        if (c.Revive.Location >= 0) save.Revive = new PlaceRef { Location = c.NameOf(c.Revive.Location), Entrance = c.Revive.Entrance };
        if (c.AdvanceTo.Location >= 0 || c.AdvanceTo.Scene.Length > 0)
            save.Advance = new AdvanceSave
            {
                Location = c.AdvanceTo.Location >= 0 ? c.NameOf(c.AdvanceTo.Location) : null,
                Scene = c.AdvanceTo.Scene, Entrance = c.AdvanceTo.Entrance,
            };
        for (int i = 0; i < c.Count; i++)
        {
            if (c.At(i) is not { } loc) continue;
            var entry = new ChronicleLocationSave { Scene = loc.Scene, Flags = loc.Flags };
            for (int e = 0; e < loc.ExitCount; e++)
            {
                var x = loc.Exits[e];
                entry.Exits.Add(new ExitSave { To = x.To >= 0 ? c.NameOf(x.To) : null, Entrance = x.Entrance, Closed = x.Closed });
            }
            save.Locations[loc.Name] = entry;
        }
        if (scripts is not null)
        {
            save.Scripts = scripts.SaveChronicle();
            if (scripts.WeatherEffect.Length > 0) save.Weather = scripts.WeatherEffect;
        }
        return save;
    }

    // ---- what belongs to no place ----------------------------------------------------------------

    static SessionSave OfSession(Session session, Location here, Dictionary<WorldThing, string> party)
    {
        var save = new SessionSave
        {
            Gold = session.Gold,
            TreasureCounter = here.Treasure?.Counter ?? 1,
            EffectCounter = EffectRuntime.NextGuid,
            ThingCounter = here.Guids.Next,                              // the game's one counter
            ReservedGuids = here.Guids.Reserved.Count > 0 ? new(here.Guids.Reserved, StringComparer.OrdinalIgnoreCase) : null,
            Teleports = TeleportSaves.Take(session.Teleports, session.Chronicle),
        };
        foreach (var q in session.Quests.All)
            save.Quests.Add(new QuestSave
            {
                Slot = q.Slot, Name = q.Name, Flags = q.Flags, Xp = q.Xp, ShortKey = q.ShortKey,
                LongKey = q.LongKey, Icon = q.Icon,
                Items = [.. q.Items.Select(i => new QuestItemSave
                {
                    Flags = i.Flags, Steps = i.Steps, Step = i.Step, ShortKey = i.ShortKey,
                    LongKey = i.LongKey, Icon = i.Icon, Xp = i.Xp,
                })],
            });
        foreach (var store in session.Shops.All)
        {
            var owner = here.Thing(store.OwnerGuid);
            var s = new StoreSave
            {
                Owner = owner is null ? null : party.GetValueOrDefault(owner) ?? ThingIds.Of(here, owner),
                OwnerGuid = store.OwnerGuid, DescriptionId = store.DescriptionId, Flags = store.Flags,
                BuyRate = store.BuyRate, SellRate = store.SellRate, SellMask = store.SellMask,
            };
            foreach (var line in store.Lines)
                s.Lines.Add(new StockLineSave
                {
                    Remaining = line.Remaining, Copies = line.Copies,
                    Template = line.Direct ? line.Template : null, Class = line.Direct ? 0 : line.Class,
                    Stock = line.Treasure is { } t
                        ? new TreasureSave { Id = t.Id, Template = t.Template, Item = OfItem(t.Item, t.Template, here.Templates) }
                        : null,
                });
            save.Stores.Add(s);
        }
        return save;
    }

    // ---- a place ------------------------------------------------------------------------------

    /// <summary>
    /// One place. <paramref name="party"/> names the party members standing in it, which only the
    /// place being played has; they are left out, because the party is saved on its own.
    /// </summary>
    public static LocationSave OfPlace(Location place, Dictionary<WorldThing, string>? party = null) =>
        OfPlace(place, party, out _);

    /// <inheritdoc cref="OfPlace(Location, Dictionary{WorldThing, string}?)"/>
    /// <param name="live">What the place's scripts saved — which the party's own records are taken from.</param>
    public static LocationSave OfPlace(Location place, Dictionary<WorldThing, string>? party, out PlaceScripts? live)
    {
        party ??= new Dictionary<WorldThing, string>(ReferenceEqualityComparer.Instance);
        var save = new LocationSave
        {
            Level = place.Name,
            Scene = place.ChapterScene is not null ? place.SceneFile : "",
            Entrance = place.Entrance,
            NextGuid = place.NextGuid,
            Made = place.Made,
        };

        var kept = SaveOrder.Filed(place, place.Things.Where(t => Kept(place, t, party)));   // the list's order is ListOrder
        var ids = new Dictionary<WorldThing, string>(party, ReferenceEqualityComparer.Instance);
        foreach (var t in kept)
        {
            // **Nothing the save keeps goes unnamed.** A thing no scene placed and nothing named when it
            // was made — an item a load put back into a pack under a placed or a pack id, then dropped;
            // a companion a load made, who has since left the party — was skipped here until then,
            // and so lost at the next load. It is named now as a thing made here (ours).
            if (ThingIds.Of(place, t) is { } id) ids[t] = id;
            else
            {
                t.SaveId = ThingIds.Made(place, ++place.Made);
                ids[t] = t.SaveId;
                log.LogInformation("save: {Place} {Template} had no name; filed as {Id}", place.Name, t.Template, t.SaveId);
            }
        }
        save.Made = place.Made;

        var scripts = place.ScriptObjects as IScriptPersistence;
        var looseKeys = LooseKeys(kept.Concat(party.Keys), ids);                   // the party's effects' timers are filed with the place's
        live = null;
        if (scripts is not null)
        {
            live = scripts.Save(place,
                guid => place.Thing(guid) is { } t ? IdOf(place, t, ids) : null,
                obj => looseKeys.GetValueOrDefault(obj));
            save.ScriptClockMs = live.ClockMs;
            save.Timers.AddRange(live.Timers);
        }

        foreach (var t in kept)
        {
            if (!ids.TryGetValue(t, out var id)) continue;
            save.Things[id] = OfThing(place, t, ids, scripts, live, looseKeys);
        }
        save.ListOrder = SaveOrder.Of(place, ids, save);                // the list's order, head first

        // The placed things that are neither in the world nor carried by anyone here.
        var carried = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in save.Things.Values) Collect(t, carried);
        foreach (var id in PlacedIds(place))
            if (!save.Things.ContainsKey(id) && !carried.Contains(id)) save.Removed.Add(id);
        return save;
    }

    /// <summary>Every id the place's scenes placed, in order — what a fresh build of it would contain.</summary>
    public static IEnumerable<string> PlacedIds(Location place)
    {
        if (place.WorldScene is { } world)
            for (int i = 0; i < world.Things.Length; i++) yield return $"{place.Name}#{i}";
        if (place.ChapterScene is { } chapter)
            for (int i = 0; i < chapter.Things.Length; i++) yield return $"{place.SceneFile}#{i}";
    }

    static void Collect(ThingSave t, HashSet<string> into)
    {
        if (t.Actor is null) return;
        foreach (var (id, item) in t.Actor.Inventory) { into.Add(id); Collect(item, into); }
        if (t.Actor.Held is { } held) into.Add(held);
    }

    /// <summary>1.1's filter, in this tree's terms.</summary>
    static bool Kept(Location place, WorldThing t, Dictionary<WorldThing, string> party)
    {
        if (party.ContainsKey(t)) return false;
        if (ReferenceEquals(t, place.Player?.Thing)) return false;
        if ((t.Flags & WorldThing.Flag.SaveNever) != 0) return false;
        // THING_FLAG_SAVE_SUSPEND keeps a thing out of a network game's save only.
        if ((t.Flags & WorldThing.Flag.SaveSuspend) != 0 && Net.NetOrders.Mode(place) != Net.NetMode.Single) return false;
        if (t.Type is ThingType.Projectile or ThingType.Explosion) return false;
        // The torch on a hand is in the world and in its holder's pack; it is written with the pack
        // and put back on the hand by the load (Rpg.Utility). Ours: 1.1's record of it is unread.
        if (t.Item is { } carried && ReferenceEquals(t.Owner?.Actor?.Utility, carried)) return false;
        if (t.Actor is { } a && t.Type == ThingType.Player && a.Has(Actor.Flag.Party)) return false;
        if (Net.NetSaves.LeftOut(place, t)) return false;                     // a network game's players
        return true;
    }

    static string? IdOf(Location place, WorldThing t, Dictionary<WorldThing, string> ids) =>
        ids.TryGetValue(t, out var id) ? id : ThingIds.Of(place, t);

    /// <summary>Every engine-made script object the given characters hold, and the key a save names it by.</summary>
    static Dictionary<object, string> LooseKeys(IEnumerable<WorldThing> things, Dictionary<WorldThing, string> ids)
    {
        var keys = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        foreach (var t in things)
        {
            if (!ids.TryGetValue(t, out var owner)) continue;
            foreach (var e in t.Effects.All)
                if (e.Script is { } s) keys[s] = ScriptKeys.OfEffect(owner, e.Guid);
            if (t.Actor is { } a)
                foreach (var d in a.Disciplines.All)
                    if (d.Script is { } s) keys[s] = ScriptKeys.OfDiscipline(owner, d.Name);
        }
        return keys;
    }

    // ---- a thing ------------------------------------------------------------------------------

    static ThingSave OfThing(Location place, WorldThing t, Dictionary<WorldThing, string> ids, IScriptPersistence? scripts,
                           PlaceScripts? live, Dictionary<object, string> looseKeys)
    {
        var s = new ThingSave
        {
            Template = t.Template,
            Guid = t.Guid,
            Position = t.Position,
            Orientation = t.Orientation,
            Sector = t.Sector,
            Flags = t.Flags,
            RenderFlags = t.RenderFlags,
            DescriptionId = t.DescriptionId,
            Model = t.Model,
            Foley = t.Foley.Length > 0 ? t.Foley : null,
            MoveType = t.MoveType,
            PhysicsFlags = t.PhysicsFlags,
            Velocity = t.Velocity != Vector3.Zero ? t.Velocity : null,
            Spin = t.RotVelocity != Vector3.Zero ? t.RotVelocity : null,
            ContainerHealth = t.Type == ThingType.Container ? t.ContainerHealth : null,
            State = t.State,
            ExpireAt = t.ExpireAt != 0 ? t.ExpireAt : null,
            Frame = t.Frame,
            SpawnedBy = t.SpawnedBy,
            Pose = Poses.Of(t),
        };
        if (t.Finalized && (t.SpawnPosition != t.Position || t.SpawnOrientation != t.Orientation))
        {
            s.Home = t.SpawnPosition;
            s.HomeOrientation = t.SpawnOrientation;
        }
        if (!ReferenceEquals(t.Keyframes, t.Scene.Keyframes) && !t.Keyframes.SequenceEqual(t.Scene.Keyframes))
            s.Keyframes = [.. t.Keyframes.Select(k => new KeyframeSave { Position = k.Position, Orientation = k.Orientation })];
        if (t.Fx is { } fx)
        {
            // A fade or a grow under way is kept where it was heading (ours: 1.1's record keeps no model
            // alpha at all; saving it half-way left a thing half-seen for good).
            if (fx.AlphaHeading != 1f) s.Alpha = fx.AlphaHeading;
            if (fx.ScaleHeading != 1f) s.Scale = fx.ScaleHeading;
        }
        s.Collide = (uint)t.Collide;
        if (t.Type == ThingType.Region) s.RegionFlags = t.RegionFlags;                    // tag 0x30 (a template may give some)
        if (t.Type == ThingType.Prop && t.PropOwner != 0) s.PropOwner = t.PropOwner;           // tag 0x2f
        if (!t.Finalized) s.Unfinalized = true;                                        // thing flag 0x10000000
        if (t.MoveType == WorldThing.MoveTrack && (t.TrackerAnglesSet is not null || t.Motion is not null))
            s.Track = new TrackSave                                                     // the mover's 0x90 bytes
            {
                Angles = t.TrackerAnglesSet,
                Move = t.Motion is { } m ? new MoveSave
                {
                    Frame = m.Frame, FromPosition = m.FromPosition, ToPosition = m.ToPosition,
                    FromOrientation = m.FromOrientation, ToOrientation = m.ToOrientation, Pivot = m.Pivot,
                    Duration = m.Duration, Elapsed = m.Elapsed, Rate = m.Rate,
                } : null,
            };
        if (t.Owner is { } owner && IdOf(place, owner, ids) is { } ownerId) s.Owner = ownerId;
        if (t.Holder is { } holder && IdOf(place, holder, ids) is { } holderId) s.Holder = holderId;
        if (t.Attachments.Any(a => (a.Child.Flags & WorldThing.Flag.SaveNever) == 0))    // a rider that is never saved is not named (a blade's trail)
            s.Attachments = [.. t.Attachments.Where(a => IdOf(place, a.Child, ids) is not null
                                                     && (a.Child.Flags & WorldThing.Flag.SaveNever) == 0).Select(a => new AttachmentSave
            {
                Child = IdOf(place, a.Child, ids)!, Flags = a.Flags, Bone = a.Bone, Relative = Rows(a.Relative),
            })];
        if (t.Item is { } item) s.Item = OfItem(item, t.Template, place.Templates);
        if (t.Actor is { } actor) s.Actor = OfActor(place, t, actor, ids, scripts, live, looseKeys);
        Rpg.ShapeShift.Capture(t, s);
        var effects = OfEffects(place, t, ids, scripts);
        if (effects.Count > 0) s.Effects = effects;
        if (live?.ByThing.GetValueOrDefault(t.Guid) is { } script) s.Script = script;
        var captures = live?.Captures.Where(c => c.ThingGuid == t.Guid).Select(c => c.Capture).ToList();
        if (captures is { Count: > 0 }) s.Captures = captures;
        return s;
    }

    static float[] Rows(Matrix4x4 m) =>
        [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33, m.M41, m.M42, m.M43];

    static List<EffectSave> OfEffects(Location place, WorldThing t, Dictionary<WorldThing, string> ids, IScriptPersistence? scripts)
    {
        var list = new List<EffectSave>();
        foreach (var e in t.Effects.All)
        {
            if (e.Spent) continue;
            var saved = new EffectSave
            {
                Name = e.Definition.Name, Level = e.Level, Value = e.Value, Until = e.Until, NextTick = e.NextTick,
                Fade = e.Fade, Guid = e.Guid, Creator = e.CreatorGuid, Flags = e.Flags, IntParam = e.IntParam,
                FloatParam = e.FloatParam,
                Source = e.Source?.Thing is { } src ? IdOf(place, src, ids) : null,
                Script = e.Script is { } obj ? scripts?.SaveObject(obj) : null,
            };
            LoadedEffects.Capture(place, e, saved);                    // ours (the repair `loadedeffects`): what its script made that no save keeps
            list.Add(saved);
        }
        return list;
    }

    /// <summary>What a template's item is before anything has happened to it — what an item is compared with.</summary>
    static Item? Fresh(TemplateSet? templates, string template)
    {
        if (templates is null || template.Length == 0 || !templates.Has(template)) return null;
        return Item.From(template, templates.Resolve(template));
    }

    static ItemSave OfItem(Item item, string template, TemplateSet? templates)
    {
        var s = new ItemSave
        {
            Flags = (uint)item.Flags, Amount = item.Amount, Location = item.Location,
            Cell = item.Cell != default ? [item.Cell.X, item.Cell.Y] : null,
            BaseCost = item.BaseCost, SellFlags = item.SellFlags, ItemId = item.ItemId,
        };
        var fresh = Fresh(templates, template);
        if (fresh is null || !fresh.Effects.SequenceEqual(item.Effects))
            s.Effects = [.. item.Effects.Select(e => new ItemEffectSave { Name = e.Name, Level = e.Level, Value = e.Value, TimeMs = e.TimeMs })];
        // Tag 0x2d: how many attacks carry ammo, each one's rounds loaded, then the
        // current attack.
        if (item.Weapon is { } w)
            s.Weapon = new WeaponSave { Attack = w.CurrentAttack, Ammo = [.. w.Attacks.Select(a => a.Ammo)] };
        return s;
    }

    static ActorSave OfActor(Location place, WorldThing t, Actor a, Dictionary<WorldThing, string> ids, IScriptPersistence? scripts,
                           PlaceScripts? live, Dictionary<object, string> looseKeys)
    {
        var s = new ActorSave
        {
            Name = a.Name, CastId = a.CastId.Length > 0 ? a.CastId : null, Flags = a.Flags, Flags2 = a.Flags2,
            Type = a.Type.ToString(), Team = a.Team, OriginalTeam = a.OriginalTeam, Perception = a.Perception,
            Stealth = a.Stealth, Clan = a.Clan, Aura = a.Aura, AuraName = a.AuraName.Length > 0 ? a.AuraName : null,
            Health = a.Health, MaxHealth = a.MaxHealth, Experience = a.Experience, Unspent = a.SpendableExperience,
            Cash = a.Cash, Xp = a.Xp, GibClass = a.GibClass, SkeletonId = a.SkeletonId,
            MoveSpeed = a.MoveSpeedScale, AttackSpeed = a.AttackSpeedScale, WalkSpeed = a.WalkSpeed, RunSpeed = a.RunSpeed,
            Stance = a.Stance.ToString(), Head = a.HeadModel.Length > 0 ? a.HeadModel : null, WeaponHidden = a.WeaponHidden, OffhandHidden = a.OffhandHidden,
            NoItemArmor = a.NoItemArmor.Length > 0 ? a.NoItemArmor : null, PlayerThing = a.IsPlayerThing,
            DisciplineTab = a.DisciplineTab, DisciplineTabOffset = a.DisciplineTabOffset,
            FadeAt = a.FadeAt, FadingFrom = a.FadingFrom, Dusted = a.Dusted,
        };
        for (int i = 0; i < a.Stat.Length; i++)
            s.Stats[StatName(i)] = new StatSave { Now = a.Stat[i], Base = a.BaseStat[i], Min = a.StatMin[i], Max = a.StatMax[i] };
        for (int i = 0; i < a.Soak.Length; i++)
            s.Soaks[SoakName(i)] = new SoakSave { Now = a.Soak[i], Base = a.BaseSoak[i] };

        string ownerId = IdOf(place, t, ids) ?? t.Template;
        int n = 0;
        foreach (var item in a.Inventory.Items.Concat(a.Held is { } h && !a.Inventory.Items.Contains(h) ? [h] : []))
        {
            // A destroyed item is carried by nobody (its destruction unlinks it); one still listed is not written.
            if (item.Destroyed) continue;
            string id = item.Thing is { } body && IdOf(place, body, ids) is { } bodyId ? bodyId : $"{ownerId}/item:{n}";
            n++;
            // An item with no id of its own is named here, and the name has to be known to whatever
            // else points at it — a worn item's effects name it as their source. Earlier, it was
            // not, and those effects were written with no source.
            if (item.Thing is { } named && !ids.ContainsKey(named) && IdOf(place, named, ids) is null) ids[named] = id;
            var entry = item.Thing is { } thing
                ? OfThing(place, thing, ids, scripts, live, looseKeys)
                : new ThingSave { Template = item.Name, Item = OfItem(item, item.Name, place.Templates) };
            // The world half of a carried item means nothing while it is carried.
            entry.Position = default; entry.Orientation = default; entry.Sector = 0; entry.Home = null; entry.HomeOrientation = null;
            entry.Owner = null; entry.Holder = null;
            s.Inventory[id] = entry;
            if (ReferenceEquals(item, a.Held)) s.Held = id;
            if (Rpg.WeaponOverride.Active(a))
            {
                s.Override ??= new OverrideSave { Weapon = a.Weapon?.Name ?? "" };
                if (item.Weapon is { } w && ReferenceEquals(w, a.PrevWeapon)) s.Override.PrevWeapon = id;
                if (ReferenceEquals(item, a.PrevOffhand)) s.Override.PrevOffhand = id;
            }
        }
        if (Rpg.WeaponOverride.Active(a)) s.Override ??= new OverrideSave { Weapon = a.Weapon?.Name ?? "" };
        foreach (var d in a.Disciplines.All)
            s.Disciplines.Add(new DisciplineSave
            {
                Name = d.Name, Level = d.Level, QuickSlot = d.QuickSlot, Flags = d.Flags, CastCount = d.CastCount,
                RecastUntil = d.RecastUntil, Active = d.Active,
                Script = d.Script is { } obj ? scripts?.SaveObject(obj) : null,
            });
        s.Discipline = a.Disciplines.Current?.Name;
        return s;
    }

    public static string StatName(int i) => i < Actor.Stats ? ((ActorStat)i).ToString().ToLowerInvariant() : $"stat{i}";
    public static string SoakName(int i) => i < Actor.Soaks ? ((DamageType)i).ToString().ToLowerInvariant() : $"soak{i}";
}
