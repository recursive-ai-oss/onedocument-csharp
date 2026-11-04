using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Schema;
using W = RecursiveAI.OneOffice.Schema.Wordprocessing;

namespace RecursiveAI.OneOffice.Documents;

/// <summary>Where a section starts relative to the one before it (<c>w:type</c>, ECMA-376 Part 1 §17.6.22).</summary>
public enum SectionBreakType
{
    /// <summary>The section starts on a new page. This is also the default when <c>w:type</c> is absent.</summary>
    NextPage = 0,

    /// <summary>The section starts on the same page.</summary>
    Continuous = 1,

    /// <summary>The section starts on the next even-numbered page.</summary>
    EvenPage = 2,

    /// <summary>The section starts on the next odd-numbered page.</summary>
    OddPage = 3,

    /// <summary>The section starts in the next column.</summary>
    NextColumn = 4,
}

/// <summary>Page orientation (<c>w:orient</c>, §17.6.13).</summary>
public enum PageOrientation
{
    /// <summary>Portrait; the default.</summary>
    Portrait = 0,

    /// <summary>Landscape.</summary>
    Landscape = 1,
}

/// <summary>The page size in twentieths of a point (§17.6.13).</summary>
/// <param name="Width">The width; 12240 (8.5 in) when the section states none.</param>
/// <param name="Height">The height; 15840 (11 in) when the section states none.</param>
/// <param name="Orientation">The orientation as written. The width and height are stored as printed on the page; a landscape section has the larger side as width.</param>
/// <param name="PaperCode">The printer paper code (<c>w:code</c>), or null.</param>
public readonly record struct PageSize(long Width, long Height, PageOrientation Orientation, long? PaperCode);

/// <summary>Page margins in twentieths of a point (§17.6.11).</summary>
/// <param name="Top">Top margin; 1440 when absent.</param>
/// <param name="Right">Right margin; 1440 when absent.</param>
/// <param name="Bottom">Bottom margin; 1440 when absent.</param>
/// <param name="Left">Left margin; 1440 when absent.</param>
/// <param name="Header">Distance from the page top to the header; 720 when absent.</param>
/// <param name="Footer">Distance from the page bottom to the footer; 720 when absent.</param>
/// <param name="Gutter">Extra binding margin; 0 when absent.</param>
public readonly record struct PageMargins(long Top, long Right, long Bottom, long Left, long Header, long Footer, long Gutter);

/// <summary>One explicit text column (§17.6.3).</summary>
/// <param name="Width">The width in twentieths of a point, or null when not stated.</param>
/// <param name="Space">The space after the column, or null when not stated.</param>
public readonly record struct ColumnDefinition(long? Width, long? Space);

/// <summary>The text columns of a section (<c>w:cols</c>, §17.6.4).</summary>
/// <param name="Count">The number of columns: the explicit column list when it is non-empty and unequal, else <c>w:num</c>, else 1.</param>
/// <param name="EqualWidth">True when the columns share the width equally (default true).</param>
/// <param name="Space">The space between equal columns; 720 when absent.</param>
/// <param name="Separator">True when a line is drawn between columns.</param>
/// <param name="Columns">The explicit columns of an unequal layout; empty for equal columns.</param>
public sealed record SectionColumns(int Count, bool EqualWidth, long Space, bool Separator, IReadOnlyList<ColumnDefinition> Columns);

/// <summary>Page numbering of a section (<c>w:pgNumType</c>, §17.6.12).</summary>
/// <param name="Start">The number the section's first page gets, or null to continue the previous section.</param>
/// <param name="Format">The number format as written (<c>decimal</c>, <c>upperRoman</c>, …), or null for decimal.</param>
/// <param name="ChapterStyle">The heading level that supplies chapter numbers (<c>w:chapStyle</c>), or null.</param>
/// <param name="ChapterSeparator">The chapter separator (<c>hyphen</c>, <c>period</c>, …), or null.</param>
public readonly record struct PageNumbering(long? Start, string? Format, long? ChapterStyle, string? ChapterSeparator);

