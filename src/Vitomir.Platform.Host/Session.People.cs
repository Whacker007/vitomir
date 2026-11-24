// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Control;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host;

/// <summary>
/// The session's side of the player's new body (<see cref="PeopleBodies"/>) and of the spring and the
/// arrows' notes: the body the content built, the inputs it has no combat for yet, and what a recording's head says of all three.
/// </summary>
public sealed partial class Session
{
    /// <summary>The player's body built from the models repository's files, or null for 1.1's Christof.</summary>
    PeopleBodies.Built? playerBody;

    /// <summary>Whether the session has said once that the new body has no combat yet.</summary>
    bool saidNoCombat;

    /// <summary>
    /// The inputs the new body has no combat for yet (the attack, the block, the bow's aim and its toggle, the swap):
    /// taken out of every frame before the tick, so they do nothing and nothing throws. Dodge, the quick slots and the rest work.
    /// </summary>
    public const Buttons NoCombatYet = Buttons.Attack | Buttons.Block | Buttons.Aim | Buttons.AimToggle | Buttons.SwapWeapon;

    /// <summary>The player's body as built — <c>models</c> or <c>woman</c> — or null for 1.1's Christof.</summary>
    public string? PlayerBody => playerBody?.Body;

    /// <summary>
    /// The models repository's the player's new body crouches to <see cref="SessionOptions.PersonCrouchHeight"/> (an earlier build:
    /// <see cref="PeopleBodies.CrouchHeight"/>, 1.15 m since the models repository): its profile given again as one of its
    /// own with that crouch (<see cref="PeopleBodies.Crouched"/>), or, in a world without profiles, its numbers so. Christof keeps his.
    /// After the modules have given the player its profile.
    /// </summary>
    void CrouchNewBody()
    {
        if (playerBody is not { } body || !world.Alive(player)) return;
        float height = Math.Clamp(options.PersonCrouchHeight, PeopleBodies.LeastCrouchHeight, PeopleBodies.MostCrouchHeight);
        if (world.TryService<MovementProfiles>(out var profiles) && world.TryStore<MasqueradeState>(out var states) && states.Has(player)
            && states.Get(player).Profile is var index && index >= 0 && index < profiles.All.Count)
            profiles.Give(world, player, PeopleBodies.Crouched(profiles, index, body.Body, height));
        else if (world.Has<Locomotion>(player)) world.Get<Locomotion>(player).CrouchHeight = height;
    }

    /// <summary>The frame with the new body's missing combat taken out (said once, the first time one is pressed); as it is for Christof.</summary>
    ActionFrame WithoutCombat(in ActionFrame actions)
    {
        if (playerBody is null || (actions.Held & NoCombatYet) == 0) return actions;
        if (!saidNoCombat)
        {
            saidNoCombat = true;
            options.Say("the new body has no combat yet: --body oneone for combat");
        }
        return actions with { Held = actions.Held & ~NoCombatYet };
    }

