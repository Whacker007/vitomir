// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A place brought over from 1.1: `location_<n>.nsw` put onto the place as Vitomir builds it.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Save;

/// <summary>
/// **One place as 1.1 left it**, from its `.nsw` (the place's own save, written when the party
/// leaves and read back when it returns). 1.1 does not build a returning place from its scene at all: its reader makes every
/// thing from its record and a thing the file does not have simply is not
/// there. Vitomir's place is built from its level and scenes and then *told* what the save says
/// (<see cref="SaveApply.Place"/>), so this does the same with 1.1's records: the place is built
/// fresh, every record is laid over the thing it names, the placed things the file no longer has
/// are taken away, the things made in play are made again, and <see cref="SaveCapture.OfPlace"/>
/// writes the result as a Vitomir place — the scripts' own words, their timers and their captures
/// laid in beside it (<see cref="Scripts"/>).
///
/// **Which thing a record is.** A thing record carries its scene and its index in that scene's own
/// thing table (the writer saves the scene as −2 for the world's own scene or the scene's id, and
/// then the index; the reader puts the thing back into that slot of the scene's table,
/// asserting `pThing->sceneIndex &lt; pScene->...`). That is exactly the pair a Vitomir id names
/// (<see cref="ThingIds"/>), so `P1_MON1#47` is the record with scene −2 and index 47, and the
/// chapter's own `MON1_8_2#17` is scene 0, index 17. A record with no scene (−1) was made in play.
/// Checked against every `.nsw` in two 1.1 saves: all 2,105 placed records land on a
/// Vitomir thing made from the same template.
/// </summary>
public static class NswImport
{
    /// <summary>What a place's scripts had, keyed the way a Vitomir save keys them.</summary>
    public sealed class Scripts
    {
        /// <summary>Each thing's script state, by thing id.</summary>
        public Dictionary<string, ScriptSave> ByThing { get; } = new(StringComparer.Ordinal);
        /// <summary>Each character's effect scripts' state, by the effect's script key.</summary>
        public Dictionary<string, ScriptSave> ByEffect { get; } = new(StringComparer.Ordinal);
        public List<TimerSave> Timers { get; } = [];
        /// <summary>Every capture, by the id of the thing captured.</summary>
        public List<(string ThingId, CaptureSave Capture)> Captures { get; } = [];
        /// <summary>The animations things were holding at their end — a corpse's fall — by thing id.</summary>
        public Dictionary<string, PoseSave> Poses { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Lays <paramref name="nsw"/> over <paramref name="place"/> — built fresh from the level and the
    /// scene the story has for it, **minds not woken** — and answers what its scripts had.
    /// <paramref name="clockMs"/> is 1.1's clock when the save was made, which the
    /// timers and the expiries in the file are times on. The place's player, when it has one, is
    /// the party's and is left alone; the party is not in a `.nsw` (the writer keeps it out).
    /// </summary>
    public static Scripts Apply(Location place, NswWorld nsw, long clockMs, EffectDefinitions effects,
                                DisciplineDefinitions powers, List<string> notes,
                                IReadOnlyDictionary<uint, string>? partyScripts = null)
    {
        place.Effects ??= effects;
        place.Powers ??= powers;
        var built = new Dictionary<string, WorldThing>(StringComparer.Ordinal);
        foreach (var t in place.Things)
            if (ThingIds.Placed(place, t) is { } id && !ReferenceEquals(t, place.Player?.Thing)) built[id] = t;

        var context = new Context(place, built, effects, powers, notes);
        // The party's own running scripts, which the file's timers may name too (the list is the
        // whole game's).
        foreach (var (guid, key) in partyScripts ?? new Dictionary<uint, string>()) context.ScriptKeys[guid] = key;
        // Every record first finds or makes its thing, so the links between them can be made after.
        var records = new List<(NsgThing Record, WorldThing Thing, string Id)>();
        foreach (var r in nsw.Things)
            if (context.Resolve(r, inWorld: true) is var (thing, id)) records.Add((r, thing, id));

        // The placed things the file does not have are gone — killed and taken away, picked up, broken.
        foreach (var (id, thing) in built)
            if (!context.Used.Contains(thing)) place.Remove(thing, drop: false);

        foreach (var (r, thing, id) in records) context.Lay(r, thing, id, inWorld: true);
        context.Link();
        // The file is the list head first: the place is left in that order, as it stood at
        // 1.1's save, and the Vitomir save it becomes turns it round on its load as 1.1's does (SaveOrder).
        place.Relink(Enumerable.Reverse(records).Select(r => r.Thing));

        // The scripts' timers (tag 0x19) and captures (tag 0x1a), named by what the scripts belong to.
        foreach (var timer in nsw.Timers)
        {
            if (!context.ScriptKeys.TryGetValue(timer.CodexGuid, out var key)) continue;   // Only a script that exists
            context.Found.Timers.Add(new TimerSave
            {
                Script = key, Id = (int)timer.Id, InMs = (int)Math.Max(0, (long)timer.Time - clockMs),
                Args = [.. timer.Args.Select(BitConverter.UInt32BitsToSingle)],
            });
        }
        foreach (var capture in nsw.Captures)
            if (context.IdOfGuid.TryGetValue(capture.ThingGuid, out var thingId)
                && context.ScriptKeys.TryGetValue(capture.CodexGuid, out var key))   // Both must exist
                context.Found.Captures.Add((thingId, new CaptureSave { Script = key, Id = (int)capture.Id }));
        return context.Found;
    }

    /// <summary>Puts what <see cref="Apply"/> found of the scripts into the place's save, once it is written.</summary>
    public static void Into(LocationSave save, Scripts scripts, long clockMs)
    {
        save.ScriptClockMs = clockMs;
        save.AsOfMs = clockMs;
        foreach (var (id, script) in scripts.ByThing)
            if (save.Things.TryGetValue(id, out var t)) t.Script = script;
        foreach (var (key, script) in scripts.ByEffect)
        {
            // An effect's key is `<owner>/effect:<guid>`: find the owner, then the effect.
            int slash = key.LastIndexOf("/effect:", StringComparison.Ordinal);
            if (slash < 0 || !int.TryParse(key.AsSpan(slash + 8), out int guid)) continue;
            if (Find(save, key[..slash]) is { Effects: { } list } && list.FirstOrDefault(e => e.Guid == guid) is { } e) e.Script = script;
        }
        save.Timers.AddRange(scripts.Timers);
        foreach (var (id, pose) in scripts.Poses)
            if (Find(save, id) is { } t) t.Pose = pose;
        foreach (var (id, capture) in scripts.Captures)
            if (Find(save, id) is { } t) (t.Captures ??= []).Add(capture);
    }

    static ThingSave? Find(LocationSave save, string id)
    {
        if (save.Things.TryGetValue(id, out var t)) return t;
        foreach (var holder in save.Things.Values)
            if (holder.Actor?.Inventory.GetValueOrDefault(id) is { } carried) return carried;
        return null;
    }

    /// <summary>
    /// Where a thing stands and which way it faces, out of a 3 × 4 transform (the thing's, and its
    /// spawn copy): three rows of rotation, then the position.
    /// </summary>
    public static (Vector3 Position, Vector3 Orientation) Placement(byte[] m)
    {
        if (m.Length < 48) return (Vector3.Zero, Vector3.Zero);
        float F(int i) => BitConverter.ToSingle(m, i * 4);
        var rotation = new Matrix4x4(F(0), F(1), F(2), 0, F(3), F(4), F(5), 0, F(6), F(7), F(8), 0, 0, 0, 0, 1);
        return (new Vector3(F(9), F(10), F(11)), WorldThing.OrientationOf(rotation));
    }

    /// <summary>Whether two orientations turn a thing the same way — so a placed thing that has not
    /// turned keeps the scene's own angles rather than the same turn written differently.</summary>
    static bool SameTurn(Vector3 a, Vector3 b)
    {
        static Matrix4x4 R(Vector3 d)
        {
            var o = d * (MathF.PI / 180f);
            return Matrix4x4.CreateRotationY(o.Y) * Matrix4x4.CreateRotationX(o.X) * Matrix4x4.CreateRotationZ(o.Z);
        }
        var x = R(a); var y = R(b);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                if (MathF.Abs(x[i, j] - y[i, j]) > 1e-3f) return false;
        return true;
    }

    sealed class Context(Location place, Dictionary<string, WorldThing> built, EffectDefinitions effects,
                         DisciplineDefinitions powers, List<string> notes)
    {
        public Scripts Found { get; } = new();
        /// <summary>The things a record has claimed.</summary>
        public HashSet<WorldThing> Used { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>1.1's GUID → the thing's id here, for the timers, the captures and the links.</summary>
        public Dictionary<uint, string> IdOfGuid { get; } = [];
        /// <summary>1.1's codex GUID (a script's own) → the Vitomir script key.</summary>
        public Dictionary<uint, string> ScriptKeys { get; } = [];
        readonly Dictionary<uint, WorldThing> byGuid = [];
        readonly List<(WorldThing Thing, NsgLinks Links)> links = [];

        /// <summary>The thing a record is: the placed thing its scene and index name, or one made again.</summary>
        public (WorldThing, string)? Resolve(NsgThing r, bool inWorld)
        {
            string? placedId = r.Scene == NswWorld.OwnScene ? $"{place.Name}#{(int)r.Field28}"
                : r.Scene >= 0 && place.ChapterScene is not null ? $"{place.SceneFile}#{(int)r.Field28}"
                : null;
            if (placedId is not null && built.TryGetValue(placedId, out var t) && !Used.Contains(t)
                && string.Equals(t.Template, r.Name, StringComparison.OrdinalIgnoreCase))
            {
                Used.Add(t);
                if (!inWorld) place.Withdraw(t);
                return Claim(r, t, placedId);
            }
            if (placedId is not null) notes.Add($"{place.Name}: {placedId} is a {r.Name} in 1.1's file; made again");
            string id = ThingIds.Made(place, ++place.Made);
            if (place.Restore(r.Name, (int)r.Guid, id) is not { } made)
            {
                notes.Add($"{place.Name}: a {r.Name} that cannot be made from its template; left out");
                return null;
            }
            return Claim(r, made, id);
        }

        (WorldThing, string) Claim(NsgThing r, WorldThing t, string id)
        {
            // The thing takes 1.1's own GUID, which a script's saved state and the party may name.
            if (r.Guid > 0 && t.Guid != (int)r.Guid && !place.Rekey(t, (int)r.Guid))
                notes.Add($"{place.Name}: {id} cannot have 1.1's GUID {r.Guid}, which another thing holds");
            IdOfGuid[r.Guid] = id;
            byGuid[r.Guid] = t;
            if (r.Script is { } s) ScriptKeys[s.Guid] = Save.ScriptKeys.OfThing(id);
            return (t, id);
        }

        /// <summary>A record laid over its thing: the thing record's fields and then its type's.</summary>
        public void Lay(NsgThing r, WorldThing t, string id, bool inWorld)
        {
            var (position, orientation) = Placement(r.Transform);
            var (home, homeTurn) = Placement(r.Transform2);
            if (inWorld && t.InWorld)
            {
                // A placed thing: moved and turned only if it has been, so one that has not keeps the
                // scene's own numbers rather than the same turn written another way.
                if (SameTurn(orientation, t.Orientation)) orientation = t.Orientation;
                if (SameTurn(homeTurn, t.SpawnOrientation)) homeTurn = t.SpawnOrientation;
                t.Orientation = orientation;
                if (Vector3.DistanceSquared(t.Position, position) > 1e-6f) place.Move(t, position);
                t.Rehome(home, homeTurn);
            }
            else if (inWorld)
            {
                // A thing made in play, made again: finalized as 1.1 left it (its spawn transform is the
                // record's second matrix) before it enters, so the entry does not finalize it afresh —
                // no born-with effects, no `create`, no `created` (the reader makes it from the record).
                t.Position = position;
                t.Orientation = orientation;
                t.Sector = r.Sector;
                t.Rehome(home, homeTurn);
                place.Enter(t);
            }
            else t.Rehome(position, orientation);
            // The sector the file says, which a region outside every sector is not in (−1).
            if (inWorld) place.Resector(t, r.Sector);
            t.Flags = r.Flags;                                   // THING_FLAG_*
            t.RenderFlags = r.Field128;
            if (r.DescriptionId.Length > 0) t.DescriptionId = r.DescriptionId;
            t.ExpireAt = (int)r.Field90;                         // on the game clock
            t.MoveType = r.Shape;
            if (r.Shape == WorldThing.MovePhysics && r.ShapeData.Length >= 0x18)
            {
                // The physics flags, then the velocity and the spin.
                float F(int at) => BitConverter.ToSingle(r.ShapeData, at);
                t.PhysicsFlags = BitConverter.ToUInt32(r.ShapeData, 0);
                t.Velocity = new Vector3(F(4), F(8), F(12));
                t.RotVelocity = new Vector3(F(16), F(20), F(24));
            }
            if (r.Shape == WorldThing.MoveTrack && r.ShapeData.Length >= 0x88)
            {
                // The keyframe the last track move went to (set as the move starts), which is
                // the one the thing is at once the move is over (its moving bit clear).
                // A move still under way is not carried on — Vitomir's own save does not keep one either.
                uint frame = BitConverter.ToUInt32(r.ShapeData, 0x84);
                if ((BitConverter.ToUInt32(r.ShapeData, 0) & 1) == 0 && frame < (uint)Math.Max(r.Records.Length, t.Keyframes.Length))
                    t.Frame = frame;
            }
            if (r.Records.Length > 0)
            {
                // The keyframes (0x18 bytes each: the angles, then the position — the
                // scene's own order, checked against the monastery's doors and levers).
                var keys = r.Records.Select(k => new NsdKeyframe(
                    new Vector3(BitConverter.ToSingle(k, 0), BitConverter.ToSingle(k, 4), BitConverter.ToSingle(k, 8)),
                    new Vector3(BitConverter.ToSingle(k, 12), BitConverter.ToSingle(k, 16), BitConverter.ToSingle(k, 20)))).ToArray();
                if (!keys.SequenceEqual(t.Keyframes)) t.Keyframes = keys;
            }
            // Its foley, when it has changed (the record's name is the `.nag`).
            if (r.Template is { } foley && foley.EndsWith(".nag", StringComparison.OrdinalIgnoreCase)) t.Foley = foley;
            if (r.Model is { Mode: 2, Model: { Length: > 0 } model } && !string.Equals(model, t.Model, StringComparison.OrdinalIgnoreCase))
                place.SetModel(t, model);
            // The animation it was holding at its end — a corpse's fall — which the writer saves only then.
            if (r.Model is { Mode: 2, Animation: { Length: > 0 } held })
                Found.Poses[id] = new PoseSave { Animation = held, Flags = r.Model.AnimationFlags };

            switch (r.Type)
            {
                case NsgThingType.Container when r.TypeFields.Length >= 2:
                    t.State = r.TypeFields[0];                                   // Bit 0 opened
                    t.ContainerHealth = BitConverter.UInt32BitsToSingle(r.TypeFields[1]);
                    break;
                case NsgThingType.Prop when r.TypeFields.Length >= 1:
                    t.State = r.TypeFields[0];
                    break;
                case NsgThingType.Region when r.TypeFields.Length >= 1:
                    t.RegionFlags = r.TypeFields[0];                             // SetRegionFlags
                    break;
            }
            if (r.Item is { } item && t.Item is { } it) Item(it, r);
            if (r.Actor is { } actor && t.Actor is { } a) Actor(t, a, id, actor);
            if (r.Script is { } script && Decode(script) is { } saved) Found.ByThing[id] = saved;
            links.Add((t, r.Links));
        }

        static ScriptSave? Decode(NsgScript s) => NswImport.Decode(s);

        static void Item(Item item, NsgThing r)
        {
            var s = r.Item!;
            item.Flags = (ItemFlags)s.Flags;
            item.Amount = (int)s.Amount;
            item.Location = (int)s.Location;
            item.Cell = s.Fields2BC is [var x, var y, ..] ? ((int)x, (int)y) : default;
            item.BaseCost = (int)s.BaseCost;
            item.SellFlags = (int)s.SellFlags;
            item.Effects = [.. s.Effects.Select(e => new ItemEffect(e.Name, e.Level, e.Level < 0 ? e.ValueAsFloat : null, (int)e.Time))];
            if (item.Weapon is { } weapon)
            {
                // Tag 0x2d's reader: the rounds loaded per attack, then the byte after them.
                for (int k = 0; k < weapon.Attacks.Length && k < r.Ammo.Length; k++) weapon.Attacks[k].Ammo = (int)r.Ammo[k];
                // The byte after the rounds: which attack a swing uses.
                if (r.AmmoTrailer < weapon.Attacks.Length) weapon.CurrentAttack = r.AmmoTrailer;
            }
        }

        /// <summary>A character's own fields (the actor record), its pack, its effects and its disciplines.</summary>
        void Actor(WorldThing t, Actor a, string id, NsgActor n)
        {
            a.Flags = n.Field1F4;                           // THING_AF_* — a corpse's DEAD bit among them
            a.Flags2 = n.Flags;                             // THING_AF2_*
            a.Type = (ActorType)n.ActorType;
            a.Team = n.Field1FF;
            a.OriginalTeam = n.Field200;                    // which the constructor sets beside the team
            if (n.Text208 is { Length: > 0 } clan && place.Files is { } files) { a.Clan = clan; a.ClanRecord = Clans.For(files)[clan]; }
            a.Health = n.Health;
            a.MaxHealth = n.MaxHealth;
            a.WalkSpeed = n.WalkSpeed;
            a.RunSpeed = n.RunSpeed;
            a.MoveSpeedScale = n.MoveSpeed;
            a.AttackSpeedScale = n.AttackSpeed;
            a.FadeAt = (int)n.FadeAt;

            // What it carries, made again — or, for a thing a scene placed, the placed thing, out of the world.
            foreach (var old in a.Inventory.Items.ToArray())
            {
                a.Inventory.Remove(old);
                if (old.Thing is { } body && !Used.Contains(body)) place.Remove(body, drop: false);
            }
            a.Held = null;
            a.Weapon = a.DefaultWeapon;
            a.Offhand = null;
            a.Armor = null;
            var byItemGuid = new Dictionary<uint, Item>();
            foreach (var saved in n.Inventory.Items)
            {
                if (saved.Item is null || Resolve(saved, inWorld: false) is not var (body, itemId) || body.Item is not { } item)
                {
                    notes.Add($"{place.Name}: {id} carried a {saved.Name} that cannot be made");
                    continue;
                }
                if (body.InWorld) place.Withdraw(body);
                Lay(saved, body, itemId, inWorld: false);
                body.Owner = t;
                item.Thing = body;
                a.Inventory.Add(item);
                byItemGuid[saved.Guid] = item;
            }
            foreach (var item in a.Inventory.Items) Equipment.Restore(t, item);

            // The live effects, a worn item's hanging from it.
            foreach (var old in t.Effects.All) if (old.Script is { } os) place.ScriptObjects?.Free(os);
            t.Effects.Clear();
            foreach (var e in n.Effects)
            {
                if (effects[e.Name] is not { } def) { notes.Add($"{place.Name}: {id} had {e.Name}, which effects.ned does not have"); continue; }
                var effect = new Effect
                {
                    Definition = def, Level = e.ValueLevel, Value = e.ValueAsFloat, Until = e.Level, NextTick = (int)e.NextAt,
                    Fade = BitConverter.UInt32BitsToSingle(e.Field10), Guid = (int)e.Id, CreatorGuid = (int)e.SourceGuid,
                    Flags = e.Flags, IntParam = (int)e.Field28, FloatParam = BitConverter.UInt32BitsToSingle(e.Field2C),
                };
                if ((e.Flags & Effect.Worn) != 0 && byItemGuid.TryGetValue(e.SourceGuid, out var source)) effect.Source = source;
                t.Effects.Append(effect);
                if (e.Script is { } es)
                {
                    string key = Save.ScriptKeys.OfEffect(id, effect.Guid);
                    ScriptKeys[es.Guid] = key;
                    if (Decode(es) is { } saved) Found.ByEffect[key] = saved;
                }
            }

            // The disciplines (tag 0x28), as the party's are read (SaveImport).
            var list = new List<Discipline>();
            foreach (var d in n.Disciplines)
            {
                if (powers[d.Name] is not { } def) { notes.Add($"{place.Name}: {id} knew {d.Name}, which game.ndd does not have"); continue; }
                list.Add(new Discipline
                {
                    Definition = def, Owner = t, Level = Math.Max(-1, d.Level), Flags = d.Field8, CastCount = (int)d.FieldC,
                    QuickSlot = d.Field18 == uint.MaxValue ? -1 : (int)d.Field18,
                });
            }
            if (list.Count > 0 || a.Disciplines.All.Any())
            {
                a.Disciplines.Replace(list);
                a.Disciplines.Current = n.SelectedDiscipline is { } selected ? a.Disciplines[selected] : null;
            }

            // The stats and soaks last, base and current, as the file has them.
            for (int i = 0; i < World.Actor.Stats && i < n.Stats.Length; i++) { a.BaseStat[i] = n.BaseStats[i]; a.Stat[i] = n.Stats[i]; }
            for (int i = 0; i < World.Actor.Soaks && i < n.Soaks.Length; i++) { a.BaseSoak[i] = n.BaseSoaks[i]; a.Soak[i] = n.Soaks[i]; }
        }

        /// <summary>
        /// What rides what (tag 0x23): each child by GUID with the flags, the bone
        /// and the relative transform it rides at. An equipped item records no parent.
        /// </summary>
        public void Link()
        {
            foreach (var (t, l) in links)
                foreach (var a in l.Attachments)
                {
                    if (!byGuid.TryGetValue(a.Guid, out var child) || a.Transform.Length < 48) continue;
                    float F(int i) => BitConverter.ToSingle(a.Transform, i * 4);
                    var m = new Matrix4x4(F(0), F(1), F(2), 0, F(3), F(4), F(5), 0, F(6), F(7), F(8), 0, F(9), F(10), F(11), 1);
                    if (t.Attachments.Any(x => ReferenceEquals(x.Child, child))) continue;
                    t.Attachments.Add(new Attachment(child, a.A, (int)a.B, m));
                    child.AttachedTo = t;
                }
        }
    }

    /// <summary>A codex object's saved words, decoded by its shipped class's layout; null for a class that keeps none.</summary>
    public static ScriptSave? Decode(NsgScript s)
    {
        var values = NsgScriptLayouts.Decode(s.Class, s.Record.ScriptState);
        if (values is null) return null;
        return new ScriptSave
        {
            Class = s.Class,
            Values = [.. values.Select(v => v is string text ? ScriptValue.Of(text) : ScriptValue.Of((int)v))],
        };
    }
}
