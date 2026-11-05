using RecursiveAI.OneOffice.Formats;

namespace RecursiveAI.OneOffice.Documents;

/// <summary>
/// Registers what this package can do with the word-processing formats, so that <see cref="Office.Open(string, OpenContext?, CancellationToken)"/>,
/// <see cref="Office.ExtractText(string, OpenContext?, CancellationToken)"/> and <c>SaveAs</c> reach it.
/// </summary>
/// <remarks>
/// Nothing is found by scanning assemblies: an application that uses the <see cref="Office"/> statics calls
/// <see cref="Register(FormatRegistry)"/> once at start, with <see cref="FormatRegistry.Default"/>. <see cref="Document.Open(string, OpenContext?, CancellationToken)"/>
/// needs no registration.
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
    }
}
