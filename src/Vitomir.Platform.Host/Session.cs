// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Host;

/// <summary>What a session is built from: the level, the 1.1 install, the game and its modules' switches, where to start, who walks about, the seed.</summary>
public sealed record SessionOptions
{
    public required string Level { get; init; }

    /// <summary>The 1.1 install (the <c>oneone:</c> source and 1.1's characters), or null to look for it.</summary>
    public string? Game { get; init; }

    /// <summary>The game played: an id (<c>Games/&lt;id&gt;.json</c> beside the program) or a game file; null for <see cref="ModuleLoader.DefaultGame"/>.</summary>
    public string? GameList { get; init; }

    /// <summary>A folder of module folders searched after the program's own <c>Modules/</c> (<c>--modules</c>), or null.</summary>
    public string? ModulesFolder { get; init; }

    /// <summary>The switches given (<c>--sandbox</c> is <c>sandbox</c>); each module is given those its manifest declares.</summary>
    public IReadOnlySet<string> Switches { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The start to begin at (<c>--start</c>): its number in the level's list, or its name (<see cref="LevelSpace.TryFindStart"/>).</summary>
    public string Start { get; init; } = "0";
    public int People { get; init; } = 6;
    public ulong Seed { get; init; } = 1;
    /// <summary>The player's third-person facing (the view's by default), or a tape's.</summary>
    public ThirdPersonFacing Facing { get; init; } = ThirdPersonFacing.View;
    /// <summary>Always run (<see cref="TickInput.AlwaysRun"/>): the player's setting, or a tape's.</summary>
    public bool AlwaysRun { get; init; } = true;
    /// <summary>
    /// The legs' turn (<see cref="Animation.LegTurn"/>; the setting <c>legTurn</c>, <c>--no-leg-turn</c>);
    /// carried in a recording's head (a tape without the note was made before it: off).
    /// </summary>
    public bool LegTurn { get; init; } = true;
    /// <summary>
    /// Double tap to dodge (<see cref="TickInput.DoubleTapDodge"/>): the window in seconds, 0 for off (the
    /// settings <c>doubleTapDodge</c>, off by default, and <c>doubleTapSeconds</c>); carried in a recording's head.
    /// </summary>
    public float DoubleTapDodge { get; init; }
    /// <summary>
    /// Speed by direction (<see cref="Bodies.DirectionPace"/>:, backpedal 0.70, sidestep 0.90);
    /// on by default, <c>--no-direction-pace</c> turns it off; carried in a recording's head (without the note: off).
    /// </summary>
    public bool DirectionPace { get; init; } = true;
    /// <summary>
    /// How the player's third-person blow is pitched (<see cref="Control.ThirdPersonAimSettings"/>: the view's pitch
    /// by default; from the reticle's point with the setting <c>thirdPersonAimFromReticle</c> or <c>--aim-from-reticle</c>);
    /// carried in a recording's head (a tape without the note was made earlier: from the reticle).
    /// </summary>
    public Control.ThirdPersonAimSettings ThirdPersonAim { get; init; } = new();

    /// <summary>
    /// How the player's blow's direction is chosen (<see cref="Control.AttackDirectionInput"/>: automatic by
    /// default; the setting <c>attackDirection</c>, <c>--attack-direction</c>); carried in a recording's head (without the note: automatic).
    /// </summary>
    public Control.AttackDirectionInput AttackDirection { get; init; } = new();

    /// <summary>The tape head's note for <see cref="DirectionPace"/>.</summary>
    public const string DirectionPaceNote = "directionPace";

    /// <summary>
    /// The global combat tempo over the rules module's own (<c>--combat-tempo &lt;x&gt;</c>, an arena spec's
    /// <c>combat_tempo</c>; <see cref="CombatTempoOverride"/>), or null for the module's (the enhanced module's pace.json, 0.70);
    /// carried in a recording's head (without the note: the module's).
    /// </summary>
    public float? CombatTempo { get; init; }

    /// <summary>A tempo as the program takes it: a number within <see cref="CombatTempoOverride.Least"/> to <see cref="CombatTempoOverride.Most"/>, else null.</summary>
    public static float? TempoOf(string? text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v >= CombatTempoOverride.Least && v <= CombatTempoOverride.Most ? v : null;

    /// <summary>The player's own movement profile by name or id (<c>--profile human|neonate|elder</c>), or null for the game's choice (the human).</summary>
    public string? BodyProfile { get; init; }

    /// <summary>
    /// Where to put the player before the first tick (<c>--place x,y,z[,yaw[,view]]</c>, <see cref="Placement"/>), or null
    /// for the start anchor's place; for headless stills and in play, carried in a recording's head.
    /// </summary>
    public string? Place { get; init; }

    /// <summary>Whether the masquerade switches by itself with the place (on by default; <see cref="MasqueradeSettings.AutoSwitch"/>).</summary>
    public bool AutoMasquerade { get; init; } = true;

    /// <summary>
    /// How long the player lies after a zero-Health stop before the sandbox recovery gets it up (s; <see cref="RecoveryServices"/>;
    /// ENGINE_SANDBOX, a convenience, not a rule), 0 for never (<c>--no-recovery</c>, the setting <c>playerRecovers</c>); carried in
    /// a recording's head. The arena never recovers anyone.
    /// </summary>
    public float RecoverSeconds { get; init; } = DefaultRecoverSeconds;

    /// <summary>The sandbox recovery's wait by default (s; ENGINE_SANDBOX).</summary>
    public const float DefaultRecoverSeconds = 5f;

    /// <summary>
    /// The developer's standing target request (<c>--target-test &lt; range&gt; [,nolos][,person|place|any]</c>,
    /// <see cref="TargetRequest.TryParse"/>), or null; carried in a recording's head.
    /// </summary>
    public string? TargetTest { get; init; }

    /// <summary>
    /// Observers put where a test wants them (<c>--witness &lt; kind&gt; @x, y, z[,yaw]</c>, repeatable,
    /// <see cref="WitnessRequest.TryParse"/>), carried in a recording's head.
    /// </summary>
    public IReadOnlyList<string> Witnesses { get; init; } = [];

    /// <summary>
    /// Hostile fighters placed in play (<c>--foe &lt;who&gt;@x,y,z[,yaw]</c>, repeatable; <see cref="FoeRequest"/>, the
    /// presets <see cref="FoePresets"/>), each coming for the player with the duelling brain; carried in a recording's head.
    /// </summary>
    public IReadOnlyList<string> Foes { get; init; } = [];

    /// <summary>Whether it is night (<c>--time night</c>, the default) or day (<c>--time day</c>): the light perception sees by and the townsfolk out; carried in a recording's head.</summary>
    public bool Night { get; init; } = true;

    /// <summary>The tape head's note for <see cref="Night"/>: <c>night</c> or <c>day</c>.</summary>
    public const string TimeNote = "time";

    /// <summary>The tape head's note for <see cref="LegTurn"/>.</summary>
    public const string LegTurnNote = "legTurn";

    /// <summary>
    /// The 1.1 weapon the player carries (<c>--weapon</c>), or null for <see cref="DefaultWeapon"/>; carried in a
    /// recording's head.
    /// </summary>
    public string? Weapon { get; init; }

    /// <summary>
    /// The cameras' numbers — the player's <c>firstPersonActionSway</c> and <c>thirdPersonFollow</c>,
    /// <c>--camera-rigid</c>, or the camera survey's — or null for <see cref="CameraSettings"/>' defaults. They move the
    /// simulation's view, so a tape records them (<see cref="CamerasNote"/>).
    /// </summary>
    public CameraSettings? Cameras { get; init; }

    /// <summary>The tape's note of <see cref="Cameras"/> (<see cref="CameraSettings.ToNote"/>; empty for the defaults).</summary>
    public const string CamerasNote = "cameras";

    /// <summary><c>--play-as &lt;template&gt;</c>, the kind the player is drawn and animated as (a developer's option for pictures; null: Christof).</summary>
    public string? PlayAs { get; init; }

    /// <summary>
    /// <c>--stitched-as-quick overhead|thrust|both</c>: every kind that has the stitched blows plays them as its
    /// quick blows in place of 1.1's (for measuring their blow quality and filming them before the direction table plays them);
    /// null: 1.1's quick blows. Carried in a recording's head (<see cref="StitchedAsQuickNote"/>).
    /// </summary>
    public string? StitchedAsQuick { get; init; }

    /// <summary>The tape head's note for <see cref="StitchedAsQuick"/>.</summary>
    public const string StitchedAsQuickNote = "stitchedAsQuick";

    /// <summary>
    /// Christof's sword when nothing else is asked: 1.1's <c>BroadSword</c>, the weapon his <c>.NPC</c> kit equips at the
    /// start of a new game (<c>item broadsword equip</c>; the faithful engine gives him the same).
    /// </summary>
    public const string DefaultWeapon = "BroadSword";

    /// <summary>The tape head's note for <see cref="Weapon"/>.</summary>
    public const string WeaponNote = "weapon";

    /// <summary>Townsfolk and guards made to act on a schedule (<c>--npc-actions</c>, <see cref="ActionScript"/>), or null; carried in a recording's head.</summary>
    public string? NpcActions { get; init; }

    /// <summary>
    /// Sheets given in place of the modules' own (<c>--sheet &lt; who&gt; =&lt; file&gt; </c>, repeatable, the arena's route
    /// too): who as for <c>--npc-actions</c>, or <c>Christof</c> / <c>player</c> for the player; given through the rules
    /// module's <see cref="IPersonSheets"/> before the first tick. Carried in a recording's head with each file's text.
    /// </summary>
    public IReadOnlyList<SheetRequest> Sheets { get; init; } = [];

    /// <summary>The tape head's note for <see cref="Sheets"/> (a JSON list of who, file and text).</summary>
    public const string SheetsNote = "sheets";

    /// <summary>
    /// Powers learned before the first tick (<c>--learn [&lt;who&gt;@]&lt; power&gt; =&lt; rank&gt; [:&lt;option&gt;,…]</c>, repeatable,
    /// The powers work): the player by default, else who as for <c>--sheet</c>; with no options, the power's options as the module
    /// lists them (<see cref="IPersonSheets.OptionsOf"/>). After the sheets; carried in a recording's head.
    /// </summary>
    public IReadOnlyList<LearnRequest> Learn { get; init; } = [];

    /// <summary>The tape head's note for <see cref="Learn"/> (the requests as given, joined by ';').</summary>
    public const string LearnNote = "learn";

    /// <summary>Armour worn before the first tick (<c>--armor &lt;who&gt;=&lt;piece&gt;[,…]</c>, repeatable; after the sheets and the powers; carried in a recording's head).</summary>
    public IReadOnlyList<ArmorRequest> Armor { get; init; } = [];

    /// <summary>The tape head's note for <see cref="Armor"/> (the requests as given, joined by ';').</summary>
    public const string ArmorNote = "armor";

    /// <summary>A file to write the rules' resolution traces to, one JSON line per contact (<c>--combat-trace</c>), or null.</summary>
    public string? CombatTrace { get; init; }

    /// <summary>A file to write the event trace to (<c>--event-trace</c>, <see cref="Host.EventTrace"/>), or null. Not the world's state.</summary>
    public string? EventTrace { get; init; }
    /// <summary>The people marked protected (<c>--protected</c>, <see cref="ProtectedPeople"/>), or null; carried in a recording's head.</summary>
    public string? Protected { get; init; }
    /// <summary>The arena's cast (<see cref="Arena.ArenaCast"/>, as JSON): its fighters instead of the player and the townsfolk; carried in a recording's head.</summary>
    public string? Arena { get; init; }

    /// <summary>The tape head's note for <see cref="Arena"/>.</summary>
    public const string ArenaNote = "arena";

    /// <summary>What is loaded before the world, kept across sessions by the arena, or null to load it; used only when it was loaded for these options.</summary>
    public SessionContent? Content { get; init; }

    public Action<string> Say { get; init; } = _ => { };

    // ---- ----
    /// <summary>A scenario's program (<see cref="Control.ScenarioProgram"/>, as JSON) for the cast's fighters: the scenario director is registered only when it is given; carried in a recording's head (note <see cref="ScenarioNote"/>, written only when given).</summary>
    public string? Scenario { get; init; }

    /// <summary>The tape head's note for <see cref="Scenario"/>.</summary>
    public const string ScenarioNote = "scenario";
    // ---- end

    // ---- the override work (at the end) ----
    /// <summary>
    /// Module data values changed for this session (<see cref="Modules.DataOverrides.ToNote"/>'s JSON: a scenario's
    /// <c>overrides.data</c>), applied to the modules' reads before they are configured; null for none. Part of the content's key;
    /// carried in a recording's head (note <see cref="DataOverridesNote"/>, written only when given).
    /// </summary>
    public string? DataOverrides { get; init; }

    /// <summary>The tape head's note for <see cref="DataOverrides"/>.</summary>
    public const string DataOverridesNote = "dataOverrides";

    /// <summary>
    /// The light perception sees by in place of the time's (night 0.3, day 1.0): <c>0.45</c> over the whole run,
    /// or steps <c>0:0.3;5:1</c> (seconds from the start : level 0 to 1, each held until the next); null for the time's
    /// (<see cref="Perception.LightSchedule"/>). Carried in a recording's head (note <see cref="LightNote"/>, written only when given).
    /// </summary>
    public string? Light { get; init; }

    /// <summary>The tape head's note for <see cref="Light"/>.</summary>
    public const string LightNote = "light";
    // ---- end

    // ---- the bow work (at the end) ----
    /// <summary>
    /// The bow the sandbox's player carries beside his melee weapon (the module's id, <c>war_bow</c>; the program's default,
    /// <c>--no-bow</c> for none), or null: then the world has no bow (as earlier). Not in an arena or a scenario (their casts say).
    /// Carried in a recording's head (<see cref="PlayerBowNote"/>); a tape without the note had none, so it replays as it was.
    /// </summary>
    public string? PlayerBow { get; init; }

    /// <summary>The arrows in the player's quiver (<c>--bow-arrows</c>, 24 by default; ENGINE_SANDBOX).</summary>
    public int PlayerArrows { get; init; } = DefaultPlayerArrows;

    /// <summary>The player's quiver by default (ENGINE_SANDBOX, ours: two scenario quivers of 12).</summary>
    public const int DefaultPlayerArrows = 24;

    /// <summary>The bow the program gives the player by default (the module's war bow).</summary>
    public const string DefaultPlayerBow = "war_bow";

    /// <summary>The tape head's note for <see cref="PlayerBow"/> and <see cref="PlayerArrows"/> (<c>war_bow:24</c>; empty for none).</summary>
    public const string PlayerBowNote = "playerBow";
    // ---- end

    // ---- the bow work (at the end) ----
    /// <summary>
    /// How the player's bow reads its controls (<see cref="Control.PlayerBowSettings"/>: aim mode as designed and the
    /// arrow along the line to the crosshair's point by default; the setting <c>bowAimSolve</c>, the switches <c>bow-solve-on</c> and
    /// <c>bow-aim-off</c>); null: from the switches. Carried in a recording's head (<see cref="Control.PlayerBowSettings.TapeNote"/>): a
    /// tape with the player's bow and without the note was the earlier build's, and replays by the earlier build's rules.
    /// </summary>
    public Control.PlayerBowSettings? PlayerBowAim { get; init; }
    // ---- end

    // ---- development controls (at the end) ----
    /// <summary>
    /// The view the player starts in (<i>"Yes, but you can add it when it makes sense to do so"</i>)
    /// (<c>first</c>, <c>right</c>, <c>left</c>: <see cref="CameraRig.ViewName"/>); null: over the right shoulder, as always. Play takes it
    /// from <c>--view</c> or the setting <c>lastView</c> (the view he left the last session in); headless runs never do. Carried in a
    /// recording's head (<see cref="StartViewNote"/>) when it is not the right shoulder, so a tape starts where its run did.
    /// </summary>
    public string? StartView { get; init; }

    /// <summary>The tape head's note for <see cref="StartView"/>; a tape without it started over the right shoulder.</summary>
    public const string StartViewNote = "startView";
    // ---- end

    // ---- the movement work (at the end) ----
    /// <summary>
    /// The player's body is the models repository's man on its male base: the player's body —
    /// <c>models</c> (<see cref="Assets.People.PersonRecipe.Player"/>), <c>woman</c> (<see cref="Assets.People.PersonRecipe.Woman"/>),
    /// or <c>oneone</c> / null, 1.1's Christof as before. Play takes it from <c>--body</c>, else the setting <c>playerBody</c> (models by
    /// default); the arena, scenarios, <c>--play-as</c> and the surveys keep 1.1's. When the models repository's files or its loader are
    /// missing the session falls back to Christof and says why in one line. Carried in a recording's head (<see cref="BodyNote"/>,
    /// written only for a body built: a tape without it is Christof's).
    /// </summary>
    public string? Body { get; init; }

    /// <summary>The models repository's folder (its repository, or a package's <c>People</c> copy), or null to look for it (<see cref="PeopleBodies.FindFolder"/>).</summary>
    public string? ModelsFolder { get; init; }

    /// <summary>
    /// The body must be built as asked (a replay of a tape whose note names it): a session that cannot build it stops
    /// with the reason rather than falling back to Christof, whose replay could not agree.
    /// </summary>
    public bool BodyRequired { get; init; }

    /// <summary>
    /// A development control; a person's decoded maps are about 300 MB: whether the people's images are
    /// decoded (<see cref="Assets.People.PeopleLibrary.DecodeImages"/>). Only play in a window does (<c>Program.Play</c>); headless runs
    /// (the checks, the arena, scenarios, <c>--headless</c> stills, the tests) build people without their maps in under a second.
    /// Presentation only: not in a recording.
    /// </summary>
    public bool DecodePeopleImages { get; init; }

    /// <summary>
    /// The largest side the people's decoded maps are kept at (<see cref="Assets.People.PeopleLibrary.MaxTextureSide"/>: the
    /// renderer's <c>characterTextureLimit</c>, so the 4096 maps are held at 2048 by default, the GPU's pixels the same); 0 keeps them as
    /// the files have them. Presentation only: not in a recording.
    /// </summary>
    public int PeopleTextureLimit { get; init; }

    /// <summary>
    /// A development control, to judge the feel in play: <c>--start-stop-half-life &lt;s&gt;</c>, the player's half-life for the
    /// run in place of its profile's (0 to 1 s; 0 puts the player on the ramp, as <c>accel-ramp</c> does), or null for the profile's.
    /// Carried in the tape's note <c>accel</c> (<c>spring 0.12</c>); a tape's note wins on replay.
    /// </summary>
    public float? StartStopHalfLife { get; init; }

    // ---- the people's turn as a chain and the crouch without the kneel ----
    /// <summary>
    /// How a person's body turns after the view (<see cref="Animation.People.PeopleTurn"/>): the chain's numbers (the settings
    /// <c>turnHeadRate</c> …, the <c>--turn-…</c> flags), or the earlier build's turn (<c>--turn-chain-off</c>, the setting <c>turnChainOff</c>:
    /// <see cref="Animation.LegTurnSettings.NoChain"/>). Only the models repository's bodies turn so. Carried in a recording's note <c>turn</c>
    /// (a tape without it was made on the earlier build's turn). Its <c>On</c> is <see cref="LegTurn"/>'s.
    /// </summary>
    public Animation.LegTurnSettings PersonTurn { get; init; } = new();

    /// <summary>
    /// Whether a person crouched and still settles into the kneel, as before (<c>--crouch-kneel</c>, the setting
    /// <c>crouchKneel</c>; off by default: the crouch held). Carried in a recording's note <c>crouchKneel</c> (a tape without it: the earlier build's, on).
    /// </summary>
    public bool CrouchKneel { get; init; }

    /// <summary>The tape head's notes for <see cref="PersonTurn"/> and <see cref="CrouchKneel"/>, written when the player's body is a person.</summary>
    public const string TurnNote = "turn", CrouchKneelNote = "crouchKneel";

    /// <summary>
    /// A person's planted feet, heel lift and bend rule (<see cref="Animation.People.PersonFeetSettings"/>; the settings
    /// <c>feetOff</c>, <c>footLockSeconds</c>, <c>footReplant</c>, <c>heelLift</c>, <c>bendRuleOff</c> and their flags). Carried in a
    /// recording's note <c>feet</c> (a tape without it was made on the earlier build's feet).
    /// </summary>
    public Animation.People.PersonFeetSettings PersonFeet { get; init; } = new();

    /// <summary>The tape head's note for <see cref="PersonFeet"/>, written when the player's body is a person.</summary>
    public const string FeetNote = "feet";

    /// <summary>
    /// The people on stairs and slopes (<see cref="Animation.People.PersonGroundSettings"/>; the settings <c>stairLineOff</c>,
    /// <c>slopeClipsOff</c>, <c>stairsHalfLife</c>, <c>stairsWindow</c> and their flags). Carried in a recording's note <c>ground</c> (a tape
    /// without it was made on the earlier build's: the flat gait, the body on the capsule's steps).
    /// </summary>
    public Animation.People.PersonGroundSettings PersonGround { get; init; } = new();

    /// <summary>The tape head's note for <see cref="PersonGround"/>, written when anyone is on a body of the models repository's.</summary>
    public const string GroundNote = "ground";

    /// <summary>
    /// Draw a person between ticks as before (the capsule's feet interpolated, the stair line's offset the last tick's: the body
    /// drops by up to a riser for a frame at each step). Presentation only, not on a tape. The setting <c>stairsDrawLastTick</c>, <c>--stairs-draw-last-tick</c>.
    /// </summary>
    public bool StairsDrawLastTick { get; init; }

    /// <summary>
    /// A skirt hung from the thighs as before (each half with its own thigh; <see cref="Animation.CharacterAssets.PersonSkirtSplit"/>).
    /// Presentation only (only swing bones move), not on a tape. The setting <c>skirtSplit</c>, <c>--skirt-split</c>.
    /// </summary>
    public bool SkirtSplit { get; init; }

    /// <summary>
    /// The people's swing bones simulated (<see cref="Animation.People.PersonSwingSettings"/>; <see cref="Animation.CharacterAssets.PersonSwing"/>),
    /// or drawn as before (<see cref="Animation.People.PersonSwingSettings.Off"/>). Presentation only (nothing digested), not on a tape.
    /// The settings <c>swingOff</c>, <c>swingNear</c>, <c>swingFar</c> and their flags.
    /// </summary>
    public Animation.People.PersonSwingSettings PersonSwing { get; init; } = Animation.People.PersonSwingSettings.Default;

    /// <summary>
    /// The third-person camera at walls as before (<see cref="CameraSettings.HardWalls"/>; the setting <c>cameraHardWalls</c>,
    /// <c>--camera-hard-walls</c>). The camera is the reticle's and the aim's, so a recording carries it in the note <see cref="CameraWallsNote"/>
    /// (a tape without it had the earlier build's).
    /// </summary>
    public bool CameraHardWalls { get; init; }

    /// <summary>The tape head's note for <see cref="CameraHardWalls"/>: <c>off</c> (the calm walls) or <c>off</c>.</summary>
    public const string CameraWallsNote = "cameraWalls";

    /// <summary>
    /// From what ground speed a person running ahead plays its sprint
    /// (<see cref="Animation.AnimationSettings.PersonSprintFrom"/>; the setting <c>sprintFrom</c>, <c>--sprint-from</c>; 0, <c>--sprint-clip-off</c>:
    /// the run at every speed). Carried in a recording's note <c>sprint</c> (a tape without it: 0, as before ran).
    /// </summary>
    public float PersonSprintFrom { get; init; } = new Animation.AnimationSettings().PersonSprintFrom;

    /// <summary>The tape head's note for <see cref="PersonSprintFrom"/>, written when anyone is on a body of the models repository's.</summary>
    public const string SprintNote = "sprint";

    /// <summary>
    /// An earlier build: the crouched capsule of the player's body of the models repository's (m; <see cref="PeopleBodies.CrouchHeight"/>,
    /// the setting <c>personCrouchHeight</c>, <c>--person-crouch-height</c>). Carried in a recording's note <see cref="PeopleBodies.CrouchNote"/>
    /// (a tape without it: <see cref="PeopleBodies.CrouchHeightTall"/>, the earlier builds' 1.25 m).
    /// </summary>
    public float PersonCrouchHeight { get; init; } = PeopleBodies.CrouchHeight;

    /// <summary>
    /// The models repository's starts and stops (<see cref="Animation.People.PersonStartSettings"/>; the settings <c>startClipsOff</c>,
    /// <c>crowdStartClipsOff</c>, <c>stopNearest</c> and their flags). Carried in a recording's note <c>starts</c> (a tape without it was made on the earlier build's).
    /// </summary>
    public Animation.People.PersonStartSettings PersonStarts { get; init; } = new();

    /// <summary>
    /// The models repository's jumps, fall and landings (<see cref="Animation.People.PersonAirSettings"/>; the settings <c>airClipsOff</c>,
    /// <c>jumpAtOnce</c> and their flags). Carried in a recording's note <c>air</c> (a tape without it was made on the earlier build's).
    /// </summary>
    public Animation.People.PersonAirSettings PersonAir { get; init; } = new();

    /// <summary>
    /// The models repository's clips — getting over and onto things, off edges, the air fitted to the fall, landings moving on
    /// (<see cref="Animation.People.PersonOverSettings"/>; the setting <c>overClipsOff</c> and its flag). Carried in a recording's note <c>over</c> (a tape
    /// without it was made on the earlier build's). <b>It changes the simulation</b>: the capsule follows a move's clip's path.
    /// </summary>
    public Animation.People.PersonOverSettings PersonOver { get; init; } = new();

    /// <summary>
    /// The models repository's clips — the ledge (hang, shimmy, braces, pull-up, drop, the grabs) on clips (<see cref="Animation.People.PersonLedgeSettings"/>;
    /// the setting <c>ledgeClipsOff</c> and its flag). Carried in a recording's note <c>ledge</c> (a tape without it was made on the earlier build's). <b>It changes the
    /// simulation</b>: the capsule hangs at the clips' place and follows their paths.
    /// </summary>
    public Animation.People.PersonLedgeSettings PersonLedge { get; init; } = new();

    /// <summary>
    /// The models repository — the prelanding before a standing landing (<see cref="Animation.People.PersonPrelandingSettings"/>; the setting
    /// <c>prelandingOff</c> and its flag). Carried in a recording's note <c>prelanding</c> (a tape without it was made on the earlier build's). The capsule's flight is
    /// untouched; the drawn body (the animation's digested state) is not.
    /// </summary>
    public Animation.People.PersonPrelandingSettings PersonPrelanding { get; init; } = new();

    /// <summary>
    /// The hands of the moves over and onto things put on the real top by the arms' IK (<see cref="Animation.People.PersonHandsSettings"/>; the
    /// setting <c>handsOnTopsOff</c> and its flag). Carried in a recording's note <c>hands</c> (a tape without it was made on the earlier build's). Nothing simulated moves;
    /// the drawn pose (on which the hit volumes are posed) does. The run's own vault (<c>autoVaultOff</c>) rides in the note <c>over</c>.
    /// </summary>
    public Animation.People.PersonHandsSettings PersonHands { get; init; } = new();

    /// <summary>
    /// The commit point in the air (<see cref="Animation.People.PersonCommitSettings"/>; the setting <c>commitOff</c> and its flag,
    /// <c>commitLead</c>, <c>autoCatch</c>, <c>autoCatchCone</c>, <c>catchArmReach</c>, <c>braceOverlap</c>). Carried in a recording's note <c>commit</c> (a tape
    /// without it was made on the earlier build's). <b>It changes the simulation</b>: a catch's clip carries the capsule from the commit; a fall catches only with use held.
    /// </summary>
    public Animation.People.PersonCommitSettings PersonCommit { get; init; } = new();

    /// <summary>
    /// Tier 1's moves, part 1 (<see cref="Animation.People.PersonMovesSettings"/>; the setting <c>movesOff</c> and its flag, <c>ladderSprintRate</c>,
    /// <c>toeBendLimitDeg</c>): the grabs that end braced, the low vaults at a run, the ladders on clips. Carried in a recording's note <c>moves</c> (a tape without
    /// it was made on the earlier build's). <b>It changes the simulation</b>: the braced grabs', the low vaults' and the ladders' capsule paths.
    /// </summary>
    public Animation.People.PersonMovesSettings PersonMoves { get; init; } = new();

    /// <summary>
    /// Tier 1's moves, part 2 (<see cref="Animation.People.PersonHangSettings"/>; the setting <c>hangMovesOff</c> and its flag, <c>leapTolerance</c>,
    /// <c>cornerSlack</c>, <c>hangAngleTolerance</c>): the hang's corners and leaps. Carried in a recording's note <c>hang</c> (a tape without it was made on
    /// The earlier build's). <b>It changes the simulation</b>: the corners' and the leaps' capsule paths, the facing carrying their turn.
    /// </summary>
    public Animation.People.PersonHangSettings PersonHang { get; init; } = new();

    /// <summary>
    /// Tier 1's moves, part 3 (<see cref="Animation.People.PersonWallSettings"/>; the setting <c>wallsOff</c> and its flag, <c>wallHoldReach</c>,
    /// <c>wallClimbRate</c>, <c>splayWristLimit</c>): climbing walls. Carried in a recording's note <c>walls</c> (a tape without it was made on the earlier build's).
    /// <b>It changes the simulation</b>: the walls' capsule paths.
    /// </summary>
    public Animation.People.PersonWallSettings PersonWall { get; init; } = new();

    /// <summary>
    /// Tier 1's moves, part 4 (<see cref="Animation.People.PersonStealthSettings"/>; the setting <c>stealthOff</c> and its flag, <c>stealthSpeed</c>,
    /// <c>stealthLower</c>, <c>coverReach</c>, <c>duckIn</c>, <c>duckOut</c>): squeezing through gaps, stealth and cover, the duck's blends. Carried in a recording's
    /// note <c>stealth</c> (a tape without it was made on the earlier build's or before). <b>It changes the simulation</b>: the squeeze's and the cover's capsule paths, stealth's pace.
    /// </summary>
    public Animation.People.PersonStealthSettings PersonStealth { get; init; } = new();

    /// <summary>
    /// The player's own walk, run and sprint and their starts and stops (<see cref="Animation.People.PersonGaitSettings"/>; the setting <c>playerGaitsOff</c> and
    /// its flag, <c>playerRunFrom</c>, <c>playerSprintFrom</c>, <c>stopBlend</c>). Carried in a recording's note <c>gaits</c> (a tape without it was made on the earlier build's or
    /// before). <b>It changes the simulation</b>: the player's paces, its stops' capsule and timing.
    /// </summary>
    public Animation.People.PersonGaitSettings PersonGaits { get; init; } = new();

    /// <summary>
    /// The player's lean into turns, the head looking ahead and the chain led by the way it goes (<see cref="Animation.People.PersonPresentSettings"/>):
    /// presentation (the drawn pose only, kept in no digested state), so no tape note; a replay draws by the settings of the run that replays it.
    /// </summary>
    public Animation.People.PersonPresentSettings PersonPresent { get; init; } = new();

    /// <summary>A number in a tape's note (invariant), or <paramref name="none"/> without the note or one that does not read.</summary>
    static float NoteNumber(TapeHeader h, string note, float none) =>
        h.Notes.TryGetValue(note, out var text) && float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)
        && float.IsFinite(v) ? v : none;

    /// <summary>A number as a tape's note writes it (invariant, round-tripping).</summary>
    public static string NoteOf(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>What a recording's notes say of the people's turn and crouch: none (an older tape) is the earlier build's turn and kneel.</summary>
    static SessionOptions FromPeopleNotes(TapeHeader h, SessionOptions o) => o with
    {
        PersonTurn = o.PersonTurn.WithNote(h.Notes.TryGetValue(TurnNote, out var tn) ? tn : null),
        CrouchKneel = !h.Notes.TryGetValue(CrouchKneelNote, out var ck) || ck == "true",
        PersonFeet = o.PersonFeet.WithNote(h.Notes.TryGetValue(FeetNote, out var fn) ? fn : null),
        PersonGround = o.PersonGround.WithNote(h.Notes.TryGetValue(GroundNote, out var gn) ? gn : null),   // none, the earlier build's
        CameraHardWalls = !h.Notes.TryGetValue(CameraWallsNote, out var cw) || cw == "hard",          // none, the earlier build's camera at walls
        PersonSprintFrom = NoteNumber(h, SprintNote, 0f),                                              // none, the run
        PersonCrouchHeight = NoteNumber(h, PeopleBodies.CrouchNote, PeopleBodies.CrouchHeightTall),   // none, 1.25 m
        PersonStarts = o.PersonStarts.WithNote(h.Notes.TryGetValue(Animation.People.PersonStartSettings.TapeNote, out var sn) ? sn : null),   // none, the earlier build's
        PersonAir = o.PersonAir.WithNote(h.Notes.TryGetValue(Animation.People.PersonAirSettings.TapeNote, out var an) ? an : null),         // none, the earlier build's
        PersonOver = o.PersonOver.WithNote(h.Notes.TryGetValue(Animation.People.PersonOverSettings.TapeNote, out var on) ? on : null),     // none, the earlier build's
        PersonLedge = o.PersonLedge.WithNote(h.Notes.TryGetValue(Animation.People.PersonLedgeSettings.TapeNote, out var ln) ? ln : null), // none, the earlier build's
        PersonPrelanding = o.PersonPrelanding.WithNote(h.Notes.TryGetValue(Animation.People.PersonPrelandingSettings.TapeNote, out var pn) ? pn : null), // none, the earlier build's
        PersonHands = o.PersonHands.WithNote(h.Notes.TryGetValue(Animation.People.PersonHandsSettings.TapeNote, out var hn) ? hn : null),   // none, the earlier build's
        PersonCommit = o.PersonCommit.WithNote(h.Notes.TryGetValue(Animation.People.PersonCommitSettings.TapeNote, out var cmn) ? cmn : null),   // none, the earlier build's
        PersonMoves = o.PersonMoves.WithNote(h.Notes.TryGetValue(Animation.People.PersonMovesSettings.TapeNote, out var mvn) ? mvn : null),     // none, the earlier build's
        PersonHang = o.PersonHang.WithNote(h.Notes.TryGetValue(Animation.People.PersonHangSettings.TapeNote, out var hgn) ? hgn : null),         // none, the earlier build's
        PersonWall = o.PersonWall.WithNote(h.Notes.TryGetValue(Animation.People.PersonWallSettings.TapeNote, out var wln) ? wln : null),         // none, the earlier build's
        PersonStealth = o.PersonStealth.WithNote(h.Notes.TryGetValue(Animation.People.PersonStealthSettings.TapeNote, out var sln) ? sln : null),   // none, the earlier build's
        PersonGaits = o.PersonGaits.WithNote(h.Notes.TryGetValue(Animation.People.PersonGaitSettings.TapeNote, out var gtn) ? gtn : null),           // none, the earlier build's
    };
    // ---- end

    /// <summary>The tape head's note for <see cref="Body"/> (<c>models</c> or <c>woman</c>), and the models' commit beside it (said on replay when it differs).</summary>
    public const string BodyNote = "body", BodyCommitNote = "bodyCommit";

    /// <summary>
    /// The new models' movement across the board: the townsfolk who do not fight — <c>models</c>, drawn as
    /// The models repository's crowd (<see cref="TownsfolkBodies"/>), or null (and <c>oneone</c>), 1.1's stand-ins as before. Play takes it from
    /// the setting <c>townsfolkBodies</c> (models by default) unless <c>--townsfolk-oneone</c>; the guards, the watch, the foes and the
    /// arena keep 1.1's. Carried in a recording's head (<see cref="TownsfolkBodies.TapeNote"/>, written only when they were drawn so: a tape
    /// without it had 1.1's).
    /// </summary>
    public string? TownsfolkBody { get; init; }

    /// <summary>The townsfolk must be the crowd as asked (a replay of a tape whose note says so): a session that cannot build them stops.</summary>
    public bool TownsfolkRequired { get; init; }

    /// <summary>
    /// The crowd built at run time on the bases as before (<c>--crowd-built</c>, the setting <c>townsfolkBodies</c>
    /// <c>models-built</c>; a tape whose note <c>townsfolk</c> says <c>models</c>), rather than as the models repository baked it (
    /// <see cref="TownsfolkBodies.Baked"/>). Of use only with <see cref="TownsfolkBody"/> <c>models</c>.
    /// </summary>
    public bool CrowdBuilt { get; init; }

    /// <summary>
    /// What a recording's notes say beyond the switches: a tape without the note <c>accel</c> was made on the earlier build's
    /// ramp (<c>accel-ramp</c> given), one without <c>arrows</c> with the earlier build's arrows (<c>arrows-heavy</c> given; harmless in a world
    /// without a bow), and the player's body from <see cref="BodyNote"/> (none: Christof). a tape with <c>accel</c> and without
    /// <c>landing</c> was made with the earlier build's landings on the spring (<c>landing-slide</c> given).
    /// </summary>
    static SessionOptions FromBodyNotes(TapeHeader h, SessionOptions o)
    {
        var switches = new HashSet<string>(o.Switches, StringComparer.Ordinal);
        if (!h.Notes.ContainsKey(StartStopSprings.TapeNote)) switches.Add(PeopleBodies.AccelRampSwitch);
        else if (!h.Notes.ContainsKey(StartStopSprings.LandingNote)) switches.Add(PeopleBodies.LandingSlideSwitch);   // a tape on the spring with the earlier build's landings
        if (!h.Notes.ContainsKey(PeopleBodies.ArrowsNote)) switches.Add(PeopleBodies.ArrowsHeavySwitch);
        string? body = h.Notes.TryGetValue(BodyNote, out var bn) && bn.Length > 0 ? bn : null;
        float? given = StartStopSprings.GivenIn(h.Notes.TryGetValue(StartStopSprings.TapeNote, out var an) ? an : null);   // the run's half-life, when given
        // The townsfolk on the crowd; baked (the note baked), or built at run time as before (the note models).
        string? townsfolk = h.Notes.TryGetValue(TownsfolkBodies.TapeNote, out var tn) ? tn : null;
        bool crowd = townsfolk is TownsfolkBodies.Models or TownsfolkBodies.Baked;
        return o with { Switches = switches, Body = body, BodyRequired = body is not null, StartStopHalfLife = given,
                        TownsfolkBody = crowd ? TownsfolkBodies.Models : null, TownsfolkRequired = crowd,
                        CrowdBuilt = townsfolk == TownsfolkBodies.Models };
    }
    // ---- end

    /// <summary>These options as a recording made them (its level, seed, start, townsfolk, game and switches).</summary>
    public SessionOptions From(TapeHeader h) => FromPeopleNotes(h, FromBodyNotes(h, this with   // the accel, arrows and body notes; the turn and the kneel
    {
        // A older tape names its rules module where the game now goes; it was the enhanced game.
        GameList = GameList ?? (h.Modules.Count > 0 && !string.IsNullOrEmpty(h.Game) ? h.Game : null),
        Switches = h.Notes.TryGetValue("switches", out var sw)
            ? new HashSet<string>(sw.Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal)
            : h.Notes.TryGetValue("sandbox", out var sb) && sb == "true" ? new HashSet<string>(StringComparer.Ordinal) { "sandbox" }
            : new HashSet<string>(StringComparer.Ordinal),
        Level = !string.IsNullOrEmpty(h.Level) && (Directory.Exists(h.Level) || BuiltInGrounds.Is(h.Level)) ? h.Level : Level,   // a ground built in code
        Seed = h.Seed,
        // A number earlier; a name or a number since.
        Start = string.IsNullOrWhiteSpace(h.Start) ? "0" : h.Start,
        People = h.Notes.TryGetValue("people", out var p) && int.TryParse(p, out int n) ? n : People,
        // A tape's "pointerKey" note is not read: the freed pointer's latch is the simulation's now, and a
        // older tape does not replay anyway (the camera work changed the eye's ease and the view's cycle).
        // A tape from before the setting was made facing the view.
        Facing = h.Notes.TryGetValue("facing", out var f) && Enum.TryParse<ThirdPersonFacing>(f, out var facing) ? facing : ThirdPersonFacing.View,
        // Always run; a tape from before it ran so.
        AlwaysRun = TickInput.AlwaysRunFrom(h.Notes),
        // The legs' turn and double tap to dodge; a tape without them was made before them (neither).
        LegTurn = h.Notes.TryGetValue(LegTurnNote, out var lt) && lt == "true",
        DoubleTapDodge = TickInput.DoubleTapFrom(h.Notes),
        DirectionPace = h.Notes.TryGetValue(SessionOptions.DirectionPaceNote, out var dpn) && dpn == "true",
        // The global combat tempo given; a tape without the note played the module's.
        CombatTempo = h.Notes.TryGetValue(CombatTempoOverride.TapeNote, out var ctn) ? TempoOf(ctn) : null,
        // The third-person blow's pitch; a tape without the note was made from the reticle's point.
        ThirdPersonAim = Control.ThirdPersonAimSettings.FromNote(h.Notes.TryGetValue(Control.ThirdPersonAimSettings.TapeNote, out var tpa) ? tpa : null),
        AttackDirection = Control.AttackDirectionInput.FromNote(h.Notes.TryGetValue(Control.AttackDirectionInput.TapeNote, out var adn) ? adn : null),
        // The player's movement profile and the masquerade's switching; a tape from before them was made with neither.
        BodyProfile = h.Notes.TryGetValue("bodyProfile", out var bp) && bp.Length > 0 ? bp : null,
        AutoMasquerade = !h.Notes.TryGetValue("masqueradeAuto", out var ma) || ma != "false",
        // A tape without it was made before the recovery: none.
        RecoverSeconds = h.Notes.TryGetValue("recoverSeconds", out var rs) && float.TryParse(rs, NumberStyles.Float, CultureInfo.InvariantCulture, out var rsv) ? rsv : 0f,
        // The player's placement; a tape without it began at its start anchor.
        Place = h.Notes.TryGetValue(Placement.TapeNote, out var pl) && pl.Length > 0 ? pl : null,
        // The developer's target test; a tape without it asked for none.
        TargetTest = h.Notes.TryGetValue(TargetTestNote, out var tt) && tt.Length > 0 ? tt : null,
        // The witnesses and the time of day; a tape without them had none, at night.
        Witnesses = h.Notes.TryGetValue(WitnessRequest.TapeNote, out var wn) && wn.Length > 0 ? wn.Split(';', StringSplitOptions.RemoveEmptyEntries) : [],
        // The foes placed; a tape without them had none.
        Foes = h.Notes.TryGetValue(FoeRequest.TapeNote, out var fn) && fn.Length > 0 ? fn.Split(';', StringSplitOptions.RemoveEmptyEntries) : [],
        Night = !h.Notes.TryGetValue(TimeNote, out var tn) || tn != "day",
        // The player's weapon and the townsfolk's schedule; a tape without them had the default and none.
        Weapon = h.Notes.TryGetValue(WeaponNote, out var wp) && wp.Length > 0 ? wp : null,
        // The earlier build's camera and the body drawn (they move the simulation's view and pose); a tape without them had neither.
        Cameras = h.Notes.TryGetValue(CamerasNote, out var cn) ? CameraSettings.FromNote(cn) : null,
        PlayAs = h.Notes.TryGetValue("playAs", out var pa) && pa.Length > 0 ? pa : null,
        StitchedAsQuick = h.Notes.TryGetValue(StitchedAsQuickNote, out var sq) && sq.Length > 0 ? sq : null,
        NpcActions = h.Notes.TryGetValue(ActionScript.TapeNote, out var na) && na.Length > 0 ? na : null,
        // The sheets given, with their text; a tape without them gave none.
        Sheets = h.Notes.TryGetValue(SheetsNote, out var sn) && sn.Length > 0 ? SheetRequest.FromNote(sn) : [],
        // The powers learned; a tape without them learned none.
        Learn = h.Notes.TryGetValue(LearnNote, out var ln) && ln.Length > 0 ? LearnRequest.FromNote(ln) : [],
        // The armour worn; a tape without it dressed nobody by hand.
        Armor = h.Notes.TryGetValue(ArmorNote, out var an) && an.Length > 0 ? ArmorRequest.FromNote(an) : [],
        // The protected people; a tape without them marked none.
        Protected = h.Notes.TryGetValue(ProtectedPeople.TapeNote, out var pr) && pr.Length > 0 ? pr : null,
        // The arena's cast; a tape without it is no bout.
        Arena = h.Notes.TryGetValue(ArenaNote, out var ar) && ar.Length > 0 ? ar : null,
        // The scenario's program; a tape without it had none.
        Scenario = h.Notes.TryGetValue(ScenarioNote, out var sp) && sp.Length > 0 ? sp : null,
        // The data overrides and the light; a tape without them had none.
        DataOverrides = h.Notes.TryGetValue(DataOverridesNote, out var dov) && dov.Length > 0 ? dov : null,
        Light = h.Notes.TryGetValue(LightNote, out var lgt) && lgt.Length > 0 ? lgt : null,
        // The player's bow; a tape without the note carried none (its world had no bow).
        PlayerBow = h.Notes.TryGetValue(PlayerBowNote, out var pbn) && pbn.Split(':')[0] is { Length: > 0 } pbw ? pbw : null,
        PlayerArrows = h.Notes.TryGetValue(PlayerBowNote, out var pba) && pba.Split(':') is { Length: 2 } pbs && int.TryParse(pbs[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pbc) ? pbc : DefaultPlayerArrows,
        // The player's bow's rules; a tape without the note was the earlier build's.
        // The bow's rules; a tape with a bow in its world and no note was made earlier: the earlier build's.
        PlayerBowAim = Control.PlayerBowSettings.FromNote(h.Notes.TryGetValue(Control.PlayerBowSettings.TapeNote, out var bam) ? bam : null)
                       ?? ((h.Notes.TryGetValue(PlayerBowNote, out var pbo) && pbo.Length > 0) || Vitomir.Platform.Host.Arena.ArenaCast.BowsIn(h.Notes.TryGetValue(ArenaNote, out var arb) ? arb : null)
                           ? new Control.PlayerBowSettings { NoAim = true } : null),
        // The view the run started in; a tape without the note started over the right shoulder.
        StartView = h.Notes.TryGetValue(StartViewNote, out var svn) && svn.Length > 0 ? svn : null,
    }));

    /// <summary>The tape head's note for <see cref="TargetTest"/>.</summary>
    public const string TargetTestNote = "targetTest";

    internal static SessionOptions ForTape(TapeHeader h, Cli cli, PlatformSettings settings, HostLog log)
    {
        var level = !string.IsNullOrEmpty(h.Level) && (Directory.Exists(h.Level) || BuiltInGrounds.Is(h.Level)) ? h.Level : Program.LevelFolder(cli, settings);
        var baseline = new SessionOptions { Level = level, Game = Program.GameFolder(cli, settings), GameList = ModulesCommand.GameChoice(cli), ModulesFolder = cli.Arg("--modules"), Say = log.Say,
                                            ModelsFolder = PeopleBodies.FindFolder(cli.Arg("--models"), settings.ModelsFolder) };
        return baseline.From(h);
    }
}

/// <summary>
/// One game on the platform: the level's space, the game's modules loaded from their
/// folders and configured (fail closed; the program knows them only through the platform's loader and
/// <see cref="IModule"/>), 1.1's characters, the world built from them, the player's body at a start and townsfolk
/// walking routes round it. The program steps it a tick at a time and asks it for the picture between the
/// last two ticks; a recording and a replay check drive it the same way without a window.
/// </summary>
public sealed partial class Session : IDisposable
{
    SessionOptions options;
    readonly World world;
    readonly LoadedGame modules;
    readonly GameInstall? install;
    readonly LevelSpace level;
    readonly Dictionary<string, TextureImage> textures = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Each character's place after the tick before the last (the picture is drawn between it and the last); and its stair line (<see cref="LineOf"/>).</summary>
    readonly Dictionary<ulong, (Vector3 Feet, float Yaw, float Line)> before = [];
    readonly Dictionary<ulong, DrawnCharacter> drawn = [];
    /// <summary>The held models drawn, by holder and index, reused picture to picture.</summary>
    readonly Dictionary<(ulong, int), DrawnCharacter> drawnHeld = [];
    readonly List<EntityId> characters = [];
    /// <summary>Each character's name as the cursor and the reticle show it: 1.1's character it is drawn as.</summary>
    readonly Dictionary<ulong, string> names = [];
    /// <summary>1.1's cursors' art from the install, or none.</summary>
    OneOneCursors cursors = OneOneCursors.From(null);
    ViewPoint viewBefore, viewNow;
    /// <summary>The head's eyes less the steady eye, the tick before the last and the last: the bob the picture is given a share of.</summary>
    Vector3 bobBefore, bobNow;
    bool haveViewBefore;
    EntityId player = EntityId.None;
    int startIndex;
    string notice = "";
    long noticeUntil;

    /// <summary>How long a notice (an exit reached, a body put back) stays on the status line: four seconds.</summary>
    const int NoticeTicks = 4 * World.TicksPerSecond;

    /// <summary>
    /// The reach highlight's marks are made again at most ten times a second of the simulation, and then only
    /// when the player has moved 0.2 m or the view turned 5° since, or a second has passed (its numbers may have
    /// changed with the masquerade): a body standing still costs nothing.
    /// </summary>
    const int ReachEveryTicks = World.TicksPerSecond / 10, ReachAtLeastTicks = World.TicksPerSecond;
    long reachTick = -1;
    Vector3 reachFeet;
    float reachYaw;

    Session(SessionOptions options, World world, LoadedGame modules, GameInstall? install, LevelSpace level)
    {
        // Every line the session says is offered to the combat log's window as it is said (the very string, kept
        // when it is about the fight); presentation only — the lines and what they say are the (Tell).
        var say = options.Say;
        this.options = options with { Say = line => { CombatLog.Offer(line, world.Seconds); say(line); } };
        this.world = world;
        this.modules = modules;
        this.install = install;
        this.level = level;
    }

    public long Tick => world.Tick;
    public World World => world;
    /// <summary>The player's body.</summary>
    public EntityId Player => player;

    /// <summary>
    /// Whether <see cref="Fill"/> gives the view the reach highlight's marks round the player (the setting
    /// <c>showReach</c>, <c>--show-reach</c>, the action <c>reach</c>). Presentation only: the marks are read from the
    /// level and the player's numbers and change nothing in the world, its digests or a recording.
    /// </summary>
    public bool ShowReach { get; set; }

    /// <summary>
    /// Whether <see cref="Fill"/> gives the view the awareness overlay's marks (<c>--show-awareness</c>, the action
    /// <c>awareness</c>, no key by default; <see cref="AwarenessView"/>): a mark over each observer's head and its cone.
    /// Presentation only.
    /// </summary>
    public bool ShowAwareness { get; set; }

    /// <summary>
    /// Whether <see cref="Fill"/> gives the view the hit volumes' overlay (<c>--show-hit-volumes</c>, the action
    /// <c>hitVolumes</c>, no key by default; <see cref="HitVolumeView"/>): each character's capsules as wire outlines over
    /// its drawn body, coloured by region. Presentation only.
    /// </summary>
    public bool ShowHitVolumes { get; set; }

    /// <summary>
    /// Whether the flurry's cue is drawn (a flash per extra Celerity blow). On in play; a
    /// <c>--headless</c> or arena picture draws it only with <c>--show-cues</c> or <c>--show-hud</c>.
    /// </summary>
    public bool ShowCues { get; set; } = true;

    /// <summary>
    /// Whether <see cref="Fill"/> gives the view the swing's trail (<c>--show-swing</c>, the action
    /// <c>swingTrail</c>, no key by default; <see cref="SwingTrails"/>): each recent swing's tip path and blade as contact swept
    /// them, the reticle's point and the tip's nearest pass to the reticle's ray. Presentation only.
    /// </summary>
    public bool ShowSwingTrail { get; set; }

    /// <summary>A body drawn as first person draws the player's (its head and neck hidden), for an arena's film from its eyes. Presentation only.</summary>
    public EntityId FilmFirstPerson { get; set; }

    /// <summary>A film's swing arc drawn for this body in place of the player (an arena's first fighter); none, the player's.</summary>
    public EntityId ArcOf { get; set; }

    /// <summary>
    /// Whether <see cref="Fill"/> gives the view the swing's arc (the action <c>swingArc</c>,
    /// no key by default, and the setting <c>showSwingArc</c>, on): where the player's next blow's blade will go, predicted from
    /// the body as it stands each tick (<see cref="SwingArcs"/>). Drawn at <see cref="SwingArcOpacity"/>. Presentation only.
    /// </summary>
    public bool ShowSwingArc { get; set; } = true;

    /// <summary>The swing's arc's opacity (the setting <c>swingArcOpacity</c>, 0 to 1; 25 % by default; 0 hides it). Presentation only.</summary>
    public float SwingArcOpacity { get; set; } = 0.25f;

    /// <summary>The swing's arc's prediction: presentation only, the session's own — not a world service, never digested.</summary>
    public SwingArcs SwingArcs { get; } = new();

    /// <summary>
    /// The reticle's opacity while a melee weapon or the fists are out and it has nothing else to say
    /// (the setting <c>meleeReticleOpacity</c>, 0 to 1; 20 % in play; 1 in a <c>--headless</c> picture unless
    /// <c>--melee-reticle</c> says, so the pictures that check levels do not change). Presentation only.
    /// </summary>
    public float MeleeReticleOpacity { get; set; } = 1f;

    /// <summary>
    /// Whether <see cref="Fill"/> gives the view the combat log's window (the setting <c>showCombatLog</c>, on; the
    /// action <c>combatLog</c>, no key by default; <c>--show-combat-log</c> for a headless picture). Presentation only.
    /// </summary>
    public bool ShowCombatLog { get; set; }

    /// <summary>The combat log — the last lines this session said about the fight (<see cref="Presentation.CombatLog"/>); kept whether shown or not.</summary>
    public CombatLog CombatLog { get; } = new();

    long swingArcTick = -1;
    bool swingArcFrozen;

    /// <summary>
    /// <c>--arc-freeze</c>, for pictures: the arc predicted now and kept as it is, stood where it was predicted, so a
    /// picture taken after the blow shows the arc drawn before it beside the swing's trail.
    /// </summary>
    public void FreezeSwingArc()
    {
        if (world.Alive(player)) SwingArcs.Predict(world, player);
        swingArcFrozen = true;
        int shown = 0;
        foreach (var p in SwingArcs.Places) if (p.Front) shown++;
        options.Say(string.Create(CultureInfo.InvariantCulture,
            $"{world.Seconds:0.00} s: swing arc: kept as predicted now: {SwingArcs.Blow.ToString().ToLowerInvariant()}, {SwingArcs.Places.Length} places ({shown} shown), {SwingArcs.Lead} ticks ahead{(SwingArcs.Meets.IsNone ? "" : $", meets {NameOf(SwingArcs.Meets.Packed)} ({(SwingArcs.MeetsShield ? "shield" : BodyRegions.Name(SwingArcs.MeetsRegion))})")}"));
    }

    /// <summary>
    /// How much of the head's motion the first-person picture has (the setting <c>headMotion</c>, 0 to 1): 0 the
    /// steady eye, 1 the head's eyes (<see cref="FirstPersonView.Eye"/>). Presentation only, as <see cref="ShowReach"/>.
    /// </summary>
    public float HeadMotion { get; set; } = FirstPersonView.DefaultHeadMotion;

    /// <summary>
    /// The first-person picture's field of view (the setting <c>fieldOfView</c>, vertical degrees, or
    /// <c>fieldOfViewHorizontal</c>, degrees across, by <see cref="FieldOfViewAxis"/>). Presentation only.
    /// </summary>
    public float FieldOfView { get; set; } = FirstPersonView.DefaultFieldOfView;

    /// <summary>Which way <see cref="FieldOfView"/> is measured (the setting <c>fieldOfViewAxis</c>). Presentation only.</summary>
    public FieldOfViewAxis FieldOfViewAxis { get; set; } = FieldOfViewAxis.Vertical;

    /// <summary>The shape of the picture drawn (its width over its height; the program sets it every frame from the picture's size).</summary>
    public float PictureAspect { get; set; } = 16f / 9f;

    /// <summary>The vertical field the first-person picture is drawn with (degrees): <see cref="FirstPersonView.Drawn"/> of the choice and the picture's shape.</summary>
    public float DrawnFieldOfView => FirstPersonView.Drawn(FieldOfViewAxis, FieldOfView, PictureAspect);

    /// <summary>The player's view now (<see cref="CameraRig.ViewName"/>: first, right, left), null without a player.</summary>
    public string? PlayerView =>
        world.Alive(player) && world.Has<CameraRig>(player) ? CameraRig.ViewName(world.Get<CameraRig>(player)) : null;

    /// <summary>
    /// How far a place on the picture is put from its centre for the simulation's pick
    /// (<see cref="FirstPersonView.PickScale"/>): in first person the drawn field against the simulation's pick field, else 1.
    /// The program puts the pointer's place through it (<see cref="FirstPersonView.ToPick"/>) before it goes into a tick.
    /// </summary>
    public float PickScale =>
        world.Alive(player) && world.Has<CameraRig>(player) && world.Get<CameraRig>(player).View == ViewKind.First
            ? FirstPersonView.PickScale(DrawnFieldOfView, world.Service<CameraSettings>().FirstPersonPickField) : 1f;

    /// <summary>The game's modules as loaded.</summary>
    public LoadedGame Modules => modules;

    public static Session Create(SessionOptions options)
    {
        var say = options.Say;

        // What is loaded before the world — the modules (a refusal stops everything before a window opens), the level,
        // 1.1's characters — is the arena's to keep across its bouts (SessionOptions.Content), else loaded here.
        var content = options.Content is { } kept && kept.LoadedFor == SessionContent.KeyOf(options) ? kept : LoadContent(options);
        bool owned = !ReferenceEquals(content, options.Content);
        var (install, modules, level, assets, kinds) = (owned ? content.Install : null, content.Modules, content.Level, content.Assets, content.Kinds);
        // The new body carries no bow yet (it has no combat: the player's bow is 1.1's Christof's).
        if (content.PlayerBody is not null && options.PlayAs is null && options.Arena is null && options.PlayerBow is not null)
        {
            options = options with { PlayerBow = null };
            say("people: the new body carries no bow yet: --body oneone for the bow");
        }
        // The start asked for, by number or by name: an unknown name stops the start with the level's names.
        if (!level.TryFindStart(options.Start, out int startIndex, out string startError)) { install?.Dispose(); throw new SessionException(startError); }

        var b = new WorldBuilder(options.Seed);
        MovementServices.Register(b, level, (options.Cameras ?? new CameraSettings()) with { HardWalls = options.CameraHardWalls });    // the camera survey's settings; null, the defaults; the walls' rule
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = options.AutoMasquerade });
        PointerServices.Register(b);    // what is under the freed pointer or the reticle, its verb, the click
        // Who sees — the observers, the acts in view, the areas' light, concealment and heat; the modules give the kinds.
        var areas = content.Areas;
        PerceptionServices.Register(b, areas);
        // The legs' turn; a person's turn as a chain (its numbers through the tape's note, so a replay's
        // are the recording's to the bit) and the crouch with or without the kneel.
        b.Animation(assets, new AnimationSettings { PersonKneel = options.CrouchKneel, PersonSprintFrom = options.PersonSprintFrom },   // the people's sprint
                    legs: options.PersonTurn.WithNote(options.PersonTurn.Note()) with { On = options.LegTurn },
                    feet: options.PersonFeet.WithNote(options.PersonFeet.Note()),    // the feet's numbers through the tape's note too
                    ground: options.PersonGround.WithNote(options.PersonGround.Note()),   // stairs and slopes, likewise
                    starts: options.PersonStarts.WithNote(options.PersonStarts.Note()),    // the starts and stops, likewise
                    air: options.PersonAir.WithNote(options.PersonAir.Note()),             // the jumps, the fall and the landings, likewise
                    over: options.PersonAir.Any ? options.PersonOver.WithNote(options.PersonOver.Note()) : Animation.People.PersonOverSettings.Off,   // over and onto things, off edges
                    ledge: options.PersonAir.Any && options.PersonOver.Any ? options.PersonLedge.WithNote(options.PersonLedge.Note()) : Animation.People.PersonLedgeSettings.Off,   // the ledge
                    prelanding: options.PersonAir.Any && options.PersonOver.Any ? options.PersonPrelanding.WithNote(options.PersonPrelanding.Note()) : Animation.People.PersonPrelandingSettings.Off,   // the prelanding
                    commit: options.PersonAir.Any && options.PersonOver.Any && options.PersonLedge.Any ? options.PersonCommit.WithNote(options.PersonCommit.Note()) : Animation.People.PersonCommitSettings.Off,   // the commit point
                    moves: options.PersonAir.Any && options.PersonOver.Any && options.PersonLedge.Any && options.PersonCommit.Any
                        ? options.PersonMoves.WithNote(options.PersonMoves.Note()) : Animation.People.PersonMovesSettings.Off,   // tier 1's moves, likewise
                    hang: options.PersonAir.Any && options.PersonOver.Any && options.PersonLedge.Any && options.PersonCommit.Any && options.PersonMoves.Any
                        ? options.PersonHang.WithNote(options.PersonHang.Note()) : Animation.People.PersonHangSettings.Off,   // the hang's corners and leaps, likewise
                    wall: options.PersonAir.Any && options.PersonOver.Any && options.PersonLedge.Any && options.PersonCommit.Any && options.PersonMoves.Any && options.PersonHang.Any
                        ? options.PersonWall.WithNote(options.PersonWall.Note()) : Animation.People.PersonWallSettings.Off,   // the walls, likewise
                    stealth: options.PersonAir.Any && options.PersonOver.Any && options.PersonLedge.Any && options.PersonCommit.Any && options.PersonMoves.Any && options.PersonHang.Any
                             && options.PersonWall.Any
                        ? options.PersonStealth.WithNote(options.PersonStealth.Note()) : Animation.People.PersonStealthSettings.Off,   // squeezing, stealth, cover, ducking
                    gaits: options.PersonStarts.Player ? options.PersonGaits.WithNote(options.PersonGaits.Note()) : Animation.People.PersonGaitSettings.Off,   // the player's own gaits
                    present: options.PersonPresent);   // the lean, the look, the chain led by the way (presentation)
        // The hands of the moves over and onto things on the real top (presentation: read when a pose is made).
        b.Service(options.PersonAir.Any && options.PersonOver.Any ? options.PersonHands.WithNote(options.PersonHands.Note()) : Animation.People.PersonHandsSettings.Off);
        b.Service(new DirectionPace { On = options.DirectionPace });                  // speed by direction
        b.Service(options.ThirdPersonAim);                                            // the third-person blow's pitch
        b.Service(new Control.AttackDirectionInput { Mode = options.AttackDirection.Mode });   // the player's way of choosing a blow's direction (a copy, so a change mid-run leaves the options' start mode for the tape's head)
        b.HitVolumes();                 // the hit volumes on the drawn pose (the reticle's part, the overlay; contact next)
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);     // attacks, the block, the strike's moment, the townsfolk's script
        DeathServices.Register(b);      // what a death does to a body; the harm and death events
        TextWriter? traceWriter = null;  // the rules' resolution traces (--combat-trace)
        if (options.CombatTrace is { } tracePath)
        {
            try { traceWriter = new StreamWriter(tracePath) { AutoFlush = false }; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { install?.Dispose(); throw new SessionException($"The combat trace {tracePath} could not be written: {e.Message}"); }
        }
        b.Service(new CombatTrace(traceWriter));
        ContactServices.Register(b);    // the blade swept against shields, bodies and the level; the evidence
        if (!options.Switches.Contains(OneOneStitched.SwingsOneOneSwitch)) b.Service(new SwingAim { Track = !options.Switches.Contains(SwingAim.AimAtPressSwitch) });   // the aim kept on the foe through the blow unless aim-at-press // an earlier build: the duelling brain aims at its foe's body
        BlowCueServices.Register(b);    // the flurry's cue (presentation only, not digested)
        FlurryBlowServices.Register(b); // a Celerity swing's extra blows checked at their moments
        if (options.Arena is not null) DuelBrains.Register(b);     // the arena's brains (its worlds have no fights of perception's)
        else
        {
            WorldFights.Register(b);        // people fight back — the duelling brain on whoever perception says fights
            RecoveryServices.Register(b);   // the player's sandbox recovery after a zero-Health stop (ENGINE_SANDBOX)
        }
        if (options.Scenario is { } scenarioProgram) RegisterScenario(b, scenarioProgram, install);   // the scenario director, only with a program
        if (Vitomir.Platform.Host.Arena.ArenaCast.SidesIn(options.Arena)) DuelSides.Register(b);   // one against several, the foe chosen by side
        bool foeArchers = FoeArchersIn(options, modules) || Vitomir.Platform.Host.Arena.ArenaCast.ArchersIn(options.Arena);   // an archer foe (--foe archer@…) or a scenario's archer
        if (Vitomir.Platform.Host.Arena.ArenaCast.BowsIn(options.Arena) || PlayerBowIn(options) || foeArchers)
        {
            BowServices.Register(b, null, BowExtrasFor(options, b));   // the bow and its arrows, only when a fighter carries one; or the sandbox's player
            b.Service(PlayerBowAimFor(options));                          // the player's bow's rules (not digested)
        }
        if (foeArchers) ArcherBrains.Register(b);                         // the archer brain, only with an archer
        if (options.Foes.Count > 0 && options.Arena is null) FoeWakes.Register(b);                  // --foe, waiting for the player to come near
        if (UsesOneOne(options, modules)) DuelOneOnes.Register(b);                                  // 1.1's approach, only when a brain uses it
        if (options.CombatTempo is { } tempo) b.Service(new CombatTempoOverride(tempo));   // --combat-tempo, read by the rules module
        RegisterOverrides(b, options, install);   // the light given, the fighters' own tempos (Session.Overrides.cs); nothing when none
        modules.Register(b);
        // The player starts and stops on the spring — play's player, or a scenario's
        // player-input actor; an arena's bout has no player, so its world is the earlier build's.
        // A development control (--start-stop-half-life): the run's half-life for the player; 0 keeps the player on the ramp.
        if (options.StartStopHalfLife is not 0f
            && (options.Arena is null || (b.Peek.TryService<ScenarioDirector>(out var director) && director.Program.Input?.Actor is not null))
            && StartStopSprings.RegisterPlayer(b) && options.StartStopHalfLife is { } given)
            b.Peek.Service<StartStopSprings>().Override = given;
        var world = b.Build();
        BowSwitches(world, options);                    // shield-at-bow-off
        world.Service<HitVolumes>().Prepare(assets);    // every model's volumes fitted now, not on a tick
        ContactServices.Prepare(world);                 // every attack clip's active window, every kind's matter, the level's surfaces
        say(ContactLine(world));
        world.Service<TickInput>().ThirdPersonFacing = options.Facing;
        world.Service<TickInput>().AlwaysRun = options.AlwaysRun;
        world.Service<TickInput>().DoubleTapDodge = options.DoubleTapDodge;
        var perception = world.Service<PerceptionConfig>();
        perception.Night = options.Night;
        if (world.TryService<LightSchedule>(out var lightGiven)) perception.LightLevel = lightGiven.LevelAt(0);
        say(perception.Kinds.Count == 0 ? "perception: no module gives kinds of observer; nobody sees anything"
            : string.Create(CultureInfo.InvariantCulture, $"perception: {(options.Night ? "night" : "day")}, light {perception.Light:0.##}; kinds {perception.Names}{(perception.Sandbox ? " (SANDBOX numbers)" : "")}; areas: {areas.Count - 1} for heat, {areas.Lights} with their own light, {areas.Conceals} concealing"));
        foreach (var problem in areas.Problems) say($"perception: {problem}");
        // The developer's target test, asked before the first tick (so a replay asks it too).
        if (options.TargetTest is { } test)
        {
            if (!TargetRequest.TryParse(test, out var request, out string testError)) { install?.Dispose(); throw new SessionException(testError); }
            world.Service<PointerState>().Ask(request);
            say($"pointer: target test asked — {(request.Kinds == TargetKinds.Any ? "a person or a place" : request.Kinds == TargetKinds.Person ? "a person" : "a place")} within {request.Range:0.##} m{(request.NeedsSight ? " in sight" : ", sight not needed")}; a click picks (or is refused), and it is asked again");
        }

        // The bodies' movement profiles, from the modules, and the player's own.
        var profiles = world.Service<MovementProfiles>();
        if (options.BodyProfile is { } asked)
        {
            if (profiles.IndexOf(asked) < 0)
            {
                install?.Dispose();
                throw new SessionException($"There is no movement profile '{asked}' in game {modules.Game.Id} (its profiles: {profiles.Names}).");
            }
            profiles.PlayerProfile = asked;
        }
        if (profiles.All.Count == 0) say("movement: no module gives movement profiles; the bodies keep the platform's sandbox numbers and the masquerade does nothing");
        else
        {
            var mine = options.BodyProfile is { } n ? profiles.Find(n)! : profiles.Human ?? profiles.All[0];
            say($"movement: profiles {profiles.Names}{(profiles.All.Any(p => p.Sandbox) ? " (SANDBOX numbers)" : "")}; the player's {mine.Id}; the masquerade holds bodies to {profiles.Human?.Id.ToString() ?? "nothing (no human profile)"}; it switches with the place {(options.AutoMasquerade ? "by itself" : "only by the key")}");
        }
        var places = level.Places;
        say(places.Places.Count == 0 && places.Whole is null ? "places: none marked; the whole level is public"
            : $"places: {(places.Whole is { } whole ? $"the level {LevelPlaces.Word(whole)}; " : "")}{places.Places.Count} marked area{(places.Places.Count == 1 ? "" : "s")}{(places.Places.Count == 0 ? "" : ": " + string.Join(", ", places.Places.Select(p => $"{p.Name} {LevelPlaces.Word(p.Kind)}")))}; elsewhere {LevelPlaces.Word(places.Whole ?? PlaceKind.Public)}");
        foreach (var problem in places.Problems) say($"places: {problem}");

        // A placement asked for is read before anything is made, so a malformed one stops the start.
        Placement? placement = null;
        if (options.Place is { } placeText)
        {
            if (!Placement.TryParse(placeText, out var parsed, out string placeError)) { install?.Dispose(); throw new SessionException(placeError); }
            placement = parsed;
        }

        var session = new Session(options, world, modules, install, level) { startIndex = startIndex, traceWriter = traceWriter, arrowModel = content.ArrowModel, bowArrowBones = content.BowArrowBones,   // the arrows' model; the bow model's arrow
            playerBody = options.PlayAs is null && options.Arena is null ? content.PlayerBody : null,   // the player's new body
            crowd = options.Arena is null ? content.Townsfolk : null };   // the townsfolk on the models repository's crowd
        foreach (var (name, image) in content.Textures) session.textures[name] = image;
        // 1.1's cursors, read from the install as the characters are; without it the picture draws a plain arrow.
        session.cursors = content.Cursors;
        session.creatures = content.Creatures;   // 1.1's creatures' speeds and AI files
        say(content.Install is null ? "cursors: no 1.1 install; the freed pointer is a plain arrow" : $"cursors: {session.cursors.Read} of 1.1's {Cursors.Count} read from {content.Install.Root}");
        session.Populate(kinds);
        session.BindScenario();         // the program's actors, once the cast is made
        session.GiveSheets();           // --sheet who=file
        session.GiveLearned();          // --learn power=rank
        session.GiveArmor();            // --armor who=piece,piece
        if (options.EventTrace is { } eventPath)  // the event trace, its head once everyone is made and given a sheet
        {
            try { session.eventTrace = new EventTrace(eventPath); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { session.Dispose(); throw new SessionException($"The event trace {eventPath} could not be written: {e.Message}"); }
            session.eventTrace.Head(world, session.TapeHeader(PlatformVersion.Text),
                session.characters.Select(c => (c, session.NameOf(c.Packed) ?? "")).ToList(),
                "NATIVE: the platform's own simulation of this run; numbers marked SANDBOX or ENGINE_SANDBOX are not rules");
        }
        if (placement is { } place)
        {
            session.PlacedAside = place.Apply(world, session.player);
            say($"session: the player placed at {place} before the first tick");
        }
        return session;
    }

    /// <summary>How far the view was turned from the facing by <see cref="SessionOptions.Place"/> (radians; 0 when not placed).</summary>
    public float PlacedAside { get; private set; }

    /// <summary>The player at the start asked for, and the townsfolk round it on reachable ground.</summary>
    void Populate(IReadOnlyDictionary<string, int> kinds)
    {
        if (options.Arena is { } cast) { PopulateArena(cast, kinds); return; }     // the arena's fighters instead
        var starts = level.Starts.ToList();
        if (starts.Count == 0) throw new SessionException($"The level {level.Name} has no start to begin at.");
        var start = starts[Math.Clamp(startIndex, 0, starts.Count - 1)];
        var people = world.Service<PeopleHooks>();
        var assetsInWorld = world.Service<CharacterAssets>();

        // Each person is said to the modules as it is made (the enhanced module gives it its sheet).
        player = MovementServices.SpawnControlled(world, start.Position, start.Yaw, Locomotion.Sandbox);
        // The view the player starts in, when not the right shoulder (the last view he played in, or --view).
        if (options.StartView is { } startView)
        {
            if (!CameraRig.TryInView(world.Get<CameraRig>(player), startView, out var inView))
                throw new SessionException($"the start view is first, right or left, not '{startView}'");
            world.Set(player, inView);
            options.Say($"view: starts {(startView == "first" ? "in first person" : $"over the {startView} shoulder")} (the view key goes on from there)");
        }
        people.Made(player, PersonRole.Player, "Christof");      // the character drawn, for the fighters' sheets
        CrouchNewBody();                                          // the new body's crouch (1.15 m)
        // --play-as draws and animates the player as another of the session's kinds (a developer's
        // option for first-person pictures of other bodies; the sheet stays Christof's).
        if (playerBody is { } newBody)
        {
            Animate(player, assetsInWorld, newBody.Kind);   // The models repository's man
            // The player's starts and stops on the spring clips (its capsule on the spring as before), when it is on the spring.
            if (options.PersonStarts.Player && StartStopSprings.In(world) && world.TryStore<Animation.People.PersonStep>(out _)
                && assetsInWorld.Kind(newBody.Kind).Person?.Starts is not null)
                world.Set(player, new Animation.People.PersonStep { Curve = 1 });
            GiveAir(player, assetsInWorld, newBody.Kind, curve: 1);   // the player's jumps, fall and landings
            GiveOver(player, assetsInWorld, newBody.Kind);             // the player's moves over and onto things, off edges
            GiveLedge(player, assetsInWorld, newBody.Kind);            // the player's ledge; the townsfolk none
        }
        else if (kinds.TryGetValue(options.PlayAs ?? "Christof", out int christof) || kinds.TryGetValue("Christof", out christof)) Animate(player, assetsInWorld, christof);
        names[player.Packed] = ShownName("Christof");
        GivePlayerBow(kinds);   // the sandbox's player carries a bow beside his sword
        // The sandbox recovery (a convenience, not a rule: the rules package's torpor and incapacity are later).
        if (options.RecoverSeconds > 0f) RecoveryServices.Recover(world, player, options.RecoverSeconds);
        options.Say(options.RecoverSeconds > 0f
            ? string.Create(CultureInfo.InvariantCulture, $"recovery: the player gets up {options.RecoverSeconds:0.#} s after a zero-Health stop, Health back to its effective maximum (a sandbox convenience, ENGINE_SANDBOX, not a rule)")
            : "recovery: off (the player stays down after a zero-Health stop)");

        // The townsfolk out at this time of day, then one or two keepers of the peace; each an observer of its character's kind.
        var perception = world.Service<PerceptionConfig>();
        var townsfolk = options.Night ? Townsfolk.Night : Townsfolk.Day;
        string[] keepers = options.People <= 0 ? [] : (options.Night ? Townsfolk.NightKeepers : Townsfolk.DayKeepers)[..(options.People >= 6 ? 2 : 1)];
        var rng = world.Random.For("host.spawn");
        int total = options.People + keepers.Length;
        for (int i = 0, made = 0; made < total && i < total * 4; i++)
        {
            if (level.Navigation is null || !level.Navigation.RandomReachable(start.Position, 30f, rng, out var at)) continue;
            var name = made < options.People ? townsfolk[made % townsfolk.Length] : keepers[made - options.People];
            var e = MovementServices.SpawnWanderer(world, at, rng.NextFloat(-DetMath.Pi, DetMath.Pi), Locomotion.Sandbox);
            Dress(e, name, kinds, assetsInWorld, people, perception, made < options.People ? CrowdKind(made) : -1);   // The crowd's, when drawn so
            made++;
        }

        // The witnesses asked for, posted where they stand.
        foreach (var text in options.Witnesses)
        {
            if (!WitnessRequest.TryParse(text, out var w, out string error)) throw new SessionException(error);
            int kind = perception.IndexOf(w.Kind);
            if (kind < 0) throw new SessionException($"There is no kind of observer '{w.Kind}' in game {modules.Game.Id} (its kinds: {perception.Names}).");
            var e = PerceptionServices.Witness(world, w, kind, Locomotion.Sandbox);
            string? drawn = perception.Kinds[kind].Characters.FirstOrDefault(kinds.ContainsKey);
            Dress(e, drawn, kinds, assetsInWorld, people, null);
            names[e.Packed] = $"{(drawn is null ? perception.Kinds[kind].Name : Townsfolk.Shown(drawn))} (witness)";
            options.Say($"perception: a witness, {names[e.Packed]} of kind {perception.Kinds[kind].Name}, at {w.Feet.X:0.##} {w.Feet.Y:0.##} {w.Feet.Z:0.##} facing {w.YawDegrees:0.#}°");
        }
        PlaceFoes(kinds, assetsInWorld, people);   // --foe, the hostile fighters placed
        options.Say($"session: the player at start {Math.Clamp(startIndex, 0, starts.Count - 1)} ({start.Name}), {characters.Count - (characters.Contains(player) ? 1 : 0)} townsfolk");
        if (options.NpcActions is { Length: > 0 } npcActions) Script(npcActions);
        if (options.Protected is { Length: > 0 } protectedPeople) Protect(protectedPeople);
    }

    /// <summary>
    /// A townsperson made (from <see cref="Populate"/>): said to the modules (the enhanced module gives it its sheet and
    /// the human's numbers), walking at its 1.1 motion's pace, drawn as <paramref name="name"/> when that character was read,
    /// named, and an observer of its character's kind when <paramref name="perception"/> has one.
    /// </summary>
    void Dress(EntityId e, string? name, IReadOnlyDictionary<string, int> kinds, CharacterAssets assetsInWorld, PeopleHooks people, PerceptionConfig? perception,
               int crowd = -1)
    {
        people.Made(e, PersonRole.Townsperson, name);   // the modules give it its numbers (the human's; its sheet by the character drawn)
        if (name is null) return;
        if (crowd >= 0) DressInCrowd(e, name, kinds, assetsInWorld, crowd);   // drawn as the models repository's crowd
        else if (kinds.TryGetValue(name, out int kind))
        {
            // Each walks at the pace its 1.1 motion was drawn for, so its feet keep to the ground.
            var walk = assetsInWorld.Kind(kind).Motion(MotionRole.Walk).NaturalSpeed;
            if (walk > 0.05f) world.Get<Locomotion>(e).WalkSpeed = walk;
            Animate(e, assetsInWorld, kind);
        }
        names[e.Packed] = Townsfolk.Shown(name);
        if (perception is not null && perception.KindOf(name) is var observer and >= 0) PerceptionServices.Observe(world, e, observer);
    }

    /// <summary>A catalogue name as it is shown: <c>YoungLady</c> is "Young Lady".</summary>
    internal static string ShownName(string catalogue)
    {
        var b = new System.Text.StringBuilder(catalogue.Length + 4);
        for (int i = 0; i < catalogue.Length; i++)
        {
            if (i > 0 && char.IsUpper(catalogue[i]) && char.IsLower(catalogue[i - 1])) b.Append(' ');
            b.Append(catalogue[i]);
        }
        return b.ToString();
    }

    /// <summary>What is under the pointer (or the reticle) and its verb, for the log.</summary>
    public string PointerLine
    {
        get
        {
            var p = world.Service<PointerState>();
            var u = p.Under;
            string what = u.Kind switch
            {
                ReticleTarget.Person => $"{WithPart(NameOf(u.Entity) ?? "a person", u.Part)} (e{EntityId.FromPacked(u.Entity).Index}, {(float.IsFinite(p.Gap) ? $"{p.Gap:0.00} m" : "no")} gap)",
                ReticleTarget.Level => "the level",
                _ => "nothing",
            };
            return string.Create(CultureInfo.InvariantCulture,
                $"under the {(p.Free ? "pointer" : "reticle")}: {what}{(u.Kind == ReticleTarget.Nothing ? "" : $" at {u.Point.X:0.0} {u.Point.Y:0.0} {u.Point.Z:0.00}, {u.Distance:0.0} m from the eyes{(u.Blocked ? ", blocked" : "")}")}; the verb {p.Verb.ToString().ToLowerInvariant()}{(p.Why != TargetRefusal.None ? $" ({p.Why.ToString().ToLowerInvariant()})" : "")}");
        }
    }

    /// <summary>A person's name with the part under the focus ("Guard — head"), or the name alone when no part is.</summary>
    public static string? WithPart(string? name, string? part) => part is { Length: > 0 } && name is not null ? $"{name} — {part}" : name;

    /// <summary>A body's name as the cursor shows it, or null for one that has none (or nothing).</summary>
    public string? NameOf(ulong entity) => names.TryGetValue(entity, out var n) ? n : null;

    /// <summary>1.1's art for a cursor, or null when the install has none (the picture then draws a plain arrow).</summary>
    public TextureImage? CursorArt(CursorId id) => cursors.Art(id);

    void Animate(EntityId e, CharacterAssets assetsInWorld, int kind)
    {
        world.Set(e, CharacterAnimation.For(assetsInWorld, kind));
        world.Set(e, MotionInput.Standing);
        CombatServices.Arm(world, e);       // every character can attack, block, be hurt and die (as its set allows)
        characters.Add(e);
    }

    /// <summary>
    /// The sheets asked for (<c>--sheet</c>): each who found among the people made — <c>Christof</c> or <c>player</c>
    /// for the player, else as <see cref="Script"/> finds them — and given the file's sheet through the rules module's
    /// <see cref="IPersonSheets"/>; a who nobody is, a file that cannot be read or a sheet the module refuses stops the start.
    /// </summary>
    void GiveSheets()
    {
        if (options.Sheets.Count == 0) return;
        if (!world.TryService<IPersonSheets>(out var route)) throw new SessionException($"--sheet: no module of game {modules.Game.Id} takes sheets");
        foreach (var request in options.Sheets)
        {
            // A template's id (sandbox.elite_stress) where no file of that name is, as the arena takes it; the template's
            // sheet comes from the module's data, so a recording of it needs no text.
            if (request.Text is null && !File.Exists(request.File) && route.Templates.Contains(request.File))
            {
                foreach (var e in Who("--sheet", request.Who))
                {
                    if (!route.TryTemplate(e, request.File, out string why)) throw new SessionException($"--sheet {request.Who}={request.File}: {why}");
                    options.Say($"sheet: {NameOf(e.Packed) ?? e.ToString()} (e{e.Index}) has the template {request.File}");
                }
                continue;
            }
            string text;
            try { text = request.Text ?? File.ReadAllText(request.File); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new SessionException($"--sheet: {request.File} could not be read: {e.Message} (a sheet is a file or one of the templates: {string.Join(", ", route.Templates)})");
            }
            var found = Who("--sheet", request.Who);
            foreach (var e in found)
            {
                try { options.Say($"sheet: {route.Give(e, text, request.File)}"); }
                catch (PersonSheetException x) { throw new SessionException($"--sheet {request.Who}={request.File}: {string.Join("; ", x.Problems)}"); }
            }
        }
    }

    /// <summary>
    /// Who a <c>--sheet</c> or <c>--learn</c> names among the people made (an earlier build shared): <c>Christof</c>, <c>player</c>
    /// or the player's <c>e&lt;index&gt;</c> for the player, else a shown name (case aside, <c>Guard</c> for <c>Guard
    /// (witness)</c>), a 1.1 template or <c>e&lt;index&gt;</c> — every match; nobody stops the start by name.
    /// </summary>
    List<EntityId> Who(string option, string who)
    {
        var assetsInWorld = world.Service<CharacterAssets>();
        var found = new List<EntityId>();
        if (who.Equals("player", StringComparison.OrdinalIgnoreCase) || who.Equals("Christof", StringComparison.OrdinalIgnoreCase)
            || who.Equals($"e{player.Index}", StringComparison.OrdinalIgnoreCase))
            found.Add(player);
        else
            foreach (var packed in names.Keys)
            {
                var e = EntityId.FromPacked(packed);
                if (e == player) continue;
                string shown = NameOf(e.Packed) ?? "";
                string kind = KindNameOf(e, assetsInWorld);   // a townsperson on the crowd by its 1.1 template
                if (shown.Equals(who, StringComparison.OrdinalIgnoreCase) || shown.StartsWith(who + " (", StringComparison.OrdinalIgnoreCase)
                    || kind.Equals(who, StringComparison.OrdinalIgnoreCase) || who.Equals($"e{e.Index}", StringComparison.OrdinalIgnoreCase))
                    found.Add(e);
            }
        if (found.Count == 0) throw new SessionException($"{option}: nobody here is '{who}' (people: Christof e{player.Index}, {string.Join(", ", characters.Where(c => c != player).Select(c => $"{NameOf(c.Packed)} e{c.Index}"))})");
        return found;
    }

    /// <summary>
    /// The powers asked for (<c>--learn</c>): each who found (<see cref="Who"/>; the player when none is named) and the
    /// power learned at its rank through the rules module's <see cref="IPersonSheets"/> with the options given — or, given none,
    /// the module's options for it; a refusal stops the start with the module's reason.
    /// </summary>
    void GiveLearned()
    {
        if (options.Learn.Count == 0) return;
        if (!world.TryService<IPersonSheets>(out var route)) throw new SessionException($"--learn: no module of game {modules.Game.Id} keeps sheets");
        foreach (var request in options.Learn)
        {
            var given = request.Options.Count > 0 ? request.Options : route.OptionsOf(request.Power);
            foreach (var e in Who("--learn", request.Who))
            {
                if (!route.TryLearn(e, request.Power, request.Rank, given, out string error))
                    throw new SessionException($"--learn {request.Text}: {error}");
                options.Say($"learn: {NameOf(e.Packed) ?? e.ToString()} (e{e.Index}) {request.Power} {request.Rank}{(given.Count > 0 ? $" with {string.Join(", ", given)}" : "")}");
            }
        }
    }

    /// <summary>
    /// The townsfolk's script (<c>--npc-actions</c>): each line's who found among the people made — a shown name
    /// (<c>Guard</c>, <c>Guard (witness)</c>; case aside), a 1.1 template or catalogue name, or <c>e&lt;index&gt;</c> —
    /// every match acting.
    /// </summary>
    void Script(string text)
    {
        if (!ActionScript.TryParse(text, world.Service<CombatSettings>(), out var lines, out string error)) throw new SessionException(error);
        var script = world.Service<ActionScript>();
        var assetsInWorld = world.Service<CharacterAssets>();
        foreach (var line in lines)
        {
            int found = 0;
            foreach (var e in characters)
            {
                if (e == player) continue;
                string shown = NameOf(e.Packed) ?? "";
                string kind = KindNameOf(e, assetsInWorld);   // a townsperson on the crowd by its 1.1 template
                bool match = shown.Equals(line.Who, StringComparison.OrdinalIgnoreCase)
                             || shown.StartsWith(line.Who + " (", StringComparison.OrdinalIgnoreCase)
                             || kind.Equals(line.Who, StringComparison.OrdinalIgnoreCase)
                             || line.Who.Equals($"e{e.Index}", StringComparison.OrdinalIgnoreCase);
                if (!match) continue;
                script.Add(e, line);
                found++;
            }
            if (found == 0) throw new SessionException($"--npc-actions: nobody here is '{line.Who}' (people: {string.Join(", ", characters.Where(c => c != player).Select(c => $"{NameOf(c.Packed)} e{c.Index}"))})");
            options.Say(string.Create(CultureInfo.InvariantCulture,
                $"actions: {line.Who} ({found}) {line.Act.ToString().ToLowerInvariant()} at {line.From / (float)World.TicksPerSecond:0.##} s to {line.To / (float)World.TicksPerSecond:0.##} s{(line.Aim is { } a ? $", aimed {a * DetMath.RadToDeg:+0;-0}°" : "")}"));
        }
    }

    /// <summary>
    /// The people marked protected (<c>--protected</c>): each name found among the people made as
    /// <c>--npc-actions</c> finds them (a shown name, a template or catalogue name, or <c>e&lt;index&gt;</c>), every match marked.
    /// </summary>
    void Protect(string text)
    {
        var assetsInWorld = world.Service<CharacterAssets>();
        foreach (var who in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int found = 0;
            foreach (var e in characters)
            {
                if (e == player) continue;
                string shown = NameOf(e.Packed) ?? "";
                string kind = KindNameOf(e, assetsInWorld);   // a townsperson on the crowd by its 1.1 template
                if (!(shown.Equals(who, StringComparison.OrdinalIgnoreCase) || shown.StartsWith(who + " (", StringComparison.OrdinalIgnoreCase)
                      || kind.Equals(who, StringComparison.OrdinalIgnoreCase) || who.Equals($"e{e.Index}", StringComparison.OrdinalIgnoreCase))) continue;
                ProtectedPeople.Mark(world, e);
                found++;
            }
            if (found == 0) throw new SessionException($"--protected: nobody here is '{who}' (people: {string.Join(", ", characters.Where(c => c != player).Select(c => $"{NameOf(c.Packed)} e{c.Index}"))})");
            options.Say($"contact: {who} ({found}) protected — weapons never harm them");
        }
    }

    /// <summary>Contact's set-up for the log: the window's rule, the player's blows' windows, the protected kinds, the level's surfaces.</summary>
    static string ContactLine(World world)
    {
        var ic = CultureInfo.InvariantCulture;
        var config = world.Service<ContactConfig>();
        var assets = world.Service<CharacterAssets>();
        var windows = world.Service<ActiveWindows>();
        var shown = new List<string>();
        int christof = assets.FindKind("Christof");
        if (christof >= 0 && assets.Kind(christof).Actions is { } set)
            foreach (var m in set.Quick.Append(set.Strong).Where(m => m.Has).DistinctBy(m => m.Clip))
            {
                var w = windows.For(assets, christof, m.Clip, m.StrikeSeconds, config.Window);
                if (w.Has) shown.Add(string.Create(ic, $"{assets.Clip(m.Clip).Name} {w.From:0.000}–{w.To:0.000} s of {assets.Clip(m.Clip).Duration:0.00} (strike {w.Strike:0.000}, peak {w.PeakSpeed:0} m/s{(w.FromMotion ? "" : ", the least span")})"));
            }
        // The directions' blows (the wind-ups), each from where its strike proper begins.
        if (christof >= 0 && assets.Kind(christof).Actions is { } ws)
            foreach (var st in ws.Stitched)
            {
                var w = windows.For(assets, christof, st.Motion.Clip, st.Motion.StrikeSeconds, config.Window);
                if (w.Has) shown.Add(string.Create(ic, $"{assets.Clip(st.Motion.Clip).Name} {w.From:0.000}–{w.To:0.000} s of {assets.Clip(st.Motion.Clip).Duration:0.00}{(w.Cut > 0f ? $" (opening at the strike proper, {w.Cut:0.000} s of the clip later than the move out of the ready pose)" : "")}"));
            }
        var protectedKinds = Enumerable.Range(0, assets.KindCount).Where(k => config.KindProtected(assets, k)).Select(k => assets.Kind(k).Name).ToList();
        return string.Create(ic, $"contact: active windows where the blow's point moves at least {config.Window.ShareOfPeak:0%} of its fastest{(config.Sandbox ? " (SANDBOX)" : "")}{(shown.Count > 0 ? "; Christof's " + string.Join(", ", shown) : "")}; parry {config.Parry}; protected kinds: {(protectedKinds.Count == 0 ? "none" : string.Join(", ", protectedKinds))}; the level's surfaces: {config.LevelMaterialsKnown} of {config.LevelMaterialNames.Count} named, the rest {ContactConfig.Word(config.LevelDefault)}");
    }

    /// <summary>What a character carries and its action channel can play, for the log.</summary>
    static string ActionsLine(OneOneCharacter c)
    {
        var a = c.Actions;
        string Clip(OneOneAction? x) => x is null ? "none" : $"{x.Clip.Name}{(x.Composed ? " (composed, SANDBOX)" : "")}";
        string held = c.Held.Count == 0 ? "nothing drawn" : string.Join(", ", c.Held.Select(h => $"{h.Model.Name} ({string.Join(", ", h.Things.Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.Kind.ToString().ToLowerInvariant()} on bone {t.Bone}{(t.Kind == HeldKind.Blade ? $" {t.Length:0.00} m r {t.Radius:0.000}" : "")}")))})"));
        return a is null ? $"actions: {c.Name} has none"
            : $"actions: {c.Name} carries {(c.Weapon?.Template ?? "nothing")} (carry {c.Weapon?.Carry ?? 0}): quick {(a.Quick.Count == 0 ? "none" : string.Join(" / ", a.Quick.Select(q => q.Clip.Name)))}, strong {Clip(a.Strong)}, block {Clip(a.Block)}, hurt {Clip(a.Hurt)}, deaths {Clip(a.DeathQuick)} / {Clip(a.DeathSlow)}; holds {held}"
              + (c.Stitched.Count == 0 ? "" : $"; stitched {string.Join(", ", c.Stitched.Select(b => $"{b.Direction} from {b.From}"))} (composed, SANDBOX)");
    }

