#!/usr/bin/env bash
# Schema-validates a sample of the files `XlsxProbe features` wrote for each edit class and corpus set.
#
#   tools/corpus/features_validate.sh FEATDIR OUTDIR [SAMPLE] [JOBS] [MAX_MINUTES]
#
# FEATDIR holds one directory per corpus set (named like the set with / replaced by -), as written by the feature sweep. Needs
# ONEDOCUMENT_XSD (as for run_checks.py) and PYTHON (a python with lxml; default python3). Corpus sets are looked up under
# $ONEDOCUMENT_HOME/corpus.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
feat="${1:?usage: features_validate.sh FEATDIR OUTDIR [SAMPLE] [JOBS] [MAX_MINUTES]}"
out="${2:?}"
sample="${3:-30}"
jobs="${4:-8}"
minutes="${5:-12}"
home="${ONEDOCUMENT_HOME:?set ONEDOCUMENT_HOME}"
mkdir -p "$out"
export here feat out sample minutes home
for c in style table cf dv name; do
  for s in commoncrawl/xlsx bugzilla/documentfoundation/xlsx lo-xlsx; do
    echo "$c $s"
  done
done | xargs -P "$jobs" -L1 bash -c '
  c=$0; s=$1; n=${s//\//-}
  "${PYTHON:-python3}" "$here/xlsx_edit_checks.py" validate "$feat/$n" "$home/corpus/$s" --edited "ed-$c" \
    --sample-validate "$sample" --max-minutes "$minutes" > "$out/$c-$n.json" 2>&1'
for f in "$out"/*.json; do echo "$(basename "$f"): $(cut -c1-400 "$f")"; done
