using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Styles;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>
/// A style was asked for by name and the document has no such style of the wanted type. Nothing is created in its place: add the
/// style first (<see cref="StyleSheet.AddBuiltIn(string)"/> for the common built-in ones, or <see cref="StyleSheet.AddStyle"/>).
/// </summary>
public sealed class StyleNotFoundException : OfficeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="name">The style name that was asked for.</param>
    /// <param name="wanted">The type of style that was wanted.</param>
    /// <param name="reason">Why nothing matched: the document has no such name, or the name belongs to another type.</param>
    public StyleNotFoundException(string name, StyleType wanted, string reason)
        : base(new Defect(DefectKind.Reference, "docx.style-not-found", DefectSeverity.Fatal,
            "There is no " + wanted.ToString().ToLowerInvariant() + " style named '" + name + "': " + reason, DefectLocation.None))
    {
        Name = name;
        Wanted = wanted;
    }

    /// <summary>Gets the style name that was asked for.</summary>
    public string Name { get; }

    /// <summary>Gets the type of style that was wanted.</summary>
    public StyleType Wanted { get; }
}

public sealed partial class Paragraph
{
    /// <summary>
    /// Gets or sets the name of the paragraph's style, as Word shows it ("Heading 1"). Reading gives the name of the style the paragraph
    /// uses: the one it names, or the document's default paragraph style ("Normal") when it names none; a style id the style sheet does
    /// not define reads as the id itself. Setting applies the style with that name (ignoring case); setting null removes the direct
    /// reference, so the paragraph falls back to the default style. <see cref="StyleId"/> reads and writes the id instead.
    /// </summary>
    /// <exception cref="StyleNotFoundException">When setting: the document has no paragraph style of that name; nothing is created (<c>docx.style-not-found</c>).</exception>
    public string? Style
    {
        get
        {
            StyleDefinition? style = StyleId is { } id ? Owner.Styles.Find(id) : Owner.Styles.Default(StyleType.Paragraph);
            return style is null ? StyleId : StyleSheet.DisplayName(style);
        }
        set
        {
            if (value is null)
            {
                StyleId = null;
                return;
            }

            StyleId = Owner.Styles.Require(value, StyleType.Paragraph).Id;
        }
    }
}

public sealed partial class Run
{
    /// <summary>
    /// Gets or sets the name of the character style applied to the run ("Strong"), or null when it has none. Setting applies the
    /// character style with that name (ignoring case); null removes it. <see cref="StyleId"/> reads and writes the id instead.
    /// </summary>
    /// <exception cref="StyleNotFoundException">When setting: the document has no character style of that name; nothing is created (<c>docx.style-not-found</c>).</exception>
    public string? Style
    {
        get => StyleId is { } id ? (Owner.Styles.Find(id) is { } s ? StyleSheet.DisplayName(s) : id) : null;
        set => StyleId = value is null ? null : Owner.Styles.Require(value, StyleType.Character).Id;
    }
}

public sealed partial class Table
{
    /// <summary>
    /// Gets or sets the name of the table style ("Table Grid"), or null when the table names none. Setting applies the table style with
    /// that name (ignoring case); null removes it. <see cref="StyleId"/> reads and writes the id instead.
    /// </summary>
    /// <exception cref="StyleNotFoundException">When setting: the document has no table style of that name; nothing is created (<c>docx.style-not-found</c>).</exception>
    public string? Style
    {
        get => StyleId is { } id ? (Owner.Styles.Find(id) is { } s ? StyleSheet.DisplayName(s) : id) : null;
        set => StyleId = value is null ? null : Owner.Styles.Require(value, StyleType.Table).Id;
    }
}

