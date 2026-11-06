#!/usr/bin/env python3
"""Groups the mismatches of a CalcSweep run by workbook, so each cause can be looked at once.

    calc_causes.py DIR [--classes bug,downstream-or-bug] [--top N] [--examples K]

DIR holds mismatches-N.tsv (class, address, formula, expected, got). Prints, for the classes asked for, the workbooks with the most
mismatches (by file hash name), the first examples in each, and the expected/got shapes (number vs error type) over all of them.
"""
import argparse
import collections
import glob

ap = argparse.ArgumentParser()
ap.add_argument("dir")
ap.add_argument("--classes", default="bug,downstream-or-bug")
ap.add_argument("--top", type=int, default=12)
ap.add_argument("--examples", type=int, default=2)
a = ap.parse_args()
want = set(a.classes.split(","))
by = collections.defaultdict(list)
shape = collections.Counter()


def kind(v):
    if v.startswith("e:"):
        return v
    return v[:2]


for p in sorted(glob.glob(a.dir + "/mismatches-*.tsv")):
    for line in open(p, errors="replace"):
        f = line.rstrip("\n").split("\t")
        if len(f) < 5 or f[0] not in want or not f[3].startswith("expected "):
            continue
        by[f[1].split("!")[0]].append(f)
        shape[(kind(f[3].replace("expected ", "")), kind(f[4].replace("got ", "")))] += 1
total = sum(len(v) for v in by.values())
print(f"{total} mismatches in {len(by)} workbooks")
print("expected -> got shapes:", shape.most_common(10))
for name, v in sorted(by.items(), key=lambda kv: -len(kv[1]))[: a.top]:
    print(f"{name[:48]} {len(v)}")
    for f in v[: a.examples]:
        print("    ", f[1].split("!", 1)[1][:36], "|", f[2][:100], "|", f[3][:44], "|", f[4][:44])
