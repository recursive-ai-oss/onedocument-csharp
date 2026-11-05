#!/usr/bin/env bash
# Run one fuzz target for a fixed time and report executions, coverage features and findings.
# Meant to run inside a time-boxed queue slot on a build host:
#
#   tools/fuzz-run.sh <target> <seconds> [libFuzzer flags...]
#
# Environment (defaults under $ONEDOCUMENT_HOME):
#   ONEDOCUMENT_FUZZ_WORK    builds, corpora and findings (default $ONEDOCUMENT_HOME/fuzz); outside the checkout
#   LIBFUZZER_DOTNET       the libFuzzer host binary (default $ONEDOCUMENT_HOME/tools/libfuzzer-dotnet)
#   SHARPFUZZ              the SharpFuzz instrumenter (default $ONEDOCUMENT_HOME/tools/sharpfuzz)
#   DOTNET                 the dotnet executable (default: dotnet on PATH)
#
# One fuzz process at a time; never alongside a build. Findings are listed with their size and a replay command;
# minimize one with the libFuzzer flags `-minimize_crash=1 -runs=10000 <file>` through this script.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
home="${ONEDOCUMENT_HOME:?set ONEDOCUMENT_HOME}"
export ONEDOCUMENT_FUZZ_WORK="${ONEDOCUMENT_FUZZ_WORK:-$home/fuzz}"
export LIBFUZZER_DOTNET="${LIBFUZZER_DOTNET:-$home/tools/libfuzzer-dotnet}"
export SHARPFUZZ="${SHARPFUZZ:-$home/tools/sharpfuzz}"
target="${1:?usage: fuzz-run.sh <target> <seconds> [libFuzzer flags...]}"
seconds="${2:?usage: fuzz-run.sh <target> <seconds> [libFuzzer flags...]}"
shift 2
mkdir -p "$ONEDOCUMENT_FUZZ_WORK/logs"
log="$ONEDOCUMENT_FUZZ_WORK/logs/$target-$(date +%Y%m%d-%H%M%S).log"

set +e
"$here/../fuzz/run.sh" "$target" "$seconds" "$ONEDOCUMENT_FUZZ_WORK/corpus/$target" "$@" >"$log" 2>&1
rc=$?
set -e

execs=$(grep -E '^stat::number_of_executed_units' "$log" | tail -1 | awk '{print $2}')
features=$(grep -oE 'ft: [0-9]+' "$log" | tail -1 | awk '{print $2}')
findings=$(find "$ONEDOCUMENT_FUZZ_WORK/findings/$target" -type f 2>/dev/null | wc -l)
echo "target=$target seconds=$seconds exit=$rc executions=${execs:-unknown} features=${features:-unknown} findings=$findings log=$log"
find "$ONEDOCUMENT_FUZZ_WORK/findings/$target" -type f 2>/dev/null | head -20 | while read -r f; do
  echo "  finding $(basename "$f") $(stat -c %s "$f") bytes; replay: fuzz/run.sh replay $target $f"
done
[[ $rc -eq 0 && $findings -eq 0 ]]
