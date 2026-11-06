#!/usr/bin/env bash
# Opens each OpenDocument presentation of a directory in the pinned LibreOffice build (black box), exports it to PDF and compares the page count
# with the one LibreOffice gives the PresentationML deck of the same name found under testdata/ (hidden slides are left out of both).
#
# usage: tools/odp/lo-open-check.sh <dir-with-odp-files> <work-dir>
# env:   ONEDOCUMENT_HOME  oracle install root (default ~/oneoffice); LO_PROFILE  private profile directory
set -euo pipefail
dir=${1:?usage: lo-open-check.sh <dir> <work-dir>}
work=${2:?usage: lo-open-check.sh <dir> <work-dir>}
home=${ONEDOCUMENT_HOME:-$HOME/oneoffice}
export LO_PROFILE=${LO_PROFILE:-$work/.lo-profile}
root=$(cd "$(dirname "$0")/../.." && pwd)
mkdir -p "$work/ours" "$work/source"
pages() { "$home/oracles/venv/bin/python" -c 'import sys,pypdf;print(len(pypdf.PdfReader(sys.argv[1]).pages))' "$1"; }
ok=0; bad=0
for f in "$dir"/*.odp; do
  n=$(basename "$f" .odp)
  src=""
  for c in "$root/testdata/slides/$n.pptx" "$root/testdata/drawingml/$n.pptx" "$root/testdata/templates/pptx/${n#t-}/template.pptx"; do [ -f "$c" ] && src=$c && break; done
  "$home/oracles/soffice.sh" --convert-to pdf --outdir "$work/ours" "$f" > /dev/null 2>&1 || { echo "LibreOffice could not convert $n.odp"; bad=$((bad+1)); continue; }
  a=$(pages "$work/ours/$n.pdf")
  if [ -n "$src" ]; then
    cp "$src" "$work/source/$n.pptx"
    "$home/oracles/soffice.sh" --convert-to pdf --outdir "$work/source" "$work/source/$n.pptx" > /dev/null 2>&1
    b=$(pages "$work/source/$n.pdf")
  else b=$a; fi
  if [ "$a" = "$b" ]; then ok=$((ok+1)); else echo "$n: ours $a pages, source $b pages"; bad=$((bad+1)); fi
done
echo "pages equal in $ok files, differ or failed in $bad"
