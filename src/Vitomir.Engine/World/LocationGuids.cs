// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// How a place numbers its things from the game's one counter (`ThingGuids`).
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

public sealed partial class Location
{
    /// <summary>
    /// The game's GUID counter, which this place draws every new number from.
    /// A campaign's places share one; a place loaded on its own has its own.
    /// </summary>
    public ThingGuids Guids { get; init; } = new();

    /// <summary>
    /// The numbers the scene's own things were given: <c>First</c> is the counter as the scene
    /// reader found it (the scene load records it before it reads a thing),
    /// <c>End</c> the counter after the last. Every placed thing's GUID is in the range, and so are
    /// the numbers its characters' fallback weapons spent. The network tells a joining machine
    /// where its host's range starts (<see cref="RenumberScene"/>).
    /// </summary>
    public (int First, int End) SceneGuids { get; private set; }

    /// <summary>The scene's range moved by <paramref name="by"/>, its things having been given their saved numbers.</summary>
    internal void ShiftScene(int by) => SceneGuids = (SceneGuids.First + by, SceneGuids.End + by);

    /// <summary>Whether <paramref name="guid"/> is one the scene numbered as it was read.</summary>
    public bool FromScene(int guid) => guid >= SceneGuids.First && guid < SceneGuids.End;

    /// <summary>
    /// Numbers a place's things as 1.1 makes them, from the one counter.
    ///
    /// **The player first, when one is being made.** 1.1 makes a new game's player at the
    /// chronicle's start, before any place is read, and its making
    /// takes the kit first — each `.NPC` `item` line is made and an equipped
    /// sword hangs its blade trail — then the fallback
    /// weapon (<see cref="SpendFallback"/>), then the player: `CuthbertLetter` 1, broadsword 2,
    /// Christof **5**, as in every game and save of 1.1. The kit is given later here (the harness gives it
    /// once the place stands), so its numbers are held for it (<see cref="ThingGuids.Hold"/>). A
    /// player arriving by a journey keeps the number it has (<see cref="ThingGuids.Arriving"/>).
    ///
    /// **Then the scene, in its order** (the world scene's things, then the chapter's),
    /// each character first spending a number on its fallback weapon.
    /// </summary>
    static (int First, int End) NumberThings(WorldThing[] things, int playerAt, ThingGuids guids,
                                             GameFiles files, TemplateSet templates, string key)
    {
        if (playerAt >= 0 && playerAt < things.Length)
        {
            var player = things[playerAt];
            if (guids.Arriving > 0) player.Guid = guids.Arriving;
            else
            {
                foreach (bool trail in KitGuids(player, files, templates))
                    if (trail) guids.HoldTrail(); else guids.Hold(1);
                SpendFallback(player, templates, guids);
                player.Guid = guids.Take();
            }
        }
        guids.ReservePending();                                          // the preloaded places' (Preloads.cs)
        // A preloaded place is numbered where its numbers were set aside, the counter left alone.
        var counter = guids.Reserved.Remove(key, out int reserved) && !guids.Loading
            ? new ThingGuids { Next = reserved, Host = guids.Host } : guids;
        int first = counter.Peek;
        for (int i = 0; i < things.Length; i++)
        {
            if (i == playerAt) continue;
            if (guids.KeepWorld?.GetValueOrDefault(i) is > 0 and var kept) { things[i].Guid = kept; continue; }
            if (!guids.Loading) CharacterSheets.Hold(things[i], files, templates, counter);   // a companion's kit first
            SpendFallback(things[i], templates, counter);
            things[i].Guid = counter.Take();
        }
        // A level kept through a chapter swap: its things' numbers are older than the counter.
        if (guids.KeepWorld is { Count: > 0 } world) first = Math.Min(first, world.Values.Min());
        return (first, counter.Peek);
    }

    /// <summary>
    /// This place's level-scene things by scene index, with their GUIDs — the ones still here or
    /// carried, and those a load took away (<see cref="Gone"/>) — for a rebuild that keeps them
    /// (<see cref="ThingGuids.KeepWorld"/>). Null for a place with no chapter scene over its level.
    /// </summary>
    public Dictionary<int, int>? WorldGuids()
    {
        if (ChapterScene is null || WorldScene is not { } world) return null;
        var map = new Dictionary<int, int>();
        foreach (var t in byGuid.Values.Concat(things))
            if (t.Index >= 0 && t.Index < world.Things.Length && !ThingGuids.IsPlaceholder(t.Guid)) map[t.Index] = t.Guid;
        return map;
    }

    /// <summary>
    /// A character's fallback weapon is a thing in 1.1: the template tag 0x5d (`WEAPON` on an actor)
    /// is read by the weapon tag, which makes a thing of the template it names — unless a save
    /// is being read — while the character is still being built, so it takes the number **before**
    /// its character's own. Vitomir keeps the fallback weapon as the actor's
    /// <see cref="Actor.DefaultWeapon"/> and makes no thing of it (ours), but spends the number, which
    /// is what keeps every GUID after it where 1.1 has it: the convent's szlachta are 99 and 101, their
    /// claws 98 and 100, and Old Town's knights 271 and 273.
    /// </summary>
    static void SpendFallback(WorldThing thing, TemplateSet templates, ThingGuids guids)
    {
        if (thing.Actor is { WeaponTemplate.Length: > 0 } a && templates.Has(a.WeaponTemplate)) guids.Take();
    }

