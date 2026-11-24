// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;

namespace Vitomir.Platform.Host;

/// <summary>The program's command line: <c>--name value</c> options and <c>--flag</c> switches.</summary>
public sealed class Cli(string[] args)
{
    public string[] Args { get; } = args;

    public bool Flag(string name) => Array.IndexOf(Args, name) >= 0;

    public string? Arg(string name)
    {
        int i = Array.IndexOf(Args, name);
        return i >= 0 && i + 1 < Args.Length && !Args[i + 1].StartsWith("--", StringComparison.Ordinal) ? Args[i + 1] : null;
    }

    public int Int(string name, int fallback) =>
        Arg(name) is { } s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    public float Float(string name, float fallback) =>
        Arg(name) is { } s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

    /// <summary>A size given as <c>WxH</c>.</summary>
    public (int W, int H)? Size(string name)
    {
        if (Arg(name) is not { } s) return null;
        var parts = s.Split('x', 'X');
        return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0 ? (w, h) : null;
    }

    public const string Usage = """
        vitomir-platform: the Vitomir platform with the enhanced module

          --level <folder>        the level package (an .l2 folder) to play
          --game <id|file>        the game to play: Games\<id>.json beside the program, or a game file (default: enhanced)
          --oneone <folder>       the 1.1 install whose characters are drawn (default: VTMR_GAME_DIR or C:\Games\VTMR;
                                  --game <folder> is still taken as it). Under --game classic a developer's override: the
                                  game plays from the data --setup imported, else VTMR_GAME_DIR's, else it says to run --setup
          --setup [<step>]        the setup: finds 1.1 (GOG, Steam, the usual folders), checks its files
                                  against the manifest, imports them into <user data>\Imported\vtmr-1.1, lists 1.1's own saves
                                  and finds FFmpeg; a step alone: find, check [--fast], import, saves, import-saves
                                  <SP4[=SP12]>... [--replace], ffmpeg [--install-ffmpeg], status; --from <folder> the install;
                                  move-user-folder <folder|portable> [--keep-old] moves the user folder (vitomir.ini).
                                  shortcut [desktop|startmenu on|off|update]: the launcher's shortcut (alone: both's state).
                                  Exit codes: 0 done, 1 failed, 2 the command line, 3 not the data (each file named), 4 no room,
                                  5 a save not imported, 6 no FFmpeg
          --modules <folder>      a folder of module folders, searched after Modules\ beside the program
          --check-modules         check the modules and the game (manifests, ids, versions, overrides) and exit
          --game classic --headless  1.1, the Classic module, which keeps its own time (headless only): --frames <n>
                                  (60) frames of --frame-ms <ms> (33) each for a picture of --size WxH (640x480); the rest is
                                  the module's: --level <1.1 level>, --player, --new-game, --scene, --chronicle, --entrance,
                                  --seed, --no-ai, --no-scripts, --fix, --qol, --digest <file>, --digest-check <file>
          --game classic          1.1 in a window, its things in <user data>\Modules\classic\ and its window's settings
                                  in <user data>\Games\classic\window.json; it takes the players' switches its
                                  module.json declares (--check-modules lists them: --window, --size WxH, --fullscreen, --gpu, ...)
          --developer             with a game played by its module's own loop (--game classic): every other switch of the
                                  module's too, the harness's and the developer's as the module takes them (--headless,
                                  --level, --player, --ui-click, --menu-steps, --record, --replay-check, ...); without it an
                                  undeclared switch is refused (exit code 2). It changes nothing in the game
          --sandbox               run the rules module's research sandbox profile (the shipping profile refuses: fail closed)
          --profile <name>        the player's own movement profile, unbound: human, neonate or elder (default human; the
                                  masquerade, on in public places, holds every body to the human's; Caps Lock toggles it)
          --start <n|name>        the level's start anchor to begin at, by number or by name (default 0; a name the
                                  level does not have is refused with the names it has: the proving ground's mantle)
          --people <n>            townsfolk walking the level (default 6)
          --seed <n>              the world's seed (default 1)
          --centered WxH          a window of that size with no title bar at the centre of the display (the default, 2560x1440)
          --windowed WxH          a window of that size with a title bar
          --picture WxH           the size the world is drawn at (fitted to the window; a centred window's own size by default)
          --character-texture-limit <1024|2048|4096>  the largest a character's texture is uploaded at (the setting
                                  characterTextureLimit, default 2048; 4096 the files' own); larger ones are halved
          --record <file>         record the session to a tape
          --replay <file>         play a tape back in the window
          --replay-check <file>   replay a tape headless and compare its digests (exit code 0 when it agrees)
          --replay-dump <file>    replay a tape headless and write the player's animation tick by tick to dump.tsv
          --dump-dir <folder>     with --replay-dump: where (default <tape>-dump beside the tape)
          --dump-without-notes    with --replay-dump: the tape's input with this build's people (the turn, the kneel, the feet,
                                  the sprint and the crouched capsule from the settings and flags, not the tape's notes), to
                                  set beside the tape as it was
          --dump-stills <ticks>   with --replay-dump: stills from the player's camera at those ticks, e.g. 300-420:10,600
                                  (t00300.png ...; --picture WxH their size, default 960x540)
          --record-drive <file>   drive by script headless for --seconds (default 30) and record it (to check replays across machines)
          --headless <png>        draw one picture headless after --seconds of simulation and exit
          --seconds <s>           with --headless: how long to simulate first (default 1)
          --drive                 with --headless: the scripted drive instead of standing still
          --view first|right|left the view play starts in (else the one the last session was left in: the setting
                          lastView; a replay starts as its tape did)
          --first-person          with --headless: in first person
          --left-shoulder         with --headless: in third person over the left shoulder
          --look-pitch <degrees>  with --headless: the view pitched so (up positive) on the first tick
          --head-motion <0-100>   the first-person head motion for this run, per cent (else the setting's)
          --fov <60-110>          the first-person field of view for this run, vertical degrees (else the setting's)
          --fov-axis <vertical|horizontal>  how --fov (and the setting) is measured: up the picture or across it (70-140)
          --turn-view <deg>[@<s>[:<over s>]]
                                  with --headless: turn the view by that much (left positive) from that second
                                  (default 0.5) over that long (default 0.2), as the mouse would
          --picture-turn <deg>    with --headless: swing the third-person camera round the player by that much for the
                                  picture only (free look's orbit; 180 looks at the front)
          --no-leg-turn           the legs do not turn toward the way a body moves (the setting legTurn): the
                                  poses without it; with --arena too
          --no-direction-pace     every body moves at its pace every way (else backpedal 0.70, sidestep 0.90 of
                                  the pace for a body facing elsewhere); with --arena too
          --look-at x,y           with --headless: turn the view to that point of the level first
          --walk-to x,y[@s[,sprint]]
                                  with --headless: run to that point of the level (the view turned to it); from that
                                  second on (before it the player stands), at a sprint if asked
          --place x,y,z[,yaw[,view]]
                                  put the player there before the first tick, facing yaw (degrees), the view turned
                                  to the same or to view (degrees): in the window, with --headless and with
                                  --record-drive; a recording carries it and its replay puts the player there again
          --forward [walk|run|sprint]
                                  with --headless: go the placed way the whole time at that pace (default run)
          --jump-at <s>[:<held s>]
                                  with --headless: press jump at that second, held that long (default a tap)
          --crouch-at <s>         with --headless: hold crouch from that second on
          --use-at <s>            with --headless: press use at that second (a ladder's foot or top: onto it)
          --attack-at <s>[:<held s>][,...]
                                  with --headless: press attack at that second, held that long (default a tap: a
                                  quick blow; held 0.3 s or more: a strong one, made as it is let go; the press
                                  readies the blow from the direction chosen and holds it, the release strikes)
          --attack-key left|right|forward|back
                                  with --attack-at: that move key held on each press's first tick (with
                                  --attack-direction keys: A left, D right, W overhead, S a thrust)
          --dodge-at <s>[:left|right|back|forward][,...]
                                  with --headless: press Dodge at that second with those move keys held for the press
                                  (none: straight back)
          --block-at <s>[:<held s>]
                                  with --headless: hold block from that second, that long (default to the end)
          --swap-at <s>[,...]     with --headless: press swapWeapon (F) at that second: the sword and the bow swap
          --picture-turn-from <s> with --headless and --picture-turn: the camera swung round from that second on, free look
                                  held to the end (stills from the side for a film)
          --orbit <yaw>,<pitch>[,<boom>][@<s>]
                                  with --headless: the orbit camera (Right Alt's) swung by yaw degrees (its look turned left
                                  positive: -90 puts it on the player's left) and tilted (up positive), at boom m, from that
                                  second (default the start), kept there as he turns
          --show-impact           with --headless: the bow's point of impact drawn (the aid's action showImpact)
          --show-path             with --headless: the bow's path drawn, and each shot's real path beside it (showPath)
          --bow-solve-on          the player's arrow launched at the angle that meets the crosshair's point at the draw reached
                                  (the setting bowAimSolve on for the run; by default it leaves along the line and drops)
          --bow-aim-off           the player's bow without aim mode: attack held draws, the module's spread, no
                                  solve, 1.1's shot clip without the ready pose (a recording carries it)
          --aim-at <s>[:<held s>][,...]
                                  with --headless: hold aim (the bow's aim mode) from that second, that long (default to
                                  the end)
          --stills-from <s>       with --headless: a picture every 1/--stills-fps s (default 30) from that second to the
                                  end, <picture>-f0001.png on beside the picture, for a film (ffmpeg) of the free yard
          --weapon <template>     the 1.1 weapon the player carries (default BroadSword, Christof's starting sword;
                                  Rapier, Halberd, GreatSword, Punch, ...); a recording carries it
          --no-bow                the player carries no bow (by default, in the sandbox, the module's war_bow beside
                                  his sword: swapWeapon, F, swaps them; aim held, Q, raises it to the ready pose,
                                  attack held while aiming draws, released looses at the crosshair's point; block or
                                  cancel lets it down, aim let go lowers it; drawn as 1.1's longBow); a recording carries it
          --bow-arrows <n>        the arrows in the player's quiver (default 24); a recording carries it
          --bow-zoom <percent>    the view narrowed by that much at full draw (0 to 50; the setting bowZoomPercent, 0 by
                                  default: off), with the draw's share; presentation only
          --shield-at-bow-off     a duelling brain does not raise its shield against a bow drawn at it (it answers only a blow in reach)
          --bow-look-off          an archer keeps his melee stance and sword while the bow is out
          --stitched-as-quick overhead|thrust|both
                                  every one-handed blade of Christof's skeleton plays the blows stitched from 1.1's
                                  clips (the overhead, the thrust) as its quick blows in place of 1.1's, for
                                  measuring and filming them (the direction table plays them otherwise); a recording
                                  carries it
          --npc-actions <who>:<action>@<s>[+<held s>][^<aim deg>],...
                                  townsfolk and guards made to act: who is a shown name (Guard, Guard (witness)), a
                                  template (rudolfGuard) or e<entity>; action attack, strong, block, hurt, death,
                                  deathslow, or left, right, overhead, thrust (a blow from that direction);
                                  ^20 aims a blow 20 degrees up; a recording carries it
          --sheet <who>=<file>    give that person this sheet (a sheet template or a sheet's save) in place of the
                                  game's: who is Christof (or player), a shown name, a template or e<entity>;
                                  repeatable; a recording carries the sheet
          --learn [<who>@]<power>=<rank>[:<option>,...]
                                  learn a power at a rank before the first tick: the player unless who is
                                  given (as for --sheet); with no options, the power's options as the game lists
                                  them (celerity=3 learns celerity.active); rank 0 unlearns; repeatable; a recording
                                  carries it. The game's powers: Celerity, blood_strength, blood_dexterity,
                                  blood_stamina and blood_healing (cast: quick slots 1 to 8 in that order), Potence
                                  and Fortitude (passive). Celerity's default curve is split (a body tempo and a
                                  flurry of blows each swing carries); --celerity-curve-tempo plays the pure tempo,
                                  --celerity-curve-oneone 1.1's curve, --celerity-attack-off its movement alone
          --armor <who>=<piece>[,<piece>...]
                                  dress who (as for --sheet) in these pieces of the game's armour before the first tick
                                  in place of what it wore ('none' undresses); after --sheet and --learn;
                                  repeatable; a recording carries it. The pieces (data/combat/armor.json): padded_coat,
                                  leather_coat, studded_leather, mail_shirt, mail_hauberk, scale_shirt, half_plate,
                                  plate_harness, skullcap, light_helm, full_helm, leather_gloves, gauntlets,
                                  heavy_gauntlets, greaves (one a slot). 1.1's guards and watch wear a mail hauberk and a
                                  light helm by their template
          --armor-off             armour soaks nothing: the harm of before armour (what is worn is still kept);
                                  otherwise a blow adds the dice of the pieces over the part it struck to the soak
          --severance-off         no severance: nothing is cut off and every body contact stops the blade (the
                                  harm without it); otherwise a blade's blow whose harm after soak reaches a part's threshold cuts
                                  it off and goes on with what its swing has left (the module's rules/severance)
          --dodge-dexterity-off   the dodge's Dexterity link off: every body's burst a human's, to measure the
                                  Dodge Ability's channel alone
          --severance-angle-off   severance without the blow's angle: every blow's whole harm after soak counts
                                  toward cutting; otherwise a glancing blow never cuts and an acute one
                                  counts a share
          --severance-melee-off   severance without Melee's edge: each angle band's own cut share, whatever the
                                  attacker's Melee
          --flurry-compressed     Celerity's extra blows compressed: every one resolved at once on the
                                  swing's contact; by default each is its own strike at its own moment, landing only
                                  where the blade's line still meets the target then
          --flurry-off            Celerity without extra blows: pure tempo, each swing one blow
          --flurry-swing-spacing  the extra blows spread through the rest of the swing, not 0.04 s apart
          --flurry-motion         show each extra blow as a re-strike: the blade pulled back a little from the bite and
                                  driven through the strike again, arriving at the checked line at the blow's moment
          --flurry-spacing-80ms, --flurry-spacing-120ms, --flurry-spacing-160ms
                                  the extra blows that far apart in place of 0.04 s (to compare)
          --blow-quality-off      blow quality off: every contact deals its whole damage potential whatever the
                                  work it could do; otherwise the potential is scaled by the contact's speed
                                  into the surface over its blow's own typical (rules/quality), at most 1.25 times, no roll
          --blow-quality-hilt-off the hilt alone off: a blade meeting at its hilt keeps its class's kind (otherwise
                                  bashing harm, never a cut)
          --brain-range-off       the duelling brain without range keeping: a blade fighter swings from wherever it
                                  is; otherwise it steps back out of a clinch (round its foe when pinned) before swinging
          --guard-skill-off       the guard by skill off: every fighter guards with fixed numbers whatever its
                                  Dexterity and Melee (otherwise its hand's speed, read time and the brain's chances of
                                  guarding and parrying from its technique: data/combat/guard.json's skill)
          --brain-parry-fixed     the duelling brain parries 0.25 s before the strike mark (otherwise timed by its hand and chosen only
                                  when its own swing will be over in time)
          --stagger-oneone        1.1's flinch: a wounding blow plays the hurt at most once in 3.5 s (otherwise the
                                  stagger: 1.1's hurt clip played whole, 1.3 s, the swing cut short, the guard lowered, the
                                  chest pushed, none again within 0.5 s of its end)
          --stagger-push-only     the stagger as the push alone: no hurt clip, 0.3-0.6 s, the body upright
          --stagger-hurt-short    the stagger with 1.1's hurt clip cut at 0.3-0.6 s (--stagger-hurt-clip is its alias)
          --rebound-two-thirds    the blocked blade's rebound at two thirds of its size, a parry twice the held
          --rebound-stun-at-meeting
                                  the attacker's stun after a blocked blow counted from the meeting (by default from
                                  the blocked swing's end)
          --pace-off              the duelling brain's constant aggression: no pause between its runs of blows
                                  (otherwise each kind of person's pace, data/combat/guard.json's brain_pace)
          --blade-guard-everyone  every brain guards with a blade by skill (otherwise one below Melee 1, Brawl
                                  bare-handed, guards only with a shield: guard.json's skill.blade_guard_least)
          --combat-tempo <x>      the global combat tempo for the run, 0.25 to 4: every fighter's swing and recovery at
                                  x times 1.1's timing, under Melee's and Celerity's rates, in place of data/combat/pace.json's
                                  tempo.global (0.70); not the block's rise, a hurt or a death; a recording carries it
          --combat-tempo-off      no global combat tempo (1.1's timing under Melee's and Celerity's rates)
          --fighting-pace-off     no fighting pace: a body runs and sprints through its own blow (otherwise, swinging
                                  or holding a blow ready, it moves at most pace.json's fighting_pace, 1.8 m/s)
          --dodge-paced           the dodge at pace.json's pace: a human's 1 m burst in 0.35 s and recovery 0.45 s
          --shield-read-off       no shield read: a duelling brain aims at a shield-bearer whatever its Melee
                                  (0.30 and 0.40 otherwise)
          --combat-trace <file>   write the rules' resolution of every contact (and Blood's ledger) to this file, one
                                  JSON line each
          --event-trace <file>    write the run's identity, everyone's sheet and every action, swing, contact, harm and
                                  stop to this file, one JSON line each (the arena writes one beside each tape)
          --protected <who>[,<who>...]
                                  people weapons never harm: a blow at one is found but does nothing, and they
                                  protest; who as for --npc-actions; the module's data protects some by kind (the
                                  merchants); a recording carries it
          --no-recovery           the player stays down after a zero-Health stop (by default it gets up after 5 s,
                                  Health back to its effective maximum: a sandbox convenience, not a rule; the setting
                                  playerRecovers keeps it off; a recording carries it)
          --show-reach            show the reach highlight for this run (the lips round the player where a move would
                                  start, in the proving ground's colours), in the window and in a --headless picture;
                                  the setting showReach keeps it on, the action reach (no key by default) toggles it
          --witness <kind>@x,y,z[,yaw]
                                  put an observer of that kind (guard, watchman, oldman, drunk, ...) standing there
                                  facing yaw (degrees), posted: it reacts and comes back; repeatable; a recording
                                  carries them
          --foe <who>@x,y,z[,yaw] put a hostile fighter there facing yaw (degrees), who comes for the player with
                                  the duelling brain, dressed and armed as the arena's fighters: who is a preset of the
                                  module's combat/foes data (szlachta, guard, watchman, townsman-club, townsman-dagger);
                                  repeatable (several come at once); a recording carries them
          --rush-unpaced          rushing creatures (pace.json's rush_unpaced: the szlachta) keep their own pace
                                  through their blows, not the fighting pace's walk
          --foes-uniform          every fighter moves as the human and closes as the duelling brain
                                  (no 1.1 template speeds, no 1.1 approach)
          --brain-straight        the duelling brain closes straight at its foe whatever is between them
                                  (by default it runs the navigation mesh's route round a wall its straight line meets)
          --body models|woman|oneone
                                  the player's body: the models repository's man (models, the default: the setting
                                  playerBody), its woman, or 1.1's Christof (oneone); the new bodies have no combat yet
                                  (attack, block, the bow and the swap do nothing) and crouch to 1.15 m (1.25 is
                                  the tall crouch: --person-crouch-height); files or loader
                                  missing: Christof, said once; a recording carries it. The arena, scenarios, --play-as and
                                  the surveys keep 1.1's
          --models <folder>       the models repository's files: its repository (else the setting modelsFolder, the one
                                  used last; VITOMIR_MODELS_DIR; People beside the program's bin; C:\Games\Vitomir\models)
          --accel-ramp            the player starts and stops on the fixed ramp, as every other body does (by
                                  default the player on a critically damped spring of its movement profile's half-life:
                                  the human 0.2 s); a recording carries it
          --townsfolk-oneone      the townsfolk who do not fight on 1.1's bodies (by default they
                                  are the models repository's crowd, each a person of its sex and role walking its own style:
                                  the setting townsfolkBodies, models or oneone); the guards, the watch, the foes and the
                                  arena are 1.1's either way; a recording carries it
          --crowd-built           the townsfolk on the crowd built at run time, on the bases with a
                                  tint and a height scale (by default each is the person the models repository baked:
                                  its own face, figure, skin and eyes, its outfit fitted to it; the setting townsfolkBodies
                                  models-built); a person whose folder is missing is built so anyway; a recording carries it
          --skin-every-frame      every person of the models repository skinned every frame, in view or not
                                  (by default those out of view are left out and the distant skinned every 2nd frame
                                  past 12 m, every 4th past 30 m: the settings characterSkinCull, characterSkinNear,
                                  characterSkinFar, characterSkinMidEvery, characterSkinFarEvery); presentation only
          --landing-slide         the player's landings on the spring keep their speed, so
                                  a landing on a slope slides (by default a landing keeps only the speed along the way
                                  asked, up to its pace, and none when nothing is asked); a recording carries it
          --start-stop-half-life <s>
                                  the player's starts and stops on a spring of this half-life for the run, in place
                                  of the profile's (0 to 1 s; 0 the ramp), for judging the feel; a recording carries it
          --turn-chain-off        a person's body turns without the chain: the chest on the view at once, the legs planted until
                                  45° and then a turn clip at its own pace, held by the 90° twist (by default the turn as a
                                  chain: the head, the chest and the hips each chase the view at their rates and catch up;
                                  the setting turnChainOff); a recording carries it
          --turn-head-rate <°/s>  the fastest the head turns after the view (300; the setting turnHeadRate)
          --turn-head-lag <s>     how far the head trails a steady turn of the view (0.03; turnHeadLag)
          --turn-neck-most <°>    the most the head is ahead of the chest (25; turnNeckMost)
          --turn-chest-rate <°/s> the fastest the chest turns after the head (200; turnChestRate)
          --turn-chest-lag <s>    how far the chest trails the head (0.06; turnChestLag)
          --turn-spine-most <°>   the most the chest is ahead of the hips (30; turnSpineMost)
          --turn-from <°>         how far the view leaves the standing hips before they turn on the spot (30; 45
                                  without the chain; turnFrom)
          --turn-on <°>           at a turn's end, how far off the next turn follows (20; turnOn)
          --turn-most-speedup <x> the most a turn on the spot is sped up with the hips far behind (2; turnMostSpeedup)
          --turn-pivot-from <°>   how far behind the view the legs also pivot on their own (75; turnPivotFrom)
          --turn-pivot-rate <°/s> how fast they pivot (240; turnPivotRate); every --turn- number rides on a recording
          --turn-leash <°>        the most the hips are ever behind the view (90; turnLeash): past it the whole body is
                                  carried round with the view; 0 or 180 is off (the hips up to half a turn behind)
          --turn-waist-even       the turn's waist even: the chest's turn split evenly over the spine and the spine
                                  slid on the hips to keep its top in place (by default spread mostly over the chest, the spine
                                  left on the pelvis; the setting turnWaistEven); a recording carries it
          --crouch-kneel          a person crouched and still settles into the kneel (by default the crouch
                                  held on the balls of the feet; the setting crouchKneel); a recording carries it
          --feet-off              a person's feet off: no foot locks (by default a foot the motion has planted is
                                  held where it was planted while the body stands, turns on the spot or crouches), no heel
                                  lift, no soft limit on the legs (the setting feetOff); a recording carries it
          --foot-lock-seconds <s> how long a planted foot takes to be held or let go (0.1; footLockSeconds)
          --foot-replant <m>      how far the motion may carry a held foot before it is planted again where the motion
                                  has it (0.2; footReplant)
          --heel-lift <°>         how far the crouch held and the crouched turns lift the heels onto the balls of the
                                  feet (28; 0 off; heelLift), only by what the clip lacks: the models repository's
                                  crouch is itself on the balls of the feet (heels 40° up), so it adds nothing there
          --bend-rule-off         the knees' and hips' bend helpers as the clips keyed them, the bend rule not run
                                  after the legs' IK (bendRuleOff); every feet number rides on a recording
          --sprint-from <m/s>     from this speed a person running ahead plays the models repository's sprint
                                  (drawn for 5.5 m/s, stride-matched) in place of the run capped at 1.35x (4.25; 0 the run at
                                  every speed; the setting sprintFrom); sideways or back at that speed the run as before
          --sprint-clip-off       a person's sprint as the run's blend capped at 1.35x (sprintClipOff); a
                                  recording carries the sprint
          --person-crouch-height <m>
                                  the crouched capsule of the models repository's bodies (1.15, every crouch clip's head
                                  at or below 1.10 m; 1.25 the tall crouch; personCrouchHeight); a recording
                                  carries it
          --arrows-heavy          the heavier war arrow (87.5 g at 53.3 m/s; by default the items
                                  set's long bow's, 54.5 g at 58.2 m/s); a recording carries it
          --time day|night        the time of day perception sees by (default night: light 0.3; day 1.0); it also
                                  chooses the townsfolk out; a recording carries it
          --show-awareness        show the awareness overlay: a mark over each observer's head (? aware, ! alerted,
                                  red ! alarmed) and its cone on the ground; the action awareness (no key by default)
                                  toggles it
          --show-hit-volumes      show the hit volumes: each character's capsules as wire outlines over its drawn
                                  body, coloured by region; the action hitVolumes (no key by default) toggles it
          --show-swing            show the swing trail: each swing's blade tip path (and, lighter, the blade) as contact
                                  swept it, kept 4 s, with the reticle's point (magenta) and the tip's nearest pass to the
                                  reticle's ray (yellow, the miss in orange); the action swingTrail (no key by default)
                                  toggles it; with an arena's --picture-at too
          --arc-freeze <s>        with --headless and --swing-arc: the arc predicted at that second and kept (with
                                  --show-swing, the arc drawn before a blow beside the blade's trail)
          --attack-direction <m>  how the blow's direction is chosen: keys (A left, D right, W overhead, S thrust; the
                                  default), automatic (the side of the foe the crosshair is on, above his shoulders
                                  overhead, below his waist a thrust) or mouse (the mouse's flick); the setting
                                  attackDirection. The press readies the blow, the release strikes; while held, the mode's
                                  choice now (a key, a flick, the crosshair) switches it
          --attack-spin           1.1's tap-and-hold for the run: quick blows in turn, the spin attack when held
          --aim-from-reticle      third person's blow pitched from the chest to the reticle's point (the earlier way; the
                                  setting thirdPersonAimFromReticle); by default the view's pitch, as first person
          --swing-arc <0..100>    with --headless: draw the swing's arc (where the player's next blow's blade will go) at
                                  that opacity, per cent; in play it is the setting swingArcOpacity (25 %, the Controls
                                  page's slider) and the action swingArc (no key by default) toggles it; with an
                                  arena's --film-at, the first fighter's arc
          --arc-carry-on          the swing's arc the old way: the body carried on by its velocity; by default it
                                  is in the body's frame, drawn on him (the setting swingArcCarryOn)
          --melee-reticle <0..100> with --headless: the reticle's opacity, per cent, while a melee weapon or the fists
                                  are out (in play the setting meleeReticleOpacity, 20 %, the Controls page's slider;
                                  a --headless picture draws it full otherwise)
          --show-combat-log       with --headless: draw the combat log's window (the last lines said about the fight,
                                  in the corner of the setting combatLogCorner, combatLogLines rows); in play the
                                  setting showCombatLog (on) and the action combatLog (no key by default); with an
                                  arena's --film-at too
          --hide-hud              hide the HUD (the quick slots, Health and Blood, the masquerade's mode) for this
                                  run in the window; it is shown by default (the setting showHud), the action hud
                                  (no key by default) toggles it (a stand-in until the designed HUD)
          --show-hud              with --headless: draw the HUD in the picture (a --headless picture has none
                                  otherwise, so the pictures that check levels stay as they were)
          --hud-sample            with --headless: the HUD drawn from a sample of every slot's state and both bars
                                  in place of the module's views (for pictures of the stand-in HUD)
          --show-cues             with --headless or an arena's --picture-at: draw the flurry's cue, a flash at the
                                  struck point for each extra blow a Celerity swing carries (drawn in play always;
                                  --show-hud draws it too)
          --target-test <range>[,nolos][,person|place|any]
                                  ask for a target (the hook a power's targeting will use): within range (m) of the
                                  eyes, in sight unless nolos; the cursor shows target or refused and a click (or use
                                  on the reticle) picks or is refused, logged, and it is asked again; a recording
                                  carries it
          --free-pointer x,y      with --headless: free the pointer at that place on the picture (0 to 1 across, down)
          --pointer               with --record-drive: free the pointer from 11 s to 12 s of each dozen, swept and clicked
          --toggles               with --record-drive: press crouch, walk and the pointer's toggles over the drive
          --fights                with --record-drive: press attack and block over the drive (taps, a hold, a buffered
                                  press, a block, the masquerade key)
          --slots                 with --record-drive: press the quick slots over the drive (each in turn, one held,
                                  two together), and count the powers the module used and refused
          --headless-screen pause|controls <png>
                                  draw a menu headless to a picture (--picture, default 2560x1440; over the level after
                                  --seconds when --level is given) and exit
          --ui-wait action.slot   with --headless-screen controls: leave that key cell waiting (slot 0 or 1)
          --ui-press <name>       with --ui-wait: then press that key, button or wheel turn into it
          --ui-focus <id>         with --headless-screen: put the focus on that piece
          --ui-scale <s>          with --headless-screen: the interface's scale (0.75 to 1.5)
          --snagcheck <file>      drive bodies along routes headless and write where they snag (JSON lines)
          --pairs <n>             with --snagcheck: how many routes (default 50)
          --gait walk|run|sprint  with --snagcheck: the pace (default run)
          --unbound               with --snagcheck: the profile's own numbers (by default held to the human's, as the
                                  player starts in the masquerade); --profile names the profile (default human)
          --snag-spring           with --snagcheck: the body starts and stops on the player's spring (by default on the fixed
                                  ramp, as the townsfolk walk; --start-stop-half-life gives its half-life)
          --arena <spec.json>     run headless bouts between fighters: needs --out
          --out <folder>          with --arena: where bouts.csv, contacts.csv, summary.html and tapes/ go
                                  (--bouts, --tapes, --seed, --seconds over the spec's; --sandbox for the rules sandbox)
          --scenario <file|folder> --out <folder>
                                  run a scenario (vitomir-scenario/1) or every
                                  *.scenario.json in a folder (each into <out>/<name>/, and <out>/batch.json): results.json
                                  (VITOMIR_SCENARIO_RESULTS_1), runs.csv, actors.csv, actions.csv, sweeps.csv, contacts.csv,
                                  powers.csv, bouts.csv, event streams (VITOMIR_EVENT_TRACE_2) and tapes as the trace policy
                                  keeps them; --runs n, --seed n, --only <point>, --trace-runs n, --jobs n over the file's;
                                  exit 0 passed, 3 an assertion failed, 4 inconclusive, 1 refused, 2 modules refused, 5 a run
                                  threw, 6 cancelled (Ctrl-C or a file named CANCEL in the output folder)
          --scenario <file> --out <folder> --only-run <run_id> [--compare <results.json>] [--trace-event <id>]
                                  one run again by its id (runs.csv's run_id, p003-r017: point 3, run 17; or p003@4717,
                                  point 3 at seed 4717, as an assertion's first failing seed) with its whole
                                  trace (stream, harm trace, tape, every tick); --compare checks its end digest against the
                                  recorded run's (and takes its seed): only-run.json says; exit 7 when it differs
          --trace-event <events.jsonl>#<id> | <run_id>#<id> --out <folder> [--json]
                                  an event's chain in a run's stream: its parents up to the first and every event that
                                  follows from it (results.json's evidence names events as p000-r003#41)
          --scenario <file|folder> --check [--json]
                                  check a scenario file (vitomir-scenario/1) or every
                                  *.scenario.json in a folder without running it: every name against the loaded game, every
                                  place against the ground, each point's first world built; prints the scenario resolved
                                  (every default explicit), its points, execution modes and setup; exit 0 valid, 1 not, 2 the
                                  modules refused (--check-no-world: no world built; --sandbox for the rules sandbox)
          --describe [all|<list>,...] [--json]
                                  everything a scenario may name, from the loaded game: characters, presets, sheets,
                                  traits, weapons, armor, powers, switches, brain, steps, events, observables, settable,
                                  measures, checks, data, grounds, modes, units, properties, coverage, errors
          --aim-survey <folder>   measure where the player's blade goes against the crosshair: quick and strong
                                  blows in first person and over each shoulder, free.csv (the view pitched -30 to +25, the
                                  reticle's point 1 to 2.5 m ahead) and targets.csv (a posted guard and a townsman 1 to 2.5
                                  m ahead, the reticle on the head, chest, belly or thigh: what the blade met); with
                                  --survey-at x,y (default 40,3), --survey-facing deg (default 90), --survey-only free|targets
          --arc-survey <folder>   measure the swing's arc against the blade swung: accuracy.csv (each blow
                                  in first person and over each shoulder, pitched -20, 0 and +20, standing, walking,
                                  running and stopping: the predicted tip against the swept) and targets.csv (a townsman
                                  1.0 and 1.5 m ahead, the arc put on his head, chest, belly or thigh: what the blade met);
                                  --survey-only accuracy|targets
          --picture-at <s,s,...>  with --arena: pictures of the first bout at those seconds, from beside the fighters
          --film-at <s0:s1[:fps]> with --arena: a film of the first bout from s0 to s1 (fps of the bout's time, 120 by
                                  default: a frame a tick) into film/<name>/, and with ffmpeg on the path <name>.mp4 (real
                                  time), <name>-quarter.mp4 and a small <name>.gif; --film-view side|near|first|third
                                  (several, comma-separated: one film each; first and third from the first fighter),
                                  --film-name <name> (bout0), --film-caption <text> (on the status line); tools/platform/
                                  film_side.py puts films side by side with captions
          --jobs <n>              with --arena: run n bouts at once (each on its own loaded content; default 1)
          --bind action=key[,key] bind an action and save it (as often as wanted; an empty right side unbinds; a key
                                  given to one action is taken off any other, but +key adds the key and shares it:
                                  both actions work it); e.g. --bind "jump=Mouse Right"
          --bindings              list the bindings in force (Escape, the action menu, opens the pause menu, where
                                  Controls remaps every action)
          --log-file <file>       also write the log there
        """;
}
