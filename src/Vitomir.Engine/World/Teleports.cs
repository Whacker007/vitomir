// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The sixteen teleport slots, and what Walk the Abyss and a click on a portal do
// with them.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Save;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// One of the sixteen slots (0x1c bytes each). The field names are 1.1's own two
/// assert strings: the **in** template is the far end, the **out** template the near one — you go
/// *out* of the place you are in through the near end and come *in* at the far end.
///
/// Where 1.1 keeps a GUID, Vitomir keeps a thing's **save id** (`Save.ThingIds`), which names the
/// same thing across journeys and saves. (Ours, chosen when a GUID was unique only within one place;
/// GUIDs are game-wide as in 1.1, but the party's bodies are still made again at every
/// journey, where 1.1's travel.)
/// </summary>
public sealed class TeleportSlot
{
    /// <summary>The location the far end stands in — the chronicle's `portal` place.</summary>
    public int InLocation { get; set; }
    /// <summary>The far end (1.1: its GUID).</summary>
    public string? InThing { get; set; }
    /// <summary>The location the near end stands in — where the creator was.</summary>
    public int OutLocation { get; set; }
    /// <summary>The near end, beside the creator (1.1: its GUID).</summary>
    public string? OutThing { get; set; }
    /// <summary>The flags the script passed, and <see cref="Teleports.Used"/>.</summary>
    public uint Flags { get; set; }
    /// <summary>
    /// Who made it (1.1: the creator's GUID) — the party key of a party member,
    /// <c>party:&lt;cast id&gt;</c>, which is the same character in every place. **A slot is free
    /// when this is empty** (the click tests this word and nothing else), and freeing one clears only
    /// this: the ends' names stay in the table until the slot is used again, as 1.1's GUIDs do.
    /// </summary>
    public string? Creator { get; set; }
    /// <summary>The game clock the script's expiry comes due at, 0 for never. Nothing reads it.</summary>
    public long ExpireMs { get; set; }

    public bool Free => Creator is null;

    public TeleportSlot Copy() => (TeleportSlot)MemberwiseClone();
    public override string ToString() => Free ? "free" :
        $"{Creator}: out {OutThing}@{OutLocation} <-> in {InThing}@{InLocation}, flags 0x{Flags:x}";
}

/// <summary>
/// **The teleporters**: a table of sixteen two-ended portals and the four
/// things done with it. It is small, and the rest of the engine does the heavy lifting: the ends
/// are ordinary things made from templates, the place the far end goes is the chronicle's
/// `portal` line, and going through one is a journey of the main sequence's own (state 6).
///
/// <list type="bullet">
/// <item>**Making a pair** (<see cref="Create"/>) — `CodexSequence.CreateTeleporter`,
/// which only Walk the Abyss calls: the near end beside the creator, the far end on the chronicle's
/// portal entrance, the far location loaded to put it there.</item>
/// <item>**Using an end** (<see cref="Activate"/>) — `ActivateTeleporter`, which the
/// `Teleporter` script on both ends calls when clicked: a request to the main sequence to take the
/// party through. The way back from the far end closes the pair behind the party.</item>
/// <item>**Going through** (<see cref="Depart"/> and <see cref="Land"/>, the journey's
/// teleport branch and the arrival's): the party carried to
/// the other end's place and stood around the end.</item>
/// <item>**Closing** (<see cref="CloseIn"/>, <see cref="CloseOut"/>, <see cref="Release"/>).</item>
/// </list>
///
/// Held by the <see cref="Session"/>, because 1.1's table is a global that belongs to no place
/// and is written into `!main.nsg` (tag
/// 8); a new chronicle empties it.
/// </summary>
public sealed partial class Teleports(Session session)
{
    static readonly ILogger log = Diag.For<Teleports>();

    /// <summary>`MAX_TELEPORTS` (the close's and the load's asserts).</summary>
    public const int Max = 16;

    /// <summary>
    /// The entrance a teleport arrives at: the journey calls the location's arrival with
    /// **−2**, which stands the party round the teleport rather than on player starts.
    /// </summary>
    public const int Entrance = -2;

    /// <summary>
    /// The creator flags. **1** keeps everything: neither end is ever taken away and the
    /// slot is never freed (the closes test <c>&amp; 3</c>, <c>&amp; 5</c>,
    /// <c>&amp; 1</c>). **2** keeps the far end, **4** the near one. Walk the Abyss passes 0.
    /// </summary>
    public const uint KeepAll = 0x1, KeepIn = 0x2, KeepOut = 0x4;