    /// <summary>
    /// The head with what an earlier build adds: the note <c>accel</c> when the world's bodies start and stop on the spring (none:
    /// The earlier build's ramp), <c>arrows</c> when the world has a bow (the items set's or, with <c>arrows-heavy</c>), and the player's
    /// body when it is the models repository's (none: Christof), with the models' commit.
    /// </summary>
    /// <summary>
    /// The head with the people's turn (<see cref="SessionOptions.TurnNote"/>: the chain's numbers or the earlier build's) and the kneel
    /// (<see cref="SessionOptions.CrouchKneelNote"/>), when anyone is on a body of the models repository's: the player, or the
    /// townsfolk drawn as its crowd, who turn and plant their feet by the same rules (a tape of Christof among the crowd
    /// lost these notes and its replay turned the townsfolk the earlier build's way). Christof's tapes among 1.1's townsfolk are as before. the
    /// people's sprint (<see cref="SessionOptions.SprintNote"/>) and, for the player's new body, its crouched capsule (<see cref="PeopleBodies.CrouchNote"/>).
    /// </summary>
    internal TapeHeader NotePeople(TapeHeader h)
    {
        if (playerBody is null && !TownsfolkOnCrowd) return h;
        h.Notes[SessionOptions.TurnNote] = options.PersonTurn.Note();
        h.Notes[SessionOptions.CrouchKneelNote] = options.CrouchKneel ? "true" : "false";
        h.Notes[SessionOptions.FeetNote] = options.PersonFeet.Note();
        h.Notes[SessionOptions.GroundNote] = options.PersonGround.Note();   // stairs and slopes
        h.Notes[Animation.People.PersonStartSettings.TapeNote] = options.PersonStarts.Note();   // the starts and stops
        h.Notes[Animation.People.PersonAirSettings.TapeNote] = options.PersonAir.Note();         // the jumps, the fall and the landings
        h.Notes[Animation.People.PersonOverSettings.TapeNote] = options.PersonOver.Note();       // over and onto things, off edges
        h.Notes[Animation.People.PersonLedgeSettings.TapeNote] = options.PersonLedge.Note();     // the ledge on clips
        h.Notes[Animation.People.PersonPrelandingSettings.TapeNote] = options.PersonPrelanding.Note();   // the prelanding
        h.Notes[Animation.People.PersonHandsSettings.TapeNote] = options.PersonHands.Note();             // the hands on the real top
        h.Notes[Animation.People.PersonCommitSettings.TapeNote] = options.PersonCommit.Note();           // the commit point in the air
        h.Notes[Animation.People.PersonMovesSettings.TapeNote] = options.PersonMoves.Note();             // tier 1's moves
        h.Notes[Animation.People.PersonHangSettings.TapeNote] = options.PersonHang.Note();               // the hang's corners and leaps
        h.Notes[Animation.People.PersonWallSettings.TapeNote] = options.PersonWall.Note();               // the walls
        h.Notes[Animation.People.PersonStealthSettings.TapeNote] = options.PersonStealth.Note();         // squeezing, stealth, cover, ducking
        h.Notes[Animation.People.PersonGaitSettings.TapeNote] = options.PersonGaits.Note();              // the player's own gaits
        h.Notes[SessionOptions.SprintNote] = SessionOptions.NoteOf(options.PersonSprintFrom);   // the people's sprint
        if (playerBody is not null) h.Notes[PeopleBodies.CrouchNote] = SessionOptions.NoteOf(options.PersonCrouchHeight);   // the player's crouched capsule
        if (playerBody is not null) h.Notes[SessionOptions.CameraWallsNote] = options.CameraHardWalls ? "hard" : "calm";   // the camera at walls (the player's new body's)
        return h;
    }

    internal TapeHeader NoteBody(TapeHeader h)
    {
        if (StartStopSprings.In(world)) h.Notes[StartStopSprings.TapeNote] = StartStopSprings.Note(world.Service<StartStopSprings>().Override);
        if (StartStopSprings.In(world) && !world.Service<StartStopSprings>().LandingSlide) h.Notes[StartStopSprings.LandingNote] = StartStopSprings.LandingStops;
        if (world.TryStore<Bow>(out _))
            h.Notes[PeopleBodies.ArrowsNote] = modules.Modules.Any(m => m.Switches.Contains(PeopleBodies.ArrowsHeavySwitch)) ? "eh1" : "items";
        if (playerBody is { } body)
        {
            h.Notes[SessionOptions.BodyNote] = body.Body;
            if (body.Commit is { Length: > 0 } commit) h.Notes[SessionOptions.BodyCommitNote] = commit;
        }
        if (crowd is { Any: true }) h.Notes[TownsfolkBodies.TapeNote] = crowd.Note;   // baked, or models (the earlier build's run-time crowd)
        return h;
    }

    /// <summary>Whether the session has said once that the models' files have no jumps, fall or landings (an older set).</summary>
    bool saidNoAir;