/// <summary>Line numbering of a section (<c>w:lnNumType</c>, §17.6.8).</summary>
/// <param name="CountBy">Show a number on every n-th line; 1 when absent.</param>
/// <param name="Start">The first number; 1 when absent.</param>
/// <param name="Distance">The distance from the text in twentieths of a point; 0 means automatic.</param>
/// <param name="Restart"><c>newPage</c> (default), <c>newSection</c> or <c>continuous</c>.</param>
public readonly record struct LineNumbering(long CountBy, long Start, long Distance, string Restart);

/// <summary>The document grid of a section (<c>w:docGrid</c>, §17.6.5), used for East Asian layout.</summary>
/// <param name="Type"><c>default</c>, <c>lines</c>, <c>linesAndChars</c> or <c>snapToChars</c>.</param>
/// <param name="LinePitch">The line pitch in twentieths of a point, or null.</param>
/// <param name="CharacterSpace">The character-space adjustment, or null.</param>
public readonly record struct DocumentGrid(string Type, long? LinePitch, long? CharacterSpace);

/// <summary>
/// A section of the document: the run of blocks that ends at a section break and shares its page setup (ECMA-376 Part 1 §17.6). A
/// section is a snapshot of where it sits: ask <see cref="Document.Sections"/> again after adding or removing section breaks. Reading goes
/// through the <c>w:sectPr</c> element on demand, so an edit is visible at once.
/// </summary>
/// <remarks>
/// The properties of the last section sit in the body; every other section's sit in the last paragraph of that section. A body without
/// <c>w:sectPr</c> has one section whose properties are all defaults (<see cref="PropertiesElement"/> is invalid until an edit adds one).
/// </remarks>
public sealed partial class Section
{
    private readonly int _firstBlock;
    private readonly int _lastBlock;
    private MarkupRef _properties;

    internal Section(Document owner, int index, MarkupRef properties, int firstBlock, int lastBlock, bool isLast)
    {
        Owner = owner;
        Index = index;
        _properties = properties;
        _firstBlock = firstBlock;
        _lastBlock = lastBlock;
        IsLast = isLast;
    }

    /// <summary>Gets the document the section belongs to.</summary>
    public Document Owner { get; }

    /// <summary>Gets the zero-based position of the section.</summary>
    public int Index { get; }

    /// <summary>Gets a value indicating whether this is the last section, whose properties close the body.</summary>
    public bool IsLast { get; }

    /// <summary>Gets the <c>w:sectPr</c> element; invalid when the last section has none yet.</summary>
    public MarkupRef PropertiesElement => _properties;

    /// <summary>Gets the typed view of the section properties, or null when the element is absent.</summary>
    public W.SectionProperties? View => _properties.IsValid ? W.SectionProperties.Create(_properties) : null;

    /// <summary>Enumerates the top-level blocks of the section, including the paragraph that carries its <c>w:sectPr</c>.</summary>
    /// <returns>The blocks.</returns>
    public IEnumerable<BlockElement> Blocks()
    {
        int i = 0;
        foreach (BlockElement b in Owner.Body.Blocks())
        {
            if (i > _lastBlock)
            {
                yield break;
            }

            if (i >= _firstBlock)
            {
                yield return b;
            }

            i++;
        }
    }

    /// <summary>Gets how the section starts; <see cref="SectionBreakType.NextPage"/> when <c>w:type</c> is absent.</summary>
    public SectionBreakType BreakType => Wml.Attr(Wml.Child(_properties, "type"u8) ?? default, "val") switch
    {
        "continuous" => SectionBreakType.Continuous,
        "evenPage" => SectionBreakType.EvenPage,
        "oddPage" => SectionBreakType.OddPage,
        "nextColumn" => SectionBreakType.NextColumn,
        _ => SectionBreakType.NextPage,
    };

