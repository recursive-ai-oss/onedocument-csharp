#!/usr/bin/env bash
# Writes the spreadsheet inputs (make_inputs.py, openpyxl) and converts them to ODS and flat ODS with the pinned LibreOffice.
# Usage: ONEDOCUMENT_HOME=$HOME/.onedocument tools/ods/make-oracle-fixtures.sh OUTDIR
set -euo pipefail
home=${ONEDOCUMENT_HOME:?set ONEDOCUMENT_HOME}
out=${1:?output directory}
mkdir -p "$out"
here=$(cd "$(dirname "$0")" && pwd)
"$home/oracles/venv/bin/python" "$here/make_inputs.py" "$out"
export LO_PROFILE="$home/oracles/lo-profile-ods"
for f in ods-rich ods-big; do
  "$home/oracles/soffice.sh" --convert-to ods --outdir "$out" "$out/$f.xlsx" >/dev/null
  "$home/oracles/soffice.sh" --convert-to fods --outdir "$out" "$out/$f.xlsx" >/dev/null
done
"$home/oracles/soffice.sh" --version | head -1 > "$out/oracle-version.txt"
ls -l "$out"
