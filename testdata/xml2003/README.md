# Office 2003 XML fixtures

Oracle output: `lo-word.xml` and `lo-excel.xml` were written by LibreOffice 26.2.6.3 (`--convert-to xml:"MS Word 2003 XML"` and
`xml:"MS Excel 2003 XML"`) from `tools/xml2003/oracle-input.fodt` and `oracle-input.fods`. They show how another writer
spells the formats (nested runs, repeated properties, fractional twips, formulas in OpenFormula text); the readers must open them.

The other cases are written by hand in the tests, in the form Word and Excel 2003 document.
