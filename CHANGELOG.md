# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

- `Document` on the word-processing hub: `Open` (sniffs content), `Create`, atomic `Save`, `SaveAs` by extension or format (DOCX, DOCM, DOTX, DOTM, Flat OPC), `Paragraphs`, `Tables`, `Paragraph.SetText`, `Body.AddParagraph`; `DocumentFormats.Register`.
- `oneoffice extract` with `-o` and `--json`; `oneoffice detect --json`.
- `RecursiveAI.OneOffice.Documents` and `RecursiveAI.OneOffice.Schema` are packable.
- Repository foundation: solution, build and packaging conventions, CI and release workflows.
- Core contracts: byte sources, resource limits, typed defects and exceptions, content-based format identification, the format registry, the part/package abstraction with atomic save, and the shared base of the document types.
