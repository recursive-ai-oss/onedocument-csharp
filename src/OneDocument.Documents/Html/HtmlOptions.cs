namespace RecursiveAI.OneDocument.Documents;

/// <summary>How much of an HTML page the importer lets into the document.</summary>
public enum HtmlSanitization
{
    /// <summary>
    /// The default. Scripts, event-handler attributes, <c>javascript:</c> and other script-capable URLs, forms and their controls, frames,
    /// objects, embeds, plug-ins, canvases and inline vector or math islands never reach the document, and each is a loss item. Links keep
    /// <c>http</c>, <c>https</c>, <c>mailto</c>, <c>tel</c>, <c>ftp</c>, relative and in-page targets only.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// A raw-but-safe superset: the text of form controls (button labels, field values, option labels) and the fallback text of frames and objects is
    /// kept as plain text, and links with other schemes (not script-capable ones) are kept. Scripts, handlers, <c>javascript:</c> / <c>vbscript:</c> /
    /// <c>data:</c> link targets and executable elements are still dropped, because a document cannot hold them.
    /// </summary>
    KeepInert = 1,
}

/// <summary>A resource an <see cref="HtmlImportOptions.ImageResolver"/> returns.</summary>
/// <param name="Data">The image bytes (PNG, JPEG, GIF or BMP).</param>
public readonly record struct HtmlResource(ReadOnlyMemory<byte> Data);

/// <summary>Options of the HTML importer. Nothing here changes what a document may do: no option fetches anything on its own.</summary>
public sealed class HtmlImportOptions
{
    /// <summary>Gets or sets how much is let in. Default <see cref="HtmlSanitization.Strict"/>.</summary>
    public HtmlSanitization Sanitization { get; init; } = HtmlSanitization.Strict;

    /// <summary>
    /// Gets or sets the callback that supplies images whose <c>src</c> is not a <c>data:</c> URI, given the attribute value as written. The default is
    /// null: nothing external is read and each such image is a loss item (its alternate text stays). The callback decides what may be read; the
    /// importer never opens a URL itself. Return null to refuse one image.
    /// </summary>
    public Func<string, HtmlResource?>? ImageResolver { get; init; }

    /// <summary>Gets or sets the language written into the document defaults when the page declares none (a BCP 47 tag). Default null: none.</summary>
    public string? DefaultLanguage { get; init; }
}