    /// <summary>
    /// Set on a slot when its far end is used: the way back. The near end goes and the
    /// slot is freed once the party has arrived beside it.
    /// </summary>
    public const uint Used = 0x100;

    /// <summary>`LOCATION_FLAG_NOTELEPORTS` (`Codex.java`): no pair is made in such a place.</summary>
    public const uint NoTeleports = 0x20;

    /// <summary>
    /// The player's number among a network game's players, which picks the far end's start
    /// (the start search's order). **1** in single player; a network player's own number is not
    /// built.
    /// </summary>
    public const int SinglePlayerOrder = 1;

    readonly TeleportSlot[] slots = Enumerable.Range(0, Max).Select(_ => new TeleportSlot()).ToArray();

    /// <summary>The sixteen slots, as they stand.</summary>
    public IReadOnlyList<TeleportSlot> Slots => slots;

    /// <summary>
    /// The campaign, which knows the other places and can build one (<see cref="Campaign.Load"/>).
    /// Without it only the place the party is in can be reached, and no pair can be made unless
    /// its far end is there too.
    /// </summary>
    public Campaign? Campaign { get; set; }

    Chronicle? Chronicle => Campaign?.Chronicle ?? session.Chronicle;
    Location? Here => Campaign?.Here ?? session.Here;

    /// <summary>Empties the table (at a new chronicle, and at start and shut-down).</summary>
    public void Clear()
    {
        foreach (var s in slots) Reset(s);
    }

    static void Reset(TeleportSlot s)
    {
        s.InLocation = 0; s.InThing = null; s.OutLocation = 0; s.OutThing = null;
        s.Flags = 0; s.Creator = null; s.ExpireMs = 0;
    }

    /// <summary>A load's table (the load reads sixteen and asserts there are sixteen).</summary>
    public void Restore(IEnumerable<TeleportSlot> saved)
    {
        Clear();
        int i = 0;
        foreach (var s in saved)
        {
            if (i >= Max) break;
            var into = slots[i++];
            into.InLocation = s.InLocation; into.InThing = s.InThing; into.OutLocation = s.OutLocation;
            into.OutThing = s.OutThing; into.Flags = s.Flags; into.Creator = s.Creator; into.ExpireMs = s.ExpireMs;
        }
    }

    // ---- making a pair ---------------------------------------------------------------------------

