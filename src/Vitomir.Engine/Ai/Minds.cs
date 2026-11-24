// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Everybody's mind, ticked the way the engine ticks them.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ai;

/// <summary>
/// MSVC's <c>rand()</c>, which is the engine's only source of chance: a 32-bit linear congruential
/// generator whose top bits are the answer. Reproduced exactly so a run can
/// be repeated, and so a test can say which spot a character will pick.
/// </summary>
public sealed class Rng(int seed = 1)
{
    public const int Max = 0x7fff;
    /// <summary>What the engine divides a <c>rand()</c> by to get 0..1. Note: not 32768.</summary>
    public const float Scale = 1f / 32767f;

    uint state = (uint)seed;

    /// <summary>How many numbers it has handed out — for a test to see whether a path rolled (ours).</summary>
    public long Draws { get; private set; }

    public int Next()
    {
        Draws++;
        state = state * 214013 + 2531011;
        return (int)((state >> 16) & Max);
    }

    /// <summary>The engine's <c>rand() * (1/32767)</c>, which reaches 1.0 exactly.</summary>
    public float Unit() => Next() * Scale;
}

/// <summary>
/// The AI of a place. It attaches a <see cref="Mind"/> to every character whose template names an
/// AI file, and gives each one its turn on the engine's schedule.
///
/// The engine runs the AI from the actor's own update: every
/// character, every frame, but each one only acts when its own clock says so. So this is a tick,
/// not a loop over a queue, and a character that is walking simply does not get a new order.
/// </summary>
public sealed partial class Minds
{
    readonly ILogger log = Diag.For<Minds>();
    readonly HashSet<AiMode> announced = [];

    /// <summary>
    /// Every mind in the place. **An array, replaced rather than grown**: `Awaken` builds it, and a
    /// character that enters the world afterwards (<see cref="Adopt"/>) joins at the start of the
    /// next <see cref="Tick"/>, never in the middle of one — several walks of this run per frame and
    /// a callback inside one can spawn, which is the shape of fault that crashed
    /// the simulation's animation walk. A walk that is already running keeps the array it
    /// started with.
    /// </summary>
    public Mind[] All { get; private set; }
    readonly List<Mind> joining = [];
    readonly AiClasses classes;
    public InterestSpots Spots { get; }
    public Rng Random { get; }
    /// <summary>The engine's millisecond clock.</summary>
    public int Now { get; private set; }

    /// <summary>The thing the player is steering. Its AI only runs in the four modes that take over.</summary>
    public WorldThing? Steered { get; set; }

    /// <summary>
    /// The party's selected member (`rpgParty+4`) — the same as <see cref="Steered"/> except while a
    /// possession lasts (<see cref="World.Location.Selected"/>). The party follows this one.
    /// </summary>
    public WorldThing? Selected { get; set; }

    /// <summary>Whether the AI is allowed to run at all — the engine's own debug switches.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// `AIMAIN_FLAG_STOPPEDS`, bit 0x2000 of the AI's main flags, which parks every wanderer where it
    /// stands — the main flags themselves, as 1.1 reads them (a switch of its own until then).
    /// </summary>
    public bool WanderingOff
    {
        get => (MainFlags & AiMainFlag.StopPeds) != 0;
        set => MainFlags = value ? MainFlags | AiMainFlag.StopPeds : MainFlags & ~AiMainFlag.StopPeds;
    }
    /// <summary>`AIMAIN_FLAG_STOPFLYPEDS`, bit 0x4000, the same for fliers.</summary>
    public bool FlyingOff
    {
        get => (MainFlags & AiMainFlag.StopFlyPeds) != 0;
        set => MainFlags = value ? MainFlags | AiMainFlag.StopFlyPeds : MainFlags & ~AiMainFlag.StopFlyPeds;
    }

    double carry;
    readonly World.WalkMap? walk;

    /// <summary>The modes with no handler in the engine at all: a character in one does nothing.</summary>
    public static bool Idle(AiMode mode) =>
        mode is AiMode.None or AiMode.Asleep or AiMode.Neutral or AiMode.DoNothing;

    public Minds(Location place, AiClasses classes, int seed = 1)
    {
        Random = Chance.Game?.Ai ?? new Rng(seed);      // the launch's stream in a game (Chance)
        where = place;
        // The spots belong to the place, not to the chapter: all 15 scenes that have any are
        // level scenes, and no chapter scene has one. The engine keeps them on the world, which
        // the level's own scene fills and the chapter scene is then laid on top of.
        Spots = new InterestSpots(place.WorldScene ?? place.Scene, place.Walk);
        this.classes = classes;
        motions = place.Motions;
        var minds = new List<Mind>();
        foreach (var thing in place.Things)
            if (Make(thing) is { } mind) minds.Add(mind);
        All = [.. minds];
        walk = place.Walk;
        Everyone = place.Things;
        foreach (var m in All) minded.Add(m.Thing);
        Steered = Selected = place.Player?.Thing;
        log.LogInformation("AI: {Minds} minds, {Spots} interest spots in {Groups} groups",
                           All.Length, Spots.All.Length, Spots.Groups);
    }

    /// <summary>
    /// One character's mind, made the way the engine's own set-up makes it: a walker (the player's
    /// own, for the thing the player steers), the AI file's class, the starting mode, and a START
    /// HIDDEN character lying down. Null for anything that is not a character with an AI file.
    /// </summary>
    Mind? Make(WorldThing thing)
    {
        if (thing.Actor is not { } actor || actor.AiFile.Length == 0) return null;
        if (classes.Get(actor.AiFile) is not { } ai) return null;
        var place = where!;
        // The player's own thing gets a mind too: it is what Frenzy and Mesmerize drive.
        // A thing a script has already walked has a walker of its own (`Location.WalkerFor`); the
        // mind takes that one over rather than making a second one to fight it.
        var walker = ReferenceEquals(place.Player?.Thing, thing) && place.Player is { } p ? p
            : thing.Walker
            ?? new Player
            {
                Thing = thing, Map = place.Walk!, Motions = place.Motions,
                Solid = place.Solid, Props = place.Props, Path = place.Paths,
            };
        if (thing.Walker is { } taken)
        {
            place.ScriptWalkers.Remove(taken);
            thing.Walker = null;
        }
        if (!ReferenceEquals(walker, place.Player)) walker.Settle();
        var mind = new Mind { Thing = thing, Class = ai, Walker = walker, Following = PetsFollow };
        walker.Jammed = () => Jammed(mind);
        // The engine hangs the mind off the character as well as the other way
        // round; the corpse rules and the revive both read it from there.
        thing.Mind = mind;
        mind.Enter(AiClasses.StartMode(thing.Type == ThingType.Player, ai.BehaviorClass));
        mind.Settle(Spots, place.Walk);
        // The last thing the engine's mind set-up does: a START HIDDEN character
        // lies down. See `MotionHidden`.
        if (actor.Has(World.Actor.Flag.StartHidden)) Hide(thing, place.Motions);
        return mind;
    }

    /// <summary>
    /// A character that entered the world after the place woke up — a party member carried in from
    /// the last location, anything a script spawns — gets its mind the way everybody else did. The
    /// engine makes a thing's AI when it makes the thing, so a late arrival is not a special case
    /// there; here it has to be asked for, and without it a companion rebuilt at the far end of an
    /// exit stood where it was put and never followed (*"I went outside the
    /// Monastery, and noticed that Wilhem stopped following"*). It joins <see cref="All"/> at the
    /// start of the next tick.
    /// </summary>
    public Mind? Adopt(WorldThing thing)
    {
        if (thing.Mind is not null || minded.Contains(thing)) return thing.Mind;
        if (Make(thing) is not { } mind) return null;
        joining.Add(mind);
        minded.Add(thing);
        if (Powers is not null) Learn(mind);
        return mind;
    }

    /// <summary>
    /// The disciplines an AI file gives its character, as the mind's set-up gives them:
    /// each `discipline` line is set at its level (<see cref="Rpg.DisciplineBook.SetLevel"/>, which
    /// also opens the next of its group at −1). Then — **for a character that is not player-type
    /// only** — the first six it knows at a real level, newest first as the list runs, go into the
    /// quick slots 0, 1, 2… in that order, and the first of them is selected. A player-type
    /// character keeps the slots it has.
    /// </summary>
    public void Learn(Mind mind)
    {
        if (Powers is not { } powers || mind.Thing.Actor is not { } me) return;
        foreach (var line in mind.Class.Disciplines)
            Rpg.DisciplineBook.SetLevel(mind.Thing, powers, line.Name, line.Level);
        if (me.IsPlayerThing) return;
        int slot = 0;
        Rpg.Discipline? first = null;
        foreach (var d in me.Disciplines.All)
        {
            if (slot >= Rpg.DisciplineList.QuickSlots) break;
            if (d.Level == -1) continue;
            d.QuickSlot = slot++;
            first ??= d;
        }
        if (first is not null) Rpg.DisciplineBook.Select(mind.Thing, first);
    }

    /// <summary>
    /// Puts the AI clock at the game's time, on arrival (<see cref="World.Simulation.Resume"/>). It
    /// only goes forward: a place visited before simply finds everything it was waiting for due.
    /// </summary>
    public void Resume(int now)
    {
        if (now > Now) Now = now;
    }

    /// <summary>
    /// Advances the clock and gives everybody whose turn it is a think, all at once — for a caller
    /// with no simulation. <see cref="World.Simulation"/> splits it: <see cref="Begin"/> before the
    /// world's walk, <see cref="ThinkOne"/> in each character's own tick,
    /// <see cref="Finish"/> after it (<c>World/ThingTick.cs</c>).
    /// </summary>
    public void Tick(float seconds)
    {
        if (!Begin(seconds)) return;
        foreach (var mind in All) Think(mind);
        Finish();
    }

    /// <summary>The frame's AI clock and the minds that joined since the last; false when the AI is off.</summary>
    public bool Begin(float seconds)
    {
        carry += seconds * 1000.0;
        int step = (int)carry;
        carry -= step;
        Now += step;
        if (joining.Count > 0) { All = [.. All, .. joining]; joining.Clear(); }
        return Enabled;
    }

    /// <summary>One character's think, from its own tick.</summary>
    public void ThinkOne(Mind mind) => Think(mind);

    /// <summary>The frame's end of the AI: the oscillation check (ours).</summary>
    public void Finish() => Flapping();

    /// <summary>
    /// How often a mind may take a target after dropping one before it is a loop rather than a fight.
    /// A character that re-takes a target this many times inside <see cref="FlapWindow"/> is not
    /// changing its mind, it is being handed one back by a search that will not refuse it and dropped
    /// by a mode that will not keep it. Only a take with no target before it counts
    /// (<see cref="Mind.Retakes"/>): a target handed over for another is not a drop, and 1.1's rally
    /// does that to the selected member once for every listener of every event-9 shout — a steered
    /// Christof under two attackers with three characters in earshot is handed about eleven targets a
    /// second, in 1.1 as here (the story sweep's TEU1_22_1, TEU3_22_1 and CNVT_7_1 until then).
    /// </summary>
    public const int FlapLimit = 10, FlapWindow = 1000;

    /// <summary>
    /// Says once, per character, when its targeting is oscillating.
    ///
    /// **This exists because the only way the last one was found was a 60-frames-a-second video.**
    /// Two unarmed cast members in the University take against a party member, the enemy mode drops
    /// the target for want of anything to fight with, and the enemy search hands it straight back —
    /// every frame, because letting a target go schedules the next think immediately. Nothing in a
    /// log, a journal or a still said so; the overlay dutifully reported it sixty times a second
    /// and it read as flicker. A count over a second is the cheapest possible detector for the
    /// whole class, and it costs one comparison per mind per second.
    /// </summary>
    void Flapping()
    {
        foreach (var mind in All)
        {
            if (mind.Flapped) continue;
            if (mind.SampledAt == 0) { mind.SampledAt = Now; mind.SampledRetakes = mind.Retakes; continue; }
            if (Now - mind.SampledAt < Rules.GameRules.Current.Ai.TargetFlapLimit[1]) continue;
            int took = mind.Retakes - mind.SampledRetakes;
            mind.SampledAt = Now;
            mind.SampledRetakes = mind.Retakes;
            if (took < Rules.GameRules.Current.Ai.TargetFlapLimit[0]) continue;
            mind.Flapped = true;
            log.LogWarning("AI: {Name} took a target after dropping one {Took} times in a second and is oscillating between "
                           + "{Mode} and its search — something is handing back a target that "
                           + "{Mode} will not keep", Watch.Name(mind.Thing), took, mind.Mode, mind.Mode);
        }
    }

