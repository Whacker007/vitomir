// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Travelling between the chronicle's locations: the half of the level change that is the engine's.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// The campaign as it stands: which chronicle is running, which of its locations you are in, and
/// how you get from one to the next.
///
/// **This is the half of the level change that belongs to the engine.** `CodexSequence.TakeExit`
/// does not change the level — the main sequence validates the exit, remembers it, and
/// sets the campaign's state machine to 4. Something else notices that state
/// and does the work, and this is that something: build the destination, carry the party into it,
/// and stand them on the entrance the exit named.
///
/// What it deliberately does **not** own is how a <see cref="Location"/> is built — the caller
/// hands that in as <see cref="Build"/>, because the options a harness wants (no sky, no things,
/// a seed, a starting kit) are the harness's business and duplicating them here would put two
/// copies of the same decisions in the tree.
/// </summary>
public sealed partial class Campaign
{
    static readonly ILogger log = Diag.For<Campaign>();

    /// <summary>The chronicle being played; its <see cref="Chronicle.Current"/> is where you are.</summary>
    public required Chronicle Chronicle { get; set; }

    /// <summary>
    /// Builds a location: the level's name, the scene to lay over it, and the entrance to open it
    /// at. Whatever else the caller wants done to a new place — effects, AI, scripts, the starting
    /// kit — it does inside this.
    /// </summary>
    public required Func<string, string?, int, Location> Build { get; init; }

    /// <summary>Where the party is now.</summary>
    public required Location Here { get; set; }

    /// <summary>What a completed journey moved.</summary>
    /// <param name="From">The location left.</param>
    /// <param name="To">The location arrived in.</param>
    /// <param name="Entrance">The entrance stood on.</param>
    /// <param name="Party">How many characters were carried across, the leader included.</param>
    /// <param name="Carried">How many items travelled in their packs.</param>
    /// <param name="Returned">Whether <paramref name="To"/> was still loaded from an earlier visit.</param>
    /// <param name="Restored">Whether <paramref name="To"/> was brought back from a loaded save's record.</param>
    public readonly record struct Arrival(Location From, Location To, int Entrance, int Party, int Carried,
                                          bool Returned = false, bool Restored = false);

    /// <summary>
    /// The places the party has been, still loaded, by location index. **Six at most**, as in the
    /// original: loading a seventh lets go of the one loaded longest ago, whose
    /// record goes to <see cref="Parked"/> to be
    /// read back on the next visit (<c>CampaignUnload.cs</c>).
    /// </summary>
    public IReadOnlyDictionary<int, Location> Resident => resident;
    readonly Dictionary<int, Location> resident = [];

    /// <summary>
    /// The places a loaded save remembers and nobody has gone back to yet, by location index —
    /// 1.1's <c>location_n.nsw</c> files, which it reads only when the party next arrives.
    /// A journey to one builds the place and hands its record to <see cref="Restore"/>
    /// before the party is carried in; a save taken meanwhile writes the record through unchanged.
    /// </summary>
    public Dictionary<int, Save.LocationSave> Parked { get; } = [];

    /// <summary>
    /// Builds a parked place from its record — the level and scene loaded, the record put back
    /// (<see cref="Save.SaveApply.Place"/>), the minds woken after it — and arms its scripts. The
    /// caller's, like <see cref="Build"/>, because it needs the script layer. Null builds a parked
    /// place fresh instead.
    /// </summary>
    public Func<Save.LocationSave, int, Location>? BuildSaved { get; init; }

    /// <summary>
    /// Puts a place back among the loaded ones — what a load does with the place the party is in.
    /// The journey's place load is the location load's leaner twin, called by the journey build for the place the
    /// party is arriving at: it builds the level and scene and marks the location loaded the same
    /// way, without the location load's own read-back-from-file or eviction accounting, which happen here
    /// regardless of which of the two loaded it.
    /// </summary>
    public void Keep(int index, Location place) { resident[index] = place; Loaded(index); }

    /// <summary>A loaded place that is being thrown away — its chronicle ended.</summary>
    public event Action<Location>? Discarded;

    /// <summary>
    /// Whether the journey asked for will **build** its place rather than go back to one still
    /// loaded — which is when 1.1 puts the loading plaque up: the chronicle's location load
    /// calls the load screen for a place with no world, fresh or read back from its `.nsw`,
    /// and it draws `ui\LoadScreen.nui` over whatever was on the screen before it
    /// loads anything. A return to a loaded place shows no plaque.
    /// </summary>
    public bool Builds
    {
        get
        {
            if (Chronicle.Leaving is not { } going || Chronicle.At(going.To) is not { } destination
                || destination.LevelName.Length == 0) return false;
            return (going.To == Chronicle.Current ? Here : resident.GetValueOrDefault(going.To)) is null;   // a loaded place is swapped, not built
        }
    }

