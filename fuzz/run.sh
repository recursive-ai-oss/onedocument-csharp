#!/usr/bin/env bash
# Build, instrument and run the fuzz target under libFuzzer.
#
#   fuzz/run.sh <target> <seconds> [corpus dir] [extra libFuzzer flags...]
#   fuzz/run.sh replay <target> <file>...      # run saved inputs once, without libFuzzer (reproduce a finding)
#
# Targets: formula, formulaeval, finfuncs, statfuncs, funcs, lookups, sniff, xml, markup, opc, cfb, sdt, sdtpath, text, revisions, stories, pptx, fieldinstr, fields, numfmt, numfmtfmt, sheet, sst, wbpart, styles, sheetfeat, table, pivot, csv, stream, streamwrite, template, richtext, mailmerge, math, sheetrender, xls, ptg   (see fuzz/README.md)
#
# Environment (nothing is baked into the script):
#   ONEDOCUMENT_FUZZ_WORK    required: where builds, corpora and findings go. Keep it outside the checkout.
#   ONEDOCUMENT_FUZZ_TOOLS   directory holding `fuzz/libfuzzer-dotnet` (the libFuzzer host) and `sharpfuzz/sharpfuzz`
#                        (the instrumenter). LIBFUZZER_DOTNET and SHARPFUZZ override the two paths individually.
#   ONEDOCUMENT_ARTIFACTS    build output root, as for every other project in the repo
#   DOTNET               the dotnet executable (default: dotnet from PATH)
#   FUZZ_TIMEOUT         per-input timeout in seconds (default 10)
#   FUZZ_RSS_MB          memory limit in MB (default 1024)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_bin="${DOTNET:-dotnet}"
work="${ONEDOCUMENT_FUZZ_WORK:?set ONEDOCUMENT_FUZZ_WORK to a scratch directory outside the checkout}"
case "$(readlink -f "$work")/" in
  "$root"/*) echo "ONEDOCUMENT_FUZZ_WORK must be outside the checkout" >&2; exit 2 ;;
esac
tools="${ONEDOCUMENT_FUZZ_TOOLS:-}"
host="${LIBFUZZER_DOTNET:-$tools/fuzz/libfuzzer-dotnet}"
instrument="${SHARPFUZZ:-$tools/sharpfuzz/sharpfuzz}"

mode=fuzz
if [[ "${1:-}" == replay ]]; then
  mode=replay
  shift
fi
target="${1:?usage: run.sh <target> <seconds> [corpus dir] | run.sh replay <target> <file>...}"
shift

# Maximum input length per target.
case "$target" in
  sniff) maxlen=262144 ;;
  fieldinstr) maxlen=4096 ;;
  numfmt|numfmtfmt) maxlen=2048 ;;
  fields) maxlen=65536 ;;
  text) maxlen=65536 ;;
  revisions) maxlen=65536 ;;
  stories) maxlen=65536 ;;
  pptx) maxlen=131072 ;;
  opc) maxlen=262144 ;;
  cfb) maxlen=262144 ;;
  csv) maxlen=65536 ;;
  xml|markup|sdt|sdtpath|sheet|sst|wbpart|styles|sheetfeat|table|pivot|stream) maxlen=262144 ;;
  streamwrite) maxlen=4096 ;;
  formula|formulaeval) maxlen=2048 ;;
  statfuncs) maxlen=64 ;;
  funcs) maxlen=512 ;;
  lookups) maxlen=256 ;;
  finfuncs) maxlen=512 ;;
  template) maxlen=2048 ;;
  mailmerge) maxlen=1024 ;;
  richtext) maxlen=4096 ;;
  math) maxlen=16384 ;;
  sheetrender) maxlen=65536 ;;
  xls) maxlen=262144 ;;
  ptg) maxlen=2048 ;;
  *) echo "unknown target '$target'" >&2; exit 2 ;;
esac

bin="$work/bin"
rm -rf "$bin"
mkdir -p "$bin"

# Build once per run: release, then instrument only the library assemblies (the harness itself needs no coverage).
"$dotnet_bin" build "$root/fuzz/OneDocument.Fuzz/OneDocument.Fuzz.csproj" -c Release -v quiet -clp:NoSummary -o "$bin"

if [[ "$mode" == replay ]]; then
  ONEDOCUMENT_FUZZ_WORK="$work" "$dotnet_bin" "$bin/RecursiveAI.OneDocument.Fuzz.dll" replay "$target" "$@"
  exit $?
fi

seconds="${1:?usage: run.sh <target> <seconds> [corpus dir]}"
shift
extra=""
if [[ $# -gt 0 && "${1:0:1}" != "-" ]]; then
  extra="$1"
  shift
fi
[[ -x "$host" ]] || { echo "libFuzzer host not found at '$host' (set ONEDOCUMENT_FUZZ_TOOLS)" >&2; exit 2; }
[[ -x "$instrument" ]] || { echo "sharpfuzz not found at '$instrument' (set ONEDOCUMENT_FUZZ_TOOLS)" >&2; exit 2; }

export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v "$dotnet_bin")")")}"
corpus="$work/corpus/$target"
findings="$work/findings/$target"
mkdir -p "$corpus" "$findings"

# Seeds come from the uninstrumented build (instrumented code needs libFuzzer's shared memory).
"$dotnet_bin" "$bin/RecursiveAI.OneDocument.Fuzz.dll" seeds "$target" "$corpus" >/dev/null
[[ -n "$(ls -A "$corpus")" ]] || printf '\x00' >"$corpus/seed-empty"

"$instrument" "$bin/RecursiveAI.OneDocument.dll" >/dev/null
[[ ! -f "$bin/RecursiveAI.OneDocument.Documents.dll" ]] || "$instrument" "$bin/RecursiveAI.OneDocument.Documents.dll" >/dev/null
[[ ! -f "$bin/RecursiveAI.OneDocument.Spreadsheets.dll" ]] || "$instrument" "$bin/RecursiveAI.OneDocument.Spreadsheets.dll" >/dev/null
[[ ! -f "$bin/RecursiveAI.OneDocument.Rendering.dll" ]] || "$instrument" "$bin/RecursiveAI.OneDocument.Rendering.dll" >/dev/null
"$instrument" "$bin/RecursiveAI.OneArchive.dll" >/dev/null

# An extra seed directory is read-only for libFuzzer: new finds go to $corpus.
dirs=("$corpus")
[[ -z "$extra" ]] || dirs+=("$extra")

# Run. -max_total_time bounds the run; crashes, hangs and out-of-memory inputs are written to the findings folder.
ONEDOCUMENT_FUZZ_TARGET="$target" ONEDOCUMENT_FUZZ_WORK="$work" "$host" \
  --target_path="$(command -v "$dotnet_bin")" --target_arg="$bin/RecursiveAI.OneDocument.Fuzz.dll" \
  -max_total_time="$seconds" -timeout="${FUZZ_TIMEOUT:-10}" -rss_limit_mb="${FUZZ_RSS_MB:-1024}" \
  -max_len="$maxlen" -artifact_prefix="$findings/" -print_final_stats=1 "$@" "${dirs[@]}"