    /// <summary>
    /// The engine's gate, in its own order. Everything here stops the think
    /// rather than delaying it, so a character that fails the gate simply thinks again next frame.
    /// </summary>
    void Think(Mind mind)
    {
        var actor = mind.Thing.Actor;
        if (actor is null) return;
        // A thing that has left the world stops. The engine frees the AI record with the thing;
        // here the mind stays in the array and simply never runs again, which is the same
        // behaviour with none of the lifetime. A character that arrives after the place woke — a
        // spawn, a companion carried through an exit — gets its mind from `Adopt`.
        if (mind.Thing.Removed) return;
        // Before any gate the think clears the mind's per-think flags whole, and its lowest bit is what
        // says the heading to the target has been worked out. So the afraid and dazed
        // modes, which ask for the heading every think, get it fresh every think: a frightened
        // character runs from where its target *is*, not from where it stood when the fear began
        //
        mind.HeadingKnown = false;
        // The first thing the think does, before any gate: while the home node is 0,
        // take the home from the spawn transform and look for the ground under it.
        mind.LookUpHome(where?.Walk);
        // Dead characters are out.
        if (actor.Has(World.Actor.Flag.Dead)) return;
        // **And anybody in a conversation.** `THING_AF2_CONVERSATION` is tested here, second after
        // death, and it is the whole of why a conversation freezes a room: a character
        // talking to you does not think at all — does not wander, does not notice, does not fight.
        // `Rpg.Conversation.Begin` is what sets it and `End` what takes it off.
        if ((actor.Flags2 & World.Actor.Flag2.Conversation) != 0) return;

        // The idle flourish comes next, before every other gate — so the character the player is
        // steering has them too, and so does one a script has paused.
        Flourish(mind, actor);

        // The thing the player is steering does not think — and neither does a possessed body,
        // steered or not (the mind's 0x10000000 is tested beside the steered test) — unless it
        // is in one of the four modes that take the controls away, or is a pet (bit 0x4).
        bool steered = ReferenceEquals(mind.Thing, Steered) || (mind.Flags & Mind.FlagPossessed) != 0;
        bool overridden = mind.Mode is AiMode.Frenzied or AiMode.Mesmerized or AiMode.Dazed or AiMode.Afraid
                          || (mind.Flags & FlagAlwaysThink) != 0;
        if (steered && !overridden) return;
        // A character a script has taken over (THING_AF_AIPAUSED).
        if (actor.Has(World.Actor.Flag.AiPaused)) return;

        // **The script's off switch.** `Codex.AIOff()` is `AISetMainFlags(AIMAIN_FLAG_PAUSED)`, and
        // the gate tests the low byte of the main flags against 5 (`(byte)flags & 5`) —
        // bit 1, PAUSED, and bit 4, which `Codex.java` does not name. Every conversation script
        // in the game brackets itself with AIOff/AIOn, so this is what stops the room while
        // people talk. A character already walking finishes its step: only the thinking stops.
        if ((MainFlags & (AiMainFlag.Paused | AiMainFlag.Unnamed4)) != 0) return;

        if (mind.NextThink > Now) return;
        Dispatch(mind);
    }

    /// <summary>The AI record's bit 2: think even while a human is steering.</summary>
    public const uint FlagAlwaysThink = 0x4;

    /// <summary>
    /// How an idle flourish comes about: every <see cref="IdleRetry"/> a standing
    /// character rolls <c>rand() × (1/32767)</c>, and under <see cref="IdleChance"/> plays its idle
    /// and waits <see cref="IdleRest"/> before the next roll. Busy, restrained or moving, it only
    /// waits the four seconds.
    /// </summary>
    public const int IdleRetry = 4000, IdleRest = 30_000;
    public const float IdleChance = 0.2f;

    /// <summary>
    /// The idle flourish, the think's second block — every standing character with a mind has one
    /// now and then: a look round, a stretch, a horse shaking its head. As in 1.1,
    /// each gap
    /// between one and the next on the same character is thirty seconds and a whole number of
    /// four-second rolls, to within a tenth of a second.
    /// </summary>
    void Flourish(Mind mind, World.Actor actor)
    {
        if (mind.NextIdle >= Now) return;
        bool busy = actor.BusyUntil > Now || mind.BusyUntil > Now
                    || (actor.Flags2 & World.Actor.Flag2.Restrained) != 0 || mind.Walker.Moving;
        if (!busy && Random.Unit() < Rules.GameRules.Current.Ai.Idle[1])
        {
            mind.Walker.Fidget();
            mind.NextIdle = Now + (int)Rules.GameRules.Current.Ai.Idle[2];
            return;
        }
        mind.NextIdle = Now + (int)Rules.GameRules.Current.Ai.Idle[0];
    }

    void Dispatch(Mind mind)
    {
        switch (mind.Mode)
        {
            case AiMode.PedWander: Wander(mind); break;
            case AiMode.PedFly: Fly(mind); break;
            case AiMode.EnemySearch: Search(mind); break;
            case AiMode.Enemy: Enemy(mind); break;
            case AiMode.Approach: Approaching(mind); break;
            case AiMode.Party: if (PartyFlagged(mind)) PartyMember(mind); break;
            case AiMode.Frenzied: Frenzied(mind); break;
            case AiMode.Mesmerized: Mesmerized(mind); break;
            case AiMode.Dazed: Dazed(mind); break;
            case AiMode.Afraid: Afraid(mind); break;
            case AiMode.Majesty: Majestic(mind); break;
            case AiMode.Possessing: Possessing(mind); break;
            case AiMode.Pet: PetTurn(mind); break;
            default:
                if (Idle(mind.Mode)) { mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.Idle[3]; break; }
                // Everything else is decoded but not written yet. Say so once and stand still,
                // rather than inventing behaviour that is not the original's.
                if (announced.Add(mind.Mode))
                    log.LogInformation("AI mode {Mode} ({Number}) is not implemented yet; {Name} waits",
                                       AiModes.Name(mind.Mode), (int)mind.Mode, mind.Thing.Template);
                mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.Idle[3];
                break;
        }
    }

    /// <summary>How long a character with nothing to do waits before asking again.</summary>
    public const int IdleThink = 1000;

    // --- pedwander ------------------------------------------------------

    /// <summary>The spread of the re-think delay, and the floor under it.</summary>
    public const int WanderSpread = 5000, WanderHurriedSpread = 1000, WanderFloor = 1000;
    /// <summary>How long it waits after refusing a spot for being too far from home.</summary>
    public const int OutOfRangeWait = 500;
    /// <summary>The <c>mindflags</c> bit that makes a wanderer hurry — it runs, and waits less. No shipped file sets it.</summary>
    public const uint MindHurry = 0x1000;

    void Wander(Mind mind)
    {
        // Parked: whatever it is doing cancelled, unforced, its walk stopped, a second.
        if (WanderingOff) { Rpg.Actions.Cancel(mind.Thing); mind.Walker.Stop(); mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.Idle[3]; return; }

        bool hurried = (mind.Class.MindFlags & MindHurry) != 0;
        if (Ready(mind) && mind.SpotCount > 0 && Spots.All.Length > 0)
        {
            var spot = PickSpot(mind);
            // wanderRadius is measured from where the character came into the world — the mode
            // reads the spawn transform's translation directly.
            if (mind.Class.WanderRadius != 0f
                && Vector3.Distance(mind.Thing.SpawnPosition, spot.Position) > mind.Class.WanderRadius)
            {
                mind.Refused();
                mind.NextThink = Now + OutOfRangeWait;
                return;
            }
            MoveTo(mind, spot, run: hurried);                          // 0x1000 runs (0x10), else walks (0x20)
        }
        mind.NextThink = Now + (int)(Random.Unit() * (hurried ? (int)Rules.GameRules.Current.Ai.Wander[1] : (int)Rules.GameRules.Current.Ai.Wander[0])) + (int)Rules.GameRules.Current.Ai.Wander[2];
    }

    // --- pedfly ---------------------------------------------------------

    /// <summary>How far above its feet a flier's sight line runs.</summary>
    public const float FlyEyeHeight = 50f;
    /// <summary>The delay's spread, subtracted from a second (−15000).</summary>
    public const int FlySpread = 15000, FlyFloor = 1000;

    void Fly(Mind mind)
    {
        if (FlyingOff) { Rpg.Actions.Cancel(mind.Thing); mind.Walker.Stop(); mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.Idle[3]; return; }

        if (Ready(mind) && Spots.All.Length > 0)
        {
            var spot = PickSpot(mind);
            // A flier goes straight there, so it has to be able to see the place first. The engine
            // traces both ways and accepts either, because the trace is one-sided.
            if (!Sighted(mind, spot))
            {
                mind.Refused();
                mind.NextThink = Now + OutOfRangeWait;
                return;
            }
            MoveTo(mind, spot, run: true, fly: true);
        }
        mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.Wander[4] + (int)(Random.Unit() * (int)Rules.GameRules.Current.Ai.Wander[3]);
    }

    /// <summary>
    /// How much of the way to the spot a flier's sight line is traced.
    /// </summary>
    public const float FlyReach = 0.975f;

    /// <summary>The level the sight line is traced through.</summary>
    public Formats.NilLevel? Level { get; init; }

    /// <summary>
    /// **Whether a flier can see the perch it has picked — as the engine asks it, fault and all.**
    /// Both eyes are 50 above their feet, each in the sector found from its own hint (the flier's
    /// sector; the spot's), and the trace (<see cref="World.Collision.Clear"/>) is run from
    /// each end with a reach of 0.975 of the distance between them.
    ///
    /// What the engine passes as the ray's **direction is the other end's position** (a quirk of the original, kept: it takes
    /// the address of the spot's point, and forwards it as the line test's third argument, which is
    /// used only as a direction — and the reverse call passes the flier's). So the ray does not run
    /// towards the perch at all: it runs from the eye along the perch's coordinates as a vector from
    /// the level's origin, much further than the gap between them. Out of doors that mostly means
    /// "into the sky", and whether it meets a sky face decides the flight.
    ///
    /// A crow's flights in 1.1 (Old Town by day and at
    /// night, Petrin Hill) are the ones this reading allows; a trace towards the
    /// perch, the engine's or Vitomir's old one against the whole level, refuses some of them. On Petrin
    /// Hill it is what keeps the crow off its perch on the ground by the path (1056, −920, 152), which
    /// 1.1's crow never visits and Vitomir's took one flight in four. One flight it refuses
    /// (Petrin Hill, from (−864, −24) to (736, −280)) is open: both traces meet the sky.
    /// </summary>
    public bool Sighted(Mind mind, InterestSpot spot)
    {
        if (where?.Solid is not { } solid) return true;
        var eye = mind.Thing.Position with { Z = mind.Thing.Position.Z + Rules.GameRules.Current.Ai.Wander[5] };
        var there = spot.Position with { Z = spot.Position.Z + Rules.GameRules.Current.Ai.Wander[5] };
        float reach = Vector3.Distance(eye, there) * Rules.GameRules.Current.Ai.Wander[6];
        int from = (int)where.At(eye, mind.Thing.Sector);
        int to = (int)where.At(there, where.At(spot.Position, -1));
        return solid.Clear(eye, from, there, reach) || solid.Clear(there, to, eye, reach);
    }

    // --- what both of them share ---------------------------------------------------------------

    /// <summary>
    /// Whether a ped may be given a new place to go: it must be standing still, off its busy timer,
    /// alive, and somewhere with interest spots.
    /// </summary>
    bool Ready(Mind mind) => !mind.Moving && mind.BusyUntil <= Now && !mind.Paused && PedFree(mind);

    /// <summary>
    /// One of the character's own spots, chosen the way the engine chooses: a uniform
    /// number scaled by the count, truncated, and then clamped down to the last index — so the one
    /// case where <c>rand()</c> returns its maximum lands on the last spot rather than past it.
    /// </summary>
    InterestSpot PickSpot(Mind mind)
    {
        int count = Math.Max(mind.SpotCount, 1);
        int index = (int)(Random.Unit() * count);
        if (index > count - 1) index = count - 1;
        index += mind.FirstSpot;
        return Spots.All[Math.Clamp(index, 0, Spots.All.Length - 1)];
    }

    /// <summary>
    /// Sends a character to a spot. The engine's wanderer goes through <c>rpgAction::MoveTo</c>,
    /// which searches a path; its flier calls the walker straight
    /// at its bare run speed, so a flight is one straight line with no search and no ground under it.
    ///
    /// That is how the crows reach their perches: 34 of the 36 spots in group 9 — the ones they use —
    /// are markers up in the air with no walkable ground anywhere under them. A wanderer sent to one
    /// of those finds no route and stays where it is, as the move order does.
    /// </summary>
    void MoveTo(Mind mind, InterestSpot spot, bool run, bool fly = false)
    {
        bool went = fly ? mind.Walker.FlyTo(spot.Position, mind.Walker.RunSpeed) : Go(mind, spot.Position, spot.Node, run);
        if (went) mind.Went(Array.IndexOf(Spots.All, spot));
    }

