// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
//
// The Classic game's command line, and the developer's tools' (vitomir-tools).
//
// **Every switch is listed here.** The list is grouped by what you are trying to
// do rather than alphabetically. Under `vitomir-platform --game classic` a switch the module's manifest does not declare wants
// --developer; the DEVELOPER VIEWS, THE REGRESSION HARNESS, --list-saves and --replay-folder are vitomir-tools' (src/Vitomir.Tools),
// which the platform refuses by name. Each of those turns on developer mode by itself.
//
// WHAT TO LOAD
//   --game <dir>            the install to read (otherwise it is found; under the platform, --oneone <dir>)
//   --data <dir>            layer a folder of loose files over the install, last one winning; repeatable
//   --level <name>          which level (default P1_HAVN)
//   --chronicle <name>      which chronicle owns the level, when several do
//   --scene <name>          one particular scene of it rather than the chronicle's first
//   --no-things             geometry only
//   --no-sky                no sky
//   --no-scripts            do not run the game's Java
//   --players-machine       a player's machine's scripts: the effects' only, none of the place's (a joined game's)
//   --no-ai                 no minds: nobody thinks
//   --rebuild-scripts       recompile the game's Java instead of using the cache
//   --script-sources        compile the Codex folder's sources instead of running Codex.nob's own classes (a developer's comparison)
//   --strict-scripts        a script error stops the run instead of being logged
//
// THE WINDOW AND THE PICTURE
//   --size 1280x720         window size
//   --window                a window where it was left, not full screen, and nothing written to [window] (ours, after 1.1's -window)
//   --fullscreen            full screen for this run whatever [window] says (ours; with --size, at that size as the mode)
//   --borderless WxH|off no title bar at that size, centred on its display, for this run (ours; [window] borderless keeps it)
//   --gpu vulkan|direct3d12 which backend; with none named, Vulkan first on Windows, else SDL's choice (ours)
//   --frame-log <file.csv> a row a second of frame times, memory and streaming while a level is played (ours)
//   --replay-size WxH       a replay drawn at another size than its recording's (a harness knob, ours: no clicks in the tape)
//   --menu-lettering ours|1.1  1.1's menus' words in our own lettering, drawn by the engine (ours, the default), or
//                           1.1's own pictures of them; [interface] lettering in vitomir.ini keeps it
//   --ui-layout wide|fit|stretch  the interface's sheet as the 16:9 stretch: 1.1's stretch up to 16:9, 16:9's centred
//                           beyond it (ours, the default); in its own proportions, centred; or pulled to the
//                           picture's shape as 1.1 does. [interface] layout in vitomir.ini keeps it
//   --no-vsync              the picture shown without waiting for the screen's refresh (a harness knob, ours: for measuring)
//   --frame-times <file.tsv> a row for every frame: its phases, the step's and the draw's parts, allocation, the collector
//                           (ours; summed by tools/perf/frames.py)
//   --frame-dump <folder>   the run's pictures, frame00000.png on, at --frame-dump-fps a second (30 unless given) of its
//                           frames' steps: a headless walk's, or a window's (a replay above all: --replay a.tape --frame-dump f)
//   --frame-dump-log <file.jsonl> with a window's dump: a line per picture of the keys, buttons, wheel, pointer, camera
//                           and swivel lock (ours, for the web site's clips; tools/site/media.py)
//   --headless <out.png>    render one frame with no window and write it
//   --frames <n>            quit after n frames (a smoke test of the windowed path)
//   --shot <out.png>        with --frames: write the last frame (menus and all) when the run ends
//   --ui-click x,y[;x,y]    windowed: left clicks at these window pixels, one a frame from frame 10,
//                           pushed into SDL's own queue so they take a hand's path (--ui-type t: then type t, Enter);
//                           a step may be r:x,y (right click), key:Ctrl+V, text:words or wait:frames,
//                           down:Key / up:Key (held), shot:file.png, and @template for x,y (the
//                           nearest living one, where it is drawn: r:@cappadocian)
//   --frame-ms <n>          windowed: every frame steps the game n ms (a harness; 16 is a player's 60 fps)
//   --settle <seconds>      let the scene run this long before the still (--settle-ms <n>: each step n ms, 33 by default)
//   --seed <n>              the launch's random seed (the log's `# random seed:`): a game takes the clock's,
//                           a harness run (--headless, --sweep) 1; --ai-seed <n>: the AI's part alone
//   --swing                 the character strikes at the ground ahead, --swing-ms (250) before the still
//                           (--swing-attack <n>: with the weapon's n-th attack)
//   --no-reflections        draw no mirrors (reflective floors as ordinary surfaces)
//   --explode <t>[@x,y,z]   make template t in front of the character (or at the point), run
//                           --explode-ms (150) more, then the still; --no-dynalights: no light blobs
//   --record <f.tape>       a window's run written down — launch, every input, every step — with the
//                           simulation's digest each frame beside it (<f>.tape.digest; InputTape.cs)
//   --replay <f.tape>       that run again from the tape, no real input (anything after it is added;
//                           --replay-fast: not waiting for the screen, as a check does not)
//   --replay-check <f.tape> the same, compared with the recorded digest: the first frame and field that differ
//   --replay-folder <dir>   every tape of a folder checked, one process each, several at once (--replay-jobs N),
//                           each in a small window (--replay-full: at its own size), with a table (replay-folder.txt);
//                           --rebase: a tape that diverged is given the digest of its replay (ReplayFolder.cs)
//   --pos x,y,z  --yaw deg  --pitch deg      put the camera somewhere exactly
//   --hold-camera           with --pos: the view stays there through cutscenes and camera paths (ours)
//   --cell-scene-report <f.json> a cell level that carries a 1.1 scene: what became of each of its things (ours)
//   --time <s>              the clock a still is rendered at (what sliding materials slide by)
//   --gamma <g>             brightness; 1 is the frame as drawn, 1.4 is the original's own
//   --no-fog  --no-passes   drop the fog; draw multi-pass materials as one plain pass
//   --sky-frame <f>         the sky animation's starting frame
//   --sky-everywhere        draw the sky regardless of the level's own sky window
//   --sky-only <name>  --sky-hide <name>     restrict or hide one sub-model of the sky
//
// THE GAME ITSELF
//   (no flags at all)       the title screen and the main menu, as 1.1 starts — New, Load, Credits
//   --menu                  the same, with any of the settings flags below (--size, --fix, --log…);
//                           --journal on its own is a setting too, and every game played writes one
//   --credits               the main menu with the credits already rolling (what the end of the game does)
//   --skip-intro            skip the films the front end plays; --no-video: no films at all;
//                           --ffmpeg <path>: the ffmpeg that decodes them (or VITOMIR_FFMPEG)
//   --fix <name>[,…]|all --qol <name>[,…] --no-qol <name>[,…] the Fixes and QoL switches
//   --new-game              straight into a new game, as the single-player menu's New does
//   --play                  a game run as the menus start one: the status bar up and the autosave on
//   --autosave              autosave on every location entry in any run (the menus' games do anyway)
//   --menu-open <what>      headless: open title, sp, credits, load, save, escape, message or input first
//                           (pane:<file> shows any pane of the install by itself, for a still of it)
//   --menu-click x,y[;x,y]  headless: press and release at these window pixels on the menus
//   --menu-steps <steps>    --ui-click's steps on the first front end, through SDL's queue, so a recording keeps them
//   --create-advance <t>    headless: the advancement screen after Create, on a new multiplayer character of template t
//   --console               headless: the developer console left open for the still (the grave key opens
//                           it in every game; 1.1's -console); --console-run "a;b" runs lines
//   --devmode               1.1's -devmode (the options word's 0x80): out of the Storyteller's seat, the
//                           move command (Alt and the right button) puts the steered character at the spot
//                           — and Ctrl+P into the free camera, as [qol] freecamera does
//   --show-objects          headless: the show-objects key held for the still (ours); lists what it lights
//   --map                   headless: the map of the place, as M opens it (--map-reveal: every place found)
//   --menu-open options     the Options screen (also gameplay, controls, display, advanced, sound,
//                           and Vitomir's own fixes and keys pages); --options is the same
//   In a game Esc opens the escape menu (Resume, Load, Save, Options, Quit to the main menu),
//   Pause pauses, and every location entered writes the autosave — exactly 1.1's rules.

