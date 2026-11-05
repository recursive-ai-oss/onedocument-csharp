namespace RecursiveAI.OneDocument;

/// <summary>Which kind of document a format holds: the hub a file converts into.</summary>
public enum OfficeKind
{
    /// <summary>Not known, or not a document (a container whose contents cannot be told, such as an encrypted package).</summary>
    Unknown = 0,

    /// <summary>A word-processing document.</summary>
    Document = 1,

    /// <summary>A spreadsheet workbook, or delimited text that loads as one.</summary>
    Workbook = 2,

    /// <summary>A presentation.</summary>
    Presentation = 3,

    /// <summary>Something that is not an editable document: a PDF or an image, which are outputs.</summary>
    Output = 4,
}

/// <summary>
/// The formats OneDocument knows by name. The set is closed and explicit: sniffing returns one of these, the registry is keyed by it
/// and <c>SaveAs</c> chooses one by extension. Strict and Transitional OOXML are the same format here; the difference is
/// a property of the opened file. Which of them can be read or written depends on the packages referenced (see
/// <see cref="Formats.FormatRegistry"/>).
/// </summary>
public enum OfficeFormat
{
    /// <summary>The content is not recognised.</summary>
    Unknown = 0,

    // Word-processing, OOXML.
    /// <summary>Word-processing document (.docx).</summary>
    Docx = 1,

    /// <summary>Macro-enabled word-processing document (.docm).</summary>
    Docm = 2,

    /// <summary>Word-processing template (.dotx).</summary>
    Dotx = 3,

    /// <summary>Macro-enabled word-processing template (.dotm).</summary>
    Dotm = 4,

    /// <summary>A word-processing document as a single flat XML file (Flat OPC, .xml).</summary>
    FlatOpcDocument = 5,

    // Spreadsheets, OOXML.
    /// <summary>Workbook (.xlsx).</summary>
    Xlsx = 10,

    /// <summary>Macro-enabled workbook (.xlsm).</summary>
    Xlsm = 11,

    /// <summary>Workbook template (.xltx).</summary>
    Xltx = 12,

    /// <summary>Macro-enabled workbook template (.xltm).</summary>
    Xltm = 13,

    /// <summary>Macro-enabled add-in workbook (.xlam).</summary>
    Xlam = 14,

    /// <summary>A workbook as a single flat XML file (Flat OPC, .xml).</summary>
    FlatOpcWorkbook = 15,

    /// <summary>Binary workbook (.xlsb).</summary>
    Xlsb = 16,

    // Presentations, OOXML.
    /// <summary>Presentation (.pptx).</summary>
    Pptx = 20,

    /// <summary>Macro-enabled presentation (.pptm).</summary>
    Pptm = 21,

    /// <summary>Presentation template (.potx).</summary>
    Potx = 22,

    /// <summary>Macro-enabled presentation template (.potm).</summary>
    Potm = 23,

    /// <summary>Slide show (.ppsx).</summary>
    Ppsx = 24,

    /// <summary>Macro-enabled slide show (.ppsm).</summary>
    Ppsm = 25,

    /// <summary>Macro-enabled add-in presentation (.ppam).</summary>
    Ppam = 26,

    /// <summary>A presentation as a single flat XML file (Flat OPC, .xml).</summary>
    FlatOpcPresentation = 27,

    // Legacy binary (compound file or bare BIFF stream).
    /// <summary>Word 97–2003 binary document (.doc), including fast-saved files and, on read, Word 6/95.</summary>
    Doc = 30,

    /// <summary>Excel 97–2003 binary workbook, BIFF8 (.xls).</summary>
    Xls = 31,

    /// <summary>Excel 2.0–5.0/95 binary workbook, BIFF2–5 (.xls); read only.</summary>
    XlsLegacy = 32,

    /// <summary>PowerPoint 97–2003 binary presentation (.ppt).</summary>
    Ppt = 33,

    /// <summary>An encrypted OOXML package: a compound file holding an encrypted package. The kind is known only after decryption.</summary>
    EncryptedPackage = 34,

    // Interchange and text.
    /// <summary>Rich Text Format (.rtf).</summary>
    Rtf = 40,

    /// <summary>Word 2003 XML (WordprocessingML 2003, .xml); read only.</summary>
    Word2003Xml = 41,

    /// <summary>Excel 2003 XML (SpreadsheetML 2003, .xml); read only.</summary>
    Excel2003Xml = 42,

    /// <summary>Comma-separated values (.csv).</summary>
    Csv = 43,

    /// <summary>Tab-separated values (.tsv, .tab).</summary>
    Tsv = 44,

    /// <summary>HTML (.html, .htm).</summary>
    Html = 45,

    /// <summary>Markdown (.md). Never detected from content, only chosen by extension or by the caller.</summary>
    Markdown = 46,

    /// <summary>Plain text (.txt). Never detected from content, only chosen by extension or by the caller.</summary>
    Text = 47,

    // OpenDocument.
    /// <summary>OpenDocument text (.odt).</summary>
    Odt = 50,

    /// <summary>OpenDocument spreadsheet (.ods).</summary>
    Ods = 51,

    /// <summary>OpenDocument presentation (.odp).</summary>
    Odp = 52,

    /// <summary>OpenDocument text template (.ott).</summary>
    Ott = 53,

    /// <summary>OpenDocument spreadsheet template (.ots).</summary>
    Ots = 54,

    /// <summary>OpenDocument presentation template (.otp).</summary>
    Otp = 55,

    /// <summary>Flat OpenDocument text (.fodt).</summary>
    Fodt = 56,

    /// <summary>Flat OpenDocument spreadsheet (.fods).</summary>
    Fods = 57,

    /// <summary>Flat OpenDocument presentation (.fodp).</summary>
    Fodp = 58,

    // Outputs.
    /// <summary>PDF (.pdf). An output; detected so that it is not mistaken for something else.</summary>
    Pdf = 60,

    /// <summary>PNG image (.png). An output.</summary>
    Png = 61,

    /// <summary>JPEG image (.jpg, .jpeg). An output.</summary>
    Jpeg = 62,

    /// <summary>TIFF image (.tif, .tiff). An output.</summary>
    Tiff = 63,

    /// <summary>WebP image (.webp). An output.</summary>
    WebP = 64,

    /// <summary>SVG image (.svg). An output (one per page).</summary>
    Svg = 65,
}