    /// <summary>
    /// `rpgAction::MoveTo` as the minds use it — every AI walk goes through it but the
    /// flier's (the fear, the daze, the enemy's manoeuvres, the searches, following, the party, the
    /// wanderer). Two things it does that a bare walk does not:
    /// <list type="bullet">
    /// <item>**not now** (false) while the character is busy, dead, or held —
    /// `THING_AF2_RESTRAINED`, which is what keeps a victim with teeth in its neck where it is;</item>
    /// <item>**refused** (false) when somebody else stands at the point, unless the
    /// caller passes flag `0x100` (<paramref name="anyoneThere"/>) — which only the approach does
    /// (<see cref="Rpg.MoveOrder.Head"/> lists who pushes what);</item>
    /// <item>whatever it was doing cancelled first, unless it was already walking under a move
    /// order, and the walk that starts is **ACTION 1** until it stops
    /// (<see cref="Rpg.MoveOrder"/>);</item>
    /// <item>the speed: the run for <paramref name="run"/> true, the walk for false, and by the
    /// distance (300) when it is not said — **times the character's MOVESPEED** at this moment,
    /// so a limp slows the AI down and Celerity speeds it up.</item>
    /// </list>
    /// </summary>
    bool Go(Mind mind, Vector3 to, int node, bool? run, bool anyoneThere = false)
    {
        if (mind.Thing.Actor is not { } me) return mind.Walker.WalkTo(to, node, run, refuseOccupied: !anyoneThere, mind: true);
        // Not now, and somebody standing there refuses it — **every AI walk asks but the approach**,
        // which passes `0x100` (<paramref name="anyoneThere"/>) — before the cancel
        // (A refused walk leaves the character walking its last route and in
        // its action (World.FreeSpot. Earlier, only the manoeuvre and the sidestep asked).
        if (Rpg.MoveOrder.Head(mind.Thing, mind.Walker, to, anyoneThere) != 0) return false;
        // The cancel — unless it is already walking under a move order — and the gate again
        // (Rpg.MoveOrder). So an AI walk ends a swing's approach or a cast's wait.
        if (!Rpg.MoveOrder.Ready(mind.Thing)) return false;
        bool runs = run ?? Vector3.Distance(to, mind.Thing.Position) >= Rules.GameRules.Current.Movement.RunBeyond;
        float speed = (runs ? mind.Walker.RunSpeed : mind.Walker.WalkSpeed) * me.MoveSpeedScale;
        if (!mind.Walker.WalkTo(to, node, runs, refuseOccupied: false, speed, mind: true)) return false;   // ten waits
        Rpg.MoveOrder.Taken(mind.Thing);                              // ACTION 1
        return true;
    }


    // --- enemysearch (picked by searchStyle) ----------------

    /// <summary>How often a searcher looks again (both styles set this before they look).</summary>
    public const int SearchThink = 500;
    /// <summary>How often a searcher a script has switched off asks again.</summary>
    public const int SearchOffThink = 1000;

    /// <summary>
    /// How far from its home a character has to be before it walks back, and how
    /// long <c>ef_wanted</c> lasts when a guard catches somebody at it.
    /// </summary>
    public const float HomeReach = 40f;
    public const int WantedFor = 120_000;

    /// <summary>The engine's AI debug bits for the two search styles.</summary>
    public bool SearchingOff { get; set; }

    /// <summary>
    /// The engine's AI main flags word, which a script sets through
    /// <c>AISetMainFlags</c>. Every bit is named in <see cref="AiMainFlag"/> and each was matched to
    /// the function that reads it. The scripts set and clear them through <c>AISetMainFlags</c> and
    /// <c>AIClearMainFlags</c> (<c>CodexScenes.Cutscenes</c>); until one does this is zero and the AI
    /// runs unrestricted, as it does in a plain run of the original.
    /// </summary>
    public uint MainFlags { get; set; }

    /// <summary>Where attack animations come from, when the place has any.</summary>
    readonly Anim.MotionSets? motions;

    /// <summary>
    /// Looking for somebody to fight. Two styles, and the AI file picks: style 0 takes the nearest
    /// character that is not a friend, style 1 — the guards, and only two of the game's AI files —
    /// takes the nearest it **catches in the act** (<see cref="IsCriminal"/>), and marks everybody
    /// it catches <c>ef_wanted</c> for two minutes.
    ///
    /// Everything about the shape is the engine's: the range comes from the AI file
    /// (<c>searchDist</c>), the nearest candidate wins, and a searcher that finds nobody walks back
    /// to where it was placed. It looks again in half a second either way.
    ///
    /// <para>
    /// **The wanted mark does nothing, and that is the engine's.** The guard's "already wanted"
    /// test is the effect fold of kind 22, and <c>ef_wanted</c> composes by
    /// <i>multiplying</i> from a starting value of 0 — so the fold is 0 whatever is on the
    /// character, nobody ever counts as wanted, and every catch marks again. 1.1 shows both
    /// halves: a knight swinging at Wilhem collects a fresh <c>ef_wanted</c>
    /// every frame while a fellow knight keeps catching him, and Christof, wanted for two minutes,
    /// walks past a knight that has just finished with Wilhem and is not touched.
    /// </para>
    /// </summary>
    void Search(Mind mind)
    {
        // **The script's switches come first**: `AIMAIN_FLAG_NOENEMYSEARCH`
        // stops the plain search and `AIMAIN_FLAG_NOGUARDSEARCH` the guard's, and either way the
        // searcher asks again in a second rather than half of one. `OLDT_7_1` sets both until Old
        // Town's attack is over and `DOCK_33_1_Main` sets the guards' (found by a comparison
        // with 1.1; until then only the test switch below was known).
        uint off = mind.Class.SearchStyle == GuardSearchStyle ? AiMainFlag.NoGuardSearch : AiMainFlag.NoEnemySearch;
        if ((MainFlags & off) != 0) { mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.Search[1]; return; }
        mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.Search[0];
        if (SearchingOff) return;
        if (mind.Thing.Actor is not { } me || me.Has(World.Actor.Flag.Dead)) return;
        // **A character you are meant to talk to never searches** (in the enemy search, and in the
        // guard's): with THING_AF_TALKTO on the searcher itself, both styles look again in half a
        // second and do nothing else, not even walk home. Cosmas and Ecaterina carry it, and in 1.1
        // Ecaterina stood in her search for two minutes with Christof and Wilhem in
        // the room and never took either; Vitomir had her take Christof on her first think.
        if (me.Has(World.Actor.Flag.TalkTo)) return;

        bool masquerade = mind.Class.SearchStyle == GuardSearchStyle;
        WorldThing? best = null;
        float nearest = float.MaxValue;

        foreach (var other in Candidates(mind, guard: masquerade))
        {
            if (masquerade)
            {
                // The fold, then the act; either one qualifies, and a fold of 0 marks.
                float wanted = other.Effects.Value(Formats.EffectKind.Wanted);
                if (!IsCriminal(other) && !(wanted > 0f)) continue;
                if (wanted == 0f && Wanted is not null)
                    Rpg.EffectRuntime.Apply(other, Wanted, 0, Now, (int)Rules.GameRules.Current.Ai.Search[3], value: Rules.GameRules.Current.Ai.Search[4]);
            }
            float distance = Vector3.Distance(other.Position, mind.Thing.Position);
            if (distance < nearest) { nearest = distance; best = other; }
        }

        if (best is not null)
        {
            if (masquerade) Caught(mind, best);
            else Take(mind, best);
            return;
        }
        GoHome(mind);
    }

    /// <summary>What a guard's catch puts on the character, by value (1.0).</summary>
    public const float WantedValue = 1f;

    /// <summary>
    /// A guard taking the one it caught: take it as a target — reacting, if this mind
    /// has not reacted to anybody yet — go into the enemy mode, **shout** (<c>vox_alert</c>, every
    /// time, where the ordinary search shouts only once it has reacted before), and play
    /// <c>awaken</c> on the motion channel if the set has it. It then stands for the longer of that
    /// and the AI file's <c>wakeupDelay</c>, and thinks again after the delay — or on the very next
    /// frame when both are nothing, which is what the knights have: a knight that catches a fellow
    /// knight lets him go as a friend on its next think, searches, catches him again, and marks him
    /// again, frame after frame, for as long as the other is swinging.
    /// </summary>
    void Caught(Mind mind, WorldThing target)
    {
        SetTarget(mind, target);
        mind.Enter(AiMode.Enemy, andBase: false);
        Audio.Noise.Foley(mind.Thing, Audio.FoleyEvent.VoxAlert);
        int played = motions is not null && mind.Thing.Animation is { } animation
            ? animation.Play(MotionAwaken, motions, onMotion: true) : -1;
        int wake = mind.Class.WakeupDelay;
        int hold = Math.Max(played, wake);
        if (hold < 1) { mind.NextThink = Now; return; }
        mind.NextThink = Now + wake;
        mind.BusyUntil = Now + hold;
    }

    /// <summary>
    /// Whether a guard has caught this character in the act. The whole crime system is
    /// **this one test** — nothing else in 1.1 ever applies <c>ef_wanted</c>, and no
    /// script does either:
    ///
    /// > the character is in one of five action states — 2, 5, 6, 7 or 8: closing to attack,
    /// > swinging, 6, walking up to feed, feeding — and the thing it is acting on resolves to an
    /// > actor or the player.
    ///
    /// In other words, a guard makes you wanted by **seeing you attack somebody**. In
    /// play: if Christof or his party actively attack a civilian, the guards on patrol
    /// come after him. That is exactly what this is, and the guard does not care who you are
    /// attacking — it cares that you are attacking, which is why a knight swinging at you is caught
    /// by the knight beside him (the log of Old Town at night).
    /// </summary>
    public static bool IsCriminal(WorldThing thing) =>
        thing.Actor is { } actor && (int)actor.Action is 2 or 5 or 6 or 7 or 8
        && actor.Target is { Removed: false } on && on.Type is ThingType.Actor or ThingType.Player;

    /// <summary>The <c>ef_wanted</c> row, when the effect definitions have been loaded.</summary>
    public Formats.NedEffect? Wanted { get; init; }

    /// <summary>
    /// `misc/effects.ned`, which the item chooser needs: an item's category is decided by what its
    /// effects <i>are</i>, not by the item's type (`Minds.Choose`).
    /// </summary>
    public Rpg.EffectDefinitions? Effects { get; init; }

    /// <summary>
    /// `misc/game.ndd`. The chooser needs a discipline's type, its cost per level and its range,
    /// and all three are in this table rather than on the character.
    /// </summary>
    public Rpg.DisciplineDefinitions? Powers { get; init; }

    /// <summary>
    /// Everyone this character could take against right now: near enough, in the same place, alive,
    /// with a mind of their own, not a friend, not one of the kinds the engine skips, noticeable,
    /// and in sight. The engine's own order of tests, which matters because
    /// the cheap ones come first.
    /// </summary>
    /// <param name="guard">
    /// Style 1's chain, which is **shorter**, and the difference is the engine's not ours. The
    /// enemy search runs the gather, then the sight test, then the friendly test,
    /// then the enemy qualifier. The guard search
    /// runs the gather and the sight test and then goes straight to the wanted
    /// test — it calls **neither** the friendly test nor the qualifier, and so does not test the mind pointer,
    /// the team, or the neutral/talk-to mask at all. A guard takes against whoever it catches in
    /// the act, friend or not — which is how one knight catches another swinging at you.
    /// </param>
    IEnumerable<WorldThing> Candidates(Mind mind, bool guard = false)
    {
        float range = mind.Class.SearchDist;
        foreach (var other in Nearby(mind.Thing, range))
        {
            if (ReferenceEquals(other, mind.Thing)) continue;
            if (other.Actor is not { } them) continue;
            if (other.Type is not (ThingType.Actor or ThingType.Player)) continue;
            if (them.Has(World.Actor.Flag.Dead)) continue;
            if (!guard)
            {
                // No mind of its own, or marked out of the fight: the engine skips both.
                if (!minded.Contains(other)) continue;
                // The engine's own mask is 0x40080: **neutral**, and **talk-to** — a
                // character you are meant to have a conversation with is not a character to attack.
                if (them.Has(World.Actor.Flag.Neutral) || them.Has(World.Actor.Flag.TalkTo)) continue;
                if (Rpg.Sight.Friendly(mind.Thing, other)) continue;
                // And the qualifier's last two clauses: somebody a friend is
                // feeding on, and — for a party member — somebody Mesmerize holds for a friend.
                if (FedOnByAFriend(mind.Thing, other) || HeldForAFriend(mind.Thing, other)) continue;
            }
            if (Vector3.Distance(other.Position, mind.Thing.Position) > range) continue;
            if (!Rpg.Sight.Notices(mind.Thing, other, Moving(other))) continue;
            if (!Rpg.Sight.Sees(Level, mind.Thing, other)) continue;
            yield return other;
        }
    }

