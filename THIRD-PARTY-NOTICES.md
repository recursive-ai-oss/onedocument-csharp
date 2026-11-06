# Third-party notices

OneDocument's own code is licensed `MIT OR Apache-2.0`. Material from other projects that ships in a package is listed here, one section per item, with its licence text.

Each entry has this form:

    ## <name and version>

    <where it ships and what it is used for>
    Licence: <SPDX expression>
    Source: <URL>

    <the licence's copyright notice and permission text>

## Packages

No third-party code ships in the packages. `RecursiveAI.OneDocument` depends on the Recursive AI library `RecursiveAI.OneArchive` (`MIT OR Apache-2.0`), which is not third-party material. Test, fuzz and benchmark projects use other packages (xUnit, BenchmarkDotNet, SharpFuzz); they are not part of any shipped package.

## Fonts

`RecursiveAI.OneDocument.Rendering` ships no font files. It depends on the Recursive AI package `RecursiveAI.OneRender.Fonts`, which carries Liberation, Carlito, Caladea and Noto Symbols 2 (SIL Open Font License 1.1), unmodified and with their licence texts inside that package. Callers who register the optional script packages (`RecursiveAI.OneRender.Fonts.Noto.*`, `RecursiveAI.OneRender.Fonts.Cjk`) get Noto fonts under the same licence; the `onedocument` tool references them. Fonts embedded in a document are used only to lay out and draw that document and are never copied into packages.

## Standards schemas and generated code

The repository carries the XML Schemas of the standards it implements under `schemas/`, unchanged, and the typed views in `src/OneDocument.Schema/Generated` are generated from them. Each generated file names its source schemas in its header.

### ECMA-376, 5th edition (Ecma International; ISO/IEC 29500)

The XSD files under `schemas/ecma376/` come from Part 1 (Strict), Part 2 (Open Packaging Conventions) and Part 4 (Transitional) of ECMA-376, 5th edition. They are copied without change.

Source: https://ecma-international.org/publications-and-standards/standards/ecma-376/

The generated typed views in `src/OneDocument.Schema/Generated/` are derived from these schemas as part of an implementation of the specification (item (iv) below). The preset shape data in `src/OneDocument.Rendering/Drawing/Generated/` is generated from the standard's `presetShapeDefinitions.xml` (Part 1, `OfficeOpenXML-DrawingMLGeometries.zip`) on the same terms. The notice below is Ecma's default copyright notice, quoted verbatim from https://ecma-international.org/policies/by-ipr/ecma-text-copyright-policy/.

    COPYRIGHT NOTICE

    © Ecma International

    By obtaining and/or copying this work, you (the licensee) agree that you have read, understood, and will
    comply with the following terms and conditions.

    This document may be copied, published and distributed to others, and certain derivative works of it may be
    prepared, copied, published, and distributed, in whole or in part, provided that the above copyright notice
    and this Copyright License and Disclaimer are included on all such copies and derivative works. The only
    derivative works that are permissible under this Copyright License and Disclaimer are:

    (i) works which incorporate all or portion of this document for the purpose of providing commentary or
    explanation (such as an annotated version of the document),

    (ii) works which incorporate all or portion of this document for the purpose of incorporating features that
    provide accessibility,

    (iii) translations of this document into languages other than English and into different formats and

    (iv) works by making use of this specification in standard conformant products by implementing (e.g. by
    copy and paste wholly or partly) the functionality therein.

    However, the content of this document itself may not be modified in any way, including by removing the
    copyright notice or references to Ecma International, except as required to translate it into languages
    other than English or into a different format.

    The official version of an Ecma International document is the English language version on the Ecma
    International website. In the event of discrepancies between a translated version and the official
    version, the official version shall govern.

    The limited permissions granted above are perpetual and will not be revoked by Ecma International or its
    successors or assigns.

    This document and the information contained herein is provided on an "AS IS" basis and ECMA INTERNATIONAL
    DISCLAIMS ALL WARRANTIES, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTY THAT THE USE OF THE
    INFORMATION HEREIN WILL NOT INFRINGE ANY OWNERSHIP RIGHTS OR ANY IMPLIED WARRANTIES OF MERCHANTABILITY OR
    FITNESS FOR A PARTICULAR PURPOSE.

