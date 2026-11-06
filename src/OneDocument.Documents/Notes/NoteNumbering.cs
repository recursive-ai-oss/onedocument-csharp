using System.Globalization;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Documents.Notes;

/// <summary>Where notes are placed (<c>w:pos</c>).</summary>
public enum NotePosition
{
    /// <summary>Footnotes at the bottom of the page (<c>pageBottom</c>); the default for footnotes.</summary>
    PageBottom = 0,

    /// <summary>Footnotes directly under the text (<c>beneathText</c>).</summary>
    BeneathText = 1,

    /// <summary>Notes at the end of the section (<c>sectEnd</c>).</summary>
    SectionEnd = 2,

    /// <summary>Notes at the end of the document (<c>docEnd</c>); the default for endnotes.</summary>
    DocumentEnd = 3,
}

/// <summary>When note numbering starts over (<c>w:numRestart</c>).</summary>
public enum NoteRestart
{
    /// <summary>Numbering runs through the document (<c>continuous</c>); the default.</summary>
    Continuous = 0,

    /// <summary>Numbering restarts in each section (<c>eachSect</c>).</summary>
    EachSection = 1,

    /// <summary>Numbering restarts on each page (<c>eachPage</c>).</summary>
    EachPage = 2,
}

/// <summary>The numbering properties of notes after the section's and the document's settings are combined.</summary>
/// <param name="Position">Where the notes go.</param>
/// <param name="Format">The number format name (<c>decimal</c>, <c>lowerRoman</c>, …).</param>
/// <param name="Start">The first number.</param>
/// <param name="Restart">When numbering starts over.</param>
public readonly record struct EffectiveNoteNumbering(NotePosition Position, string Format, long Start, NoteRestart Restart);

/// <summary>
/// The note numbering properties held by one element: a section's <c>w:footnotePr</c>/<c>w:endnotePr</c>, or the document-wide
/// one in the settings part. A getter returns null when the property is not set there; a setter creates the property element
/// (and the settings part's or section's element around it) in schema order. Setting null removes the property.
/// </summary>
/// <remarks>ECMA-376 Part 1 §17.11 (<c>footnotePr</c> and <c>endnotePr</c> in a section and in the settings): the children are <c>pos</c>, <c>numFmt</c>, <c>numStart</c> and <c>numRestart</c>, in that order.</remarks>
public sealed class NoteNumbering
{
    private static readonly string[] Sequence = ["pos", "numFmt", "numStart", "numRestart", "footnote", "endnote"];

    // What follows footnotePr / endnotePr inside w:sectPr and w:settings in the schema sequences.
    private static readonly string[] AfterFootnotePrInSection =
        ["endnotePr", "type", "pgSz", "pgMar", "paperSrc", "pgBorders", "lnNumType", "pgNumType", "cols", "formProt", "vAlign", "noEndnote", "titlePg", "textDirection", "bidi", "rtlGutter", "docGrid", "printerSettings", "sectPrChange"];

    private static readonly string[] AfterEndnotePrInSection = AfterFootnotePrInSection[1..];

    private static readonly string[] AfterFootnotePrInSettings =
        ["endnotePr", "compat", "docVars", "rsids", "mathPr", "attachedSchema", "themeFontLang", "clrSchemeMapping", "doNotIncludeSubdocsInStats", "doNotAutoCompressPictures", "forceUpgrade", "captions", "readModeInkLockDown", "smartTagType", "schemaLibrary", "shapeDefaults", "doNotEmbedSmartTags", "decimalSymbol", "listSeparator"];

    private static readonly string[] AfterEndnotePrInSettings = AfterFootnotePrInSettings[1..];

    private readonly Document _owner;
    private readonly MarkupRef _parent;
    private readonly bool _isSettings;

    internal NoteNumbering(Document owner, NoteKind kind, MarkupRef parent, bool isSettings)
    {
        _owner = owner;
        Kind = kind;
        _parent = parent;
        _isSettings = isSettings;
    }

    /// <summary>Gets whether these are footnote or endnote properties.</summary>
    public NoteKind Kind { get; }

    private string PropertiesName => Kind == NoteKind.Footnote ? "footnotePr" : "endnotePr";

    private MarkupNode Properties => _parent.IsValid ? HubXml.FirstChildElement(_parent.Node, PropertiesName) : default;

    /// <summary>Gets a value indicating whether the properties element exists.</summary>
    public bool IsPresent => !Properties.IsNull;

    /// <summary>Gets or sets where notes go; null when not set. Footnotes allow page bottom and beneath text, endnotes section end and document end; other values are written as given.</summary>
    public NotePosition? Position
    {
        get => Get("pos") switch
        {
            "pageBottom" => NotePosition.PageBottom,
            "beneathText" => NotePosition.BeneathText,
            "sectEnd" => NotePosition.SectionEnd,
            "docEnd" => NotePosition.DocumentEnd,
            _ => null,
        };
        set => Set("pos", value switch
        {
            null => null,
            NotePosition.PageBottom => "pageBottom",
            NotePosition.BeneathText => "beneathText",
            NotePosition.SectionEnd => "sectEnd",
            _ => "docEnd",
        });
    }

