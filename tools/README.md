# Tools

Harness and corpus tools. None of them is shipped. Locations come from environment variables or arguments;
nothing here is tied to a machine. Oracles (LibreOffice, Jing, python-docx and friends) are only ever run.

| Tool | What it does |
|---|---|
| `setup/install-oracles.sh` | Installs the pinned oracles under `$ONEOFFICE_HOME/oracles` (sha256-checked, idempotent). |
| `setup/extract-schemas.sh` | Extracts the pinned ECMA-376 5th edition XSDs to `$ONEOFFICE_XSD/{transitional,strict,opc}`. |
| `setup/fetch-govdocs1.sh` | Fetches govdocs1 thread zips and extracts the Office files. |
| `corpus/fetch_commoncrawl.py` | Office files from a Common Crawl crawl: columnar index, ranged WARC reads, per-domain cap. |
| `corpus/fetch_bugzilla.py` | Attachments (files only) from public Bugzilla trackers. |
| `corpus/fetch_templates.py` | Template files from caller-supplied pages or URL lists. |
| `corpus/make_stress_docx.py` | Deterministic ~1000-page DOCX (headings, lists, tables, sections, footnotes, comments, revisions). |
| `corpus/run_checks.py` | Parallel, resumable, time-boxed checks over a directory: `lo`, `validate`, `cmd`. |
| `StreamProbe/` | Streaming workbook cursors: `compare` (cursor against the workbook model over a directory), `read` and `write` (throughput, allocation and working-set checkpoints), `layers` (where read time goes), `sample` (small generated workbooks for opening in an application), `xcompare` (values of two workbooks). |
| `RoundTrip/` | Per-part comparison of two packages (zip or Flat OPC). |
| `validate/validate_ooxml.py` | Schema validation of every XML part, with markup-compatibility preprocessing. |
| `fuzz-run.sh` | Builds and runs one fuzz target for N seconds and prints executions, features and findings. |
| `tests/selftest.sh` | Checks the round-trip tool and the validator against small generated packages. |

Corpus files and their manifests (which hold URLs) stay on the build host: they are never committed and never
copied elsewhere. Fetchers are polite (serial or low concurrency, back-off on throttling) and respect the sources'
terms.

## Environment

| Variable | Meaning |
|---|---|
| `ONEOFFICE_HOME` | Root for oracles, corpora, results (default `~/oneoffice`). |
| `ONEOFFICE_CORPUS` | Corpus root (default `$ONEOFFICE_HOME/corpus`). |
| `ONEOFFICE_XSD` | Path-separated schema directories for the validator. |
| `ONEOFFICE_SOFFICE` | Headless LibreOffice launcher; `LO_PROFILE` selects its profile directory. |
| `ONEOFFICE_FUZZ_WORK`, `LIBFUZZER_DOTNET`, `SHARPFUZZ` | See `fuzz-run.sh` and `fuzz/README.md`. |

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
