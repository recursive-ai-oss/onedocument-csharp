#!/usr/bin/env bash
# Times LibreOffice headless on the rendering inputs, for the information rows next to OneDocument's own: one process per conversion
# (so start-up is included, as it is for anyone shelling out), three repetitions, median in milliseconds. LibreOffice is run as a
# binary only.
#
#   bench/lo-info.sh INPUTS_DIR OUT.tsv [soffice]
#
# Set LO_PROFILE to a scratch directory for LibreOffice's profile.
set -euo pipefail
inputs="${1:?inputs dir}"
out="${2:?out.tsv}"
soffice="${3:-soffice}"
profile="${LO_PROFILE:-${TMPDIR:-/tmp}/lo-profile}"
work="$(mktemp -d "${TMPDIR:-/tmp}/lo-info.XXXXXX")"
trap 'rm -rf "$work"' EXIT
printf 'row\tinput\treps\tmedian_ms\tversion\n' > "$out"
version="$("$soffice" --version 2>/dev/null | head -1 | tr '\t' ' ')"
timeit() { # row input filter
  local times=() i
  for i in 1 2 3; do
    local t0 t1
    t0=$(date +%s%N)
    "$soffice" --headless -env:UserInstallation="file://$profile" --convert-to "$3" --outdir "$work" "$2" > /dev/null 2>&1
    t1=$(date +%s%N)
    times+=($(( (t1 - t0) / 1000000 )))
  done
  local med
  med=$(printf '%s\n' "${times[@]}" | sort -n | sed -n 2p)
  printf '%s\t%s\t3\t%s\t%s\n' "$1" "$(basename "$2")" "$med" "$version" >> "$out"
}
timeit libreoffice.render.docx "$inputs/small.docx" pdf
timeit libreoffice.render.docx "$inputs/medium.docx" pdf
timeit libreoffice.doc.extract "$inputs/legacy/medium.doc" txt:Text
timeit libreoffice.ods.model "$inputs/legacy/values-10000.ods" csv
