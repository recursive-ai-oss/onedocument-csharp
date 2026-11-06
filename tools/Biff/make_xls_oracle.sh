#!/bin/bash
# Converts the generated SpreadsheetML inputs to legacy .xls files with LibreOffice (black box). LibreOffice writes BIFF8 only
# ("MS Excel 97"); its BIFF5 and older filters are import-only, so older dialects come from the test writers.
# usage: make_xls_oracle.sh <soffice> <input dir> <output dir> <profile dir>
set -e
SOFFICE=$1; IN=$2; OUT=$3; PROFILE=$4
mkdir -p "$OUT"
for f in "$IN"/*.xlsx; do
  "$SOFFICE" --headless -env:UserInstallation=file://"$PROFILE" --convert-to 'xls:MS Excel 97' --outdir "$OUT" "$f" >/dev/null 2>&1
done