    /// <summary>
    /// A person given the models repository's jumps, fall and landings (<see cref="Animation.People.PeopleAir"/>) when they are on and its kind
    /// has them: <paramref name="curve"/> 1 the player's jump, 2 everyone else's. A kind without them (an older set) leaves, flies and lands as
    /// The earlier build's (said once in the log).
    /// </summary>
    void GiveAir(EntityId e, Animation.CharacterAssets assetsInWorld, int kind, int curve)
    {
        if (!options.PersonAir.Any) return;
        if (world.TryStore<Animation.People.PersonAir>(out _) && assetsInWorld.Kind(kind).Person?.Air is not null)
        {
            world.Set(e, new Animation.People.PersonAir { Curve = curve });
            return;
        }
        if (saidNoAir || assetsInWorld.Kind(kind).Person is null) return;
        saidNoAir = true;
        options.Say("people: the models' files have no jumps, fall or landings (an older set): the stand-ins in the air and landing");
    }

    /// <summary>Whether the session has said once that the models' files have no moves over and onto things or off edges (an older set).</summary>
    bool saidNoOver;

    /// <summary>
    /// A person given the models repository's moves over and onto things and off edges (<see cref="Animation.People.PeopleOver"/>,
    /// <see cref="Animation.People.PeopleOff"/>) when they are on and its kind has them. A kind without them (an older set) moves as before (said
    /// once in the log).
    /// </summary>
    void GiveOver(EntityId e, Animation.CharacterAssets assetsInWorld, int kind)
    {
        if (!options.PersonOver.Any || !options.PersonAir.Any) return;
        if (world.TryStore<Animation.People.PersonOver>(out _) && world.Has<Animation.People.PersonAir>(e) && assetsInWorld.Kind(kind).Person?.Over is not null)
        {
            world.Set(e, new Animation.People.PersonOver());
            // A kind with the moves but no prelandings (an older set) lands from the fall as before, said once.
            if (options.PersonPrelanding.Any && !saidNoPrelanding && assetsInWorld.Kind(kind).Person?.Air is { Prelandings.Length: 0 })
            {
                saidNoPrelanding = true;
                options.Say("people: the models' files have no prelandings (an older set): the standing landings from the fall without them");
            }
            return;
        }
        if (saidNoOver || assetsInWorld.Kind(kind).Person?.Air is null) return;
        saidNoOver = true;
        options.Say("people: the models' files have no moves over and onto things or off edges (an older set): the traversal's own paths");
    }

    /// <summary>Whether the session has said once that the models' files have no prelandings (an older set).</summary>
    bool saidNoPrelanding;

    /// <summary>Whether the session has said once that the models' files have no ledge clips (an older set).</summary>
    bool saidNoLedge;

    /// <summary>Whether the session has said once that the models' files have no ladder clips (an older set).</summary>
    bool saidNoLadder;

    /// <summary>Whether the session has said once that the models' files have no hang leaps or corners (an older set).</summary>
    bool saidNoHang;

    /// <summary>Whether the session has said once that the models' files have no wall clips (an older set).</summary>
    bool saidNoWall;

    /// <summary>Whether the session has said once that the models' files have no squeeze or cover clips (an older set).</summary>
    bool saidNoSide;

