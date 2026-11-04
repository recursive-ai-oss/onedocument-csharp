using RecursiveAI.OneCompress;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Formats;
using RecursiveAI.OneOffice.IO;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.TextExtraction;

/// <summary>
/// Extracts text from OOXML packages (word-processing, spreadsheet and presentation) by streaming the parts that hold text, in reading
/// order, without building the document model. It is built for indexing and comparing: fast, constant memory in the size of the main
/// part, and with the same output for every way the producer may have split runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Word-processing documents:</b> paragraphs one per line; a tab as a tab and a line break as a line feed; a table row on one line
/// with its cells separated by tabs (paragraphs and line breaks inside a cell become spaces; a table inside a cell flattens into it);
/// fields as their results; text boxes after the paragraph that holds them; <c>mc:AlternateContent</c> read once, from the first
/// branch whose required namespaces Office understands; deleted revision text left out unless
/// <see cref="TextExtractionOptions.IncludeDeletedText"/> says otherwise. After the main story come headers, footers, footnotes,
/// endnotes and comments, each as asked by the options.
/// </para>
/// <para>
/// <b>Presentations:</b> slides in presentation order, shapes in document order, then each slide's speaker notes. <b>Workbooks:</b> sheets
/// in workbook order; for each row the string cells (shared, inline and formula string results), separated by tabs.
/// </para>
/// <para>
/// A part other than the main one that cannot be read is left out and reported to <see cref="OpenContext.Defects"/> (<c>text.part-missing</c>,
/// <c>text.part-unreadable</c>); the extraction goes on. Damage in the main part, or a limit, raises the exception.
/// </para>
/// </remarks>
public static class OoxmlTextExtractor
{
    /// <summary>Extracts the text of an OOXML package to a writer.</summary>
    /// <param name="source">The input; it is left open.</param>
    /// <param name="output">Where the text goes.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="options">What to include; null for <see cref="TextExtractionOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancels the extraction; text already written stays written.</param>
    /// <exception cref="OfficeFormatException">The content is not an OOXML package, or its main part is damaged.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="FormatNotAvailableException">The content is a format this extractor does not read.</exception>
    public static void Extract(IByteSource source, TextWriter output, OpenContext? context = null, TextExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        context ??= new OpenContext();
        Run(source, Detect(source, context), context, options ?? TextExtractionOptions.Default, new PlainTextSink(output), cancellationToken);
    }

