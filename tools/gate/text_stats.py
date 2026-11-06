#!/usr/bin/env python3
"""Similarity of our extracted text with Apache Tika's, per file (information; Tika runs as a black box).

    text_stats.py OURS_DIR TIKA_DIR EXT [--worst N] [--json FILE]

OURS_DIR holds NAME.txt (GateProbe text), TIKA_DIR holds NAME.EXT.txt or NAME.txt (the Tika application run over the same files).
Three measures per file:
  sim        Dice coefficient of character 5-gram multisets of the lower-cased text with all white space removed, so line, tab and
             paragraph conventions do not count (1.0 when both sides are empty);
  precision  the share of our word tokens (letters and digits) that Tika's text also has, as a multiset: is anything of ours invented?
  recall     the share of Tika's word tokens that contain no digit that ours also has: workbooks leave numbers out of their text by
             design (docs/text-extraction.md), so numbers are left out of this measure.
Prints one JSON object: files, then mean, median and 5th percentile of each measure, counts of files under thresholds, files where exactly
one side is empty, and the worst files by sim.
"""
import json
import re
import sys
from collections import Counter
from pathlib import Path


def grams(text, n=5):
    s = "".join(text.lower().split())
    if len(s) < n:
        return Counter([s]) if s else Counter()
    return Counter(s[i:i + n] for i in range(len(s) - n + 1))


def dice(a, b):
    ta, tb = sum(a.values()), sum(b.values())
    if ta == 0 and tb == 0:
        return 1.0
    if ta == 0 or tb == 0:
        return 0.0
    return 2.0 * sum((a & b).values()) / (ta + tb)


def words(text):
    return Counter(re.findall(r"\w+", text.lower()))


def stat(v):
    v = sorted(v)
    n = len(v)
    return {"mean": round(sum(v) / n, 4), "median": round(v[n // 2], 4), "p5": round(v[n // 20], 4)} if n else None


def main():
    args = sys.argv[1:]
    worst = 10
    out = None
    if "--worst" in args:
        i = args.index("--worst"); worst = int(args[i + 1]); del args[i:i + 2]
    if "--json" in args:
        i = args.index("--json"); out = args[i + 1]; del args[i:i + 2]
    ours_dir, tika_dir, ext = Path(args[0]), Path(args[1]), args[2]
    rows, missing, one_empty = [], [], []
    prec, rec = [], []
    for p in sorted(ours_dir.glob("*.txt")):
        name = p.name[:-4]
        t = tika_dir / (name + "." + ext + ".txt")
        if not t.exists():
            t = tika_dir / (name + ".txt")
        if not t.exists():
            missing.append(name)
            continue
        ours = p.read_text(encoding="utf-8", errors="replace")
        tika = t.read_text(encoding="utf-8", errors="replace")
        if bool(ours.strip()) != bool(tika.strip()):
            one_empty.append({"file": name, "ours": len(ours.strip()), "tika": len(tika.strip())})
        rows.append((dice(grams(ours), grams(tika)), name, len(ours), len(tika)))
        wo, wt = words(ours), words(tika)
        if wo:
            prec.append(sum((wo & wt).values()) / sum(wo.values()))
        elif not wt:
            prec.append(1.0)
        wt_nd = Counter({w: c for w, c in wt.items() if not re.search(r"\d", w)})
        if wt_nd:
            rec.append(sum((wo & wt_nd).values()) / sum(wt_nd.values()))
        else:
            rec.append(1.0)
    rows.sort()
    v = [r[0] for r in rows]
    n = len(v)
    res = {
        "ext": ext, "files": n, "noTika": len(missing),
        "sim": stat(v), "precision": stat(prec), "recallNoDigits": stat(rec),
        "simLt095": sum(x < 0.95 for x in v), "simLt08": sum(x < 0.8 for x in v), "simLt05": sum(x < 0.5 for x in v),
        "precisionLt095": sum(x < 0.95 for x in prec), "recallLt095": sum(x < 0.95 for x in rec),
        "exactlyOneSideEmpty": one_empty[:20], "exactlyOneSideEmptyCount": len(one_empty),
        "worst": [{"file": r[1][:12], "sim": round(r[0], 4), "ours": r[2], "tika": r[3]} for r in rows[:worst]],
    }
    print(json.dumps(res))
    if out:
        json.dump(res, open(out, "w"), indent=1)


main()
