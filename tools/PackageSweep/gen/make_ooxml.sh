#!/usr/bin/env bash
# Generates the OOXML files the package sweep uses, then has LibreOffice rewrite them (a second writer) in OOXML and the template variants.
#
#   tools/PackageSweep/gen/make_ooxml.sh OUT_DIR
#
# Environment: ONEOFFICE_PYTHON  a python with python-docx, openpyxl and python-pptx (default: python3)
#              ONEOFFICE_SOFFICE a soffice launcher (default: soffice); LO_PROFILE a profile directory of your own
set -euo pipefail
out="${1:?usage: make_ooxml.sh OUT_DIR}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mkdir -p "$out/lo"
"${ONEOFFICE_PYTHON:-python3}" "$here/make_ooxml.py" "$out"
soffice="${ONEOFFICE_SOFFICE:-soffice}"
convert() { # filter-extension, input, [output directory]
  "$soffice" --headless --convert-to "$1" --outdir "${3:-$out/lo}" "$2" >/dev/null 2>&1 || echo "convert $1 $2 failed" >&2
}
for f in small.docx medium.docx; do convert docx "$out/$f"; convert dotx "$out/$f"; convert odt "$out/$f"; done
for f in small.xlsx large.xlsx; do convert xlsx "$out/$f"; convert xltx "$out/$f"; done
for f in small.pptx large.pptx; do convert pptx "$out/$f"; convert potx "$out/$f"; done
# And back from ODF, so the writer starts from a different model.
mkdir -p "$out/lo-from-odf"
for f in "$out"/lo/*.odt; do convert docx "$f" "$out/lo-from-odf"; done
ls "$out" "$out/lo" "$out/lo-from-odf" | head -50
