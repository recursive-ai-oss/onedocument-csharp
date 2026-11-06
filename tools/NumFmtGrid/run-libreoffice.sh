#!/usr/bin/env bash
# Writes the grid, lets LibreOffice (headless, black box) render both workbooks to CSV "as shown", and compares with our manifest.
# Runs on a build host. Output under $ONEDOCUMENT_HOME/numfmt-grid/grid.
set -euo pipefail
export PATH=~/dotnet10:$PATH DOTNET_ROOT=~/dotnet10 DOTNET_PROCESSOR_COUNT=4 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
[ ! -e ~/WORKER-BENCH.lock ] || { echo "bench lock present"; exit 3; }
home=${ONEDOCUMENT_HOME:?set ONEDOCUMENT_HOME}
out=$home/numfmt-grid/grid
rm -rf "$out"; mkdir -p "$out/lo"
here=$(pwd)
ONEDOCUMENT_ARTIFACTS=$home/numfmt-grid/artifacts nice dotnet build tools/NumFmtGrid/NumFmtGrid.csproj -m:4 -v quiet -clp:NoSummary
dotnet $home/numfmt-grid/artifacts/bin/NumFmtGrid/debug/NumFmtGrid.dll write "$out"
export LO_PROFILE=$home/numfmt-grid/lo-profile
for f in grid-text grid-cells; do
  $home/oracles/soffice.sh --headless --convert-to 'csv:Text - txt - csv (StarCalc):44,34,76,1,,0,false,true,true,false,false,-1' --outdir "$out/lo" "$out/$f.xlsx" >/dev/null 2>&1 || true
done
ls "$out/lo"
python3 "$here/tools/NumFmtGrid/lo_answers.py" "$out"
dotnet $home/numfmt-grid/artifacts/bin/NumFmtGrid/debug/NumFmtGrid.dll compare "$out/manifest.tsv" "$out/answers-lo.tsv" "$out/report-lo.tsv"
