using System.Collections.Frozen;

namespace RecursiveAI.OneDocument.Formats;

/// <summary>What a format is: its name, kind, media type and file extensions. Describes the format only; whether a reader or writer exists is the <see cref="FormatRegistry"/>'s business.</summary>
public sealed class FormatDescriptor
{
    internal FormatDescriptor(OfficeFormat format, OfficeKind kind, string name, string mediaType, string[] extensions, FormatTraits traits)
    {
        Format = format;
        Kind = kind;
        Name = name;
        MediaType = mediaType;
        Extensions = extensions;
        Traits = traits;
    }

    /// <summary>Gets the format.</summary>
    public OfficeFormat Format { get; }

    /// <summary>Gets the kind of document the format holds.</summary>
    public OfficeKind Kind { get; }

    /// <summary>Gets a short human name, such as <c>Word document (OOXML)</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the media type, such as <c>application/vnd.openxmlformats-officedocument.wordprocessingml.document</c>.</summary>
    public string MediaType { get; }

    /// <summary>Gets the file extensions with the leading dot, lower case; the first is the default one.</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>Gets the default file extension, with the leading dot.</summary>
    public string DefaultExtension => Extensions[0];

    /// <summary>Gets the properties of the format that cut across kinds (template, macros, flat, container).</summary>
    public FormatTraits Traits { get; }

    /// <summary>Gets a value indicating whether this is a template format.</summary>
    public bool IsTemplate => (Traits & FormatTraits.Template) != 0;

    /// <summary>Gets a value indicating whether the format can hold macros.</summary>
    public bool IsMacroEnabled => (Traits & FormatTraits.MacroEnabled) != 0;

    /// <summary>Gets a value indicating whether the format is one XML file instead of a package.</summary>
    public bool IsFlat => (Traits & FormatTraits.Flat) != 0;

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>Properties of a format that cut across kinds.</summary>
[Flags]
public enum FormatTraits
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>A template.</summary>
    Template = 1,

    /// <summary>May hold macros.</summary>
    MacroEnabled = 2,

    /// <summary>A single XML file rather than a package.</summary>
    Flat = 4,

    /// <summary>A zip-based package (OPC or ODF).</summary>
    Zip = 8,

    /// <summary>A compound file (CFB).</summary>
    CompoundFile = 16,

    /// <summary>Plain text of some kind (delimited text, RTF, HTML, Markdown, XML).</summary>
    Text = 32,

    /// <summary>Read only by design: written output is not planned.</summary>
    ReadOnly = 64,
}

/// <summary>The descriptors of every <see cref="OfficeFormat"/>, and the one place extensions map to formats.</summary>
public static class OfficeFormats
{
    private const string Wml = "application/vnd.openxmlformats-officedocument.wordprocessingml.";
    private const string Sml = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
    private const string Pml = "application/vnd.openxmlformats-officedocument.presentationml.";
    private const string Odf = "application/vnd.oasis.opendocument.";
    private const FormatTraits Z = FormatTraits.Zip;
    private const FormatTraits M = FormatTraits.Zip | FormatTraits.MacroEnabled;
    private const FormatTraits T = FormatTraits.Zip | FormatTraits.Template;
    private const FormatTraits TM = FormatTraits.Zip | FormatTraits.Template | FormatTraits.MacroEnabled;
    private const FormatTraits F = FormatTraits.Flat | FormatTraits.Text;

