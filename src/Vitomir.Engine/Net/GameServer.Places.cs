// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's half of players in different places (`HostPlaces`): which place each player is in, a
// player sent alone to another (the mover's player branch), the revive at the chronicle's revive
// location, and each place's world sent only to the players in it.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public sealed partial class NetPlayer
{
    /// <summary>
    /// The host's place their character is in, or is on its way to (1.1's slot keeps the location
    /// index the mover writes). Null until they first arrive somewhere: then it is the host's own place.
    /// </summary>
    public Location? Place { get; set; }

    /// <summary>The entrance they are arriving at there (kept in the slot); −1 for the place's own arrival.</summary>
    public int Entrance { get; set; } = -1;

    /// <summary>The teleporter they are coming through — its slot and way (kept in the slot, the mover's last two arguments) — or null.</summary>
    public (int Slot, int Way)? Teleport { get; set; }
}

public sealed partial class GameServer
{
    HostPlaces? places;

    /// <summary>The host's places and which one each player is in (<see cref="HostPlaces"/>).</summary>
    public HostPlaces Places => places ??= new HostPlaces(this);

    /// <summary>Where a player's character is on the host: their own place, or the host's until they have one.</summary>
    public Location? PlaceOf(NetPlayer p) => p.Host ? Place : p.Place ?? Place;

    /// <summary>The players whose characters are in a place, or on their way to it.</summary>
    public IEnumerable<NetPlayer> In(Location place) => Players.Where(p => ReferenceEquals(PlaceOf(p), place));

    /// <summary>
    /// A message to the players in one place (the server's send to every client whose
    /// character is in the same world): a place's things, its snapshot, its shots.
    /// </summary>
    void InPlace(Location place, Channel channel, byte[] message)
    {
        foreach (var p in In(place).Where(p => p.Ready && p.Link is not null)) Send(p, channel, message);
    }

    /// <summary>A place of the host's is a network game's (its orders, conversations and awards the host's), as the host's own is at <see cref="Enter"/>.</summary>
    internal void Attach(Location place)
    {
        NetOrders.Attach(place, NetMode.Server);
        SceneRoutes.Attach(place, Scenes);
        NetAwards.Attach(place, this);
        HostSeen.Attach(place, this);                               // what each machine sees there
    }

    /// <summary>A place the host has let go is nobody's any more.</summary>
    internal static void Detach(Location place)
    {
        NetOrders.Detach(place);
        SceneRoutes.Detach(place);
        NetAwards.Detach(place);
        HostSeen.Detach(place);
    }

    /// <summary>Who a player is, as another player is told it: their character's GUID only when both are in the same place (a GUID names a thing of one world).</summary>
    byte[] Joined(NetPlayer p, NetPlayer to)
    {
        if (p.Avatar is null || ReferenceEquals(PlaceOf(p), PlaceOf(to))) return Joined(p);
        var w = new NetWriter(Msg.Joined);
        (p.Info(Storyteller) with { Avatar = 0 }).Write(w);
        return w.ToArray();
    }

    /// <summary>
    /// **A player goes alone to another place** (the mover's player branch). Their character is taken out
    /// of the place it is in, keeping its sheet and pack as it stands (**ours**: 1.1's character makes
    /// the journey itself; here it is made again at the arrival, as the whole game's travel does); their
    /// conversation and camera path stay behind (the slot's conversation and camera path closed); and their
    /// machine is sent the place to load — 1.1's message 0x3f, here `Msg.Travel` with the place's level,
    /// scene and entrance, to them alone. When their machine says it has it (`Msg.Ready`) the
    /// character is made there at the entrance and they are sent that place's world
    /// (<see cref="Catch"/>). The other players are told their character has left their world.
    /// </summary>
    internal void Go(NetPlayer p, Location to, int index, int entrance)
    {
        var from = PlaceOf(p);
        if (p.Avatar is { } body)
        {
            if (body.InWorld && body.Place is { } there)
            {
                p.Character = p.Character.Keep(body);
                there.Remove(body, drop: false);
            }
            if (ReferenceEquals(Possessed, body)) Possessed = null;
        }
        p.Avatar = null;
        p.Ready = false;
        p.Talking = null;
        p.PathEnded = null;
        p.Driving = false;
        p.DrivePush = default;
        p.Place = to;
        p.Entrance = entrance;
        string scene = to.SceneFile.Equals(to.Name, StringComparison.OrdinalIgnoreCase) ? "" : to.SceneFile;
        var w = new NetWriter(Msg.Travel);
        (Settings with { Level = to.Name, Scene = scene, Entrance = Math.Max(0, entrance), SceneGuid = to.SceneGuids.First }).Write(w);
        HostSwitches.Write(w, FixesInForce, QolInForce);
        Send(p, Channel.Reliable, w.ToArray());
        Tell(p, index, entrance);                                   // first: it names the character the others still have
        foreach (var other in Players.Where(o => o != p && o.Link is not null)) Send(other, Channel.Reliable, Joined(p, other));
        Log?.Invoke($"net: {p.Name} goes alone from {from?.Name ?? "nowhere"} to {Places.Chronicle?.NameOf(index) ?? to.Name} " +
                    $"({to.Name}{(scene.Length > 0 ? "/" + scene : "")}) at entrance {entrance}");
    }

