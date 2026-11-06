#!/usr/bin/env bash
# Writes the OpenDocument inputs (make_ole_inputs.py) and has an office suite save them as Word, Excel and PowerPoint 97-2003 files.
#
#   make_ole_fixtures.sh <out dir>
#
# Environment: SOFFICE is a headless LibreOffice launcher (default: soffice); LO_PROFILE, when the launcher honours it, selects a
# private profile. The suite is only run; the version it reports goes into <out dir>/oracle-version.txt.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="${1:?usage: make_ole_fixtures.sh <out dir>}"
soffice="${SOFFICE:-soffice}"
work="$(mktemp -d "${TMPDIR:-.}/olefixtures.XXXXXX")"
trap 'rm -rf "$work"' EXIT
mkdir -p "$out"
python3 "$here/make_ole_inputs.py" "$work"
"$soffice" --headless --convert-to 'doc:MS Word 97' --outdir "$out" "$work/ole-props.odt" >/dev/null
"$soffice" --headless --convert-to 'xls:MS Excel 97' --outdir "$out" "$work/ole-props.ods" >/dev/null
"$soffice" --headless --convert-to 'ppt:MS PowerPoint 97' --outdir "$out" "$work/ole-props.odp" >/dev/null
"$soffice" --version | head -1 >"$out/oracle-version.txt"