### Microsoft Open Specifications schemas

The XSD files under `schemas/ms/` are the schemas printed in Appendix A of [MS-DOCX], [MS-ODRAWXML] and [MS-PPTX] (the exact documents and revisions are in `schemas/README.md`). Their text is unchanged; `tools/schemas/pin-schemas.py` extracts them.

Licence: the Intellectual Property Rights Notice for Open Specifications Documentation, which says: "you can make copies of it in order to develop implementations of the technologies that are described in this documentation and can distribute portions of it in your implementations that use these technologies ... You can also distribute in your implementation, with or without modification, any schemas, IDLs, or code samples that are included in the documentation." The documents are also covered by Microsoft's Open Specification Promise.

Source: https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/ and https://learn.microsoft.com/en-us/openspecs/office_standards/ms-odrawxml/

    Copyright (c) Microsoft Corporation. The names of companies and products in the documentation might be covered by trademarks; the notice grants no licence under those rights.

## Test files written by other programs

The small legacy workbooks in `testdata/xls/` (`*.xls`) are the output of LibreOffice 26.8.2.2 (`soffice --headless --convert-to 'xls:MS Excel 97'`, run as a black box, MPL-2.0) on the generated inputs `testdata/xls/*.xlsx`, which `tools/Biff/make_xls_inputs.py` writes with openpyxl 3.1.5 (MIT) from fixed values. They contain no text from either program. Regenerate them with `tools/Biff/make_xls_oracle.sh`.

The Word 97-2003 files in `testdata/doc/` (`*.doc`) are the output of LibreOffice 26.8.2.2 (filter `MS Word 97`, run as a black box, MPL-2.0) on DOCX inputs that `tools/DocFixtures/make_docx.py` writes from fixed, hand-written WordprocessingML. They contain no text from the program. Regenerate them with `tools/DocFixtures/make_doc.sh`.

The files in `testdata/ole/` (`ole-props.doc`, `.xls`, `.ppt`) are the output of LibreOffice 26.8.2.2 (filters `MS Word 97`, `MS Excel 97` and `MS PowerPoint 97`, run as a black box, MPL-2.0) on OpenDocument inputs that `tools/Ole/make_ole_inputs.py` writes from fixed values. They contain no text from the program. Regenerate them with `tools/Ole/make_ole_fixtures.sh`.

The PowerPoint 97-2003 files in `testdata/ppt/` (`lo-*.ppt`) are the output of LibreOffice 26.8.2.2 (filter `MS PowerPoint 97`, run as a black box, MPL-2.0) on presentations generated by this project's test data (`testdata/slides/`, `testdata/drawingml/`, `testdata/templates/pptx/`), reduced to their `Current User`, `PowerPoint Document` and `Pictures` streams. Regenerate them with `tools/ppt/make-fixtures.sh`.

The RTF files in `testdata/rtf/lo/` are the output of LibreOffice 26.8.2.2 (`soffice --headless --convert-to rtf`, run as a black box, MPL-2.0) on documents generated by this project's test data. `testdata/rtf/oxrtfex-3.1.*` are typed from the worked example in Microsoft's [MS-OXRTFEX] specification (Microsoft Open Specifications notice).

`testdata/xml2003/lo-word.xml` and `lo-excel.xml` are the output of LibreOffice 26.2.6.3 (filters `MS Word 2003 XML` and `MS Excel 2003 XML`, run as a black box, MPL-2.0) on `tools/xml2003/oracle-input.fodt` and `oracle-input.fods`, which this project wrote. They contain no text from the program.
