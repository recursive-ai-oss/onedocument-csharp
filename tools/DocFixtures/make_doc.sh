#!/usr/bin/env bash
# Writes the DOCX inputs (make_docx.py) and has a word processor save them as Word 97-2003 files.
#
#   make_doc.sh <out dir>
#
# Environment: SOFFICE is a headless LibreOffice launcher (default: soffice); LO_PROFILE, when the launcher honours it, selects a
# private profile. The oracle is only run. The version it reports goes into <out dir>/oracle-version.txt.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="${1:?usage: make_doc.sh <out dir>}"
soffice="${SOFFICE:-soffice}"
work="$(mktemp -d "${TMPDIR:-.}/docfixtures.XXXXXX")"
trap 'rm -rf "$work"' EXIT
mkdir -p "$out"
python3 "$here/make_docx.py" "$work"
for f in "$work"/*.docx; do
  "$soffice" --headless --convert-to 'doc:MS Word 97' --outdir "$out" "$f" >/dev/null
done
"$soffice" --version | head -1 >"$out/oracle-version.txt"
