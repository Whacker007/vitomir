#!/usr/bin/env python3
# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""Films side by side, each with its caption (who judges a motion by looking).

The arena's --film-at writes a film's frames to <out>/film/<name>/f0000.png ...; this puts
several such films in one video, in a grid, each cell captioned, at real time or slowed down:

    film_side.py --out shield.mp4 --cols 3 [--fps 120] [--speed 0.25] [--cell 480x270] [--title "text"] \
        "small=/runs/small/film/bout0-first" "default=/runs/default/film/bout0-first" "large=/runs/large/film/bout0-first"

A cell is CAPTION=FOLDER (a folder of f%04d.png frames). --fps is the frames' rate of the bout's time (the arena's default,
120: a frame a tick); --speed 1 is real time (written at 60 frames a second), 0.25 a quarter of it. The films are cut to the
shortest. Needs ffmpeg (drawtext, xstack) on the path; a font is looked for among the usual places (--font to give one).
"""
import argparse
import glob
import os
import shutil
import subprocess
import sys

FONTS = ["/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
         "C:/Windows/Fonts/arialbd.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf"]


def esc(text):
    # drawtext's own escaping: the text is in single quotes inside the filter graph.
    return text.replace("\\", "\\\\").replace("'", "\u2019").replace(":", "\\:").replace("%", "\\%").replace(",", "\\,")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--out", required=True)
    p.add_argument("--cols", type=int, default=0, help="cells across (default: all in one row)")
    p.add_argument("--fps", type=float, default=120.0, help="the frames' rate of the bout's time (default 120: one a tick)")
    p.add_argument("--speed", type=float, default=1.0, help="1 real time, 0.25 a quarter of it")
    p.add_argument("--cell", default="480x270")
    p.add_argument("--title", default="", help="a line over the whole grid")
    p.add_argument("--font", default="")
    p.add_argument("--gif", action="store_true", help="also a small gif beside the video")
    p.add_argument("cells", nargs="+", help="CAPTION=FOLDER")
    a = p.parse_args()
    if shutil.which("ffmpeg") is None:
        sys.exit("film_side: ffmpeg is not on the path")
    font = a.font or next((f for f in FONTS if os.path.exists(f)), "")
    cw, ch = (int(v) for v in a.cell.lower().split("x"))
    cells = []
    for c in a.cells:
        cap, _, folder = c.partition("=")
        frames = sorted(glob.glob(os.path.join(folder, "f[0-9][0-9][0-9][0-9].png")))
        if not frames:
            sys.exit(f"film_side: no frames f0000.png... in {folder}")
        cells.append((cap, folder, len(frames)))
    n = min(c[2] for c in cells)
    cols = a.cols or len(cells)
    rows = (len(cells) + cols - 1) // cols
    rate = a.fps * a.speed
    out_rate = 60 if a.speed >= 0.5 else max(15, min(60, round(rate)))
    args = ["ffmpeg", "-y", "-loglevel", "error"]
    for cap, folder, _ in cells:
        args += ["-framerate", f"{rate:g}", "-i", os.path.join(folder, "f%04d.png")]
    fontopt = f":fontfile='{font}'" if font else ""
    graph = []
    for i, (cap, _, _) in enumerate(cells):
        graph.append(f"[{i}:v]trim=end_frame={n},setpts=PTS-STARTPTS,scale={cw}:{ch},"
                     f"drawtext=text='{esc(cap)}'{fontopt}:fontsize={max(14, ch // 13)}:fontcolor=white:box=1:boxcolor=black@0.6:boxborderw=6:x=10:y=h-th-12[c{i}]")
    # Empty cells fill the grid's last row.
    for i in range(len(cells), rows * cols):
        graph.append(f"color=c=black:s={cw}x{ch}:r={rate:g},trim=end_frame={n}[c{i}]")
    layout = "|".join(f"{(i % cols) * cw}_{(i // cols) * ch}" for i in range(rows * cols))
    stack = "".join(f"[c{i}]" for i in range(rows * cols))
    if rows * cols > 1:
        graph.append(f"{stack}xstack=inputs={rows * cols}:layout={layout}[g]")
    else:
        graph.append("[c0]null[g]")
    title = a.title + ("" if a.speed == 1 else f"   (x{a.speed:g} speed)")
    if title:
        graph.append(f"[g]pad=iw:ih+{ch // 7}:0:{ch // 7}:black,drawtext=text='{esc(title)}'{fontopt}:fontsize={max(14, ch // 12)}:fontcolor=yellow:x=10:y=8[t]")
        last = "[t]"
    else:
        last = "[g]"
    graph.append(f"{last}fps={out_rate},format=yuv420p[v]")
    args += ["-filter_complex", ";".join(graph), "-map", "[v]", "-c:v", "libx264", "-crf", "20", a.out]
    subprocess.run(args, check=True)
    if a.gif:
        gif = os.path.splitext(a.out)[0] + ".gif"
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", a.out, "-vf",
                        "fps=20,scale=720:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=128[p];[b][p]paletteuse", gif], check=True)
    print(f"film_side: {a.out}: {len(cells)} films, {n} frames each, {cols}x{rows}, {a.speed:g}x speed")


if __name__ == "__main__":
    main()
