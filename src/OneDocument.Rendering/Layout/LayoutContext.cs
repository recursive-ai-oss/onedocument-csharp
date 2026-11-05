using OneFont;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

// The document settings that change how text is laid out. ECMA-376 Part 1 §17.15.3 (compat) and §17.15.1.25 (defaultTabStop);
// compatibilityMode is [MS-DOCX] §2.3.2.3 (w:compatSetting). The table of what each mode changes is in docs/layout.md.
internal sealed class CompatSettings
{
    public int Mode = 12;
    public double DefaultTabStop = 36; // points; ECMA-376 default is 720 twips
    public bool DoNotExpandShiftReturn;
    public bool EvenAndOddHeaders;
    public bool MirrorMargins; // §17.15.1.57
    public bool GutterAtTop; // §17.15.1.46
    public bool DoNotUseHtmlParagraphAutoSpacing;

    public static CompatSettings Read(Document doc)
    {
        var c = new CompatSettings();
        (MarkupDocument? settings, _) = Xml.LoadRelated(doc, "settings");
        if (settings is null)
        {
            return c;
        }

        var root = new MarkupRef(settings.Root);
        if (Xml.Child(root, "defaultTabStop") is { } dts && Xml.Long(Xml.Attr(dts, "val")) is { } v && v > 0)
        {
            c.DefaultTabStop = v / 20.0;
        }

        c.EvenAndOddHeaders = Xml.Child(root, "evenAndOddHeaders") is { } eo && Xml.OnOff(eo);
        c.MirrorMargins = Xml.Child(root, "mirrorMargins") is { } mm && Xml.OnOff(mm);
        c.GutterAtTop = Xml.Child(root, "gutterAtTop") is { } gt && Xml.OnOff(gt);
        if (Xml.Child(root, "compat") is { } compat)
        {
            // Word 2003-era documents carry no compatSetting; those lay out as mode 11.
            c.Mode = 11;
            foreach (MarkupRef e in Xml.Elements(compat))
            {
                if (Xml.Eq(e.LocalName, "compatSetting"))
                {
                    if (Xml.Attr(e, "name") == "compatibilityMode" && Xml.Long(Xml.Attr(e, "val")) is { } m)
                    {
                        c.Mode = (int)Math.Clamp(m, 11, 15);
                    }
                }
                else if (Xml.Eq(e.LocalName, "doNotExpandShiftReturn"))
                {
                    c.DoNotExpandShiftReturn = Xml.OnOff(e);
                }
                else if (Xml.Eq(e.LocalName, "doNotUseHTMLParagraphAutoSpacing"))
                {
                    c.DoNotUseHtmlParagraphAutoSpacing = Xml.OnOff(e);
                }
            }
        }

        return c;
    }
}

// State shared by every paragraph of one layout run.
internal sealed class LayoutContext
{
    private readonly Dictionary<(string, bool, bool), FontMatch?> _fontCache = [];
    private readonly HashSet<(string, string)> _noted = [];
    private readonly HashSet<string> _noteOnce = [];
    private readonly Dictionary<Paragraph, ListLabel> _labels;

    public LayoutContext(Document doc, IFontProvider fonts, CompatSettings compat, IReadOnlyDictionary<string, DocumentFontInfo> fontTable)
    {
        Doc = doc;
        Fonts = fonts;
        Compat = compat;
        FontTable = fontTable;
        _labels = [];
        foreach (ListLabel l in doc.Numbering.LabelBody())
        {
            _labels[l.Paragraph] = l;
        }

        Relationships = doc.Package.GetRelationships(doc.MainPartName).ToDictionary(static r => r.Id, StringComparer.Ordinal);
        Drawings = DrawingSources.Create(doc, Relationships);
        Drawings.Layout = this;
    }

    public DrawingSources Drawings { get; }

    public Document Doc { get; }

    public IFontProvider Fonts { get; }

    public CompatSettings Compat { get; }

    public IReadOnlyDictionary<string, DocumentFontInfo> FontTable { get; }

    public IReadOnlyDictionary<string, RecursiveAI.OneDocument.Packaging.Relationship> Relationships { get; }

    public List<FontSubstitutionNote> FontNotes { get; } = [];

    public List<LayoutNote> Notes { get; } = [];

    public ShapedRun Scratch { get; } = new();

    /// <summary>The page the header or footer being formatted is for; null while the body is formatted (fields keep their cached results).</summary>
    public PageFieldState? PageFields { get; set; }

    /// <summary>Replacement text for runs of page-number fields in the story being formatted, keyed by run.</summary>
    public Dictionary<Run, string>? RunText { get; set; }

    /// <summary>Set when a field needed the page totals (NUMPAGES, SECTIONPAGES), so a second pass with the first pass's counts is worth running.</summary>
    public bool UsesPageTotals { get; set; }

    public ListLabel? LabelOf(Paragraph p) => _labels.GetValueOrDefault(p);

    public FontMatch? ResolveFont(string family, bool bold, bool italic)
    {
        if (_fontCache.TryGetValue((family, bold, italic), out FontMatch? cached))
        {
            return cached;
        }

        FontRequest request = FontTable.TryGetValue(family, out DocumentFontInfo? info)
            ? new FontRequest(family, bold, italic, info.FamilyClass, info.Panose, info.Charset)
            : new FontRequest(family, bold, italic);
        FontMatch? match = Fonts.Resolve(request);
        _fontCache[(family, bold, italic)] = match;
        if (match is null)
        {
            NoteOnce("layout.font-missing:" + family, "layout.font-missing", $"No font is available for \"{family}\"; its text is not laid out.", null);
        }
        else if (!match.IsExact && _noted.Add((match.RequestedFamily, match.ActualFamily)))
        {
            FontNotes.Add(new FontSubstitutionNote(match.RequestedFamily, match.ActualFamily, match.Kind));
        }

        return match;
    }

    public void NoteOnce(string key, string code, string message, BlockElement? source)
    {
        if (_noteOnce.Add(key))
        {
            Notes.Add(new LayoutNote(code, message, source));
        }
    }
}