    /// <summary>Gets the page size. An absent or unusable <c>w:pgSz</c> reads as Letter portrait; an unusable one is reported (<c>docx.section-page-size-invalid</c>).</summary>
    public PageSize PageSize
    {
        get
        {
            MarkupRef? e = Wml.Child(_properties, "pgSz"u8);
            long w = 12240, h = 15840;
            PageOrientation orientation = PageOrientation.Portrait;
            long? code = null;
            if (e is { } pg)
            {
                long? pw = Wml.Long(Wml.Attr(pg, "w")), ph = Wml.Long(Wml.Attr(pg, "h"));
                if (pw is > 0 && ph is > 0)
                {
                    w = pw.Value;
                    h = ph.Value;
                }
                else
                {
                    Owner.ReportDefect("docx.section-page-size-invalid", DefectKind.Value, DefectSeverity.Repaired,
                        "The page size is missing a positive width or height; Letter portrait is used.", pg);
                }

                orientation = Wml.Attr(pg, "orient") == "landscape" ? PageOrientation.Landscape : PageOrientation.Portrait;
                code = Wml.Long(Wml.Attr(pg, "code"));
            }

            return new PageSize(w, h, orientation, code);
        }
    }

    /// <summary>Gets the page margins with the defaults for absent attributes. Negative values are kept: they mean the body is placed exactly (§17.6.11).</summary>
    public PageMargins Margins
    {
        get
        {
            MarkupRef? e = Wml.Child(_properties, "pgMar"u8);
            long Get(string name, long fallback) => e is { } m ? Wml.Long(Wml.Attr(m, name)) ?? fallback : fallback;
            return new PageMargins(Get("top", 1440), Get("right", 1440), Get("bottom", 1440), Get("left", 1440), Get("header", 720), Get("footer", 720), Get("gutter", 0));
        }
    }

    /// <summary>Gets the text columns; one column when <c>w:cols</c> is absent.</summary>
    public SectionColumns Columns
    {
        get
        {
            if (Wml.Child(_properties, "cols"u8) is not { } cols)
            {
                return new SectionColumns(1, true, 720, false, []);
            }

            bool equal = Wml.OnOff(Wml.Attr(cols, "equalWidth")) ?? true;
            long num = Wml.Long(Wml.Attr(cols, "num")) ?? 1;
            var list = new List<ColumnDefinition>();
            foreach (MarkupRef c in Wml.Elements(cols))
            {
                if (Wml.Is(c, "col"u8))
                {
                    list.Add(new ColumnDefinition(Wml.Long(Wml.Attr(c, "w")), Wml.Long(Wml.Attr(c, "space"))));
                }
            }

            int count = (int)Math.Clamp(num, 1, 45);
            if (!equal && list.Count > 0)
            {
                // §17.6.4: the explicit column list defines the layout when the columns are not equal.
                if (list.Count != count && Wml.Attr(cols, "num") is not null)
                {
                    Owner.ReportDefect("docx.section-columns-mismatch", DefectKind.Value, DefectSeverity.Repaired,
                        "w:cols says " + num + " columns but lists " + list.Count + "; the list is used.", cols);
                }

                count = list.Count;
            }
            else
            {
                list.Clear();
            }

            return new SectionColumns(count, equal, Wml.Long(Wml.Attr(cols, "space")) ?? 720, Wml.OnOff(Wml.Attr(cols, "sep")) ?? false, list);
        }
    }

    /// <summary>Gets the page numbering settings.</summary>
    public PageNumbering PageNumbering
    {
        get
        {
            MarkupRef? e = Wml.Child(_properties, "pgNumType"u8);
            return e is { } p
                ? new PageNumbering(Wml.Long(Wml.Attr(p, "start")), Wml.Attr(p, "fmt"), Wml.Long(Wml.Attr(p, "chapStyle")), Wml.Attr(p, "chapSep"))
                : default;
        }
    }