    /// <summary>
    /// **Where a search actually looks — and it is not the whole level.** Both `enemysearch`
    /// handlers gather through the sphere query, which gets its candidates from **the sector flood**: a
    /// **portal flood out of the searcher's own sector**, stamped so no sector is visited twice
    /// (a global counter), crossing a portal only when the searcher is in front of
    /// its plane by less than the search radius, and capped at **256** entries. Only things in the
    /// sectors that flood reaches are ever offered to the tests in <see cref="Candidates"/>.
    ///
    /// This tree walked every character in the place instead, and the symptom showed
    /// in play twice: the Cappadocian vampires went after the monks, which is wrong,
    /// since *"The cappadocians never aggro on those monks ever in the base game,
    /// period."* He was right and the reason is geometric, not a flag — six other explanations
    /// (neutrality, team, the enemy qualifier, range, `AISetMainFlags`, the AI class) were read and
    /// eliminated one at a time. A Cappadocian in the crypt is never *offered* a
    /// monk in the cloister, whatever the flags say, because the flood does not reach him.
    ///
    /// `Bodies.SectorsNear` is the same flood, written for collision in an earlier batch and
    /// already carrying the engine's portal rule; it simply had no second caller until now. With
    /// no sector index, or a thing that is in no sector, this falls back to the flat walk — which
    /// is what every test that builds a place without a level gets.
    /// </summary>
    IEnumerable<WorldThing> Nearby(WorldThing from, float range)
    {
        if (where?.Props is not { } bodies || from.Sector < 0 || Level is null) return Everyone;
        return bodies.In(bodies.SectorsNear(from.Position, range, (int)from.Sector)).Where(Met);
    }

    /// <summary>
    /// **The query is a collision query, and it meets only bodies** (the query's filter and its
    /// shape tests): a thing with no collide shape (`THING_COLLIDE_NONE`) or with `DISABLECOLLIDE` is
    /// never among its answers, so no search is offered it and no shout reaches it. That is how the
    /// scripts put a character away until its scene — `FLS1_Main` keeps Vukodlak unrendered, bodiless
    /// and held in the Cathedral of Flesh's first level until Libussa has helped. Earlier, Vitomir
    /// offered every thing in the flooded sectors, and the companions went for him on the way back
    /// in (in play: *"vukodlak will be there and will start attacking you but he's invisible"*).
    /// The asker itself is let through whatever its shape, as before: the callers pass over it.
    /// </summary>
    static bool Met(WorldThing t) =>
        t.Collide != CollideShape.None && (t.Flags & WorldThing.Flag.DisableCollide) == 0;

    /// <summary>The place these minds belong to, for the sector index the search gathers through.</summary>
    readonly Location? where;

    /// <summary>Whether a character is on the move, which is what makes it easier to spot.</summary>
    bool Moving(WorldThing thing)
    {
        foreach (var m in All) if (ReferenceEquals(m.Thing, thing)) return m.Walker.Speed > 0f;
        return false;
    }

    /// <summary>
    /// `MOTION_AWAKEN`, the animation a `THING_AF_STARTHIDDEN` character plays when it rises.
    /// The number is the game's own — `Codex.java` declares `MOTION_AWAKEN = 149` —
    /// and the rate beside it is the literal `0x41f00000`, 30.
    /// </summary>
    public const int MotionAwaken = 149;
    /// <inheritdoc cref="MotionAwaken"/>
    public const float AwakenRate = 30f;

    /// <summary>
    /// Mode **257**, `hidden` — the last entry in the engine's table of body modes, one past
    /// `MOTION_STAKED`, and not one the scripts have a name for. The mind set-up ends by
    /// playing it on the action channel at rate 30 for any character that is START HIDDEN.
    /// In `corpseMinion.nms` it is `ZombuLieDown.nad`, nine frames, `noloop endpause
    /// nofadein`: the corpse lies down on its slab in the first third of a second and holds the
    /// last frame until `MOTION_AWAKEN` replaces it.
    ///
    /// In play: *"The awakened dead start standing up, as soon as you enter and
    /// 'activate' them, they immediately switch to laying down on those tables, and do the
    /// 'awakening' crawl."* Their first frame was the motion set's `stand`, because nothing had
    /// asked for anything else.
    /// </summary>
    public const int MotionHidden = 257;

    /// <summary>
    /// Plays <see cref="MotionHidden"/>. The engine has the model by the time it builds the mind;
    /// Vitomir builds minds before the renderer builds the animation, so a character with no
    /// animation yet is left the request, and the scene mesh plays it the moment it makes one.
    /// </summary>
    public static void Hide(WorldThing thing, Anim.MotionSets? motions)
    {
        if (motions is not null && thing.Animation is { } lying)
            lying.Play(MotionHidden, motions, onMotion: false, rate: AwakenRate);
        else thing.Waiting = (MotionHidden, AwakenRate);
    }

    /// <summary>Everything in the place, for the search to look through.</summary>
    public IReadOnlyList<WorldThing> Everyone { get; private set; } = [];

    /// <summary>The things that have a mind of their own, which is one of the search's own tests.</summary>
    readonly HashSet<WorldThing> minded = [];

    /// <summary>
    /// Takes a target and goes to <see cref="AiMode.Enemy"/> as the enemy search does:
    /// the set-target reacts the first time — stop where you are, make a noise, stand
    /// still for the AI file's <c>wakeupDelay</c> — and then, every time, <c>vox_alert</c> and a
    /// think now (<see cref="Sighted"/>). A character that has reacted once does not react again.
    /// </summary>
    public void Aggravate(Mind mind, WorldThing target) => Take(mind, target);

    /// <inheritdoc cref="Aggravate"/>
    void Take(Mind mind, WorldThing target) => Sighted(mind, target);

    /// <summary>
    /// `SetTarget` as the engine has it: take the target, and — for a mind that has not
    /// yet reacted to anybody (the reacted bit clear) — react to it: wake, say so, play
    /// the awakening if it was lying hidden, and tell the scripts. The mind-altering modes take
    /// their target this way, so a monk mesmerised before he ever saw you still wakes up to it.
    /// </summary>
    /// <remarks>
    /// What is stored is checked first: a character, **alive**, in this place — otherwise
    /// nobody. The react still goes by what was asked for, so a script's `AISetTarget` on a corpse
    /// wakes a sleeper and leaves it with no one to fight.
    /// </remarks>
    public void SetTarget(Mind mind, WorldThing? target)
    {
        if (ReferenceEquals(target, mind.Thing)) return;
        var keep = target is { IsActor: true, Actor.Dead: false } && ReferenceEquals(target.Place, mind.Thing.Place)
                   && target.Type is ThingType.Actor or ThingType.Player ? target : null;
        mind.Aim(keep, Now, target);
        if (target is not null && !mind.HasNoticed) React(mind, target);
    }

    /// <summary>The react — the second half of <see cref="Take"/> and of <see cref="SetTarget"/>.</summary>
    void React(Mind mind, WorldThing target)
    {
        mind.HasNoticed = true;
        // The walk is **not** stopped: the react's first call is the locomotion put back on the
        // motion channel, not a stop. Vitomir stopped the walk here too until then, which the party rally's
        // react (the rally → the set-target → here) would have turned into Christof's first attack standing
        // still; in 1.1 he and Wilhem react (mind flags 0x0 → 0x2) at the moment their run
        // at the enemy starts.
        mind.Walker.Reanimate();                                      // before the noise
        Audio.Noise.Foley(mind.Thing, Audio.FoleyEvent.VoxWakeup);   // the react's own noise
        int wake = mind.Class.WakeupDelay;
        // **This is where a hidden thing stops being hidden**. `THING_AF_STARTHIDDEN`
        // is a *starting* state and not a permanent one: an awakening corpse lies among the scenery
        // until something rouses it, and the moment it takes against somebody it plays
        // `MOTION_AWAKEN` (mode **149**, at rate 30) and **the flag is cleared**. The character
        // stands still for the longer of the AI file's `wakeupDelay` and that animation.
        //
        // It matters far past the animation, because the flag is also what keeps the pointer off a
        // character (`Ui.Highlight.Offers`) — so a corpse that never clears it
        // can attack you and cannot be attacked back. That is what play showed in the first
        // monastery: the awakened zombies could not be attacked.
        if (mind.Thing.Actor is { } woken && woken.Has(World.Actor.Flag.StartHidden))
        {
            woken.Flags &= ~World.Actor.Flag.StartHidden;
            mind.Thing.Waiting = null;
            if (motions is not null && mind.Thing.Animation is { } rise)
                wake = Math.Max(wake, rise.Play(MotionAwaken, motions, onMotion: false, rate: AwakenRate));
        }
        mind.BusyUntil = Now + wake;
        mind.NextThink = Now + wake;
        // And the scripts hear it: `awaken(guid, target, capture)`.
        where?.Awakened?.Invoke(mind.Thing, target);
        // Not "noticed" — by the time this runs the mind has passed every gate and committed to a
        // target. The engine's own notice test is a bare stealth-against-perception
        // comparison with no team, flag, sight or range in it, and it is a separate step. The
        // old wording read like a diagnosis and was a conclusion, which cost a session of chasing
        // the wrong step.
        log.LogDebug("AI: {Name} has taken against {Target}", mind.Thing.Template, target.Template);
    }

    /// <summary>
    /// **A searcher with nobody to fight goes back to its post** (the tail of both
    /// searches, the same twelve lines in both): with a home node known and no
    /// <c>THING_AF_AI_NORETURNHOME</c>, more than 40 units from home it is ordered to walk there
    /// (the move order with flag 0x20, walking whatever the distance); within 40 it takes the spawn
    /// transform's turn again, on every search, so a guard back on its spot
    /// faces the way the scene faced it.
    ///
    /// The home is where the character came into the world (<see cref="Mind.LookUpHome"/>).
    /// In 1.1, in all instances, when they lose contact with the player they
    /// actively go back to their standing places or patrol routes. The patrol routes are not
    /// this: a patroller's script sets <c>AINORETURNHOME</c> — `WalkCycle`'s own comment is
    /// "override the AI behavior to return home on losing target" — and walks it back to its
    /// route itself, from the walker's <c>arrived</c> (<see cref="World.Location.Arrived"/>).
    /// </summary>
    void GoHome(Mind mind)
    {
        if (mind.HomeNode < 0) return;
        if (mind.Thing.Actor is { } me && me.Has(World.Actor.Flag.AiNoReturnHome)) return;
        if (Vector3.Distance(mind.Home, mind.Thing.Position) > Rules.GameRules.Current.Ai.Search[2])
        {
            Go(mind, mind.Home, mind.HomeNode, run: false);
            return;
        }
        mind.Thing.Orientation = mind.Thing.SpawnOrientation;
    }

    // --- enemy -----------------------------------------------------------

    /// <summary>How often a manoeuvre comes with <c>vox_taunt</c>.</summary>
    public const float TauntChance = 0.2f;

    /// <summary>How long a character will chase somebody it has never got to grips with.</summary>
    public const int GiveUpOnTarget = 6000;
    /// <summary>The spread of the manoeuvre's angle, and the shortest one worth walking.</summary>
    public const float ManoeuvreArc = 60f, ManoeuvreFloor = 50f;
    /// <summary>How often a character with a target thinks again.</summary>
    public const int EnemyThink = 1000;

