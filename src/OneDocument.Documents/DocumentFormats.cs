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
    /// <summary>The formats this package reads and writes: DOCX, DOCM, DOTX, DOTM and Flat OPC.</summary>
    public static IReadOnlyList<OfficeFormat> Formats { get; } =
        [OfficeFormat.Docx, OfficeFormat.Docm, OfficeFormat.Dotx, OfficeFormat.Dotm, OfficeFormat.FlatOpcDocument];

    /// <summary>
    /// Registers the readers and writers of <see cref="Formats"/>. Text extraction for them is already in the core package's default
    /// registry, and is left as it is.
    /// </summary>
    /// <param name="registry">The registry to fill.</param>
    public static void Register(FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        OfficeFormat[] formats = [.. Formats];
        registry.RegisterReader(
            static (source, _, context, filePath, cancellationToken) => Document.OpenSource(source, context, filePath, cancellationToken),
            formats);
        registry.RegisterWriter(
            static (file, format, destination, cancellationToken) =>
            {
                if (file is not Document document)
                {
                    throw new FormatNotAvailableException(format, "write a " + file.Kind + " as");
                }

                document.Write(format, destination, cancellationToken);
            },
            formats);
        registry.RegisterTemplateBinder(Templating.DocxTemplateBinder.Instance, formats);
        registry.RegisterReader(
            static (source, _, context, filePath, cancellationToken) => Document.OpenSource(source, context, filePath, cancellationToken),
            OfficeFormat.Doc);

        // Word 97-2003 files (and Word 6/95) are read only: text without building the model, or the model through the hub.
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