    /// <summary>
    /// This tick's blows, strikes, blocks and refusals as lines, added to <paramref name="into"/>: for a run's summary,
    /// and the shape of the log lines a caller may give them (<see cref="Tell"/> is the).
    /// </summary>
    readonly List<string> fightLines = [];

    /// <summary>The log's lines for this tick's fight events (the action work's events), added to <paramref name="into"/>.</summary>
    public void FightLines(List<string> into)
    {
        var ic = CultureInfo.InvariantCulture;
        var assetsInWorld = world.Service<CharacterAssets>();
        string Who(EntityId e)
        {
            string name = NameOf(e.Packed) ?? "someone";
            return name.EndsWith(')') ? string.Create(ic, $"{name[..^1]}, e{e.Index})") : string.Create(ic, $"{name} (e{e.Index})");
        }
        var begun = world.Events<AttackBegun>().ThisTick;
        for (int i = 0; i < begun.Count; i++)
        {
            var b = begun[i];
            // A directional blow says its direction (begun at the press; held ready while attack is held).
            string blow = b.Direction switch
            {
                AttackDirection.Left => $"a blow from the left #{b.Swing} (",
                AttackDirection.Right => $"a blow from the right #{b.Swing} (",
                AttackDirection.Overhead => $"an overhead blow #{b.Swing} (",
                AttackDirection.Thrust => $"a thrust #{b.Swing} (",
                _ => null!,
            };
            into.Add(b.Direction == AttackDirection.None
                ? string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(b.Entity)} begins a {b.Strength.ToString().ToLowerInvariant()} blow #{b.Swing}, {assetsInWorld.Clip(b.Clip).Name}, aimed {b.AimPitch * DetMath.RadToDeg:+0.0;-0.0}°")
                : string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(b.Entity)} begins {blow}{assetsInWorld.Clip(b.Clip).Name}), aimed {b.AimPitch * DetMath.RadToDeg:+0.0;-0.0}°"));
        }
        // A blow held ready and let go (a tap, held 0 s, says nothing more).
        if (world.TryService<EventQueue<AttackReleased>>(out var releasedQ))
            for (int i = 0; i < releasedQ.ThisTick.Count; i++)
            {
                var x = releasedQ.ThisTick[i];
                if (x.HeldSeconds <= 0f) continue;
                string from = x.Direction switch
                {
                    AttackDirection.Left => "from the left", AttackDirection.Right => "from the right",
                    AttackDirection.Overhead => "overhead", AttackDirection.Thrust => "with a thrust", _ => "",
                };
                into.Add(string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(x.Entity)} strikes {from} (#{x.Swing}) after holding it {x.HeldSeconds:0.00} s"));
            }
        // A held blow switched to another direction.
        if (world.TryService<EventQueue<AttackSwitched>>(out var switchedQ))
            for (int i = 0; i < switchedQ.ThisTick.Count; i++)
            {
                var x = switchedQ.ThisTick[i];
                static string Way(AttackDirection d) => d switch
                {
                    AttackDirection.Left => "from the left", AttackDirection.Right => "from the right",
                    AttackDirection.Overhead => "overhead", AttackDirection.Thrust => "to a thrust", _ => "",
                };
                into.Add(string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(x.Entity)} turns the held blow #{x.Swing} {Way(x.To)} (was {Way(x.From)}){(x.AtReady ? "" : ", still winding up")}"));
            }
        // The stagger when hit — a wound's (the swing it cut short, the chest's push) or a guard overpowered.
        if (world.TryService<EventQueue<Staggered>>(out var staggeredQ))
            for (int i = 0; i < staggeredQ.ThisTick.Count; i++)
            {
                var x = staggeredQ.ThisTick[i];
                into.Add(x.Cause == StaggerCause.Overpowered
                    ? string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(x.Who)} staggered {x.Seconds:0.00} s: its guard overpowered by {Who(x.By)}{(x.Interrupted ? $"; its blow #{x.Swing} cut short before its strike" : "")}")
                    : string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(x.Who)} staggered {x.Seconds:0.00} s by {Who(x.By)}'s blow (contact #{x.ContactId}){(x.Interrupted ? $": its blow #{x.Swing} cut short before its strike" : "")}; the chest pushed {x.PushDegrees:0}°"));
            }
        var strikes = world.Events<StrikeMoment>().ThisTick;
        for (int i = 0; i < strikes.Count; i++)
        {
            var t = strikes[i];
            into.Add(string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(t.Entity)} strikes (#{t.Swing}): the tip at {t.Tip.X:0.00} {t.Tip.Y:0.00} {t.Tip.Z:0.00}, {t.TipSpeed:0.0} m/s"));
        }
        var raised = world.Events<BlockRaised>().ThisTick;
        for (int i = 0; i < raised.Count; i++)
            into.Add(string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(raised[i].Entity)} raises the block"));
        var lowered = world.Events<BlockLowered>().ThisTick;
        for (int i = 0; i < lowered.Count; i++)
            into.Add(string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(lowered[i].Entity)} lowers the block after {lowered[i].HeldSeconds:0.00} s"));
        // A refusal is told once until its fighter begins a blow or is refused for another reason (an earlier build's log had
        // a line for every click while a blow waited: "attack refused: mid-swing, a blow already waiting", eleven in two s).
        for (int i = 0; i < begun.Count; i++) refusalTold.Remove(begun[i].Entity.Packed);
        var refused = world.Events<AttackRefused>().ThisTick;
        for (int i = 0; i < refused.Count; i++)
        {
            var r = refused[i];
            if (refusalTold.TryGetValue(r.Entity.Packed, out var told) && told == r.Why) continue;
            refusalTold[r.Entity.Packed] = r.Why;
            into.Add(string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(r.Entity)}'s attack refused: {RefusalWords(r.Why)}{(r.Why == AttackRefusal.MidSwing ? " (said once until the next blow)" : "")}"));
        }
    }

    /// <summary>The last refusal told for each fighter since its last blow began (the fight lines tell a refusal once).</summary>
    readonly Dictionary<ulong, AttackRefusal> refusalTold = [];

    /// <summary>
    /// Contact's lines this tick (<see cref="Tell"/> writes them): each contact made, a swing's rejected meetings, a protected person's protest, the catch let go.
    /// </summary>
    public void ContactLines(List<string> into)
    {
        var ic = CultureInfo.InvariantCulture;
        string Who(EntityId e)
        {
            if (e.IsNone) return "the level";
            string name = NameOf(e.Packed) ?? "someone";
            return name.EndsWith(')') ? string.Create(ic, $"{name[..^1]}, e{e.Index})") : string.Create(ic, $"{name} (e{e.Index})");
        }
        var made = world.Events<ContactMade>().ThisTick;
        for (int i = 0; i < made.Count; i++)
        {
            var c = made[i];
            string what = c.Met switch
            {
                ContactMet.Body => $"{Who(c.Target)} — {c.Site}",
                ContactMet.Shield => $"{Who(c.Target)}'s shield",
                ContactMet.Weapon => $"{Who(c.Target)}'s blade",
                _ => "the level",
            };
            if (c.Shot > 0)   // an arrow's contact
                into.Add(string.Create(ic, $"{world.Seconds:0.00} s: contact {c.ContactId}: {Who(c.Attacker)}'s arrow #{c.Shot} meets {what} ({ContactConfig.Word(c.Material)}) at {c.Point.X:0.00} {c.Point.Y:0.00} {c.Point.Z:0.00}, {c.PointSpeed:0.0} m/s ({c.ImpactSpeed:0.0} into it), impulse {c.NormalImpulse:0.00} N·s{(c.Protected ? ", protected" : "")}; it stops there"));
            else
            into.Add(string.Create(ic, $"{world.Seconds:0.00} s: contact {c.ContactId}: {Who(c.Attacker)}'s blow #{c.Swing} meets {what} ({ContactConfig.Word(c.Material)}) at {c.Point.X:0.00} {c.Point.Y:0.00} {c.Point.Z:0.00}, {c.ImpactSpeed:0.0} m/s, impulse {c.NormalImpulse:0.0} N·s{(c.Protected ? ", protected" : "")}{(c.Met is ContactMet.Shield or ContactMet.Weapon or ContactMet.Level ? "; the swing stops" : "")}"));
        }
        var dup = world.Events<ContactDuplicates>().ThisTick;
        for (int i = 0; i < dup.Count; i++)
            into.Add(string.Create(ic, $"{world.Seconds:0.00} s: contact: {Who(dup[i].Attacker)}'s blow #{dup[i].Swing} ends; {dup[i].Rejected} meeting{(dup[i].Rejected == 1 ? "" : "s")} again rejected ({dup[i].Contacts} contact{(dup[i].Contacts == 1 ? "" : "s")})"));
        var protests = world.Events<ProtectedProtest>().ThisTick;
        for (int i = 0; i < protests.Count; i++)
            into.Add(string.Create(ic, $"{world.Seconds:0.00} s: contact: {Who(protests[i].Who)} protests — struck by {Who(protests[i].Attacker)}, but weapons never harm them"));
        var let = world.Events<CatchLetGo>().ThisTick;
        for (int i = 0; i < let.Count; i++)
            into.Add(string.Create(ic, $"{world.Seconds:0.00} s: masquerade: the catch lets go — {Who(let[i].Entity)} attacked by {Who(let[i].Attacker)}; unbound"));
    }

    /// <summary>
    /// For a caller of <see cref="Tell"/>: what each blade did at a contact this tick — it stops in
    /// a body, springs back off a shield, a blade or the level, or goes on past a part it cut off.
    /// </summary>
    public void FollowThroughLines(List<string> into)
    {
        var ic = CultureInfo.InvariantCulture;
        string Who(EntityId e)
        {
            string name = NameOf(e.Packed) ?? "someone";
            return name.EndsWith(')') ? string.Create(ic, $"{name[..^1]}, e{e.Index})") : string.Create(ic, $"{name} (e{e.Index})");
        }
        if (!world.TryService<EventQueue<SwingFollowThrough>>(out var q)) return;
        var lines = q.ThisTick;
        for (int i = 0; i < lines.Count; i++)
        {
            var f = lines[i];
            string what = (f.Outcome, f.Met) switch
            {
                (BladeOutcome.GoesOn, _) => $"goes on past {Who(f.Whom)}'s severed {BodyRegions.Name(f.Region)}",
                (BladeOutcome.Stops, _) => $"stops in {Who(f.Whom)}'s {BodyRegions.Name(f.Region)}",
                (_, ContactMet.Shield) => $"rebounds off {Who(f.Whom)}'s shield",
                (_, ContactMet.Weapon) => $"rebounds off {Who(f.Whom)}'s blade",
                _ => "rebounds off the level",
            };
            string back = f.Outcome == BladeOutcome.GoesOn ? "" : f.ReadySeconds > 0f ? string.Create(ic, $"; back to guard in {f.ReadySeconds:0.00} s") : "; cut short";
            string sprang = f.ReboundMetres > 0f ? string.Create(ic, $", its tip springing back {f.ReboundMetres:0.00} m") : "";
            into.Add(string.Create(ic, $"{world.Seconds:0.00} s: contact: {Who(f.Attacker)}'s blow #{f.Swing}: the blade {what}{sprang} (contact {f.Chain} of the swing){back}"));
        }
        // The attacker's stun after a blocked blow.
        if (world.TryService<EventQueue<AttackerStunned>>(out var stuns))
            foreach (var s in stuns.ThisTick)
                into.Add(string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(s.Attacker)} stunned {s.Seconds:0.00} s ({(s.Parried ? "parried" : "blocked")}: no attack until it passes; a guard and a dodge allowed)"));
    }

    /// <summary>Why an attack was refused, in words.</summary>
    public static string RefusalWords(AttackRefusal why) => why switch
    {
        AttackRefusal.NoWeaponHand => "no weapon hand (cut off)",
        AttackRefusal.Catch => "the masquerade's catch is on (let it go with the masquerade key, or in a free place)",
        AttackRefusal.MidSwing => "mid-swing, a blow already waiting",
        AttackRefusal.Blocking => "blocking",
        AttackRefusal.Moving => "in a move",
        AttackRefusal.NoAttack => "no attack in the motion set",
        AttackRefusal.Dead => "dead",
        AttackRefusal.Stunned => "stunned a moment (the blade was blocked); the blow is buffered",
        _ => why.ToString(),
    };

    /// <summary>One tick with these actions; what was drawn before it is kept for the picture between ticks.</summary>
    public void Step(in ActionFrame actions)
    {
        if (actions.AttackMode is { } attackMode) ChangeAttackDirection(attackMode);   // the Controls page's change, from this tick
        var bodies = world.Store<Body>();
        world.TryStore<Animation.People.PersonGround>(out var grounds);
        foreach (var e in characters)
            if (bodies.Has(e)) { ref var body = ref bodies.Get(e); before[e.Packed] = (body.Feet, body.Yaw, LineOf(grounds, e)); }
        var view = world.Service<ViewState>();
        if (view.HasView) { viewBefore = view.View; bobBefore = bobNow; haveViewBefore = true; }
        MovementServices.Step(world, WithoutCombat(actions));   // the new body's missing combat taken out
        viewNow = view.View;
        bobNow = view.Head - view.Eyes;
        if (!haveViewBefore) (viewBefore, bobBefore) = (viewNow, bobNow);
        Tell();
        eventTrace?.Tick(world);
    }

    /// <summary>
    /// What the world's edges said this tick, logged and put on the status line
    /// for a few seconds: an exit the player reached (taking it is not built yet), a body put back after falling
    /// out of the world.
    /// </summary>
    void Tell()
    {
        var reached = world.Events<ExitReached>().ThisTick;
        for (int i = 0; i < reached.Count; i++)
        {
            var r = reached[i];
            Notice($"{level.Exits[r.Exit].Name}; changing level is not built yet");
        }
        var recovered = world.Events<BodyRecovered>().ThisTick;
        for (int i = 0; i < recovered.Count; i++)
        {
            var r = recovered[i];
            string line = string.Create(CultureInfo.InvariantCulture,
                $"{(r.Entity == player ? "the player" : $"body {r.Entity.Index}")} fell out of the world (z {r.From.Z:0.0}) and was put back where it last stood, {r.To.X:0.0} {r.To.Y:0.0} {r.To.Z:0.00}");
            if (r.Entity == player) Notice(line);
            else options.Say(line);
        }
        // The player's moves, in the log only, so a drive at the proving ground can be read back: what began
        // (the obstacle's height above the feet), how it ended, the lip held and let go.
        var began = world.Events<TraversalStarted>().ThisTick;
        for (int i = 0; i < began.Count; i++)
            if (began[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: move: {Word(began[i].Kind)} begins at {began[i].From.X:0.00} {began[i].From.Y:0.00} {began[i].From.Z:0.00}, {(began[i].Kind == TraversalKind.LowerToHang ? "a drop" : "an obstacle")} of {began[i].Height:0.00} m, to {began[i].To.X:0.00} {began[i].To.Y:0.00} {began[i].To.Z:0.00}"));
        // What the low vaults' clips do not serve keeps the earlier build's stand-ins (the sprint vault, the run's step-over): said once each.
        for (int i = 0; i < began.Count; i++)
            if (began[i].Entity == player && began[i].Kind is TraversalKind.Hurdle or TraversalKind.StepOver && options.PersonMoves is { On: true, LowVaults: true }
                && world.TryStore<Animation.People.PersonCommit>(out var pcs) && pcs.Has(player) && saidPaceStandIn.Add(began[i].Kind)
                && world.Service<CharacterAssets>().Kind(world.Get<Animation.CharacterAnimation>(player).Kind).Person?.Over?.Paths is { HasLow: true })
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: move: {Word(began[i].Kind)} on the stand-in over an obstacle of {began[i].Height:0.00} m: no low vault's clip serves it (hurdle_low a thin top 0.35-0.6 m, vault_speed 0.4-0.65 m high and 0.3-0.75 m deep; said once)"));
        var ended = world.Events<TraversalEnded>().ThisTick;
        for (int i = 0; i < ended.Count; i++)
            if (ended[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: move: {Word(ended[i].Kind)} {(ended[i].Completed ? "ends" : "is cut short")} at {ended[i].Feet.X:0.00} {ended[i].Feet.Y:0.00} {ended[i].Feet.Z:0.00}"));
        var grabbed = world.Events<LedgeGrabbed>().ThisTick;
        for (int i = 0; i < grabbed.Count; i++)
            if (grabbed[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture, $"{world.Seconds:0.00} s: move: a lip held at {grabbed[i].Lip.Z:0.00} m ({grabbed[i].Height:0.00} m above the ground left)"));
        // The commit point in the air: what the player committed to, its contact's time foreseen, the lip, and a catch's why.
        var committed = world.Events<AirCommitted>().ThisTick;
        for (int i = 0; i < committed.Count; i++)
            if (committed[i].Entity == player)
            {
                var c = committed[i];
                string what = c.Outcome switch
                {
                    AirOutcome.Catch => string.Create(CultureInfo.InvariantCulture, $"catch the lip at {c.Lip.Z:0.00} m {(c.Way > 0 ? "rising" : "falling")} ({(c.Auto ? "jumped at it" : "use held")})"),
                    AirOutcome.Past => string.Create(CultureInfo.InvariantCulture, $"fall past the lip at {c.Lip.Z:0.00} m {(c.Way > 0 ? "rising" : "falling")} ({c.Why switch
                    {
                        CatchRefused.NoRoom => "no room to hang there", CatchRefused.PathBlocked => "the catch's way is blocked", CatchRefused.LipEnds => "the lip ends under the hands",
                        CatchRefused.NoClimb => "not to be climbed", CatchRefused.LipGone => "the lip is gone", _ => "use not held",
                    }})"),
                    AirOutcome.Top => string.Create(CultureInfo.InvariantCulture, $"land on the top at {c.Lip.Z:0.00} m (the feet clear it)"),
                    _ => "land",
                };
                options.Say(string.Create(CultureInfo.InvariantCulture, $"{world.Seconds:0.00} s: air: commit: {what}, the contact in {c.Seconds:0.00} s, from {c.Feet.X:0.00} {c.Feet.Y:0.00} {c.Feet.Z:0.00}"));
            }
        var dropped = world.Events<LedgeDropped>().ThisTick;
        for (int i = 0; i < dropped.Count; i++)
            if (dropped[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture, $"{world.Seconds:0.00} s: move: let go of the lip at {dropped[i].Lip.Z:0.00} m"));
        // The body: the player stopped at an edge by the walk's guard (once a stop), a climb down
        // ended standing, every landing of the player's with its height and class, a landing on a no-stand top.
        var stoppedAt = world.Events<EdgeStopped>().ThisTick;
        for (int i = 0; i < stoppedAt.Count; i++)
            if (stoppedAt[i].Entity == player)
            {
                var s = stoppedAt[i];
                string drop = float.IsPositiveInfinity(s.Drop) ? "no ground within 10 m" : string.Create(CultureInfo.InvariantCulture, $"a drop of {s.Drop:0.00} m");
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: edge: stopped at {s.Feet.X:0.00} {s.Feet.Y:0.00} {s.Feet.Z:0.00}, {drop} (a walk stops beyond {s.Limit:0.00} m)"));
            }
        var downs = world.Events<ClimbedDown>().ThisTick;
        for (int i = 0; i < downs.Count; i++)
            if (downs[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: move: climbed down {downs[i].Drop:0.00} m, standing at {downs[i].Feet.X:0.00} {downs[i].Feet.Y:0.00} {downs[i].Feet.Z:0.00}"));
        var lands = world.Events<Landed>().ThisTick;
        for (int i = 0; i < lands.Count; i++)
            if (lands[i].Entity == player && lands[i].FallHeight >= 0.75f)   // a plain jump's own landing (0.5 m) is not told
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: land: from {lands[i].FallHeight:0.00} m at {lands[i].ImpactSpeed:0.0} m/s, {lands[i].Class.ToString().ToLowerInvariant()}, at {lands[i].Feet.X:0.00} {lands[i].Feet.Y:0.00} {lands[i].Feet.Z:0.00}"));
        var slid = world.Events<NoStandLanded>().ThisTick;
        for (int i = 0; i < slid.Count; i++)
            if (slid[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: land: on a top marked no place to stand at {slid[i].Feet.X:0.00} {slid[i].Feet.Y:0.00} {slid[i].Feet.Z:0.00}; slid off"));
        // Ladders and the hang: on and off a ladder, the shimmy's start and stop, a push-off from a lip.
        var mounted = world.Events<LadderMounted>().ThisTick;
        for (int i = 0; i < mounted.Count; i++)
            if (mounted[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: ladder: on {mounted[i].Name} from its {mounted[i].From switch { LadderEnd.Top => "top", LadderEnd.Side => "side (a landing)", LadderEnd.Hang => "side (a hang)", _ => "foot" }} at {mounted[i].Feet.X:0.00} {mounted[i].Feet.Y:0.00} {mounted[i].Feet.Z:0.00}"));
        // A ladder the clips do not fit, climbed on the earlier build's stand-in: said once a ladder.
        var standIn = world.Events<LadderStandIn>().ThisTick;
        for (int i = 0; i < standIn.Count; i++)
            if (standIn[i].Entity == player && saidLadderStandIn.Add(standIn[i].Ladder))
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: ladder: {standIn[i].Name} leans {standIn[i].LeanDegrees:0.0}° (its top {standIn[i].TopOff:+0.00;-0.00} m off the climb's rungs): no clip set fits it, the stand-in (said once)"));
        // The hang's leaps and corners begun; one asked for and not taken said with why (a corner once a place and reason: it is looked at every tick
        // the wish holds there).
        var hangBegan = world.Events<HangMoveStarted>().ThisTick;
        for (int i = 0; i < hangBegan.Count; i++)
            if (hangBegan[i].Entity == player)
            {
                var hb = hangBegan[i];
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: hang: {(hb.Kind == HangMoveKind.Corner ? "round " : "")}{HangWhat(hb.Kind, hb.Leap, hb.Outside, hb.Way, hb.Braced)} from {hb.From.X:0.00} {hb.From.Y:0.00} {hb.From.Z:0.00} to the hang at {hb.To.X:0.00} {hb.To.Y:0.00} {hb.To.Z:0.00} ({(hb.Kind == HangMoveKind.Leap ? "the lip" : "the corner")} at {hb.Lip.X:0.00} {hb.Lip.Y:0.00} {hb.Lip.Z:0.00})"));
            }
        var hangNot = world.Events<HangMoveRefused>().ThisTick;
        for (int i = 0; i < hangNot.Count; i++)
            if (hangNot[i].Entity == player)
            {
                var hr = hangNot[i];
                if (hr.Kind == HangMoveKind.Corner && !saidHangCorner.Add((MathF.Round(hr.At.X * 5f), MathF.Round(hr.At.Y * 5f), MathF.Round(hr.At.Z * 5f), hr.Why))) continue;
                string instead = hr.Kind == HangMoveKind.Corner ? "the shimmy stops there (said once)" : hr.Leap switch
                {
                    HangLeap.Left or HangLeap.Right => "jump as before (the pull-up where it can, else the shimmy)", HangLeap.Up => "nothing to pull up onto either",
                    HangLeap.Down => "the drop, a lip caught on the way down with use held", HangLeap.Back => "the push-off", _ => "as before",
                };
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: hang: {(hr.Kind == HangMoveKind.Corner ? "not round" : "no")} {HangWhat(hr.Kind, hr.Leap, hr.Outside, hr.Way, null)}: {HangWhy(hr.Why)} (at {hr.At.X:0.00} {hr.At.Y:0.00} {hr.At.Z:0.00}); {instead}"));
            }
        // The walls' moves begun, and one asked for and not taken said with why (once a place: the motor tells it so).
        var wallBegan = world.Events<WallMoveStarted>().ThisTick;
        for (int i = 0; i < wallBegan.Count; i++)
            if (wallBegan[i].Entity == player)
            {
                var wb = wallBegan[i];
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: wall: {WallWhat(wb.Act, wb.Left, wb.Up, wb.Pace, wb.Outside)}{(wb.Sheer ? " (a face with nothing to hold: the splayed grip)" : "")} from {wb.From.X:0.00} {wb.From.Y:0.00} {wb.From.Z:0.00} to {wb.To.X:0.00} {wb.To.Y:0.00} {wb.To.Z:0.00}"));
            }
        var wallNot = world.Events<WallMoveRefused>().ThisTick;
        for (int i = 0; i < wallNot.Count; i++)
            if (wallNot[i].Entity == player)
            {
                var wr = wallNot[i];
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: wall: no {WallWhat(wr.Act, wr.Left, wr.Up, byte.MaxValue, false)}: {WallWhy(wr.Why)} (at {wr.At.X:0.00} {wr.At.Y:0.00} {wr.At.Z:0.00}; said once there)"));
            }
        // Squeezing and cover begun, one not taken said with why (once a place: the motor tells it so), and stealth turned on or off.
        var sideBegan = world.Events<SideMoveStarted>().ThisTick;
        for (int i = 0; i < sideBegan.Count; i++)
            if (sideBegan[i].Entity == player)
            {
                var sb = sideBegan[i];
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: {SideWhat(sb.Act, sb.Way)} at {sb.From.X:0.00} {sb.From.Y:0.00} {sb.From.Z:0.00}{(float.IsNaN(sb.Width) || sb.Width <= 0f ? "" : $" (the gap {sb.Width:0.00} m wide)")}"));
            }
        var sideNot = world.Events<SideMoveRefused>().ThisTick;
        for (int i = 0; i < sideNot.Count; i++)
            if (sideNot[i].Entity == player)
            {
                var sr = sideNot[i];
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: no {SideWhat(sr.Act, 0)}: {SideWhy(sr.Why)}{(float.IsNaN(sr.Width) ? "" : $" (the gap {sr.Width:0.00} m wide)")} (at {sr.At.X:0.00} {sr.At.Y:0.00} {sr.At.Z:0.00}; said once there)"));
            }
        var stealthWas = world.Events<StealthChanged>().ThisTick;
        for (int i = 0; i < stealthWas.Count; i++)
            if (stealthWas[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: stealth {(stealthWas[i].On ? "on: careful and lower, the back to a wall behind as cover" : "off")} (at {stealthWas[i].Feet.X:0.00} {stealthWas[i].Feet.Y:0.00} {stealthWas[i].Feet.Z:0.00})"));
        var leftLadder = world.Events<LadderLeft>().ThisTick;
        for (int i = 0; i < leftLadder.Count; i++)
            if (leftLadder[i].Entity == player)
            {
                string how = leftLadder[i].How switch
                {
                    LadderLeave.Top => "over the top", LadderLeave.Foot => "stepped off at the foot",
                    LadderLeave.PushOff => "pushed off", LadderLeave.LetGo => "let go",
                    LadderLeave.Side => "onto a landing beside it", LadderLeave.Hang => "into a hang beside it", _ => "stopped",
                };
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: ladder: off {leftLadder[i].Name}, {how}, at {leftLadder[i].Feet.X:0.00} {leftLadder[i].Feet.Y:0.00} {leftLadder[i].Feet.Z:0.00}"));
            }
        var shimmies = world.Events<ShimmyStarted>().ThisTick;
        for (int i = 0; i < shimmies.Count; i++)
            if (shimmies[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: move: shimmy {(shimmies[i].Way > 0 ? "right" : "left")} along the lip at {shimmies[i].Lip.Z:0.00} m"));
        var shimmyStops = world.Events<ShimmyStopped>().ThisTick;
        for (int i = 0; i < shimmyStops.Count; i++)
            if (shimmyStops[i].Entity == player)
            {
                string why = shimmyStops[i].Why switch { ShimmyStop.Released => "the key let go", ShimmyStop.End => "the lip's end", _ => "the hang left" };
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: move: the shimmy stops ({why}) at {shimmyStops[i].Feet.X:0.00} {shimmyStops[i].Feet.Y:0.00} {shimmyStops[i].Feet.Z:0.00}"));
            }
        var pushed = world.Events<LipPushedOff>().ThisTick;
        for (int i = 0; i < pushed.Count; i++)
            if (pushed[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture, $"{world.Seconds:0.00} s: move: pushed off the lip at {pushed[i].Lip.Z:0.00} m"));
        // The freed pointer: the verb under it changing, a talk asked for, a target picked or refused.
        var verbs = world.Events<PointerVerbChanged>().ThisTick;
        for (int i = 0; i < verbs.Count; i++)
            if (verbs[i].Entity == player)
            {
                var v = verbs[i];
                string thing = v.Thing != 0 ? " — " + (NameOf(v.Thing) ?? "a person") : "";
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: pointer: {v.Verb.ToString().ToLowerInvariant()}{thing} ({(v.Free ? "the freed pointer" : "the reticle")})"));
            }
        var talks = world.Events<TalkRequested>().ThisTick;
        for (int i = 0; i < talks.Count; i++)
            if (talks[i].Who == player)
                Notice($"talk: {NameOf(talks[i].Whom.Packed) ?? "a person"} — conversations are not built yet");
        var picked = world.Events<TargetPicked>().ThisTick;
        for (int i = 0; i < picked.Count; i++)
            if (picked[i].Who == player)
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: target: picked {(picked[i].Thing != 0 ? NameOf(picked[i].Thing) ?? "a person" : "the level")} at {picked[i].Point.X:0.00} {picked[i].Point.Y:0.00} {picked[i].Point.Z:0.00}, {picked[i].Distance:0.0} m (request {picked[i].Request})"));
        var refusedTargets = world.Events<TargetRefused>().ThisTick;
        for (int i = 0; i < refusedTargets.Count; i++)
            if (refusedTargets[i].Who == player)
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: target: refused, {refusedTargets[i].Why switch { TargetRefusal.TooFar => "too far", TargetRefusal.WrongKind => "not a kind it takes", TargetRefusal.Nothing => "nothing there", _ => refusedTargets[i].Why.ToString().ToLowerInvariant() }} ({refusedTargets[i].Distance:0.0} m; request {refusedTargets[i].Request})"));
        // Who sees: an observer's awareness changing, a shout, what an observer does, an area's heat.
        TellPerception();
        TellCombat();       // contacts, harm, deaths
        TellPowers();       // powers used, refused, ended
        // The fights: blows begun, strikes, blocks raised and lowered, attacks refused.
        fightLines.Clear();
        FightLines(fightLines);
        for (int i = 0; i < fightLines.Count; i++) options.Say(fightLines[i]);
        // What stopped the player (once a stop): its height and depth, and the move that would take it.
        var stops = world.Events<PlayerStopped>().ThisTick;
        for (int i = 0; i < stops.Count; i++)
            if (stops[i].Entity == player)
            {
                var p = stops[i];
                var ic = CultureInfo.InvariantCulture;
                string pace = p.Gait.ToString().ToLowerInvariant();
                // A thin thing in the player's vault clips' bands is vaulted by a run or a sprint by itself (the run's own vault), a walk tapping.
                string taker = RunVaults(p) ? "a run or a sprint vaults it (a walk: a tap)" : p.By switch
                {
                    StopTaker.Run => $"a run {(p.Move == TraversalKind.StepOver ? "steps over" : Word(p.Move))} it",
                    StopTaker.Sprint => "a sprint vaults it",
                    StopTaker.Tap => $"a tap {Took(p.Move)} it",
                    StopTaker.Charge => $"a charge {Took(p.Move)} it",
                    _ => "nothing takes it",
                };
                string what = p.Cause switch
                {
                    StopCause.TooHigh => string.Create(ic, $"a {p.Height:0.00} m thing {p.Depth:0.00} m deep: {taker}"),
                    StopCause.Wall => "a wall",
                    StopCause.NoClimb => LadderAt(p.Feet, p.Way) is { } lad
                        ? $"the ladder {lad} (use climbs it)"
                        : string.Create(ic, $"a {p.Height:0.00} m face marked not to be climbed"),
                    StopCause.NoStand => "a top marked no place to stand",
                    _ => "something the level does not show (a body, a ceiling, a slope)",
                };
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: stop: at {p.Feet.X:0.00} {p.Feet.Y:0.00} {p.Feet.Z:0.00} (at a {pace}), {what}"));
            }
        // The jump: every take-off, tapped or charged; a charge's steps; a move refused at a face marked not to
        // be climbed. The pace: the sprint kept and let go, supernatural speed on and off.
        var launched = world.Events<JumpLaunched>().ThisTick;
        for (int i = 0; i < launched.Count; i++)
            if (launched[i].Entity == player)
            {
                var j = launched[i];
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: jump: {(j.Kind == JumpKind.Tap ? "a tap" : $"charged {j.ChargedSeconds:0.00} s")}, to {j.Height:0.00} m, from {j.Feet.X:0.00} {j.Feet.Y:0.00} {j.Feet.Z:0.00}"));
            }
        var charges = world.Events<JumpCharge>().ThisTick;
        for (int i = 0; i < charges.Count; i++)
            if (charges[i].Entity == player)
            {
                var c = charges[i];
                var ic = CultureInfo.InvariantCulture;
                string what = c.Step switch
                {
                    ChargeStep.Began => string.Create(ic, $"jump: charging ({c.Height:0.00} m now, up to {c.Most:0.00} m)"),
                    ChargeStep.LetGo => string.Create(ic, $"jump: the charge let go at {c.Height:0.00} m"),
                    ChargeStep.Cancelled => string.Create(ic, $"jump: the charge cancelled at {c.Height:0.00} m, nothing jumps"),
                    _ => string.Create(ic, $"jump: the charge lost at {c.Height:0.00} m (the ground left)"),
                };
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: {what}"));
            }
        var refused = world.Events<MoveRefused>().ThisTick;
        for (int i = 0; i < refused.Count; i++)
            if (refused[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: move: a {Word(refused[i].Kind)} refused, the face is marked not to be climbed (lip {refused[i].Lip.X:0.00} {refused[i].Lip.Y:0.00} {refused[i].Lip.Z:0.00}, {refused[i].Height:0.00} m)"));
        var kept = world.Events<SprintKept>().ThisTick;
        for (int i = 0; i < kept.Count; i++)
            if (kept[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: pace: {(kept[i].Supernatural ? "supernatural speed" : "sprint")} {(kept[i].Kept ? "kept" : $"let go ({kept[i].Why.ToString().ToLowerInvariant()})")}"));
        var fast = world.Events<SupernaturalSpeed>().ThisTick;
        for (int i = 0; i < fast.Count; i++)
            if (fast[i].Entity == player)
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"{world.Seconds:0.00} s: pace: supernatural speed {(fast[i].On ? $"on ({fast[i].Speed:0.0} m/s)" : "off")}"));
        // The masquerade: the player's mode changing, and every act no human could do in public.
        var changed = world.Events<MasqueradeModeChanged>().ThisTick;
        for (int i = 0; i < changed.Count; i++)
        {
            var c = changed[i];
            if (c.Entity != player) continue;
            bool on = c.Mode == MasqueradeMode.Masquerade;
            Notice(c.Cause switch
            {
                MasqueradeCause.EnteredPublic => "masquerade: on — entering a public place",
                MasqueradeCause.EnteredFree => "unbound — entering a free place",
                _ => on ? "masquerade: on — moving as a human" : "unbound — moving as the body can",
            });
        }
        var risks = world.Events<MasqueradeRisk>().ThisTick;
        for (int i = 0; i < risks.Count; i++)
        {
            var r = risks[i];
            string line = string.Create(CultureInfo.InvariantCulture,
                $"masquerade risk: {r.Words} in public at {r.Where.X:0.0} {r.Where.Y:0.0} {r.Where.Z:0.00}");
            if (r.Entity == player) Notice(line);
            else options.Say($"body {r.Entity.Index}: {line}");
        }
    }

    /// <summary>A move's name for the log: <c>HopUp</c> is "hop up".</summary>
    /// <summary>The name of a ladder whose face is just ahead of <paramref name="feet"/> along <paramref name="way"/> (for the stop's line), or null.</summary>
    string? LadderAt(Vector3 feet, Vector2 way)
    {
        var ladders = level.Collision.Ladders;
        for (int k = 1; k <= 8; k++)
        {
            var at = feet + new Vector3(way * (0.1f * k), 0.5f);
            for (int i = 0; i < ladders.Count; i++)
                if (ladders.OnFace(at) && Vector3.DistanceSquared(at, ladders[i].FaceAt(at.Z)) < 1f) return ladders[i].Name;
        }
        return null;
    }

    /// <summary>What a tap or a charge does to a thing, for the stop's line.</summary>
    static string Took(TraversalKind kind) => kind switch
    {
        TraversalKind.HopUp => "hops up onto", TraversalKind.Vault => "vaults", TraversalKind.ClimbOnto => "climbs onto",
        TraversalKind.Mantle => "mantles", TraversalKind.Grab => "grabs", _ => Word(kind) + "s",
    };

    /// <summary>The log's lines for combat: each contact the world found, the harm the rules made of it, a death.</summary>
    readonly List<string> contactLines = [];

    void TellCombat()
    {
        var ic = CultureInfo.InvariantCulture;
        bool tempoRegistered = tempoKnown ??= Vitomir.Platform.Combat.CombatTempos.Registered(world);
        string Who(EntityId e)
        {
            if (e.IsNone) return "the level";
            string name = NameOf(e.Packed) ?? "someone";
            return name.EndsWith(')') ? string.Create(ic, $"{name[..^1]}, e{e.Index})") : string.Create(ic, $"{name} (e{e.Index})");
        }
        // Contact's own lines (ContactLines): each contact, a swing's rejected meetings, a protest, the catch let go.
        contactLines.Clear();
        ContactLines(contactLines);
        BowLines(contactLines);   // nock, draw, loose, spent, let down
        for (int i = 0; i < contactLines.Count; i++) options.Say(contactLines[i]);
        // A Celerity swing's extra blows that did not land, at their own moments (one that met the body has its
        // harm line below; one met at a guard says so here, an overpowered one's harm line follows). A stopped attacker's are one
        // line for the rest; none for a target down (its stop says it) or the earlier build's compressed blows.
        IReadOnlyList<Vitomir.Platform.Combat.FlurryStrike> strikes =
            world.TryService<EventQueue<Vitomir.Platform.Combat.FlurryStrike>>(out var strikeQ) ? strikeQ.ThisTick : [];
        string Whose(EntityId e) { string w = Who(e); int at = w.LastIndexOf(" (e", StringComparison.Ordinal); return at < 0 ? w + "'s" : w[..at] + "'s" + w[at..]; }
        for (int i = 0; i < strikes.Count; i++)
        {
            var fs = strikes[i];
            string blow = string.Create(ic, $"{world.Seconds:0.00} s: {Whose(fs.Attacker)} extra blow {fs.Index} of {fs.Count}");
            switch (fs.Check)
            {
                case Vitomir.Platform.Combat.FlurryCheck.Guarded:
                    string what = fs.Guard switch
                    {
                        Vitomir.Platform.Combat.GuardMeet.Shield => "raised shield",
                        Vitomir.Platform.Combat.GuardMeet.Blade => "blade held in guard",
                        Vitomir.Platform.Combat.GuardMeet.Parry => "blade in its parry window",
                        _ => fs.Met == Vitomir.Platform.Combat.ContactMet.Shield ? "shield in the way" : "blade",
                    };
                    options.Say($"{blow} met {Whose(fs.Target)} {what}");
                    break;
                case Vitomir.Platform.Combat.FlurryCheck.OutOfReach:
                    options.Say(string.Create(ic, $"{blow} missed: {Who(fs.Target)} was out of the blade's reach (moved {fs.Moved:0.00} m)"));
                    break;
                case Vitomir.Platform.Combat.FlurryCheck.Dodged:
                    options.Say($"{blow} missed: {Whose(fs.Target)} dodge carried him out of the blade's line");
                    break;
                case Vitomir.Platform.Combat.FlurryCheck.AttackerStopped:
                    int last = i;
                    while (last + 1 < strikes.Count && strikes[last + 1].ContactId == fs.ContactId && strikes[last + 1].Check == fs.Check) last++;
                    string which = last == i ? string.Create(ic, $"extra blow {fs.Index} of {fs.Count} ended")
                        : last == i + 1 ? string.Create(ic, $"extra blows {fs.Index} and {strikes[last].Index} of {fs.Count} ended")
                        : string.Create(ic, $"extra blows {fs.Index} to {strikes[last].Index} of {fs.Count} ended");
                    options.Say(string.Create(ic, $"{world.Seconds:0.00} s: {Whose(fs.Attacker)} {which}: the swing was cancelled or stopped"));
                    i = last;
                    break;
            }
        }
        var harms = world.Events<Vitomir.Platform.Combat.HarmResolved>().ThisTick;
        for (int i = 0; i < harms.Count; i++)
        {
            var h = harms[i];
            // The contest on contact when it ran, and the double count's warning when the tempo runs too.
            string contest = !h.Contested ? "" : string.Create(ic,
                $", contest {h.AttackDice} dice → {h.AttackSuccesses} against {h.DefenceDice} → {h.DefenceSuccesses}: {h.ContestChange:+0;-0;0} dice{(tempoRegistered ? " (Melee counted in the animation and after impact: a research comparator, not a rule)" : "")}");
            // Why a resolved contact dealt no wound.
            string none = h.Wounds != 0 ? "" : h.ResistedBy switch
            {
                Vitomir.Platform.Combat.ResistedBy.NoDamage => " (no damage)",
                Vitomir.Platform.Combat.ResistedBy.Soak => " (soaked)",
                Vitomir.Platform.Combat.ResistedBy.Halving => " (halved to nothing)",
                Vitomir.Platform.Combat.ResistedBy.Armor => " (stopped by armour)",   // the armour alone kept the wound off
                Vitomir.Platform.Combat.ResistedBy.LowWork => " (low work)",   // the contact did too little work to wound
                _ => "",
            };
            // A Celerity swing's flurry (its own blow says how many it carries; each extra is its own line),
            // and the Health band after the blow (labels only).
            string which = h.Flurry > 0 ? string.Create(ic, $"contact #{h.ContactId}, flurry {h.Flurry} of {h.FlurryBlows}") : string.Create(ic, $"contact #{h.ContactId}");
            // An extra blow lands on the part its line met at its own moment (named when its strike says).
            if (h.Flurry > 0 && h.Strike != 0)
                for (int k = 0; k < strikes.Count; k++)
                    if (strikes[k].Strike == h.Strike && strikes[k].Region != Vitomir.Platform.Combat.BodyRegion.None)
                    { which += $", on the {Vitomir.Platform.Combat.BodyRegions.Name(strikes[k].Region)}"; break; }
            string carries = h.Flurry == 0 && h.FlurryBlows > 0 ? string.Create(ic, $" (its swing carries {h.FlurryBlows} more)") : "";
            string band = h.Band switch
            {
                Vitomir.Platform.Combat.HealthBand.Injured => " (Injured)",
                Vitomir.Platform.Combat.HealthBand.Crippled => " (Crippled)",
                _ => "",
            };
            // A later contact of a swing's chain (after a part was cut through) is resolved from what is left.
            string chain = "";
            var madeQ = world.Events<Vitomir.Platform.Combat.ContactMade>().ThisTick;
            for (int m = 0; m < madeQ.Count; m++)
                if (madeQ[m].ContactId == h.ContactId && madeQ[m].Chain > 0) { chain = string.Create(ic, $" (the chain's {Ordinal(madeQ[m].Chain + 1)} contact, from what the cut left of the swing)"); break; }
            // The armour over the part struck, its dice (in the soak's) and the wounds it kept off.
            string armour = h.Armor is null ? "" : string.Create(ic, $" (armour: {h.Armor}, {h.ArmorDice} dice, kept {h.ArmorKept})");
            // The blow's angle when it took from the cut (severance judged it: a band is measured).
            string angle = h.Angle switch
            {
                Vitomir.Platform.Combat.AngleBand.Glancing => " (glancing: a skim, it cannot cut)",
                Vitomir.Platform.Combat.AngleBand.Acute => string.Create(ic, $" (acute: {h.CutShare * 100f:0} % of it cuts)"),
                Vitomir.Platform.Combat.AngleBand.Normal when h.CutShare < 0.995f => string.Create(ic, $" ({h.CutShare * 100f:0} % of it cuts)"),
                _ => "",
            };
            // Blow quality when it changed the potential, and a blade's contact at its hilt (bashing).
            string quality = MathF.Abs(h.QualityFactor - 1f) < 0.0005f ? "" : string.Create(ic, $" (quality {h.Quality:0.00}: {h.PotentialBefore:0.##} dice → {h.PotentialAfter:0.#})");
            if (h.Hilt) quality += " (at the hilt: bashing)";
            options.Say(string.Create(ic, $"{world.Seconds:0.00} s: harm ({which}): {Who(h.Attacker)} on {Who(h.Target)}: {h.Kind.ToString().ToLowerInvariant()}, {h.DamageDice} dice → {h.DamageSuccesses} + {h.AutoSuccesses} auto, soak {h.SoakDice} dice → {h.SoakSuccesses}{armour}{quality}: {h.Wounds} wounds{none}, Health {h.HealthBefore} → {h.HealthAfter}{band}{angle}{carries}{chain}{contest}"));
        }
        // A part cut off — the blow's harm against the part's threshold, whether the blade goes on, and the
        // effective maximum Health the missing anatomy leaves (the purchased maximum unchanged).
        if (world.TryService<EventQueue<Vitomir.Platform.Combat.PartSevered>>(out var severedQ))
        {
            var cut = severedQ.ThisTick;
            for (int i = 0; i < cut.Count; i++)
            {
                var c = cut[i];
                string on = "the blade stops there", cutting = "";
                for (int k = 0; k < harms.Count; k++)
                    if (harms[k].ContactId == c.ContactId && harms[k].Flurry == 0)
                    {
                        if (harms[k].Continues) on = string.Create(ic, $"the blade goes on with {harms[k].BudgetLeft * 100f:0} % of its swing");
                        // The share of the harm that counted toward the cut, when not all of it did.
                        if (harms[k].CutShare < 0.995f)
                            cutting = string.Create(ic, $", {c.Harm * harms[k].CutShare:0} of it cutting{(harms[k].Angle == Vitomir.Platform.Combat.AngleBand.Acute ? ", acute" : "")}");
                        break;
                    }
                string part = Vitomir.Platform.Combat.BodyRegions.Name(c.Region);
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: sever: {Who(c.By)} severs {Who(c.Who)}'s {part} ({c.Harm} Health after soak{cutting}; its threshold {c.Threshold}; {on})"));
                if (c.StructuralLoss > 0)
                    options.Say(string.Create(ic, $"{world.Seconds:0.00} s: health: {Who(c.Who)}'s effective maximum Health {c.EffectiveMaximum} of {c.PurchasedMaximum} ({part} missing; healing stops there, ENGINE_SANDBOX)"));
            }
        }
        // A person's Health band changing (labels only; the stop has its own line).
        if (world.TryService<EventQueue<Vitomir.Platform.Combat.HealthBandChanged>>(out var bandQ))
        {
            var bands = bandQ.ThisTick;
            for (int i = 0; i < bands.Count; i++)
            {
                var b = bands[i];
                if (b.To == Vitomir.Platform.Combat.HealthBand.Stopped) continue;
                string line = b.To == Vitomir.Platform.Combat.HealthBand.None
                    ? string.Create(ic, $"{world.Seconds:0.00} s: health: {Who(b.Who)} is no longer {b.From}: Health {b.Health} of {b.HealthMax}")
                    : string.Create(ic, $"{world.Seconds:0.00} s: health: {Who(b.Who)} is {b.To}: Health {b.Health} of {b.HealthMax} (below {(b.To == Vitomir.Platform.Combat.HealthBand.Injured ? 25 : 10)} %; a label, no penalty)");
                options.Say(line);
            }
        }
        var deaths = world.Events<Vitomir.Platform.Combat.ActorDied>().ThisTick;
        for (int i = 0; i < deaths.Count; i++)
        {
            var d = deaths[i];
            // Health 0 is not "death": a harness stop, said so. a severed neck or trunk the same.
            string what = d.Stop == Vitomir.Platform.Combat.ActorStop.SeveredStop ? "a part whose loss stops the body severed (the neck or the trunk)" : "Health at nothing";
            options.Say(string.Create(ic, $"{world.Seconds:0.00} s: stop: {Who(d.Who)} — {what}: a harness stop (ENGINE_SANDBOX), not a ruled death (contact #{d.ContactId}, by {Who(d.Killer)}, {d.Kind.ToString().ToLowerInvariant()}; {d.Stop})"));
        }
        // Where each blade stopped, sprang back or went on (after the harm: the follow-through follows its verdict).
        followLines.Clear();
        FollowThroughLines(followLines);
        for (int i = 0; i < followLines.Count; i++) options.Say(followLines[i]);
        // Swings cancelled, parries begun, the rules' verdicts on guards met, guards broken.
        bool hasCancels = world.TryService<EventQueue<SwingCancelled>>(out var cancelQ);
        if (hasCancels)
            for (int i = 0; i < cancelQ!.ThisTick.Count; i++)
            {
                var x = cancelQ.ThisTick[i];
                if (x.Swing == 0) continue;   // a press held or buffered let go: nothing to tell
                options.Say(x.Cause == SwingCancelCause.Cancel
                    ? string.Create(ic, $"{world.Seconds:0.00} s: cancel: {Who(x.Entity)} feints (blow #{x.Swing} cancelled in its wind-up)")
                    : string.Create(ic, $"{world.Seconds:0.00} s: cancel: {Who(x.Entity)}'s blow #{x.Swing} cancelled by a dodge{(x.BeforeStrike ? " in its wind-up" : "")}"));
            }
        if (world.TryService<EventQueue<ParryBegun>>(out var parryQ))
            for (int i = 0; i < parryQ.ThisTick.Count; i++)
            {
                var p = parryQ.ThisTick[i];
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: guard: {Who(p.Entity)} parries{(p.Threat == EntityId.None ? "" : $" toward {Who(p.Threat)}'s blow")}"));
            }
        if (world.TryService<EventQueue<GuardResolved>>(out var guardQ))
            for (int i = 0; i < guardQ.ThisTick.Count; i++)
            {
                var g = guardQ.ThisTick[i];
                string met = g.Meet switch
                {
                    GuardMeet.Shield => "shield",
                    GuardMeet.Blade => "blade",
                    GuardMeet.Parry => "blade in its parry window",
                    GuardMeet.Clash => "blade, both swinging",
                    GuardMeet.Incidental => "shield (in the way, not raised)",
                    _ => "guard",
                };
                string what = g.Verdict switch
                {
                    GuardVerdict.Held => "held",
                    GuardVerdict.Parried => string.Create(ic, $"parried ({g.ParryChance * 100f:0} %); {Who(g.Attacker)}'s recovery {g.RiposteSeconds:0.0} s longer"),
                    GuardVerdict.Overpowered => string.Create(ic, $"overpowered ({g.OverpowerChance * 100f:0} %), the blade goes on with {g.BudgetLeft:0.00} of its budget"),
                    GuardVerdict.Clashed => "clashed: it stops",
                    GuardVerdict.ClashDominates => string.Create(ic, $"clashed and dominated: the blade goes on with {g.BudgetLeft:0.00} of its budget"),
                    GuardVerdict.ClashDeflected => "clashed: both turned aside",
                    _ => g.Verdict.ToString(),
                };
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: guard: {Who(g.Attacker)}'s blow (contact #{g.ContactId}) met {Who(g.Defender)}'s {met}: {what}"));
            }
        if (world.TryService<EventQueue<GuardBroken>>(out var brokenQ))
            for (int i = 0; i < brokenQ.ThisTick.Count; i++)
            {
                var b = brokenQ.ThisTick[i];
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: guard: {Who(b.Who)}'s guard is broken for {b.Seconds:0.0} s"));
            }
        // The guard's arm solver following a seen blow (once a blow), and a struck guard giving.
        if (world.TryService<EventQueue<Vitomir.Platform.Animation.GuardSolverFollows>>(out var followsQ))
            for (int i = 0; i < followsQ.ThisTick.Count; i++)
            {
                var f = followsQ.ThisTick[i];
                string hand = f.Margin < 0f ? string.Create(ic, $"the hand {-f.Margin:0.00} s late") : string.Create(ic, $"the hand there {f.Margin:0.00} s before it");
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: guard: {Who(f.Who)}'s guard follows {Who(f.Threat)}'s blow #{f.Swing} — to meet it {f.Lead:0.00} s ahead, {hand}"));
            }
        if (world.TryService<EventQueue<Vitomir.Platform.Animation.GuardGave>>(out var gaveQ))
            for (int i = 0; i < gaveQ.ThisTick.Count; i++)
            {
                var g = gaveQ.ThisTick[i];
                string part = g.Shield ? "shield" : "blade";
                options.Say(g.Verdict == GuardVerdict.Overpowered
                    ? string.Create(ic, $"{world.Seconds:0.00} s: guard: {Who(g.Who)}'s {part} is knocked aside {g.Metres * 100f:0.0} cm by {Who(g.By)}'s blow (overpowered)")
                    : string.Create(ic, $"{world.Seconds:0.00} s: guard: {Who(g.Who)}'s {part} gives {g.Metres * 100f:0.0} cm under {Who(g.By)}'s blow ({g.Verdict.ToString().ToLowerInvariant()})"));
            }
        // Dodges begun, refused and ended.
        if (world.TryService<EventQueue<Bodies.DodgeBegun>>(out var dodgeQ))
            for (int i = 0; i < dodgeQ.ThisTick.Count; i++)
            {
                var d = dodgeQ.ThisTick[i];
                string way = "back";
                if (world.Store<Body>().Has(d.Entity))
                {
                    float yaw = world.Get<Body>(d.Entity).Yaw;
                    float fwd = d.Way.X * MathF.Cos(yaw) + d.Way.Y * MathF.Sin(yaw), left = -d.Way.X * MathF.Sin(yaw) + d.Way.Y * MathF.Cos(yaw);
                    way = MathF.Abs(fwd) >= MathF.Abs(left) ? (fwd >= 0 ? "forward" : "back") : (left > 0 ? "left" : "right");
                }
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: dodge: {Who(d.Entity)} dodges {way} from {d.Feet.X:0.00} {d.Feet.Y:0.00} {d.Feet.Z:0.00}: start {d.Ready:0.00} s, {d.Distance:0.0} m in {d.Move:0.00} s ({d.Speed:0.0} m/s), recovery {d.Recover:0.00} s{(d.Held ? ", held to a human's (the masquerade)" : "")}{(hasCancels && CancelledByDodge(cancelQ!, d.Entity) ? " (cancelling its swing)" : "")}"));
            }
        if (world.TryService<EventQueue<Bodies.DodgeRefused>>(out var dodgeNo))
            for (int i = 0; i < dodgeNo.ThisTick.Count; i++)
            {
                var d = dodgeNo.ThisTick[i];
                string why = d.Why switch
                {
                    Bodies.DodgeRefusal.MidSwing => "a blow is playing",
                    Bodies.DodgeRefusal.Dodging => "already dodging",
                    Bodies.DodgeRefusal.Recovering => "recovering from a dodge",
                    Bodies.DodgeRefusal.InAir => "in the air",
                    Bodies.DodgeRefusal.Ladder => "on a ladder",
                    Bodies.DodgeRefusal.Moving => "in a move",
                    Bodies.DodgeRefusal.Stopped => "stopped",
                    _ => d.Why.ToString(),
                };
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: dodge refused: {Who(d.Entity)} — {why}"));
            }
        if (world.TryService<EventQueue<Bodies.DodgeEnded>>(out var dodgeEnd))
            for (int i = 0; i < dodgeEnd.ThisTick.Count; i++)
            {
                var d = dodgeEnd.ThisTick[i];
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: dodge ended: {Who(d.Entity)} at {d.Feet.X:0.00} {d.Feet.Y:0.00} {d.Feet.Z:0.00}, carried {d.Moved:0.00} of {d.Distance:0.00} m{(d.Cut ? ", cut short by a stop" : "")}"));
            }
        // Fights in the world begun and ended, and the player getting up (the sandbox recovery).
        if (world.TryService<EventQueue<Vitomir.Platform.Combat.FightBegun>>(out var begunQ))
            for (int i = 0; i < begunQ.ThisTick.Count; i++)
                if (begunQ.ThisTick[i] is var f) options.Say(string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(f.Who)} fights {Who(f.Foe)} — {(f.Cause == Vitomir.Platform.Combat.FightCause.Struck ? "struck" : "joins (saw a blow struck, or was told)")}"));
        if (world.TryService<EventQueue<Vitomir.Platform.Combat.FightEnded>>(out var endedQ))
            for (int i = 0; i < endedQ.ThisTick.Count; i++)
            {
                var f = endedQ.ThisTick[i];
                string why = f.Why switch
                {
                    Vitomir.Platform.Combat.FightEnd.FoeDown => $"stands down, {Who(f.Foe)} is down",
                    Vitomir.Platform.Combat.FightEnd.GaveUp => $"gives up on {Who(f.Foe)}, too far too long",
                    Vitomir.Platform.Combat.FightEnd.Crippled => $"flees {Who(f.Foe)}, Crippled",
                    Vitomir.Platform.Combat.FightEnd.Disarmed => $"flees {Who(f.Foe)}, its blade gone",
                    _ => $"stops fighting {Who(f.Foe)}",
                };
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: fight: {Who(f.Who)} {why} (fought {f.Seconds:0.0} s)"));
            }
        if (world.TryService<EventQueue<Vitomir.Platform.Combat.ActorRecovered>>(out var upQ))
            for (int i = 0; i < upQ.ThisTick.Count; i++)
                if (upQ.ThisTick[i] is var u) options.Say(string.Create(ic, $"{world.Seconds:0.00} s: up: {Who(u.Who)} gets up after the stop at {u.StoppedTick / (double)World.TicksPerSecond:0.00} s, Health {u.Health} of {u.HealthMax} — a sandbox convenience (ENGINE_SANDBOX), not a rule"));
    }

    /// <summary>Whether <paramref name="e"/>'s swing was cancelled by a dodge this tick.</summary>
    static bool CancelledByDodge(EventQueue<SwingCancelled> q, EntityId e)
    {
        for (int i = 0; i < q.ThisTick.Count; i++)
            if (q.ThisTick[i].Entity == e && q.ThisTick[i].Cause == SwingCancelCause.Dodge && q.ThisTick[i].Swing != 0) return true;
        return false;
    }

    static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };

    readonly List<string> followLines = [];   // the follow-through's lines this tick

    bool? tempoKnown;   // whether the world plays the Abilities' tempo (for the harm line's double-count warning)

    /// <summary>The log's lines for the powers: each use (Blood paid, before and after, how long), refusal (why) and end.</summary>
    void TellPowers()
    {
        if (!world.TryService<EventQueue<Vitomir.Platform.Powers.PowerUsed>>(out var usedQ)) return;
        var ic = CultureInfo.InvariantCulture;
        world.TryService<Vitomir.Platform.Powers.IPowerCatalogue>(out var catalogue);
        string Label(string? id)
        {
            if (id is null) return "the slot";
            if (catalogue is not null)
                for (int k = 0; k < catalogue.Count; k++) if (catalogue.Id(k) == id) return catalogue.Name(k);
            return id;
        }
        string Who(EntityId e) => NameOf(e.Packed) is { } n ? string.Create(ic, $"{n} (e{e.Index})") : string.Create(ic, $"e{e.Index}");
        var used = usedQ.ThisTick;
        for (int i = 0; i < used.Count; i++)
        {
            var u = used[i];
            // A rise says its amount (raw points), a heal the Health it gave.
            string amount = u.Amount == 0 ? "" : u.Seconds > 0 ? string.Create(ic, $", +{u.Amount}") : string.Create(ic, $", Health +{u.Amount}");
            options.Say(string.Create(ic, $"{world.Seconds:0.00} s: power: {Who(u.Entity)}: {Label(u.Power)} {u.Rank} {(u.Renewed ? "renewed" : "used")}: {u.Cost} Blood ({u.BloodBefore} → {u.BloodAfter}){(u.Seconds > 0 ? string.Create(ic, $", {u.Seconds:0.#} s") : "")}{amount}"));
        }
        if (world.TryService<EventQueue<Vitomir.Platform.Powers.PowerRefused>>(out var refusedQ))
        {
            var refused = refusedQ.ThisTick;
            for (int i = 0; i < refused.Count; i++)
            {
                var r = refused[i];
                string why = r.Reason switch
                {
                    Vitomir.Platform.Powers.PowerDenial.EmptySlot => "the slot holds nothing",
                    Vitomir.Platform.Powers.PowerDenial.Unlearned => "not learned",
                    Vitomir.Platform.Powers.PowerDenial.NoSemantics => "no rules at this rank yet (ranks 6 to 10 are not defined)",
                    Vitomir.Platform.Powers.PowerDenial.NoBlood => BloodShort(r),
                    Vitomir.Platform.Powers.PowerDenial.Recast => "its recast is running",
                    Vitomir.Platform.Powers.PowerDenial.Dead => "at nothing (a zero-Health stop)",
                    Vitomir.Platform.Powers.PowerDenial.NoSheet => "no sheet",
                    Vitomir.Platform.Powers.PowerDenial.Passive => "passive: always on, nothing to use",
                    Vitomir.Platform.Powers.PowerDenial.NoEffect => HealthFull(r),
                    _ => r.Reason.ToString(),
                };
                string what = r.Power is null ? string.Create(ic, $"slot {r.Slot}") : Label(r.Power);
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: power: {Who(r.Entity)}: {what} refused: {why}"));
            }
        }
        if (world.TryService<EventQueue<Vitomir.Platform.Powers.PowerEnded>>(out var endedQ))
        {
            var ended = endedQ.ThisTick;
            for (int i = 0; i < ended.Count; i++)
            {
                var x = ended[i];
                if (x.Cause == Vitomir.Platform.Powers.PowerEndCause.Renewed) continue;   // the use line says "renewed"
                options.Say(string.Create(ic, $"{world.Seconds:0.00} s: power: {Who(x.Entity)}: {Label(x.Power)} {x.Rank} ended: {x.Cause.ToString().ToLowerInvariant()}"));
            }
        }

        string HealthFull(in Vitomir.Platform.Powers.PowerRefused r)
        {
            if (!world.TryStore<Vitomir.Platform.Powers.Vitals>(out var vitals) || !vitals.Has(r.Entity)) return "it would do nothing";
            var v = vitals.Get(r.Entity);
            return string.Create(CultureInfo.InvariantCulture, $"Health is full ({v.Health:0} of {v.HealthMax:0})");
        }

        string BloodShort(in Vitomir.Platform.Powers.PowerRefused r)
        {
            if (!world.TryStore<Vitomir.Platform.Powers.Vitals>(out var vitals) || !vitals.Has(r.Entity)) return "not enough Blood";
            var v = vitals.Get(r.Entity);
            int cost = 0;
            if (r.Slot > 0 && world.TryStore<Vitomir.Platform.Powers.PowerSlots>(out var slots) && slots.Has(r.Entity))
                cost = slots.Get(r.Entity).Slots[r.Slot - 1].Cost;
            return cost > 0 ? string.Create(CultureInfo.InvariantCulture, $"not enough Blood ({v.Blood:0} of {cost})") : string.Create(CultureInfo.InvariantCulture, $"not enough Blood ({v.Blood:0})");
        }
    }

    /// <summary>The log's lines for who sees (the sight work's events): who, what changed, why, where.</summary>
    void TellPerception()
    {
        var ic = CultureInfo.InvariantCulture;
        var changed = world.Events<AwarenessChanged>().ThisTick;
        for (int i = 0; i < changed.Count; i++)
        {
            var c = changed[i];
            string why = c.Cause switch
            {
                AwarenessCause.Seen => $"saw {ActWord(c.Act, c.Move)} at {c.Where.X:0.00} {c.Where.Y:0.00} {c.Where.Z:0.00}",
                AwarenessCause.Heard => $"heard a shout about {c.Where.X:0.00} {c.Where.Y:0.00}",
                AwarenessCause.Struck => $"struck (a blow from {c.Where.X:0.00} {c.Where.Y:0.00})",
                _ => "nothing more seen",
            };
            options.Say(string.Create(ic, $"{world.Seconds:0.00} s: seen: {Who(c.Observer)}: {Rung(c.From)} → {Rung(c.To)}, {why} ({c.Value:0.00})"));
        }
        var shouts = world.Events<ShoutRaised>().ThisTick;
        for (int i = 0; i < shouts.Count; i++)
        {
            var h = shouts[i];
            options.Say(string.Create(ic, $"{world.Seconds:0.00} s: shout: {Who(h.Who)} shouts at {h.Where.X:0.00} {h.Where.Y:0.00} about {h.About.X:0.00} {h.About.Y:0.00}, heard by {h.Heard} within {h.Radius:0.#} m"));
        }
        var reactions = world.Events<ReactionStarted>().ThisTick;
        for (int i = 0; i < reactions.Count; i++)
        {
            var r = reactions[i];
            string doing = r.Doing switch
            {
                Reaction.Routine => "goes back to its routine",
                Reaction.Look => "stops and looks",
                Reaction.BackOff => "backs off",
                Reaction.EdgeCloser => "edges closer",
                Reaction.Watch => "watches",
                Reaction.GoLook => "goes to look",
                Reaction.Search => "searches about",
                Reaction.Flee => "flees",
                Reaction.RunTo => "runs to where it last saw it",
                Reaction.Challenge => "challenges the player",
                Reaction.Fight => "fights",
                _ => r.Doing.ToString().ToLowerInvariant(),
            };
            string at = r.Doing == Reaction.Routine ? "" : string.Create(ic, $" ({r.Place.X:0.00} {r.Place.Y:0.00})");
            options.Say(string.Create(ic, $"{world.Seconds:0.00} s: react: {Who(r.Who)} {doing}{at}"));
        }
        var state = world.Service<PerceptionState>();
        var rose = world.Events<AreaHeatRose>().ThisTick;
        for (int i = 0; i < rose.Count; i++)
            options.Say(string.Create(ic, $"{world.Seconds:0.00} s: heat: {state.Areas.Name(rose[i].Area)} rises to {rose[i].Heat:0.00} (an alarm at {rose[i].Where.X:0.00} {rose[i].Where.Y:0.00})"));
        var gone = world.Events<AreaHeatGone>().ThisTick;
        for (int i = 0; i < gone.Count; i++)
            options.Say(string.Create(ic, $"{world.Seconds:0.00} s: heat: {state.Areas.Name(gone[i].Area)} gone cold after {gone[i].Seconds:0} s"));

        string Who(EntityId e)
        {
            string name = NameOf(e.Packed) ?? "someone";
            return name.EndsWith(')') ? string.Create(ic, $"{name[..^1]}, e{e.Index})") : string.Create(ic, $"{name} (e{e.Index})");
        }
        static string Rung(Awareness a) => a.ToString().ToLowerInvariant();
        static string ActWord(MasqueradeAct act, TraversalKind move) => act switch
        {
            MasqueradeAct.Jump => "a leap",
            MasqueradeAct.Speed => "a burst of speed",
            MasqueradeAct.Fall => "a fall",
            MasqueradeAct.Move when move != TraversalKind.None => "a " + Word(move),
            _ => "something",
        };
    }

    /// <summary>A squeeze's or a cover's move in words.</summary>
    static string SideWhat(SideAct act, int way) => act switch
    {
        SideAct.SqueezeIn => way == 0 ? "squeeze: into the gap" : $"squeeze: into the gap, a quarter turn to the {(way > 0 ? "left" : "right")}",
        SideAct.SqueezeStep => $"squeeze: a side-step to the {(way > 0 ? "left" : way < 0 ? "right" : "side")}",
        SideAct.SqueezeOut => $"squeeze: out, a quarter turn to the {(way > 0 ? "left" : "right")}",
        SideAct.CoverIn => "cover: the back to the wall",
        SideAct.CoverStep => $"cover: a side-step to the {(way > 0 ? "left" : way < 0 ? "right" : "side")}",
        SideAct.CoverOut => "cover: out, standing",
        SideAct.CoverLetGo => "cover: the way in let go of before the back met the wall",
        _ => "side-step",
    };

    /// <summary>Why a squeeze or a cover was not taken, in words.</summary>
    static string SideWhy(SideRefusal why) => why switch
    {
        SideRefusal.Narrow => "the gap is narrower than the squeeze's clips allow",
        SideRefusal.Bend => "the gap bends (or is closed before its far end): a squeeze goes straight through",
        SideRefusal.Low => "too little room overhead for the body standing",
        SideRefusal.Crouched => "crouched: stand up to squeeze through",
        SideRefusal.Long => "no way out of the gap within 8 m",
        SideRefusal.WallEnds => "the wall ends there (no going round it)",
        SideRefusal.Blocked => "its way is blocked",
        SideRefusal.NoRoom => "no room for the body there",
        _ => "not taken",
    };

    /// <summary>A wall's move in words.</summary>
    static string WallWhat(WallAct act, int left, int up, byte pace, bool outside) => act switch
    {
        WallAct.OnGround => "on from the ground", WallAct.OnJump => "on with a jump", WallAct.Catch => "caught in the air",
        WallAct.Move => $"{(pace == WallEvents.Long ? "a long move" : pace == WallEvents.Careful ? "a careful move" : pace == WallEvents.Quick ? "a quick move" : "a move")} {(up != 0 && left != 0 ? (up > 0 ? "up and " : "down and ") + (left > 0 ? "left" : "right") : up > 0 ? "up" : up < 0 ? "down" : left > 0 ? "left" : "right")}",   // a diagonal
        WallAct.Corner => $"round the {(outside ? "outside" : "inside")} corner to the {(left > 0 ? "left" : "right")}",
        WallAct.ToHang => "over the top into the braced hang", WallAct.Off => "off at the foot", WallAct.LetGo => "let go", WallAct.PushOff => "a push off",
        _ => "move",
    };

    /// <summary>Why a wall's move was not taken, in words.</summary>
    static string WallWhy(WallRefusal why) => why switch
    {
        WallRefusal.Sheer => "a face with nothing to hold, only out of the masquerade or in a free place",
        WallRefusal.NoClimb => "the level says not to be climbed there",
        WallRefusal.NoHolds => "the wall (or its holds) does not go on under every hand and foot there",
        WallRefusal.NoRoom => "no room for the body there",
        WallRefusal.Blocked => "its way is blocked",
        WallRefusal.Top => "the top of the face, with no edge the clip over the top can take",
        WallRefusal.TooNear => "the corner is too near for the clip",
        WallRefusal.Angle => "the faces are not square to each other within the tolerance",
        WallRefusal.ShortFace => "the face round it is too short",
        WallRefusal.NoClip => "the models' files have no clip for it",
        _ => "not taken",
    };

    /// <summary>The corners not taken that have been said (about where, a fifth of a metre, and why).</summary>
    readonly HashSet<(float, float, float, HangRefusal)> saidHangCorner = [];

    /// <summary>A leap or a corner in words.</summary>
    static string HangWhat(HangMoveKind kind, HangLeap leap, bool outside, int way, bool? braced) => kind == HangMoveKind.Leap
        ? leap switch { HangLeap.Left => "leap left", HangLeap.Right => "leap right", HangLeap.Up => "leap up", HangLeap.Down => "leap down", HangLeap.Back => "leap back", _ => "leap" }
        : $"the {(outside ? "outside" : "inside")} corner to the {(way > 0 ? "left" : "right")}{(braced is { } b ? b ? " (braced)" : " (free)" : "")}";

    /// <summary>Why a leap or a corner was not taken, in words.</summary>
    static string HangWhy(HangRefusal why) => why switch
    {
        HangRefusal.NoLip => "no lip to leap to within the tolerance of the clip's own",
        HangRefusal.ShortLip => "the lip round it is too short to hang on (the end's hang and half the hands beyond)",
        HangRefusal.NoWall => "no wall under the edge there for the feet",
        HangRefusal.Wall => "a wall under the edge there (the free corner's legs swing under)",
        HangRefusal.NoRoom => "no room to hang there",
        HangRefusal.Blocked => "its way is blocked",
        HangRefusal.TooNear => "the corner is too near the hang's middle for the clip",
        HangRefusal.Angle => "the faces are not square to each other within the tolerance",
        HangRefusal.FreeHang => "the leaps start braced (the feet push off the wall), none from a free hang",
        HangRefusal.NoClip => "the models' files have no clip for it",
        HangRefusal.Continuous => "the lip goes on to it, no break between (the shimmy's)",
        _ => "not taken",
    };

    /// <summary>The ladders whose stand-in has been said, and the pace moves (the earlier build's stand-ins) said for the low vaults.</summary>
    readonly HashSet<int> saidLadderStandIn = [];
    readonly HashSet<TraversalKind> saidPaceStandIn = [];

    static string Word(TraversalKind kind) => kind switch
    {
        TraversalKind.LadderOff => "off the ladder",
        TraversalKind.WallOn => "onto the wall", TraversalKind.Wall => "on the wall", TraversalKind.WallOff => "off the wall",
        TraversalKind.Squeeze => "squeeze", TraversalKind.Cover => "cover",
        TraversalKind.HopUp => "hop up", TraversalKind.ClimbOnto => "climb onto", TraversalKind.PullUp => "pull up",
        TraversalKind.Hurdle => "sprint vault", TraversalKind.StepOver => "step-over", TraversalKind.LowerToHang => "lower to hang",
        _ => kind.ToString().ToLowerInvariant(),
    };

    void Notice(string line)
    {
        options.Say(string.Create(CultureInfo.InvariantCulture, $"{world.Seconds:0.00} s: {line}"));
        notice = line;
        noticeUntil = world.Tick + NoticeTicks;
    }

    public ulong Digest() => world.Digest();

    /// <summary>
    /// The player's third-person facing and always run changed on the Controls page, from the next tick on. Not
    /// while a recording is made or played: a tape's head carries the values it started with.
    /// </summary>
    public void SetControlOptions(ThirdPersonFacing facing, bool alwaysRun = true, float doubleTapDodge = 0f)
    {
        options = options with { Facing = facing, AlwaysRun = alwaysRun, DoubleTapDodge = doubleTapDodge };
        world.Service<TickInput>().ThirdPersonFacing = facing;
        world.Service<TickInput>().AlwaysRun = alwaysRun;
        world.Service<TickInput>().DoubleTapDodge = doubleTapDodge;
    }

    /// <summary>
    /// The player's way of choosing a blow's direction now (<see cref="Control.AttackDirectionInput"/>): the run's
    /// start's (<see cref="SessionOptions.AttackDirection"/>), or the last change a frame carried (<see cref="ActionFrame.AttackMode"/>).
    /// </summary>
    public Control.AttackDirectionMode AttackDirectionNow =>
        world.TryService<Control.AttackDirectionInput>(out var input) ? input.Mode : Control.AttackDirectionMode.Automatic;

    /// <summary>
    /// (He set <i>Keys</i> on the Controls page mid-run and kept getting blows from the right — the
    /// mode was taken once at the start): the mode changed from this tick, by a frame (<see cref="ActionFrame.AttackMode"/>), so a
    /// recording carries it at its tick and a replay changes it there. The next press chooses by it (a blow held ready now is switched
    /// by it too). Logged: <i>attack direction: keys (from the next blow)</i>.
    /// </summary>
    void ChangeAttackDirection(Control.AttackDirectionMode mode)
    {
        if (!world.TryService<Control.AttackDirectionInput>(out var input) || input.Mode == mode) return;
        input.Mode = mode;
        options.Say(string.Create(CultureInfo.InvariantCulture, $"{world.Seconds:0.00} s: attack direction: {input.Note} (from the next blow)"));
    }

    /// <summary>Whether the player has freed the pointer (the program gives the mouse back to the system then).</summary>
    public bool PointerFree => world.Alive(player) && world.Has<CameraRig>(player) && world.Get<CameraRig>(player).PointerFree;

    /// <summary>
    /// The camera to watch from (<see cref="OrbitCamera"/>): swung round the player with its key held, kept there let go,
    /// zoomed by the wheel, back behind with a double tap. Presentation only: the picture's view (<see cref="Fill"/>), never the simulation's.
    /// </summary>
    public OrbitCamera Orbit { get; } = new();

    /// <summary>
    /// Whether what stopped the player is a thing the run's own vault takes (<see cref="Animation.People.PersonOverSettings.Auto"/>): thin, in the
    /// player's vault clips' bands, the player given the clips — then a run or a sprint vaults it by itself, and the stop was at a walk.
    /// </summary>
    bool RunVaults(in PlayerStopped p)
    {
        if (!options.PersonOver.Auto || p.Cause != StopCause.TooHigh || !world.TryStore<Animation.People.PersonOver>(out var overs) || !overs.Has(player)
            || !world.Has<CharacterAnimation>(player) || !world.Has<Locomotion>(player)) return false;
        var paths = world.Service<CharacterAssets>().Kind(world.Get<CharacterAnimation>(player).Kind).Person?.Over?.Paths;
        return paths is not null && p.Depth < world.Get<Locomotion>(player).MantleMinDepth - 1e-4f && paths.For(TraversalKind.Vault, p.Height) is not null;
    }

    /// <summary>Whether the view is third person (the orbit camera's only).</summary>
    public bool ThirdPerson => world.Service<ViewState>() is { HasView: true, Kind: ViewKind.Third };

    /// <summary>The simulation's view's pitch now (radians), the orbit camera's tilt measured from it.</summary>
    public float ViewPitch => viewNow.Pitch;

    /// <summary>
    /// The picture's view from the orbit camera when it is not the default (third person, the player's body there): the head where the
    /// simulation's camera pivots from, between the ticks as the body is drawn, the driving camera's look and shoulder, the orbit's swing, tilt and zoom.
    /// </summary>
    ViewPoint? OrbitView(ViewPoint driving, float alpha)
    {
        if (Orbit.IsDefault || !world.Alive(player) || !world.Has<Body>(player) || !world.Has<CameraRig>(player) || !world.Has<Locomotion>(player)) return null;
        var settings = world.Service<CameraSettings>();
        ref var body = ref world.Get<Body>(player);
        var feet = before.TryGetValue(player.Packed, out var was) ? Vector3.Lerp(was.Feet, body.Feet, alpha) : body.Feet;
        var loco = world.Get<Locomotion>(player);
        var ease = world.Has<EyeEase>(player) ? world.Get<EyeEase>(player) : default;
        var head = feet + new Vector3(0f, 0f, loco.EyeHeight + ease.Lift - ease.Duck);
        var (sy, cy) = DetMath.SinCos(driving.Yaw);
        var offset = new Vector3(sy, -cy, 0f) * (settings.ShoulderSide * (world.Get<CameraRig>(player).Shoulder >= 0 ? 1f : -1f)) + new Vector3(0f, 0f, settings.ShoulderUp);
        Orbit.CameraBoomDefault = settings.Boom;
        return Orbit.View(world.Service<LevelSpace>().Collision, settings, head, driving.Yaw, driving.Pitch, offset, settings.Boom, driving.FieldOfViewDegrees);
    }

    /// <summary>A character part's texture by name, for the renderer.</summary>
    public TextureImage? Texture(string name) => textures.TryGetValue(name, out var t) ? t : null;

    /// <summary>
    /// The picture at <paramref name="alpha"/> of the way from the tick before the last to the last: the view and
    /// every character's place interpolated, poses as the last tick left them, the reticle, the sandbox's banner.
    /// </summary>
    /// <summary>
    /// The renderer's look for this session's time: at night the modern path's <c>dusk</c> (the moon a weak cool
    /// sun, a dark sky), by day the default; who sees counts the night's light, and the picture shows it. Presentation only.
    /// </summary>
    public static string LookFor(bool night) => night ? "dusk" : "";

    /// <summary>Whether this session is a night (<see cref="SessionOptions.Night"/>).</summary>
    public bool Night => options.Night;

    /// <summary>
    /// How far a person drawn on the stair line (<see cref="Animation.People.PersonGround.Line"/>) stands above its capsule's feet this
    /// tick (m), 0 for anyone else: the picture between two ticks interpolates the capsule's feet plus this (<see cref="Fill"/>).
    /// </summary>
    static float LineOf(Core.Store<Animation.People.PersonGround>? grounds, EntityId e)
    {
        if (grounds is null || !grounds.Has(e)) return 0f;
        ref var g = ref grounds.Get(e);
        return g.Stairs ? g.Line : 0f;
    }

    public void Fill(FrameView view, float alpha)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        var state = world.Service<ViewState>();
        bool firstPerson = state.Kind == ViewKind.First;
        // In first person the picture stands the viewer's share of the bob from the steady eye, with the viewer's
        // field of view and a near plane of a few centimetres; the look is the player's at any setting.
        var eye = Vector3.Lerp(viewBefore.Eye, viewNow.Eye, alpha);
        if (firstPerson) eye = FirstPersonView.Eye(eye, eye + Vector3.Lerp(bobBefore, bobNow, alpha), HeadMotion);
        view.View = new ViewPoint(eye,
                                  viewBefore.Yaw + DetMath.WrapAngle(viewNow.Yaw - viewBefore.Yaw) * alpha,
                                  viewBefore.Pitch + (viewNow.Pitch - viewBefore.Pitch) * alpha,
                                  (firstPerson ? DrawnFieldOfView : viewNow.FieldOfViewDegrees > 0 ? viewNow.FieldOfViewDegrees : 75f) * BowZoomFactor(),   // the bow's zoom
                                  firstPerson ? FirstPersonView.NearPlane : 0f);
        if (fighters.Count >= 2 && !world.Alive(player)) view.View = Spectator(alpha);     // an arena's bout has no player to look from
        // The camera to watch from, in third person when swung, tilted or zoomed (the simulation's view, aim and reticle's answer stay the driving camera's).
        bool orbiting = false;
        if (!firstPerson && OrbitView(view.View, alpha) is { } orbited) { view.View = orbited; orbiting = true; }
        var poses = world.Service<CharacterPoses>();
        var assetsInWorld = world.Service<CharacterAssets>();
        var bodies = world.Store<Body>();
        var anims = world.Store<CharacterAnimation>();
        world.TryStore<Animation.People.PersonGround>(out var grounds);
        view.Characters.Clear();
        view.HitVolumes.Clear();
        foreach (var e in characters)
        {
            if (!bodies.Has(e) || !anims.Has(e)) continue;
            ref var body = ref bodies.Get(e);
            bool had = before.TryGetValue(e.Packed, out var was);
            var (feet, yaw) = had
                ? (Vector3.Lerp(was.Feet, body.Feet, alpha), was.Yaw + DetMath.WrapAngle(body.Yaw - was.Yaw) * alpha)
                : (body.Feet, body.Yaw);
            // A person drawn on the stair line is drawn between the ticks on the line, not on the capsule: its pose stands on the last
            // tick's line, which took the capsule's step out at once, so with the feet alone interpolated the body sank (going up) or rose
            // (going down) by the rest of a riser for the frames between the tick before a step and the step (up to 0.2 m for a frame,
            // at any draw rate that is not the tick's; the "jittering and popping" seen on the stairs). The drawn line is interpolated instead.
            if (had && !options.StairsDrawLastTick) feet.Z += (1f - alpha) * (was.Line - LineOf(grounds, e));
            var kind = assetsInWorld.Kind(anims.Get(e).Kind);
            drawn.TryGetValue(e.Packed, out var reuse);
            var place = kind.Placement(feet, yaw);
            var d = poses.Draw(world, e, place, (firstPerson && e == player) || e == FilmFirstPerson, reuse);   // a film's first person
            drawn[e.Packed] = d;
            view.Characters.Add(d);
            // What it holds, on its pose; in first person the held weapon is drawn, not hidden.
            for (int h = 0; h < kind.Held.Count; h++)
            {
                drawnHeld.TryGetValue((e.Packed, h), out var reuseHeld);
                var held = poses.DrawHeld(world, e, h, d.World, reuseHeld);
                HideBowModelArrow(e, anims.Get(e).Kind, held);   // 1.1's bow model's own arrow, while nothing is on the string
                drawnHeld[(e.Packed, h)] = held;
                view.Characters.Add(held);
            }
            // The hit volumes' overlay, stood where the body is drawn (not the player's own in first person).
            if (ShowHitVolumes && !(firstPerson && e == player)) HitVolumeView.Add(world, e, place, view.HitVolumes);
        }
        if (ShowHitVolumes) HitVolumeView.AddLevelMarks(world, view.HitVolumes);   // where blows met the level, a moment ago
        // The flurry's cue at the picture's time (between the last tick and this one).
        if (ShowCues && world.TryService<BlowCues>(out var cues)) cues.Fill((world.Tick - 1 + alpha) / (double)World.TicksPerSecond, view.Cues);
        else view.Cues.Clear();
        // The swing's trail at the picture's time.
        if (ShowSwingTrail && world.TryService<SwingTrails>(out var trails)) trails.Fill((world.Tick - 1 + alpha) / (double)World.TicksPerSecond, view.SwingTrail);
        else view.SwingTrail.Clear();
        DrawArrows(view);   // the arrows, in flight and stuck (a world with a bow only); with 1.1's arrow model, the newest stopped ones for a while
        DrawBowAids(view);  // the bow's point of impact and path (developer's aids, when shown)
        // The swing's arc — predicted once a tick (the first picture after it), stood on the body as drawn.
        view.SwingArcOpacity = Math.Clamp(SwingArcOpacity, 0f, 1f);
        var arcOf = world.Alive(ArcOf) ? ArcOf : player;   // a film's fighter
        if (ShowSwingArc && view.SwingArcOpacity > 0f && world.Alive(arcOf) && bodies.Has(arcOf))
        {
            if (!swingArcFrozen && (swingArcTick != world.Tick || SwingArcs.Of != arcOf))
            {
                SwingArcs.Predict(world, arcOf);
                swingArcTick = world.Tick;
            }
            ref var pb = ref bodies.Get(arcOf);
            var (arcFeet, arcYaw) = swingArcFrozen ? (SwingArcs.Feet, SwingArcs.Yaw)
                : before.TryGetValue(arcOf.Packed, out var pw)
                ? (Vector3.Lerp(pw.Feet, pb.Feet, alpha), pw.Yaw + DetMath.WrapAngle(pb.Yaw - pw.Yaw) * alpha)
                : (pb.Feet, pb.Yaw);
            SwingArcs.Fill(arcFeet, arcYaw, view.SwingArc);
        }
        else { view.SwingArc.Clear(); swingArcTick = -1; }
        // The reticle's answer with the thing's name, the verb there, and the freed pointer's cursor.
        var pointer = world.Service<PointerState>();
        var reticle = world.Service<ReticleState>().Answer;
        view.Reticle = reticle with { Name = reticle.Kind == ReticleTarget.Person ? NameOf(reticle.Entity) : null };
        view.ShowReticle = !PointerFree && !orbiting;   // no crosshair from the orbit camera (the aim is the driving camera's, behind the player)
        view.ReticleOpacity = ReticleLook.For(world, player, MeleeReticleOpacity);    // faint in melee
        view.CombatLog = ShowCombatLog ? CombatLog : null;                            // the combat log's window
        view.ReticleVerb = pointer.Free ? PointerVerb.None : pointer.Verb;
        // The simulation's place back on the picture (in first person it is given in the simulation's pick field).
        view.Cursor = PointerFree && pointer.Free
            ? new PointerCursor(FirstPersonView.FromPick(pointer.Place, PickScale), Cursors.For(pointer.Verb), pointer.Under.Kind == ReticleTarget.Person ? WithPart(NameOf(pointer.Under.Entity), pointer.Under.Part) : null,
                                pointer.Verb is PointerVerb.None or PointerVerb.Refused)
            : null;
        view.Banner = modules.Banner;
        view.Seconds = world.Seconds;
        // The awareness overlay: read from the observers each picture.
        if (ShowAwareness) AwarenessView.For(world, view);
        else { view.Awareness.Clear(); view.AwarenessAreas.Clear(); }
        // The reach highlight: made again ten times a second of the simulation, and at once when it is turned on.
        if (!ShowReach) { view.Reach.Clear(); reachTick = -1; }
        else if (world.Alive(player) && world.Has<Body>(player))
        {
            var feet = world.Get<Body>(player).Feet;
            float yaw = viewNow.Yaw;
            long since = world.Tick - reachTick;
            if (reachTick < 0 || since < 0 || since >= ReachAtLeastTicks
                || (since >= ReachEveryTicks && (Vector3.DistanceSquared(feet, reachFeet) > 0.04f || MathF.Abs(DetMath.WrapAngle(yaw - reachYaw)) > 5f * DetMath.DegToRad)))
            {
                ReachHighlight.For(world, player, view.Reach);
                (reachTick, reachFeet, reachYaw) = (world.Tick, feet, yaw);
            }
        }
    }

    /// <summary>A short line for the corner: where the player stands and how.</summary>
    public string Status
    {
        get
        {
            if (!world.Alive(player)) return "";
            ref var body = ref world.Get<Body>(player);
            var rig = world.Get<CameraRig>(player);
            // The move being made, and a crouch; the masquerade's mode and the place the feet are settled in.
            var move = world.Has<Traversal>(player) ? world.Get<Traversal>(player).Kind : TraversalKind.None;
            string how = (move != TraversalKind.None ? move.ToString().ToLowerInvariant() : body.State.ToString().ToLowerInvariant())
                         + (body.Crouched != 0 ? ", crouched" : "");
            // What the action channel plays.
            if (world.Has<CharacterAnimation>(player) && world.Get<CharacterAnimation>(player) is { Action: not ActionRole.None, ActionEnding: false } acting)
                how += acting.Action switch
                {
                    ActionRole.AttackQuick => ", swinging (quick)", ActionRole.AttackStrong => ", swinging (strong)", ActionRole.Block => ", blocking",
                    ActionRole.Hurt => ", hurt", ActionRole.Shoot => ", the bow up", _ => ", dead",   // the bow's shot
                };
            string masquerade = world.Has<MasqueradeState>(player) && world.Get<MasqueradeState>(player) is var m
                ? $"  {(m.Mode == MasqueradeMode.Masquerade ? "masquerade" : "unbound")}, {LevelPlaces.Word(m.Place)}" : "";
            // The person under the reticle and the part of them.
            var on = world.Service<ReticleState>().Answer;
            string under = on.Kind == ReticleTarget.Person && WithPart(NameOf(on.Entity) ?? "a person", on.Part) is { } who ? "  on " + who : "";
            return string.Create(CultureInfo.InvariantCulture,
                $"{body.Feet.X:0.0} {body.Feet.Y:0.0} {body.Feet.Z:0.00}  {how}  {MathF.Sqrt(body.Velocity.X * body.Velocity.X + body.Velocity.Y * body.Velocity.Y):0.0} m/s  {(rig.View == ViewKind.First ? "first" : "third")} person{masquerade}{under}")
                + (world.Tick < noticeUntil ? "   " + notice : "");
        }
    }

    /// <summary>The head of a recording of this session.</summary>
    public TapeHeader TapeHeader(string build) => WithFoes(new Control.TapeHeader()
    {
        Build = build,
        Game = modules.Game.Id,
        Modules = modules.Recorded,
        ConfigHashes = modules.Code.ToDictionary(c => c.Info.Id, c => c.Stamp.ConfigHash),
        Level = options.Level,
        Start = options.Start,
        Seed = options.Seed,
        Notes = new()
        {
            ["people"] = options.People.ToString(CultureInfo.InvariantCulture),
            ["switches"] = string.Join(",", modules.Modules.SelectMany(m => m.Switches).Distinct().Order(StringComparer.Ordinal)),
            ["facing"] = options.Facing.ToString(),
            [TickInput.AlwaysRunNote] = options.AlwaysRun ? "true" : "false",
            [SessionOptions.LegTurnNote] = options.LegTurn ? "true" : "false",
            [SessionOptions.DirectionPaceNote] = options.DirectionPace ? "true" : "false",
            [CombatTempoOverride.TapeNote] = options.CombatTempo is { } ct ? ct.ToString("R", CultureInfo.InvariantCulture) : "",
            [Control.ThirdPersonAimSettings.TapeNote] = options.ThirdPersonAim.Note,
            [Control.AttackDirectionInput.TapeNote] = options.AttackDirection.Note,
            [TickInput.DoubleTapNote] = options.DoubleTapDodge.ToString("0.###", CultureInfo.InvariantCulture),
            ["profile"] = string.Join(",", modules.Code.Select(c => c.Stamp.ProfileId)),
            ["bodyProfile"] = options.BodyProfile ?? "",
            ["masqueradeAuto"] = options.AutoMasquerade ? "true" : "false",
            ["recoverSeconds"] = options.RecoverSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            [SessionOptions.TargetTestNote] = options.TargetTest ?? "",
            [WitnessRequest.TapeNote] = string.Join(";", options.Witnesses),
            [SessionOptions.TimeNote] = options.Night ? "night" : "day",
            [SessionOptions.WeaponNote] = options.Weapon ?? "",
            [ActionScript.TapeNote] = options.NpcActions ?? "",
            [SessionOptions.SheetsNote] = SheetRequest.ToNote(options.Sheets),
            [SessionOptions.LearnNote] = LearnRequest.ToNote(options.Learn),
            [SessionOptions.ArmorNote] = ArmorRequest.ToNote(options.Armor),
            [SessionOptions.CamerasNote] = options.Cameras?.ToNote() ?? "",                // the camera's numbers, when not the defaults
            ["playAs"] = options.PlayAs ?? "",                                              // --play-as
            [SessionOptions.StitchedAsQuickNote] = options.StitchedAsQuick ?? "",           // --stitched-as-quick
            [ProtectedPeople.TapeNote] = options.Protected ?? "",
            [SessionOptions.ArenaNote] = options.Arena ?? "",
            [SessionOptions.PlayerBowNote] = PlayerBowIn(options) ? string.Create(CultureInfo.InvariantCulture, $"{options.PlayerBow}:{options.PlayerArrows}") : "",
            [Control.PlayerBowSettings.TapeNote] = world.TryStore<Bow>(out _) ? PlayerBowAimFor(options).Note : "",   // every world with a bow
        },
    }.Placed(options.Place).Scenario(options.Scenario).Overridden(options.DataOverrides, options.Light).Viewed(options.StartView).WithBodyNotes(this).WithPeopleNotes(this));   // accel, arrows, body   // the override work: the data overrides and the light, when given   // the foes work: the foes' note, when there are foes; the scenario's program, when there is one

    public void Dispose()
    {
        world.TryService<CombatTrace>(out var trace);
        trace?.Flush();
        traceWriter?.Dispose();
        eventTrace?.Dispose();
        install?.Dispose();
    }

    TextWriter? traceWriter;
    EventTrace? eventTrace;
}

/// <summary>
/// One <c>--sheet &lt;who&gt;=&lt;file&gt;</c>: who, the file, and its text once read (a recording carries the text, so a
/// replay gives the same sheet though the file has changed or gone). the file may be a template's id
/// (<c>sandbox.elite_stress</c>) where no file of that name is.
/// </summary>
public sealed record SheetRequest(string Who, string File, string? Text = null)
{
    /// <summary>Reads <c>who=file</c>; false with the reason for anything else.</summary>
    public static bool TryParse(string text, out SheetRequest request, out string error)
    {
        int eq = text.IndexOf('=');
        request = new SheetRequest("", "");
        if (eq <= 0 || eq == text.Length - 1) { error = $"--sheet '{text}': expected <who>=<file> (Christof=my.json, Guard=guard.json, e3=x.json)"; return false; }
        request = new SheetRequest(text[..eq].Trim(), text[(eq + 1)..].Trim());
        error = "";
        return true;
    }

    /// <summary>The requests as a tape head's note: a JSON list of who, file and the text (read now when not yet read).</summary>
    public static string ToNote(IReadOnlyList<SheetRequest> requests)
    {
        if (requests.Count == 0) return "";
        var list = requests.Select(r =>
        {
            string? text = r.Text;
            if (text is null) try { text = System.IO.File.ReadAllText(r.File); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return new Dictionary<string, string?> { ["who"] = r.Who, ["file"] = r.File, ["text"] = text };
        }).ToList();
        return System.Text.Json.JsonSerializer.Serialize(list);
    }

    /// <summary>The requests from a tape head's note.</summary>
    public static IReadOnlyList<SheetRequest> FromNote(string note)
    {
        var list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(note) ?? [];
        return [.. list.Select(d => new SheetRequest(d.GetValueOrDefault("who") ?? "", d.GetValueOrDefault("file") ?? "", d.GetValueOrDefault("text")))];
    }
}

/// <summary>
/// One <c>--learn [&lt;who&gt;@]&lt;power&gt;=&lt;rank&gt;[:&lt;option&gt;,…]</c>: who (the player by default), the power
/// by the module's id, the rank (0 unlearns) and the options (none: the module's for it), and the text as given.
/// </summary>
public sealed record LearnRequest(string Who, string Power, int Rank, IReadOnlyList<string> Options, string Text)
{
    /// <summary>Reads one; false with the reason for anything else.</summary>
    public static bool TryParse(string text, out LearnRequest request, out string error)
    {
        request = new LearnRequest("player", "", 0, [], text);
        string t = text.Trim();
        int at = t.IndexOf('@');
        string who = at > 0 ? t[..at].Trim() : "player";
        string rest = at > 0 ? t[(at + 1)..] : t;
        int eq = rest.IndexOf('=');
        int colon = rest.IndexOf(':');
        string rankText = eq < 0 ? "" : colon > eq ? rest[(eq + 1)..colon] : rest[(eq + 1)..];
        if (at == 0 || eq <= 0 || !int.TryParse(rankText.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int rank))
        {
            error = $"--learn '{text}': expected [<who>@]<power>=<rank>[:<option>,...] (celerity=3, celerity=2:celerity.active, Guard@fortitude=1)";
            return false;
        }
        var options = colon > eq ? rest[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        request = new LearnRequest(who, rest[..eq].Trim(), rank, options, text);
        error = "";
        return true;
    }

    /// <summary>The requests as a tape head's note: their texts joined by ';'.</summary>
    public static string ToNote(IReadOnlyList<LearnRequest> requests) => string.Join(";", requests.Select(r => r.Text));

    /// <summary>The requests from a tape head's note.</summary>
    public static IReadOnlyList<LearnRequest> FromNote(string note) =>
        [.. note.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(t => TryParse(t, out var r, out string e) ? r : throw new SessionException($"the recording's {SessionOptions.LearnNote} note: {e}"))];
}

public static class TapeHeaderPlacement
{
    /// <summary>The head with the player's placement noted, when there is one; a head without stays as before.</summary>
    public static TapeHeader Placed(this TapeHeader h, string? place)
    {
        if (place is { Length: > 0 }) h.Notes[Placement.TapeNote] = place;
        return h;
    }

    /// <summary>The head with the scenario's program noted, when there is one; a head without stays as before.</summary>
    public static TapeHeader Scenario(this TapeHeader h, string? program)
    {
        if (program is { Length: > 0 }) h.Notes[SessionOptions.ScenarioNote] = program;
        return h;
    }

    /// <summary>The head with the start view noted, when it is not the right shoulder; a head without stays as before.</summary>
    public static TapeHeader Viewed(this TapeHeader h, string? view)
    {
        if (view is { Length: > 0 } and not "right") h.Notes[SessionOptions.StartViewNote] = view;
        return h;
    }

    /// <summary>The head with the spring's, the arrows' and the player's body's notes (<see cref="Session.NoteBody"/>), when they apply; a head without stays as before.</summary>
    public static TapeHeader WithBodyNotes(this TapeHeader h, Session session) => session.NoteBody(h);

    /// <summary>The head with the people's turn and crouch noted, when the player's body is a person.</summary>
    public static TapeHeader WithPeopleNotes(this TapeHeader h, Session session) => session.NotePeople(h);

    /// <summary>The head with the data overrides and the light noted, when given; a head without stays as before.</summary>
    public static TapeHeader Overridden(this TapeHeader h, string? data, string? light)
    {
        if (data is { Length: > 0 }) h.Notes[SessionOptions.DataOverridesNote] = data;
        if (light is { Length: > 0 }) h.Notes[SessionOptions.LightNote] = light;
        return h;
    }
}

/// <summary>
/// The townsfolk the session makes (ours: in the cities at night, a combination of some
/// citizens out after dark, and guards): 1.1's characters out by day and by night, the keepers of the peace, the extra
/// 1.1 templates read for them, and the names they are shown by.
/// </summary>
static class Townsfolk
{
    /// <summary>By day: the catalogue's four, a townsman and a milkmaid.</summary>
    public static readonly string[] Day = ["Merchant", "YoungLady", "Monk", "OldMan", "Civ12male", "Civ06Milkmaid"];

    /// <summary>By night: citizens out after dark, a drunk, a beggar, a monk, a merchant.</summary>
    public static readonly string[] Night = ["Civ11female", "Civ07drunk", "Civ02beggar", "Monk", "Civ11female", "Merchant"];

    /// <summary>
    /// The townsfolk a session of <paramref name="people"/> makes, in the order it makes them (the keepers after them are
    /// not in it): the time's list round again (<c>Session.Populate</c>).
    /// </summary>
    public static string[] ForSlots(bool night, int people)
    {
        var list = night ? Night : Day;
        return Enumerable.Range(0, Math.Max(0, people)).Select(i => list[i % list.Length]).ToArray();
    }

    /// <summary>The keepers of the peace: by day guards, by night a watchman and a guard (one, or two with six townsfolk or more).</summary>
    public static readonly string[] DayKeepers = ["rudolfGuard", "rudolfGuard"], NightKeepers = ["KnightOfStJohn_torch", "rudolfGuard"];

    /// <summary>The 1.1 templates read beside the catalogue's characters, by their own names.</summary>
    public static readonly string[] Templates = ["Civ12male", "Civ03merchant1", "Civ10monk2", "Civ06Milkmaid", "Civ11female", "Civ07drunk", "Civ02beggar", "KnightOfStJohn_torch", "rudolfGuard"];

    static readonly Dictionary<string, string> ShownAs = new(StringComparer.Ordinal)
    {
        ["Civ12male"] = "Townsman", ["Civ03merchant1"] = "Merchant", ["Civ10monk2"] = "Monk", ["Civ06Milkmaid"] = "Milkmaid",
        ["Civ11female"] = "Townswoman", ["Civ07drunk"] = "Drunk", ["Civ02beggar"] = "Beggar", ["KnightOfStJohn_torch"] = "Night Watchman",
        ["rudolfGuard"] = "Guard",
    };

    /// <summary>The name a character is shown by: its own for a template, the catalogue's name spaced otherwise.</summary>
    public static string Shown(string name) => ShownAs.TryGetValue(name, out var shown) ? shown : Session.ShownName(name);
}