    /// <summary>
    /// **A host's own character through the mover** (the mover's player branch for slot 0): on a host
    /// every journey but the advance goes through the mover — an exit, a teleporter,
    /// the Storyteller's jump and a script's or the console's `Jump` (which
    /// calls it for every slot and then slot 0) — and before the place is filed the mover
    /// cancels the character's action **forced, as a release**  and stops
    /// its walk. The advance is the journey state's own and
    /// skips it. Beside the party's own leaving cancel (<see cref="PartyTravel.Leave"/>) this
    /// adds three things: a feed let go as a release, a staked host cancelled too, and the walk of the
    /// body left in the place stopped. Nothing on a single-player campaign (<see cref="Files"/> null).
    /// </summary>
    void HostMoverCancel()
    {
        if (Files is null || Chronicle.Pending is { Kind: SequenceKind.AdvanceGo }) return;
        if (Here.Player?.Thing is not { } host) return;
        Rpg.Actions.Cancel(host, force: true, release: true);
        Rpg.Actions.StopWalking(host);
    }

    /// <summary>
    /// Takes the journey a script has asked for (<see cref="Chronicle.Leaving"/>: an exit, a jump or
    /// the journey after an advance), or answers null when none is pending. On success
    /// <see cref="Here"/> is the new place and the chronicle's current location has moved with it.
    /// </summary>
    public Arrival? Travel()
    {
        if (Chronicle.Leaving is not { } going) return null;
        // A chapter changed this frame is swapped before the journey is taken (the chapter swap, then the journey;
        // ChapterSwap.cs) — the game loop has done it already; a harness that has not has it done here.
        SwapChapters();
        if (!Departing()) return null;                                          // a teleport's end gone (CampaignTeleport.cs)
        HostMoverCancel();
        File(Chronicle.Current);                                                // a host's: the place left, to its file (CampaignFiles.cs)
        if (Chronicle.At(going.To) is not { } destination || destination.LevelName.Length == 0)
        {
            log.LogWarning("travel: location {To} names no level; staying put", going.To);
            Chronicle.Arrive();
            return null;
        }

        var from = Here;
        from.Decals.Clear();                  // every journey ends the scene, and its end clears the marks
        // A possessed body stays behind: its possession ends and the controls go to the
        // party's selected member before anybody moves.
        if (from.EndSteeredPossession() && from.Selected is { } selected) from.SetSteered(selected);
        int fromIndex = Chronicle.Current;
        string? template = from.Player?.Thing.Template;
        var leader = from.Player?.Thing;
        var members = from.Things.Where(t => !ReferenceEquals(t, leader) && t.Actor is { } a && a.Has(Actor.Flag.Party)).ToList();
        string? scene = destination.SceneName.Length > 0 ? destination.SceneName : null;

        // Where the party is going: a place still loaded from an earlier visit (this one included),
        // as it is — **a loaded place is never built again**: a chapter changed since was swapped in
        // it at the end of the frame (ChapterSwap.cs).
        Location to;
        bool returned = false, restored = false;
        var kept = going.To == fromIndex ? from : resident.GetValueOrDefault(going.To);
        if (kept is not null)
        {
            to = kept;
            returned = true;
        }
        else
        {
            // A place a load remembers, or one let go, is brought back as it was left — and swapped to
            // the chapter the chronicle now names if that has changed since (ChapterSwap.cs).
            LoadScreen?.Invoke(true);                                          // The players' load screen (CampaignLoadScreen.cs)
            from.Guids.Arriving = leader?.Guid ?? 0;                           // the leader's body keeps its GUID (ThingGuids)
            using var counting = ThingGuids.Use(from.Guids);                   // and the place is numbered from the game's counter
            try
            {
                if (Parked.Remove(going.To, out var record) && BuildSaved is not null)
                {
                    from.Guids.Loading = true;                                   // the file's numbers, not the counter's
                    try { to = BuildSaved(Save.AwayTimers.Since(record, from.Milliseconds), going.Entrance); }   // on the game's clock
                    finally { from.Guids.Loading = false; }
                    ReadBack(to, scene);
                    restored = true;
                }
                else to = Build(destination.LevelName, scene, going.Entrance);
            }
            finally { from.Guids.Arriving = 0; from.Guids.Loading = false; LoadScreen?.Invoke(false); }
        }

        // The party leaves: out of the world it is leaving, so a place kept loaded keeps
        // nobody who has gone. A companion's thing was made for that visit and does not come back;
        // the leader's stays with the place for when the party returns.
        // Each member's torch off the hand and out of the world first (Rpg.Utility).
        // And each one's order cancelled, forced, unless staked (PartyTravel).
        foreach (var member in members) { Rpg.Utility.Leave(member); PartyTravel.Leave(member); from.Withdraw(member); }
        if (leader is not null) { Rpg.Utility.Leave(leader); PartyTravel.Leave(leader); }
        if (!ReferenceEquals(to, from) && leader is not null) from.Withdraw(leader);
        // And every member's pets are let go (Ai/Pets.cs).
        from.Minds?.PartyLeft(leader is null ? members : [leader, .. members]);
        if (returned) to.Reenter(going.Entrance);

        int party = 0, carried = 0;
        if (leader is not null && to.Player?.Thing is { } arrived)
        {
            if (!ReferenceEquals(leader, arrived)) carried += Carry(leader, arrived, to);
            party++;
            // The rest of the party. Their *things* belong to the level being left, so each one is
            // rebuilt from its own template in the new place and given its character back — which
            // is what the original does the long way round, by writing the party into `!main.nsg`
            // and reading it out again at the other end.
            int slot = 1;
            foreach (var member in members)
            {
                var spot = PartyTravel.Arrival(to, slot, arrived);                // no start: Repairs.MemberStart
                if (to.Place(member.Template, spot, arrived.Orientation, arrived.Sector, member.Guid) is not { } rebuilt)
                {
                    log.LogWarning("travel: {Who} could not be rebuilt in {Where}", member.Template, to.Name);
                    continue;
                }
                carried += Carry(member, rebuilt, to);
                party++;
                slot++;
            }
        }
        else if (template is not null)
            log.LogWarning("travel: {Where} has no player thing for {Template}", to.Name, template);
        Landing(to, from.Session);                                              // round a teleport's end (CampaignTeleport.cs)
        // And back on the hand in the new world, before any script hears the scene begin.
        RejoinUtilities(to);

        Chronicle.Arrive();
        if (!ReferenceEquals(to, from) && fromIndex >= 0)
        {
            if (fromIndex == going.To) Discarded?.Invoke(from);                  // rebuilt under the party's feet
            else resident[fromIndex] = from;
        }
        resident[going.To] = to;
        Here = to;
        if (!returned) Loaded(going.To, fromIndex);               // and a seventh lets one go
        log.LogInformation("travel: {From} -> {To} by exit {Exit}, entrance {Entrance}; " +
                           "{Party} carried across with {Carried} items{Returned}",
                           from.Name, to.Name, going.Exit, going.Entrance, party, carried,
                           returned ? ", a place still loaded" : "");
        return new Arrival(from, to, going.Entrance, party, carried, returned, restored);
    }

