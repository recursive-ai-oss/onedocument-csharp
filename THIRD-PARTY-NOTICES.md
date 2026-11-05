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

## Standards schemas and generated code

The repository carries the XML Schemas of the standards it implements under `schemas/`, unchanged, and the typed views in `src/OneDocument.Schema/Generated` are generated from them. Each generated file names its source schemas in its header.

### ECMA-376, 5th edition (Ecma International; ISO/IEC 29500)

The XSD files under `schemas/ecma376/` come from Part 1 (Strict), Part 2 (Open Packaging Conventions) and Part 4 (Transitional) of ECMA-376, 5th edition. They are copied without change.

Source: https://ecma-international.org/publications-and-standards/standards/ecma-376/

The generated typed views in `src/OneDocument.Schema/Generated/` are derived from these schemas as part of an implementation of the specification (item (iv) below). The notice below is Ecma's default copyright notice, quoted verbatim from https://ecma-international.org/policies/by-ipr/ecma-text-copyright-policy/.

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