    /// <summary>
    /// `CreateTeleporter(creator, inTemplate, outTemplate, expireTime, flags)` past the native's own
    /// checks (a character, both templates known) — **the pair's making**. In order:
    /// <list type="number">
    /// <item>nothing in a location flagged <see cref="NoTeleports"/> — which Walk the
    /// Abyss's own `cancast` has asked already;</item>
    /// <item>**the creator's open pair is closed first** — both ends taken away and the slot freed.
    /// A single-player party member of the player type closes the first pair
    /// made by *any* such party member (`THING_AF_PARTY`) — the party has one portal
    /// between them; anybody else, and anybody at all in a network game, closes only
    /// its own;</item>
    /// <item>the first free slot, or nothing;</item>
    /// <item>**both ends are made first**, the near end then the far end, so
    /// they take the next two numbers and a portal place built fresh below is numbered after them;</item>
    /// <item>the near end is made and put **beside the creator** — `SpawnThingNear`'s placing with flags 3, the same
    /// search `SpawnThingNear` makes, eight directions from the creator's front at the two radii
    /// apart (<see cref="Location.PlaceNear"/>);</item>
    /// <item>the chronicle's portal location is **loaded if it is not** (the location load — behind the
    /// loading plaque in 1.1, not in Vitomir: <see cref="Campaign.Load"/>), and the far end goes on
    /// the start of the portal's entrance whose number is the player's (the start search — the first
    /// unnumbered start in single player) and faces the way it faces;</item>
    /// <item>failing any of that, both ends are destroyed and the answer is false —
    /// which the native does not pass on: it answers true whenever the templates exist.</item>
    /// </list>
    /// Then the slot is filled, the expiry is the clock now plus the script's time (or 0), a prop end
    /// is given its creator's GUID as its owner (<see cref="WorldThing.PropOwner"/>, which only
    /// the network rollover reads, <see cref="PropOwners"/>), and 1.1 writes both locations to their `.nsw` —
    /// which Vitomir does on a host (<see cref="Campaign.File"/>); single player keeps its places in
    /// memory and has no need to.
    /// </summary>
    public bool Create(WorldThing creator, string inTemplate, string outTemplate, uint flags, int expireMs)
    {
        if (creator.Place is not { } here || creator.Removed || Chronicle is not { } chronicle) return false;
        int hereIndex = Network?.IndexOf(here) ?? chronicle.Current;       // a host's: the creator's own place (TeleportsNetwork.cs)
        if (chronicle.At(hereIndex) is not { } hereLoc || (hereLoc.Flags & NoTeleports) != 0) return false;

        // The creator's open pair — or, for a player-type party member, the party's — is shut first.
        string key = KeyOf(creator);
        // The party's one portal is single player's only (the network
        // game's word, zero) — in a network game each player closes their own (a comparison with 1.1).
        int open = Network is null && PartyPlayer(creator)
            ? Array.FindIndex(slots, s => !s.Free && Resolve(s.Creator!) is { } c && PartyPlayer(c))
            : Array.FindIndex(slots, s => s.Creator == key);
        if (open >= 0)
        {
            CloseIn(open, false);
            CloseOut(open, false);
            Release(open, false);
        }

        int slot = Array.FindIndex(slots, s => s.Free);
        if (slot < 0) return false;

        if (here.Make(outTemplate) is not { } near) return false;
        // 1.1 makes both ends (the near end then the far end) before it
        // places either or loads the portal's place, so the far end's number follows the near end's
        // and a place built fresh for it is numbered after both (a comparison with 1.1).
        int farGuid = here.TakeGuidFor(inTemplate);
        here.PlaceNear(near, creator, 0f, NearFlags);

        var (portal, entrance) = chronicle.Portal;
        var far = PlaceAt(portal, load: true);
        var start = far is null ? null : Location.PickStart(far.AllStarts, (uint)entrance, (uint)(Network?.Order(creator) ?? SinglePlayerOrder));
        var farEnd = start is null ? null : far!.Place(inTemplate, start.Position, Facing(start), start.Sector, farGuid);
        if (farEnd is null)
        {
            log.LogWarning("teleport: no far end for {Creator} — portal location {Portal} entrance {Entrance} {Why}",
                           key, portal, entrance, far is null ? "cannot be loaded" : "has no start");
            here.Remove(near, drop: false);
            return false;
        }

        var s = slots[slot];
        s.InLocation = portal;
        s.InThing = ThingIds.Of(far!, farEnd);
        s.OutLocation = hereIndex;
        s.OutThing = ThingIds.Of(here, near);
        s.Creator = key;
        s.ExpireMs = expireMs == 0 ? 0 : here.Milliseconds + expireMs;
        s.Flags = flags;
        // A prop end is given its creator's GUID, which a network game's rollover names.
        foreach (var end in new[] { farEnd, near })
            if (end.Type == ThingType.Prop) end.PropOwner = creator.Guid;
        Campaign?.File(portal);                                                 // both ends' places to their files (a host's, CampaignFiles.cs)
        Campaign?.File(hereIndex);
        log.LogInformation("teleport: slot {Slot} opened by {Creator}: {Near} at {NearAt} in {Here} to {Far} at {FarAt} in {There}",
                           slot, key, near.Template, near.Position, chronicle.NameOf(hereIndex), farEnd.Template,
                           farEnd.Position, chronicle.NameOf(portal));
        return true;
    }

    /// <summary>The placing's flags for the near end and for the arriving party: into the anchor's world (1), no answer wanted (2).</summary>
    const int NearFlags = 3;

    /// <summary>A start's turn about Z, as a thing's orientation (the start faces its own +Y).</summary>
    static Vector3 Facing(NsdStart start) =>
        new(0f, 0f, MathF.Atan2(start.Rotation[1], start.Rotation[0]) * (180f / MathF.PI));

    // ---- using an end ----------------------------------------------------------------------------

