// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's places. A network game's host keeps several of the chronicle's locations loaded at once
// — up to eleven — and each player is in whichever one
// they went to: a player taking an exit travels alone, the
// Storyteller's Jump to Location moves one actor, a revive moves the character to
// the chronicle's revive location. This is the host's list of them, which of
// them each player is in, what each one's players have been told of it, and what steps it.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Net;

/// <summary>One of the host's places: its world, what its players have been told of it, and what steps it.</summary>
public sealed class HostPlace
{
    public required Location Place { get; init; }
    /// <summary>The chronicle's location index (what 1.1's player slot keeps and message 0x3f carries), −1 for a game with no chronicle.</summary>
    public int Index { get; init; } = -1;
    /// <summary>What the players in it have been told of it: things made and removed, and the snapshot (`Replication.cs`).</summary>
    public required WorldWatch Watch { get; init; }
    /// <summary>
    /// What steps it while a player is there — a place of its own, made when a player first arrives
    /// alone (<see cref="HostPlaces.Simulate"/>). The host's own place has none here: the game's run
    /// steps that one, as it always has.
    /// </summary>
    public Simulation? Sim { get; set; }
}

/// <summary>
/// **The host's places** (1.1 keeps one world per chronicle location, and each player slot the
/// location it is in). The host's own place — where the game's run is, where the host's character and camera are
/// — is <see cref="Home"/>; every other place a player has gone to alone is kept here beside it with a
/// <see cref="Simulation"/> of its own, and stepped while a player is in it.
///
/// **What moves whom** (the host's one mover): a remote player's character is taken out of
/// its place — its scripts hear `endscene` with its GUID — and its player is sent the new
/// place to load (message 0x3f, here `Msg.Travel` to that player alone); when their machine says it has
/// the place the host makes the character there at the entrance and sends that place's
/// world, and its scripts hear `joined` and `beginscene` for the player as on any arrival.
/// The host's own character goes the host's own way (the chronicle's journey), and a
/// character nobody plays goes only when it is the one being steered — the Storyteller's possessed
/// body — made again in the other place at the entrance's first start (the mover's last branch).
///
/// **Which places are stepped**: the host's own, by the run, and every other place it has loaded,
/// whoever is in it (on a host the original ticks every location with a world;
/// <c>HostPlaces.Loaded.cs</c>). A single player's game steps only the place the party is in.
///
/// **When a place goes** is the campaign's rule (`CampaignUnload.cs`): past eleven, the one loaded
/// earliest goes, passing over — on a host — every place a player's character is in (the unload's
/// slot walk, <see cref="Occupied"/>). A place the campaign lets go is forgotten here
/// (<see cref="Forget"/>).
/// </summary>
public sealed partial class HostPlaces : ITeleportNetwork
{
    /// <summary>How many places a network game's host keeps loaded (a single player's game keeps six).</summary>
    public const int Most = Campaign.KeptPlacesHosting;

    readonly GameServer server;
    readonly List<HostPlace> all = [];

    internal HostPlaces(GameServer server)
    {
        this.server = server;
        server.Arrived += MoverMinds.Arrived;                      // a frenzy the mover kept
        // A player leaving has every pair they made shut.
        server.Departing += p =>
        {
            if (p.Avatar is { Place.Session.Teleports: { } pairs } body && pairs.ShutFor(body) is var shut and > 0)
                server.Note($"net: {p.Name}'s {shut} teleporter pair{(shut == 1 ? "" : "s")} shut");
        };
    }

    /// <summary>The host these places are.</summary>
    public GameServer Server => server;

    /// <summary>The chronicle whose locations the places are, for their indices, names and exits; null for a game on a bare level.</summary>
    public Chronicle? Chronicle { get; set; }

    /// <summary>
    /// Loads a chronicle location for a player going there alone, at an entrance — or answers the place
    /// already loaded (a place with a world is not built again). The run's, because building a
    /// place wants its minds, effects and scripts (`Campaign.Visit` and the scripts' `Open`). Null: no
    /// place can be loaded, and nobody travels alone.
    /// </summary>
    public Func<int, int, Location?>? Load { get; set; }

    /// <summary>Makes a place's own simulation, the first time a player arrives there alone (the run's: its scripts, its conversations).</summary>
    public Func<Location, Simulation>? Simulate { get; set; }

