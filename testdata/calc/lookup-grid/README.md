# Lookup, reference, dynamic-array and LAMBDA-helper grid

`tools/LookupGrid gen DIR` wrote the `look*.fgrid` inputs (about 9,700 formulas, 17 workbooks; every workbook starts with three data
tests that spill into B1:D3, B61:E64 and B121:B123). The `look*.fgrid.tsv` files are the slots for Excel's answers and are empty until the
full Excel run: `office-oracle.sh run --formulas DIR` fills them, `tools/LookupGrid compare DIR` then lists every disagreement in
`mismatches.txt`. `tools/LookupGrid selfcheck DIR` proves the file format round-trips by comparing OneDocument with its own answers.
Where the function reference leaves a behaviour open, the code says `open:` and `docs/calc-functions.md` lists it.
The random-looking arrays are produced from a fixed seed, so regenerating gives the same files.