    /// <summary>
    /// `ActivateTeleporter(teleporter, player)` past the native's check that the player is a
    /// `THING_TYPE_PLAYER` — **the teleporter's touch**. The slots are searched by their **near**
    /// ends first and their far ends second; a thing that is neither does
    /// nothing.
    /// <list type="bullet">
    /// <item>A near end asks the main sequence to take the party **into the far end**
    /// (with way 1).</item>
    /// <item>A far end is **the way back**, and in single player (or by the creator in a network
    /// game) it closes the pair behind it: the slot is marked <see cref="Used"/>, the far
    /// end is taken away **now**, before the party has gone (unless flags 1 or 2), and the
    /// near end is flagged `THING_FLAG_SAVENEVER` so that its place is not saved with it
    /// on the way through. Then the request, way 0.</item>
    /// </list>
    /// The travel itself is the main sequence's, a frame later.
    /// </summary>
    public bool Activate(int teleporterGuid, WorldThing player)
    {
        if (teleporterGuid == 0 || Chronicle is not { } chronicle) return false;
        var place = player.Place ?? Here;
        int here = place is not null && Network is { } net ? net.IndexOf(place) : chronicle.Current;
        if (place?.Thing(teleporterGuid) is not { } end || ThingIds.Of(place, end) is not { } id) return false;

        int slot = Array.FindIndex(slots, s => s.OutLocation == here && s.OutThing == id);
        if (slot >= 0)
        {
            if (Relayed(player, slot, 1)) return true;                                   // a player's, alone
            chronicle.Teleport(slot, 1, player.Guid, slots[slot].InLocation);
            log.LogInformation("teleport: {Who} goes through slot {Slot} to {There}",
                               player.Template, slot, chronicle.NameOf(slots[slot].InLocation));
            return true;
        }
        slot = Array.FindIndex(slots, s => s.InLocation == here && s.InThing == id);
        if (slot < 0) return false;
        var s = slots[slot];
        // Only the creator's way back closes the pair in a network game.
        if (Network is null || s.Creator == KeyOf(player))
        {
            s.Flags |= Used;
            CloseIn(slot, false);
            if (Find(s.OutLocation, s.OutThing, load: false) is { } nearEnd) nearEnd.Flags |= WorldThing.Flag.SaveNever;
        }
        if (Relayed(player, slot, 0)) return true;
        chronicle.Teleport(slot, 0, player.Guid, s.OutLocation);
        log.LogInformation("teleport: {Who} goes back through slot {Slot} to {There}",
                           player.Template, slot, chronicle.NameOf(s.OutLocation));
        return true;
    }

    // ---- going through ---------------------------------------------------------------------------

    /// <summary>
    /// The first half of state 6 (**the teleport journey**), before anything is torn down: the end the party is
    /// going to — the far one for way 1, the near one for way 0 — in its place, **loaded if it is not**.
    /// If that place cannot be loaded nothing happens. If the end is gone, or out of its
    /// world, the whole pair is shut instead — near end, far end, and the slot freed whatever its
    /// flags (then the release, forced) — and the party stays.
    /// </summary>
    public bool Depart(SequenceRequest asked)
    {
        if ((uint)asked.Slot >= Max) return false;
        var s = slots[asked.Slot];
        int to = asked.Way != 0 ? s.InLocation : s.OutLocation;
        if (PlaceAt(to, load: true) is not { } place) return false;
        if (Find(place, asked.Way != 0 ? s.InThing : s.OutThing) is { InWorld: true }) return true;
        log.LogInformation("teleport: slot {Slot}'s end in {There} is gone; the pair is shut", asked.Slot, to);
        CloseOut(asked.Slot, false);
        CloseIn(asked.Slot, false);
        Release(asked.Slot, true);
        return false;
    }

    /// <summary>
    /// The arrival (**the arrival with entrance −2**), once the party has been carried into
    /// <paramref name="to"/>: every member, in party order, is taken out of the world and put back
    /// **beside the end** — `SpawnThingNear`'s placing with flags 3 on each, the eight-direction search round the end
    /// at the two radii apart, facing out, the first one clear of the level and of everybody placed
    /// before it, and the `drop` noise — and stood on the ground. Then, on the way
    /// back through a used pair, the near end goes (unless flags 1 or 4) and the slot is
    /// freed (unless flag 1).
    ///
    /// Answers where the leader now stands, as a player start for the arrival to stand the camera
    /// on, or null when there was no end to stand beside.
    /// </summary>
    public NsdStart? Land(Location to, SequenceRequest asked)
    {
        if ((uint)asked.Slot >= Max) return null;
        var s = slots[asked.Slot];
        if (Find(to, asked.Way != 0 ? s.InThing : s.OutThing) is not { } end) return null;
        var party = to.PartyMembers().ToList();
        foreach (var member in party) to.Withdraw(member);
        foreach (var member in party)
        {
            to.PlaceNear(member, end, 0f, NearFlags);
            member.Unsurfaced = false;                       // the ground test finds its feet at once
            if (to.Player is { } steered && ReferenceEquals(steered.Thing, member)) { steered.Stop(); steered.Ground(); }
            else if (member.Mind?.Walker is { } walker) { walker.Stop(); walker.Ground(); }
        }
        if (asked.Way == 0 && (s.Flags & Used) != 0)
        {
            CloseOut(asked.Slot, false);
            Release(asked.Slot, false);
        }
        if (party.Count == 0) return null;
        var leader = party[0];
        float yaw = leader.Orientation.Z * (MathF.PI / 180f);
        return new NsdStart((uint)Math.Max(leader.Sector, 0), [MathF.Cos(yaw), MathF.Sin(yaw), 0f, -MathF.Sin(yaw), MathF.Cos(yaw), 0f, 0f, 0f, 1f],
                            leader.Position, unchecked((uint)Entrance), 0);
    }

