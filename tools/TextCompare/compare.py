#!/usr/bin/env python3
"""Compare extracted texts: ours, the Open XML SDK's raw w:t concatenation, and Apache Tika's.

    compare.py <dir> [--worst N] [--json FILE]

<dir> holds <name>.ours.txt, <name>.sdk.txt and <name>.tika.txt (written by TextCompare extract and by running the
Tika application jar over the same documents). Similarity is the Dice coefficient of character 5-gram multisets
of the lower-cased text with all white space removed, so paragraph, tab and line-break conventions (which differ
between extractors) do not count, and only the characters and their order locally do. Files where an extractor
failed (an .err file) or produced nothing are listed apart.
"""
import json
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
    common = sum((a & b).values())
    return 2.0 * common / (ta + tb)


def pct(values, p):
    if not values:
        return float("nan")
    values = sorted(values)
    return values[min(len(values) - 1, int(p * len(values)))]


def main():
    args = sys.argv[1:]
    worst_n = 20
    out = None
    if "--worst" in args:
        i = args.index("--worst")
        worst_n = int(args[i + 1])
        del args[i:i + 2]
    if "--json" in args:
        i = args.index("--json")
        out = args[i + 1]
        del args[i:i + 2]
    d = Path(args[0])
    names = sorted(p.name[:-len(".ours.txt")] for p in d.glob("*.ours.txt"))
    errs = sorted(p.name[:-len(".ours.err")] for p in d.glob("*.ours.err"))
    rows = []
    for n in names:
        ours = (d / f"{n}.ours.txt").read_text(encoding="utf-8", errors="replace")
        sdk_p, tika_p = d / f"{n}.sdk.txt", d / f"{n}.tika.txt"
        sdk = sdk_p.read_text(encoding="utf-8", errors="replace") if sdk_p.exists() else None
        tika = tika_p.read_text(encoding="utf-8", errors="replace") if tika_p.exists() else None
        g = grams(ours)
        row = {"name": n, "oursChars": len(ours), "sdkChars": None if sdk is None else len(sdk),
               "tikaChars": None if tika is None else len(tika)}
        if tika is not None:
            row["vsTika"] = dice(g, grams(tika))
        if sdk is not None:
            row["vsSdk"] = dice(g, grams(sdk))
        if tika is not None and sdk is not None:
            row["tikaVsSdk"] = dice(grams(tika), grams(sdk))
        rows.append(row)

    report = {"files": len(names), "oursFailed": errs, "sets": {}}
    for key in ("vsTika", "vsSdk", "tikaVsSdk"):
        vals = [r[key] for r in rows if key in r]
        if not vals:
            continue
        report["sets"][key] = {
            "n": len(vals),
            "mean": sum(vals) / len(vals),
            "p05": pct(vals, 0.05), "p25": pct(vals, 0.25), "median": pct(vals, 0.5), "p75": pct(vals, 0.75),
            "ge0.99": sum(v >= 0.99 for v in vals), "ge0.95": sum(v >= 0.95 for v in vals),
            "ge0.90": sum(v >= 0.90 for v in vals), "lt0.90": sum(v < 0.90 for v in vals),
            "min": min(vals),
        }
    report["worstVsTika"] = sorted((r for r in rows if "vsTika" in r), key=lambda r: r["vsTika"])[:worst_n]
    report["worstVsSdk"] = sorted((r for r in rows if "vsSdk" in r), key=lambda r: r["vsSdk"])[:worst_n]
    text = json.dumps(report, indent=1)
    if out:
        Path(out).write_text(text)
    print(text)


if __name__ == "__main__":
    main()
