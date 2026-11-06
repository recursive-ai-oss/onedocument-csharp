namespace RecursiveAI.OneDocument;

/// <summary>An image the HTML exporter is about to write.</summary>
/// <param name="SuggestedName">A file name with the right extension, unique in the export.</param>
/// <param name="ContentType">The media type.</param>
/// <param name="Data">The bytes.</param>
public readonly record struct HtmlImage(string SuggestedName, string ContentType, ReadOnlyMemory<byte> Data);

/// <summary>Options of the HTML exporter.</summary>
public sealed class HtmlExportOptions
{
    /// <summary>Gets or sets whether to write a fragment (the content only, with no <c>html</c>, <c>head</c> or <c>body</c> and no style sheet). Default false.</summary>
    public bool Fragment { get; init; }

    /// <summary>
    /// Gets or sets the callback that stores an image and returns the URL to write for it (for example a path relative to the HTML file). The default
    /// is null, and a callback that returns null keeps that image inline: every image becomes a <c>data:</c> URI, so the output is one self-contained file.
    /// </summary>
    public Func<HtmlImage, string?>? ImageSink { get; init; }
}