    /// <summary>
    /// The player given the models repository's ledge (<see cref="Animation.People.PeopleLedge"/>, BodyMotor.Ledge.cs) when it is on and
    /// its kind has it. Only the player: the models repository's note (ordinary townsfolk do not hang or shimmy; the clips are for trained and supernatural
    /// bodies). A kind without it (an older set) hangs as before (said once in the log).
    /// </summary>
    void GiveLedge(EntityId e, Animation.CharacterAssets assetsInWorld, int kind)
    {
        if (!options.PersonLedge.Any || !options.PersonOver.Any || !options.PersonAir.Any) return;
        if (world.TryStore<Animation.People.PersonLedge>(out _) && world.Has<Animation.People.PersonOver>(e) && assetsInWorld.Kind(kind).Person?.Ledge is not null)
        {
            world.Set(e, new Animation.People.PersonLedge());
            // The commit point in the air, with the ledge's clips (its catches are theirs).
            if (world.TryStore<Animation.People.PersonCommit>(out _)) world.Set(e, new Animation.People.PersonCommit());
            // The ladders on clips (tier 1's), with the commit point; a kind without them (an older set) climbs on the earlier build's stand-in, said once.
            bool committed = world.TryStore<Animation.People.PersonCommit>(out var commits) && commits.Has(e);
            if (world.TryStore<Animation.People.PersonLadder>(out _) && committed && assetsInWorld.Kind(kind).Person?.Ladder is not null)
                world.Set(e, new Animation.People.PersonLadder());
            else if (!saidNoLadder && options.PersonMoves is { On: true, Ladders: true } && committed && assetsInWorld.Kind(kind).Person?.Ladder is null)
            {
                saidNoLadder = true;
                options.Say("people: the models' files have no ladder clips (an older set): the ladder's stand-in");
            }
            // The hang's corners and leaps, with tier 1's moves; a kind without them (an older set) stops at corners and does not leap, said once.
            if (world.TryStore<Animation.People.PersonHang>(out _) && committed && assetsInWorld.Kind(kind).Person?.Ledge?.Paths.HasHangMoves == true)
                world.Set(e, new Animation.People.PersonHang());
            else if (!saidNoHang && options.PersonHang.On && options.PersonMoves.On && committed && assetsInWorld.Kind(kind).Person?.Ledge?.Paths.HasHangMoves != true)
            {
                saidNoHang = true;
                options.Say("people: the models' files have no hang leaps or corners (an older set): the hang without its moves (corners a stop, no leaps)");
            }
            // The walls, with the hang's corners and leaps; a kind without them (an older set) climbs no wall, said once.
            bool hung = world.TryStore<Animation.People.PersonHang>(out var hangs) && hangs.Has(e);
            if (world.TryStore<Animation.People.PersonWall>(out _) && hung && assetsInWorld.Kind(kind).Person?.Ledge?.Paths.HasWalls == true)
            {
                world.Set(e, new Animation.People.PersonWall());
                if (world.TryStore<Animation.People.PersonWallFlow>(out _)) world.Set(e, new Animation.People.PersonWallFlow());   // the flow
            }
            else if (!saidNoWall && options.PersonWall.On && options.PersonHang.On && hung && assetsInWorld.Kind(kind).Person?.Ledge?.Paths.HasWalls != true)
            {
                saidNoWall = true;
                options.Say("people: the models' files have no wall clips (an older set): no wall is climbed");
            }
            // Squeezing, stealth and cover, with the walls; a kind without the clips (an older set) has none of them, said once.
            bool walled = world.TryStore<Animation.People.PersonWall>(out var wallStore) && wallStore.Has(e);
            var sidePaths = assetsInWorld.Kind(kind).Person?.Ledge?.Paths;
            if (world.TryStore<Animation.People.PersonStealth>(out _) && walled && sidePaths is { HasSqueeze: true } or { HasCover: true })
                world.Set(e, new Animation.People.PersonStealth());
            else if (!saidNoSide && options.PersonStealth.On && options.PersonWall.On && walled && sidePaths is not ({ HasSqueeze: true } or { HasCover: true }))
            {
                saidNoSide = true;
                options.Say("people: the models' files have no squeeze or cover clips (an older set): no squeeze, no stealth, no cover");
            }
            return;
        }
        if (saidNoLedge || assetsInWorld.Kind(kind).Person?.Over is null) return;
        saidNoLedge = true;
        options.Say("people: the models' files have no ledge clips (an older set): the motor's own hang, shimmy, pull-up, drop and grabs");
    }

    // ---- the townsfolk on the models repository's crowd ----
    /// <summary>The townsfolk built on the crowd (each place's kind), or null when they are 1.1's.</summary>
    TownsfolkBodies.Built? crowd;

