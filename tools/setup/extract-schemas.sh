#!/usr/bin/env bash
# Extract the pinned ECMA-376 5th edition schemas into a directory validate_ooxml.py can read.
#
#   ONEOFFICE_SCHEMA_ZIPS  directory holding the four ECMA-376 5th edition zips (parts 1, 2, 4 are used)
#   ONEOFFICE_XSD          output directory (default: $ONEOFFICE_SCHEMA_ZIPS/../xsd)
#
# Layout written: $ONEOFFICE_XSD/{transitional,strict,opc}/*.xsd. Idempotent; each zip's sha256 is checked
# against the pins below before use.
set -euo pipefail
zips="${ONEOFFICE_SCHEMA_ZIPS:?set ONEOFFICE_SCHEMA_ZIPS}"
out="${ONEOFFICE_XSD:-$zips/../xsd}"

declare -A pin=(
  [ECMA-376-1_5th_edition_december_2016.zip]=9d0bcad9cf06054785b03762fcfadbf6bab7e54a5f9d69434e34b7fd464d4129
  [ECMA-376-2_5th_edition_december_2021.zip]=1d489dc491168ea1f9e9a59063acc8dd5f02b4ad1d21aa7ec19ba9a58d020c70
  [ECMA-376-4_5th_edition_december_2016.zip]=bd25da1109f73762356596918bf5ff8b74a1331642dba5f1c1d1dfc6bed34ecd
)
for z in "${!pin[@]}"; do
  got=$(sha256sum "$zips/$z" | cut -d' ' -f1)
  [[ "$got" == "${pin[$z]}" ]] || { echo "sha256 mismatch for $z: $got" >&2; exit 1; }
done

work=$(mktemp -d "${TMPDIR:-.}/xsd.XXXXXX")
trap 'rm -rf "$work"' EXIT
unzip -q -o "$zips/ECMA-376-4_5th_edition_december_2016.zip" -d "$work"
unzip -q -o "$zips/ECMA-376-1_5th_edition_december_2016.zip" -d "$work"
unzip -q -o "$zips/ECMA-376-2_5th_edition_december_2021.zip" -d "$work"
mkdir -p "$out/transitional" "$out/strict" "$out/opc"
unzip -q -o "$(find "$work" -name 'OfficeOpenXML-XMLSchema-Transitional.zip')" -d "$out/transitional"
unzip -q -o "$(find "$work" -name 'OfficeOpenXML-XMLSchema-Strict.zip')" -d "$out/strict"
unzip -q -o "$(find "$work" -name 'OpenPackagingConventions-XMLSchema.zip')" -d "$out/opc"
echo "schemas in $out: $(find "$out" -name '*.xsd' | wc -l) files"
