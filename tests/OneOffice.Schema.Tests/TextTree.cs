using System;
using System.Text;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Schema.Tests;

/// <summary>A markup document parsed from text, with the root as a <see cref="MarkupRef"/> and the serialization as text. Test and smoke-app helper.</summary>
public sealed class TextTree
{
    private readonly MarkupDocument _document;

    private TextTree(MarkupDocument document) => _document = document;

    /// <summary>Parses a document.</summary>
    public static TextTree Parse(string xml) => new(MarkupDocument.Parse(Encoding.UTF8.GetBytes(xml)));

    /// <summary>The root element.</summary>
    public MarkupRef RootRef => new(_document.Root);

    /// <summary>The document as text: original bytes where nothing was edited.</summary>
    public string Serialize() => Encoding.UTF8.GetString(_document.ToArray());
}
