#!/usr/bin/env bash
# Converts the generated PresentationML decks in testdata/ to PowerPoint 97-2003 files with the pinned LibreOffice build, so the .ppt reader
# has files that a different writer produced from inputs we know. The converter is run as a black box.
#
# usage: tools/ppt/make-fixtures.sh <output-dir>
# env:   ONEDOCUMENT_HOME  oracle install root (default ~/.onedocument); LO_PROFILE  private profile directory
set -euo pipefail
out=${1:?usage: make-fixtures.sh <output-dir>}
home=${ONEDOCUMENT_HOME:-$HOME/.onedocument}
export LO_PROFILE=${LO_PROFILE:-$out/.lo-profile}
mkdir -p "$out"
root=$(cd "$(dirname "$0")/../.." && pwd)
"$home/oracles/soffice.sh" --version > "$out/oracle-version.txt" 2>&1 || true
for deck in "$root"/testdata/slides/*.pptx "$root"/testdata/drawingml/drawing-slide-shapes.pptx \
            "$root"/testdata/templates/pptx/placeholders/template.pptx "$root"/testdata/templates/pptx/table-rows/template.pptx \
            "$root"/testdata/templates/pptx/slide-per-item-with-notes/template.pptx "$root"/testdata/templates/pptx/hyperlink-target/template.pptx; do
  [ -f "$deck" ] || continue
  name=$(basename "$(dirname "$deck")")-$(basename "$deck" .pptx)
  name=${name#slides-}
  cp "$deck" "$out/$name.pptx"
  "$home/oracles/soffice.sh" --convert-to ppt:"MS PowerPoint 97" --outdir "$out" "$out/$name.pptx" > /dev/null 2>&1 || echo "failed: $name"
done
ls -l "$out"/*.ppt | wc -l