    /// <summary>
    /// Having a target, in the engine's own order:
    ///
    /// <list type="number">
    /// <item>nothing to swing: the target goes;</item>
    /// <item>a target that has become a friend goes too;</item>
    /// <item>if the target can be **made out** — alive, here, its stealth within this character's
    /// perception and a clear line to it, but **at any range** (the sight test with its range flag off) —
    /// <see cref="Fight"/> is asked. If it ordered an attack or cast something, **that is the whole
    /// turn**;</item>
    /// <item>otherwise, a target that cannot be made out sends the character into
    /// <see cref="AiMode.Approach"/> for up to ten seconds (<see cref="Approaching"/>);</item>
    /// <item>a character that is busy — mid-swing, mid-reload, getting up — waits until it is not;</item>
    /// <item>one that has never got to grips with its target and took it more than six seconds ago
    /// gives up;</item>
    /// <item>and everybody else repositions (<see cref="Manoeuvre"/>).</item>
    /// </list>
    ///
    /// **Why the order matters: the archer.** In play, *"the skeleton archer ... did this
    /// odd slide while shooting when coming at me ... the skeleton seemed to want to get to within
    /// melee distance. The archers don't do that, they just get into range where they can use their
    /// bows and attack while standing still."* Two of this function's steps were wrong. The fight
    /// answered "nothing happened" whenever the attack order was refused — and it is always refused
    /// while the last shot's reload is running — so a bowman in range manoeuvred on every think in
    /// between shots, which walked it at its target with the bow still drawn. The engine never
    /// looks at the order's answer: in reach, the attack was *ordered*, and a character
    /// that has ordered an attack does not reposition. And the busy test that stops a manoeuvre
    /// mid-swing is the actor's own timer, which this function had not been reading at all.
    /// </summary>
    void Enemy(Mind mind)
    {
        mind.NextThink = Now + Rules.GameRules.Current.Ai.EnemyThinkMs;
        if (mind.Thing.Actor is not { } me) return;

        // Nothing to fight with: the engine lets the target go rather than closing in empty-handed —
        // and **stays in the enemy mode** (`SetTarget(0)` and return, no mode change), so
        // a character with no weapon never searches again and takes a target only when an event
        // hands it one. 1.1's Old Town horse did exactly that: in the knights' fight
        // it takes and drops a target again and again, and stays in the enemy mode,
        // with nobody, after the fight.
        if (me.Weapon is null) { mind.Aim(null, Now); return; }

        // How much trouble it is in — the ladder escalates against it (512 units).
        mind.Danger = Threat(mind);
        var target = mind.Target;
        // A target it cannot notice any more: a sense discipline, if it has one.
        if (target is not null && !Rpg.Sight.Notices(mind.Thing, target, Moving(target)) && SenseRung(mind)) return;
        if (target is not null && Rpg.Sight.Friendly(mind.Thing, target))
        {
            mind.Aim(null, Now);
            mind.Enter(mind.BaseMode);
            return;
        }
        // `mind.BusyUntil` is the half of the engine's busy time that the AI writes — the wake-up
        // after a react, the flinch. Nothing is attempted while it runs.
        if (mind.BusyUntil > Now) { mind.NextThink = mind.BusyUntil; return; }

        bool seen = target is not null && MadeOut(mind, target);
        if (seen && Fight(mind, target!)) return;

        // The fight may have let the target go.
        target = mind.Target;
        if (target is null) { mind.Aim(null, Now); mind.Enter(mind.BaseMode); return; }
        if (!seen)
        {
            mind.Enter(AiMode.Approach, andBase: false);
            mind.ApproachUntil = Now + Rules.GameRules.Current.Ai.ApproachForMs;
            return;
        }
        // The other half of the busy time: a swing or a shot that is still playing.
        if (me.BusyUntil > Now || me.Has(World.Actor.Flag.Dead) || me.Has2(World.Actor.Flag2.Restrained))
        {
            mind.NextThink = Math.Max(me.BusyUntil, Now);
            return;
        }
        // Never laid a hand on it and took it six seconds ago: forget the whole thing.
        if (mind.EngagedAt == 0 && mind.TargetAt + Rules.GameRules.Current.Ai.GiveUpOnTargetMs < Now)
        {
            mind.Aim(null, Now);
            mind.Enter(mind.BaseMode);
            return;
        }
        Manoeuvre(mind, target);
    }

    /// <summary>
    /// Whether a character can make out its target at all (the sight test with its range flag clear): the
    /// target is a character, alive and here, its stealth is within this character's perception, and
    /// there is a clear line to it. **No distance** — the search looks only as far as
    /// <c>searchDist</c>, but once a character has somebody it keeps them however far they go.
    /// </summary>
    bool MadeOut(Mind mind, WorldThing target) =>
        target.Actor is { } them && !them.Has(World.Actor.Flag.Dead) && target.InWorld
        && Rpg.Sight.Notices(mind.Thing, target, Moving(target))
        && Rpg.Sight.Sees(Level, mind.Thing, target);

    /// <summary>How long <see cref="AiMode.Approach"/> is given before the target is let go (`now + 10000`).</summary>
    public const int ApproachFor = 10_000;
    /// <summary>How often an approaching character looks again (250 ms), and after a walk is ordered (500).</summary>
    public const int ApproachThink = 250, ApproachWalkThink = 500;
    /// <summary>Closer than this, body to body, and an approaching character stops walking (`_DAT_004eb634` = 10).</summary>
    public const float ApproachStop = 10f;

    /// <summary>
    /// **`AiMode.Approach`**, which the enemy mode drops into when it
    /// cannot make its target out — round a corner, behind a pillar — and which this tree had
    /// decoded and never written: a character in it stood still and said so once. It walks straight
    /// at the target's own position and walk node, every half second, and hands back to
    /// <see cref="AiMode.Enemy"/> the moment the target can be made out **and** is within the
    /// current attack's reach. Ten seconds of that and the target is dropped.
    ///
    /// The walk is the move order with flag `0x100` — which skips the move order's "is anybody
    /// standing there" test, since the target always is — plus `0x10` when the AI file's
    /// <c>attackFlags</c> bit 0 says to run, and neither run nor walk otherwise, which leaves it to
    /// the 300-unit rule.
    /// </summary>
    void Approaching(Mind mind)
    {
        mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.ApproachThinkMs[0];
        if (mind.Thing.Actor is not { } me) return;
        if (me.Weapon is null) { mind.Aim(null, Now); return; }

        var target = mind.Target;
        if (target is null || target.Actor is not { } them || them.Has(World.Actor.Flag.Dead)
            || Rpg.Sight.Friendly(mind.Thing, target) || mind.ApproachUntil < Now)
        {
            mind.Aim(null, Now);
            mind.Enter(mind.BaseMode);
            return;
        }
        if (MadeOut(mind, target) && World.Bodies.InReach(mind.Thing, target, me.Weapon.Current.MaxDistance))
        {
            mind.Enter(AiMode.Enemy, andBase: false);
            mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.ApproachThinkMs[0];
            return;
        }
        int busy = Math.Max(me.BusyUntil, mind.BusyUntil);
        if (busy > Now || me.Has(World.Actor.Flag.Dead) || me.Has2(World.Actor.Flag2.Restrained))
        {
            mind.NextThink = busy;
            return;
        }
        if (World.Bodies.Gap(mind.Thing, target) >= Rules.GameRules.Current.Ai.ApproachThinkMs[2])
        {
            int node = target.Mind?.Walker.Node ?? walk?.Find(target.Position, World.WalkMap.Reach) ?? -1;
            bool? run = (mind.Class.AttackFlags & AttackFlagRun) != 0 ? true : null;
            if (node >= 0) Go(mind, target.Position, node, run, anyoneThere: true);     // `0x100`
            mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.ApproachThinkMs[1];
        }
    }

    /// <summary>How long a character waits after a refusal it expects to pass shortly.</summary>
    public const int RetryCombat = 100;

    /// <summary>
    /// Whether to attack, and the attack itself. Returns true when the
    /// caller should **not** reposition — the mind's two flags, cast something and
    /// ordered an attack, cleared before every think. An attack counts as
    /// ordered the moment the reach test passes, whatever the order itself answered.
    ///
    /// The engine's own order, gate by gate:
    /// <list type="number">
    /// <item>the think is pushed out by a plain <c>attackRate</c> — and only when an
    /// attack is actually ordered is it rolled again with up to a fifth more
    /// (<c>now + rate + trunc(rand01 × 0.2 × rate)</c>). So a character that has just **cast**
    /// thinks again after exactly <c>attackRate</c>: a Szlachta's heal is followed by half a second
    /// standing where it was stopped, never more;</item>
    /// <item>a target that is gone, dead or in another level is dropped;</item>
    /// <item>a target that is friendly, neutral, being talked to, or has no mind of its own is
    /// **dropped**, and the character goes back to its base mode;</item>
    /// <item>a discipline may be cast instead, and if one is the character is busy;</item>
    /// <item><c>AIMAIN_FLAG_NOENEMYCOMBAT</c> stops the attack without dropping the target;</item>
    /// <item>no weapon, no fight — and the target goes (the engine drops it rather than closing in
    /// empty-handed, which is also the first test in <see cref="Enemy"/>);</item>
    /// <item>and finally the reach test, which is **body to body across the ground, the heights
    /// overlapping**, against the chosen attack's <c>MAXDISTANCE</c> (the reach test,
    /// <see cref="World.Bodies.InReach"/>), not centre to centre.</item>
    /// </list>
    ///
    /// Which of the weapon's two attacks is used is a roll against the AI file's <c>attackAlt</c>:
    /// zero always takes the first, otherwise the second is used when the roll comes in under it.
    /// </summary>
    bool Fight(Mind mind, WorldThing target)
    {
        mind.NextThink = Now + mind.Class.AttackRate;

        if (mind.Thing.Actor is not { } me) return false;
        if (!Worth(mind, target)) { mind.Aim(null, Now); mind.Enter(mind.BaseMode); return false; }
        if (LeaveAlone(mind, target))
        {
            // A drop, not a pause (`SetTarget(0)`, back to the base mode, think again in a
            // tenth of a second). This tree kept the target here until then.
            mind.Aim(null, Now);
            mind.Enter(mind.BaseMode);
            mind.NextThink = Now + Rules.GameRules.Current.Ai.RetryCombatMs;
            return false;
        }
        // The discipline and item ladder, before the combat flag and the weapon — an
        // enemy casts exactly as a companion does, and a cast taken is the turn. This
        // tree said the enemy path never reached the ladder until then; the attack choice calls it.
        if (Reach(mind, target) is { Acted: true } chose) { Reached(mind, chose); return true; }
        if ((MainFlags & AiMainFlag.NoEnemyCombat) != 0) { mind.NextThink = Now + Rules.GameRules.Current.Ai.RetryCombatMs; return false; }
        if (me.Weapon is null) { mind.Aim(null, Now); mind.Enter(mind.BaseMode); return false; }
        if (!Loaded(mind, me)) return false;

        // **The reach is the attack the weapon has selected now** (the weapon's
        // selected attack), not the one about to be rolled: the roll only decides what is *ordered*.
        if (!World.Bodies.InReach(mind.Thing, target, me.Weapon.Current.MaxDistance)) return false;

        // In reach: the think is rolled out again, and only here — the cast above and
        // every refusal keep the plain rate. The roll comes before the attack's own.
        mind.NextThink = Now + mind.Class.AttackRate
                       + (int)(Random.Unit() * Rules.GameRules.Current.Ai.AttackJitter * mind.Class.AttackRate);

        // The second attack record is a roll; a weapon with only one ignores the ask anyway.
        int which = mind.Class.AttackAlt > 0f && Random.Unit() < mind.Class.AttackAlt ? 1 : 0;
        // **The order's answer is not read** (`AttackThing` is called and throws its result
        // away). A refusal — most often "still busy with the last one" — is still a turn spent on
        // the fight, and the caller does not reposition. See `Enemy` for what reading it cost.
        Rpg.Actions.Attack(mind.Thing, target, me.Weapon, which, Now, motions: motions);
        Shout(mind, AiEvent.Attacked, Rules.GameRules.Current.Ai.AttackShoutRange);
        mind.EngagedAt = Now;
        return true;
    }

    /// <summary>
    /// The ammo gate that stands in front of an attack, and the reason the
    /// party handler could not be finished until items existed. In order: if the attack in use needs
    /// ammo and has none, try to reload from what the character is carrying; failing that, a
    /// **player** looks through its own inventory for another weapon it could actually use and
    /// switches to it; failing *that*, it says so — once every
    /// <see cref="OutOfAmmoMessage"/> milliseconds, not once a think — and does nothing this turn.
    ///
    /// Two things here are the original's and neither is obvious. The **switch is player-only**,
    /// gated on the holder's thing type, so an enemy never rummages for a spare. And because
    /// <see cref="Rpg.Weapon.Spend"/> is player-only as well, an enemy never runs out in the first
    /// place — this whole path is the party's, and everybody else walks straight through it.
    /// </summary>
    bool Loaded(Mind mind, World.Actor me)
    {
        var weapon = me.Weapon!;
        if (!weapon.OutOfAmmo || weapon.Reload(me.Inventory, mind.Thing))
        {
            mind.OutOfAmmoAt = 0;
            return true;
        }

        if (mind.Thing.Type == World.ThingType.Player && Switch(me)) { mind.Walker.Reanimate(); return false; }

        // "%A is out of ammo!" in the message log, yellow (the character's own name).
        if (Now >= mind.OutOfAmmoAt)
        {
            log.LogInformation("{Who} is out of ammo for {Weapon}", me.Name, weapon.Name);
            if (where?.Session is { } session)
                session.Messages.Post("RPG_OUTOFAMMO", "%A" + session.NameOf(mind.Thing), Ui.MessageLog.Yellow);
            mind.OutOfAmmoAt = Now + Rules.GameRules.Current.Ai.OutOfAmmoMessageMs;
        }
        return false;
    }

