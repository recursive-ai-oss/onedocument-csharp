#!/usr/bin/env bash
# Layout differential: renders the generated layout cases with OneDocument, asks the Word oracle for its own PDF export of the
# same files, and compares page counts and per-page text lines. Slide decks (slide-*.pptx) go through PowerPoint the same way.
#
#   layout-differential.sh [OUTDIR]
#
# Environment:
#   OFFICE_ORACLE_SSH, OFFICE_ORACLE_SSH_KEY   the Windows machine (see office-oracle/README.md)
#   ONEDOCUMENT_HOME      results go to $ONEDOCUMENT_HOME/results/layout unless OUTDIR is given
#   ONEDOCUMENT_FONTS     optional directory of extra font files for our side (colon-separated list allowed)
#   ONEDOCUMENT_INSTANCE  name written into the shared oracle lock
#
# Only generated files are sent to the machine. Both sides should use the same installed fonts for the comparison to mean
# anything; the substitute-font run on Linux is information only. Output: ours/NAME.pdf, word/NAME.pdf, summary.jsonl.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
out=${1:-${ONEDOCUMENT_HOME:?set ONEDOCUMENT_HOME or pass OUTDIR}/results/layout}
mkdir -p "$out/cases" "$out/ours" "$out/word" "$out/powerpoint"
probe=(dotnet run --project "$here/LayoutProbe" -c Release --)

"${probe[@]}" gen "$out/cases"
IFS=: read -r -a fonts <<< "${ONEDOCUMENT_FONTS:-}"
"${probe[@]}" render "$out/cases" "$out/ours" "${fonts[@]}"

"$here/office-oracle/office-oracle.sh" run --run layout --instance "${ONEDOCUMENT_INSTANCE:-onedocument}" --pdf --out "$out/oracle" "$out/cases"
for f in "$out"/oracle/*.word.pdf; do
  [ -e "$f" ] || continue
  name=$(basename "$f" .word.pdf)
  cp "$f" "$out/word/$name.pdf"
done

# PowerPoint's own PDFs of the generated slide decks (slide-*.pptx) are kept in powerpoint/ next to ours/, and compared the same way:
# page count (one page per visible slide) and the text lines of each page.
for f in "$out"/oracle/*.powerpoint.pdf; do
  [ -e "$f" ] || continue
  cp "$f" "$out/powerpoint/$(basename "$f" .powerpoint.pdf).pdf"
done
"${probe[@]}" compare "$out/ours" "$out/powerpoint" | tee "$out/summary-powerpoint.jsonl" || true

# Exit status 1 means some page count differs; the summary lines are kept either way.
status=0
"${probe[@]}" compare "$out/ours" "$out/word" | tee "$out/summary.jsonl" || status=$?
exit $status
