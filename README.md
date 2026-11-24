# Vitomir

Vitomir is a new engine for *Vampire: The Masquerade – Redemption* (Nihilistic Software, 2000), written from scratch, that
plays the game as its patch 1.1 does, on a modern Windows PC. The name plays on VTMR, the game's usual abbreviation, and
is a real Slavic given name, fitting a story set in Prague and Vienna.

**It needs your own copy of the game.** Vitomir comes with none of the game's content: it reads the copy you own (patch
1.1, from GOG or Steam), which its Setup imports into your user folder once. Your copy is only read, never changed.

The Vitomir platform and all of its original content are copyright © 2026 Whacker007, under the Vitomir Licence: free
for any noncommercial purpose, never to be sold (see [Licence](#licence)). You are responsible for owning and installing
a legitimate copy of the original game for Vitomir to read.

*Vampire: The Masquerade*, *Vampire: The Masquerade – Redemption*, *White Wolf*, *World of Darkness* and all related
names, characters and properties are trademarks or copyrights of their respective owners, not of this project. Vitomir is
an unofficial fan project, not affiliated with or endorsed by them; their names are used here only to say which game
this engine plays.

## What you need

- **Windows 10 or 11, 64-bit**, with a graphics card that runs Direct3D 12 or Vulkan.
- ***Vampire: The Masquerade – Redemption*, version 1.1: the GOG or Steam release**, installed.
- A display of 1024x768 or larger, and about 2.5 GB free besides the game itself. Modest hardware is enough: Vitomir
  is tested on a laptop with a dual-core Intel Core i3-7100U, 8 GB of memory and Intel HD Graphics 620.
- Nothing else to install: Vitomir carries its own runtime. For the game's movies, FFmpeg (optional): the launcher's Setup
  page can install it for you.

## Installing

1. **Download** the zip from the [latest release](https://github.com/Whacker007/vitomir/releases/latest). Vitomir is
   published only here; a copy from anywhere else is not this project's.
2. **Unzip the whole Vitomir folder** into a folder of your own, such as `C:\Games\Vitomir`. Unzip it first: do not start
   it from inside the zip. And never put it:
   - in **Program Files**: Windows does not let programs there keep their own files beside them;
   - in **the game's own folder** (Steam's `steamapps\common`, GOG's `GOG Games`, or a disc install's): the store can
     change or delete files there when it updates, checks or uninstalls the game, and take Vitomir with them.

   The launcher warns you if it finds itself in either place.
3. **Start `vitomir-launcher.exe`** in that folder. Vitomir is not signed, so Windows may say it protected your PC: choose
   **More info**, then **Run anyway**.
4. On the first start the launcher asks where to keep your saves, settings and logs (your user folder; the usual place
   is `%LOCALAPPDATA%\Vitomir`), then its **Setup** page finds your copy of the game, checks it and imports it. Then press
   **Play**.
5. If you want them, Setup's last section makes a Desktop and a Start menu shortcut (nothing is made unless you ask).

**Updating:** unzip the new version over the old one. Your saves and settings stay in your user folder. **Removing:**
delete the Vitomir folder, and the user folder too if you want your saves gone.

## Building from source

Everything about building Vitomir, running its tests and making the zip is in [`BUILDING.md`](BUILDING.md).

## Support

The best way to support Vitomir is to buy the game it plays, on [GOG](https://www.gog.com/en/game/vampire_the_masquerade_redemption)
or [Steam](https://store.steampowered.com/app/559680), and to [report what you find](https://github.com/Whacker007/vitomir/issues).
If circumstances allow, there is also [Patreon](https://www.patreon.com/cw/Whacker007) and
[Ko-fi](https://ko-fi.com/whacker007). Nothing is ever unlocked by paying, and Vitomir stays free for everyone.

## Credits

Vitomir is made by Whacker007.

A huge thank you to my beta testers and friends who helped me iron out the wrinkles in the engine. You guys are
appreciated.

Roan 'Red' DeSeer\
Lillian Harper\
Goat

(Also in `CREDITS.md`.)

## Licence

Copyright © 2026 Whacker007. Vitomir is under **the Vitomir Licence 1.0** (`LICENSE`). In plain words (the
licence itself is what counts):

- **Free, and staying free.** Anyone who owns the original game may use Vitomir, change it and share their changes, at
  no cost, for any noncommercial purpose.
- **Never sold.** Nobody may sell Vitomir or anything made from it: no price, subscription, paid features or built-in
  advertising, and no copies in app stores or shops. Voluntary tips and donations for free work are fine, and so are
  videos, streams and reviews of playing it, even where those earn money.
- **One home.** Vitomir is published only here. Link to this repository rather than uploading Vitomir anywhere else.
  Changes to Vitomir itself are shared as forks of this repository, under another name; modules that run on Vitomir
  may be shared anywhere, free to everyone.
- **Only what is yours to share.** A module may hold only your own work, or work whose owners have said you may share it.
  Someone else's mod stays theirs when it is turned into a module or changed: ask its author before sharing it.
- **Never the game's files.** Nothing of the original game (its archives, levels, models, textures, sounds, music,
  movies, scripts, or files converted from them) may be shared with Vitomir or with anything made from it, and nothing
  made from it may help anyone get the game other than by buying it. Doing so ends your licence. This is the rule that
  protects everyone's work, so please keep it.
- **The name is Vitomir's.** A changed version needs its own name; it may say that it runs on Vitomir.

**Why this licence.** Vitomir is a fan-made project, and a love letter to the original game that so many of us
remember and played for hours and hours. It is free to people who own the game, and it should always stay so. An
open-source licence would have let anyone upload it elsewhere or sell it, as long as they passed the code along; a
closed one would have kept people from learning from it and building on it. This one sits between: the code is public,
and anyone may study it, change it and build on it, but nobody may sell it, scatter copies of it across other sites, or
use it to spread the game's own files. It is adapted from a standard noncommercial licence written by licensing
lawyers, with Vitomir's own conditions added. Because it forbids selling, Vitomir is "source-available" rather than
"open source" in the Open Source Initiative's sense.

Vitomir's typefaces are under the SIL Open Font License 1.1 (`assets/fonts/OFL.txt`); the components Vitomir builds on
and their licences are in `docs/THIRD_PARTY.md`. The game's own content is covered by none of these and is never
distributed here.
