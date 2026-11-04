#!/usr/bin/env bash
# Installs the black-box oracles user-locally under $ONEOFFICE_HOME/oracles (default ~/oneoffice). Idempotent;
# every download is checked against a pinned sha256. Oracles are only ever executed; their installed files are
# never read. No sudo and nothing system-wide.
#
#   ONEOFFICE_HOME   root for oracles, corpora and results (default ~/oneoffice)
#   UV               path of the uv binary (default: uv on PATH)
set -euo pipefail
O="${ONEOFFICE_HOME:-$HOME/oneoffice}/oracles"
UV="${UV:-uv}"
mkdir -p "$O"/dl
cd "$O"
log() { echo "== $*"; }
fetch() { # url file sha256
  [ -s "dl/$2" ] || curl -fsSL -o "dl/$2" "$1"
  echo "$3  dl/$2" | sha256sum -c -
}

# Java runtime (for Jing; later veraPDF)
JRE_URL=https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.13%2B9/OpenJDK21U-jre_x64_linux_hotspot_21.0.13_9.tar.gz
fetch "$JRE_URL" jre21.tar.gz a535a1df9c6284d9618872984e026ae0352d1f7e9e0605425c0e6114f47351dd
[ -x jre/bin/java ] || { mkdir -p jre && tar -xzf dl/jre21.tar.gz -C jre --strip-components=1; }
jre/bin/java -version 2>&1 | head -1

# Jing (RELAX NG validator, for the ODF schemas)
JING_URL=https://github.com/relaxng/jing-trang/releases/download/V20241231/jing-20241231.zip
fetch "$JING_URL" jing.zip d11a765f9106e398e01d66aaffb629beb1da21f8a716299e2930a751130bfad2
[ -d jing ] || { mkdir -p jing && (cd jing && python3 -c 'import zipfile;zipfile.ZipFile("../dl/jing.zip").extractall(".")'); }
JING_JAR=$(find jing -name 'jing*.jar' -path '*bin*' | head -1)
cat > jing.sh <<EOF
#!/usr/bin/env bash
exec "$O/jre/bin/java" -jar "$O/$JING_JAR" "\$@"
EOF
chmod +x jing.sh

# LibreOffice, the release deb tarball extracted user-locally (no dpkg install)
LO_VER=26.8.2
LO_URL=https://download.documentfoundation.org/libreoffice/stable/$LO_VER/deb/x86_64/LibreOffice_${LO_VER}_Linux_x86-64_deb.tar.gz
fetch "$LO_URL" lo.tar.gz 8290c8f46cb33d6df8f681cb796e0697461ea070f86ec55b63c94d7eb9d9a476
if [ ! -d libreoffice/root ]; then
  mkdir -p libreoffice/debs libreoffice/root
  tar -xzf dl/lo.tar.gz -C libreoffice/debs --strip-components=1
  for d in libreoffice/debs/DEBS/*.deb; do dpkg-deb -x "$d" libreoffice/root; done
fi
SOFFICE=$(find libreoffice/root/opt -maxdepth 3 -path '*program/soffice' | head -1)
cat > soffice.sh <<EOF
#!/usr/bin/env bash
# Headless, with a private profile directory per caller (set LO_PROFILE).
exec "$O/$SOFFICE" -env:UserInstallation=file://\${LO_PROFILE:-$O/lo-profile} --headless --norestore "\$@"
EOF
chmod +x soffice.sh
./soffice.sh --version || true

# Python venv: python-docx, openpyxl, python-pptx (black-box comparators and sample writers), lxml (libxml2 XSD
# validation), pypdf (page counts)
[ -x venv/bin/python ] || "$UV" venv --python 3.13 venv
"$UV" pip install --python venv/bin/python python-docx==1.2.0 openpyxl==3.1.5 python-pptx==1.0.2 lxml==6.1.3 pypdf
venv/bin/python -c 'import lxml.etree as e; print("libxml2", e.LIBXML_VERSION)'
log done
