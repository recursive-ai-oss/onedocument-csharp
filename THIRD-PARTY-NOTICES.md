# Third-party notices

OneOffice's own code is licensed `MIT OR Apache-2.0`. Material from other projects that ships in a package is listed here, one section per item, with its licence text.

Each entry has this form:

    ## <name and version>

    <where it ships and what it is used for>
    Licence: <SPDX expression>
    Source: <URL>

    <the licence's copyright notice and permission text>

## Packages

No third-party code ships in the packages. `RecursiveAI.OneOffice` depends on the Recursive AI library `RecursiveAI.OneArchive` (`MIT OR Apache-2.0`), which is not third-party material. Test, fuzz and benchmark projects use other packages (xUnit, BenchmarkDotNet, SharpFuzz); they are not part of any shipped package.

## Standards schemas and generated code

The repository carries the XML Schemas of the standards it implements under `schemas/`, unchanged, and the typed views in `src/OneOffice.Schema/Generated` are generated from them. Each generated file names its source schemas in its header.

### ECMA-376, 5th edition (Ecma International; ISO/IEC 29500)

The XSD files under `schemas/ecma376/` come from Part 1 (Strict), Part 2 (Open Packaging Conventions) and Part 4 (Transitional) of ECMA-376, 5th edition. They are copied without change.

Licence: Ecma International copyright licence for ECMA-376. The licence allows the specification to be copied, published and distributed to others, and certain derivative works to be prepared, provided that the Ecma copyright notice and the licence and disclaimer are included on all such copies and derivative works; derivative works include making use of the specification in standard conformant products by implementing its functionality.

Source: https://ecma-international.org/publications-and-standards/standards/ecma-376/

    Copyright (c) Ecma International. ECMA-376 is published by Ecma International under the licence above.

### Microsoft Open Specifications schemas

The XSD files under `schemas/ms/` are the schemas printed in Appendix A of [MS-DOCX] and [MS-ODRAWXML] (the exact documents and revisions are in `schemas/README.md`). Their text is unchanged; `tools/schemas/pin-schemas.py` extracts them.

Licence: the Intellectual Property Rights Notice for Open Specifications Documentation, which says: "you can make copies of it in order to develop implementations of the technologies that are described in this documentation and can distribute portions of it in your implementations that use these technologies ... You can also distribute in your implementation, with or without modification, any schemas, IDLs, or code samples that are included in the documentation." The documents are also covered by Microsoft's Open Specification Promise.

Source: https://learn.microsoft.com/en-us/openspecs/office_standards/ms-docx/ and https://learn.microsoft.com/en-us/openspecs/office_standards/ms-odrawxml/

    Copyright (c) Microsoft Corporation. The names of companies and products in the documentation might be covered by trademarks; the notice grants no licence under those rights.
