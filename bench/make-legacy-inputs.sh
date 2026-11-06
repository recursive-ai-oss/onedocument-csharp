#!/usr/bin/env bash
# Makes the legacy-format inputs of the format rows with LibreOffice (run as a binary, never read): .xls from the generated
# workbooks, and .doc, .rtf, .odt and .ods for the information rows. Inputs are never committed.
#
#   bench/make-legacy-inputs.sh INPUTS_DIR [soffice]
#
# INPUTS_DIR is the directory `fmtbench gen` and `gen` wrote into; the results go to INPUTS_DIR/legacy. Set LO_PROFILE to a
# scratch directory for LibreOffice's profile.
set -euo pipefail
inputs="${1:?inputs dir}"
soffice="${2:-soffice}"
profile="${LO_PROFILE:-${TMPDIR:-/tmp}/lo-profile}"
out="$inputs/legacy"
mkdir -p "$out"
convert() { # filter file
  "$soffice" --headless -env:UserInstallation="file://$profile" --convert-to "$1" --outdir "$out" "$2" > /dev/null 2>&1
}
for n in 10000 50000; do convert 'xls:MS Excel 97' "$inputs/xlsx/values-$n.xlsx"; done
convert ods "$inputs/xlsx/values-10000.xlsx"
convert 'doc:MS Word 97' "$inputs/medium.docx"
convert rtf "$inputs/medium.docx"
convert odt "$inputs/medium.docx"
( cd "$out" && { sha256sum $(ls | grep -v manifest) 2>/dev/null || shasum -a 256 $(ls | grep -v manifest); } ) > "$out.sha256" && mv "$out.sha256" "$out/manifest.sha256"
