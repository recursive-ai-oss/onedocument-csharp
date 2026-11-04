#!/usr/bin/env bash
# Runs a group of benchmark rows and writes a results directory (see bench/README.md).
#
#   bench/run.sh [--run NAME] [--filter GLOB] [--threads 1|all] [--no-peak] [--no-timing] [--force]
#
#   --run NAME      result directory name under $ONEOFFICE_RESULTS (default: UTC timestamp)
#   --filter GLOB   BenchmarkDotNet filter, e.g. '*Bench.DocxLoadSave*' (default: all document rows)
#   --threads 1     one core visible to the runtime (DOTNET_PROCESSOR_COUNT=1); 'all' leaves the machine alone
#   --no-peak       skip the fresh-process peak-memory pass
#   --no-timing     skip BenchmarkDotNet (peak-memory pass only)
#   --force         start even when the machine is busy; the override is written into env.txt
#
# Environment:
#   ONEOFFICE_RESULTS         where result directories go (default: $HOME/oneoffice/results/bench)
#   ONEOFFICE_BENCH_INPUTS    where generated inputs are kept (default: $ONEOFFICE_RESULTS/inputs)
#   ONEOFFICE_BENCH_MAXLOAD   refuse to start when the 1-minute load average is above this (default: 2.0)
#   ONEOFFICE_BENCH_DLL       a built OneOffice.Bench.dll (default: build Release and use that)
#   DOTNET                    the dotnet executable (default: dotnet from PATH)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_bin="${DOTNET:-dotnet}"
results_root="${ONEOFFICE_RESULTS:-$HOME/oneoffice/results/bench}"
run="$(date -u +%Y%m%dT%H%M%SZ)"
filter='*Bench.Docx*'
threads=all
peak=1
timing=1
force=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --run) run="$2"; shift 2 ;;
    --filter) filter="$2"; shift 2 ;;
    --threads) threads="$2"; shift 2 ;;
    --no-peak) peak=0; shift ;;
    --no-timing) timing=0; shift ;;
    --force) force=1; shift ;;
    *) echo "unknown option '$1'" >&2; exit 2 ;;
  esac
done

out="$results_root/$run"
inputs="${ONEOFFICE_BENCH_INPUTS:-$results_root/inputs}"
maxload="${ONEOFFICE_BENCH_MAXLOAD:-2.0}"
mkdir -p "$out" "$inputs"
export ONEOFFICE_BENCH_INPUTS="$inputs"

# Load average: /proc on Linux, sysctl on macOS.
loadavg3() {
  if [[ -r /proc/loadavg ]]; then cut -d' ' -f1-3 /proc/loadavg; else sysctl -n vm.loadavg | tr -d '{}' | awk '{print $1, $2, $3}'; fi
}
load1() { loadavg3 | cut -d' ' -f1; }
snapshot() {
  echo "[$1] $(date -u +%FT%TZ) load1/5/15: $(loadavg3)"
}

# Refuse to time on a busy machine.
start_load="$(load1)"
if awk -v l="$start_load" -v m="$maxload" 'BEGIN { exit !(l > m) }'; then
  if [[ $force -eq 0 ]]; then
    echo "load average $start_load is above $maxload; not starting (use --force to override, recorded in the results)" >&2
    exit 3
  fi
  forced="forced start at load $start_load (limit $maxload)"
else
  forced="none"
fi

{
  echo "run: $run"
  echo "filter: $filter"
  echo "threads: $threads"
  echo "load override: $forced"
  if [[ -r /proc/cpuinfo ]]; then
    echo "cpu: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2- | sed 's/^ //')"
    echo "cores: $(nproc --all) (visible to this run: $(nproc))"
    echo "isa: $(grep -m1 '^flags' /proc/cpuinfo | tr ' ' '\n' | grep -E '^(avx2|avx512f|bmi2|sse4_2|pclmulqdq|aes|sha_ni)$' | tr '\n' ' ')"
    echo "memory: $(awk '/MemTotal/ {printf "%.0f GB", $2/1048576}' /proc/meminfo)"
  else
    echo "cpu: $(sysctl -n machdep.cpu.brand_string)"
    echo "cores: $(sysctl -n hw.ncpu) (performance $(sysctl -n hw.perflevel0.physicalcpu 2>/dev/null || echo ?), efficiency $(sysctl -n hw.perflevel1.physicalcpu 2>/dev/null || echo ?))"
    echo "isa: $(uname -m) (NEON)"
    echo "memory: $(( $(sysctl -n hw.memsize) / 1073741824 )) GB"
  fi
  echo "storage (inputs): $(df -hP "$inputs" | awk 'NR==2 {print $1, $2}')"
  echo "os: $(uname -srm)"
  echo "sdk: $("$dotnet_bin" --version)"
  snapshot before
} > "$out/env.txt"

dll="${ONEOFFICE_BENCH_DLL:-}"
if [[ -z "$dll" ]]; then
  "$dotnet_bin" build "$root/bench/OneOffice.Bench/OneOffice.Bench.csproj" -c Release -v quiet -clp:NoSummary >"$out/build.log" 2>&1
  # The repository's Directory.Build.props sends build output to $ONEOFFICE_ARTIFACTS (default: artifacts/ at the root).
  dll="$(find "${ONEOFFICE_ARTIFACTS:-$root/artifacts}/bin/OneOffice.Bench/release" -name 'OneOffice.Bench.dll' | head -1)"
fi

prefix=()
if [[ "$threads" == 1 ]]; then
  export DOTNET_PROCESSOR_COUNT=1
fi

"${prefix[@]}" "$dotnet_bin" "$dll" versions > "$out/versions.txt"
"${prefix[@]}" "$dotnet_bin" "$dll" gen "$inputs" > "$out/inputs.txt"

# Sample the load every 10 s while the benchmarks run; stopped when this script exits.
( while true; do snapshot during >> "$out/load.log"; sleep 10; done ) &
sampler=$!
trap 'kill "$sampler" 2>/dev/null || true' EXIT

if [[ $timing -eq 1 ]]; then
  "${prefix[@]}" "$dotnet_bin" "$dll" --filter "$filter" --artifacts "$out/bdn" > "$out/bdn.log" 2>&1
fi
if [[ $peak -eq 1 ]]; then
  "${prefix[@]}" "$dotnet_bin" "$dll" peakall "$inputs" "$out/peak.tsv" > "$out/peak.log" 2>&1
fi

kill "$sampler" 2>/dev/null || true
trap - EXIT
snapshot after >> "$out/env.txt"
"$dotnet_bin" "$dll" report "$out"
