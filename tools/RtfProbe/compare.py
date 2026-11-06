#!/usr/bin/env python3
"""Compare our text of RTF files with Apache Tika's (information, not a gate).

    compare.py <dir> [--worst N]

<dir> holds NAME.ours.txt (RtfProbe read --txt) and NAME.tika.txt (the Tika application run over the same files). The
similarity is the Dice coefficient of character 5-gram multisets of the lower-cased text with all white space removed, the
measure TextCompare uses, so line, tab and paragraph conventions do not count.
"""
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


def main():
    args = sys.argv[1:]
    worst = 10
    if "--worst" in args:
        i = args.index("--worst")
        worst = int(args[i + 1])
        del args[i:i + 2]
    d = Path(args[0])
    rows = []
    for p in sorted(d.glob("*.ours.txt")):
        name = p.name[: -len(".ours.txt")]
        t = d / (name + ".tika.txt")
        if not t.exists():
            continue
        ours = p.read_text(encoding="utf-8", errors="replace")
        tika = t.read_text(encoding="utf-8", errors="replace")
        rows.append((dice(grams(ours), grams(tika)), name, len(ours), len(tika)))
    rows.sort()
    for s, name, a, b in rows[:worst]:
        print(f"{s:.4f} {name} ours={a} tika={b}")
    if rows:
        vals = [r[0] for r in rows]
        print(f"files={len(rows)} mean={sum(vals) / len(vals):.4f} min={vals[0]:.4f} >=0.95: {sum(v >= 0.95 for v in vals)}")


main()