    /// <summary>
    /// **The host's own journey, alone** (the mover for slot 0): the host's character takes
    /// an exit or goes through a teleporter, and the host's place is the new one; every other player
    /// stays where they are — their place stays loaded and theirs, and the host's places step it
    /// (<see cref="HostPlaces.Step"/>). A place a player was already in keeps what they have been told
    /// of it. The whole game's travel (a script's `Jump`, or the advance) is
    /// <see cref="Travel"/>.
    /// </summary>
    public void TravelAlone(Location next, WorldThing? hostAvatar)
    {
        if (Place is not { } old || ReferenceEquals(old, next)) { Travel(next, hostAvatar); return; }
        foreach (var p in Players.Where(p => !p.Host && p.Place is null)) p.Place = old;
        string scene = next.SceneFile.Equals(next.Name, StringComparison.OrdinalIgnoreCase) ? "" : next.SceneFile;
        Settings = Settings with { Level = next.Name, Scene = scene, Entrance = Math.Max(0, next.Entrance), SceneGuid = next.SceneGuids.First };
        Place = next;
        watch = Places.Of(next)?.Watch ?? new WorldWatch(next);
        Places.SetHome(next, watch);
        Attach(next);
        if (Possessed is { } held && !ReferenceEquals(held.Place, next)) Possessed = null;
        if (slots[0] is { } me)
        {
            me.Talking = null;
            me.PathEnded = null;
            MoverMinds.Journeyed(me.Avatar, hostAvatar);            // slot 0's mind across
            me.Avatar = hostAvatar;
            me.Ready = true;
            if (hostAvatar is not null) { me.Character.Apply(hostAvatar); NetJoin.Teams(hostAvatar, 0, Flags); }
        }
        if (slots[0] is { } host) Tell(host, Places.IndexOf(next), next.Entrance);
        Log?.Invoke($"net: the host goes alone to {next.Name}{(scene.Length > 0 ? "/" + scene : "")}; " +
                    $"{In(old).Count(p => !p.Host)} player(s) stay in {old.Name}");
    }

    /// <summary>
    /// Whether a chronicle journey the host is about to take is **the host's own character moved alone**
    /// (the mover for slot 0): an exit taken on a host, a teleporter,
    /// or the Storyteller's Jump to Location of the host's own character
    /// (<see cref="HostPlaces.HostAlone"/>). A script's `Jump` (Jump All's
    /// own) and the advance take everybody.
    /// </summary>
    public bool Alone(SequenceRequest? pending) =>
        pending is { Kind: SequenceKind.Exit or SequenceKind.Teleport }
        || (pending is not null && Places.HostAlone == pending);

    /// <summary>
    /// **The host's game has moved** (the windowed host's chronicle journey, `NetPlay.Travelled`): alone
    /// (<see cref="Alone"/>) it is <see cref="TravelAlone"/> — whether or not any player is in the place
    /// it leaves: 1.1's slot-0 branch never moves another player, wherever they are — and otherwise the
    /// whole game's <see cref="Travel"/>. Answers whether players stay in the place the host left, whose
    /// scripts then go on running for them (`CodexScenes.KeepRunning`).
    /// </summary>
    public bool Journey(Location next, WorldThing? hostAvatar, bool alone)
    {
        var old = Place;
        if (!alone || old is null || ReferenceEquals(old, next)) { Travel(next, hostAvatar); return false; }
        TravelAlone(next, hostAvatar);
        return Places.Occupied(old);
    }

