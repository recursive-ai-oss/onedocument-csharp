#!/usr/bin/env bash
# Run several fuzz targets side by side, one process (one core) each, from one prepared build, and print a results row per target.
#
#   tools/fuzz-campaign.sh prepare
#   tools/fuzz-campaign.sh run <seconds> <target>...
#
# Environment: ONEDOCUMENT_HOME (as for fuzz-run.sh). Builds, corpora, findings and logs live under $ONEDOCUMENT_HOME/fuzz.
# Row columns: target, seconds, executions, execs/s, coverage features, findings (files in the findings folder), exit code.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
home="${ONEDOCUMENT_HOME:?set ONEDOCUMENT_HOME}"
export ONEDOCUMENT_FUZZ_WORK="${ONEDOCUMENT_FUZZ_WORK:-$home/fuzz}"
export LIBFUZZER_DOTNET="${LIBFUZZER_DOTNET:-$home/tools/libfuzzer-dotnet}"
export SHARPFUZZ="${SHARPFUZZ:-$home/tools/sharpfuzz}"
export FUZZ_PREBUILT="${FUZZ_PREBUILT:-$ONEDOCUMENT_FUZZ_WORK/prebuilt}"
mkdir -p "$ONEDOCUMENT_FUZZ_WORK/logs"
mode="${1:?usage: fuzz-campaign.sh prepare | run <seconds> <target>...}"
shift
if [[ "$mode" == prepare ]]; then
  exec "$here/../fuzz/run.sh" prepare
fi
seconds="${1:?seconds}"
shift
stamp="$(date +%Y%m%d-%H%M%S)"
for t in "$@"; do
  (
    log="$ONEDOCUMENT_FUZZ_WORK/logs/$t-$stamp.log"
    "$here/../fuzz/run.sh" "$t" "$seconds" >"$log" 2>&1
    echo $? >"$log.rc"
  ) &
done
wait
for t in "$@"; do
  log="$ONEDOCUMENT_FUZZ_WORK/logs/$t-$stamp.log"
  execs=$(grep -E '^stat::number_of_executed_units' "$log" | tail -1 | awk '{print $2}')
  eps=$(grep -E '^stat::average_exec_per_sec' "$log" | tail -1 | awk '{print $2}')
  ft=$(grep -oE 'ft: [0-9]+' "$log" | tail -1 | awk '{print $2}')
  fnd=$(find "$ONEDOCUMENT_FUZZ_WORK/findings/$t" -type f 2>/dev/null | wc -l)
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$t" "$seconds" "${execs:-?}" "${eps:-?}" "${ft:-?}" "$fnd" "$(cat "$log.rc")"
done
