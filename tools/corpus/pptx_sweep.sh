#!/usr/bin/env bash
# Runs the presentation sweep over the PPTX corpus sets, then the schema and LibreOffice checks on its output.
#
#   tools/corpus/pptx_sweep.sh OUTDIR [LO_SAMPLE]
#
# Needs: PptxProbe built (path in PPTXPROBE_DLL), ONEDOCUMENT_SOFFICE and ONEDOCUMENT_XSD as for run_checks.py, and the corpus sets
# under $ONEDOCUMENT_HOME/corpus (PPTX directories listed in PPTX_SETS, relative to it). Sets that are absent are skipped.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="${1:?usage: pptx_sweep.sh OUTDIR [LO_SAMPLE]}"
sample="${2:-100}"
home="${ONEDOCUMENT_HOME:?set ONEDOCUMENT_HOME}"
sets="${PPTX_SETS:-bugzilla/documentfoundation/pptx lo-pptx}"
rm -rf "$out"; mkdir -p "$out"
for s in $sets; do
  d="$home/corpus/$s"; [ -d "$d" ] || continue
  n="${s//\//-}"
  for i in 0 1 2 3; do dotnet "$PPTXPROBE_DLL" sweep "$d" "$out/$n" --shard $i/4 > "$out/$n-$i.log" 2>&1 & done
done
wait
grep -h summary "$out"/*.log
python3 - "$out" <<'PY'
import json, glob, collections, sys
out = sys.argv[1]
c = collections.Counter(); u = collections.Counter(); parts = same = 0; ops = collections.Counter(); bad = collections.Counter()
for f in glob.glob(out + "/*/sweep-*.jsonl"):
    for l in open(f):
        r = json.loads(l)
        if r.get("kind") != "file": continue
        c[r["status"]] += 1
        if r["status"] == "typed-error": u[r["errorClass"] + "/" + r["errorCode"]] += 1
        if r["status"] in ("untouched-diff", "UNTYPED-EXCEPTION"): print("FINDING", r["file"], r["status"], r.get("firstDiff") or r.get("error"))
        parts += r.get("untouchedParts", 0); same += r.get("untouchedParts", 0) - r.get("untouchedDiff", 0)
        for op in r.get("changed", {}): ops[op] += 1
        for op, names in r.get("unexpected", {}).items():
            bad[op] += 1; print("UNEXPECTED", r["file"], op, names[:5])
print("statuses", dict(c)); print("typed errors", dict(u))
print("untouched parts", parts, "identical", same)
print("edits run per kind", dict(ops), "with unexpected changes", dict(bad))
PY
for s in $sets; do
  d="$home/corpus/$s"; [ -d "$d" ] || continue
  n="${s//\//-}"
  echo "== validate $s"; python3 "$here/pptx_edit_checks.py" validate "$out/$n" "$d" | cut -c1-1500
done
for s in $sets; do
  d="$home/corpus/$s"; [ -d "$d" ] || continue
  n="${s//\//-}"
  echo "== lo $s"; python3 "$here/pptx_edit_checks.py" lo "$out/$n" "$d" --sample "$sample" | cut -c1-1500
done