    /// <summary>
    /// Makes a place's scripts the ones that answer, before anything happens in it (the scripts' swap,
    /// `CodexScenes.Use`). 1.1's scripts live with their world; Vitomir's one script layer is pointed at
    /// the place it is working in.
    /// </summary>
    public Action<Location>? Use { get; set; }

    /// <summary>A player's character leaves a place: its scripts hear `endscene` with the character's GUID.</summary>
    public event Action<Location, WorldThing>? Left;

    /// <summary>A character nobody plays has been moved into a place: its scripts hear `beginscene` with the character's GUID (the mover's last branch).</summary>
    public event Action<Location, WorldThing>? Came;

    /// <summary>Every place the host has, its own first.</summary>
    public IReadOnlyList<HostPlace> All => all;

    /// <summary>The host's own place (`GameServer.Place`).</summary>
    public HostPlace? Home { get; private set; }

    /// <summary>The host's record of a place, or null.</summary>
    public HostPlace? Of(Location? place) => place is null ? null : all.Find(h => ReferenceEquals(h.Place, place));

    /// <summary>The chronicle's index of a place (its level and scene), −1 when it is not one of the chronicle's.</summary>
    public int IndexOf(Location place)
    {
        if (Of(place) is { Index: >= 0 } known) return known.Index;
        if (Chronicle is not { } story) return -1;
        int byLevel = -1;
        for (int i = 0; i < story.Count; i++)
        {
            if (story.At(i) is not { } at || !at.LevelName.Equals(place.Name, StringComparison.OrdinalIgnoreCase)) continue;
            string scene = at.SceneName.Length > 0 ? at.SceneName : place.Name;
            if (scene.Equals(place.SceneFile, StringComparison.OrdinalIgnoreCase)) return i;
            if (byLevel < 0) byLevel = i;
        }
        return byLevel;
    }

    /// <summary>Whether a remote player's character is in a place, or on its way there — the unload lets no such place go.</summary>
    public bool Occupied(Location place) =>
        server.Players.Any(p => !p.Host && ReferenceEquals(server.PlaceOf(p), place));

    /// <summary>
    /// The place the host's camera looks into when it is not the host's own: the Storyteller's camera
    /// gone with a body it carried there (`Vitomir.StoryView`). Null otherwise.
    /// </summary>
    public Location? Watched { get; set; }

    /// <summary>
    /// Whether a place stays loaded when the campaign lets one go: a player is in it (<see cref="Occupied"/>),
    /// or it is the one the camera is in (<see cref="Watched"/>) — the unload passes over "the camera's"
    /// place as well as the players'. Every place loaded is stepped, kept or not (<see cref="Step"/>).
    /// </summary>
    public bool Kept(Location place) => Occupied(place) || ReferenceEquals(place, Watched);

    /// <summary>
    /// The simulation that steps one of the host's other places (made now if it has none), for a view of it
    /// to draw from without stepping it a second time; null for the host's own place (the run steps that)
    /// or a place the host does not have.
    /// </summary>
    public Simulation? Stepper(Location place)
    {
        if (Of(place) is not { } h || ReferenceEquals(h, Home)) return null;
        return h.Sim ??= Started(h.Place);
    }

    /// <summary>The thing the last carry made — the body in the place it went to, with its new GUID (<see cref="Carry"/>).</summary>
    public WorldThing? Carried { get; private set; }

    /// <summary>The host's own place is this one now (the host's world came up, or the whole game travelled).</summary>
    internal void SetHome(Location place, WorldWatch watch)
    {
        var had = Of(place);
        if (had is not null && !ReferenceEquals(had.Watch, watch)) all.Remove(had);
        var home = had is not null && ReferenceEquals(had.Watch, watch) ? had : new HostPlace { Place = place, Index = IndexOf(place), Watch = watch };
        // The run steps its own place; a simulation this list made for it while a player was there alone is let go.
        home.Sim = null;
        all.Remove(home);
        all.Insert(0, home);
        Home = home;
        HostAlone = null;                                           // taken
    }

    /// <summary>A place is among the host's from now on (a player is going there alone).</summary>
    internal HostPlace Adopt(Location place, int index)
    {
        server.Attach(place);                                       // its orders, conversations and awards the host's
        if (Of(place) is { } had) return had;
        var made = new HostPlace { Place = place, Index = index, Watch = new WorldWatch(place) };
        all.Add(made);
        return made;
    }

