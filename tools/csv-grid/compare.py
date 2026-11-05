#!/usr/bin/env python3
"""Compares OneDocument's reading of a CSV (CsvProbe cells) with Excel's (office-oracle --cells).

    compare.py INPUT.csv OURS.tsv EXCEL.tsv [--ids] [--tol X]   (--ids skips the leading id column of the value grid)

Cells are keyed by row and column. A cell agrees when the type and value agree; number formats are listed separately (Excel's
format for a typed date or percent against the code OneDocument would give it). Prints the counts and every difference.
"""
import sys


def load(path, width):
    cells = {}
    for line in open(path, encoding="utf-8"):
        parts = line.rstrip("\n").split("\t")
        parts += [""] * (width - len(parts))
        cells[(int(parts[0]), int(parts[1]))] = parts[2:]
    return cells


TOL = float(sys.argv[sys.argv.index("--tol") + 1]) if "--tol" in sys.argv else 1e-12


def main():
    raw = open(sys.argv[1], encoding="utf-8-sig", newline="").read().split("\r\n")
    ours = load(sys.argv[2], 5)
    excel = load(sys.argv[3], 6)
    keys = sorted(set(ours) | set(excel))
    same = diff = fmt_same = fmt_diff = 0
    for k in keys:
        o = ours.get(k, ["empty", "", ""])
        e = excel.get(k, ["empty", "", "", ""])
        label = raw[k[0] - 1] if k[0] - 1 < len(raw) else ""
        if "--ids" in sys.argv and k[1] == 1 and label.split(",")[0].isdigit():
            continue  # the id column
        ot, ov, ofmt = o[0], o[1], o[2] if len(o) > 2 else ""
        et, ev, efmt = e[0], e[1], e[2]
        if ot == et == "number":
            ok = abs(float(ov) - float(ev)) <= TOL * max(1.0, abs(float(ev)))
        else:
            ok = ot == et and ov == ev
        if ok:
            same += 1
            if ot == "number":
                if ofmt == efmt or (ofmt == "General" and efmt == "General"):
                    fmt_same += 1
                else:
                    fmt_diff += 1
                    print(f"FORMAT  {label!r}: ours {ofmt!r} excel {efmt!r}")
        else:
            diff += 1
            print(f"VALUE   {label!r}: ours {ot}:{ov!r} excel {et}:{ev!r} (excel text {e[3]!r})")
    print(f"cells compared {same + diff}: agree {same}, differ {diff}; formats of agreeing numbers: same {fmt_same}, differ {fmt_diff}")


main()
