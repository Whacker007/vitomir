# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""
The platform's swing simulator (src/Vitomir.Platform/Animation/People/SwingRule.cs) checked against the models repository's reference.
The platform's test SwingTests.A_run_for_the_reference, with VITOMIR_SWING_DUMP=<folder>, writes a run of a crowd
person's swing chains (swing-<person>.json: the skeleton's rest, the record as the port runs it, every 60 Hz step's body and the port's tails,
in the models repository's skeleton space); this steps swing.py's Swing over the same bodies and says how far apart the two are, step by step.

    python3 tools/platform/swing_compare.py --models <the models repository> <folder or files>
"""
import argparse, glob, json, os, sys
import numpy as np

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--models", default=os.environ.get("VITOMIR_MODELS_DIR", ""), help="the character models' folder (its swing.py, the reference swing-bones simulator)")
    ap.add_argument("runs", nargs="+")
    a = ap.parse_args()
    sys.path.insert(0, os.path.join(a.models, "tools", "anim"))
    from swing import Swing
    files = [f for r in a.runs for f in (sorted(glob.glob(os.path.join(r, "swing-*.json"))) if os.path.isdir(r) else [r])]
    worst_all = 0.0
    for path in files:
        d = json.load(open(path))
        names, parents = d["names"], d["parents"]
        rest = np.array(d["rest"], float).reshape(-1, 4, 4)
        sim = Swing(names, parents, rest, d["spec"])
        sim.set_lengths(d["lengths"]); sim.ground = d["ground"]
        order = [sim.I[n] for n in d["order"]]
        dt = d["dt"]; worst = 0.0; per_clip = {}; first_far = None
        for k, st in enumerate(d["steps"]):
            body = {n: np.array(m, float).reshape(4, 4) for n, m in zip(names, st["body"])}
            if k == 0: sim.reset(body)
            else: sim.step(body, dt)
            ref = np.array([sim.tail[i] for i in order]); port = np.array(st["tails"], float)
            diff = float(np.max(np.linalg.norm(ref - port, axis=1)))
            worst = max(worst, diff); per_clip[st["clip"]] = max(per_clip.get(st["clip"], 0.0), diff)
            if first_far is None and diff > 1e-3: first_far = k
        worst_all = max(worst_all, worst)
        print(f"{d['person']}: {len(d['steps'])} steps of {len(order)} bones; the tails at most {worst * 1000:.4f} mm apart "
              f"({', '.join(f'{c} {v * 1000:.4f}' for c, v in per_clip.items())})"
              + (f"; first over 1 mm at step {first_far}" if first_far is not None else ""))
    print(f"largest difference: {worst_all * 1000:.4f} mm")

if __name__ == "__main__":
    main()
