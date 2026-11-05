#!/usr/bin/env python3
"""Totals over the output of `XlsxProbe features`: census counts, untouched-save identity and the outcome of each edit class.

  feature_census.py SWEEPDIR...
"""
import collections, glob, json, sys


def main(dirs):
    st = collections.Counter(); tot = collections.Counter(); cf = collections.Counter(); dv = collections.Counter()
    edits = collections.defaultdict(collections.Counter); typed = collections.Counter(); finds = []
    codes = collections.Counter(); files_with = collections.Counter()
    for d in dirs:
        for f in glob.glob(d + "/sweep-*.jsonl"):
            for line in open(f):
                r = json.loads(line)
                st[r["status"]] += 1
                if r["status"] == "typed-error":
                    typed[r.get("errorClass", "") + "/" + r.get("errorCode", "")] += 1
                    continue
                if r["status"] != "ok":
                    finds.append((r["file"], r["status"], r.get("untouchedFirstDiff") or r.get("error") or str({k: v for k, v in r.get("edits", {}).items() if str(v).startswith("FAIL")})))
                for k in ("xfs", "fonts", "fills", "borders", "dxfs", "numFmts", "cellStyles", "tableStyles", "resolveFailures", "tables", "tableColumns", "cfGroups", "cfExtended", "dv", "dvExtended",
                          "formattedNumbers", "names", "builtinNames", "hiddenNames", "untouchedParts", "untouchedDiff"):
                    tot[k] += r.get(k, 0)
                    if r.get(k, 0) and k in ("tables", "cfGroups", "dv", "names", "cfExtended", "dvExtended"):
                        files_with[k] += 1
                for k, v in r.get("cfTypes", {}).items(): cf[k] += v
                for k, v in r.get("dvTypes", {}).items(): dv[k] += v
                for k, v in r.get("defectCodes", {}).items(): codes[k] += v
                for cls, v in r.get("edits", {}).items():
                    edits[cls][str(v).split(";")[0].split(":")[0]] += 1
    print("files", dict(st)); print("typed rejections", dict(typed))
    print("totals", dict(tot)); print("files with feature", dict(files_with))
    print("cf rules by type", dict(cf)); print("dv by type", dict(dv))
    for cls, c in edits.items(): print("edit", cls, dict(c))
    print("defect codes", dict(codes.most_common(12)))
    for x in finds[:20]: print("FINDING", x)
    return 1 if finds else 0


sys.exit(main(sys.argv[1:]))
