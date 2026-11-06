# Tools

Harness and corpus tools. None of them is shipped. Locations come from environment variables or arguments;
nothing here is tied to a machine. Oracles (LibreOffice, Jing, python-docx and friends) are only ever run.

| Tool | What it does |
|---|---|
| `setup/install-oracles.sh` | Installs the pinned oracles under `$ONEDOCUMENT_HOME/oracles` (sha256-checked, idempotent). |
| `setup/extract-schemas.sh` | Extracts the pinned ECMA-376 5th edition XSDs to `$ONEDOCUMENT_XSD/{transitional,strict,opc}`. |
| `setup/fetch-govdocs1.sh` | Fetches govdocs1 thread zips and extracts the Office files. |
| `corpus/fetch_commoncrawl.py` | Office files from a Common Crawl crawl: columnar index, ranged WARC reads, per-domain cap. |
| `corpus/fetch_bugzilla.py` | Attachments (files only) from public Bugzilla trackers. |
| `corpus/fetch_templates.py` | Template files from caller-supplied pages or URL lists. |
| `corpus/make_stress_docx.py` | Deterministic ~1000-page DOCX (headings, lists, tables, sections, footnotes, comments, revisions). |
| `corpus/run_checks.py` | Parallel, resumable, time-boxed checks over a directory: `lo`, `validate`, `cmd`. |
| `StreamProbe/` | Streaming workbook cursors: `compare` (cursor against the workbook model over a directory), `read` and `write` (throughput, allocation and working-set checkpoints), `layers` (where read time goes), `sample` (small generated workbooks for opening in an application), `xcompare` (values of two workbooks). |
| `RedactProbe/` | Redaction in bulk, information only: every document under a directory is redacted by a literal or pattern and written to an output directory; one JSON line per file (replacements, gap codes, verified, survivors, typed error). LibreOffice and the validator are run over its outputs with `corpus/run_checks.py`. |
| `RoundTrip/` | Per-part comparison of two packages (zip or Flat OPC). |
| `validate/validate_ooxml.py` | Schema validation of every XML part, with markup-compatibility preprocessing. |
| `LayoutProbe/`, `layout-differential.sh` | The generated layout case set (`gen`: DOCX and the `slide-*.pptx` decks), rendering of a directory of DOCX and PPTX to PDF and page-1 PNG with a layout log (`render`), a recursive PPTX sweep with page and note counts (`sweep-pptx`), per-page text lines of PDFs (`lines`), and page-count and line-break comparison of two PDF directories (`compare`). The script chains gen, render, the Word and PowerPoint oracle's PDF exports and compare; its answers come at the milestone gate. |
| `RtfProbe/` | RTF in bulk: `read` (every .rtf in a directory: defects, loss codes, text, the DOCX and the RTF written back), `write` (every DOCX through the RTF exporter), `text` and `similar` (extracted text and its similarity between two directories); `oracle.sh` runs LibreOffice as a black box over the same documents; `compare.py` compares our text with Apache Tika's. |
| `HtmlProbe/` | HTML in bulk, information only: `export` (every DOCX: written as HTML, parsed with OneHtml for parse errors, its words compared with the extracted text, read back; export loss codes counted; the HTML files are kept for LibreOffice) and `import` (every HTML file: imported, extracted, written as DOCX and opened again; import loss codes counted). |
| `fuzz-run.sh` | Builds and runs one fuzz target for N seconds and prints executions, features and findings. |
| `tests/selftest.sh` | Checks the round-trip tool and the validator against small generated packages. |

Corpus files and their manifests (which hold URLs) stay on the build host: they are never committed and never
copied elsewhere. Fetchers are polite (serial or low concurrency, back-off on throttling) and respect the sources'
terms.

## Environment

| Variable | Meaning |
|---|---|
| `ONEDOCUMENT_HOME` | Root for oracles, corpora, results (required by the scripts that use it). |
| `ONEDOCUMENT_CORPUS` | Corpus root (default `$ONEDOCUMENT_HOME/corpus`). |
| `ONEDOCUMENT_XSD` | Path-separated schema directories for the validator. |
| `ONEDOCUMENT_SOFFICE` | Headless LibreOffice launcher; `LO_PROFILE` selects its profile directory. |
| `ONEDOCUMENT_FUZZ_WORK`, `LIBFUZZER_DOTNET`, `SHARPFUZZ` | See `fuzz-run.sh` and `fuzz/README.md`. |

## Contracts

All tools write JSON lines (one object per line, UTF-8). Exit status: 0 clean, 1 findings, 2 usage or unreadable input.

### RoundTrip

    dotnet RoundTrip.dll <before> <after> [--json file] [--quiet]

One `part` line per part: `{"kind":"part","uri","status":"identical|changed|added|removed","sha256Before","sha256After",`
`"rawPreserved":bool}`. A changed part adds `firstDiffOffset`, `sizeBefore/After`, `before/after` excerpts,
`xmlEquivalent` (same infoset, different bytes) and, when the XML differs, `firstDifferingElement`. A content type
difference adds `contentType:[before,after]`; a changed `.rels` part adds `relationships:["-Id|Type|Target|Mode","+..."]`.
`rawPreserved` is true when the stored zip entry (method, CRC, compressed bytes) is unchanged, i.e. not recompressed.
The last line is `{"kind":"summary","identical","changed","added","removed","rawPreserved","rawNotPreserved",`
`"contentTypeDiffs","relationshipDiffs"}`. Exit 0 only when nothing changed and every raw entry was preserved.

### validate_ooxml.py

    validate_ooxml.py [--schemas DIR]... [--understand NS]... [--json FILE] FILE...

`{"kind":"part","file","uri","status":"valid|invalid|no-schema","schema",`
`"errors":[{"line","column","path","message"}],"errorCount"}` per XML part, then
`{"kind":"summary","file","valid","invalid","no-schema"}` per file. Line and column locate the start tag in the
original part. Markup compatibility is applied first (see the script header). The core-properties schema imports
Dublin Core definitions from a remote location; `validate/stubs/` stands in for them, so those values are
checked for structure only.

### run_checks.py

    run_checks.py lo|validate|cmd --input PATH --out DIR [--glob G] [--shards N] [--timeout S] [--max-minutes M]

Writes `DIR/results-<check>-<shard>.jsonl` (one line per file: `file` relative to the input, `sha256`, `size`,
`check`, `status`, plus check fields such as `pages`, `seconds`, `invalidParts`, `roundtrip`) and
`DIR/summary-<check>.json` (`files`, `collected`, `remaining`, `statuses`). Statuses: `lo`: opened / failed /
timeout; `validate`: valid / invalid / unreadable; `cmd`: ok / failed / timeout. Re-running the same command
continues where it stopped, and shards stop starting new files after `--max-minutes`, so a run fits a queue slot.

### Office oracle (stub)

The desktop-Office oracle is not driven yet. When it is, it takes a file hash and an operation (`open`,
`repair-check`, `export-pdf`) and returns one JSON line `{"file","op","status","detail"}`; nothing calls it today.