// SAVES
//   --load <file.vsave>     start from a Vitomir save (a zip, or a folder of its parts)
//   --save <file.vsave>     write a save when the run ends — after --settle, at the still, or on quit
//   --save-plain <dir>      the same save as a loose folder of readable parts
//   --save-strict-json      write saves (and kept network characters) as plain JSON, without the comments
//   --list-saves            list the saves in the player's save folder and stop
//   --import <SaveGames\SPn>  start from 1.1's save (the folder or its !main.nsg) as a load does;
//                           --import-to <file.vsave>: write it as a Vitomir save instead and stop
//   The menus' slots are SP0.vsave (the autosave) to SP29.vsave in the game's Saves (Modules\classic\Saves in the user data).
//
// PLAYING
//   --player                put the chronicle's own player character in
//   --player-template <t>   build the player from this template instead
//   --start x,y[,z]         put the player here instead of at the scene's start (--companion ones come too)
//   --face <deg>  --face-thing <guid>        which way to face
//   --party                 recruit the scene's party members
//   --entrance <n>          open the level at this entrance; --take-exit <n>: leave by this exit at once
//   --buff [n]              every stat n (100), health and blood three times that (a harness knob)
//   --cast <d>[@<t>][,…]    cast these disciplines, at the nearest <t> if given; --use <t>@<s>[,…]: use the
//                           nearest <t> that far into --settle (HarnessOrders.cs)
//   --behead <t>            the first <t> made loses its head as the place opens
//   --hover <t|guid>        the pointer over that thing for the still
//   --shapeshift <t>[:flags] the steered character in template t's shape; `t,end` and back
//   --weather <name>        start with this weather effect (snow, rain, lightRain, heavyRain, drizzle)
//   --party-stance <s>      their stance
//   --party-there           do NOT teleport a recruited member to the player (the default does)
//   --companion <t>[,<t>]   bring these templates along as party members (e.g. wilhem), the way
//                           the campaign would have — for the scenes that expect one
//   --run                   the walk harness runs instead of walking
//   --hud                   the status bar in a headless still too (a windowed run with a player has it;
//                           F11 toggles it in the game camera, 1.1's key and the `statusbar` binding)
//   --no-hud                start a windowed run without the status bar
//   --character  --inventory  --vault  --quests  --talk  --journal        open a pane at start
//                           (C or Ctrl+F5 sheet, I pack, Q quests, the console's `vault` for the vault; F6-F9 and Tab steer; Ctrl+F2/F3 the overlay)
//   --quest <n>[,<n>...]    load these quests (quests/<n>.nqd) the way a script does; <n>! completes it too
//   --answer <n>            answer every conversation with its n-th reply (for headless runs)
//   --arm <t>               this weapon template in the hands of the player and the whole party
//   --gold <n>              start with this much
//   --shop <file.nbs>       open that shop on the nearest character (--shop-name <DESCRIPTIONID>)
//   --shop-click x,y[;x,y]  press and release at these window pixels on the open shop (headless)
//   --pointer x,y           rest the pointer at this window pixel before the still
//   --pointer-log           say in the log whose each pointer event is: a pane's, the bar's, the world's or nobody's
//   --give <t>[,<t>...]     put these templates in the player's pack and equip them
//   --quick <t>[,…]         fill the quick-use strip with these templates
//   --disciplines <d[:level]>[,…]   teach these disciplines off game.ndd and fill the power slots
//   --discipline-pane       open the discipline pane at start (D in play)
//   --effects <e>[,<e>...]  put these effects.ned effects on the character, for a minute
//   --feed <t|guid>         start a feed on the nearest of this template (or this thing), at once
//   --advance [<xp>]        open the advancement screen on the player, with this much to spend
//   --no-cursor             do not draw the cursor
//   --no-rollover           do not draw the description boxes
//   --no-heads              do not draw the portrait heads' models
//   --mute                  run the sound as usual but send nothing to the speakers
//   --record-sound <f.wav>  also write everything the mixer plays to a file (works headless too)
//   --say <recording>       the steered character speaks a recorded line (to watch its lip-sync)
//   --film <name.bik>       start a story film as a script's PlayVideo does (--film-at <s>: headless still of it)
//   --material-film <material>,<name.bik>[,loop][,nohw]   play a film onto a material as a mod's MaterialPlayback does
//   --ui-right              make the synthetic --click a right-click
//   --ui-modifier shift|ctrl|alt|space       the modifier held for that click
//   --click x,y             one synthetic click, for testing the UI with no hands
//   --walk <seconds>        how long to advance the simulation while walking (default 30)
//   --walk-to x,y,z         walk there and report what happened
//
// DEVELOPER VIEWS (each implies --debug)
//   --things                list every thing in the level          [loads a level]
//   --modes                 which draw mode every surface ends in   [loads a level]
//   --find <material>       where a material is used                [loads a level]
//   --chronicles            the campaign's chronicles and exits     (add --verbose for locations)
//   --scripts-all           every script call the run makes
//   --gait                  the walk/run animation rates
//   --saves [dir]           read the save games and report them
//   --storm <seconds>       measure the Tower of London's lightning
//   --sky-groups            the sky model's groups
//   --actor-type <type>     force every actor's type
//   --watch                 the diagnostic overlay (Ctrl+F2 toggles it in play)
//   --thing-lights          with --headless: what lights each party member; --no-relight: things keep the light they were built with
//   --diagnostics           print the settings in force and where each came from
//   --gpu-debug             the GPU validation layers
//   --verbose               more of whatever the above is printing
//
// THE REGRESSION HARNESS
//   --sweep <dir>           render every level, no window, into a folder with a sweep.csv
//   --compare <dir>         point at an earlier sweep and report what changed
//                           (--sweep does NOT imply --debug; pass it if you want the log)
//   --story-sweep <dir>     start every single-player place with its scripts, run each 60 s with no
//                           window, and write story-sweep.tsv/.md of what broke (--story-sweep-only
//                           <names>: chronicles, locations, levels or scenes; --story-seconds <s>;
//                           --story-buff [n]: the player at n, 100 by default, in every stat, as --buff)
//
// LOGGING
//   --debug                 developer features on, log level Debug
//   --log <level>  --log <Category=level>    e.g. --log Vitomir.World=Trace; repeatable
//   --log-file <path>       write the log HERE INSTEAD of the managed folder
//   --log-folder <dir>      where the managed log folder lives
//   --log-keep <n>          how many old logs to keep
//   --no-log-file           write no log file at all
//   --no-log-console        no log on the console
//   --config <file>         the settings file to read
//
// A run with no diagnostic flag still WRITES A LOG FILE (warnings and errors, to the managed
// folder); what is off by default is the developer features, not the file.
//
// Controls, with a player: the game's own camera keys — keypad 4/6 turn, 8/2 tilt, − and = zoom,
// Z (held) looks through the character's eyes, X or keypad 5 (held) swivels, the pointer at the
// screen's edge turns; and under [qol] camera the wheel zooms, the arrows turn and middle-drag
// swivels. W/A/S/D walk (ours, [qol] driving), Space skips a conversation line, Ctrl+F1 prints where
// things are (with --debug). With no player, the free camera's keys: W/A/S/D move, X/C
// up/down, Shift faster, the arrows turn and tilt, right-drag turns (and middle-drag, with [qol] driving).
// Esc opens the escape menu; the window's close button quits.
