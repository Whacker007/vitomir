// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One character's mind: what mode it is in, when it next thinks, and what it does when it does.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ai;

/// <summary>
/// The engine's per-character AI record (168 bytes of it). One is attached to
/// every thing whose template names an AI file, and it is what <see cref="Minds"/> ticks.
/// </summary>
public sealed class Mind
{
    /// <summary>The character this mind belongs to.</summary>
    public required WorldThing Thing { get; init; }
    /// <summary>Its <c>.nai</c> file — shared with every other character of the same class.</summary>
    public required NaiFile Class { get; init; }
    /// <summary>
    /// What moves it. The engine has one walker for everybody (<c>simThing</c>'s) and the
    /// player is simply the one a human steers; <see cref="Player"/> is that walker.
    /// </summary>
    public required Player Walker { get; init; }

    /// <summary>What it is doing.</summary>
    public AiMode Mode { get; private set; }
    /// <summary>What it goes back to when a temporary mode ends.</summary>
    public AiMode BaseMode { get; private set; }
    /// <summary>
    /// AI flags: <see cref="FlagUnaffected"/> 0x1, noticed 0x2 (kept as
    /// <see cref="HasNoticed"/>), <see cref="FlagPet"/> 0x4, <see cref="FlagPossessed"/> 0x10000000.
    /// </summary>
    public uint Flags { get; set; }

    /// <summary>
    /// 0x1: nothing mind-altering takes — no mesmerize, daze, fear or frenzy mode (each of
    /// those modes tests it first). The behead path sets it.
    /// </summary>
    public const uint FlagUnaffected = 0x1;
    /// <summary>
    /// 0x4: a pet. A temporary mode ends back in `pet` rather than the home mode (the mode's end and its
    /// neighbours), and it keeps thinking while steered.
    /// </summary>
    public const uint FlagPet = 0x4;
    /// <summary>
    /// 0x10000000: possessed. A second possession is refused, and the body does not think
    /// — steered or not — unless something takes the controls.
    /// </summary>
    public const uint FlagPossessed = 0x10000000;

    /// <summary>A pet's master, by GUID, so a master that has left reads as nobody; 0 for none.</summary>
    public int MasterGuid { get; set; }

    /// <summary>
    /// The pets this character has: the list the engine makes on the first pet, four slots
    /// of GUIDs (<see cref="Minds.MaxPets"/>), 0 for an empty one. Null until the first.
    /// </summary>
    public int[]? Pets { get; set; }

    /// <summary>
    /// When the afraid mode may next say `vox_fear` and next shout event 1; the
    /// frenzied mode's next event-2 shout.
    /// </summary>
    public int NextFear { get; set; }
    /// <inheritdoc cref="NextFear"/>
    public int NextFearShout { get; set; }
    /// <inheritdoc cref="NextFear"/>
    public int NextFrenzyShout { get; set; }

    /// <summary>
    /// The unit vector toward the target, worked out at most once a think (a flag bit
    /// records it; the think clears the flags first) and turned by the afraid and dazed
    /// modes to pick where to wander.
    /// </summary>
    public System.Numerics.Vector3 Heading { get; set; }
    /// <summary>Whether <see cref="Heading"/> has been worked out this think.</summary>
    public bool HeadingKnown { get; set; }
    /// <summary>When it may think again, on the engine's millisecond clock.</summary>
    public int NextThink { get; set; }
    /// <summary>When its next idle noise is due.</summary>
    public int NextNoise { get; set; }

    /// <summary>
    /// **The home**: where the character came into the world — its spawn transform's
    /// translation (<see cref="World.WorldThing.SpawnPosition"/>), copied here by
    /// the think for as long as <see cref="HomeNode"/> is still unknown. A searcher that
    /// finds nobody walks back to it.
    /// </summary>
    public Vector3 Home { get; private set; }
    /// <summary>The walk node within 20 units of <see cref="Home"/>, or −1 — the engine's 0 — while none is known.</summary>
    public int HomeNode { get; private set; } = -1;
    /// <summary>How close to the home the engine will accept a node.</summary>
    public const float HomeReach = 20f;

    /// <summary>The first of the interest spots this character may use, and how many.</summary>
    public int FirstSpot { get; private set; }
    public int SpotCount { get; private set; }

    /// <summary>
    /// When the mind is next free to be told to move.
    ///
    /// <b>This is NOT the same field as <c>World.Actor.BusyUntil</c>, and the two do not talk to
    /// each other.</b> <c>Actor.BusyUntil</c> is the mid-action timer — a swing sets it, an order,
    /// a stow and the inventory hand all refuse while it runs, death and conversation clear it —
    /// and <b>no AI code reads it</b>. This one is written in exactly one place, the flinch/make-way
    /// wake, and read by <c>Ready</c>, <c>Enemy</c> and the cast ladder; <c>Search</c> and the party
    /// modes never consult it at all. So a character in the middle of a swing is not "busy" as far
    /// as its own mind is concerned. The original has one busy field; we have two,
    /// and which one a new caller wants is not obvious.
    /// </summary>
    public int BusyUntil { get; set; }

