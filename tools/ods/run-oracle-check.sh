#!/usr/bin/env bash
# Round trips the workbooks in testdata through OpenDocument with the reference reader in between (see OdsOracleTests).
# Usage: ONEDOCUMENT_HOME=$HOME/oneoffice tools/ods/run-oracle-check.sh WORK [extra-input-dir[:dir...]]
# Needs the .NET SDK on PATH. Writes WORK/export.tsv, WORK/validate-*.txt, WORK/compare.tsv.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="${1:?work directory}"
mkdir -p "$work"
export ONEDOCUMENT_ODS_WORK="$work"
[[ -n "${2:-}" ]] && export ONEDOCUMENT_ODS_INPUTS="$2"
export ONEDOCUMENT_ARTIFACTS="${ONEDOCUMENT_ARTIFACTS:-$work/artifacts}"
project="$root/tests/OneDocument.Spreadsheets.Tests/OneDocument.Spreadsheets.Tests.csproj"
dotnet build "$project" -c Release -v quiet -clp:NoSummary
exe="$ONEDOCUMENT_ARTIFACTS/bin/OneDocument.Spreadsheets.Tests/release/RecursiveAI.OneDocument.Spreadsheets.Tests"
"$exe" -class '*OdsOracleTests' -method '*Export' | tail -3
python3 "$root/tools/ods/check_with_reference.py" "$work"
"$exe" -class '*OdsOracleTests' -method '*Compare' | tail -3
tail -1 "$work/compare.tsv"
