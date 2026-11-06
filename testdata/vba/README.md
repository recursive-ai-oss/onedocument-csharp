# VBA test data

Small files for the VBA project reader and remover. Nothing here is a real user's document.

| File | What it is |
|---|---|
| `generated-project.bin` | A `vbaProject.bin` written by the test builder (`tests/Shared/VbaTestProject.cs`) from the layout in the Office VBA file format specification: a document module, a procedural module, a class module and a user form, one registered reference. |
| `lo-roundtrip.docm`, `.xlsm`, `.pptm`, `.xls` | Written by LibreOffice 26.2.6.3 (`soffice --headless --convert-to`, black box) from plain packages that carried `generated-project.bin`. LibreOffice re-writes the project in its own way (it adds attribute lines and references, and turns the user form into a class), so these are an independent writer's output for the same logical project. The `.xls` is the Excel 97-2003 export; LibreOffice does not write a project into DOC or PPT. |

Regenerate with `ONEDOCUMENT_VBA_FIXTURES=<dir>` (see `tests/OneDocument.Tests/Vba/VbaFixtureTools.cs`) and LibreOffice.
The oracle's version is recorded here and nowhere else; the files are not edited by hand.
Office's own answers for the same operations are in `ORACLE-REQUEST.md`.
