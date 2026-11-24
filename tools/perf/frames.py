#!/usr/bin/env python3
# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""Sums the files `vitomir --frame-times <file.tsv>` writes (ours).

    python3 tools/perf/frames.py <file.tsv | folder> ... [--settle N] [--worst N] [--csv out.csv] [--by-place]

Each file is one run of the game (a replayed recording, a harness run): a row for every frame. For each file
this prints how long its frames took (mean, median, the 95th and 99th percentile, the longest), how many missed
a 144 Hz, 60 Hz and 30 Hz refresh, where the mean frame's time went by phase and by the step's and the draw's
parts, what a frame allocated and what the garbage collector did, and the worst frames with their own
breakdown. The first --settle frames of each game loop (a place's loading and first uploads; 120 by default,
as the slow-frame log has it) are counted apart, as the load. --csv writes one line a file, for laying two
builds side by side. Standard library only.
"""
import csv
import gzip
import os
import sys

PARTS = ["step", "animated", "shadows", "mirrors", "world", "interface"]
AFTER = ["rest"] + PARTS + ["posed", "passed", "things", "minds", "alloc_kb", "gc0", "gc1", "gc2", "gc_pause_ms",
                            "textures_made", "buffers_rebuilt", "files_read", "read_ms", "focused"]
REFRESH = [("144 Hz", 1000 / 144), ("60 Hz", 1000 / 60), ("30 Hz", 1000 / 30)]


def read(path):
    opener = gzip.open if path.endswith(".gz") else open
    with opener(path, "rt", encoding="utf-8", newline="") as f:
        rows = list(csv.reader(f, delimiter="\t"))
    if not rows:
        return None, []
    head, body = rows[0], rows[1:]
    if head[:5] != ["run", "frame", "place", "sim_ms", "frame_ms"] or "rest" not in head:
        return None, []  # some other table of the folder (the measured runs' own runs.tsv)
    phases = head[5:head.index("rest")]
    out = []
    for r in body:
        if len(r) != len(head):
            continue  # a row cut short by a killed run
        d = dict(zip(head, r))
        for k in head:
            if k != "place":
                d[k] = float(d[k])
        out.append(d)
    return phases, out


def pct(sorted_values, p):
    """The p-th percentile by nearest rank: the value p in 100 of them are at or under."""
    if not sorted_values:
        return 0.0
    k = max(0, min(len(sorted_values) - 1, int(-(-p * len(sorted_values) // 100)) - 1))
    return sorted_values[k]


def stats(values):
    v = sorted(values)
    n = len(v)
    return {"n": n, "mean": sum(v) / n if n else 0.0, "p50": pct(v, 50), "p95": pct(v, 95), "p99": pct(v, 99),
            "max": v[-1] if n else 0.0}


def summarise(name, phases, rows, settle, worst, by_place):
    lines = []
    if not rows:
        return [f"## {name}", "", "no rows", ""], None
    # The first `settle` frames of each game loop are its load.
    seen, steady, load = {}, [], []
    for r in rows:
        k = r["run"]
        seen[k] = seen.get(k, 0) + 1
        (load if seen[k] <= settle else steady).append(r)
    s = stats([r["frame_ms"] for r in steady])
    total_s = sum(r["frame_ms"] for r in rows) / 1000
    places = []
    for r in rows:
        if r["place"] not in places:
            places.append(r["place"])
    lines += [f"## {name}", "",
              f"{len(rows)} frames in {len(seen)} game loop(s), {total_s:.1f} s of frames; places: {', '.join(places)}.",
              f"Load (the first {settle} frames of each loop): {sum(r['frame_ms'] for r in load) / 1000:.2f} s, "
              f"longest frame {max((r['frame_ms'] for r in load), default=0):.0f} ms.", ""]
    if not steady:
        return lines + ["no frames after the load", ""], None
    fps = 1000 / s["mean"] if s["mean"] else 0
    lines += ["| frames | mean ms | fps | median | p95 | p99 | longest | " + " | ".join(f"over {n}" for n, _ in REFRESH) + " |",
              "|---|---|---|---|---|---|---|" + "---|" * len(REFRESH),
              f"| {s['n']} | {s['mean']:.2f} | {fps:.0f} | {s['p50']:.2f} | {s['p95']:.2f} | {s['p99']:.2f} | {s['max']:.1f} | "
              + " | ".join(f"{sum(1 for r in steady if r['frame_ms'] > ms)} ({100 * sum(1 for r in steady if r['frame_ms'] > ms) / s['n']:.2f}%)"
                           for _, ms in REFRESH) + " |", ""]

    def table(title, cols):
        out = [f"**{title}** (ms; share of the mean frame)", "", "| part | mean | share | median | p95 | p99 | longest |", "|---|---|---|---|---|---|---|"]
        for c in cols:
            t = stats([r[c] for r in steady])
            share = 100 * t["mean"] / s["mean"] if s["mean"] else 0
            out.append(f"| {c} | {t['mean']:.3f} | {share:.1f}% | {t['p50']:.3f} | {t['p95']:.3f} | {t['p99']:.3f} | {t['max']:.2f} |")
        return out + [""]

    lines += table("Where a frame goes", phases + ["rest"])
    lines += table("Inside step+draw", PARTS)
    a = stats([r["alloc_kb"] for r in steady])
    gc = [sum(r[k] for r in steady) for k in ("gc0", "gc1", "gc2")]
    pause = sum(r["gc_pause_ms"] for r in steady)
    steady_s = sum(r["frame_ms"] for r in steady) / 1000
    lines += [f"**Memory**: a frame allocates {a['mean']:.1f} KB at the mean (median {a['p50']:.1f}, p95 {a['p95']:.1f}, most {a['max']:.0f}); "
              f"collections {gc[0]:.0f}/{gc[1]:.0f}/{gc[2]:.0f} (generation 0/1/2), {pause:.0f} ms paused "
              f"({100 * pause / (steady_s * 1000) if steady_s else 0:.2f}% of the time).",
              f"**The scene**: things {stats([r['things'] for r in steady])['mean']:.0f}, minds {stats([r['minds'] for r in steady])['mean']:.0f}, "
              f"posed {stats([r['posed'] for r in steady])['mean']:.1f} (most {max(r['posed'] for r in steady):.0f}), "
              f"passed over {stats([r['passed'] for r in steady])['mean']:.1f}; first uses after the load: "
              f"{sum(r['textures_made'] for r in steady):.0f} textures, {sum(r['buffers_rebuilt'] for r in steady):.0f} buffer rebuilds, "
              f"{sum(r['files_read'] for r in steady):.0f} files read ({sum(r['read_ms'] for r in steady):.0f} ms).", ""]
    if by_place and len(places) > 1:
        lines += ["**By place**", "", "| place | frames | mean ms | p95 | p99 | longest | step | animated | shadows | world | interface |", "|---|---|---|---|---|---|---|---|---|---|---|"]
        for p in places:
            sub = [r for r in steady if r["place"] == p]
            if not sub:
                continue
            t = stats([r["frame_ms"] for r in sub])
            m = {c: sum(r[c] for r in sub) / len(sub) for c in PARTS}
            lines.append(f"| {p} | {t['n']} | {t['mean']:.2f} | {t['p95']:.2f} | {t['p99']:.2f} | {t['max']:.1f} | {m['step']:.3f} | {m['animated']:.3f} | {m['shadows']:.3f} | {m['world']:.3f} | {m['interface']:.3f} |")
        lines.append("")
    if worst:
        lines += [f"**The {worst} longest frames after the load**", "",
                  "| frame | place | ms | " + " | ".join(phases) + " | step | animated | shadows | world | interface | alloc KB | gc | pause | textures | files |",
                  "|---|---|---|" + "---|" * (len(phases) + 10)]
        for r in sorted(steady, key=lambda r: -r["frame_ms"])[:worst]:
            lines.append(f"| {r['frame']:.0f} | {r['place']} | {r['frame_ms']:.1f} | " + " | ".join(f"{r[p]:.1f}" for p in phases)
                         + f" | {r['step']:.1f} | {r['animated']:.1f} | {r['shadows']:.1f} | {r['world']:.1f} | {r['interface']:.1f} | {r['alloc_kb']:.0f} | "
                           f"{r['gc0']:.0f}/{r['gc1']:.0f}/{r['gc2']:.0f} | {r['gc_pause_ms']:.1f} | {r['textures_made']:.0f} | {r['files_read']:.0f} |")
        lines.append("")
    row = {"run": name, "frames": s["n"], "mean_ms": round(s["mean"], 3), "fps": round(fps, 1), "p50": round(s["p50"], 3),
           "p95": round(s["p95"], 3), "p99": round(s["p99"], 3), "max": round(s["max"], 2),
           "load_s": round(sum(r["frame_ms"] for r in load) / 1000, 3), "alloc_kb": round(a["mean"], 2),
           "gc0": int(gc[0]), "gc1": int(gc[1]), "gc2": int(gc[2]), "gc_pause_ms": round(pause, 1)}
    for c in phases + ["rest"] + PARTS:
        row[c] = round(sum(r[c] for r in steady) / len(steady), 4)
    for n, ms in REFRESH:
        row["over_" + n.replace(" ", "")] = sum(1 for r in steady if r["frame_ms"] > ms)
    return lines, row


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    def opt(name, default):
        return type(default)(argv[argv.index(name) + 1]) if name in argv else default
    settle, worst, out_csv = opt("--settle", 120), opt("--worst", 8), opt("--csv", "")
    for name in ("--settle", "--worst", "--csv"):
        if name in argv:
            args.remove(argv[argv.index(name) + 1])
    files = []
    for a in args:
        if os.path.isdir(a):
            files += sorted(os.path.join(a, f) for f in os.listdir(a) if f.endswith((".tsv", ".tsv.gz")))
        else:
            files.append(a)
    if not files:
        print(__doc__)
        return 2
    table = []
    for path in files:
        phases, rows = read(path)
        if phases is None:
            continue
        name = os.path.basename(path).replace(".tsv.gz", "").replace(".tsv", "")
        lines, row = summarise(name, phases, rows, settle, worst, "--by-place" in argv)
        print("\n".join(lines))
        if row:
            table.append(row)
    if table:
        cols = ["run", "frames", "mean_ms", "fps", "p50", "p95", "p99", "max", "over_144Hz", "over_60Hz", "load_s", "step", "animated", "shadows", "world", "interface", "wait", "submit", "alloc_kb", "gc2", "gc_pause_ms"]
        cols = [c for c in cols if c in table[0]]
        print("## All runs\n\n| " + " | ".join(cols) + " |\n|" + "---|" * len(cols))
        for r in table:
            print("| " + " | ".join(str(r[c]) for c in cols) + " |")
        if out_csv:
            keys = list(table[0].keys())
            with open(out_csv, "w", newline="", encoding="utf-8") as f:
                w = csv.DictWriter(f, fieldnames=keys, extrasaction="ignore")
                w.writeheader()
                w.writerows(table)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
