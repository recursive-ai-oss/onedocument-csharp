#!/usr/bin/env python3
"""Compare the revision model's accepted/rejected text with LibreOffice's, file by file.

  compare_lo.py PROBE_OUT_DIR LO_OUT_DIR [--json FILE]

PROBE_OUT_DIR holds <id>.accepted.txt / <id>.rejected.txt from `RevisionProbe batch`; LO_OUT_DIR holds
<name>.accept.lo.txt / <name>.reject.lo.txt from `lo_resolve.py`, where <name> is <id>'s file name. Texts are compared
with all white space removed (the office trims runs without xml:space and breaks lines differently). A text that holds
all of the other in order is reported as such: LibreOffice writes list labels, bullets and note numbers into its text,
which the revision model does not. Information only.
"""
import json, os, re, sys


def squeeze(text):
    text = text.lstrip("\ufeff").replace("\u2011", "-").replace("\u00a0", " ")
    return re.sub(r"\s+", "", text)


def is_subsequence(small, big):
    it = iter(big)
    return all(ch in it for ch in small)


def main():
    probe, lo = sys.argv[1], sys.argv[2]
    out = sys.argv[sys.argv.index("--json") + 1] if "--json" in sys.argv else None
    rows, counts = [], {"equal": 0, "lo-adds-only": 0, "ours-adds-only": 0, "different": 0, "missing": 0}
    for fn in sorted(os.listdir(probe)):
        m = re.match(r"(.*)\.(accepted|rejected)\.txt$", fn)
        if not m:
            continue
        ident, mode = m.group(1), m.group(2)
        lo_file = os.path.join(lo, f"{ident}.{'accept' if mode == 'accepted' else 'reject'}.lo.txt")
        ours = squeeze(open(os.path.join(probe, fn), encoding="utf-8").read())
        if not os.path.exists(lo_file):
            counts["missing"] += 1
            rows.append({"file": ident, "mode": mode, "status": "missing"})
            continue
        theirs = squeeze(open(lo_file, encoding="utf-8", errors="replace").read())
        if ours == theirs:
            counts["equal"] += 1
            rows.append({"file": ident, "mode": mode, "status": "equal"})
            continue
        for label, small, big in (("lo-adds-only", ours, theirs), ("ours-adds-only", theirs, ours)):
            if is_subsequence(small, big):
                counts[label] += 1
                rows.append({"file": ident, "mode": mode, "status": label, "oursLen": len(ours), "loLen": len(theirs)})
                break
        else:
            label = None
        if label:
            continue
        i = next((k for k in range(min(len(ours), len(theirs))) if ours[k] != theirs[k]), min(len(ours), len(theirs)))
        counts["different"] += 1
        rows.append({"file": ident, "mode": mode, "status": "different", "offset": i, "ours": ours[max(0, i - 30):i + 40],
                     "lo": theirs[max(0, i - 30):i + 40], "oursLen": len(ours), "loLen": len(theirs)})
    if out:
        with open(out, "w", encoding="utf-8") as f:
            for r in rows:
                f.write(json.dumps(r, ensure_ascii=False) + "\n")
    print(json.dumps(counts))


main()