    /// <summary>Each townsperson drawn on the crowd, by entity: its 1.1 template (for <c>--sheet</c>, <c>--npc-actions</c>, <c>--protected</c>).</summary>
    readonly Dictionary<ulong, string> crowdTemplates = [];

    /// <summary>Whether the townsfolk are drawn on the models repository's crowd (any of them).</summary>
    public bool TownsfolkOnCrowd => crowd is { Any: true };

    /// <summary>The kind the townsperson made <paramref name="slot"/>th is drawn as on the crowd, or −1 for its 1.1 kind.</summary>
    int CrowdKind(int slot) => crowd is { } c && slot >= 0 && slot < c.Kinds.Length ? c.Kinds[slot] : -1;

    /// <summary>
    /// A townsperson drawn as the crowd's <paramref name="kind"/>: walking at the pace its walk is drawn for, so its feet keep to
    /// the ground; protected when its 1.1 kind is (the data protects kinds by name: the merchant); its template kept for the names.
    /// </summary>
    void DressInCrowd(EntityId e, string template, IReadOnlyDictionary<string, int> kinds, Animation.CharacterAssets assetsInWorld, int kind)
    {
        var walk = assetsInWorld.Kind(kind).Motion(Animation.MotionRole.Walk).NaturalSpeed;
        if (walk > 0.05f) world.Get<Locomotion>(e).WalkSpeed = walk;
        Animate(e, assetsInWorld, kind);
        // The townsfolk's starts and stops on the natural clips, their capsule moved by the clip (their walk the plain gait's).
        if (options.PersonStarts.Crowd && world.TryStore<Animation.People.PersonStep>(out _) && assetsInWorld.Kind(kind).Person is { Starts: not null, Walk: not null })
            world.Set(e, new Animation.People.PersonStep { Curve = 2 });
        GiveAir(e, assetsInWorld, kind, curve: 2);   // the townsfolk's falls and landings (and jump_natural, wired: nothing makes them jump)
        GiveOver(e, assetsInWorld, kind);            // and off edges (nothing makes them vault or climb)
        crowdTemplates[e.Packed] = template;
        if (kinds.TryGetValue(template, out int oneOne) && world.TryService<ContactConfig>(out var config) && config.KindProtected(assetsInWorld, oneOne))
            ProtectedPeople.Mark(world, e);
    }

    /// <summary>
    /// Every character but the player — its shown name, its entity's index, where its feet are, which way it faces (radians)
    /// and its kind's name — for a headless still's log and its <c>--frame-person</c>. Presentation only.
    /// </summary>
    public IReadOnlyList<(string Name, int Index, System.Numerics.Vector3 Feet, float Yaw, string Kind)> PeoplePlaces()
    {
        var assetsInWorld = world.Service<Animation.CharacterAssets>();
        var list = new List<(string, int, System.Numerics.Vector3, float, string)>();
        foreach (var e in characters)
        {
            if (e == player || !world.Alive(e) || !world.Has<Body>(e)) continue;
            ref var body = ref world.Get<Body>(e);
            string kind = world.Has<Animation.CharacterAnimation>(e) ? assetsInWorld.Kind(world.Get<Animation.CharacterAnimation>(e).Kind).Name : "";
            list.Add((NameOf(e.Packed) ?? "", e.Index, body.Feet, body.Yaw, kind));
        }
        return list;
    }

    /// <summary>A character's kind's name, or for a townsperson on the crowd its 1.1 template (as it was earlier).</summary>
    string KindNameOf(EntityId e, Animation.CharacterAssets assetsInWorld) =>
        crowdTemplates.TryGetValue(e.Packed, out var template) ? template
        : world.Has<Animation.CharacterAnimation>(e) ? assetsInWorld.Kind(world.Get<Animation.CharacterAnimation>(e).Kind).Name : "";
    // ---- end
}