    /// <summary>
    /// **Every other player is told where one has gone** (the mover builds its message 0x3f — the slot,
    /// the location's index and entrance, the teleporter's slot and way, the location's name and two
    /// numbers of its world — and sends it to every client, before it looks at whose
    /// slot it is, so the host's own journey is told too). A machine that is not that player's reads it:
    /// the character is stopped and taken out of its world at once
    /// (`GameClient.Went`). Here `Msg.Went` with the slot, index, entrance and name, to every player but
    /// the one going (theirs is `Msg.Travel`); **ours**: the teleporter's two and the world's two
    /// numbers are not carried (nothing on the other machines reads them).
    /// </summary>
    void Tell(NetPlayer going, int index, int entrance)
    {
        string name = Places.Chronicle is { } story && index >= 0 && index < story.Count ? story.NameOf(index)
                    : going.Host ? Place?.Name ?? "" : going.Place?.Name ?? "";
        var w = new NetWriter(Msg.Went).U8((byte)going.Slot).I32(index).I32(entrance).Str(name);
        var message = w.ToArray();
        foreach (var other in Players.Where(o => o != going && !o.Host && o.Link is not null)) Send(other, Channel.Reliable, message);
    }

    /// <summary>
    /// The place a player's machine is told to load when its Start lets it in (1.1's message 0x3f):
    /// the game's own — or, in a game under way whose revive location is not the host's place, that
    /// location, loaded now and made theirs, so their machine loads it at once.
    /// </summary>
    GameSettings JoinSettings(NetPlayer p)
    {
        if (!Started || Place is not { } home || Places.Chronicle is not { } story || Places.Load is null) return Settings;
        var (index, entrance) = story.Revive;
        if (story.At(index) is not { LevelName.Length: > 0 } || Places.IndexOf(home) == index) return Settings;
        if (Places.Load(index, entrance) is not { } there) return Settings;
        Places.Adopt(there, index);
        p.Place = there;
        p.Entrance = entrance;
        string scene = there.SceneFile.Equals(there.Name, StringComparison.OrdinalIgnoreCase) ? "" : there.SceneFile;
        return Settings with { Level = there.Name, Scene = scene, Entrance = Math.Max(0, entrance), SceneGuid = there.SceneGuids.First };
    }

    /// <summary>
    /// A newcomer's first place: a player who has just joined is sent to the chronicle's
    /// revive location and entrance (where `reviveloc` says joining
    /// players start, and which `SetReviveLocation` moves), wherever the host is. A player who joined
    /// before the game began (and so loaded the lobby's place) and finds the revive location elsewhere
    /// is sent on there; one let in by its Start was told it then (<see cref="JoinSettings"/>).
    /// True when they were sent on.
    /// </summary>
    bool Newcomer(NetPlayer p)
    {
        if (!p.Joining || p.Place is not null || Places.Chronicle is not { } story || Place is not { } home) return false;
        var (index, entrance) = story.Revive;
        if (story.At(index) is not { LevelName.Length: > 0 }) return false;
        if (Places.IndexOf(home) == index) { p.Entrance = entrance; return false; }
        if (Places.Load?.Invoke(index, entrance) is not { } there) return false;
        Places.Adopt(there, index);
        Go(p, there, index, entrance);
        return true;
    }

    /// <summary>
    /// After a revive: the character is moved to the chronicle's revive location and entrance
    /// (which `SetReviveLocation` moves) through the same mover as an exit.
    /// **Ours**: when the revive location is the place the character is already in, it is
    /// stood on that entrance's start there rather than sent to load the place again — how 1.1's player
    /// takes a 0x3f for the place it is in is unread.
    /// </summary>
    void RevivedAt(NetPlayer p, WorldThing body)
    {
        if (Places.Chronicle is not { } story || story.Revive.Location < 0 || story.At(story.Revive.Location) is not { } at
            || at.LevelName.Length == 0) return;
        var (index, entrance) = story.Revive;
        if (body.Place is { } here && Places.IndexOf(here) == index)
        {
            var starts = HostPlaces.Starts(here, entrance);
            if (starts.Length == 0) return;
            var (spot, yaw) = HostPlaces.Stand(starts[p.Slot % starts.Length]);
            body.Orientation = new System.Numerics.Vector3(0, 0, yaw);
            here.Move(body, spot);
            here.WalkerFor(body)?.Ground();
            Log?.Invoke($"net: {p.Name} revives at {story.NameOf(index)}, entrance {entrance}");
            return;
        }
        if (Places.Move(body, index, entrance)) Log?.Invoke($"net: {p.Name} revives at {story.NameOf(index)}, entrance {entrance}");
    }
}
