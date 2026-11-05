#!/usr/bin/env bash
# Converts the originals and the edited files of a sample (per edit class and corpus set) to CSV with headless LibreOffice and compares them.
#
#   tools/corpus/features_lo.sh FEATDIR OUTDIR [SAMPLE_PER_SET] [JOBS] [MAX_MINUTES]
#
# Needs ONEDOCUMENT_SOFFICE (a launcher that honours LO_PROFILE) and PYTHON. The table class adds cells, so it is only checked for "opens and converts".
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
feat="${1:?usage: features_lo.sh FEATDIR OUTDIR [SAMPLE_PER_SET] [JOBS] [MAX_MINUTES]}"
out="${2:?}"
sample="${3:-34}"
jobs="${4:-8}"
minutes="${5:-12}"
home="${ONEDOCUMENT_HOME:-$HOME/oneoffice}"
mkdir -p "$out"
export here feat out sample minutes home
for c in style cf dv name table; do
  for s in commoncrawl/xlsx bugzilla/documentfoundation/xlsx lo-xlsx; do
    echo "$c $s"
  done
done | xargs -P "$jobs" -L1 bash -c '
  c=$0; s=$1; n=${s//\//-}
  export LO_PROFILE="$out/profile-$c-$n"
  extra=""; [ "$c" = table ] && extra="--open-only"
  "${PYTHON:-python3}" "$here/xlsx_edit_checks.py" lo "$feat/$n" "$home/corpus/$s" --edited "ed-$c" $extra \
    --sample "$sample" --max-minutes "$minutes" > "$out/$c-$n.json" 2>&1
  rm -rf "$out/profile-$c-$n"'
for f in "$out"/*.json; do echo "$(basename "$f"): $(cut -c1-500 "$f")"; done
