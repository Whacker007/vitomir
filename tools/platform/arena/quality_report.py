#!/usr/bin/env python3
# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""Blow quality's tables, from arena runs' contacts.csv and bouts.csv:
  quality_report.py refs <run>...               each blow's q (impact speed over peak speed) over its body contacts with harm, by
                                                attacker, clip and Celerity: n, median, tenths, the share met under 0.1 of the blade
                                                (the references in Data/rules/quality.json are such medians, with --blow-quality-off)
  quality_report.py compare <root> <spec>...    quality on against off: <root>/<spec>-off and <root>/<spec>-on (MODES=off,on,hiltoff
                                                for more), per swept point: wins (Wilson 95 %), the foe's and the time-outs, the time
                                                to the stop, per fighter wounds a body blow, cuts a bout, low work, hilt, mean Q
  quality_report.py bins <run off> <run on> [attacker]   wounds a body contact by Q band, off against on"""
import sys

def refs(argv):

    import csv, sys
    def q(v, p):
        s = sorted(v); k = (len(s) - 1) * p; lo = int(k); hi = min(lo + 1, len(s) - 1); return s[lo] + (s[hi] - s[lo]) * (k - lo)
    groups = {}
    for run in argv:
        for r in csv.DictReader(open(f"{run}/contacts.csv", encoding="utf-8")):
            if r["met"] != "body" or not r["harm_kind"] or not r["peak_speed"] or float(r["peak_speed"]) <= 0 or not r["swing_clip"]: continue
            cel = "cel" if "celerity" in r.get("attacker_live_powers", "") else "-"
            key = (run.rstrip("/").split("/")[-1], r["attacker"], r["swing_clip"], cel)
            groups.setdefault(key, []).append((float(r["impact_speed"]) / float(r["peak_speed"]), float(r["along_blade"]) if r["along_blade"] else 0.0))
    print("| run | attacker | clip | Celerity | n | q median | p10 | p90 | along < 0.1 |")
    print("|---|---|---|---|---|---|---|---|---|")
    for k in sorted(groups):
        v = [x for x, _ in groups[k]]; a = [y for _, y in groups[k]]
        print(f"| {k[0]} | {k[1]} | {k[2]} | {k[3]} | {len(v)} | {q(v,.5):.3f} | {q(v,.1):.3f} | {q(v,.9):.3f} | {sum(1 for y in a if y < 0.1)/len(a):.3f} |")


def compare(argv):

    import csv, sys, math, collections
    def wilson(k, n, z=1.96):
        if n == 0: return (float('nan'),)*3
        p = k / n; d = 1 + z*z/n; c = (p + z*z/(2*n)) / d; h = z*math.sqrt(p*(1-p)/n + z*z/(4*n*n)) / d
        return p, c - h, c + h
    root = argv[0]
    import os
    MODES = os.environ.get("MODES", "off,on").split(",")
    for spec in argv[1:]:
        print(f"\n### {spec}\n")
        print("| point | quality | wins of the first (Wilson 95 %) | the second's / time | stop s | fighter | wounds / body blow | cuts / bout | low work | hilt | mean Q |")
        print("|---|---|---|---|---|---|---|---|---|---|---|")
        for mode in MODES:
            run = f"{root}/{spec}-{mode}"
            bouts = list(csv.DictReader(open(f"{run}/bouts.csv", encoding="utf-8")))
            contacts = list(csv.DictReader(open(f"{run}/contacts.csv", encoding="utf-8")))
            names = [c[:-len(".swings")] for c in bouts[0] if c.endswith(".swings")]
            sweep = [c for c in bouts[0] if c not in ("point",) and list(bouts[0]).index(c) < list(bouts[0]).index("bout") and c != "point"]
            for p in sorted({b["point"] for b in bouts}, key=int):
                bp = [b for b in bouts if b["point"] == p]
                label = ", ".join(f"{s.split('.')[-1]} {bp[0][s]}" for s in sweep) or "-"
                n = len(bp); k = sum(1 for b in bp if b["winner"] == names[0])
                w = wilson(k, n)
                stops = [float(b["kill_seconds"]) for b in bp if b["kill_seconds"]]
                cp = [c for c in contacts if c["point"] == p and c["met"] == "body" and c["harm_kind"]]
                for i, f in enumerate(names):
                    own = [c for c in cp if c["attacker"] == f]
                    wounds = sum(int(c["wounds"]) for c in own) / len(own) if own else float('nan')
                    cuts = sum(int(b[f"{f}.severances"]) for b in bp) / n
                    low = sum(1 for c in own if c["resisted_by"] == "low_work") / len(own) if own else float('nan')
                    hilt = sum(1 for c in own if c.get("hilt") == "1") / len(own) if own else float('nan')
                    qs = [float(c["quality"]) for c in own if c.get("q_ref")]
                    mq = sum(qs) / len(qs) if qs else float('nan')
                    k2 = sum(1 for b in bp if b["winner"] == names[1]); tout = sum(1 for b in bp if not b["winner"])
                    head = f"| {label} | {mode} | {k}/{n} = {100*w[0]:.1f} % ({100*w[1]:.1f}–{100*w[2]:.1f}) | {k2} / {tout} | {sum(stops)/len(stops) if stops else float('nan'):.2f} |" if i == 0 else "| | | | | |"
                    print(f"{head} {f} | {wounds:.2f} ({len(own)}) | {cuts:.3f} | {100*low:.1f} % | {100*hilt:.1f} % | {mq:.2f} |")


def bins(argv):

    import csv, sys
    off, on = argv[0:2]; who = argv[2] if len(argv) > 2 else None
    bins = [(0, 0.1), (0.1, 0.25), (0.25, 0.5), (0.5, 0.8), (0.8, 1.25), (1.25, 99)]
    def load(run):
        out = []
        for r in csv.DictReader(open(f"{run}/contacts.csv", encoding="utf-8")):
            if r["met"] != "body" or not r["harm_kind"] or not r["q_ref"]: continue
            if who and r["attacker"] != who: continue
            out.append((float(r["quality"]), int(r["wounds"]), r["severed"] != "", r["resisted_by"] == "low_work", r["hilt"] == "1"))
        return out
    A, B = load(off), load(on)
    print("| Q band | contacts off / on | share of contacts off / on | wounds a contact off / on | share of all wounds off / on | cuts off / on | low work (on) | hilt (on) |")
    print("|---|---|---|---|---|---|---|---|")
    WA = sum(w for _, w, *_ in A) or 1; WB = sum(w for _, w, *_ in B) or 1
    for lo, hi in bins:
        a = [x for x in A if lo <= x[0] < hi]; b = [x for x in B if lo <= x[0] < hi]
        f = lambda xs, i: sum(x[i] for x in xs)
        print(f"| {lo}–{hi if hi < 99 else '∞'} | {len(a)} / {len(b)} | {100*len(a)/len(A):.1f} % / {100*len(b)/len(B):.1f} % | {f(a,1)/len(a) if a else float('nan'):.2f} / {f(b,1)/len(b) if b else float('nan'):.2f} | {100*f(a,1)/WA:.1f} % / {100*f(b,1)/WB:.1f} % | {f(a,2)} / {f(b,2)} | {f(b,3)} | {f(b,4)} |")
    print(f"| all | {len(A)} / {len(B)} | | {sum(x[1] for x in A)/len(A):.2f} / {sum(x[1] for x in B)/len(B):.2f} | | {sum(x[2] for x in A)} / {sum(x[2] for x in B)} | {sum(x[3] for x in B)} | {sum(x[4] for x in B)} |")


if __name__ == "__main__":
    {"refs": refs, "compare": compare, "bins": bins}[sys.argv[1]](sys.argv[2:])
