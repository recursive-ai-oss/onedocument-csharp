#!/usr/bin/env bash
# Fetches the pinned Chrome for Testing build the browser smoke run drives, into a user-local directory (never system-wide), and prints
# the path of its chrome executable. The download is checked against the pinned SHA-256 before it is unpacked.
#
#   ONEDOCUMENT_HOME   parent of oracles/ (default: $HOME/oneoffice)
set -euo pipefail

version=157.0.8103.38
sha256=8cdeb344e9b2cefe59f4aea8d1d93d5d93054c03cde217665c16fc1f62e72a66
url="https://storage.googleapis.com/chrome-for-testing-public/$version/linux64/chrome-linux64.zip"

dir="${ONEDOCUMENT_HOME:-$HOME/oneoffice}/oracles/chromium-$version"
chrome="$dir/chrome-linux64/chrome"
if [[ ! -x "$chrome" ]]; then
  mkdir -p "$dir"
  curl -fsS --max-time 900 -o "$dir/chrome-linux64.zip" "$url"
  echo "$sha256  $dir/chrome-linux64.zip" | sha256sum --check --quiet
  unzip -q -o "$dir/chrome-linux64.zip" -d "$dir"
fi
echo "$chrome"