    /// <summary>A place the host has let go (the campaign's `Discarded`): nobody is told any more of it.</summary>
    public void Forget(Location place)
    {
        if (ReferenceEquals(Home?.Place, place)) return;
        if (all.RemoveAll(h => ReferenceEquals(h.Place, place)) > 0) GameServer.Detach(place);
    }

    Location? working;

    /// <summary>
    /// The place the host is working in now — the one being stepped, or whose player's order is being
    /// carried out — and otherwise the host's own. What a script names by GUID is looked for here
    /// (`HostTexts`): a GUID names a thing of one place (ours: 1.1's GUIDs are the whole game's).
    /// </summary>
    public Location? Working => working ?? Home?.Place;

    /// <summary>Does something with a place's scripts the ones answering, and gives them back to the host's own place after.</summary>
    internal void Within(Location? place, Action act)
    {
        if (place is null || ReferenceEquals(place, Home?.Place)) { act(); return; }
        var was = working;
        working = place;
        Use?.Invoke(place);
        try { act(); }
        finally
        {
            working = was;
            if ((was ?? Home?.Place) is { } back) Use?.Invoke(back);
        }
    }

    /// <summary>
    /// One turn of every place but the host's own that the host has loaded (the host's arm of the
    /// world tick; see the class): its scripts are pointed at it and its simulation stepped, and the host's own place has
    /// them back after. Earlier, only a place a player or the camera was in was stepped.
    /// </summary>
    public void Step(float seconds)
    {
        AdoptLoaded();                                              // the campaign's other loaded places (HostPlaces.Loaded.cs)
        foreach (var h in all.ToArray())
        {
            if (ReferenceEquals(h, Home) || !(Awake(h) || Kept(h.Place))) continue;
            h.Sim ??= Started(h.Place);
            Within(h.Place, () => h.Sim.Step(seconds));
        }
    }

    // ---- moving one actor ------------------------------------------------------------

    /// <summary>
    /// A character takes an exit on the host (the exit taken in net mode 2, handed to the mover): the exit
    /// is the one of **the character's own place** (the location its world belongs to), checked by the
    /// same three gates the single player's `TakeExit` has — there, not closed, leads somewhere — and the
    /// character is moved alone (<see cref="Move"/>). False when it is refused, or when the character is
    /// not a remote player's (the caller's own journey takes it then).
    /// </summary>
    public bool TakeExit(WorldThing who, int exit)
    {
        if (Chronicle is not { } story || who.Place is not { } from) return false;
        int at = IndexOf(from);
        if (story.At(at) is not { } here) return false;
        if (exit < 0 || exit >= here.ExitCount) return false;
        var way = here.Exits[exit];
        if (way.Closed || way.To < 0) return false;
        return Move(who, way.To, way.Entrance);
    }

    /// <summary>
    /// **Moves one character to a location**: a remote player's goes alone and their machine
    /// loads the place; true when it went. The host's own answers false — it is the chronicle's journey,
    /// which the caller takes — and a character nobody plays goes by
    /// <see cref="Carry"/> (the mover's last branch). A player's character going has its possessions ended
    /// and its mind reset unless frenzied (<see cref="MoverMinds"/>).
    /// </summary>
    public bool Move(WorldThing who, int index, int entrance)
    {
        var owner = server.Players.FirstOrDefault(p => ReferenceEquals(p.Avatar, who));
        if (owner is null) return Carry(who, index, entrance);
        if (owner is not { Host: false } player) return false;
        if (Chronicle?.At(index) is not { } at || at.LevelName.Length == 0 || Load is null) return false;
        var from = server.PlaceOf(player);
        // The host loads the place before the player is sent it.
        if (Load(index, entrance) is not { } to) return false;
        var record = Adopt(to, index);
        MoverMinds.Leaving(player, who);                            // its possessions ended, its mind reset
        if (from is not null && ReferenceEquals(from, to) is false && who.InWorld)
            Within(from, () => Left?.Invoke(from, who));
        server.Go(player, record.Place, index, entrance);
        return true;
    }

