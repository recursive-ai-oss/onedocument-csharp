#!/usr/bin/env bash
# Converts legacy .xls files to .xlsx with headless LibreOffice (run only, as a black box), to give the workbook readers
# real-world files with the breadth of a published sample set. Resumable: files already converted are skipped.
#
#   tools/corpus/make_xls_xlsx.sh <input dir with .xls files> <output dir> [max files]
#
# Environment:
#   ONEDOCUMENT_SOFFICE   the LibreOffice launcher (required)
#   LO_PROFILE          LibreOffice profile directory (default: <output dir>/lo-profile)
set -euo pipefail

in="${1:?usage: make_xls_xlsx.sh <input dir> <output dir> [max files]}"
out="${2:?usage: make_xls_xlsx.sh <input dir> <output dir> [max files]}"
max="${3:-100000}"
: "${ONEDOCUMENT_SOFFICE:?set ONEDOCUMENT_SOFFICE to the headless LibreOffice launcher}"
mkdir -p "$out"
export LO_PROFILE="${LO_PROFILE:-$out/lo-profile}"

todo=()
while IFS= read -r f; do
  base="$(basename "${f%.*}")"
  [[ -e "$out/$base.xlsx" ]] || todo+=("$f")
done < <(find "$in" -maxdepth 1 -name '*.xls' | sort | head -n "$max")

# Batches keep one failing file from costing the whole run, and let the profile settle between launches.
batch=25
for ((i = 0; i < ${#todo[@]}; i += batch)); do
  timeout 600 "$ONEDOCUMENT_SOFFICE" --convert-to xlsx --outdir "$out" "${todo[@]:i:batch}" >/dev/null 2>&1 || true
done
find "$out" -maxdepth 1 -name '*.xlsx' | wc -l
