#!/usr/bin/env python3
"""Layout differential by word geometry, next to LayoutProbe compare (which compares extracted text lines).

Both PDFs are read with poppler's pdftotext -bbox. For every page, each word of the reference PDF is paired with the
nearest unused word of ours that has the same text; the pair agrees when the two boxes' left edges and baselines are within
TOL points. A word at the same baseline and the same x sits on the same line at the same place, so the agreement covers line
breaks and object geometry without the grouping noise of extracted lines (superscripts, columns, tab leaders).
Information beside the text-line comparison; not a replacement for page counts.

usage: layout-geometry.py OURS_DIR OTHER_DIR [--tol POINTS] [NAME...]
Prints one JSON line per document: pages of each side, words of the reference, words that agree within TOL.
"""
import json, os, re, subprocess, sys
from html import unescape

def pages(path):
    out = subprocess.run(["pdftotext", "-bbox", path, "-"], capture_output=True, text=True).stdout
    res = []
    for part in out.split("<page ")[1:]:
        words = []
        for m in re.finditer(r'<word xMin="([\d.]+)" yMin="([\d.]+)" xMax="([\d.]+)" yMax="([\d.]+)">(.*?)</word>', part):
            x0, y0, x1, y1 = map(float, m.groups()[:4])
            t = unescape(m.group(5))
            # Leader runs and other pure-punctuation fill carry no line-break information.
            if re.fullmatch(r"[.\-_·]{4,}", t):
                continue
            words.append((t, x0, y1))  # left edge and bottom of the box
        res.append(words)
    return res

def agree(ours, ref, tol):
    pool = {}
    for t, x, y in ours:
        pool.setdefault(t, []).append((x, y))
    n = 0
    for t, x, y in ref:
        c = pool.get(t)
        if not c:
            continue
        best, bd = -1, 1e9
        for i, (cx, cy) in enumerate(c):
            d = abs(cx - x) + abs(cy - y)
            if d < bd:
                best, bd = i, d
        cx, cy = c[best]
        if abs(cx - x) <= tol and abs(cy - y) <= tol:
            n += 1
        c.pop(best)
    return n

def main():
    args = sys.argv[1:]
    tol = 2.0
    if "--tol" in args:
        i = args.index("--tol")
        tol = float(args[i + 1])
        del args[i:i + 2]
    ours, other = args[0], args[1]
    names = args[2:] or sorted(f[:-4] for f in os.listdir(other) if f.endswith(".pdf"))
    for n in names:
        a, b = pages(os.path.join(ours, n + ".pdf")), pages(os.path.join(other, n + ".pdf"))
        total = sum(len(p) for p in b)
        ok = sum(agree(a[i] if i < len(a) else [], pb, tol) for i, pb in enumerate(b))
        print(json.dumps({"name": n, "pagesOurs": len(a), "pagesOther": len(b), "words": total, "agree": ok,
                          "ratio": round(ok / total, 4) if total else 1.0}))

main()
