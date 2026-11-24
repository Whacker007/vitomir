Vitomir Platform
================

The new platform that Vitomir's game modules run on, with the enhanced module (the FPS/TPS action RPG
whose rules come from the rules package). It is built beside the faithful Vitomir and shares nothing of
its player data: its settings, logs and recordings are in %LOCALAPPDATA%\Vitomir.

Start it with the level package to play and the rules sandbox (the shipping profile refuses to start
until its numbers are signed off; that is on purpose):

  bin\vitomir-platform.exe --level C:\Games\Vitomir\levels\eastgate.l2 --sandbox

It remembers the level and the 1.1 install (asked for with --oneone, else VTMR_GAME_DIR, else C:\Games\VTMR)
for the next start. bin\vitomir-platform.exe --help lists the rest.

The game's rules are a module, a folder of its own: bin\Modules\Enhanced\ (its module.json, its DLL in bin\
and its rules data in Data\, which you can read). The game is the list of modules in bin\Games\enhanced.json.
bin\vitomir-platform.exe --check-modules checks them and says what it found.

Rebinding, until the Controls page comes (a key given to one action is taken off any other):

  bin\vitomir-platform.exe --bind "jump=Mouse Right" --bind "crouch=X"
  bin\vitomir-platform.exe --bindings

For runs from a command line (the arena, the snag check, the replay check), use bin\vitomir-platform-cli.exe: the
same program for a console, so PowerShell waits for it and shows its log as it goes (vitomir-platform.exe is the
desktop's: a console starts it and moves on, and its lines go only to its log file):

  bin\vitomir-platform-cli.exe --arena <spec.json> --out <folder> --level <proving-ground.l2> --sandbox --jobs 4

No game content is in this folder: 1.1's characters are read from your own install.
