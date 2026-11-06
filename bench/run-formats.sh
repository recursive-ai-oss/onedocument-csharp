#!/usr/bin/env bash
# Runs format groups one after another with bench/run.sh, one result directory per group: <prefix>-<group>.
#
#   bench/run-formats.sh PREFIX GROUP...      groups: see `fmtbench groups`
#
# Environment (as for run.sh): ONEDOCUMENT_RESULTS, ONEDOCUMENT_BENCH_INPUTS, ONEDOCUMENT_BENCH_DLL (a built OneDocument.Bench.dll).
# The LibreOffice-made inputs are expected in $ONEDOCUMENT_BENCH_INPUTS/legacy; when that directory is missing they are made with
# the soffice named by SOFFICE (default: soffice on the path). RUN_FLAGS adds flags to run.sh (for example --force).
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
prefix="${1:?prefix}"
shift
inputs="${ONEDOCUMENT_BENCH_INPUTS:?set ONEDOCUMENT_BENCH_INPUTS}"
dll="${ONEDOCUMENT_BENCH_DLL:?set ONEDOCUMENT_BENCH_DLL}"
dotnet_bin="${DOTNET:-dotnet}"
mkdir -p "$inputs"
if [[ ! -e "$inputs/legacy/medium.doc" ]]; then
  "$dotnet_bin" "$dll" gen "$inputs" > /dev/null
  XLSXBENCH_SIZES=10000,50000 "$dotnet_bin" "$dll" fmtbench gen "$inputs" > /dev/null
  bench/make-legacy-inputs.sh "$inputs" "${SOFFICE:-soffice}"
fi
for g in "$@"; do
  # shellcheck disable=SC2086
  bench/run.sh --run "$prefix-$g" --fmt "$g" --threads 1 ${RUN_FLAGS:-}
done
