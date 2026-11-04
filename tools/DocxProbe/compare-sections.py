#!/usr/bin/env python3
"""Compares the page structure LibreOffice produces for the generated section cases with what the hub resolves.

    compare-sections.py CASE_DIR OUT_DIR

Environment: DOCXPROBE (command that runs the built probe, e.g. "dotnet /path/DocxProbe.dll"), ONEOFFICE_SOFFICE and LO_PROFILE
(see tools/README.md). Needs pdftotext and pdfinfo (poppler). The result is information: LibreOffice is not the reference for page
structure. A line starts with "ok" or "diff" per page.
"""
import json, os, re, subprocess, sys

case_dir, out_dir = sys.argv[1], sys.argv[2]
probe = os.environ["DOCXPROBE"].split()
soffice = os.environ.get("ONEOFFICE_SOFFICE", "soffice")
profile = os.environ.get("LO_PROFILE", os.path.join(out_dir, "lo-profile"))
os.makedirs(out_dir, exist_ok=True)

docs = sorted(f for f in os.listdir(case_dir) if f.endswith(".docx"))
subprocess.run([soffice, f"-env:UserInstallation=file://{profile}", "--headless", "--convert-to", "pdf", "--outdir", out_dir]
               + [os.path.join(case_dir, d) for d in docs], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)

token = re.compile(r"\b[HF]_[A-Z0-9]+\b")
pages_ok = pages_total = 0
for d in docs:
    name = d[:-5]
    pdf = os.path.join(out_dir, name + ".pdf")
    if not os.path.exists(pdf):
        print(f"diff {name}: no PDF")
        continue
    counts = open(os.path.join(case_dir, name + ".pages")).read().strip()
    expected = json.loads(subprocess.run(probe + ["expect", os.path.join(case_dir, d), counts], capture_output=True, text=True, check=True).stdout)
    info = subprocess.run(["pdfinfo", "-f", "1", "-l", "999", pdf], capture_output=True, text=True).stdout
    sizes = [(float(m.group(2)), float(m.group(3))) for m in re.finditer(r"Page\s+(\d+) size:\s+([\d.]+) x ([\d.]+) pts", info)]
    if len(sizes) != len(expected):
        print(f"diff {name}: LibreOffice made {len(sizes)} pages, expected {len(expected)}")
    for i, e in enumerate(expected):
        pages_total += 1
        text = subprocess.run(["pdftotext", "-f", str(i + 1), "-l", str(i + 1), pdf, "-"], capture_output=True, text=True).stdout
        found = set(token.findall(text))
        want = {t for t in (e["header"], e["footer"]) if t}
        size_ok = i < len(sizes) and abs(sizes[i][0] - e["w"]) < 1.5 and abs(sizes[i][1] - e["h"]) < 1.5
        if found == want and size_ok:
            pages_ok += 1
            print(f"ok   {name} page {i + 1}")
        else:
            got = sizes[i] if i < len(sizes) else None
            print(f"diff {name} page {i + 1}: hub header/footer {sorted(want)} size {e['w']}x{e['h']}; LibreOffice {sorted(found)} size {got}")
print(f"pages agree: {pages_ok}/{pages_total}")