    /// <summary>
    /// The things a player's making makes before it, in order: each of the `.NPC`'s items (false),
    /// and after an equipped weapon that hangs a trail — the blade-trail attack flag and a trail
    /// template, the template test — its trail (true). None for a character with no `.NPC`.
    /// </summary>
    static IEnumerable<bool> KitGuids(WorldThing player, GameFiles files, TemplateSet templates)
    {
        if (player.Actor is not { RpgStats.Length: > 0 } sheet) yield break;
        if (!files.TryRead(NpcFile.PathFor(sheet.RpgStats), out var data)) yield break;
        foreach (var line in NpcFile.Read(data).Items)
        {
            if (!templates.Has(line.Template)) continue;
            yield return false;
            if (line.Where == NpcPlacement.Equip
                && Rpg.Weapon.From(line.Template, templates.Resolve(line.Template)) is { } w
                && w.Attacks.Length > 0 && w.Attacks[0].Is(Rpg.WeaponFlag.BladeTrail) && w.Trail.Length > 0
                && templates.Has(w.Trail))
                yield return true;
        }
    }

    /// <summary>
    /// A number for a thing made while the game runs from <paramref name="template"/>: a character
    /// spends one on its fallback weapon first, as its making does in 1.1 (the weapon tag's case
    /// 0x5d, then the character's own). The numbers a player's making held come first.
    /// </summary>
    internal int TakeGuidFor(string template)
    {
        if (Templates is { } templates && templates.Has(template))
        {
            var tags = templates.Resolve(template);
            var type = tags.TryGetValue(Tags.Type, out var ty) ? (ThingType)ty.AsUInt() : ThingType.None;
            if (type is ThingType.Actor or ThingType.Player
                && tags.TryGetValue(Tags.ActorWeapon, out var weapon) && weapon.AsString() is { Length: > 0 } name
                && templates.Has(name))
                TakeGuid();
        }
        return TakeGuid();
    }

    /// <summary>
    /// Makes <paramref name="guid"/> free here for a thing that brings it — a party member or a
    /// carried item arriving by a journey, whose thing in 1.1 is the same thing. A body it
    /// left out of the world here on an earlier visit gives the number up; a thing in the world keeps
    /// it, and the answer is false.
    /// </summary>
    internal bool Claim(int guid)
    {
        if (guid <= 0) return false;
        if (byGuid.TryGetValue(guid, out var holder))
        {
            if (holder.InWorld) return false;
            byGuid.Remove(guid);
        }
        Guids.Past(guid);
        return true;
    }

    /// <summary>
    /// Gives a thing here another GUID — what a load does to hand every saved thing the number it was
    /// saved with (1.1 reads it from the file), after the place was built from its scene.
    /// False, and nothing changed, when another thing here holds the number.
    /// </summary>
    public bool Rekey(WorldThing thing, int guid)
    {
        if (guid <= 0 || thing.Guid == guid) return guid > 0;
        if (byGuid.TryGetValue(guid, out var other) && !ReferenceEquals(other, thing) && !Claim(guid)) return false;
        bool registered = byGuid.TryGetValue(thing.Guid, out var self) && ReferenceEquals(self, thing);
        if (registered) byGuid.Remove(thing.Guid);
        foreach (var index in Gone.Where(g => g.Value == thing.Guid).Select(g => g.Key).ToList()) Gone[index] = guid;
        thing.Guid = guid;
        if (registered) byGuid[guid] = thing;
        Guids.Past(guid);
        return true;
    }

    /// <summary>
    /// Before a load hands out its saved numbers: every thing here holding one of
    /// <paramref name="wanted"/> that is not its own is moved to a placeholder, so each saved number is
    /// free for the thing it belongs to — whatever numbers the place happened to be built with.
    /// </summary>
    public void MakeWay(IReadOnlySet<int> wanted, IReadOnlyDictionary<WorldThing, int> own)
    {
        foreach (var t in byGuid.Values.Concat(things).Distinct().ToList())
            if (wanted.Contains(t.Guid) && !(own.TryGetValue(t, out int mine) && mine == t.Guid))
                Rekey(t, Guids.Loading ? Guids.Placeholder() : Guids.Fresh(g => wanted.Contains(g) || byGuid.ContainsKey(g)));
    }

    /// <summary>
    /// A joining machine's copy of its host's place, renumbered to the host's (1.1's message 0x41,
    /// the host's scene message, sends a scene's GUIDs for the client to take). Every machine numbers a
    /// scene the same way from wherever its counter stands, so one number — where the host's scene
    /// range starts — puts every placed thing here on the host's GUID.
    /// </summary>
    public void RenumberScene(int hostFirst)
    {
        int shift = hostFirst - SceneGuids.First;
        if (shift == 0 || hostFirst <= 0) return;
        var placed = things.Concat(byGuid.Values).Where(t => FromScene(t.Guid) && t.Index >= 0).Distinct().ToList();
        foreach (var t in placed) byGuid.Remove(t.Guid);
        SceneGuids = (SceneGuids.First + shift, SceneGuids.End + shift);
        Guids.Past(SceneGuids.End - 1);                                  // by count: a host's bit is not the counter's
        foreach (var index in Gone.Keys.ToList()) Gone[index] += shift;
        foreach (var t in placed)
        {
            t.Guid += shift;
            // Anything of this machine's own already on the host's number moves out of the way.
            if (byGuid.Remove(t.Guid, out var mine)) { mine.Guid = TakeGuid(); byGuid[mine.Guid] = mine; }
            byGuid[t.Guid] = t;
        }
    }
}