    /// <summary>
    /// Swap to the first weapon in the inventory that could be used right now — not ranged, or
    /// needing no ammo, or with ammo in it. The list is in reverse order of acquisition,
    /// so "first" means the most recently picked up, and the engine does not rank the candidates.
    /// </summary>
    bool Switch(World.Actor me)
    {
        foreach (var item in me.Inventory.UsableWeapons())
        {
            if (item.Weapon is not { } replacement || ReferenceEquals(replacement, me.Weapon)) continue;
            me.Weapon = replacement;
            log.LogInformation("{Who} switches to {Weapon}", me.Name, replacement.Name);
            return true;
        }
        return false;
    }

    /// <summary>How often a character with nothing to shoot says so (5, 000 ms).</summary>
    public const int OutOfAmmoMessage = 5000;

    /// <summary>How far an attack carries to other minds (`0x43fa0000` = 500).</summary>
    public const float AttackShoutRange = 500f;

    /// <summary>
    /// The AI events one character tells others about. Only the ones Vitomir raises
    /// or handles are named; the engine's switch has fourteen.
    /// </summary>
    public static class AiEvent
    {
        /// <summary>1: I am afraid of my target (the afraid handler, 1500).</summary>
        public const int Fear = 1;
        /// <summary>3: I have been attacked (2000). Nobody handles it.</summary>
        public const int Assaulted = 3;
        /// <summary>4: I have been hurt (2000), carrying the attacker.</summary>
        public const int Hurt = 4;
        /// <summary>5: I have been killed (2000), carrying the killer.</summary>
        public const int Killed = 5;
        /// <summary>2: I am frenzied (1000) — dropped by the broadcast, never heard.</summary>
        public const int Frenzy = 2;
        /// <summary>6: I am being fed on by my target (each drain tick, 2000).</summary>
        public const int FedOn = 6;
        /// <summary>7: something is in my way and I could not get round it.</summary>
        public const int MakeWay = 7;
        /// <summary>9: I have just attacked something.</summary>
        public const int Attacked = 9;
        /// <summary>10: I cast a heal (500). Nobody handles it.</summary>
        public const int Heal = 10;
        /// <summary>11: I cast a buff (500). Nobody handles it.</summary>
        public const int Buff = 11;
        /// <summary>13: I hid, summoned or sensed (500). Nobody handles it.</summary>
        public const int Special = 13;
        /// <summary>12: I cast something harmful at my target (500).</summary>
        public const int CastHarm = 12;
        /// <summary>14: something terrifying happened here (a TERRIFYING discipline, 1000).</summary>
        public const int Terrifying = 14;
    }

    /// <summary>Events the engine never broadcasts, whoever raises them (2 and 8).</summary>
    static bool Broadcastable(int e) => e is not (0 or 2 or 8);

    /// <summary>
    /// Tells every other mind within <paramref name="range"/> that something happened.
    /// The engine sweeps a sphere around the sender and delivers to every
    /// thing in it that has a mind of its own and is not the sender.
    ///
    /// **The sphere is gathered through the sector flood**, the one the
    /// searches use (<see cref="Nearby"/>): out of the sender's own sector, across a portal only when
    /// the sender stands in front of its plane by less than the range. So a shout carries along
    /// corridors that face the one shouting and not round corners that do not, whatever the straight
    /// distance. In 1.1's Monastery a
    /// monk 1,487 units from Christof's blows, in a sector the flood from where Christof stands
    /// reaches, flinches at every one of them; the same monk 1,416 to 1,462 units from Wilhem's,
    /// which the flood from where Wilhem stands does not reach, never does; and monks 1,350 to
    /// 1,750 units away, out of every flood, never flinch at all. An earlier build
    /// once this took everybody within the straight distance, and the whole cloister flinched.
    /// </summary>
    void Shout(Mind sender, int e, float range, WorldThing? about = null)
    {
        if (!Broadcastable(e)) return;
        foreach (var other in Nearby(sender.Thing, range).ToArray())
        {
            if (ReferenceEquals(other, sender.Thing) || other.Mind is not { } heard) continue;
            if (Vector3.Distance(other.Position, sender.Thing.Position) > range) continue;
            Heard(heard, e, sender, about);
        }
    }

    /// <summary>Somebody raising an event from outside the minds — a cast's TERRIFYING, for one.</summary>
    public void Raise(Mind sender, int e, float range) => Shout(sender, e, range);

    /// <summary>
    /// One mind hearing one event (the event handler's switch): hurt and killed (4, 5), fear and fed-on
    /// (6, 1), attacked (9, whose party half is <see cref="Rally"/>), make way (7), a harmful cast
    /// (12) and a terrifying one (14). Events 3, 10, 11 and 13 do nothing in 1.1 either.
    /// </summary>
    void Heard(Mind me, int e, Mind sender, WorldThing? about = null)
    {
        switch (e)
        {
            case AiEvent.Hurt:
            case AiEvent.Killed: Unseen(me, sender, about); break;
            case AiEvent.Fear:
            case AiEvent.FedOn: Witnessed(me, sender); break;
            case AiEvent.Attacked: Attacked(me, sender); break;
            case AiEvent.MakeWay: MakeWay(me, sender); break;
            case AiEvent.CastHarm: CastAgainst(me, sender); break;
            case AiEvent.Terrifying: Terrified(me, sender); break;
        }
    }

    // The rally is Rally.cs.

    /// <summary>Whether a character is hurt enough to override "I already have a target".</summary>
    static bool Hurting(World.Actor me) =>
        me.MaxHealth > 0f && me.Health <= me.MaxHealth * Rules.GameRules.Current.Ai.HurtShare * FightLeashHealth(me.Stance);

    /// <summary>
    /// The `verylowhealth` key, which every shipped file leaves at its 0.30 default,
    /// and the stance scale that goes with it — **1.2 / 1.0 / 0.75**, which is a *third* set of
    /// numbers and not either leash's.
    /// </summary>
    public const float HurtShare = 0.30f;
    public static float FightLeashHealth(PartyStance stance) => ByStance(Rules.GameRules.Current.Ai.HurtShareStance, stance);

    /// <summary>The verylowhealth stance scales, 1.1's: attack 1.2, neutral 1, defence 0.75.</summary>
    public static readonly float[] HurtStanceScales = [1.2f, 1f, 0.75f];

    /// <summary>The leash's stance scales, 1.1's: attack 2.5, neutral 1, defence 0.8.</summary>
    public static readonly float[] LeashScales = [2.5f, 1f, 0.8f];

    /// <summary>The fight leash's stance scales, 1.1's: attack 2.5, neutral 1, defence 0.4.</summary>
    public static readonly float[] FightLeashScales = [2.5f, 1f, 0.4f];

    /// <summary>How far from the victim a member can be and still pile in (1250).</summary>
    public const float RallyRange = 1250f;
    /// <summary>How often a member takes somebody else instead (0.66).</summary>
    public const float RallySpread = 0.66f;
    /// <summary>How far from the offered enemy it looks for that somebody else (0x443b8000, 750).</summary>
    public const float RallyNearby = 750f;

    /// <summary>
    /// A walker has been stopped by something and could not slide round it. Its
    /// whole body is one test: **only a character in `party` mode asks**.
    /// Everybody else simply stays stuck, which is faithful and is why a jammed enemy stays jammed.
    /// Asking also resets its own think to *now*, so it re-decides at once rather than waiting out
    /// the party's 800 ms.
    /// </summary>
    void Jammed(Mind mind)
    {
        if (mind.Mode != AiMode.Party) return;
        Shout(mind, AiEvent.MakeWay, Rules.GameRules.Current.Ai.MakeWayRange[0]);
        mind.NextThink = Now;
    }

    /// <summary>
    /// Being asked to shift (the event switch's case 7). Four gates, and together they
    /// say something simple: **only the playable cast shoves the playable cast.**
    ///
    /// <list type="bullet">
    /// <item>the asker's thing carries <see cref="World.Actor.Flag.Party"/>;</item>
    /// <item>the asker is a <see cref="ThingType.Player"/>;</item>
    /// <item>so is the listener;</item>
    /// <item>and the listener is not <see cref="Rpg.ActionMode.Feeding"/> or
    /// <see cref="Rpg.ActionMode.WristFeeding"/> (either kind of feeder) — you do not interrupt a feed to get out of somebody's
    /// way, at either end of it.</item>
    /// </list>
    ///
    /// All it then does is write down **who asked** and think again on the next tick; the stepping
    /// itself is the party handler's first job (<see cref="StepAside"/>).
    /// </summary>
    void MakeWay(Mind me, Mind asker)
    {
        if (asker.Thing.Actor is not { } them || !them.Has(World.Actor.Flag.Party)) return;
        if (asker.Thing.Type != ThingType.Player || me.Thing.Type != ThingType.Player) return;
        if (me.Thing.Actor is not { } mine) return;
        if (mine.Action is Rpg.ActionMode.Feeding or Rpg.ActionMode.WristFeeding) return;

        me.MakeWayFor = asker.Thing;
        me.NextThink = Now + 1;
    }

    /// <summary>
    /// Get out of the way: walk <see cref="StepAsideReach"/> units **directly away** from whoever
    /// asked, flattened to the ground, halving the distance each time the order is refused and
    /// giving up below <see cref="StepAsideFloor"/>. A step that is accepted buys
    /// <see cref="StepAsideWait"/> ms of quiet before the character thinks about anything else.
    /// </summary>
    bool StepAside(Mind mind, WorldThing asker)
    {
        var away = mind.Thing.Position - asker.Position;
        away.Z = 0f;
        if (away.LengthSquared() < 1e-6f || walk is null) return false;
        away = Vector3.Normalize(away);

        for (float reach = Rules.GameRules.Current.Ai.MakeWayRange[1]; reach > Rules.GameRules.Current.Ai.MakeWayRange[2]; reach *= 0.5f)
        {
            var spot = mind.Thing.Position + away * reach;
            int node = walk.Find(spot);
            if (node < 0) continue;
            if (!Go(mind, spot, node, run: null)) continue;
            mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.MakeWayRange[3];
            return true;
        }
        return false;
    }

    /// <summary>How far a "get out of my way" carries (`0x42f00000` = 120).</summary>
    public const float MakeWayRange = 120f;
    /// <summary>How far aside to try first, the floor it gives up at, and the quiet afterwards.</summary>
    public const float StepAsideReach = 80f, StepAsideFloor = 10f;
    public const int StepAsideWait = 500;

    /// <summary>
    /// **Event 9, somebody attacked something**. It is not the general "a fight pulls the
    /// room in" that it looks like from the outside. It has exactly two halves, and which one runs
    /// is decided by the *sender's* AI file:
    ///
    /// <list type="bullet">
    /// <item>a sender whose <c>searchStyle</c> is **1** — a **guard**, and only two of the game's 136
    /// AI files are — rallies other guards: a listener that is also `searchStyle 1`, has no target of
    /// its own, and is not stopped by <c>AIMAIN_FLAG_NOGUARDSEARCH</c>, takes the sender's target and
    /// goes to <see cref="AiMode.Enemy"/>;</item>
    /// <item>any other sender, if its **home** mode is <see cref="AiMode.EnemySearch"/> and its target
    /// is the player, runs the **listener's party logic** — which is how the
    /// player's companions pile in when something goes for him. Written — <see cref="Rally"/>.</item>
    /// </list>
    ///
    /// So in the shipped game this event moves two convent knights and the player's party, and
    /// nothing else. `NOGUARDSEARCH` switching off the first half is why that flag is read in four
    /// places rather than one.
    /// </summary>
    void Attacked(Mind me, Mind sender)
    {
        if (sender.Class.SearchStyle != GuardSearchStyle)
        {
            // The party half. The attacker must be an `enemysearch` character **at
            // heart** — the test reads the sender's *home* mode, and its
            // current one is `enemy` whenever it is swinging — and what it hit must be a
            // player-type party member; what gets offered to that member's party is **this
            // listener's own thing**, which is how the 500-unit sweep feeds the party one nearby
            // character at a time. <see cref="Rally"/> throws out the ones that are no use.
            //
            // This read the current mode until then, so no attacker in a fight ever rallied anybody.
            // In 1.1's first monastery the rally works:
            // with a Cappadocian and a skeleton both swinging at a hurt Christof, Wilhem's
            // target swaps between the two every fifth of a second — each attacker's shout offering
            // the party the *other* one, and a hurt victim overriding "already has a target".
            if (sender.BaseMode == AiMode.EnemySearch
                && sender.Target is { Type: ThingType.Player, Mind: { } hurt } victim
                && victim.Actor is { } who && who.Has(World.Actor.Flag.Party))
                Rally(hurt, me.Thing);
            return;
        }
        if (me.Class.SearchStyle != GuardSearchStyle) return;
        if (me.Target is not null) return;
        if ((MainFlags & AiMainFlag.NoGuardSearch) != 0) return;
        if (sender.Target is not { IsActor: true } target) return;
        Rallied(me, target);
    }