    /// <summary>Gets the line numbering, or null when the section has none.</summary>
    public LineNumbering? LineNumbering
    {
        get
        {
            if (Wml.Child(_properties, "lnNumType"u8) is not { } e)
            {
                return null;
            }

            return new LineNumbering(Wml.Long(Wml.Attr(e, "countBy")) ?? 1, Wml.Long(Wml.Attr(e, "start")) ?? 1, Wml.Long(Wml.Attr(e, "distance")) ?? 0, Wml.Attr(e, "restart") ?? "newPage");
        }
    }

    /// <summary>Gets the document grid, or null when the section has none.</summary>
    public DocumentGrid? DocumentGrid =>
        Wml.Child(_properties, "docGrid"u8) is { } e
            ? new DocumentGrid(Wml.Attr(e, "type") ?? "default", Wml.Long(Wml.Attr(e, "linePitch")), Wml.Long(Wml.Attr(e, "charSpace")))
            : null;

    /// <summary>Gets a value indicating whether the first page of the section has its own header and footer (<c>w:titlePg</c>).</summary>
    public bool TitlePage => Wml.Child(_properties, "titlePg"u8) is { } e && Wml.OnOffElement(e);

    /// <summary>Gets a value indicating whether the section is right to left (<c>w:bidi</c>).</summary>
    public bool IsRightToLeft => Wml.Child(_properties, "bidi"u8) is { } e && Wml.OnOffElement(e);

    /// <summary>Gets the vertical alignment of the page text (<c>w:vAlign</c>: <c>top</c>, <c>center</c>, <c>both</c>, <c>bottom</c>), or null.</summary>
    public string? VerticalAlignment => Wml.Attr(Wml.Child(_properties, "vAlign"u8) ?? default, "val");

    /// <summary>Gets the text flow direction (<c>w:textDirection</c>) as written, or null.</summary>
    public string? TextDirection => Wml.Attr(Wml.Child(_properties, "textDirection"u8) ?? default, "val");

    /// <summary>Gets a value indicating whether endnotes are suppressed in this section (<c>w:noEndnote</c>).</summary>
    public bool SuppressEndnotes => Wml.Child(_properties, "noEndnote"u8) is { } e && Wml.OnOffElement(e);

    // --- edits ---

    private W.SectionProperties Ensure()
    {
        if (!_properties.IsValid)
        {
            // Only the last section can lack its properties (the body has none): the element goes at the end of the body.
            Owner.Body.Container.Node.AppendXml(WmlEdit.Utf8("<" + WmlEdit.Qualified(Owner.Body.Container, "sectPr") + "/>"));
            _properties = Owner.Body.SectionProperties ?? throw new InvalidOperationException("The section properties could not be added.");
        }

        return W.SectionProperties.Create(_properties);
    }

    private static string N(long v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Sets the page size in twentieths of a point and the orientation, keeping other attributes of <c>w:pgSz</c> (such as the paper code).</summary>
    /// <param name="width">The width, above 0.</param>
    /// <param name="height">The height, above 0.</param>
    /// <param name="orientation">The orientation to write. The width and height are written as given; swap them yourself when rotating a page.</param>
    /// <exception cref="ArgumentOutOfRangeException">The width or height is not above 0.</exception>
    public void SetPageSize(long width, long height, PageOrientation orientation)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        MarkupRef e = Ensure().GetOrAddPageSize().Markup;
        WmlEdit.SetAttr(e, "w", N(width));
        WmlEdit.SetAttr(e, "h", N(height));
        if (orientation == PageOrientation.Landscape)
        {
            WmlEdit.SetAttr(e, "orient", "landscape");
        }
        else
        {
            WmlEdit.RemoveAttr(e, "orient");
        }
    }