    /// <summary>
    /// **A character nobody plays, moved to a location** (the mover's last branch). Only the one
    /// being steered goes (the current control — on a host in the
    /// Storyteller's seat, a possessed body); any other answers false and nothing happens, as 1.1's
    /// mover does nothing for it. The place is loaded, the start is the entrance's first
    /// (group <paramref name="entrance"/>, order 1, the place's own starts);
    /// **nothing it was doing is cancelled and its walk is not stopped** — the cancel and the stop
    /// are the player branch's alone, so its action, busy time and effects go with
    /// it as they are (the whole actor is carried); it leaves its
    /// place (that place's machines are told) — its scripts hear **no** `endscene` on
    /// this branch — and stands in the other at the start, on the ground; the
    /// other place's machines are told of it and its scripts hear `beginscene(guid, 0)`,
    /// and its mind is attached there — the mind going as it is, with no
    /// possession ended and no reset, which are the player branch's (<see cref="MoverMinds.Carried"/>).
    /// **Ours**: the character is made again from its template at the start and given its own actor —
    /// sheet, pack, effects, disciplines — as the party's journey carries a companion
    /// (<see cref="Campaign"/>'s `Carry`), so it has a new GUID there (<see cref="Carried"/>). Two things
    /// do not go, where in 1.1 the thing itself keeps them: the walk — the new thing's walker is its own
    /// and still, so an ACTION 1 ends on its first tick — and the target, which
    /// `Campaign.Carry` drops. A feed ends from its own tick either way — 1.1's feeder tick
    /// cancels, forced, a feed whose victim is not in its world, and the victim's
    /// lets itself go. 1.1's camera
    /// then goes with it when it is the Storyteller's target — built on the windowed host
    /// (`Vitomir.StoryView`, <see cref="Watched"/>). A teleporter's end as the entrance (−2) is
    /// <see cref="CarryThrough"/>.
    /// </summary>
    public bool Carry(WorldThing who, int index, int entrance)
    {
        if (who.Place is not { } from || !ReferenceEquals(who, from.Steered)) return false;
        if (entrance == Teleports.Entrance || Chronicle?.At(index) is not { LevelName.Length: > 0 } || Load is null) return false;
        if (Load(index, 0) is not { } to || ReferenceEquals(to, from)) return false;                    // loaded, or the one already loaded
        if (Location.PickStart(to.AllStarts, (uint)Math.Max(0, entrance), 1) is not { } start) return false;
        Adopt(to, index);                                           // no cancel, no walk stopped: those are the player branch's
        var (at, yaw) = Stand(start);
        WorldThing? made;
        using (CharacterSheets.Moving()) made = to.Place(who.Template, at, new Vector3(0f, 0f, yaw), start.Sector);   // moved, not made: no sheet
        if (made is null) return false;
        MoverMinds.Carried(who, made);                              // the mind as it was, its spots the new place's
        from.Remove(who, drop: false);
        Campaign.Carry(who, made, to);
        Carried = made;
        to.WalkerFor(made)?.Ground();
        server.Note($"net: {who.Template} (thing {who.Guid}) moved alone from {from.Name} to {Chronicle!.NameOf(index)} as thing {made.Guid}");
        Within(to, () => Came?.Invoke(to, made));
        return true;
    }

    /// <summary>
    /// **The Storyteller's Jump to Location**: the Storyteller's current target (in
    /// the seat) must be a character (type 0 or 7) not already in the
    /// chosen location, and goes there at entrance 0 through the mover — a player's alone, a
    /// character nobody plays only when it is the one being steered (<see cref="Carry"/>). The host's own character takes the
    /// chronicle's journey **alone**, as its exit does: the request is remembered as
    /// <see cref="HostAlone"/>, which the windowed host's run reads when it takes it. False when nothing went.
    /// </summary>
    public bool JumpTo(WorldThing who, int index)
    {
        if (who.Type is not (ThingType.Actor or ThingType.Player) || Chronicle?.At(index) is not { } at) return false;
        if (who.Place is { } here && IndexOf(here) == index) return false;
        if (Move(who, index, 0)) return true;
        if (!ReferenceEquals(who, server.Slots[0]?.Avatar) || !Chronicle.Jump(at.Name, 0)) return false;
        HostAlone = Chronicle.Pending;
        return true;
    }

    /// <summary>
    /// The chronicle's journey that is the host's own character moved alone by the mover (for
    /// slot 0) rather than a script's `Jump`, which takes everybody —
    /// set by <see cref="JumpTo"/>. An exit and a teleporter are always the host's alone.
    /// </summary>
    public SequenceRequest? HostAlone { get; private set; }

