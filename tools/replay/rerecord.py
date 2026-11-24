#!/usr/bin/env python3
# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""Record a replay tape again from its own launch.

When a change to Vitomir alters what the game does *on purpose*, the baseline tapes' digests no longer
match and must be recorded again. A tape's `arg` lines are the command line it was recorded with
(the harness's --ui-click included, and the --seed it ran on), so running them with --record makes the
same run on the new build. Each is recorded by the platform's program as the baselines are replayed
(`vitomir-platform --game classic --developer [--oneone <install>]`, the install from --game or VTMR_GAME_DIR):

    python3 tools/replay/rerecord.py src/Vitomir.Engine.Tests/Replays/*.tape [--platform path/to/vitomir-platform] [--game <install>]

Use it only for a deliberate change: a divergence nobody meant is the harness doing its job.

Each run is given a player's folder of its own (VITOMIR_USERDATA set to a temporary folder), so no
setting, key binding or save of this machine's gets into a tape, and a tape that saves
(Replays/save-load.tape presses the quick save) never writes into the slots of whoever runs this: the
baselines are recorded on Vitomir's defaults, and the switches each one names on its own command line.
One default is not left to the build: **the interface's layout**, which places every click. A tape that names
no --ui-layout is recorded again in the layout it was made in (its `interface` line; none, 1.1's stretch), so
a baseline's clicks land where they did whatever the default has become since.
A tape that carries the saves it began with (a `saves` line in its head) begins again with the same saves: the temporary folder's game
Saves (`Modules/classic/Saves`) is a copy of the `<tape>.saves/` beside the tape (empty when there is none).
"""
import argparse
import gzip
import os
import shutil
import subprocess
import sys
import tempfile
import urllib.parse


def layout_of(layout, sheet, size):
    """The --ui-layout a tape was recorded in, from its `interface` line's words.

    No line at all: 1.1's stretch, the only layout there was. `wide` with `sheet=16x9`: wide.
    `wide` alone is the earlier builds', which was 1.1's stretch on a picture narrower than 16:9 and wide from there on;
    with no size to tell by (a tape that names none), wide.
    """
    if layout is None:
        return "stretch"
    if layout != "wide" or sheet == "16x9" or size is None:
        return layout
    width, height = size
    return "stretch" if height * 16 >= width * 9 + 16 else "wide"


def launch_of(tape):
    """The tape's launch: its arguments (with the layout it was recorded in, unless they name one), and whether it
    carries the saves it began with."""
    args = []
    carries = False
    layout = sheet = size = None
    with open(tape, "rb") as raw:
        compressed = raw.read(2) == b"\x1f\x8b"   # a tape named .gz is written gzip
    with (gzip.open(tape, "rt", encoding="utf-8") if compressed else open(tape, encoding="utf-8")) as f:
        if f.readline().strip() not in ("vitomir-tape 1", "vitomir-tape 2"):   # 2: the neutral events
            sys.exit(f"{tape}: not a Vitomir replay tape")
        for line in f:
            if line.startswith("frame"):
                break
            if line.startswith("saves "):
                carries = True
            words = dict(w.split("=", 1) for w in line.split()[1:] if "=" in w)
            if line.startswith("interface "):
                layout, sheet = words.get("layout"), words.get("sheet")
            if line.split(" ", 1)[0] in ("window", "interactive") and size is None and "x" in words.get("size", ""):
                size = tuple(int(n) for n in words["size"].split("x"))
            if line.startswith("arg "):
                field = line[4:].rstrip("\n")
                args.append("" if field == "%" else urllib.parse.unquote(field))
    # A click is placed by the layout, and the default layout has changed since most tapes were made (fit,
    # wide, and wide's sheet shorter than a picture narrower than 16:9): the tape's own, said.
    if "--ui-layout" not in args:
        args += ["--ui-layout", layout_of(layout, sheet, size)]
    return args, carries


def saves_of(tape):
    """The folder a tape's saves are kept in: the tape's name, less a .gz, with .saves (TapeSaves.Beside)."""
    return (tape[:-3] if tape.lower().endswith(".gz") else tape) + ".saves"


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("tapes", nargs="+")
    here = os.path.dirname(os.path.abspath(__file__))
    exe = "vitomir-platform.exe" if os.name == "nt" else "vitomir-platform"
    ap.add_argument("--platform", default=os.path.join(here, "..", "..", "src", "Vitomir.Platform.Host", "bin", "Release", "net10.0", exe))
    ap.add_argument("--game", default=os.environ.get("VTMR_GAME_DIR"), help="the 1.1 install (else the setup's imported data)")
    ap.add_argument("--dry-run", action="store_true", help="print the command lines only")
    a = ap.parse_args()
    for tape in a.tapes:
        tape = os.path.abspath(tape)
        launch, carries = launch_of(tape)
        head = ["--game", "classic", "--developer"] + (["--oneone", a.game] if a.game else [])
        cmd = [os.path.abspath(a.platform), *head, *launch, "--record", tape]
        print(" ".join(cmd), flush=True)
        kept = carries and os.path.isdir(saves_of(tape))
        print(f"  on a player's folder of its own, its Saves copied from {saves_of(tape)}" if kept
              else "  on a player's folder of its own, with no saves", flush=True)
        if a.dry_run:
            continue
        with tempfile.TemporaryDirectory(prefix="vitomir-rerecord-") as user:
            # Copied before the run: the recording replaces the folder beside the tape with what it began with.
            if kept:
                shutil.copytree(saves_of(tape), os.path.join(user, "Modules", "classic", "Saves"))
            subprocess.run(cmd, cwd=os.path.dirname(os.path.abspath(a.platform)), check=True,
                           env={**os.environ, "VITOMIR_USERDATA": user})


if __name__ == "__main__":
    main()