    /// <summary>Sets the page margins; all seven attributes of <c>w:pgMar</c> are written.</summary>
    /// <param name="margins">The margins in twentieths of a point.</param>
    public void SetMargins(PageMargins margins)
    {
        MarkupRef e = Ensure().GetOrAddPageMargins().Markup;
        WmlEdit.SetAttr(e, "top", N(margins.Top));
        WmlEdit.SetAttr(e, "right", N(margins.Right));
        WmlEdit.SetAttr(e, "bottom", N(margins.Bottom));
        WmlEdit.SetAttr(e, "left", N(margins.Left));
        WmlEdit.SetAttr(e, "header", N(margins.Header));
        WmlEdit.SetAttr(e, "footer", N(margins.Footer));
        WmlEdit.SetAttr(e, "gutter", N(margins.Gutter));
    }

    /// <summary>Sets how the section starts. <see cref="SectionBreakType.NextPage"/> removes <c>w:type</c>, which is its default.</summary>
    /// <param name="type">The break type.</param>
    public void SetBreakType(SectionBreakType type)
    {
        W.SectionProperties sp = Ensure();
        if (type == SectionBreakType.NextPage)
        {
            sp.RemoveType();
            return;
        }

        WmlEdit.SetAttr(sp.GetOrAddType().Markup, "val", type switch
        {
            SectionBreakType.Continuous => "continuous",
            SectionBreakType.EvenPage => "evenPage",
            SectionBreakType.OddPage => "oddPage",
            _ => "nextColumn",
        });
    }

    /// <summary>Switches the different-first-page header and footer on or off (<c>w:titlePg</c>).</summary>
    /// <param name="on">True to write the element, false to remove it.</param>
    public void SetTitlePage(bool on)
    {
        W.SectionProperties sp = Ensure();
        if (on)
        {
            sp.GetOrAddTitlePg();
        }
        else
        {
            sp.RemoveTitlePg();
        }
    }

    /// <summary>Sets the page numbering; null members are removed from <c>w:pgNumType</c>, and the element goes when nothing is left.</summary>
    /// <param name="numbering">The settings.</param>
    public void SetPageNumbering(PageNumbering numbering)
    {
        W.SectionProperties sp = Ensure();
        if (numbering == default)
        {
            sp.RemovePgNumType();
            return;
        }

        MarkupRef e = sp.GetOrAddPgNumType().Markup;
        void Put(string name, string? value)
        {
            if (value is null)
            {
                WmlEdit.RemoveAttr(e, name);
            }
            else
            {
                WmlEdit.SetAttr(e, name, value);
            }
        }

        Put("start", numbering.Start is { } s ? N(s) : null);
        Put("fmt", numbering.Format);
        Put("chapStyle", numbering.ChapterStyle is { } c ? N(c) : null);
        Put("chapSep", numbering.ChapterSeparator);
    }

    /// <summary>Sets equal-width columns: the count and the space between them.</summary>
    /// <param name="count">The number of columns, from 1 to 45.</param>
    /// <param name="space">The space between columns in twentieths of a point.</param>
    /// <param name="separator">True to draw a line between the columns.</param>
    /// <exception cref="ArgumentOutOfRangeException">The count is outside 1..45.</exception>
    public void SetEqualColumns(int count, long space, bool separator)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 45);
        MarkupRef e = Ensure().GetOrAddColumns().Markup;
        foreach (MarkupRef c in WmlEdit.ElementList(e))
        {
            if (Wml.Is(c, "col"u8))
            {
                c.Node.Remove();
            }
        }

        WmlEdit.RemoveAttr(e, "equalWidth");
        WmlEdit.SetAttr(e, "num", N(count));
        WmlEdit.SetAttr(e, "space", N(space));
        if (separator)
        {
            WmlEdit.SetAttr(e, "sep", "1");
        }
        else
        {
            WmlEdit.RemoveAttr(e, "sep");
        }
    }
}
