# Embedded-object comparison request (unanswered)

Run once at the final gate, on the Office oracle machine, through `tools/office-oracle/office-oracle.sh run`. Nothing here has been answered.

Inputs: `ole-props.doc`, `ole-props.xls`, `ole-props.ppt` (hashes in `README.md`). For each:

1. Open the legacy file in its application. Record whether a repair or compatibility prompt appears, the document properties shown
   (title, author, keywords, comments, custom properties) and whether the embedded spreadsheet can be activated.
2. Save it as DOCX, XLSX or PPTX from the application (its own conversion). Record how the object is written: the part, the content type, the
   relationship, the shape (for Excel: whether `objectPr`, a DrawingML shape and a legacy-drawing shape are all written).
3. Convert the same file with `tools/OleProbe convert <in> <out>` and open the result: repair prompt (yes/no); the object shows its preview in the
   right place and size; double-click activates it and shows the same spreadsheet; the document properties equal those of step 1.
4. Save the converted file from the application and compare the object part and properties again (does it keep the object?).

Questions the specifications leave open, to be decided by what the applications do:

- Excel: is the pre-2010 form (`oleObject` plus a legacy-drawing picture shape, no `objectPr`) accepted without a repair prompt, and is the shape anchored as in the source?
- Word: is a `v:shapetype` written once per part accepted in headers, footers and the body; does `ProgID` taken from the field instruction (`Excel.Sheet.5` here) matter when the storage's class says otherwise?
- PowerPoint: is `p:oleObj` without `spid` accepted, and is the object shown from its `p:pic` child alone?
- All three: the object part is a compound file written by this library (same streams, other sector layout); does activation depend on anything else in the file?
- The edit time is converted to minutes (`TotalTime`); does Office show the same value for the legacy file?