    /// <summary>The `searchStyle` the guards run, and the one this event rallies.</summary>
    public const int GuardSearchStyle = 1;

    /// <summary>The share of `attackRate` the think delay is randomly stretched by (0.2).</summary>
    public const float AttackJitter = 0.2f;

    /// <summary>Still a target at all: there, an actor, alive, and in this level.</summary>
    static bool Worth(Mind mind, WorldThing? target) =>
        target is { } t && t.IsActor && !t.Actor!.Has(World.Actor.Flag.Dead)
        && t.Sector >= 0 && mind.Thing.Sector >= 0;

    /// <summary>
    /// Not a target to swing at: it has no mind of its own, or it is friendly, or it is
    /// <c>THING_AF_NEUTRAL</c> or <c>THING_AF_TALKTO</c>, or a friend is feeding on it
    /// (<see cref="FedOnByAFriend"/>), or — for a party member — Mesmerize holds it for a friend
    /// (<see cref="HeldForAFriend"/>); the last two were read. **The fight drops it**
    /// and goes back to the base mode; this page called it a pause until then,
    /// which the original does not do — `SetTarget` is pushed a zero.
    /// </summary>
    bool LeaveAlone(Mind mind, WorldThing target) =>
        target.Mind is null
        || Rpg.Sight.Friendly(mind.Thing, target)
        || (target.Actor is { } t && (t.Flags & LeaveAloneFlags) != 0)
        || FedOnByAFriend(mind.Thing, target)
        || HeldForAFriend(mind.Thing, target);

    /// <summary>`THING_AF_NEUTRAL | THING_AF_TALKTO` — 0x40080.</summary>
    public const uint LeaveAloneFlags = World.Actor.Flag.Neutral | World.Actor.Flag.TalkTo;

    /// <summary>
    /// How fast a character closes on something it is fighting. The engine builds the move order's
    /// flags from the AI file's own <c>attackFlags</c> — (attackFlags bit 0 chosen between 0x10 and 0x20)
    /// — so bit 0 set gives **0x10** and bit 0 clear gives **0x20**, and the move order
    /// reads those as run and walk (`0x11` runs, `0x20` walks, neither runs only past
    /// <see cref="Player.RunBeyond"/>).
    ///
    /// **Every shipped `.nai` sets `attackFlags` to 0x01**, so in the game as it ships an enemy
    /// always *runs* at you. Vitomir reads the flag rather than assuming it, because a mod's AI file
    /// could clear it — and then the same character would walk.
    /// </summary>
    static bool? Pace(Mind mind) => (mind.Class.AttackFlags & AttackFlagRun) != 0 ? true : false;

    /// <summary>`attackFlags` bit 0: close at a run rather than a walk.</summary>
    public const uint AttackFlagRun = 0x1;

    // --- the party ------------------------------------

    /// <summary>How often a party member thinks.</summary>
    public const int PartyThink = 800;

    /// <summary>
    /// How close a member is content to be to its leader, body to body, before the stance scales it
    /// (180). Inside it, it stops walking and stands.
    /// </summary>
    public const float FollowReach = 180f;

    /// <summary>
    /// Where the members stand: a fan behind the leader, three angles at each of two ranks, taken by
    /// the member's place in the party. A slot that has no walkable ground under it sends the member
    /// straight at the leader instead; a slot that is occupied is passed over for the next one, up to
    /// six tries.
    /// </summary>
    public static readonly float[] FormationAngle = [0f, 45f, -45f, 0f, 45f, -45f];
    /// <summary>And how far out each of those is (130 and 80).</summary>
    public static readonly float[] FormationRadius = [130f, 130f, 130f, 80f, 80f, 80f];

    /// <summary>What the stance does to the leash and to the formation's radius.</summary>
    public static float Leash(PartyStance stance) => ByStance(Rules.GameRules.Current.Ai.StanceLeash, stance);

    /// <summary>
    /// A party member's turn: the follow, which is the half a player watches. The handler's other
    /// halves are written too — the party's own combat decision is <see cref="PartyFight"/>, which
    /// also walks the discipline ladder, and the item use at its tail is <see cref="Nurse"/>.
    /// </summary>
    void PartyMember(Mind mind)
    {
        mind.NextThink = Now + Rules.GameRules.Current.Ai.PartyThinkMs;
        // The selected member has no party turn (compared with the party's selected member).
        if (mind.Thing.Actor is null || ReferenceEquals(mind.Thing, Selected)) return;
        // Getting out of somebody's way comes before everything else, and the request is
        // consumed whether or not a step is found.
        if (mind.MakeWayFor is { } asker)
        {
            mind.MakeWayFor = null;
            if (StepAside(mind, asker)) return;
        }
        if (mind.Target is { } target && PartyFight(mind, target)) return;
        if (!PartyChain(mind)) return;
        // Only an Attack-stance member goes looking. The engine's order is the combat decision
        // first, then this, then the follow.
        if (mind.Thing.Actor is { Stance: PartyStance.Attack } && Hunt(mind)) return;
        // The follow, and whether the nurse comes after it (<see cref="FollowedInstead"/>).
        if (FollowedInstead(mind)) return;

        // And the last clause of the follow's chain, which only a member with nothing else to do
        // reaches: badly hurt, it tries a healing item, then a healing spell, and if it is *also*
        // short of blood it drinks a vial. A short ladder of its own, separate from the combat one.
        Nurse(mind);
    }

    /// <summary>
    /// The party think's own tail: heal item, heal cast, then — only if short of blood
    /// as well — a vial. It runs when the member is badly hurt and has nothing else to do, so it is
    /// what a companion does standing next to you after a fight.
    /// </summary>
    void Nurse(Mind mind)
    {
        if (mind.Thing.Actor is not { } me || !Hurt(mind, me, mind.Class.VeryLowHealth)) return;
        if (UseItem(mind, AiReach.HealItem) is { Acted: true } salve) { Reached(mind, salve); return; }
        if (Cast(mind, AiReach.HealCast) is { Reach: not AiReach.None } spell) { Reached(mind, spell); return; }
        if (!LowBlood(mind, me)) return;
        if (UseItem(mind, AiReach.BloodItem) is { Acted: true } vial) Reached(mind, vial);
    }

    /// <summary>
    /// Says what a character reached for: the item it used, or the discipline it cast.
    /// </summary>
    void Reached(Mind mind, AiChoice choice)
    {
        if (choice.Item is { } item)
        {
            log.LogInformation("AI: {Name} reaches for {Item} ({Reach})",
                               mind.Thing.Template, item.Name, choice.Reach);
            LastReach = choice;
            return;
        }
        if (choice.Cast)
            log.LogInformation("AI: {Name} casts {Wanted} ({Reach})", mind.Thing.Template, choice.Wanted, choice.Reach);
        LastReach = choice;
    }

    /// <summary>The last thing anybody reached for, for a test or a diagnostic to look at.</summary>
    public AiChoice LastReach { get; private set; }

    /// <summary>
    /// <summary>
    /// The hunt an **Attack**-stance party member runs on its own — the thing
    /// that makes a companion pick fights nobody asked it to. It is why an aggressive party
    /// "will absolutely go after the villagers without prompting or provocation".
    ///
    /// It thinks on a **300 ms** clock rather than the party's usual 800, sweeps for everyone it
    /// could take against, and aims at the **nearest**. Finding somebody sets the next think to
    /// *now*, so the combat decision runs on the very next tick rather than a third of a second
    /// later. <c>AIMAIN_FLAG_NOPARTYSEARCH</c> switches it off and costs a full second.
    ///
    /// **Its reach is the AI file's own `searchDist`, not the 2,500 units the sphere query uses.**
    /// That query is a broad phase; the per-candidate test behind it (the sight test, called with
    /// the range flag set) then rejects anything past the class's range. An earlier note in this tree had
    /// the hunt reaching *further* than an ordinary search, which is backwards — the broad phase is
    /// wide so that the real limit can be applied per candidate, with perception and line of sight.
    /// </summary>
    bool Hunt(Mind mind)
    {
        if ((MainFlags & AiMainFlag.NoPartySearch) != 0)
        {
            mind.NextThink = Now + Rules.GameRules.Current.Ai.HuntThinkMs[1];
            return false;
        }
        mind.NextThink = Now + Rules.GameRules.Current.Ai.HuntThinkMs[0];

        WorldThing? nearest = null;
        float best = float.MaxValue;
        foreach (var other in Candidates(mind))
        {
            float gap = Vector3.Distance(other.Position, mind.Thing.Position);
            if (gap >= best) continue;
            best = gap;
            nearest = other;
        }
        if (nearest is null) return false;

        // Set-target (reacting the first time), `vox_alert`, and a think now.
        Hunted(mind, nearest);
        return true;
    }

    /// <summary>How often an Attack-stance member looks for a fight (300 ms).</summary>
    public const int HuntThink = 300;
    /// <summary>And how long it waits when `NOPARTYSEARCH` has switched it off (1, 000 ms).</summary>
    public const int HuntBlocked = 1000;

    /// The party's own combat decision — the ammo-aware sibling of
    /// <see cref="Fight"/>. It shares that function's shape and its whole tail, and differs in four
    /// ways that are each worth knowing:
    ///
    /// <list type="bullet">
    /// <item>the think delay is a plain <c>attackRate</c> with **no jitter**, where an enemy's is
    /// <c>attackRate</c> plus up to a fifth again;</item>
    /// <item>it opens with a **leash** on how far the fight may take it from the player
    /// (<see cref="BreakLeash"/>);</item>
    /// <item>it runs the **ammo gate** (<see cref="Loaded"/>), which the enemy version has no trace
    /// of — because an enemy never spends ammo in the first place;</item>
    /// <item>and a ranged attacker that is in range but has **no clear shot** sidesteps instead of
    /// firing (<see cref="Sidestep"/>, not the enemy's manoeuvre), rather than firing into whatever
    /// is in the way.</item>
    /// </list>
    ///
    /// It is also gated by <c>AIMAIN_FLAG_NOPARTYCOMBAT</c> rather than the enemy flag, and — unlike
    /// <see cref="Fight"/> — it does **not** test reach before ordering the attack or drop the target
    /// for want of a weapon. The order layer decides both, which is what the engine does here.
    /// </summary>
    bool PartyFight(Mind mind, WorldThing target)
    {
        mind.NextThink = Now + mind.Class.AttackRate;

        if (mind.Thing.Actor is not { } me) return false;
        // Losing the target does **not** change mode here, and that is the difference from
        // <see cref="Fight"/>: the engine's party version calls the target-setter with
        // nothing and returns, leaving the character in `party` for ever. Sending it to its base
        // mode instead — which this did until, in play, Erik finished a fight in the Tremere
        // chantry, paced away and never followed again — takes it out of the party permanently.
        if (BreakLeash(mind, me)) { mind.Aim(null, Now); return false; }
        if (!Worth(mind, target)) { mind.Aim(null, Now); return false; }
        // Left alone, it is **let go**, and the mind re-enters its base mode — `party` for a member —
        // without making it the base again, and thinks in a tenth of a second (`SetTarget(0)`,
        // then the home mode). Earlier, this kept the target, so a
        // companion went back to a victim the moment a friend's feed on it ended.
        if (LeaveAlone(mind, target))
        {
            mind.Aim(null, Now);
            mind.Enter(mind.BaseMode, andBase: false);
            mind.NextThink = Now + Rules.GameRules.Current.Ai.RetryCombatMs;
            return false;
        }
        // The discipline and item decision sits exactly here in the
        // engine, between deciding to fight and swinging, and a cast that lands ends the turn.
        // `Danger` is what the ladder escalates against, so it is refreshed first.
        mind.Danger = Threat(mind);
        if (Reach(mind, target) is { Reach: not AiReach.None } chose)
        {
            Reached(mind, chose);
            if (chose.Acted) return true;
        }
        // AIMAIN_FLAG_NOPARTYCOMBAT stops the swing, not the ladder: 1.1 tests it after the ladder,
        // so a companion told not to fight still casts and drinks (a comparison
        // with 1.1; Vitomir tested it first until then).
        if ((MainFlags & AiMainFlag.NoPartyCombat) != 0) { mind.NextThink = Now + Rules.GameRules.Current.Ai.RetryCombatMs; return false; }

        if (me.Weapon is { } weapon)
        {
            // Out of ammo — switched weapons or said so — the think is **not** spent on the fight
            // (it returns before the attack flag is set), so the party think goes on to hunt,
            // follow and nurse (this returned "acted" and the member stood still).
            if (!Loaded(mind, me)) return false;

            var current = weapon.Current;
            if (current.Is(Rpg.WeaponFlag.Ranged)
                && World.Bodies.InReach(mind.Thing, target, current.MaxDistance)
                && !ClearShot(mind.Thing, target))
            {
                Sidestep(mind, target);
                return true;
            }
        }

        int which = mind.Class.AttackAlt > 0f && Random.Unit() < mind.Class.AttackAlt ? 1 : 0;
        // **The order's answer is not read here either**: the party fight calls `AttackThing`
        // and shouts whatever it said, stamps the engagement and marks the
        // think as spent on the fight, which is what keeps the handler from following
        // on the same think. Vitomir returned false on a refused or busy order until then — no
        // shout, no stamp, and the member went on to follow the leader in the middle of its swing.
        Rpg.Actions.Attack(mind.Thing, target, me.Weapon, which, Now, motions: motions);
        Shout(mind, AiEvent.Attacked, Rules.GameRules.Current.Ai.AttackShoutRange);
        mind.EngagedAt = Now;
        return true;
    }

