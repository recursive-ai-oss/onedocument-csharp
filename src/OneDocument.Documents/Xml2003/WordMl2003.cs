using System.Buffers;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Xml2003;

/// <summary>
/// Reads Word 2003 XML (WordprocessingML 2003: a <c>w:wordDocument</c> in the <c>http://schemas.microsoft.com/office/word/2003/wordml</c>
/// namespace) into the document hub. The file is converted to an in-memory DOCX package, which <see cref="Document"/> then wraps like any
/// other, and everything that did not map is reported to a <see cref="LossReport"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mapped: document properties, fonts, styles with inheritance, list definitions and overrides, sections with headers and footers,
/// paragraphs and runs with their properties, tables, fields (simple and complex), hyperlinks, bookmarks, footnotes and endnotes,
/// comments and tracked insertions and deletions (<c>aml:annotation</c>), pictures (<c>w:binData</c> with <c>v:imagedata</c>) and
/// text boxes. The <c>wx:</c> auxiliary elements are hints: section and sub-section wrappers and border groups are read through, the rest
/// is ignored. Nothing is fetched: a hyperlink target becomes an external relationship and is never followed.
/// </para>
/// <para>
/// Repairs are defects with the codes <c>wml2003.*</c> (see the defect catalogue); the file is never rejected for a bad value, only for
/// not being well-formed XML or not being a <c>w:wordDocument</c>.
/// </para>
/// </remarks>
public static class WordMl2003
{
    /// <summary>Converts a Word 2003 XML file to an in-memory DOCX package.</summary>
    /// <param name="source">The file's bytes.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="loss">Receives what could not be carried over; null to discard it.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>A package that <see cref="Document.FromPackage(IOpcPackage, OpenContext?, bool)"/> accepts. The caller disposes it.</returns>
    /// <exception cref="OfficeFormatException">The file is not well-formed XML (<c>xml.*</c>) or its root is not a <c>w:wordDocument</c> (<c>wml2003.not-wordml</c>).</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static OpcPackage ToPackage(IByteSource source, OpenContext? context = null, ILossSink? loss = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        context ??= new OpenContext();
        ReadOnlyMemory<byte> bytes = Xml2003Input.ReadAll(source, context.Limits);
        MarkupDocument markup = MarkupDocument.Parse(bytes, context);
        return new WordMl2003Converter(markup, context, loss ?? new LossReport(), cancellationToken).Convert();
    }

    /// <summary>Opens Word 2003 XML as a document. The source is read at once and left open.</summary>
    /// <param name="source">The file's bytes.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The document, which saves as DOCX; <see cref="Document.ImportLoss"/> lists what did not map.</returns>
    /// <exception cref="OfficeFormatException">The file is not well-formed XML or is not a <c>w:wordDocument</c>.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static Document Open(IByteSource source, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        context ??= new OpenContext();
        var loss = new LossReport();
        OpcPackage package = ToPackage(source, context, loss, cancellationToken);
        try
        {
            Document document = Document.FromPackage(package, context, ownsPackage: true);
            document.ImportLoss = loss;
            return document;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }
}

/// <summary>Reads a whole input into memory under the part-size limit.</summary>
internal static class Xml2003Input
{
    public static ReadOnlyMemory<byte> ReadAll(IByteSource source, OfficeLimits limits)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), limits.MaxPartSize, source.Length, DefectLocation.InPart("/", 0));
        if (source.TryGetMemory(out ReadOnlyMemory<byte> memory))
        {
            return memory;
        }

        var buffer = new byte[(int)source.Length];
        int total = 0;
        while (total < buffer.Length)
        {
            int n = source.Read(total, buffer.AsSpan(total));
            if (n <= 0)
            {
                break;
            }

            total += n;
        }

        return buffer.AsMemory(0, total);
    }
}

/// <summary>Namespaces of the 2003 formats, as a small code per element so names compare without allocating.</summary>
internal static class Ns2003
{
    public const string W = "http://schemas.microsoft.com/office/word/2003/wordml";
    public const string Wx = "http://schemas.microsoft.com/office/word/2003/auxHint";
    public const string Aml = "http://schemas.microsoft.com/aml/2001/core";
    public const string V = "urn:schemas-microsoft-com:vml";
    public const string O = "urn:schemas-microsoft-com:office:office";
    public const string W10 = "urn:schemas-microsoft-com:office:word";

    public const int None = 0;
    public const int Word = 1;
    public const int Aux = 2;
    public const int Annotation = 3;
    public const int Vml = 4;
    public const int Office = 5;
    public const int Word10 = 6;

    public static int Of(MarkupNode n)
    {
        ReadOnlySpan<byte> ns = n.NamespaceUriBytes;
        if (ns.SequenceEqual("http://schemas.microsoft.com/office/word/2003/wordml"u8))
        {
            return Word;
        }

        if (ns.SequenceEqual("http://schemas.microsoft.com/office/word/2003/auxHint"u8))
        {
            return Aux;
        }

        if (ns.SequenceEqual("http://schemas.microsoft.com/aml/2001/core"u8))
        {
            return Annotation;
        }

        if (ns.SequenceEqual("urn:schemas-microsoft-com:vml"u8))
        {
            return Vml;
        }

        if (ns.SequenceEqual("urn:schemas-microsoft-com:office:office"u8))
        {
            return Office;
        }

        return ns.SequenceEqual("urn:schemas-microsoft-com:office:word"u8) ? Word10 : None;
    }

    public static int OfAttribute(MarkupAttributeRef a)
    {
        ReadOnlySpan<byte> ns = a.NamespaceUriBytes;
        return ns.SequenceEqual("http://schemas.microsoft.com/office/word/2003/wordml"u8) ? Word : None;
    }
}
