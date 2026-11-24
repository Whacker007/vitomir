# Original content policy

Vitomir is our own code. It holds no content of the game and none of the original program's code. Concretely:

1. **Nothing the game installs is in this repository or in Vitomir's package.** None of the game's files, and nothing
   converted from them: archives, levels, models, animations, textures, sounds, music, movies, the interface's art,
   scripts, templates, the SDK, the manual. Vitomir is a new engine, and it reads what it needs from the player's own copy
   of the game (rule 2). `.gitignore` refuses the game's file types; an exception would need a careful review (there is
   none).
2. **Vitomir reads the player's own copy.** Its setup finds the player's install of patch 1.1 (GOG or Steam), checks each
   file Vitomir reads against a list of their names, sizes and SHA-256 fingerprints, and imports them into the player's own
   user folder on the player's machine, once; the install is only read, never changed. The game is then played from that
   copy.
3. **The scripts' bridge is generated, not stored.** The bridge classes the game's Java scripts are compiled against are
   made at load time from the SDK's sources in the player's own install, into the player's script cache
   (`src/Vitomir.Scripting/CodexBridge.cs`); the game's copy-protection hook is never bridged. The aim is to write them
   instead from the SDK's published description of that interface, so that the repository carries only our own
   implementation of it.
4. **None of the original program's code is in Vitomir**, in any form. Vitomir does what the game does, written from
   scratch.
5. **Community mods and tools are reference points, not sources**, unless their licence is explicit and compatible and the
   borrowing is recorded in `docs/THIRD_PARTY.md`.
6. **What Vitomir ships may be keyed to the player's files, never made of them.** Vitomir may ship its own corrections and
   conversion rules for the player's own game files — keyed to each file by its SHA-256 — which hold none of the game's
   data and do nothing without it; never the game's data in any form. A file that does not match its key fails the import
   rather than playing something else. The setup's list of the game's files (`src/Vitomir.Platform/Setup/Manifests`) is
   the same kind of thing: the names, sizes and SHA-256s of 1.1's files, which the setup checks the player's install
   against; it holds none of their bytes.
7. **The licence makes it a condition for everyone else too.** The Vitomir Licence (`LICENSE`, *The Original Game*)
   forbids distributing any of the game's content, or files converted from it, with Vitomir, with anything made from it
   or with a module, and using any of it to help anyone get the game other than by buying it; doing so ends the licence.
   Please keep this rule: it protects everyone's work.
8. **Other people's mods are theirs.** The licence (*Where Vitomir Comes From*) lets a module be published only with the
   publisher's own work in it, or work its owners have allowed to be shared: a mod made by someone else, made into a
   module by the launcher's import or changed, still needs its author's permission. The import itself keeps the pack in
   the player's own user folder and shares nothing.
9. **Screenshots and short clips of play are not the game's files.** The web site's pictures and clips
   (`site/assets/media`) are recorded while playing with Vitomir, as anyone streaming or reviewing a game records it, to
   show what Vitomir does and how it differs from the original. They are short, they contain none of the game's files, and
   footage of the game's own movies is kept to a few seconds. We take this to be fair use, as it is for those who stream
   the game; a rights holder who sees it otherwise can ask, and the material comes down.