    /// <summary>
    /// How far a fight may drag a party member from the player before it simply stops.
    /// The distance is straight-line from <c>rpgParty::GetCurrentPlayer</c> — not from
    /// whoever is leading a formation — and the allowance is the AI file's <c>breakDist</c> scaled by
    /// the member's stance.
    ///
    /// **No shipped `.nai` sets `breakDist`**, so in practice everybody uses the engine's default of
    /// 500 and the three stances come out as **1,250 / 500 / 200** units. That is a third
    /// stance-scaled quantity with a third set of numbers (2.5 / 1.0 / **0.4**); it is *not* the
    /// follow leash (2.5 / 1.0 / 0.8) and *not* the health and blood thresholds (1.2 / 1.0 / 0.75),
    /// and merging any two of them would produce a plausible wrong answer.
    /// </summary>
    bool BreakLeash(Mind mind, World.Actor me)
    {
        if (mind.Class.BreakDist == 0f) return false;
        if (Selected is not { } player || ReferenceEquals(mind.Thing, player)) return false;
        float allowed = mind.Class.BreakDist * FightLeash(me.Stance);
        return Vector3.Distance(player.Position, mind.Thing.Position) > allowed;
    }

    /// <summary>The stance scales on <see cref="BreakLeash"/>.</summary>
    public static float FightLeash(PartyStance stance) => ByStance(Rules.GameRules.Current.Ai.StanceFightLeash, stance);

    /// <summary>
    /// Whether a ranged attack has a clear line: **bodies block it and
    /// walls do not** — the query is cast for characters only, the shooter excluded, dead or alive
    /// (<see cref="Rpg.ClearShot"/>). So a companion standing in the way makes the
    /// shooter sidestep rather than shoot a friend in the back, and a pillar does not. Earlier, this
    /// asked <see cref="Rpg.Sight.Sees"/>, which is the other way round.
    /// </summary>
    static bool ClearShot(WorldThing shooter, WorldThing target) => Rpg.ClearShot.Clear(shooter, target);

    /// <summary>
    /// Walking after the leader. Returns false when the member did not need
    /// to move, or could not find anywhere to stand.
    ///
    /// **The slot is chosen by a query of its own** (the free-spot query,
    /// <see cref="World.FreeSpot.Fits"/>): six slots at most, from the member's own round the fan
    /// (it gives up after the sixth); the first where the member's body touches nothing but
    /// itself is ordered (flags 0) and the follow answers **"followed" whatever the move
    /// order says** — refused by somebody standing there (its own free-spot test), busy, or
    /// no route. A slot with no ground under it sends the member at the leader himself instead
    /// (flags 0), and answers "followed" too — though with the leader standing on that point
    /// the move order's test refuses it every time. Earlier, Vitomir asked the move order to choose,
    /// trying the next slot only when the walk was refused, and tried seven.
    /// </summary>
    bool Follow(Mind mind, WorldThing leader)
    {
        // The stance's scale and the member's own slot are a companion's only: a player-type party
        // member after a player-type leader. Anybody else — a pet, a summoned
        // thing, a mesmerised follower — keeps 1.0 and the first slot (Vitomir gave a pet the
        // slot after the companions and its stance's scale).
        bool companion = mind.Thing.Actor!.IsPlayerThing && mind.Thing.Actor.Has(World.Actor.Flag.Party)
                         && leader.Actor?.IsPlayerThing == true;
        float scale = companion ? Leash(mind.Thing.Actor.Stance) : 1f;
        if (World.Bodies.Gap(mind.Thing, leader) <= scale * Rules.GameRules.Current.Ai.FollowReach)
        {
            mind.Walker.Stop();
            return false;
        }

        // The direction the member already lies in from its leader, flattened: the formation is laid
        // out around wherever it happens to be standing, not around the leader's facing.
        var away = mind.Thing.Position - leader.Position;
        away.Z = 0f;
        if (away.LengthSquared() < 1e-6f) away = new Vector3(1f, 0f, 0f);
        away = Vector3.Normalize(away);

        // Never past the fan's end (ours): 1.1's roster is four long, so its slot never is either.
        int slot = companion ? PartyOrdinal(mind) % Rules.GameRules.Current.Ai.FormationAngle.Length : 0;
        for (int tries = 0; tries < Rules.GameRules.Current.Ai.FormationAngle.Length; tries++, slot = (slot + 1) % Rules.GameRules.Current.Ai.FormationAngle.Length)
        {
            float radians = Rules.GameRules.Current.Ai.FormationAngle[slot] * (MathF.PI / 180f);
            float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
            var heading = new Vector3(away.X * cos - away.Y * sin, away.X * sin + away.Y * cos, 0f);
            var spot = leader.Position + heading * (Rules.GameRules.Current.Ai.FormationRadius[slot] * scale);

            // The ground under the slot, the engine's way (WalkMap.Beneath:
            // 50 above to 90 below; Vitomir's own window until then).
            int node = walk?.Beneath(spot) ?? -1;
            // Nowhere to stand there at all: give up on the formation and walk at the leader
            // — "followed", whatever the move order answered.
            if (node < 0) { Go(mind, leader.Position, LeaderNode(leader), run: null); return true; }
            // Something is already in that slot: try the next one round.
            if (!World.FreeSpot.Fits(mind.Walker, spot)) continue;
            Go(mind, spot, node, run: null);                            // Its answer unread
            return true;
        }
        return false;
    }

    /// <summary>Which of the leader's followers this one is, which picks its slot in the fan.</summary>
    int PartyOrdinal(Mind mind)
    {
        if (where is not null) return PartySlot(where.PartyMembers(), Selected, mind.Thing);
        // With no place, the minds in the world that are the party's, in the order they came.
        return PartySlot(All.Where(m => m.Thing.InWorld && m.Thing.Actor?.Has(World.Actor.Flag.Party) == true)
                            .Select(m => m.Thing), Selected, mind.Thing);
    }

    /// <summary>
    /// **A member's slot in the formation** (the loop over the party's members): its place in the
    /// party roster, one less when the selected member came before it, and 0 when it is not in the
    /// roster at all. The roster is the party now in the place (<see cref="Location.PartyMembers"/>).
    /// Earlier, Vitomir counted over every mind the place had ever held, and a place kept loaded keeps
    /// the minds of companions who left it, so each return pushed the slots up until one ran off the
    /// fan, as seen in play.
    /// </summary>
    public static int PartySlotOf(Location place, WorldThing member) =>
        PartySlot(place.PartyMembers(), place.Selected, member);

    static int PartySlot(IEnumerable<WorldThing> roster, WorldThing? selected, WorldThing member)
    {
        int i = 0;
        bool selectedFirst = false;
        foreach (var t in roster)
        {
            if (ReferenceEquals(t, selected)) selectedFirst = true;
            if (ReferenceEquals(t, member)) return selectedFirst ? i - 1 : i;
            i++;
        }
        return 0;
    }

    /// <summary>The leader's own walk node, for the give-up case.</summary>
    int LeaderNode(WorldThing leader) =>
        leader.Mind?.Walker.Node ?? walk?.Find(leader.Position, World.WalkMap.Reach) ?? -1;

    /// <summary>
    /// Reposition around the target (the enemy mode's tail). The direction is the **three-dimensional** one
    /// to the target (kept in the mind), turned about the vertical — so the
    /// spot climbs or drops as the line to the target does — and the ground under the spot is found
    /// the engine's way (<see cref="World.WalkMap.Beneath"/>). Once the spot was
    /// flat at the character's own height and the ground was looked for within Vitomir's own window,
    /// so on a staircase the spot fell through the steps and the character walked straight at its
    /// target instead.
    /// </summary>
    public void Manoeuvre(Mind mind, WorldThing target)
    {
        var to = target.Position - mind.Thing.Position;
        float gap = to.Length();
        if (gap <= 1e-3f) return;
        var unit = to / gap;

        // Too close is tested before the angle is rolled (the roll comes after), so a
        // character touching its target rolls nothing (Vitomir rolled first).
        float reach = gap - mind.Thing.Radius - target.Radius;
        if (reach < 0f) { mind.NextThink = Now + Rules.GameRules.Current.Ai.EnemyThinkMs; return; }

        float turn = (Random.Unit() * Rules.GameRules.Current.Ai.ManoeuvreArc[0] - Rules.GameRules.Current.Ai.ManoeuvreArc[0] / 2f) * (MathF.PI / 180f);
        float cos = MathF.Cos(turn), sin = MathF.Sin(turn);
        var heading = new Vector3(unit.X * cos - unit.Y * sin, unit.X * sin + unit.Y * cos, unit.Z);

        // A roll under the floor is the floor, **not** cut to the reach; only a longer
        // one is. So a character nearer its target than 50 still steps 50.
        float out_ = Random.Unit() * mind.Class.ChaseDist;
        out_ = out_ < Rules.GameRules.Current.Ai.ManoeuvreArc[1] ? Rules.GameRules.Current.Ai.ManoeuvreArc[1] : MathF.Min(out_, reach);

        var spot = mind.Thing.Position + heading * out_;
        bool? pace = Pace(mind);
        int node = walk?.Beneath(spot) ?? -1;
        // The move order without flag 0x100, so a spot somebody is already standing on is refused
        // — then the walk to the spot.
        if (node >= 0)
        {
            Go(mind, spot, node, pace);
            // One manoeuvre in five comes with a taunt (rand against 0.2) — only
            // on this branch, so a character walking straight at its target never taunts.
            if (Random.Unit() < Rules.GameRules.Current.Ai.TauntChance) Audio.Noise.Foley(mind.Thing, Audio.FoleyEvent.VoxTaunt);
        }
        // No ground there: walk at the target itself, on the **target's** walk node
        // and with the same flags — so the same refusal of an occupied point, and
        // the target is standing on it (Vitomir passed its own node and allowed it).
        else Go(mind, target.Position, SurfaceOf(target), pace);
        mind.NextThink = Now + mind.Class.MoveRate;
    }

    /// <summary>
    /// Runs everybody's walker, every frame — including the ones standing still, which is the
    /// point. The engine's walker updates whether or not it has anywhere to go, and the
    /// step where it has nowhere is what drops the character's speed to zero and puts it back on
    /// its <c>stand</c> animation. Skipping that step leaves a character frozen mid-stride the
    /// moment it arrives.
    ///
    /// The character the player is steering is left out: its route is walked by
    /// <see cref="World.SteeredWalk"/>, and while it has none the viewer's own step stands it still
    /// or drives it.
    /// </summary>
    public void Follow(float seconds)
    {
        foreach (var mind in All) FollowOne(mind, seconds);
    }

    /// <summary>One character's walker, from its own tick; not the steered one's.</summary>
    public void FollowOne(Mind mind, float seconds)
    {
        if (!ReferenceEquals(mind.Thing, Steered)) mind.Walker.Follow(seconds);
    }

    /// <summary>How many minds are in each mode right now.</summary>
    public IEnumerable<(AiMode Mode, int Count)> ByMode() =>
        All.GroupBy(m => m.Mode).Select(g => (g.Key, g.Count())).OrderBy(e => e.Item1);
}
