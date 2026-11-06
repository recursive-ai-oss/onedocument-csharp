#!/usr/bin/env python3
"""Adds up the round-trip stages of the corpus gate into one compact text block.

    rt_summary.py OUT_DIR

OUT_DIR is the gate output directory. For each XLSX and PPTX set it reads the sweep-*.jsonl lines (files by status, parts compared and
byte-identical in the untouched round trip, workbook model mismatches, what a single-cell edit changed, what each presentation edit
changed); for the package layer it reads rt-package/*.txt (files tried and parts compared); for DOCX it reads rt-docx*/**/results.jsonl.
"""
import collections
import glob
import json
import os
import re
import sys

out = sys.argv[1]


def lines(pattern):
    for p in sorted(glob.glob(pattern)):
        for l in open(p, errors="replace"):
            if l.startswith("{"):
                try:
                    yield json.loads(l)
                except ValueError:
                    pass


print("== package layer (open, rewrite untouched, compare every part)")
for p in sorted(glob.glob(out + "/rt-package/*.txt")):
    txt = open(p, errors="replace").read()
    m = re.search(r"files tried (\d+), opened (\d+), round-tripped identical (\d+), rejected \(typed\) (\d+), recovered from damage (\d+).*failed (\d+)", txt)
    q = re.search(r"parts compared (\d+), identical (\d+)", txt)
    codes = re.findall(r"rejected with ([a-z.\-]+): (\d+)", txt)
    if m:
        print(os.path.basename(p)[:-4], "tried", m[1], "opened", m[2], "identical", m[3], "typed-rejected", m[4], "recovered", m[5], "failed", m[6],
              "| parts", q[1] if q else "?", "of", q[1] if q else "?", "identical" if q and q[1] == q[2] else "DIFFER", dict(codes))

print("== DOCX hub (Document.Open, save untouched / one-paragraph edit, per-part compare)")
for kind in ("rt-docx", "rt-docx-edit"):
    for d in sorted(glob.glob(out + "/" + kind + "/*/")):
        rows = list(lines(d + "results.jsonl"))
        st = collections.Counter(r["status"] for r in rows)
        codes = collections.Counter(r.get("code") for r in rows if r["status"] != "saved")
        ident = sum(r.get("identical", 0) for r in rows if r["status"] == "saved")
        changed = sum(len(r.get("changed", [])) for r in rows if r["status"] == "saved")
        raw_np = sum(r.get("rawNotPreserved", 0) for r in rows if r["status"] == "saved")
        only_main = sum(1 for r in rows if r["status"] == "saved" and len(r.get("changed", [])) == 1)
        print(kind, os.path.basename(d.rstrip("/")), dict(st), dict(codes), "parts identical", ident, "changed", changed, "raw not preserved", raw_np, "files with exactly one changed part", only_main)

for kind in ("rt-xlsx", "rt-pptx"):
    print("==", kind)
    for d in sorted(glob.glob(out + "/" + kind + "/*/")):
        rows = [r for r in lines(d + "sweep-*.jsonl") if r.get("kind") == "file"]
        st = collections.Counter(r["status"] for r in rows)
        parts = sum(r.get("untouchedParts", 0) for r in rows)
        diff = sum(r.get("untouchedDiff", 0) for r in rows)
        extra = ""
        if kind == "rt-xlsx":
            cells = sum(r.get("cells", 0) for r in rows)
            mm = sum(r.get("modelMismatches", 0) for r in rows)
            single = collections.Counter(len(r.get("singleEditChanged", [])) for r in rows if "singleEditChanged" in r)
            only_sheet = sum(1 for r in rows if len(r.get("singleEditChanged", [])) == 1 and "/worksheets/" in r["singleEditChanged"][0])
            extra = f"cells {cells} model-mismatches {mm} single-cell edit changed one part in {single.get(1, 0)} files (a worksheet in {only_sheet}), more in {sum(v for k, v in single.items() if k > 1)}"
        else:
            ops = collections.defaultdict(lambda: [0, 0])
            for r in rows:
                for op, ch in (r.get("changed") or {}).items():
                    ops[op][0] += 1
                    # an edit may change its own slide part(s) and the package index parts; added and removed parts (a duplicated or deleted
                    # slide's own notes, charts, media) are the edit's own and are not counted
                    bad = [c for c in ch if c[0] == "~" and not re.match(r"~(ppt/slides/|ppt/presentation.xml|ppt/_rels/presentation.xml.rels|\[Content_Types\].xml|ppt/notesSlides/)", c)]
                    ops[op][1] += bool(bad)
            extra = "edits (files, with an unexpected part changed): " + ", ".join(f"{k} {v[0]}/{v[1]}" for k, v in sorted(ops.items()))
        errs = collections.Counter(r.get("errorCode") for r in rows if r["status"] != "ok")
        print(os.path.basename(d.rstrip("/")), dict(st), dict(errs), "untouched parts", parts, "differing", diff, extra)