    private static readonly FormatDescriptor[] s_all =
    [
        D(OfficeFormat.Docx, OfficeKind.Document, "Word document (OOXML)", Wml + "document", Z, ".docx"),
        D(OfficeFormat.Docm, OfficeKind.Document, "Word macro-enabled document (OOXML)", "application/vnd.ms-word.document.macroEnabled.12", M, ".docm"),
        D(OfficeFormat.Dotx, OfficeKind.Document, "Word template (OOXML)", Wml + "template", T, ".dotx"),
        D(OfficeFormat.Dotm, OfficeKind.Document, "Word macro-enabled template (OOXML)", "application/vnd.ms-word.template.macroEnabled.12", TM, ".dotm"),
        D(OfficeFormat.FlatOpcDocument, OfficeKind.Document, "Word document (Flat OPC)", "application/xml", F, ".xml"),
        D(OfficeFormat.Xlsx, OfficeKind.Workbook, "Excel workbook (OOXML)", Sml + "sheet", Z, ".xlsx"),
        D(OfficeFormat.Xlsm, OfficeKind.Workbook, "Excel macro-enabled workbook (OOXML)", "application/vnd.ms-excel.sheet.macroEnabled.12", M, ".xlsm"),
        D(OfficeFormat.Xltx, OfficeKind.Workbook, "Excel template (OOXML)", Sml + "template", T, ".xltx"),
        D(OfficeFormat.Xltm, OfficeKind.Workbook, "Excel macro-enabled template (OOXML)", "application/vnd.ms-excel.template.macroEnabled.12", TM, ".xltm"),
        D(OfficeFormat.Xlam, OfficeKind.Workbook, "Excel macro-enabled add-in (OOXML)", "application/vnd.ms-excel.addin.macroEnabled.12", M, ".xlam"),
        D(OfficeFormat.FlatOpcWorkbook, OfficeKind.Workbook, "Excel workbook (Flat OPC)", "application/xml", F, ".xml"),
        D(OfficeFormat.Xlsb, OfficeKind.Workbook, "Excel binary workbook (XLSB)", "application/vnd.ms-excel.sheet.binary.macroEnabled.12", M, ".xlsb"),
        D(OfficeFormat.Pptx, OfficeKind.Presentation, "PowerPoint presentation (OOXML)", Pml + "presentation", Z, ".pptx"),
        D(OfficeFormat.Pptm, OfficeKind.Presentation, "PowerPoint macro-enabled presentation (OOXML)", "application/vnd.ms-powerpoint.presentation.macroEnabled.12", M, ".pptm"),
        D(OfficeFormat.Potx, OfficeKind.Presentation, "PowerPoint template (OOXML)", Pml + "template", T, ".potx"),
        D(OfficeFormat.Potm, OfficeKind.Presentation, "PowerPoint macro-enabled template (OOXML)", "application/vnd.ms-powerpoint.template.macroEnabled.12", TM, ".potm"),
        D(OfficeFormat.Ppsx, OfficeKind.Presentation, "PowerPoint slide show (OOXML)", Pml + "slideshow", Z, ".ppsx"),
        D(OfficeFormat.Ppsm, OfficeKind.Presentation, "PowerPoint macro-enabled slide show (OOXML)", "application/vnd.ms-powerpoint.slideshow.macroEnabled.12", M, ".ppsm"),
        D(OfficeFormat.Ppam, OfficeKind.Presentation, "PowerPoint macro-enabled add-in (OOXML)", "application/vnd.ms-powerpoint.addin.macroEnabled.12", M, ".ppam"),
        D(OfficeFormat.FlatOpcPresentation, OfficeKind.Presentation, "PowerPoint presentation (Flat OPC)", "application/xml", F, ".xml"),
        D(OfficeFormat.Doc, OfficeKind.Document, "Word 97-2003 document", "application/msword", FormatTraits.CompoundFile | FormatTraits.ReadOnly, ".doc"),
        D(OfficeFormat.Xls, OfficeKind.Workbook, "Excel 97-2003 workbook (BIFF8)", "application/vnd.ms-excel", FormatTraits.CompoundFile, ".xls"),
        D(OfficeFormat.XlsLegacy, OfficeKind.Workbook, "Excel 2.0-5.0/95 workbook (BIFF2-5)", "application/vnd.ms-excel", FormatTraits.ReadOnly, ".xls"),
        D(OfficeFormat.Ppt, OfficeKind.Presentation, "PowerPoint 97-2003 presentation", "application/vnd.ms-powerpoint", FormatTraits.CompoundFile | FormatTraits.ReadOnly, ".ppt"),
        D(OfficeFormat.EncryptedPackage, OfficeKind.Unknown, "Encrypted OOXML package", "application/octet-stream", FormatTraits.CompoundFile | FormatTraits.ReadOnly, ".docx", ".xlsx", ".pptx"),
        D(OfficeFormat.Rtf, OfficeKind.Document, "Rich Text Format", "application/rtf", FormatTraits.Text, ".rtf"),
        D(OfficeFormat.Word2003Xml, OfficeKind.Document, "Word 2003 XML", "application/xml", F | FormatTraits.ReadOnly, ".xml"),
        D(OfficeFormat.Excel2003Xml, OfficeKind.Workbook, "Excel 2003 XML", "application/xml", F | FormatTraits.ReadOnly, ".xml"),
        D(OfficeFormat.Csv, OfficeKind.Workbook, "Comma-separated values", "text/csv", FormatTraits.Text, ".csv"),
        D(OfficeFormat.Tsv, OfficeKind.Workbook, "Tab-separated values", "text/tab-separated-values", FormatTraits.Text, ".tsv", ".tab"),
        D(OfficeFormat.Html, OfficeKind.Document, "HTML", "text/html", FormatTraits.Text, ".html", ".htm"),
        D(OfficeFormat.Markdown, OfficeKind.Document, "Markdown", "text/markdown", FormatTraits.Text, ".md", ".markdown"),
        D(OfficeFormat.Text, OfficeKind.Document, "Plain text", "text/plain", FormatTraits.Text, ".txt"),
        D(OfficeFormat.Odt, OfficeKind.Document, "OpenDocument text", Odf + "text", Z, ".odt"),
        D(OfficeFormat.Ods, OfficeKind.Workbook, "OpenDocument spreadsheet", Odf + "spreadsheet", Z, ".ods"),
        D(OfficeFormat.Odp, OfficeKind.Presentation, "OpenDocument presentation", Odf + "presentation", Z, ".odp"),
        D(OfficeFormat.Ott, OfficeKind.Document, "OpenDocument text template", Odf + "text-template", T, ".ott"),
        D(OfficeFormat.Ots, OfficeKind.Workbook, "OpenDocument spreadsheet template", Odf + "spreadsheet-template", T, ".ots"),
        D(OfficeFormat.Otp, OfficeKind.Presentation, "OpenDocument presentation template", Odf + "presentation-template", T, ".otp"),
        D(OfficeFormat.Fodt, OfficeKind.Document, "Flat OpenDocument text", Odf + "text-flat-xml", F, ".fodt"),
        D(OfficeFormat.Fods, OfficeKind.Workbook, "Flat OpenDocument spreadsheet", Odf + "spreadsheet-flat-xml", F, ".fods"),
        D(OfficeFormat.Fodp, OfficeKind.Presentation, "Flat OpenDocument presentation", Odf + "presentation-flat-xml", F, ".fodp"),
        D(OfficeFormat.Pdf, OfficeKind.Output, "PDF", "application/pdf", FormatTraits.None, ".pdf"),
        D(OfficeFormat.Png, OfficeKind.Output, "PNG image", "image/png", FormatTraits.None, ".png"),
        D(OfficeFormat.Jpeg, OfficeKind.Output, "JPEG image", "image/jpeg", FormatTraits.None, ".jpg", ".jpeg"),
        D(OfficeFormat.Tiff, OfficeKind.Output, "TIFF image", "image/tiff", FormatTraits.None, ".tif", ".tiff"),
        D(OfficeFormat.WebP, OfficeKind.Output, "WebP image", "image/webp", FormatTraits.None, ".webp"),
        D(OfficeFormat.Svg, OfficeKind.Output, "SVG image", "image/svg+xml", FormatTraits.Text, ".svg"),
    ];

