#!/usr/bin/env python3
"""Adds up the CalcSweep shard logs and mismatch files of one calc run into one JSON object.

    calc_summary.py DIR LABEL

DIR holds shard-N.log (CalcSweep stdout) and mismatches-N.tsv. Prints files, unreadable files, formulas, the testable ones (every
function implemented, no external reference), matches, the floor (matches / testable), mismatches by class, the most common unimplemented
functions, and for the classes `bug` and `downstream-or-bug` the functions that appear most in the mismatching formulas.
"""
import collections
import glob
import json
import re
import sys

CLASSES = {"bug", "downstream-or-bug", "precision", "volatile", "volatile-downstream", "import-gap-ref", "unimplemented", "external", "service-bound"}
d, label = sys.argv[1], sys.argv[2]
tot = collections.Counter()
unimpl = collections.Counter()
files = unreadable = 0
for p in glob.glob(d + "/shard-*.log"):
    section = None
    for line in open(p, errors="replace"):
        m = re.match(r"files (\d+) \(unreadable (\d+)\); formulas (\d+); of those with every function implemented (\d+), matching (\d+)", line)
        if m:
            files += int(m[1]); unreadable += int(m[2])
            continue
        m = re.match(r"  ([a-z\-]+): (\d+)$", line)
        if m and section is None:
            tot[m[1]] += int(m[2])
            continue
        if line.startswith("most common unimplemented"):
            section = "u"
            continue
        if line.startswith("samples"):
            section = "s"
            continue
        m = re.match(r"  ([A-Z0-9_.]+): (\d+)$", line)
        if m and section == "u":
            unimpl[m[1]] += int(m[2])
funcs = collections.defaultdict(collections.Counter)
classes = collections.Counter()
files_with = collections.defaultdict(set)
for p in glob.glob(d + "/mismatches-*.tsv"):
    for line in open(p, errors="replace"):
        f = line.rstrip("\n").split("\t")
        if len(f) < 5 or f[0] not in CLASSES or not f[3].startswith("expected "):
            continue
        classes[f[0]] += 1
        files_with[f[0]].add(f[1].split("!")[0])
        if f[0] in ("bug", "downstream-or-bug"):
            for fn in set(re.findall(r"([A-Za-z][A-Za-z0-9_.]*)\(", f[2])):
                funcs[f[0]][fn.upper()] += 1
testable, match = tot["testable"], tot["testable-match"]
print(json.dumps({
    "label": label, "files": tot["files"], "unreadable": unreadable, "formulas": tot["formulas"], "testable": testable,
    "testableMatch": match, "floor": round(100.0 * match / testable, 3) if testable else None,
    "mismatchClasses": dict(classes), "filesWithMismatch": {k: len(v) for k, v in files_with.items()},
    "topUnimplemented": unimpl.most_common(15),
    "topFunctionsInBug": funcs["bug"].most_common(15), "topFunctionsInDownstream": funcs["downstream-or-bug"].most_common(10),
}))
