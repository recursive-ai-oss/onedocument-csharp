using RecursiveAI.OneDocument.Formats;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>
/// Registers what this package can do with the word-processing formats, so that <see cref="Office.Open(string, OpenContext?, CancellationToken)"/>,
/// <see cref="Office.ExtractText(string, OpenContext?, CancellationToken)"/> and <c>SaveAs</c> reach it.
/// </summary>
/// <remarks>
/// Nothing is found by scanning assemblies. A C# project that references this package gets a module initializer at build time
/// that calls <see cref="Register(FormatRegistry)"/> with <see cref="FormatRegistry.Default"/>, so the <see cref="Office"/> statics need no setup; call it
/// yourself for a custom registry, or when the project sets <c>OneDocumentAutoRegister</c> to <c>false</c>. Calling it again is harmless.
/// </remarks>
public static class DocumentFormats
{
    /// <summary>The formats this package reads and writes: DOCX, DOCM, DOTX, DOTM, Flat OPC, RTF and OpenDocument text (ODT, OTT, FODT).</summary>
    public static IReadOnlyList<OfficeFormat> Formats { get; } =
        [OfficeFormat.Docx, OfficeFormat.Docm, OfficeFormat.Dotx, OfficeFormat.Dotm, OfficeFormat.FlatOpcDocument, OfficeFormat.Rtf, OfficeFormat.Odt, OfficeFormat.Ott, OfficeFormat.Fodt];

    /// <summary>The formats this package reads but does not write: Word 2003 XML and Word 97-2003 (incl. Word 6/95) binary files, which open as a document that saves as DOCX.</summary>
    public static IReadOnlyList<OfficeFormat> ReadOnlyFormats { get; } = [OfficeFormat.Word2003Xml, OfficeFormat.Doc];

    /// <summary>
    /// Registers the readers and writers of <see cref="Formats"/> and the readers of <see cref="ReadOnlyFormats"/>. Text extraction for them is already in the core package's default
    /// registry, and is left as it is.
    /// </summary>
    /// <param name="registry">The registry to fill.</param>
    public static void Register(FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        OfficeFormat[] ooxml = [OfficeFormat.Docx, OfficeFormat.Docm, OfficeFormat.Dotx, OfficeFormat.Dotm, OfficeFormat.FlatOpcDocument];
        // One reader opens every format of the package: it sniffs the content and takes the RTF path when the bytes are RTF.
        registry.RegisterReader(
            static (source, _, context, filePath, cancellationToken) => Document.OpenSource(source, context, filePath, cancellationToken),
            [.. Formats]);
        registry.RegisterWriter(
            static (file, format, destination, cancellationToken) =>
            {
                if (file is not Document document)
                {
                    throw new FormatNotAvailableException(format, "write a " + file.Kind + " as");
                }

                if (format == OfficeFormat.Rtf)
                {
                    document.WriteRtf(destination, cancellationToken);
                }
                else if (Document.IsOdfTextFormat(format))
                {
                    document.WriteOdf(format, destination, cancellationToken);
                }
                else
                {
                    document.Write(format, destination, cancellationToken);
                }
            },
            [.. Formats]);
        registry.RegisterTemplateBinder(Templating.DocxTemplateBinder.Instance, ooxml);

        // Word 2003 XML and Word 97-2003 (and Word 6/95) files are read only: they open as a document that saves as DOCX.
        registry.RegisterReader(
            static (source, _, context, filePath, cancellationToken) => Document.OpenSource(source, context, filePath, cancellationToken),
            [.. ReadOnlyFormats]);

        // Word binary files also extract text without building the model.
        registry.RegisterTextExtractor(
            static (source, _, context, output, cancellationToken) =>
                Doc.DocGuard.Run(() =>
                {
                    var file = Doc.Binary.DocFile.Open(source, context);
                    Doc.Binary.DocTextExtractor.Extract(file, output, includeParts: true, cancellationToken);
                }),
            OfficeFormat.Doc);
    }
}