    private static readonly FrozenDictionary<OfficeFormat, FormatDescriptor> s_byFormat =
        s_all.ToFrozenDictionary(d => d.Format);

    // Extensions that name exactly one format. ".xml" names several (Flat OPC, 2003 XML) and ".docx" etc. on encrypted packages
    // are the same extension as the plain formats, so neither is here: content decides.
    private static readonly FrozenDictionary<string, OfficeFormat> s_byExtension = BuildExtensionMap();

    /// <summary>Gets the descriptors of all formats, in format order.</summary>
    public static IReadOnlyList<FormatDescriptor> All => s_all;

    /// <summary>Gets the descriptor of a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is <see cref="OfficeFormat.Unknown"/> or not a defined value.</exception>
    public static FormatDescriptor Get(OfficeFormat format) =>
        s_byFormat.TryGetValue(format, out FormatDescriptor? d) ? d : throw new ArgumentOutOfRangeException(nameof(format), format, "Not a known format.");

    /// <summary>
    /// Chooses a format from a file name's extension. This is only for <em>naming an output</em>; input is always identified from its
    /// content by <see cref="FormatSniffer"/>. An extension that names more than one format (<c>.xml</c>) is not mapped.
    /// </summary>
    /// <param name="pathOrExtension">A path or an extension with or without the dot; case is ignored.</param>
    /// <returns>The format, or <see cref="OfficeFormat.Unknown"/>.</returns>
    public static OfficeFormat FromExtension(string pathOrExtension)
    {
        ArgumentNullException.ThrowIfNull(pathOrExtension);
        string ext = pathOrExtension.Contains('.', StringComparison.Ordinal) ? Path.GetExtension(pathOrExtension) : "." + pathOrExtension;
        return s_byExtension.TryGetValue(ext.ToLowerInvariant(), out OfficeFormat f) ? f : OfficeFormat.Unknown;
    }

    private static FormatDescriptor D(OfficeFormat format, OfficeKind kind, string name, string mediaType, FormatTraits traits, params string[] extensions) =>
        new(format, kind, name, mediaType, extensions, traits);

    private static FrozenDictionary<string, OfficeFormat> BuildExtensionMap()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var owner = new Dictionary<string, OfficeFormat>(StringComparer.Ordinal);
        foreach (FormatDescriptor d in s_all)
        {
            if (d.Format == OfficeFormat.EncryptedPackage)
            {
                continue;
            }

            foreach (string e in d.Extensions)
            {
                counts[e] = counts.GetValueOrDefault(e) + 1;
                owner[e] = d.Format;
            }
        }

        var map = new Dictionary<string, OfficeFormat>(StringComparer.Ordinal);
        foreach ((string e, int n) in counts)
        {
            if (n == 1)
            {
                map[e] = owner[e];
            }
        }

        // ".xls" belongs to BIFF8, the one that can be written; BIFF2-5 files are told apart by content.
        map[".xls"] = OfficeFormat.Xls;
        return map.ToFrozenDictionary();
    }
}
