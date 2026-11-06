namespace RecursiveAI.OneDocument.Documents;

/// <summary>Hands a picture found while writing Markdown to the caller, who stores it and says what address to write in the Markdown.</summary>
/// <param name="suggestedName">A file name for the picture, such as <c>image1.png</c>; unique within one export.</param>
/// <param name="contentType">The media type of the bytes, for example <c>image/png</c>.</param>
/// <param name="data">The picture exactly as it is stored in the document.</param>
/// <returns>The address to write in the image link, for example a relative path; null or empty to leave the picture out.</returns>
public delegate string? MarkdownImageSink(string suggestedName, string contentType, ReadOnlyMemory<byte> data);

/// <summary>Choices for reading Markdown into a <see cref="Document"/>. Everything has a safe default.</summary>
public sealed class MarkdownImportOptions
{
    /// <summary>
    /// Gets a function that returns the bytes of a picture for an image address that is not a <c>data:</c> address, or null for none. The default is
    /// null: nothing is read from a path or fetched from a URL unless the caller supplies this. The function is called once per image link, on the reading thread.
    /// </summary>
    public Func<string, byte[]?>? ImageResolver { get; init; }

    /// <summary>
    /// Gets a value indicating whether a front matter block (a first line <c>---</c>, then <c>key: value</c> lines up to a closing <c>---</c>) is
    /// read into the document properties (title, author, subject, description, keywords). The default is true. When false the block is ordinary Markdown.
    /// </summary>
    public bool FrontMatter { get; init; } = true;
}

/// <summary>Choices for writing a <see cref="Document"/> as Markdown.</summary>
public sealed class MarkdownExportOptions
{
    /// <summary>
    /// Gets the function that stores pictures. When null (the default) a picture is written as an image link to a name such as
    /// <c>media/image1.png</c> and the loss report says the picture bytes were not written.
    /// </summary>
    public MarkdownImageSink? ImageSink { get; init; }
}
