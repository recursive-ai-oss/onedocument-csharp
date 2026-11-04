#!/usr/bin/env bash
# Builds a folder of .docx files for the typed-view walk test (tests/OneOffice.Schema.Tests/WalkTests.cs) by converting legacy
# .doc files with LibreOffice headless, run as a black-box converter.
#
#   make-docx-walk-corpus.sh <soffice-wrapper> <folder-with-doc-files> <output-folder> <count> <profile-dir>
#
# The output is oracle-written and is never committed.
set -euo pipefail
soffice=$1; src=$2; out=$3; count=$4; profile=$5
mkdir -p "$out" "$profile"
find "$src" -maxdepth 2 -name '*.doc' -size -400k | sort > "$out/.all"
head -n "$count" "$out/.all" > "$out/.list"
LO_PROFILE="$profile" "$soffice" --headless --convert-to docx --outdir "$out" $(cat "$out/.list") > "$out/.log" 2>&1 || true
rm -f "$out/.list" "$out/.all"
ls "$out"/*.docx | wc -l