    /// <summary>
    /// A new chronicle (state 7). Every loaded place of the old one is
    /// thrown away, the quests with it (the caller's), and the party arrives at the new
    /// chronicle's first location, entrance 0.
    /// </summary>
    public Arrival? ChangeChronicle(Chronicle next)
    {
        var from = Here;
        foreach (var (_, place) in resident)
            if (!ReferenceEquals(place, from)) Discarded?.Invoke(place);
        resident.Clear();
        Parked.Clear();
        Files?.Clear();                                                         // ours: the old chronicle's files (CampaignFiles.cs)
        Chronicle = next;
        next.Current = -1;
        if (next.At(0) is not { } start || start.LevelName.Length == 0) return null;
        from.Guids.Arriving = from.Player?.Thing.Guid ?? 0;                     // one counter across chronicles too (ThingGuids)
        if (from.Files is { } gameFiles) from.Guids.Pending.AddRange(Preloads.Of(next, gameFiles));   // its early places' numbers (Preloads.cs)
        Location to;
        try
        {
            using var counting = ThingGuids.Use(from.Guids);
            to = Build(start.LevelName, start.SceneName.Length > 0 ? start.SceneName : null, 0);
        }
        finally { from.Guids.Arriving = 0; }
        int party = 0, carried = 0;
        var leaving = from.Things.Where(t => t.Actor is { } a && a.Has(Actor.Flag.Party)).ToList();
        var leader = from.Player?.Thing;
        if (leader is not null && to.Player?.Thing is { } arrived)
        {
            carried += Carry(leader, arrived, to);
            party++;
            int slot = 1;
            foreach (var member in leaving.Where(t => !ReferenceEquals(t, leader)))
            {
                var spot = PartyTravel.Arrival(to, slot, arrived);                // no start: Repairs.MemberStart
                if (to.Place(member.Template, spot, arrived.Orientation, arrived.Sector, member.Guid) is not { } rebuilt) continue;
                carried += Carry(member, rebuilt, to);
                party++;
                slot++;
            }
        }
        RejoinUtilities(to);
        Preload(0);                                                             // its early places, before the first is entered (CampaignPreloads.cs)
        Discarded?.Invoke(from);
        next.Current = 0;
        start.Flags |= ShowInMap;                                               // the arrival's tail
        resident[0] = to;
        Here = to;
        Loaded(0);
        return new Arrival(from, to, 0, party, carried);
    }