    /// <summary>
    /// When it next considers an idle flourish, on the game clock
    /// (Four seconds on after a roll that missed or a moment it was busy or moving,
    /// thirty after a flourish. It starts at 0, so a character's first think considers one.
    /// </summary>
    public int NextIdle { get; set; }

    /// <summary>
    /// What it is fighting. The engine keeps a handle rather than a pointer so that a
    /// target which is destroyed reads back as nothing; here a destroyed thing
    /// (<see cref="WorldThing.Removed"/>) reads back as nothing the same way. Earlier, nothing
    /// cleared it: a mind whose target was destroyed alive — a summoned pet whose time ran out — went
    /// on holding it, and a party member's AI cast Feed at it twenty seconds later (in play:
    /// *"&lt;ERROR&gt;: Invalid target in DisciplineFeed"*, the script's own complaint about a GUID
    /// that is nobody's).
    /// </summary>
    public WorldThing? Target
    {
        get => target is { Removed: true } ? null : target;
        private set => target = value;
    }

    WorldThing? target;

    /// <summary>
    /// When <see cref="AiMode.Approach"/> gives up: ten seconds after the enemy mode lost
    /// sight of its target.
    /// </summary>
    public int ApproachUntil { get; set; }

    /// <summary>When the target was taken, on the engine's millisecond clock.</summary>
    public int TargetAt { get; private set; }

    /// <summary>
    /// When the target was last actually engaged. Zero while it has only ever been chased,
    /// which is what lets a character give up on someone it has never reached.
    /// </summary>
    public int EngagedAt { get; set; }

    /// <summary>
    /// How much trouble the character thinks it is in — a band from 0 (no foes) through 1
    /// (my side more than three times as strong) to 7 (half as strong or less) of the balance of
    /// strength within 512 units (<see cref="Minds.Threat"/>), refreshed by the enemy
    /// think and the party think. The discipline ladder unlocks its rungs
    /// against it, so this number is the whole difference between a companion sipping a vial and
    /// one throwing everything it has.
    /// </summary>
    public int Danger { get; set; }

    /// <summary>
    /// The eight discipline and item cooldowns, one per <see cref="AiTimer"/>.
    /// Heal is deliberately one slot for both the spell and the salve.
    /// </summary>
    readonly int[] ready = new int[8];

    /// <summary>Whether a rung's cooldown has expired.</summary>
    public bool Ready(AiTimer timer, int now) => ready[(int)timer] <= now;

    /// <summary>Whether <b>any</b> of them has — the one test the discipline choice makes before walking the ladder.</summary>
    public bool AnyReady(int now)
    {
        foreach (int at in ready) if (at <= now) return true;
        return false;
    }

    /// <summary>Puts a rung on cooldown until this moment.</summary>
    public void Arm(AiTimer timer, int until) => ready[(int)timer] = until;

    /// <summary>When a rung next comes up, for a diagnostic that wants to say so.</summary>
    public int ReadyAt(AiTimer timer) => ready[(int)timer];

    /// <summary>
    /// When this character may complain about having nothing to shoot again.
    /// It is a **rate limit on the message, not on the check**: the gate is tested every
    /// think, and only the telling is held back to once every
    /// <see cref="Minds.OutOfAmmoMessage"/> ms. Reset to zero the moment it has ammo again.
    /// </summary>
    public int OutOfAmmoAt { get; set; }

    /// <summary>
    /// Who asked this character to get out of the way (which holds the
    /// asker's **thing handle**). Set by AI event 7 and consumed by the party handler on its next
    /// think, whether or not a step is actually found. Null the rest of the time.
    /// </summary>
    public World.WorldThing? MakeWayFor { get; set; }

    /// <summary>
    /// The mind's "reacted" bit: this character has already reacted to seeing somebody, so
    /// it does not play the noticing again for the next one.
    /// </summary>
    public bool HasNoticed { get; set; }

    /// <summary>How many times it has taken a target (any change to somebody).</summary>
    public int Acquisitions { get; private set; }

    /// <summary>
    /// How many times it has taken a target **with none before it** — a take after a drop. This is
    /// what the oscillation check counts (<see cref="Minds"/>, ours): a mode that drops what a search
    /// hands straight back goes nobody → somebody every time. A target handed over for another is not
    /// that: the rally hands the selected member every listener of a shout in turn, in one frame,
    /// which is 1.1's own and would read as a loop.
    /// </summary>
    public int Retakes { get; private set; }

    /// <summary>
    /// The clock when <see cref="Retakes"/> was last sampled, and what it stood at then. A
    /// mind that takes a target, loses it and takes it again several times a second is stuck in a
    /// loop between two modes that disagree about it, and <see cref="Minds"/> says so once rather
    /// than letting it hide behind a number that only moves on a frame-by-frame video.
    /// </summary>
    public int SampledAt { get; set; }
    /// <inheritdoc cref="SampledAt"/>
    public int SampledRetakes { get; set; }
    /// <inheritdoc cref="SampledAt"/>
    public bool Flapped { get; set; }

    /// <summary>Takes a target, or lets one go.</summary>
    public void Aim(WorldThing? target, int now) => Aim(target, now, target);

