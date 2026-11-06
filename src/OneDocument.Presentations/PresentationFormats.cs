using RecursiveAI.OneDocument.Formats;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>
/// Registers what this package can do with the presentation formats, so that <see cref="Office.Open(string, OpenContext?, CancellationToken)"/>,
/// <see cref="Office.ExtractText(string, OpenContext?, CancellationToken)"/> and <c>SaveAs</c> reach it.
/// </summary>
/// <remarks>
/// Nothing is found by scanning assemblies. A C# project that references this package gets a module initializer at build time
/// that calls <see cref="Register(FormatRegistry)"/> with <see cref="FormatRegistry.Default"/>, so the <see cref="Office"/> statics need no setup; call it
/// yourself for a custom registry, or when the project sets <c>OneDocumentAutoRegister</c> to <c>false</c>. Calling it again is harmless.
/// </remarks>
public static class PresentationFormats
{
    /// <summary>The formats this package reads and writes (PowerPoint 97-2003 <c>.ppt</c> is also read, converted to PresentationML with a loss report): PPTX, PPTM, POTX, POTM, PPSX, PPSM, PPAM and Flat OPC.</summary>
    public static IReadOnlyList<OfficeFormat> Formats { get; } =
    [
        OfficeFormat.Pptx, OfficeFormat.Pptm, OfficeFormat.Potx, OfficeFormat.Potm, OfficeFormat.Ppsx, OfficeFormat.Ppsm, OfficeFormat.Ppam,
        OfficeFormat.FlatOpcPresentation,
    ];

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
            static (source, _, context, filePath, cancellationToken) => Presentation.OpenSource(source, context, filePath, cancellationToken),
            formats);
        registry.RegisterWriter(
            static (file, format, destination, cancellationToken) =>
            {
                if (file is not Presentation presentation)
                {
                    throw new FormatNotAvailableException(format, "write a " + file.Kind + " as");
                }

                presentation.Write(format, destination, cancellationToken);
            },
            formats);
        registry.RegisterReader(
            static (source, _, context, filePath, cancellationToken) => Presentation.OpenSource(source, context, filePath, cancellationToken),
            OfficeFormat.Ppt);

        // Markdown is written by every kind; this entry takes its own kind and passes the rest on to whichever package registered before.
        registry.TryGetWriter(OfficeFormat.Markdown, out WriteHandler? previousMarkdown);
        registry.RegisterWriter(
            (file, format, destination, cancellationToken) =>
            {
                if (file is Presentation presentation)
                {
                    presentation.WriteMarkdown(destination, cancellationToken);
                }
                else if (previousMarkdown is not null)
                {
                    previousMarkdown(file, format, destination, cancellationToken);
                }
                else
                {
                    throw new FormatNotAvailableException(format, "write a " + file.Kind + " as");
                }
            },
            OfficeFormat.Markdown);
        registry.RegisterTemplateBinder(Templating.PresentationTemplateBinder.Instance, formats);
    }
}