    /// <summary>
    /// The party's arrival, its last loop: every party member — the leader first — holding a utility item has it
    /// set again, on the hand in the world just entered (<see cref="Rpg.Utility"/>).
    /// </summary>
    static void RejoinUtilities(Location to)
    {
        var leader = to.Player?.Thing;
        if (leader is not null) Rpg.Utility.Rejoin(leader);
        foreach (var t in to.Things.ToArray())
            if (!ReferenceEquals(t, leader) && t.Actor is { } a && a.Has(Actor.Flag.Party)) Rpg.Utility.Rejoin(t);
    }

    /// <summary>
    /// Moves one character from the place being left into the place being entered: **the whole
    /// <see cref="Actor"/> object**, rather than a field-by-field copy, because a copy that misses
    /// a field loses something quietly and this cannot. The new thing's own character — freshly
    /// built from the same template — is discarded.
    ///
    /// Everything the character is carrying needs a body in the new world, because that body is
    /// what a put-down turns back into a thing on the floor. Each one is rebuilt from its own
    /// template with <see cref="Location.Make"/>, which registers it without putting it in the
    /// world, **under the GUID it had**: in 1.1 the item is the same thing wherever it is carried,
    /// and one counter numbers the whole game (<see cref="ThingGuids"/>).
    /// </summary>
    internal static int Carry(WorldThing was, WorldThing now, Location into)
    {
        if (was.Actor is not { } who) return 0;
        who.Target = null;                       // whatever it was fighting is in the other level
        // The fresh body's own effects go with its discarded character. They are the ones it was born
        // with as the place woke (`Location.Awaken`), and in 1.1 nothing is born again at a door — the
        // thing itself travels, GUID and all: a new game has Christof as guid 5
        // with the same effects through three places, where Vitomir's Christof got back the
        // `ef_decreasefrenzy` the convent's opening had taken off him at the first one.
        Rpg.EffectRuntime.RemoveAll(now, worn: true);
        now.Actor = who;
        now.Flags |= was.Flags & (WorldThing.Flag.Secret | WorldThing.Flag.TopSecret);
        // What it has on and what it knows go with it: the effect list is the thing's own,
        // and in the original the thing itself makes the journey. Each script
        // object is told whose it now is, so `GetClassThing` answers the new body.
        now.Effects.TakeAll(was.Effects);
        var scripts = was.Place?.ScriptObjects ?? into.ScriptObjects;
        foreach (var e in now.Effects.All) if (e.Script is { } s) scripts?.Rehome(s, now);
        foreach (var d in who.Disciplines.All)
        {
            d.Owner = now;
            if (d.Script is { } s) scripts?.Rehome(s, now);
        }
        now.Foley = was.Foley;                   // a SetFoley (the Embrace's, at OLDT_7_4) is the thing's own and saved with it
        // So is a SetModel — the Convent's morning puts Christof in `christof_human.nod` and then
        // jumps to the next day, and the model is in the thing's saved record.
        if (!string.Equals(was.Model, now.Model, StringComparison.OrdinalIgnoreCase)) into.SetModel(now, was.Model);
        Rpg.ShapeShift.Carried(was, now, into);  // and a shape change's motion set (the record is the actor's)

        int carried = 0;
        foreach (var item in who.Inventory.Items.Concat(who.Held is { } hand ? [hand] : []).Distinct())
        {
            string template = item.Thing?.Template ?? item.Name;
            if (template.Length == 0 || into.Make(template, item.Thing?.Guid ?? 0) is not { } body) { item.Thing = null; continue; }
            body.Item = item;
            // The one on the pointer is **held**, not owned: its holder is the character and it has no
            // owner (the hold takes it out of the pack and writes the owner, `Ground.Hand`), so a
            // put-down after the journey takes the hand's arm, not the pack's (no `remove` sound).
            if (ReferenceEquals(item, who.Held)) body.Holder = now; else body.Owner = now;
            item.Thing = body;
            carried++;
        }
        return carried;
    }
}
