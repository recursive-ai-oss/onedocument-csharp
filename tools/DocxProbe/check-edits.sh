#!/usr/bin/env bash
# Applies the probe's style-and-list edit to up to N documents of a directory, then validates each result against the pinned
# schemas and converts it with headless LibreOffice. Prints one summary line per check.
#
#   check-edits.sh INPUT_DIR WORK_DIR [N]
#
# Environment: PROBE_CMD (edit or annotate, default edit), DOCXPROBE (the built tool), ONEOFFICE_PYTHON (a python with lxml), ONEOFFICE_SOFFICE and LO_PROFILE (see tools/README.md).
set -u
in=${1:?input dir}; work=${2:?work dir}; n=${3:-60}
here=$(cd "$(dirname "$0")" && pwd); root=$(cd "$here/../.." && pwd)
mkdir -p "$work/edited" "$work/lo"
ok=0; failed=0
for f in $(find "$in" -name '*.docx' | sort | head -n "$n"); do
  out="$work/edited/$(basename "$f")"
  if "$DOCXPROBE" ${PROBE_CMD:-edit} "$f" "$out" >/dev/null 2>>"$work/edit.err"; then ok=$((ok+1)); else failed=$((failed+1)); rm -f "$out"; fi
done
echo "edited ok=$ok failed=$failed"
"${ONEOFFICE_PYTHON:-python3}" "$root/tools/validate/validate_ooxml.py" --quiet --json "$work/validate.jsonl" \
  --schemas "$root/schemas/ecma376/transitional" --schemas "$root/schemas/ecma376/opc" "$work"/edited/*.docx > "$work/validate.out" 2>&1
echo "validate exit=$? $(tail -1 "$work/validate.out")"
for f in "$work"/edited/*.docx; do timeout 120 "$ONEOFFICE_SOFFICE" -env:UserInstallation="file://${LO_PROFILE:-$work/lo-profile}" --headless --convert-to txt:Text --outdir "$work/lo" "$f" >/dev/null 2>&1 || echo "lo failed: $(basename "$f")" >>"$work/lo.err"; done
echo "libreoffice converted=$(ls "$work/lo" | wc -l) of $ok"