    /// <summary>
    /// `TeleportPartyToConversation(guid, n)` on a host for a remote player's character (the native's
    /// network branch): that character alone to the first start of conversation group
    /// <paramref name="group"/> in its own place (kind 1) — stood there and
    /// the ground under it found without moving it, **and nothing more**:
    /// the placing stops no walk and cancels no order, unlike the party's move (which cancels
    /// each member's unforced — <see cref="Location.TeleportPartyToConversation"/>). A comparison with 1.1
    /// flagged the outright action clear here; 1.1 cancels nothing there at all, so
    /// Vitomir's walk stop and action clear are gone. False when the character is not a remote
    /// player's (the party's move stands then) or the place has no such start; the snapshot carries the
    /// jump (250 units or more is taken at once on the player's machine).
    /// </summary>
    public bool ToConversation(WorldThing who, uint group, out bool refused)
    {
        refused = false;
        if (server.Players.FirstOrDefault(p => ReferenceEquals(p.Avatar, who)) is not { Host: false }) return false;
        refused = true;
        if (who.Place is not { } place || place.ConversationStart(group, 1) is not { } start) return true;
        refused = false;
        var (at, yaw) = Stand(start);
        who.Orientation = new Vector3(0f, 0f, yaw);
        long was = who.Sector;
        who.Position = at;
        who.Sector = start.Sector;
        place.Props?.Moved(who, was, who.Sector);
        who.Mind?.Walker.Ground(snap: false);
        place.Regions?.Moved(who, was);
        return true;
    }

    // ---- the teleporters' network parts (ITeleportNetwork) ---------------------------------------

    NetPlayer? PlayerOf(WorldThing who) => server.Players.FirstOrDefault(p => ReferenceEquals(p.Avatar, who));

    /// <inheritdoc/>
    public int Order(WorldThing creator) =>
        creator.Type == ThingType.Player && PlayerOf(creator) is { } p ? p.Slot + 1 : 1;

    /// <inheritdoc/>
    public string? Key(WorldThing who) => PlayerOf(who) is { } p ? $"net:{p.Slot}" : null;

    /// <inheritdoc/>
    public WorldThing? Who(string key) =>
        key.StartsWith("net:", StringComparison.Ordinal) && int.TryParse(key.AsSpan(4), out int slot)
            ? server.Players.FirstOrDefault(p => p.Slot == slot)?.Avatar : null;

    /// <inheritdoc/>
    public bool Relay(WorldThing who, int location, int slot, int way)
    {
        if (PlayerOf(who) is not { } p)
        {
            CarryThrough(who, location, slot, way);                  // nobody plays it: the mover's last branch, −2
            return true;                                             // the native answers 1 whatever the mover did
        }
        if (p.Host) return false;                                    // the host's own: its journey
        if (!Move(who, location, Teleports.Entrance)) return false;
        p.Teleport = (slot, way);
        return true;
    }

    /// <summary>
    /// A player's character has just been made at its arrival: one who came through a
    /// teleporter is put beside the end its way leads to (<see cref="Teleports.LandOne"/>).
    /// </summary>
    internal void Arriving(NetPlayer p, WorldThing made)
    {
        if (p.Teleport is { } through && made.Place is { Session.Teleports: { } pairs } place)
        {
            p.Avatar = made;                                        // theirs already, so the pair knows its creator
            pairs.LandOne(made, place, through.Slot, through.Way);
        }
        p.Teleport = null;
    }

    /// <summary>
    /// The starts a player arrives on at an entrance of a place (by the entrance): the
    /// entrance's group, or the place's first start when it has none — whatever entrance the place was
    /// built at, since players arrive at a loaded place by every entrance.
    /// </summary>
    public static NsdStart[] Starts(Location place, int entrance)
    {
        if (entrance < 0) return place.Arrivals.Length > 0 ? place.Arrivals : place.AllStarts;
        var group = place.AllStarts.Where(s => s.Group == entrance).ToArray();
        return group.Length > 0 ? group : place.AllStarts.Length > 0 ? [place.AllStarts[0]] : place.Arrivals;
    }

    /// <summary>A start's position and the way it faces, in degrees about +Z.</summary>
    internal static (Vector3 At, float Yaw) Stand(NsdStart s) =>
        (s.Position, MathF.Atan2(s.Rotation[1], s.Rotation[0]) * (180f / MathF.PI));
}