    /// <summary>Extracts the text of an OOXML file to a writer.</summary>
    /// <param name="path">The path.</param>
    /// <param name="output">Where the text goes.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="options">What to include; null for <see cref="TextExtractionOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    /// <exception cref="OfficeFormatException">The content is not an OOXML package, or its main part is damaged.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static void Extract(string path, TextWriter output, OpenContext? context = null, TextExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        using IByteSource source = ByteSource.FromFile(path);
        Extract(source, output, context, options, cancellationToken);
    }

    /// <summary>Extracts the text of an OOXML package as paragraphs with their positions.</summary>
    /// <param name="source">The input; it is left open.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="options">What to include; null for <see cref="TextExtractionOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    /// <returns>The paragraphs. Memory is proportional to the text.</returns>
    /// <exception cref="OfficeFormatException">The content is not an OOXML package, or its main part is damaged.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static NormalizedText ExtractNormalized(IByteSource source, OpenContext? context = null, TextExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        context ??= new OpenContext();
        var sink = new NormalizedSink();
        Run(source, Detect(source, context), context, options ?? TextExtractionOptions.Default, sink, cancellationToken);
        return sink.ToResult();
    }

    /// <summary>Extracts the text of an OOXML file as paragraphs with their positions.</summary>
    /// <param name="path">The path.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="options">What to include; null for <see cref="TextExtractionOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    /// <returns>The paragraphs.</returns>
    /// <exception cref="OfficeFormatException">The content is not an OOXML package, or its main part is damaged.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static NormalizedText ExtractNormalized(string path, OpenContext? context = null, TextExtractionOptions? options = null, CancellationToken cancellationToken = default)
    {
        using IByteSource source = ByteSource.FromFile(path);
        return ExtractNormalized(source, context, options, cancellationToken);
    }

    /// <summary>Registers the extractor for the OOXML formats.</summary>
    /// <param name="registry">The registry to fill.</param>
    internal static void Register(FormatRegistry registry) =>
        registry.RegisterTextExtractor(
            static (source, format, context, output, cancellationToken) =>
                Run(source, format, context, TextExtractionOptions.Default, new PlainTextSink(output), cancellationToken),
            OfficeFormat.Docx, OfficeFormat.Docm, OfficeFormat.Dotx, OfficeFormat.Dotm,
            OfficeFormat.Xlsx, OfficeFormat.Xlsm, OfficeFormat.Xltx, OfficeFormat.Xltm, OfficeFormat.Xlam,
            OfficeFormat.FlatOpcDocument, OfficeFormat.FlatOpcWorkbook, OfficeFormat.FlatOpcPresentation,
            OfficeFormat.Pptx, OfficeFormat.Pptm, OfficeFormat.Potx, OfficeFormat.Potm, OfficeFormat.Ppsx, OfficeFormat.Ppsm, OfficeFormat.Ppam);

    private static OfficeFormat Detect(IByteSource source, OpenContext context)
    {
        OfficeFormat format = FormatSniffer.Detect(source, context.Limits);
        if (format == OfficeFormat.Unknown)
        {
            throw new OfficeFormatException(DefectKind.Structure, "format.unrecognised", "The content is not a format OneOffice knows.");
        }

        return format;
    }

    private static void Run(IByteSource source, OfficeFormat format, OpenContext context, TextExtractionOptions options, TextSink sink, CancellationToken cancellationToken)
    {
        if (!IsOoxml(format))
        {
            throw new FormatNotAvailableException(format, "extract text from");
        }

        OfficeKind kind = OfficeFormats.Get(format).Kind;
        try
        {
            using var parts = new OoxmlParts(source, context);
            string main = parts.FindMainPart()
                ?? throw new OfficeFormatException(DefectKind.Structure, "text.no-main-part", "The package has no main document part.");
            var job = new Job(parts, context, options, sink, cancellationToken);
            switch (kind)
            {
                case OfficeKind.Document:
                    job.Document(main);
                    break;
                case OfficeKind.Workbook:
                    job.Workbook(main);
                    break;
                default:
                    job.Presentation(main);
                    break;
            }
        }
        catch (CompressionException ex)
        {
            throw ArchiveErrors.Translate(ex);
        }
    }

    private static bool IsOoxml(OfficeFormat format) => format is
        OfficeFormat.Docx or OfficeFormat.Docm or OfficeFormat.Dotx or OfficeFormat.Dotm or OfficeFormat.FlatOpcDocument
        or OfficeFormat.Xlsx or OfficeFormat.Xlsm or OfficeFormat.Xltx or OfficeFormat.Xltm or OfficeFormat.Xlam or OfficeFormat.FlatOpcWorkbook
        or OfficeFormat.Pptx or OfficeFormat.Pptm or OfficeFormat.Potx or OfficeFormat.Potm or OfficeFormat.Ppsx or OfficeFormat.Ppsm or OfficeFormat.Ppam or OfficeFormat.FlatOpcPresentation;

    private sealed class Job(OoxmlParts parts, OpenContext context, TextExtractionOptions options, TextSink sink, CancellationToken cancellation)
    {
        public void Document(string main)
        {
            List<Relationship> rels = parts.ReadRelationships(main);
            Part(main, TextStory.Body, r => new WordTextExtractor(context, options, sink, main, cancellation).Run(r));
            if (options.IncludeHeadersAndFooters)
            {
                Related(main, rels, "/header", TextStory.Header, WordPart);
                Related(main, rels, "/footer", TextStory.Footer, WordPart);
            }

            if (options.IncludeNotes)
            {
                Related(main, rels, "/footnotes", TextStory.Footnotes, WordPart);
                Related(main, rels, "/endnotes", TextStory.Endnotes, WordPart);
            }

            if (options.IncludeComments)
            {
                Related(main, rels, "/comments", TextStory.Comments, WordPart);
            }

            // Diagram text lives in a data part of its own; it is output after the story, not where the diagram is anchored.
            Related(main, rels, "/diagramData", TextStory.Diagram, DrawingPart);
        }

        public void Workbook(string main)
        {
            List<Relationship> rels = parts.ReadRelationships(main);
            SharedStrings strings = SharedStrings.Empty;
            foreach (Relationship rel in rels)
            {
                if (rel.Type.EndsWith("/sharedStrings", StringComparison.Ordinal) && OoxmlParts.Resolve(main, rel) is { } sst)
                {
                    // The table is read before the sheets because cells name strings by index; if it is damaged, string cells are lost.
                    if (OptionalPart(sst, TextStory.Sheet, r => strings = SharedStrings.Read(r, context, cancellation)))
                    {
                        break;
                    }
                }
            }

            foreach (string id in RelationshipIds(main, "sheet", NsKind.Sml))
            {
                if (Find(rels, id) is { } rel && OoxmlParts.Resolve(main, rel) is { } sheet && rel.Type.EndsWith("/worksheet", StringComparison.Ordinal))
                {
                    OptionalPart(sheet, TextStory.Sheet, r => new SheetTextExtractor(context, strings, sink, sheet, cancellation).Run(r));
                }
            }
        }

        public void Presentation(string main)
        {
            List<Relationship> rels = parts.ReadRelationships(main);
            foreach (string id in RelationshipIds(main, "sldId", NsKind.Pml))
            {
                if (Find(rels, id) is not { } rel || OoxmlParts.Resolve(main, rel) is not { } slide || !rel.Type.EndsWith("/slide", StringComparison.Ordinal))
                {
                    continue;
                }

                OptionalPart(slide, TextStory.Slide, r => new DrawingTextExtractor(context, sink, cancellation).Run(r));
                List<Relationship> slideRels = parts.ReadRelationships(slide);
                if (options.IncludeNotes)
                {
                    Related(slide, slideRels, "/notesSlide", TextStory.SlideNotes, DrawingPart);
                }

                Related(slide, slideRels, "/diagramData", TextStory.Diagram, DrawingPart);
            }
        }

        private Action<MarkupReader> WordPart(string partName) => r => new WordTextExtractor(context, options, sink, partName, cancellation).Run(r);

        private Action<MarkupReader> DrawingPart(string _) => r => new DrawingTextExtractor(context, sink, cancellation).Run(r);

        private void Related(string source, List<Relationship> rels, string typeSuffix, TextStory story, Func<string, Action<MarkupReader>> read)
        {
            foreach (Relationship rel in rels)
            {
                if (rel.Type.EndsWith(typeSuffix, StringComparison.Ordinal) && OoxmlParts.Resolve(source, rel) is { } target)
                {
                    OptionalPart(target, story, read(target));
                }
            }
        }

        private static Relationship? Find(List<Relationship> rels, string id)
        {
            foreach (Relationship rel in rels)
            {
                if (rel.Id == id)
                {
                    return rel;
                }
            }

            return null;
        }

        // The relationship ids of the elements called localName, in document order (the sheet order of a workbook, the slide order of a deck).
        private List<string> RelationshipIds(string partName, string localName, NsKind ns)
        {
            var ids = new List<string>();
            try
            {
                using Stream s = parts.Open(partName);
                using var r = new MarkupReader(s, context, partName, leaveOpen: true);
                byte[] wanted = System.Text.Encoding.ASCII.GetBytes(localName);
                while (r.Read())
                {
                    if (r.Kind is XmlTokenKind.StartElement or XmlTokenKind.EmptyElement
                        && r.LocalName.SequenceEqual(wanted) && Ns.Classify(r.NamespaceUri) == ns && Ns.TryGetRelationshipId(r, out string id))
                    {
                        ids.Add(id);
                    }
                }
            }
            catch (OfficeFormatException ex)
            {
                Report("text.part-unreadable", DefectKind.Markup, "The part that lists the sheets or slides cannot be read, so its list is incomplete.", ex.Location);
            }

            return ids;
        }

        // A part that must read, or the extraction fails with the part's own exception.
        private void Part(string partName, TextStory story, Action<MarkupReader> read)
        {
            if (!parts.Contains(partName))
            {
                throw new OfficeFormatException(DefectKind.Structure, "text.no-main-part", "The main document part named by the package is missing.", DefectLocation.InPart(partName));
            }

            sink.BeginPart(partName, story);
            try
            {
                using Stream s = parts.Open(partName);
                using var r = new MarkupReader(s, context, partName, leaveOpen: true);
                read(r);
            }
            finally
            {
                sink.EndPart();
            }

        }

        // A part whose loss costs some text, not the whole extraction. Returns whether it was read.
        private bool OptionalPart(string partName, TextStory story, Action<MarkupReader> read)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!parts.Contains(partName))
            {
                Report("text.part-missing", DefectKind.Reference, "A relationship names a part that the package does not have; its text is left out.", DefectLocation.InPart(partName));
                return false;
            }

            sink.BeginPart(partName, story);
            try
            {
                using Stream s = parts.Open(partName);
                using var r = new MarkupReader(s, context, partName, leaveOpen: true);
                read(r);
                return true;
            }
            catch (OfficeFormatException ex)
            {
                Report("text.part-unreadable", DefectKind.Markup, "A part is damaged; the text after the damage is left out. " + ex.Message, ex.Location);
                return false;
            }
            catch (CompressionException ex) when (ex is not CompressionLimitException)
            {
                Report("text.part-unreadable", DefectKind.Packaging, "A part is damaged in the archive; its text is left out. " + ex.Message, DefectLocation.InPart(partName));
                return false;
            }
            finally
            {
                sink.EndPart();
            }
        }

        private void Report(string code, DefectKind kind, string message, DefectLocation location) =>
            context.Defects.Report(new Defect(kind, code, DefectSeverity.Dropped, message, location));
    }
}
