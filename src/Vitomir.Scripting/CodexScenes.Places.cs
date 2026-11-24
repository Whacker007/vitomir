// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A host's scripts in several places at once (`Net.HostPlaces`). 1.1's scripts live with their world
// (one per location) and a host keeps up to eleven worlds loaded with players in any
// of them; Vitomir has one script layer, so it is pointed at the place it is working in — the place's
// scripts, timers and clock put aside and taken up again, with no `endscene` or `beginscene`, which a
// place hears only when a player leaves or arrives. And
// the natives that move one actor on a host: `TakeExit`, `PlayerJump`
// and `TeleportPartyToConversation` for a player's character.
using Vitomir.Engine.Net;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>A place's scripts while the layer is working in another of the host's places — everything <see cref="Kept"/> keeps, and the arrivals due.</summary>
    sealed record Held(Kept State, List<WorldThing> Arrivals);

    readonly Dictionary<Location, Held> held = new(ReferenceEqualityComparer.Instance);

    /// <summary>A place is being opened for a player going there alone: its scripts are made and told `created`, but no scene begins.</summary>
    bool opening;

    /// <summary>The place the scripts are working in now.</summary>
    public Location? Current => location;

    /// <summary>Whether a place's scripts are running — the current place's, or one put aside by <see cref="Use"/>.</summary>
    public bool Runs(Location place) => ReferenceEquals(location, place) || held.ContainsKey(place);

    /// <summary>Puts the current place's scripts aside, as they are, and works in no place.</summary>
    void Hold()
    {
        if (location is null) return;
        held[location] = new Held(new Kept([.. live], [.. timers], new(scriptThing), thingGuids, worldCount, chapterCount, now), [.. pendingArrivals]);
        live.Clear();
        timers.Clear();
        scriptThing.Clear();
        pendingArrivals.Clear();
        location = null;
    }

    /// <summary>
    /// Works in another of the host's places from now on: the current place's scripts put aside and
    /// that place's taken up where they were — nothing is told, no clock runs while a place is aside
    /// (the host steps every place it has loaded, `HostPlaces.Step`). Nothing happens for a
    /// place whose scripts are not running.
    /// </summary>
    public void Use(Location place)
    {
        if (ReferenceEquals(place, location) || !held.Remove(place, out var h)) return;
        Hold();
        var k = h.State;
        live.AddRange(k.Live);
        timers.AddRange(k.Timers);
        foreach (var (a, b) in k.ScriptThing) scriptThing[a] = b;
        thingGuids = k.ThingGuids;
        worldCount = k.WorldCount;
        chapterCount = k.ChapterCount;
        now = k.Now;
        pendingArrivals.AddRange(h.Arrivals);
        location = place;
    }

    /// <summary>
    /// A place loaded on a host for a player going there alone: its scripts made
    /// — or brought back from when the game was last there — and put aside, the place the scripts were
    /// working in taken up again. **No scene begins**: 1.1's mover fires no `beginscene` of its own for
    /// a place loaded for a player; the player's arrival does, with their GUID
    /// (<see cref="Arrived"/>). The chronicle's current location stays the host's.
    /// </summary>
    public void Open(Location place)
    {
        if (Runs(place)) return;
        var was = location;
        int current = Chronicle?.Current ?? -1;
        var story = Chronicle;
        Hold();
        opening = true;
        try { Enter(place); }
        finally
        {
            opening = false;
            if (story is not null) { Chronicle = story; story.Current = current; }
        }
        Hold();
        if (was is not null) Use(was);
    }

    /// <summary>
    /// A place's scripts put aside and the whole game arriving there (the host's own journey): they are
    /// handed to <see cref="Enter"/> as a place come back to (the same objects, their timers caught up).
    /// </summary>
    void Unhold(Location place)
    {
        if (held.Remove(place, out var h)) kept[place] = h.State;
    }

    /// <summary>
    /// The host has gone on alone and left players in the place it left (`GameServer.TravelAlone`): the
    /// place's scripts, put aside by the journey's <see cref="Leave(int, bool)"/>, go on running for them.
    /// </summary>
    public void KeepRunning(Location place)
    {
        if (kept.Remove(place, out var k)) held[place] = new Held(k, []);
    }

    /// <summary>
    /// A player's character leaves one of the host's places for another (with its
    /// GUID): every script of the current place, and the chronicle's, hears `endscene(guid, 0, 0)` — the
    /// exit is not passed on this path.
    /// </summary>
    public void LeftBy(int playerGuid)
    {
        foreach (var l in InListOrder())                                           // the world's list, newest first
        {
            try { runtime.CallEndscene(l.Script, playerGuid, 0, 0); }
            catch (Exception e) { Engine.Log($"{l.Name}.endscene failed: {ScriptException.From(l.Name, e).JavaType}"); }
        }
        foreach (var (name, script) in ChronicleScripts())
        {
            try { runtime.CallEndscene(script, playerGuid, 0, 0); }
            catch (Exception e) { Engine.Log($"{name}.endscene failed: {ScriptException.From(name, e).JavaType}"); }
        }
    }

    /// <summary>
    /// The natives that move one actor, as a host has them (`HostPlaces`), over the single player's:
    /// <list type="bullet">
    /// <item>`TakeExit(exit, actor)`: a remote player's character takes the exit of its own place alone;
    /// anybody else's goes the single player's way (the host's
    /// journey);</item>
    /// <item>`PlayerJump(location, entrance, actor)`: a character only, moved alone — no
    /// shipped script calls it;</item>
    /// <item>`TeleportPartyToConversation(guid, n)` for a remote player's character moves that character
    /// alone to its conversation start;</item>
    /// <item>`CodexThing.GetLocationNum` answers the location of the place the scripts are working in.</item>
    /// </list>
    /// </summary>
    void HostMoves(GameServer server)
    {
        var places = server.Places;
        places.Use = Use;
        places.Wake = place => { KeepRunning(place); return Runs(place); };   // a loaded place's scripts run on a host
        places.Left += (_, who) => LeftBy(who.Guid);
        places.Came += (_, who) => Arrived(who.Guid, joined: false);                // the move's last branch: the arrival
        Wrap("CodexSequence.TakeExit", (a, local) =>
        {
            if (location?.Thing(CodexHost.Int(a, 1)) is { Type: ThingType.Actor or ThingType.Player } who
                && server.Players.Any(p => !p.Host && ReferenceEquals(p.Avatar, who)))
            {
                places.TakeExit(who, CodexHost.Int(a, 0));
                return null;
            }
            return local();
        });
        Wrap("CodexSequence.PlayerJump", (a, local) =>
        {
            int to = Chronicle?.Find(Name(a, 0)) ?? -1, entrance = CodexHost.Int(a, 1);
            if (to >= 0 && entrance >= 0 && location?.Thing(CodexHost.Int(a, 2)) is { Type: ThingType.Actor or ThingType.Player } who)
                places.Move(who, to, entrance);
            return null;
        });
        Wrap("CodexPlayer.TeleportPartyToConversation", (a, local) =>
        {
            if (location?.Thing(CodexHost.Int(a, 0)) is { } who && places.ToConversation(who, (uint)CodexHost.Int(a, 1), out bool refused))
                return CodexHost.Box(!refused);
            return local();
        });
        Wrap("CodexThing.GetLocationNum", (a, local) =>
            location is { } here && places.IndexOf(here) is var at and >= 0 ? CodexHost.Box(at) : local());
    }
}
