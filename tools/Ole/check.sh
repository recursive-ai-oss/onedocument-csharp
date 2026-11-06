#!/usr/bin/env bash
# Converts the OLE fixtures (testdata/ole) with OleProbe, validates every converted part against the pinned schemas, and asks a
# word processor to open each result.
#
#   tools/Ole/check.sh <out-dir>
#
# Environment: ONEDOCUMENT_HOME (oracles, schemas), OLEPROBE_DLL (built OleProbe.dll), PYTHON (default: python3).
set -uo pipefail
out="${1:?usage: check.sh <out-dir>}"
home="${ONEDOCUMENT_HOME:?set ONEDOCUMENT_HOME}"
here="$(cd "$(dirname "$0")" && pwd)"
fx="$here/../../testdata/ole"
python="${PYTHON:-python3}"
mkdir -p "$out"
for x in doc xls ppt; do
  case $x in doc) y=docx ;; xls) y=xlsx ;; ppt) y=pptx ;; esac
  dotnet "${OLEPROBE_DLL:?set OLEPROBE_DLL}" convert "$fx/ole-props.$x" "$out/ole-props-$x.$y"
done
"$python" "$here/../validate/validate_ooxml.py" --schemas "$home/schemas/xsd/transitional" --schemas "$home/schemas/xsd/opc" --json "$out/validate.jsonl" --quiet "$out"/*.docx "$out"/*.xlsx "$out"/*.pptx > "$out/validate.txt" 2>&1
grep -h '"kind": *"summary"' "$out/validate.jsonl" || tail -5 "$out/validate.txt"
grep -h '"status": *"invalid"' "$out/validate.jsonl" | cut -c1-600
export LO_PROFILE="$out/.lo-profile"
for f in "$out"/*.docx "$out"/*.xlsx "$out"/*.pptx; do
  if timeout 180 "$home/oracles/soffice.sh" --convert-to pdf --outdir "$out/pdf" "$f" > /dev/null 2>&1; then echo "libreoffice opened $(basename "$f")"; else echo "libreoffice FAILED $(basename "$f")"; fi
done
