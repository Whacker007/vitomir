# FAQ

## What is Vitomir, in a sentence?

A new engine for *Vampire: The Masquerade – Redemption* that plays the game you own as patch 1.1 did, on today's
Windows, with modern comforts you can switch on or off. See the [features](features.md).

## Do I need to own the game?

Yes. Vitomir is an engine, not the game: it comes with none of the game's files and reads the copy you own. Vitomir
supports the [GOG](https://www.gog.com/en/game/vampire_the_masquerade_redemption) and
[Steam](https://store.steampowered.com/app/559680) releases (version 1.1).

## Steam or GOG: does it matter?

No. Both are version 1.1 and both work; Setup finds either by itself.

## What about my old CD copy?

A disc install patched to 1.1 can be imported, with a warning, but it is not supported: the store releases are the ones
Vitomir is checked against.

## Is my copy of the game changed?

Never. Setup only reads your copy and imports what it needs into your user folder. Everything Vitomir makes, it keeps
there.

## Windows warns about Vitomir. Is it safe?

Windows warns about any new program that is not signed, and Vitomir is not (see the next question). Choose **More info**,
then **Run anyway**. Download Vitomir only from its [releases page](https://github.com/Whacker007/vitomir/releases); a copy
from anywhere else is not this project's, and its source is all there for anyone to read.

## Why is there no installer?

Because a zip is all Vitomir needs: it installs nothing in Windows, keeps everything in its own folder and your user
folder (shortcuts and FFmpeg only if you ask for them), and is removed by deleting those folders. An installer that Windows trusts would also need a code-signing certificate,
which costs a yearly fee, and Vitomir is a free hobby project made by one person: that is not a cost it can carry for
now. If Vitomir grows enough for a signed installer to make sense, it will be looked at again.

## Where are my saves?

In your user folder, `%LOCALAPPDATA%\Vitomir` unless you chose another. The launcher's **Folders** page opens it, and
can move it, or make Vitomir portable so the folder travels with it.

## Can I play my old saves from the original game?

Yes: Setup's saves section lists the saves it finds in your copy of the game and brings across the ones you choose.

## Why can't I unzip it into Program Files, or the game's folder?

Windows does not let programs in Program Files keep their own files beside them, and a store may change or delete files
in the game's folder when it updates, checks or uninstalls the game, taking Vitomir with them. Any folder of your own,
such as `C:\Games\Vitomir`, is fine.

## There are no movies.

The movies need FFmpeg, which Vitomir does not ship. Setup can install it for you. The upscaled versions are optional, on
the launcher's **Movies** page. Be aware that on a PC without serious hardware (even a moderate dedicated graphics
card), making the upscaled movies can take an extremely long time: days, even. It is up to you whether that is worth the
time, the storage space and the processing.

## Will Vitomir play mods made for the original game?

Roughly, yes: texture packs, models, sounds, interface art and fan chronicles are added as player modules from the
launcher's **Modules** page. Changes a mod makes to the original game's program are not run. In short, *Neural
Redemption* works fully, *Crimson Edition*'s asset files work (and what its program changes were for, Vitomir does itself), and
*Age of Redemption* isn't supported right now, because it depends on its own patched program and scripts; possibly in
the future. The [Features](features.md#mods-for-the-original-game)
page has the details.

## My character is stuck on a corner or a step. What do I do?

Click to move a short way past the spot: a click finds its own path around what is in the way, and you can carry on from
there. Characters get caught on some edges, steps and corners in the original game too, and Vitomir keeps its movement as
it was. It shows most in driving mode, where the keys push straight ahead. Companions can be caught the same way: we
kept the game's own AI as it was, to keep the original experience. See [Known limitations](features.md#known-limitations).

## How do I host a network game for friends over the internet?

Host from the game's multiplayer menu. Vitomir asks your router to open its port by itself, and the log says the outside
address your friends type. Allow `vitomir-platform.exe` through Windows' firewall when it asks. Everyone needs the same
Vitomir release (and an unmodified GOG or Steam copy for the original game's files). Multiplayer hasn't been tested
extensively yet, so issues may crop up: save often.

When the router won't open the port, forward **UDP port 4994** to your PC in the router's own page. When your internet
provider shares one address among many customers (the log says so), no forward can reach you: all of you join one free
overlay network such as Tailscale or ZeroTier, and the host gives that network's address instead.

## I stream. Will my address show?

Not with **Streamer mode** on (the options page's last row): it hides every network address, your user name and join
details wherever text is on screen. Passwords are always starred.

## Something went wrong. How do I report it?

On the launcher's **About** page, **Copy details for a bug report**, then open a
[bug report](https://github.com/Whacker007/vitomir/issues) with it and what happened. In a network game, type `/bundle`
on the chat line and attach the zip it names.

## Is Vitomir official?

No. It is an unofficial fan project, not affiliated with or endorsed by the owners of *Vampire: The Masquerade* or of
*Redemption*.

## Will it run on Linux, a Mac or the Steam Deck?

Not officially. Vitomir is made and tested on Windows only: there is no Linux or Mac machine to test it on, and a
release nobody has tested is not one to offer. Much of it is already written to be portable, so this may change; if you
try it elsewhere, reports are welcome. As for controllers: the original game was made for the keyboard and mouse and
uses a lot of both, so adapting it to a controller would be exceptionally difficult. It's likely not impossible, but it is
low priority unless there's enough interest in it.

## Does Vitomir change how the game plays?

Not unless you ask it to. The rules and the story are the original's. The [quality-of-life](features.md) switches,
which are on, change how you control the game, not its outcomes; the Fixes, which change the game's own quirks, are off
until you switch them on.

## Can I play a network game with people on the original game?

No: everyone in a game needs Vitomir, the same release, and an unmodified GOG or Steam copy. The original's matchmaking
service is long gone anyway; Vitomir's hosting needs no server.

## Is it only in English?

Vitomir reads the GOG and Steam releases, which are the English game.

## What are the future plans?

See [What's next](roadmap.md).

## Can I put Vitomir on a mod site, or make a module for it?

Please don't upload Vitomir itself anywhere: link to its [GitHub page](https://github.com/Whacker007/vitomir), its one
official home. Modules that run on Vitomir (texture packs, models, sounds, chronicles) may be shared anywhere, free to
everyone, as long as everything in them is yours to share. Someone else's mod, turned into a module, is still theirs: ask
its author before sharing it. Nothing of the original game's files may ever be shared with Vitomir or with a module. The
[licence](licence.md) says it all in plain words.

## Can I stream or make videos of it?

Yes, monetised or not. That is what streamer mode is for.

## This is AI slop!

\*sigh\* No. This is a labour of love that I've sunk hundreds of hours into. Yes, AI helped: in fact this would not be
possible without it. I've been fairly open about what my core skills are and where AI has helped. When it comes to
writing code, anyone who doesn't use AI these days is hamstringing themselves, in my opinion. It's absolutely a force
multiplier, and it can fill in skill gaps and extend what a person is able to do. That said, it is very much a
double-edged sword: many are using it as a low-effort, throw-it-at-the-wall-until-something-sticks cash grab, or rushing
to replace skilled labour in areas where it just isn't ready yet. The genie is out of the bottle, so to speak, and yes, I
used it and will keep using it to help me. But I make a very concerted and serious effort to use it ethically in
everything I do, Vitomir included.

## Can I help?

Yes: [bug reports](https://github.com/Whacker007/vitomir/issues) with the launcher's details are the most useful thing.
Contributions to the code will be opened up to everyone eventually. Writing code isn't my primary technical skill set
(though I'm decent at it), and until recently I had never done any game programming or heavy development work, so I ask
for some patience as I keep learning.

## Why the name?

It plays on VTMR, the game's usual abbreviation, and Vitomir is a real Slavic given name, fitting a story that begins in
Prague and central Europe.
