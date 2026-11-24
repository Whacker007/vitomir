#!/usr/bin/env python3
# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""The web site's clips, made from replay tapes: the game's own pictures, and an overlay drawn from what was pressed.

A clip starts as a tape: the player plays the beats once with `--record` (the game's replay harness). `render` replays the tape
with a frame dump, so every picture is the game's own at a steady rate, and with the dump's log, a line per picture of the
keys and buttons down, the wheel, the pointer, the camera and the swivel lock. `make` then reads a recipe
(`site/media/<name>.json`) and draws its overlay on each picture, from the log, so a key lights on the very frame it went
down: a caption card, keycaps, the camera's gauge, the mouse, a ring at the pointer's tip, a padlock. It writes the clip
into `site/assets/media/`: a WebM, an MP4 (H.264), a poster and, when the recipe asks, a GIF.

    python3 tools/site/media.py render camera.tape --name camera [--fps 60]     the tape filmed into the media folder
    python3 tools/site/media.py make site/media/camera.json                      the clip from its recipe
    python3 tools/site/media.py make site/media/camera.json --frames-only DIR    the overlaid pictures only, for a look

The media folder holds what a render makes, which is large and never goes into the repository: `--media DIR`, or
`VITOMIR_MEDIA`, or `Vitomir-media` beside the repository. A render packs the pictures into one film as they come
(`raw/<name>/frames.mkv`, beside the log), so a render on the machine that recorded the tape can be made into clips on
another. A tape replays exactly only on the kind of machine that recorded it (a Windows tape on Windows: the floating
point of the two systems' maths libraries differs in the last place, and the replay check says so), so a tape is
rendered where it was recorded. `render` runs the built game (`--exe`, or the newer of the Debug and
Release builds of `src/Vitomir.Platform.Host`), which needs the game installed as any run does, and FFmpeg; `make` needs Pillow and FFmpeg.

A recipe (the site's are in `site/media/`, one per clip, the clip named after its file):

    {
      "source": "camera",                  the tape's name in the media folder (render's --name)
      "size": [1280, 720],                 the clip's size; the pictures are scaled to it, the overlay drawn at it
      "trim": [1.0, 24.0],                 seconds of the tape kept (optional)
      "gif": {"from": 3.0, "to": 9.0, "width": 640},   a GIF too, of part of it (optional)
      "webm": true,                        a WebM beside the MP4 (optional)
      "segments": [
        {"from": 1.0, "to": 4.0, "card": "title", "title": "Camera and movement", "line": "Vitomir 1.0"},
        {"from": 4.0, "to": 10.0, "tag": "original", "title": "Turn the camera", "line": "Keypad 4 and 6",
         "keys": "numpad", "gauge": true, "hold": 0.8},
        ...
      ]
    }

Segments are in the tape's seconds. `tag` is `original` (the base game's controls), `new` (Vitomir's) or `limit` (a
known limitation, kept from the original); `keys` is a cluster (`wasd`, `wasd+shift`, `numpad`, `zoom`, `ctrl`,
`shift`) or a list of key names; `gauge`, `mouse`, `ring` and `lock` add those pieces;
`hold` freezes the segment's first picture that long while its card comes in, so it can be read before anything moves.
`corner` (the recipe's or a segment's: `top-left`, the default, `top-right`, `bottom-left`, `bottom-right`) moves the card
out of the way of what the clip shows.
`keep` (a list of [from, to] spans, joined in order) cuts the filler out of a tape; `mute` (spans of the tape's seconds)
shows nothing pressed there, for a misclick; a segment's `only` (command names, such as ["Act"]) shows only those.
The keys and buttons lit are **the default keys** of the commands the log says were held or pressed, whatever the player who
recorded the tape had bound (the render writes the defaults beside its log); `"keys_as_pressed": true` shows the keys as
they were pressed instead.
`make` warns when a card is up for less time than its words take to read.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
FONTS = REPO / "assets" / "fonts"
OUT = REPO / "site" / "assets" / "media"

# The site's palette (site/assets/site.css).
GOLD = (214, 178, 90)
GOLD_DIM = (143, 106, 34)
PARCHMENT = (230, 220, 203)
MUTED = (156, 147, 132)
OXBLOOD = (138, 26, 36)
INK = (16, 11, 11)
PANEL = (13, 10, 10)

# SDL's scan codes, as the tape and the log hold them, for the keys a recipe names.
SCANCODES = {
    "A": 4, "B": 5, "C": 6, "D": 7, "E": 8, "F": 9, "G": 10, "H": 11, "I": 12, "J": 13, "K": 14, "L": 15, "M": 16,
    "N": 17, "O": 18, "P": 19, "Q": 20, "R": 21, "S": 22, "T": 23, "U": 24, "V": 25, "W": 26, "X": 27, "Y": 28, "Z": 29,
    "Space": 44, "-": 45, "=": 46, "Tab": 43,
    "Right": 79, "Left": 80, "Down": 81, "Up": 82,
    "Num 2": 90, "Num 4": 92, "Num 5": 93, "Num 6": 94, "Num 8": 96,
    "Ctrl": 224, "Shift": 225, "Alt": 226, "R Ctrl": 228, "R Shift": 229,
}
SAME = {"Ctrl": [224, 228], "Shift": [225, 229]}           # either side lights the cap
SHOWN = {"-": "–"}            # the Vitomir faces have no minus sign (U+2212); the en dash stands in for it

# Key clusters: the caps and their places on a grid of caps.
CLUSTERS = {
    "wasd": [("W", 1, 0), ("A", 0, 1), ("S", 1, 1), ("D", 2, 1)],
    "wasd+shift": [("W", 2, 0), ("Shift", 0, 1), ("A", 1, 1), ("S", 2, 1), ("D", 3, 1)],
    "numpad": [("Num 8", 1, 0), ("Num 4", 0, 1), ("Num 6", 2, 1), ("Num 2", 1, 2)],
    "zoom": [("-", 0, 0), ("=", 1, 0)],
    "ctrl": [("Ctrl", 0, 0)],
    "shift": [("Shift", 0, 0)],
}

# A card's tag: the original game's controls, Vitomir's own, or something the original does that Vitomir keeps.
TAGS = {"original": "The original game", "new": "New in Vitomir", "limit": "Known limitation"}

FADE = 0.3                 # a card's fade in and out, seconds
READ_BASE, READ_WORD = 1.5, 0.3   # a card's least time on screen: a moment, and a third of a second a word


def die(message: str) -> None:
    print(f"media: {message}", file=sys.stderr)
    sys.exit(1)


def media_root(given: str | None) -> Path:
    if given:
        return Path(given)
    if os.environ.get("VITOMIR_MEDIA"):
        return Path(os.environ["VITOMIR_MEDIA"])
    return REPO.parent / "Vitomir-media"


# ---------------------------------------------------------------------------------------------------- render

def find_exe(given: str | None) -> Path:
    if given:
        return Path(given)
    # The newest build of the two: `dotnet build` makes Debug, a Release build may be older (it was, and replayed a tape on
    # a build that had not made it).
    found = [p for config in ("Debug", "Release")
             for p in (REPO / "src" / "Vitomir.Platform.Host" / "bin" / config / "net10.0").glob("vitomir-platform*")
             if p.name in ("vitomir-platform.exe", "vitomir-platform")]
    if not found:
        die("no built vitomir-platform (build the solution, or give --exe)")
    return max(found, key=lambda p: (p.parent / "vitomir-platform.dll").stat().st_mtime if (p.parent / "vitomir-platform.dll").exists() else p.stat().st_mtime)


def render(args: argparse.Namespace) -> None:
    """The tape replayed and checked with a frame dump, its pictures packed as they come into one film, `frames.mkv`
    (H.264 at a quality that loses nothing to see, 4:4:4 so the interface's lettering stays sharp), and each picture's
    file deleted once packed, so the folder never holds more than a few: a 1080-line picture is about 4.6 MB, a minute's
    worth some 8 GB. The film, the log and `render.json` are what `make` reads, here or on another machine."""
    tape = Path(args.tape).resolve()
    if not tape.exists():
        die(f"no tape {tape}")
    if shutil.which("ffmpeg") is None:
        die("no ffmpeg on the PATH")
    raw = media_root(args.media) / "raw" / args.name
    frames = raw / "frames"
    if frames.exists():
        shutil.rmtree(frames)
    raw.mkdir(parents=True, exist_ok=True)
    log = raw / "log.jsonl"
    film = raw / "frames.mkv"
    cmd = [str(find_exe(args.exe)), "--developer", "--game", "classic", "--replay-check", str(tape),
           "--frame-dump", str(frames), "--frame-dump-fps", str(args.fps), "--frame-dump-log", str(log)]
    print("media: " + " ".join(cmd))
    game = subprocess.Popen(cmd)
    pack = subprocess.Popen(["ffmpeg", "-y", "-v", "error", "-f", "image2pipe", "-framerate", str(args.fps), "-c:v", "png",
                             "-i", "-", "-c:v", "libx264", "-crf", "10", "-preset", "veryfast", "-pix_fmt", "yuv444p",
                             str(film)], stdin=subprocess.PIPE)
    packed = 0

    def picture(n: int) -> Path:
        return frames / f"frame{n:05d}.png"

    while True:
        ended = game.poll() is not None
        here = picture(packed)
        # A picture is whole once the next one has begun, or once the game has ended.
        if here.exists() and (picture(packed + 1).exists() or ended):
            pack.stdin.write(here.read_bytes())
            here.unlink()
            packed += 1
            continue
        if ended:
            break
        time.sleep(0.02)
    pack.stdin.close()
    pack.wait()
    if frames.exists():
        shutil.rmtree(frames, ignore_errors=True)
    if game.returncode != 0:
        die("the replay did not check out the same (its own lines above say where): a tape replays on the build that "
            "recorded it, on the same kind of machine (a Windows tape on Windows), with the same copy of the game")
    if pack.returncode != 0:
        die("ffmpeg could not pack the pictures")
    lines = sum(1 for line in log.read_text(encoding="utf-8").splitlines() if line.strip()) if log.exists() else 0
    if lines != packed:
        print(f"media: warning: {packed} pictures but {lines} lines of log; a build from before the log carried on "
              "across a load writes only the last run's (rebuild, and render the tape again)")
    (raw / "render.json").write_text(json.dumps({"tape": tape.name, "fps": args.fps, "pictures": packed}, indent=1) + "\n",
                                     encoding="utf-8")
    print(f"media: {packed} pictures packed into {film} ({film.stat().st_size / 2**20:,.0f} MB), and their log beside it")


class Pictures:
    """A rendered tape's pictures in order, at the clip's size: from `frames.mkv` (decoded by ffmpeg, read forward), or
    from a folder of `frame00000.png` pictures (a render from before the film)."""

    def __init__(self, raw: Path, size: tuple[int, int]):
        self.size = size
        self.film = raw / "frames.mkv"
        self.folder = raw / "frames"
        self.at = -1
        self.last = None
        self.proc = None
        if self.film.exists():
            w, h = size
            self.proc = subprocess.Popen(["ffmpeg", "-v", "error", "-i", str(self.film), "-vf",
                                          f"scale={w}:{h}:flags=lanczos", "-f", "rawvideo", "-pix_fmt", "rgba", "-"],
                                         stdout=subprocess.PIPE)
        elif not self.folder.exists():
            die(f"no pictures in {self.film.parent} (render the tape first)")

    def get(self, n: int):
        from PIL import Image
        if self.proc is None:
            pic = Image.open(self.folder / f"frame{n:05d}.png").convert("RGBA")
            return pic.resize(self.size, Image.LANCZOS) if pic.size != self.size else pic
        if n < self.at:
            die("pictures are read forward only")
        w, h = self.size
        while self.at < n:
            data = self.proc.stdout.read(w * h * 4)
            if len(data) < w * h * 4:
                die(f"the film ends before picture {n}")
            self.last = data
            self.at += 1
        return Image.frombytes("RGBA", (w, h), self.last)

    def close(self) -> None:
        """The film's decoder stopped (a clip ends before its film does)."""
        if self.proc is not None:
            self.proc.kill()
            self.proc.wait()
            self.proc.stdout.close()


# ---------------------------------------------------------------------------------------------------- the overlay

@dataclass
class Look:
    """Fonts and sizes at the clip's scale (1 at 1080 lines)."""
    s: float
    fonts: dict = field(default_factory=dict)

    def font(self, face: str, px: float):
        from PIL import ImageFont
        key = (face, round(px))
        if key not in self.fonts:
            self.fonts[key] = ImageFont.truetype(str(FONTS / face), max(8, round(px)))
        return self.fonts[key]

    def __call__(self, v: float) -> int:
        return round(v * self.s)


def words(*texts: str | None) -> int:
    return sum(len(t.split()) for t in texts if t)


def keys_of(spec) -> list[tuple[str, int, int]]:
    if spec is None:
        return []
    if isinstance(spec, str):
        if spec not in CLUSTERS:
            die(f"no key cluster {spec!r} (there are {', '.join(CLUSTERS)})")
        return CLUSTERS[spec]
    caps = []
    for i, name in enumerate(spec):
        if name not in SCANCODES:
            die(f"no key {name!r}")
        caps.append((name, i, 0))
    return caps


def lit(name: str, down: set[int]) -> bool:
    return any(code in down for code in SAME.get(name, [SCANCODES[name]]))


def keycap(d, look: Look, x: int, y: int, name: str, on: bool) -> None:
    size = look(62)
    r = look(10)
    if on:
        d.rounded_rectangle((x - look(3), y - look(3), x + size + look(3), y + size + look(3)), r + look(3),
                            outline=GOLD + (110,), width=look(3))
        d.rounded_rectangle((x, y, x + size, y + size), r, fill=GOLD + (255,), outline=GOLD + (255,), width=look(2))
    else:
        d.rounded_rectangle((x, y, x + size, y + size), r, fill=(30, 22, 22, 235), outline=GOLD_DIM + (255,),
                            width=look(2))
    ink = INK + (255,) if on else PARCHMENT + (255,)
    if name.startswith("Num "):
        d.text((x + look(7), y + look(5)), "num", font=look.font("VitomirText-Regular.ttf", look.s * 13),
               fill=(INK if on else MUTED) + (255,))
        label = name[4:]
    else:
        label = SHOWN.get(name, name)
    face = look.font("VitomirText-SemiBold.ttf", look.s * (30 if len(label) <= 1 else 20))
    d.text((x + size / 2, y + size / 2 + look(3)), label, font=face, fill=ink, anchor="mm")


def cluster_size(look: Look, caps) -> tuple[int, int]:
    if not caps:
        return 0, 0
    step = look(70)
    return (max(c[1] for c in caps) + 1) * step - look(8), (max(c[2] for c in caps) + 1) * step - look(8)


def draw_cluster(d, look: Look, x: int, y: int, caps, down: set[int]) -> None:
    step = look(70)
    for name, cx, cy in caps:
        keycap(d, look, x + cx * step, y + cy * step, name, lit(name, down))


def arrow(d, cx: float, cy: float, angle: float, size: float, on: bool) -> None:
    """A chevron pointing along `angle` (radians, 0 right, counterclockwise up)."""
    ux, uy = math.cos(angle), -math.sin(angle)
    px, py = -uy, ux
    tip = (cx + ux * size, cy + uy * size)
    a = (cx - ux * size * 0.2 + px * size * 0.8, cy - uy * size * 0.2 + py * size * 0.8)
    b = (cx - ux * size * 0.2 - px * size * 0.8, cy - uy * size * 0.2 - py * size * 0.8)
    d.polygon([tip, a, (cx + ux * size * 0.15, cy + uy * size * 0.15), b],
              fill=(GOLD + (255,)) if on else (GOLD_DIM + (150,)))


GAUGE = 178


def gauge_size(look: Look) -> tuple[int, int]:
    return look(GAUGE + 60), look(GAUGE + 24)


def draw_gauge(d, img, look: Look, x: int, y: int, e: dict, moving: dict) -> None:
    """The camera's gauge: a compass ring turning with the heading, a horizon showing the pitch between its limits, and a
    zoom bar; chevrons light the way the camera is moving."""
    from PIL import Image, ImageDraw
    r = look(GAUGE / 2)
    cx, cy = x + look(12) + r, y + look(12) + r
    # The horizon window: sky above, ground below, the horizon line where the pitch puts it.
    inner = look(GAUGE / 2 - 22)
    disc = Image.new("RGBA", (inner * 2, inner * 2), (0, 0, 0, 0))
    dd = ImageDraw.Draw(disc)
    pitch = e["pitch"]
    hy = inner + (pitch / 90.0) * inner * 0.95          # level at the middle; looking down, the horizon climbs
    dd.rectangle((0, 0, inner * 2, hy), fill=(38, 40, 54, 255))
    dd.rectangle((0, hy, inner * 2, inner * 2), fill=(58, 38, 26, 255))
    dd.line((0, hy, inner * 2, hy), fill=GOLD + (255,), width=look(3))
    for limit in (e["pitchLow"], e["pitchTop"]):       # the pitch's limits, as ticks on the left
        ly = inner + (limit / 90.0) * inner * 0.95
        dd.line((look(4), ly, look(22), ly), fill=PARCHMENT + (220,), width=look(3))
    mask = Image.new("L", disc.size, 0)
    ImageDraw.Draw(mask).ellipse((0, 0, inner * 2 - 1, inner * 2 - 1), fill=255)
    img.paste(disc, (cx - inner, cy - inner), mask)
    # The camera's own mark, fixed at the middle.
    w = look(26)
    d.line((cx - w, cy, cx - look(8), cy, cx, cy + look(8), cx + look(8), cy, cx + w, cy), fill=GOLD + (255,),
           width=look(4), joint="curve")
    # The compass ring, turning with the heading; north is the bright tick.
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=GOLD_DIM + (255,), width=look(3))
    d.ellipse((cx - inner, cy - inner, cx + inner, cy + inner), outline=GOLD_DIM + (255,), width=look(2))
    yaw = math.radians(e["yaw"])
    for i in range(12):
        a = yaw + i * math.pi / 6 + math.pi / 2
        r0 = r - look(14 if i % 3 == 0 else 9)
        col = GOLD + (255,) if i == 0 else GOLD_DIM + (255,)
        d.line((cx + math.cos(a) * r0, cy - math.sin(a) * r0, cx + math.cos(a) * (r - look(3)),
                cy - math.sin(a) * (r - look(3))), fill=col, width=look(4 if i == 0 else 2))
    # The chevrons outside the ring: left and right for the turn, up and down for the tilt.
    o = r + look(1)
    arrow(d, cx - o, cy, math.pi, look(11), moving["left"])
    arrow(d, cx + o, cy, 0, look(11), moving["right"])
    arrow(d, cx, cy - o, math.pi / 2, look(11), moving["up"])
    arrow(d, cx, cy + o, -math.pi / 2, look(11), moving["down"])
    # The zoom bar: fuller the nearer the camera.
    bx, by0, by1 = cx + r + look(30), cy - r + look(14), cy + r - look(14)
    near = (e["zoomOut"] - e["zoom"]) / max(1e-3, e["zoomOut"] - 0.1)
    d.rounded_rectangle((bx, by0, bx + look(12), by1), look(6), outline=GOLD_DIM + (255,), width=look(2))
    fy = by1 - (by1 - by0) * max(0.0, min(1.0, near))
    d.rounded_rectangle((bx + look(3), fy, bx + look(9), by1 - look(3)), look(3), fill=GOLD + (255,))
    small = look.font("VitomirText-SemiBold.ttf", look.s * 20)
    d.text((bx + look(6), by0 - look(14)), "+", font=small, fill=(GOLD if moving["in"] else MUTED) + (255,), anchor="mm")
    d.text((bx + look(6), by1 + look(14)), "–", font=small, fill=(GOLD if moving["out"] else MUTED) + (255,), anchor="mm")


MOUSE = (74, 112)


def draw_mouse(d, look: Look, x: int, y: int, e: dict, wheel_age: tuple[int, float], flash: int) -> None:
    w, h = look(MOUSE[0]), look(MOUSE[1])
    b = e["buttons"] | flash                          # a click quicker than a picture lights its button a moment
    split = y + h * 0.42
    d.rounded_rectangle((x, y, x + w, y + h), look(34), fill=(30, 22, 22, 235), outline=GOLD_DIM + (255,), width=look(2))
    if b & 1:
        d.rounded_rectangle((x + look(3), y + look(3), x + w / 2 - look(1), split), look(30), fill=GOLD + (255,))
    if b & 4:
        d.rounded_rectangle((x + w / 2 + look(1), y + look(3), x + w - look(3), split), look(30), fill=GOLD + (255,))
    d.line((x, split, x + w, split), fill=GOLD_DIM + (255,), width=look(2))
    d.line((x + w / 2, y, x + w / 2, split), fill=GOLD_DIM + (255,), width=look(2))
    wx0, wy0 = x + w / 2 - look(6), y + look(14)
    d.rounded_rectangle((wx0, wy0, wx0 + look(12), wy0 + look(26)), look(6),
                        fill=(GOLD if b & 2 else (60, 46, 30)) + (255,), outline=GOLD_DIM + (255,), width=look(2))
    way, age = wheel_age
    if way and age < 0.4:
        fade = int(255 * (1 - age / 0.4))
        ax = x + w + look(16)
        ay = wy0 + look(13)
        arrow(d, ax, ay - look(10) if way > 0 else ay + look(10), math.pi / 2 if way > 0 else -math.pi / 2, look(10), True)
        d.text((ax + look(16), ay), "in" if way > 0 else "out", font=look.font("VitomirText-Regular.ttf", look.s * 18),
               fill=GOLD + (fade,), anchor="lm")


def draw_lock(d, look: Look, x: int, y: int, closed: bool, alpha: int) -> None:
    w, h = look(30), look(24)
    top = y + look(16)
    col = GOLD + (alpha,)
    if closed:
        d.arc((x + look(5), y, x + w - look(5), top + look(12)), 180, 360, fill=col, width=look(4))
    else:
        d.arc((x + look(5) + look(12), y - look(6), x + w - look(5) + look(12), top + look(6)), 180, 360, fill=col,
              width=look(4))
    d.rounded_rectangle((x, top, x + w, top + h), look(4), fill=col)
    d.ellipse((x + w / 2 - look(3), top + look(8), x + w / 2 + look(3), top + look(14)), fill=INK + (alpha,))


def draw_ring(d, look: Look, px: float, py: float, pulse: float | None) -> None:
    r = look(20)
    d.ellipse((px - r - look(2), py - r - look(2), px + r + look(2), py + r + look(2)), outline=INK + (200,), width=look(2))
    d.ellipse((px - r, py - r, px + r, py + r), outline=GOLD + (255,), width=look(3))
    if pulse is not None and pulse < 0.45:
        k = pulse / 0.45
        rr = r + look(30) * k
        d.ellipse((px - rr, py - rr, px + rr, py + rr), outline=GOLD + (int(255 * (1 - k)),), width=look(3))


def text_width(d, text: str, font) -> int:
    return round(d.textlength(text, font=font))


def draw_card(img, look: Look, seg: dict, alpha: float, e: dict, moving: dict, extras: dict) -> None:
    """A segment's card: its tag, title and line, the pieces it shows, and "default keys" under keys."""
    from PIL import Image, ImageDraw
    if alpha <= 0:
        return
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    W, H = img.size
    title_f = look.font("VitomirWedge-SemiBold.ttf", look.s * (64 if seg.get("card") == "title" else 42))
    line_f = look.font("VitomirText-Regular.ttf", look.s * (32 if seg.get("card") == "title" else 30))
    tag_f = look.font("VitomirText-SemiBold.ttf", look.s * 20)
    foot_f = look.font("VitomirText-Regular.ttf", look.s * 20)
    pad = look(24)
    if seg.get("card") == "title":
        tw = max(text_width(d, seg.get("title", ""), title_f), text_width(d, seg.get("line", ""), line_f))
        bw, bh = tw + pad * 4, look(64 + 30 + 78)
        x0, y0 = (W - bw) // 2, (H - bh) // 2
        d.rounded_rectangle((x0, y0, x0 + bw, y0 + bh), look(14), fill=PANEL + (215,), outline=GOLD_DIM + (255,),
                            width=look(2))
        d.text((W / 2, y0 + pad + look(34)), seg.get("title", ""), font=title_f, fill=GOLD + (255,), anchor="mm")
        if seg.get("line"):
            d.text((W / 2, y0 + pad + look(98)), seg["line"], font=line_f, fill=PARCHMENT + (255,), anchor="mm")
    else:
        caps = keys_of(seg.get("keys"))
        kw, kh = cluster_size(look, caps)
        pieces = []                                   # (width, height, draw)
        if caps:
            pieces.append((kw, kh, lambda px, py: draw_cluster(d, look, px, py, caps, set(e["keys"]))))
        if seg.get("mouse"):
            pieces.append((look(MOUSE[0] + 50), look(MOUSE[1]),
                           lambda px, py: draw_mouse(d, look, px, py, e, extras["wheel"], extras["flash"])))
        if seg.get("gauge"):
            gw, gh = gauge_size(look)
            pieces.append((gw, gh, lambda px, py: draw_gauge(d, layer, look, px, py, e, moving)))
        title, line = seg.get("title", ""), seg.get("line", "")
        tag = TAGS.get(seg.get("tag", ""), "")
        row_w = sum(p[0] for p in pieces) + look(28) * max(0, len(pieces) - 1)
        row_h = max([p[1] for p in pieces], default=0)
        tw = max(text_width(d, title, title_f), text_width(d, line, line_f),
                 text_width(d, tag, tag_f) + look(28) if tag else 0)
        bw = max(tw, row_w, look(300)) + pad * 2 + (look(44) if seg.get("lock") else 0)
        y = pad
        heights = (look(34) if tag else 0) + look(50) + (look(38) if line else 0) + \
                  (look(18) + row_h if pieces else 0) + (look(34) if caps or seg.get("mouse") else 0)
        bh = y + heights + pad - look(4)
        # The card's corner: the top left unless the recipe or the segment names another (a shop's list fills the top
        # left, so its clip puts the card over the status bar's right end, which a shop does not use).
        corner = seg.get("corner", "top-left")
        margin = look(48) if corner.startswith("top") else look(20)
        x0 = margin if corner.endswith("left") else W - bw - margin
        y0 = margin if corner.startswith("top") else H - bh - margin
        d.rounded_rectangle((x0, y0, x0 + bw, y0 + bh), look(14), fill=PANEL + (205,), outline=GOLD_DIM + (255,),
                            width=look(2))
        cy = y0 + pad
        if tag:
            tag_w = text_width(d, tag, tag_f) + look(24)
            new = seg.get("tag") == "new"
            d.rounded_rectangle((x0 + pad, cy, x0 + pad + tag_w, cy + look(26)), look(13),
                                fill=(OXBLOOD + (255,)) if new else (0, 0, 0, 0),
                                outline=(GOLD if new else MUTED) + (255,), width=look(2))
            d.text((x0 + pad + tag_w / 2, cy + look(13) + look(1)), tag, font=tag_f,
                   fill=(GOLD if new else PARCHMENT) + (255,), anchor="mm")
            cy += look(34)
        d.text((x0 + pad, cy + look(4)), title, font=title_f, fill=GOLD + (255,), anchor="la")
        if seg.get("lock"):
            closed, lock_alpha = extras["lock"]
            draw_lock(d, look, x0 + bw - pad - look(30), cy + look(6), closed, lock_alpha)
        cy += look(50)
        if line:
            d.text((x0 + pad, cy), line, font=line_f, fill=PARCHMENT + (255,), anchor="la")
            cy += look(38)
        if pieces:
            cy += look(18)
            px = x0 + pad
            for pw, ph, draw in pieces:
                draw(px, cy + (row_h - ph) // 2)
                px += pw + look(28)
            cy += row_h
        if caps or seg.get("mouse"):
            d.text((x0 + pad, cy + look(12)), "Default keys", font=foot_f, fill=MUTED + (255,), anchor="la")
    if alpha < 1:
        a = layer.getchannel("A").point(lambda v: int(v * alpha))
        layer.putalpha(a)
    img.alpha_composite(layer)


# ---------------------------------------------------------------------------------------------------- make

def load_log(path: Path) -> list[dict]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def load_defaults(raw: Path) -> dict[str, dict] | None:
    """The commands' default keys the render wrote beside its log (`log.keys.json`), by command: the first default chord of
    each, which is what the key page lists first. None for a render from before the file, which shows the keys as pressed."""
    path = raw / "log.keys.json"
    if not path.exists():
        return None
    commands = json.loads(path.read_text(encoding="utf-8"))["commands"]
    return {c["command"]: c["defaults"][0] for c in commands if c["defaults"]}


def shown(commands: set[str], defaults: dict[str, dict]) -> tuple[set[int], int]:
    """The keys (scan codes, a modifier as its left key) and mouse buttons (a bit each) of these commands' default
    chords: what the overlay lights, whatever the player who recorded the tape had bound."""
    keys: set[int] = set()
    buttons = 0
    for command in commands:
        chord = defaults.get(command)
        if chord is None:
            continue
        if "key" in chord:
            keys.add(chord["key"])
        if "button" in chord:
            buttons |= 1 << (chord["button"] - 1)
        for mod, code in (("ctrl", 224), ("shift", 225), ("alt", 226)):
            if chord.get(mod):
                keys.add(code)
    return keys, buttons


def check_reading(segments: list[dict]) -> None:
    for seg in segments:
        need = READ_BASE + READ_WORD * words(seg.get("title"), seg.get("line"), "default keys" if seg.get("keys") else "")
        up = seg["to"] - seg["from"] + seg.get("hold", 0)
        if up < need:
            print(f"media: warning: \"{seg.get('title', '')}\" is up {up:.1f} s; its words take about {need:.1f} s to read")


def make(args: argparse.Namespace) -> None:
    from PIL import Image, ImageDraw
    recipe_path = Path(args.recipe)
    recipe = json.loads(recipe_path.read_text(encoding="utf-8"))
    name = recipe_path.stem
    raw = media_root(args.media) / "raw" / recipe["source"]
    log = load_log(raw / "log.jsonl")
    rendered = json.loads((raw / "render.json").read_text(encoding="utf-8")) if (raw / "render.json").exists() else {}
    fps = rendered.get("fps", 60)
    if rendered.get("pictures", len(log)) != len(log):
        die(f"{raw.name}: {rendered['pictures']} pictures but {len(log)} lines of log, so they cannot be matched (a load "
            "in the tape, rendered by a build from before the log carried on across one): render the tape again")
    W, H = recipe.get("size", [1280, 720])
    look = Look(H / 1080)
    segments = sorted(recipe.get("segments", []), key=lambda s: s["from"])
    for seg in segments:
        if "corner" in recipe and "corner" not in seg:
            seg["corner"] = recipe["corner"]
        if seg.get("corner", "top-left") not in ("top-left", "top-right", "bottom-left", "bottom-right"):
            die(f"no corner {seg['corner']!r} (top-left, top-right, bottom-left or bottom-right)")
    check_reading(segments)
    # The seconds of the tape kept: `keep`, a list of [from, to] spans joined in order (the filler between them cut), or
    # `trim`, one span; the whole tape when neither.
    keep = recipe.get("keep") or [recipe.get("trim", [0, len(log) / fps])]
    # Seconds of the tape whose presses are not shown (a misclick, a wrong key): nothing lights, no ring pulses.
    mute = [tuple(m) for m in recipe.get("mute", [])]

    # The clip's timeline: each picture of the kept seconds once, a segment's first held for its `hold`.
    timeline: list[int] = []
    holds = {round(s["from"] * fps): round(s.get("hold", 0) * fps) for s in segments if s.get("hold")}
    for t0, t1 in keep:
        for i in range(round(t0 * fps), min(len(log), round(t1 * fps))):
            timeline += [i] * (1 + holds.get(i, 0))

    # Each segment's span in the clip's own seconds, holds included, so a card fades in while its picture is held.
    spans = []
    for seg in segments:
        a, b = round(seg["from"] * fps), round(seg["to"] * fps)
        ks = [k for k, i in enumerate(timeline) if a <= i < b]
        if ks:
            spans.append((seg, ks[0] / fps, (ks[-1] + 1) / fps))

    def seg_at(t: float):
        for seg, start, end in spans:
            if start <= t < end:
                return seg, max(0.0, min(1.0, (t - start) / FADE, (end - t) / FADE))
        return None, 0.0

    frames_out = Path(args.frames_only) if args.frames_only else None
    if frames_out:
        frames_out.mkdir(parents=True, exist_ok=True)
    out = Path(args.out) if args.out else OUT
    out.mkdir(parents=True, exist_ok=True)
    master = (frames_out or out) / f"{name}.master.mkv"
    enc = None
    if not frames_out:
        enc = subprocess.Popen(["ffmpeg", "-y", "-v", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}",
                                "-r", str(fps), "-i", "-", "-c:v", "libx264", "-crf", "8", "-preset", "veryfast",
                                "-pix_fmt", "yuv444p", str(master)], stdin=subprocess.PIPE)

    pictures = Pictures(raw, (W, H))
    defaults = None if recipe.get("keys_as_pressed") else load_defaults(raw)
    command_at: dict[str, float] = {}                 # when each command was last pressed, for its moment of light
    wheel = (0, 9.0)
    last_press: float | None = None
    pressed_at = {1: -9.0, 2: -9.0, 4: -9.0}          # when each button was last pressed, for its moment of light
    lock_was, lock_change = False, -9.0
    prev = None
    for k, i in enumerate(timeline):
        e = log[i]
        t_clip = k / fps
        held = k > 0 and timeline[k - 1] == i
        pic = pictures.get(e["n"]).copy()
        moving = {"left": False, "right": False, "up": False, "down": False, "in": False, "out": False}
        if prev is not None and not held:
            dyaw = (e["yaw"] - prev["yaw"] + 180) % 360 - 180
            moving.update(left=dyaw > 0.05, right=dyaw < -0.05, up=e["pitch"] < prev["pitch"] - 0.05,
                          down=e["pitch"] > prev["pitch"] + 0.05, **{"in": e["zoom"] < prev["zoom"] - 1e-4,
                                                                     "out": e["zoom"] > prev["zoom"] + 1e-4})
        seg, alpha = seg_at(t_clip)
        tape_t = i / fps
        muted = any(a <= tape_t < b for a, b in mute)
        only = set(seg["only"]) if seg is not None and "only" in seg else None

        def wanted(commands) -> set[str]:
            """The commands to show: none while muted, and only the segment's own when it names them."""
            if muted:
                return set()
            return {c for c in commands if only is None or c in only}

        if not held:
            if e["wheel"] and not muted:
                wheel = (1 if e["wheel"] > 0 else -1, 0.0)
            else:
                wheel = (wheel[0], wheel[1] + 1 / fps)
            for command in wanted(e.get("pressed", [])):
                command_at[command] = t_clip
            if defaults is not None:
                # What was done, shown with its default keys: the commands held, and those pressed a moment ago.
                _, clicks = shown(wanted(e.get("pressed", [])), defaults)
            else:
                clicks = 0 if muted else e.get("clicks", 0) | (e["buttons"] & ~(prev["buttons"] if prev else 0))
            for bit in pressed_at:
                if clicks & bit:
                    pressed_at[bit] = t_clip
            if clicks & 1:
                last_press = t_clip
            if prev is not None and e["lock"] != lock_was:
                lock_change = t_clip
            lock_was = e["lock"]
        lock_alpha = 255 if e["lock"] or t_clip - lock_change < 1.0 else 0
        flash = sum(bit for bit, at in pressed_at.items() if t_clip - at < 0.3)
        if defaults is not None:
            recent = {c for c, at in command_at.items() if t_clip - at < 0.3}
            keys_shown, buttons_shown = shown(wanted(set(e.get("held", [])) | recent), defaults)
            e = dict(e, keys=sorted(keys_shown), buttons=buttons_shown)
        extras = {"wheel": wheel, "lock": (e["lock"], lock_alpha), "flash": flash}
        if seg is not None and seg.get("ring") and e.get("cursor", True):
            d = ImageDraw.Draw(pic)
            sx, sy = W / max(1, e["windowWidth"]), H / max(1, e["windowHeight"])
            pulse = None if last_press is None else t_clip - last_press
            draw_ring(d, look, e["pointerX"] * sx, e["pointerY"] * sy, pulse)
        if seg is not None:
            draw_card(pic, look, seg, alpha, e, moving, extras)
        if frames_out:
            pic.convert("RGB").save(frames_out / f"clip{k:05d}.png")
        else:
            enc.stdin.write(pic.convert("RGB").tobytes())
        if not held:
            prev = e
    pictures.close()
    if frames_out:
        print(f"media: {len(timeline)} pictures in {frames_out}")
        return
    enc.stdin.close()
    if enc.wait() != 0:
        die("ffmpeg could not write the master")
    encode(master, out / name, recipe)
    master.unlink()


def encode(master: Path, base: Path, recipe: dict) -> None:
    def ff(*a: str) -> None:
        if subprocess.run(["ffmpeg", "-y", "-v", "error", *a]).returncode != 0:
            die("ffmpeg failed: " + " ".join(a))
    ff("-i", str(master), "-c:v", "libx264", "-crf", "27", "-preset", "slow", "-pix_fmt", "yuv420p",
       "-movflags", "+faststart", "-an", f"{base}.mp4")
    # The MP4 alone by default: every browser plays it, and at these clips' sizes the WebM came out no smaller, so a
    # second copy would only double what the repository carries. `"webm": true` makes one as well.
    if recipe.get("webm"):
        encoders = subprocess.run(["ffmpeg", "-v", "error", "-encoders"], capture_output=True, text=True).stdout
        if "libsvtav1" in encoders:
            ff("-i", str(master), "-c:v", "libsvtav1", "-crf", "36", "-preset", "6", "-pix_fmt", "yuv420p", "-an",
               f"{base}.webm")
        else:
            print("media: warning: this FFmpeg has no libsvtav1 encoder; no WebM made, the MP4 serves alone")
    # The poster written bit-exact, with no comment naming the encoder (the export would take one out).
    ff("-ss", str(recipe.get("poster", 0.5)), "-i", str(master), "-frames:v", "1", "-q:v", "3", "-map_metadata", "-1",
       "-fflags", "+bitexact", "-flags:v", "+bitexact", f"{base}.jpg")
    if not Path(f"{base}.jpg").exists():
        die(f"no poster: {recipe.get('poster')} s is past the clip's end (`poster` is in the clip's seconds)")
    if gif := recipe.get("gif"):
        width = gif.get("width", 640)
        ff("-ss", str(gif["from"]), "-to", str(gif["to"]), "-i", str(master), "-vf",
           f"fps=15,scale={width}:-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];"
           f"[b][p]paletteuse=dither=sierra2_4a", "-loop", "0", f"{base}.gif")
    for ext in ("webm", "mp4", "jpg", "gif"):
        p = Path(f"{base}.{ext}")
        if p.exists():
            print(f"media: {p}  {p.stat().st_size / 1024:,.0f} KB")


def main() -> None:
    ap = argparse.ArgumentParser(description="The web site's clips, made from replay tapes.")
    ap.add_argument("--media", help="the media folder (default: VITOMIR_MEDIA, or Vitomir-media beside the repository)")
    sub = ap.add_subparsers(dest="command", required=True)
    r = sub.add_parser("render", help="a tape filmed into the media folder")
    r.add_argument("tape")
    r.add_argument("--name", required=True)
    r.add_argument("--fps", type=int, default=60)
    r.add_argument("--exe")
    m = sub.add_parser("make", help="a clip from its recipe")
    m.add_argument("recipe")
    m.add_argument("--frames-only", help="write the overlaid pictures here instead of encoding")
    m.add_argument("--out", help="where the clip goes (default: site/assets/media), for a draft to look at first")
    args = ap.parse_args()
    {"render": render, "make": make}[args.command](args)


if __name__ == "__main__":
    main()