    /// <summary>Gets or sets the number format name as ST_NumberFormat spells it (<c>decimal</c>, <c>upperRoman</c>, …); null when not set. The name is not checked.</summary>
    public string? NumberFormat
    {
        get => Get("numFmt");
        set => Set("numFmt", value);
    }

    /// <summary>Gets or sets the first note number (<c>w:numStart</c>); null when not set.</summary>
    public long? Start
    {
        get => Wml.Long(Get("numStart"));
        set => Set("numStart", value?.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Gets or sets when numbering starts over; null when not set.</summary>
    public NoteRestart? Restart
    {
        get => Get("numRestart") switch
        {
            "continuous" => NoteRestart.Continuous,
            "eachSect" => NoteRestart.EachSection,
            "eachPage" => NoteRestart.EachPage,
            _ => null,
        };
        set => Set("numRestart", value switch
        {
            null => null,
            NoteRestart.Continuous => "continuous",
            NoteRestart.EachSection => "eachSect",
            _ => "eachPage",
        });
    }

    /// <summary>
    /// Combines a section's properties with the document's: what the section sets wins, then the document's, then the defaults
    /// (footnotes at the page bottom and decimal; endnotes at the end of the document and lower-case roman, which is what Word 365
    /// (16.0.20527) shows when no <c>w:numFmt</c> is given anywhere; from 1, continuous).
    /// </summary>
    /// <param name="section">The section's properties; null to use the document's only.</param>
    /// <param name="document">The document-wide properties.</param>
    /// <returns>The effective values.</returns>
    public static EffectiveNoteNumbering Combine(NoteNumbering? section, NoteNumbering document)
    {
        ArgumentNullException.ThrowIfNull(document);
        NoteKind kind = document.Kind;
        // Observed in Word 16.0.20527: a footnote position in the document settings is not applied (only a section's own is), so a
        // document-wide "beneathText" leaves the notes at the page bottom.
        NotePosition? documentPosition = kind == NoteKind.Footnote ? null : document.Position;
        return new EffectiveNoteNumbering(
            section?.Position ?? documentPosition ?? (kind == NoteKind.Footnote ? NotePosition.PageBottom : NotePosition.DocumentEnd),
            section?.NumberFormat ?? document.NumberFormat ?? (kind == NoteKind.Footnote ? "decimal" : "lowerRoman"),
            section?.Start ?? document.Start ?? 1,
            section?.Restart ?? document.Restart ?? NoteRestart.Continuous);
    }

    internal void AddSeparatorReferences()
    {
        // The settings list the separator notes by id so Word knows which notes are the separators.
        if (!_isSettings || !_parent.IsValid)
        {
            return;
        }

        MarkupNode pr = EnsureProperties();
        string child = Kind == NoteKind.Footnote ? "footnote" : "endnote";
        if (!HubXml.FirstChildElement(pr, child).IsNull)
        {
            return;
        }

        string p = HubXml.Prefix(pr, _owner.WordprocessingNamespaceUri, "w", out string decl);
        pr.AppendXml(HubXml.Bytes("<" + p + ":" + child + decl + " " + p + ":id=\"-1\"/><" + p + ":" + child + " " + p + ":id=\"0\"/>"));
    }

    private string? Get(string name)
    {
        MarkupNode pr = Properties;
        if (pr.IsNull)
        {
            return null;
        }

        MarkupNode e = HubXml.FirstChildElement(pr, name);
        return e.IsNull ? null : e.GetAttribute(e.NamespaceUri, "val");
    }

    private void Set(string name, string? value)
    {
        MarkupNode pr = Properties;
        if (value is null)
        {
            if (!pr.IsNull)
            {
                HubXml.FirstChildElement(pr, name).Remove();
            }

            return;
        }

        if (!_parent.IsValid)
        {
            throw new InvalidOperationException("The document has no settings part to hold document-wide note properties.");
        }

        pr = EnsureProperties();
        MarkupNode existing = HubXml.FirstChildElement(pr, name);
        if (!existing.IsNull)
        {
            existing.SetAttribute(existing.NamespaceUri, "val", value, "w");
            return;
        }

        string p = HubXml.Prefix(pr, _owner.WordprocessingNamespaceUri, "w", out string decl);
        int at = Array.IndexOf(Sequence, name);
        HubXml.InsertInSequence(pr, "<" + p + ":" + name + decl + " " + p + ":val=\"" + HubXml.Escape(value) + "\"/>", Sequence[(at + 1)..]);
    }

    private MarkupNode EnsureProperties()
    {
        MarkupNode pr = Properties;
        if (!pr.IsNull)
        {
            return pr;
        }

        string p = HubXml.Prefix(_parent.Node, _owner.WordprocessingNamespaceUri, "w", out string decl);
        string[] after = (_isSettings, Kind) switch
        {
            (true, NoteKind.Footnote) => AfterFootnotePrInSettings,
            (true, NoteKind.Endnote) => AfterEndnotePrInSettings,
            (false, NoteKind.Footnote) => AfterFootnotePrInSection,
            _ => AfterEndnotePrInSection,
        };
        HubXml.InsertInSequence(_parent.Node, "<" + p + ":" + PropertiesName + decl + "/>", after);
        return Properties;
    }
}
