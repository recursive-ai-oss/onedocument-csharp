#!/usr/bin/env python3
"""Layout differential floors: groups the compared documents into sets and reports, for the rows where both renderers used
equivalent fonts, page-count agreement, the mean agreement of extracted text lines (LayoutProbe compare) and of word geometry
(layout-geometry.py).

usage: layout-floors.py OURS_DIR OFFICE_DIR LINES.jsonl GEOMETRY.jsonl [--kind word|powerpoint]

A row gates only when every font Office embedded in its PDF has a metric-compatible partner among ours (the table below); a row
with any other font is font-substitution and is listed apart. Fonts are read from the PDFs with poppler's pdffonts.
"""
import json, os, re, subprocess, sys

EQUIV = {  # Office PDF font family -> the family ours uses for the same glyph advances
    "Arial": "LiberationSans", "ArialMT": "LiberationSans", "Arial-BoldMT": "LiberationSans", "Arial-ItalicMT": "LiberationSans",
    "Arial-BoldItalicMT": "LiberationSans",
    "Calibri": "Carlito", "Cambria": "Caladea", "TimesNewRomanPSMT": "LiberationSerif", "CourierNewPSMT": "LiberationMono",
}

def family(name):
    n = re.sub(r"^[A-Z]{6}\+", "", name)
    if n in EQUIV:
        return EQUIV[n]
    for k, v in EQUIV.items():
        if n.startswith(k.split("-")[0]) and k in ("Calibri", "Cambria"):
            return v
    return None

def ours_family(name):
    n = re.sub(r"^[A-Z]{6}\+", "", name)
    m = re.match(r"(LiberationSans|LiberationSerif|LiberationMono|Carlito|Caladea)", n)
    return m.group(1) if m else None

def fonts(path):
    out = subprocess.run(["pdffonts", path], capture_output=True, text=True).stdout.splitlines()[2:]
    return sorted({re.sub(r"^[A-Z]{6}\+", "", l.split()[0]) for l in out if l.strip()})

def same_fonts(office, ours):
    a = {family(f) for f in office}
    b = {ours_family(f) for f in ours}
    return None not in a and None not in b and a == b

def set_of(name, pages):
    if re.match(r"x\d\d-", name):
        return "excel"
    if re.match(r"(19|20|21|22|23|24|40)-|x28-|tbl-17|31-font", name):
        return "cjk-rtl"
    if re.match(r"(float|drawing|smartart)-|8\d-math|8\d-", name) and not re.match(r"81-page|87-doc|85-vert", name):
        return "edge"
    if pages >= 3:
        return "long"
    return "business"

def main():
    args = sys.argv[1:]
    kind = "word"
    if "--kind" in args:
        i = args.index("--kind"); kind = args[i + 1]; del args[i:i + 2]
    ours_dir, off_dir, lines_f, geo_f = args
    lines = {json.loads(l)["name"][:-4]: json.loads(l) for l in open(lines_f) if '"name"' in l}
    geo = {json.loads(l)["name"]: json.loads(l) for l in open(geo_f)}
    rows = []
    for n, g in geo.items():
        o = fonts(os.path.join(ours_dir, n + ".pdf"))
        w = fonts(os.path.join(off_dir, n + ".pdf"))
        st = "slides" if kind == "powerpoint" else set_of(n, g["pagesOther"])
        rows.append({"name": n, "set": st, "gate": same_fonts(w, o), "pagesOurs": g["pagesOurs"], "pagesOther": g["pagesOther"],
                     "lines": lines.get(n, {}).get("ratio"), "geometry": g["ratio"], "officeFonts": w, "ourFonts": o})
    json.dump(rows, open(os.path.splitext(geo_f)[0] + ".rows.json", "w"), indent=0)
    for gate in (True, False):
        print("== gating rows (same fonts)" if gate else "== font-substitution rows (not gating)")
        for st in sorted({r["set"] for r in rows}):
            rs = [r for r in rows if r["set"] == st and r["gate"] == gate]
            if not rs:
                continue
            pc = sum(1 for r in rs if r["pagesOurs"] == r["pagesOther"])
            ln = [r["lines"] for r in rs if r["lines"] is not None]
            ge = [r["geometry"] for r in rs]
            ex = sum(1 for r in rs if r["geometry"] >= 0.99)
            print(f"{st:10} docs {len(rs):4}  page count {pc}/{len(rs)}  lines {sum(ln)/max(len(ln),1):.3f}  geometry {sum(ge)/len(ge):.3f}  geometry>=0.99 {ex}")

main()