    /// <summary>
    /// Takes a target, or lets one go — and then, for a master, hands his pets what he
    /// was <paramref name="asked"/> to take (<see cref="Minds.PetsFollow"/>):
    /// every time, the same target again included, and a <c>null</c> too, since 1.1's
    /// <c>SetTarget</c> has no early way out for either. What is handed on is the argument, not what
    /// was kept after the check.
    /// </summary>
    public void Aim(WorldThing? target, int now, WorldThing? asked)
    {
        Take(target, now);
        if (Pets is not null) Following?.Invoke(this, asked);
    }

    /// <summary>
    /// Who hands a master's pets his target (<see cref="Minds.PetsFollow"/>), set when the mind is
    /// made; null for a mind nobody manages.
    /// </summary>
    internal Action<Mind, WorldThing?>? Following { get; set; }

    /// <summary>
    /// The store half of the set-target. **Every** set — the same target again, or nobody again, included —
    /// stamps the time the target was taken, clears the engaged stamp and
    /// asks for a think now; only the heading bit waits for a change. The
    /// engine's one early way out compares the target with the character itself, not
    /// with the target it had. Earlier, this returned early on the same target, so a re-hand kept
    /// the old stamps and the six-second give-up ran from the first take.
    /// </summary>
    void Take(WorldThing? target, int now)
    {
        if (!ReferenceEquals(Target, target))
        {
            Diagnostics.Journal.Current?.Write(now, Diagnostics.JournalKind.Target,
                ("who", Diagnostics.Watch.Name(Thing)), ("mode", Mode),
                ("took", target is null ? null : Diagnostics.Watch.Name(target)),
                ("dropped", Target is null ? null : Diagnostics.Watch.Name(Target)),
                ("gap", target is null ? null : World.Bodies.Gap(Thing, target)));
            if (target is not null && Target is null) Retakes++;
            Target = target;
            HeadingKnown = false;                 // A new target is a new heading
            if (target is not null) Acquisitions++;
        }
        TargetAt = now;
        EngagedAt = 0;
        NextThink = now;
    }

    /// <summary>Where it was last sent, for diagnostics.</summary>
    public int LastSpot { get; private set; } = -1;
    /// <summary>How many times it has been told to move.</summary>
    public int Moves { get; private set; }
    /// <summary>How many times a spot was refused for being outside <c>wanderRadius</c>.</summary>
    public int OutOfRange { get; private set; }

    /// <summary>Is it in motion? The engine asks <c>simThing</c> and the peds wait for it.</summary>
    public bool Moving => Walker.Walking;

    /// <summary>Puts it in a mode, as the engine's mode change does. The second mode follows when asked.</summary>
    public void Enter(AiMode mode, bool andBase = true)
    {
        if (mode != Mode)
            Diagnostics.Journal.Current?.Write(NextThink, Diagnostics.JournalKind.Mode,
                ("who", Diagnostics.Watch.Name(Thing)), ("from", Mode), ("to", mode));
        AiTrace.Changed(this, Mode, mode);                  // `aitrace`
        Mode = mode;
        if (andBase) BaseMode = mode;
    }

    /// <summary>
    /// Works out the interest-spot run — what the engine does when the AI is attached.
    /// The home is not this; it is <see cref="LookUpHome"/>, on the think.
    /// </summary>
    public void Settle(InterestSpots spots, WalkMap? walk)
    {
        (FirstSpot, SpotCount) = spots.Run(Class.InterestNum);
    }

    /// <summary>
    /// The top of every think: while the home node is still 0, copy the home from the
    /// character's spawn transform and look for the walk node under it, within
    /// 20 units. A character standing on no ground at all keeps
    /// asking, every think, which is what the engine does too.
    ///
    /// **The history, because it was wrong twice.** This tree first looked the node up near a
    /// home it believed nothing ever wrote — the origin — and sent every Cappadocian in the
    /// monastery walking toward 0,0,0 past the monks. The cure for that was to have no home at
    /// all, which left every guard standing wherever it lost you. In 1.1 the guards walk
    /// back to their posts, and the write the earlier readings missed is the
    /// finalize's block copy of the whole matrix (<c>rep movsd</c>), which names no
    /// offset. The home is where the scene put the character; the Cappadocians go back to the
    /// crypt, which is nowhere near the monks.
    /// </summary>
    public void LookUpHome(WalkMap? walk)
    {
        if (HomeNode >= 0) return;
        Home = Thing.SpawnPosition;
        if (walk is not null) HomeNode = walk.Find(Home, HomeReach);
    }

    /// <summary>THING_AF_AIPAUSED, and whether it is dead: both stop the AI where it stands.</summary>
    public bool Paused => Thing.Actor is { } a && (a.Has(Actor.Flag.AiPaused) || a.Has(Actor.Flag.Dead));

    /// <summary>Records a spot it was sent to.</summary>
    internal void Went(int spot) { LastSpot = spot; Moves++; }
    /// <summary>Records a spot it turned down.</summary>
    internal void Refused() => OutOfRange++;
}
