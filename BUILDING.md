# Building Vitomir

This page is for building Vitomir from its source. To play, the release zip is enough: unzip it into a folder of its own,
start `vitomir-launcher.exe`, and its Setup page does the rest.

## What you need

- **Windows 10 or 11, 64-bit.** The game is made and packaged on Windows. The tree also builds and its tests run on Linux,
  which is how parts of it are developed, but a Linux build is not a supported way to play.
- **The .NET 10 SDK**: the version `global.json` names, or a later feature release of it.
- **Your own copy of *Vampire: The Masquerade – Redemption*, patch 1.1** (GOG or Steam), installed. The build does not
  need it; most of the tests and the game itself do.
- An internet connection for the first build. It fetches:
  - the NuGet packages (`docs/THIRD_PARTY.md` lists them and their licences);
  - the Eclipse Compiler for Java (ECJ 4.6.1) from Maven Central, into `lib/` (which git ignores), which compiles the game's
    Java scripts when the game loads them;
  - on Windows, the DirectX Shader Compiler, as a NuGet package. On another system, put `dxc` on the `PATH` or name it in
    `DXC_PATH`; without it the build warns and there are no shaders to draw with.

Nothing of the game is in this repository, and `.gitignore` refuses its file types (`docs/ORIGINAL_CONTENT_POLICY.md`).

## Build

From the repository's folder:

```
dotnet build src/Vitomir.slnx -c Release
```

**A warning fails the build**, in every project: the compiler's, the nullable checks', xUnit's analyzers' and .NET's code
analysis rules (`TreatWarningsAsErrors` in `src/Directory.Build.props`). Where a rule's fix would change what the program
does, the rule is turned off for that place only, with the reason on the same line (`#pragma warning disable CAxxxx //
why`). Code style (`.editorconfig`) stays a suggestion.

The build is for the machine that runs it (`UseCurrentRuntimeIdentifier`): without that, every platform's native libraries
would be copied into every project, about 3 GB.

## Test

```
dotnet test src/Vitomir.slnx -c Release --no-build
```

- **The game's files.** The tests that need them read your install from the folder `VTMR_GAME_DIR` names, else
  `C:\Games\VTMR`, and skip without one. One test fails instead of skipping, so that a run with no game is not mistaken for
  a passing one; set `VTMR_NO_GAME_DATA=1` when a run without the game is what you mean.
- **The replay tests** play recorded runs through the built program and compare them with their baselines. They are the
  slowest; `VITOMIR_NO_REPLAYS=1` leaves them out of a quick run (never a final one).
- **The timing tests** hold a measured time to a bound, so they run apart from the rest, one at a time, where nothing else
  competes. The plain run above leaves them out; run them after it:

  ```
  dotnet test src/Vitomir.slnx -c Release --no-build -m:1 --filter "Category=Timing" --settings src/Timing.runsettings
  ```

- **The rendering tests** need a GPU, or a software one. A software device is more forgiving than a real driver, so a green
  rendering run on one is not the last word.

## Run

```
dotnet run --project src/Vitomir.Launcher -c Release
```

On its first start the launcher opens on **Setup**: it finds your copy of the game, checks its files against 1.1's, imports
them into `%LOCALAPPDATA%\Vitomir` (about 1.3 GB) and makes the recooked base game beside them (the templates, scenes and
camera paths as text, and the text data files laid out). Your install is only read, never changed. Then **Play**. Your
settings, saves and logs live in the same folder.

The developer's tools are `vitomir-tools`; `vitomir-tools --help` lists them with what each does.

## Package

The zip a player unzips, `Vitomir-<version>-win-x64.zip`, with `vitomir-launcher.exe` at the top of its `Vitomir` folder and
the rest in `bin`:

```
powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Out <folder>
```

It builds the launcher, the platform and the Classic module for this machine, and refuses to package any of the game's file
types. `tools\package-platform.ps1` makes a developer's package with the enhanced module as well.

## The web site

The site's pages are Markdown in `site/pages/`, laid into `site/template.html`; `site/site.json` lists them in the menu's
order. To build it into `out/site` (open `out/site/index.html` in a browser to look at it):

```
dotnet run tools/site/build.cs
```

`--strict` refuses while a page still has a place marked **Fill in**; the published site is built with it. It is
published by GitHub Actions (`.github/workflows/pages.yml`) on each push to `main` that changes the site, to
`https://whacker007.github.io/vitomir/`.

The site's clips are made from replay tapes by `tools/site/media.py` (Python with Pillow, and FFmpeg): `render` replays a
tape with a frame dump, which needs the game installed as any run does, and `make` draws a recipe's overlay (the keys,
the camera's gauge, the captions) on the pictures and encodes the clip into `site/assets/media/` (an MP4 and its poster;
the recipes are in `site/media/`). The script's own help
says how.

## Where things are

- `src/` — the solution, `Vitomir.slnx`:
  - `Vitomir.Formats` reads the original game's file formats;
  - `Vitomir.Engine` is the game: the world, the actors, the rules, the interface's logic, saves and multiplayer;
  - `Vitomir.Render` and `Vitomir.Platform.Render` draw it (SDL3's GPU API: Direct3D 12 or Vulkan);
  - `Vitomir.Scripting` runs the game's own Java scripts (ECJ on IKVM);
  - `Vitomir.Classic` and `Vitomir.Modules.*` are the game's modules, `Vitomir.Platform*` the platform that plays them,
    `Vitomir.Launcher` the launcher and `Vitomir.Tools` the tools;
  - each `*.Tests` project holds its library's tests.
- `assets/` — Vitomir's own typefaces (SIL Open Font License 1.1, `assets/fonts/OFL.txt`).
- `tools/` — the packaging scripts, the font and test-scene builders, and the measured runs.

## Licences

Vitomir is under the Vitomir Licence 1.0 (`LICENSE`; the README says what it means in plain words). Its own typefaces are
under the SIL Open Font License 1.1. The components it builds on and their licences are listed in `docs/THIRD_PARTY.md`. The game's own content is covered
by neither and is never distributed here.
