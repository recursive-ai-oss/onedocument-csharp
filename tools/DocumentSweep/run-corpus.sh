#!/usr/bin/env bash
# Runs the open/save sweep over corpus sets and prints each summary.
# usage: run-corpus.sh <bin-dir> <out-dir> [--edit] [--limit N] <corpus-dir>...
# <bin-dir> holds the published DocumentSweep (sweep/) and RoundTrip (rt/) tools.
set -u
bin=$1; out=$2; shift 2
extra=()
sets=()
while [ $# -gt 0 ]; do
  case $1 in
    --edit) extra+=(--edit);;
    --limit) extra+=(--limit "$2"); shift;;
    *) sets+=("$1");;
  esac
  shift
done
for s in "${sets[@]}"; do
  name=$(echo "$s" | tr '/' '_')
  echo "== $s"
  python3 "$(dirname "$0")/sweep.py" --input "$s" --out "$out/$name" --sweep "$bin/sweep/DocumentSweep.dll" --roundtrip "$bin/rt/RoundTrip.dll" --jobs 6 "${extra[@]}"
done
