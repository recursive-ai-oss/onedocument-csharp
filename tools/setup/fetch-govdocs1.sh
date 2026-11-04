#!/usr/bin/env bash
# Fetches govdocs1 thread zips (1000 files each) and extracts the Office files.
#
#   fetch-govdocs1.sh [threads]        default 10 (000-009)
#   ONEOFFICE_CORPUS     corpus root (default ~/oneoffice/corpus); files land in $ONEOFFICE_CORPUS/govdocs1/files
#   ONEOFFICE_PAUSE_FILE optional: while this file exists the loop waits
#
# Network-bound. Fetch-only: the files are never committed or redistributed.
set -u
N=${1:-10}
C="${ONEOFFICE_CORPUS:-$HOME/oneoffice/corpus}/govdocs1"
mkdir -p "$C"/zip "$C"/files
cd "$C"
for i in $(seq 0 $((N-1))); do
  t=$(printf %03d "$i")
  while [ -n "${ONEOFFICE_PAUSE_FILE:-}" ] && [ -e "$ONEOFFICE_PAUSE_FILE" ]; do sleep 30; done
  [ -s zip/$t.zip ] || curl -fsSL -o zip/$t.zip.part "https://downloads.digitalcorpora.org/corpora/files/govdocs1/zipfiles/$t.zip" && mv -f zip/$t.zip.part zip/$t.zip 2>/dev/null
  python3 - "zip/$t.zip" files <<'PY'
import zipfile, sys, os
z = zipfile.ZipFile(sys.argv[1])
keep = ('.doc','.docx','.xls','.xlsx','.ppt','.pptx','.rtf','.xlsb','.odt','.ods','.odp','.csv')
n = 0
for i in z.infolist():
    if i.filename.lower().endswith(keep):
        out = os.path.join(sys.argv[2], os.path.basename(i.filename))
        if not os.path.exists(out):
            open(out, 'wb').write(z.read(i)); n += 1
print(sys.argv[1], 'extracted', n)
PY
done
sha256sum zip/*.zip > zip/SHA256SUMS
ls files | sed 's/.*\.//' | sort | uniq -c