    // ---- closing ---------------------------------------------------------------------------------

    /// <summary>
    /// **The far end's close**: the far end taken away — unless the slot's flags keep it (1 or 2)
    /// and <paramref name="force"/> is not set. Its place is loaded to do it if it is not,
    /// and written to its `.nsw` after.
    /// </summary>
    public void CloseIn(int slot, bool force)
    {
        var s = slots[slot];
        if (!force && (s.Flags & (KeepAll | KeepIn)) != 0) return;
        if (Find(s.InLocation, s.InThing, load: true) is { } end) { end.Place?.Remove(end); Campaign?.File(s.InLocation); }
    }

    /// <summary>**The near end's close**: the near end the same way — kept by flags 1 or 4.</summary>
    public void CloseOut(int slot, bool force)
    {
        var s = slots[slot];
        if (!force && (s.Flags & (KeepAll | KeepOut)) != 0) return;
        if (Find(s.OutLocation, s.OutThing, load: true) is { } end) { end.Place?.Remove(end); Campaign?.File(s.OutLocation); }
    }

    /// <summary>
    /// **The player's shut-down**: a network player leaving the game has their pairs shut — every slot whose creator
    /// they are has both ends taken away, forced (both closes with 1), and its creator
    /// cleared outright, whatever its flags. The host's leave, drop and kick call it
    /// (`Net.HostPlaces`). Answers how many were shut. (It was first built twice, in two
    /// branches; this is the one name for <see cref="ShutFor"/>.)
    /// </summary>
    public int Shut(WorldThing player) => ShutFor(player);

    /// <summary>**The release**: the slot freed — its creator forgotten — unless flag 1 keeps it.</summary>
    public void Release(int slot, bool force)
    {
        var s = slots[slot];
        if (force || (s.Flags & KeepAll) == 0) s.Creator = null;
    }

    // ---- finding things --------------------------------------------------------------------------

    /// <summary>
    /// A location by index: the one the party is in, one still loaded, or — when
    /// <paramref name="load"/> — one built now (<see cref="Campaign.Load"/>).
    /// </summary>
    Location? PlaceAt(int index, bool load)
    {
        if (Chronicle is { } c && index == c.Current && Here is { } here) return here;
        if (Campaign is not { } campaign) return null;
        return load ? campaign.Load(index) : campaign.Resident.GetValueOrDefault(index);
    }

    WorldThing? Find(int location, string? id, bool load) =>
        id is null || PlaceAt(location, load) is not { } place ? null : Find(place, id);

    static WorldThing? Find(Location place, string? id)
    {
        if (id is null) return null;
        foreach (var t in place.Things)
            if (!t.Removed && ThingIds.Of(place, t) == id) return t;
        return null;
    }

    /// <summary>What a slot calls its creator: a party member by its party key, anybody else by its id here.</summary>
    string KeyOf(WorldThing who) =>
        Network?.Key(who) is { } player ? player
        : who.Actor is { } a && a.Has(Actor.Flag.Party) ? ThingIds.Member(who)
        : who.Place is { } p && ThingIds.Of(p, who) is { } id ? id : $"guid:{who.Guid}";

    /// <summary>A creator, found among the things where the party is (by the creator's GUID).</summary>
    WorldThing? Resolve(string key)
    {
        if (Here is not { } here) return null;
        foreach (var t in here.Things) if (!t.Removed && KeyOf(t) == key) return t;
        return null;
    }

    /// <summary>A `THING_TYPE_PLAYER` in the party (type 7, flag `0x20000000`), as the touch asks.</summary>
    static bool PartyPlayer(WorldThing who) =>
        who.Type == ThingType.Player && who.Actor is { } a && a.Has(Actor.Flag.Party);
}
