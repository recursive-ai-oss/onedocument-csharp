#!/usr/bin/env bash
# Memory rows for the forward cursors over binary workbooks: for growing inputs, one fresh process per row reports the peak working set
# and the bytes allocated by one pass over every cell (strings built by GetText included).
#
#   bench/cursor-memory.sh
#
# Environment:
#   ONEDOCUMENT_BENCH_DLL     a built OneDocument.Bench.dll (required)
#   ONEDOCUMENT_BENCH_INPUTS  the inputs of `fmtbench gen` (values-N.xlsb, legacy/values-N.xls) and xlsx/values-N.xlsx for the sizes below (required)
#   SOFFICE                   a LibreOffice command line that converts files, used once to make the largest .xls (default: soffice)
#   DOTNET                    the dotnet executable (default: dotnet from PATH)
#
# A BIFF8 sheet holds at most 65,536 rows, so the .xls rows stop there; the .xlsb rows go to 1,000,000 rows.
set -euo pipefail
dll="${ONEDOCUMENT_BENCH_DLL:?set ONEDOCUMENT_BENCH_DLL}"
inputs="${ONEDOCUMENT_BENCH_INPUTS:?set ONEDOCUMENT_BENCH_INPUTS}"
dotnet_bin="${DOTNET:-dotnet}"
soffice="${SOFFICE:-soffice}"
export DOTNET_PROCESSOR_COUNT=1

for n in 10000 100000 1000000; do
  [[ -e "$inputs/values-$n.xlsb" ]] || "$dotnet_bin" "$dll" fmtbench toxlsb "$inputs/xlsx/values-$n.xlsx" "$inputs/values-$n.xlsb"
done
if [[ ! -e "$inputs/legacy/values-100000.xls" ]]; then
  "$soffice" --headless --convert-to 'xls:MS Excel 97' --outdir "$inputs/legacy" "$inputs/xlsx/values-100000.xlsx" > /dev/null
fi

echo -e "row\tfile\treps\tmin_ms\tmedian_ms\talloc_bytes_per_pass\tpeak_ws_mb\tchecksum"
for f in legacy/values-10000.xls legacy/values-50000.xls legacy/values-100000.xls; do
  "$dotnet_bin" "$dll" fmtbench run onedocument.xls.read "$inputs/$f" 3
done
for n in 10000 100000 1000000; do
  "$dotnet_bin" "$dll" fmtbench run onedocument.xlsb.read "$inputs/values-$n.xlsb" 3
done
