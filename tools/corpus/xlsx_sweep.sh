#!/usr/bin/env bash
# Runs the workbook-layer sweep over the XLSX corpus sets, then the schema and LibreOffice checks on its output.
#
#   tools/corpus/xlsx_sweep.sh OUTDIR [LO_SAMPLE]
#
# Needs: XlsxProbe built in Release (path in XLSXPROBE_DLL), ONEDOCUMENT_SOFFICE and ONEDOCUMENT_XSD as for run_checks.py, and the
# corpus sets under $ONEDOCUMENT_HOME/corpus (default ~/oneoffice/corpus). Sets that are absent are skipped.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="${1:?usage: xlsx_sweep.sh OUTDIR [LO_SAMPLE]}"
sample="${2:-200}"
home="${ONEDOCUMENT_HOME:-$HOME/oneoffice}"
sets="commoncrawl/xlsx bugzilla/documentfoundation/xlsx bugzilla/apache/xlsx lo-xlsx"
rm -rf "$out"; mkdir -p "$out"
for s in $sets; do
  d="$home/corpus/$s"; [ -d "$d" ] || continue
  n="${s//\//-}"
  for i in 0 1; do dotnet "$XLSXPROBE_DLL" sweep "$d" "$out/$n" --shard $i/2 > "$out/$n-$i.log" 2>&1 & done
done
wait
grep -h summary "$out"/*.log
python3 - "$out" <<'PY'
import json, glob, collections, sys
out = sys.argv[1]
c = collections.Counter(); u = collections.Counter(); parts = same = cells = mism = defects = 0; single = collections.Counter()
for f in glob.glob(out + "/*/sweep-*.jsonl"):
    for l in open(f):
        r = json.loads(l); c[r["status"]] += 1
        if r["status"] == "typed-error": u[r["errorClass"] + "/" + r["errorCode"]] += 1
        if r["status"] in ("untouched-diff", "model-mismatch", "UNTYPED-EXCEPTION"): print("FINDING", r["file"], r["status"], r.get("firstDiff") or r.get("firstMismatch") or r.get("error"))
        parts += r.get("untouchedParts", 0); same += r.get("untouchedParts", 0) - r.get("untouchedDiff", 0)
        cells += r.get("cells", 0); mism += r.get("modelMismatches", 0)
        if "singleEditChanged" in r: single[tuple(sorted(r["singleEditChanged"]))] += 1
print("statuses", dict(c)); print("typed errors", dict(u))
print("untouched parts", parts, "identical", same, "cells", cells, "model mismatches", mism)
for k, v in single.most_common(6): print("single-cell edit changed", v, "files:", [x if "worksheets" not in x else "sheet" for x in k])
PY
for s in $sets; do
  d="$home/corpus/$s"; [ -d "$d" ] || continue
  n="${s//\//-}"
  echo "== validate $s"; python3 "$here/xlsx_edit_checks.py" validate "$out/$n" "$d" | cut -c1-1500
done
for s in $sets; do
  d="$home/corpus/$s"; [ -d "$d" ] || continue
  n="${s//\//-}"
  echo "== lo $s"; python3 "$here/xlsx_edit_checks.py" lo "$out/$n" "$d" --sample "$((sample / 4))" | cut -c1-1500
done
