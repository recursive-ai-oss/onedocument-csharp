using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Odf;

/// <summary>The media types of OpenDocument packages (OpenDocument 1.3 Part 3, the <c>mimetype</c> file) and the body element each type holds.</summary>
public static class OdfMediaTypes
{
    /// <summary>OpenDocument text.</summary>
    public const string Text = "application/vnd.oasis.opendocument.text";

    /// <summary>OpenDocument text template.</summary>
    public const string TextTemplate = "application/vnd.oasis.opendocument.text-template";

    /// <summary>OpenDocument spreadsheet.</summary>
    public const string Spreadsheet = "application/vnd.oasis.opendocument.spreadsheet";

    /// <summary>OpenDocument spreadsheet template.</summary>
    public const string SpreadsheetTemplate = "application/vnd.oasis.opendocument.spreadsheet-template";

    /// <summary>OpenDocument presentation.</summary>
    public const string Presentation = "application/vnd.oasis.opendocument.presentation";

    /// <summary>OpenDocument presentation template.</summary>
    public const string PresentationTemplate = "application/vnd.oasis.opendocument.presentation-template";

    /// <summary>Gets the local name of the child of <c>office:body</c> that a media type holds: <c>text</c>, <c>spreadsheet</c> or <c>presentation</c>.</summary>
    /// <param name="mimeType">The media type.</param>
    /// <returns>The element's local name; <c>text</c> for an unknown type.</returns>
    public static string BodyElement(string mimeType)
    {
        ArgumentNullException.ThrowIfNull(mimeType);
        if (mimeType.StartsWith(Spreadsheet, StringComparison.Ordinal))
        {
            return "spreadsheet";
        }

        return mimeType.StartsWith(Presentation, StringComparison.Ordinal) ? "presentation" : "text";
    }

    /// <summary>Gets the media type implied by the first child of an <c>office:body</c> element.</summary>
    /// <param name="body">The body element, or a null node.</param>
    /// <returns>The document media type, or an empty string when the body is missing or holds none of the three.</returns>
    public static string FromBody(MarkupNode body)
    {
        if (body.IsNull)
        {
            return string.Empty;
        }

        foreach (MarkupNode c in body.ChildElements)
        {
            if (c.NamespaceUri != OdfNamespaces.Office)
            {
                continue;
            }

            switch (c.LocalNameString)
            {
                case "text":
                    return Text;
                case "spreadsheet":
                    return Spreadsheet;
                case "presentation":
                    return Presentation;
            }
        }

        return string.Empty;
    }
}
