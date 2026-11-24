Vitomir
=======

Vitomir is a new engine for Vampire: The Masquerade - Redemption (2000, patch
1.1). It plays the game from your own copy, as the game was released, on a
modern Windows PC. It needs your own copy of the game: none of the game's files
come with it.

What you need
-------------

- Your own copy of the game, version 1.1: the GOG or Steam release. Vitomir
  checks its files. (A disc install patched to 1.1 can be imported, with a
  warning, but is not supported.)
- Windows 10 or 11, 64-bit.
- A display of 1024x768 or larger.
- About 2.5 GB free besides the game itself.
- Nothing else: no .NET or other runtime to install.
- For the game's movies, FFmpeg (optional). The launcher's Setup page can
  install it for you.

Installing
----------

1. Unzip the whole Vitomir folder to a folder of your own, such as
   C:\Games\Vitomir. Never into Program Files, and never into the game's own
   folder (Steam's steamapps\common, GOG's, or a CD install's): a store can
   change or delete files there when it updates, checks or uninstalls the
   game, taking Vitomir with it. The launcher warns if it finds itself there.
2. Start vitomir-launcher.exe in that folder.
3. On the first start the launcher's Setup page finds your copy of the game,
   checks it and imports it. Your copy is only read, never changed. Then
   press Play.
4. If you want them, Setup's last section, Shortcuts, makes a Desktop and a
   Start menu shortcut to the launcher (nothing is made unless you ask).

Your saves, settings and logs
-----------------------------

Everything Vitomir keeps is in your user folder: your saves and the game's
settings (under Modules\classic), the logs (Logs) and the imported game files
(Imported). On the first start the launcher asks where it should be: the
usual place, %LOCALAPPDATA%\Vitomir (a folder there from an older Vitomir
is not used: rename or delete it first); a user folder you already
have; or portable, UserData inside the Vitomir folder. The launcher's Folders
page opens each of them.

Moving your data / portable
---------------------------

The launcher's Folders page moves your user folder (Move...): to a folder of
your own, or into the Vitomir folder (portable, so the folder carries
everything). Every file is checked after it is copied, and the old folder is
removed only then. Where the user folder is, is written in vitomir.ini, beside
vitomir-launcher.exe. You may write it yourself:

    [folders]
    user = D:\Vitomir-data

An empty "user =" (or no vitomir.ini) is the usual place; a folder's whole
path is that folder (%USERPROFILE% and other such names are expanded); a name
alone, such as "user = UserData", is a folder inside the Vitomir folder. A
line Vitomir does not know is named on the Play page, never guessed at.
Vitomir never ships a vitomir.ini, so unzipping a new version over this
folder keeps yours.

Updating
--------

Unzip the new version over this folder, or beside it. Your saves and settings
stay where they are, in your user folder. A new version unzipped into another
folder needs your vitomir.ini copied beside its vitomir-launcher.exe, or, on
its first start, "Use an existing one..." to point it at your user folder.
To remove Vitomir, delete its folder (and the user folder too, if you want
your saves gone).

Thanks
------

Vitomir is made by Whacker007. A huge thank you to my beta testers and
friends who helped me iron out the wrinkles in the engine. You guys are
appreciated.

  Roan 'Red' DeSeer
  Lillian Harper
  Goat

Licences
--------

The Vitomir platform and all of its original content are copyright (c) 2026
Whacker007, under the Vitomir Licence 1.0 (LICENSE.txt). Its source and its
releases are at https://github.com/Whacker007/vitomir. In plain words (the
licence itself is what counts):

- Free, and staying free: anyone who owns the original game may use Vitomir,
  change it and share their changes, at no cost, for any noncommercial
  purpose.
- Never sold: nobody may sell Vitomir or anything made from it. Tips and
  donations for free work are fine, and so are videos and streams of playing
  it.
- One home: Vitomir is published only at the address above. Please link
  there rather than uploading Vitomir anywhere else. Modules that run on
  Vitomir may be shared anywhere, free to everyone.
- Only what is yours to share: a module may hold only your own work, or
  work whose owners have said you may share it. Someone else's mod stays
  theirs when it is turned into a module: ask its author first.
- Never the game's files: nothing of the original game may be shared with
  Vitomir or anything made from it. This rule protects everyone's work.

THIRD_PARTY.txt lists the other components in this package and their
licences; the licences folder has each one's licence in full and where its
source is.

Vitomir needs the original game's assets from Vampire: The Masquerade -
Redemption, and comes with none of them. You are responsible for owning and
installing a legitimate copy of the original game (GOG or Steam, version 1.1)
for Vitomir to read.

Vampire: The Masquerade, Vampire: The Masquerade - Redemption, White Wolf,
World of Darkness and all related names, characters and properties are
trademarks or copyrights of their respective owners, not of this project.
Vitomir is an unofficial fan project, not affiliated with or endorsed by
them; their names are used only to say which game it runs.
