using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Documents.Revisions;

/// <summary>
/// Writes tracked changes into the parts of a <see cref="RevisionSet"/>, as the author and date it was created with: the way a
/// comparison tool or a program records its edits as redlines.
/// </summary>
/// <remarks>
/// <para><b>Positions.</b> Text positions are character offsets (UTF-16 code units) into a paragraph's <em>current</em> text:
/// the text the paragraph shows with every tracked change accepted (text inside <c>w:del</c> and <c>w:moveFrom</c> does not
/// count; inserted text does). A tab or a line break counts as one character. Field instructions and drawings count as none.
/// Position 0 is before the first character and the paragraph's length is after the last.</para>
/// <para><b>Editing runs.</b> Runs are split at the edit position, keeping their properties; a run is never split inside
/// something that is not text. Only the paragraphs and elements an edit touches are changed; the rest of the part is
/// untouched byte for byte.</para>
/// <para><b>Ids and dates.</b> By default ids continue after the largest id in the set's parts; <see cref="RevisionIds"/> lets the
/// caller choose them instead (a start value or a callback), and an id already in use throws <see cref="RevisionIdConflictException"/>.
/// Either way the same edits on the same document write the same bytes. The date is the one given when the author was created
/// (omitted when null). The library never reads the clock.</para>
/// <para><b>Two levels.</b> The methods taking <see cref="MarkupNode"/> are the low-level path (property changes as XML fragments);
/// the overloads taking the document model's <c>Paragraph</c>, <c>Run</c>, <c>Table</c>, <c>TableRow</c> and <c>TableCell</c> take typed
/// property changes (<see cref="RunPropertiesDelta"/>, <see cref="ParagraphPropertiesDelta"/> and the table ones).</para>
/// <para><b>Exceptions.</b> A node that is not the expected element, a position out of range or a property fragment that is not
/// well-formed throws <see cref="ArgumentException"/> (or <see cref="ArgumentOutOfRangeException"/>) before the document is
/// changed. Not safe for concurrent use.</para>
/// </remarks>
public sealed partial class RevisionAuthor
{
    private readonly RevisionSet _set;
    private readonly RevisionIds _ids;

    internal RevisionAuthor(RevisionSet set, string name, DateTimeOffset? date, RevisionIds ids)
    {
        _ids = ids;
        ArgumentNullException.ThrowIfNull(name);
        WordXml.RequireXmlText(name, nameof(name));
        _set = set;
        Name = name;
        Date = date;
    }

    /// <summary>Gets the author name written on every change.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the date written on every following change; null leaves the date attribute out.</summary>
    public DateTimeOffset? Date { get; set; }

    /// <summary>Gets the prefix the WordprocessingML main namespace has at a node, for building property fragments (usually <c>w</c>).</summary>
    /// <param name="context">A node inside the part.</param>
    /// <returns>The prefix, without a colon.</returns>
    /// <exception cref="ArgumentException">The node is not in a WordprocessingML part.</exception>
    public static string Prefix(MarkupNode context) => WmlWriter.For(context).Name("")[..^1];

    // ---------------------------------------------------------------- text

    /// <summary>Inserts text as a tracked insertion at a position in a paragraph.</summary>
    /// <param name="paragraph">A <c>w:p</c> element.</param>
    /// <param name="offset">The position in the paragraph's current text, 0 to its length.</param>
    /// <param name="text">The text; <c>\t</c> and line feeds become tabs and line breaks. May be empty (nothing is written).</param>
    /// <remarks>The new run takes the properties of the run before the position (of the run after it at position 0). Inside another author's insertion the insertion is split around the new one.</remarks>
    public void InsertText(MarkupNode paragraph, int offset, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        WordXml.RequireXmlText(text, nameof(text));
        RequireParagraph(paragraph);
        CheckRange(paragraph, offset, 0);
        if (text.Length == 0)
        {
            return;
        }

        WmlWriter w = WmlWriter.For(paragraph);
        MarkupNode boundary = IsolateBoundary(paragraph, offset);
        string props = FormattingSource(paragraph, offset);
        string xml = w.Wrap("ins", Attributes(w), w.Wrap("r", string.Empty, props + RunContent(w, text)));
        PlaceAt(paragraph, boundary, xml);
        _set.Invalidate();
    }

    /// <summary>Marks a stretch of a paragraph's current text as deleted.</summary>
    /// <param name="paragraph">A <c>w:p</c> element.</param>
    /// <param name="offset">The first character to delete.</param>
    /// <param name="length">How many characters; zero does nothing.</param>
    /// <remarks>Text this author inserted and has not had accepted is removed outright, as Word does. Text in another author's insertion becomes a deletion inside that insertion.</remarks>
    public void DeleteText(MarkupNode paragraph, int offset, int length)
    {
        RequireParagraph(paragraph);
        CheckRange(paragraph, offset, length);
        if (length == 0)
        {
            return;
        }

        MarkRange(paragraph, offset, offset + length, "del", null, out _);
        _set.Invalidate();
    }

    /// <summary>Replaces a stretch of text: a tracked deletion followed by a tracked insertion of the new text.</summary>
    /// <param name="paragraph">A <c>w:p</c> element.</param>
    /// <param name="offset">The first character to replace.</param>
    /// <param name="length">How many characters (may be zero, which makes it an insertion).</param>
    /// <param name="text">The new text.</param>
    public void ReplaceText(MarkupNode paragraph, int offset, int length, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        WordXml.RequireXmlText(text, nameof(text));
        RequireParagraph(paragraph);
        CheckRange(paragraph, offset, length);
        if (length == 0)
        {
            InsertText(paragraph, offset, text);
            return;
        }

        string props = FormattingSource(paragraph, offset + 1);
        MarkRange(paragraph, offset, offset + length, "del", null, out MarkupNode lastWrapper);
        if (text.Length > 0)
        {
            WmlWriter w = WmlWriter.For(paragraph);
            string xml = w.Wrap("ins", Attributes(w), w.Wrap("r", string.Empty, props + RunContent(w, text)));
            if (!lastWrapper.IsNull)
            {
                lastWrapper.InsertXmlAfter(WordXml.Bytes(xml));
            }
            else
            {
                PlaceAt(paragraph, IsolateBoundary(paragraph, offset), xml);
            }
        }

        _set.Invalidate();
    }

    /// <summary>Moves a stretch of text to another place: <c>w:moveFrom</c> where it was, <c>w:moveTo</c> where it arrives, paired by name.</summary>
    /// <param name="paragraph">The paragraph the text is in.</param>
    /// <param name="offset">The first character to move.</param>
    /// <param name="length">How many characters (at least one).</param>
    /// <param name="destinationParagraph">The paragraph it goes into (may be the same one).</param>
    /// <param name="destinationOffset">The position in the destination paragraph's current text, measured before the move. Not inside the moved text.</param>
    /// <param name="moveName">The name that pairs the two ends (<c>w:name</c> on the range markers); null makes one from the id (<c>move7</c>). A name another move in the parts already uses is refused.</param>
    /// <exception cref="ArgumentException">The destination is inside the moved text, the name is in use, or the text spans elements that cannot be wrapped together.</exception>
    public void MoveText(MarkupNode paragraph, int offset, int length, MarkupNode destinationParagraph, int destinationOffset, string? moveName = null)
    {
        CheckMoveName(moveName);
        RequireParagraph(paragraph);
        RequireParagraph(destinationParagraph);
        CheckRange(paragraph, offset, length);
        if (length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Nothing to move.");
        }

        CheckRange(destinationParagraph, destinationOffset, 0);
        bool same = paragraph == destinationParagraph;
        if (same && destinationOffset >= offset && destinationOffset <= offset + length)
        {
            throw new ArgumentException("The destination is inside the moved text.", nameof(destinationOffset));
        }

        // Copy the moved runs first (split at the ends), as plain runs for the destination.
        IsolateBoundary(paragraph, offset + length);
        IsolateBoundary(paragraph, offset);
        List<MarkupNode> runs = RunsInRange(paragraph, offset, offset + length);
        var copy = new StringBuilder();
        foreach (MarkupNode r in runs)
        {
            copy.Append(WordXml.Portable(r));
        }

        WmlWriter w = WmlWriter.For(paragraph);
        int fromId = NextId();
        string name = moveName ?? "move" + fromId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        bool destinationFirst = same ? destinationOffset > offset : IsAfter(destinationParagraph, paragraph);
        if (destinationFirst)
        {
            InsertMoveTo(destinationParagraph, destinationOffset, copy.ToString(), name);
            MarkRange(paragraph, offset, offset + length, "moveFrom", (fromId, name), out _);
        }
        else
        {
            MarkRange(paragraph, offset, offset + length, "moveFrom", (fromId, name), out _);
            InsertMoveTo(destinationParagraph, destinationOffset, copy.ToString(), name);
        }

        _ = w;
        _set.Invalidate();
    }

    // ----------------------------------------------------------- paragraphs

    /// <summary>Inserts a new paragraph after <paramref name="paragraph"/> as a tracked change.</summary>
    /// <param name="paragraph">The paragraph to insert after.</param>
    /// <param name="text">The new paragraph's text (may be empty).</param>
    /// <remarks>
    /// Word records pressing Enter at the end of a paragraph by marking the existing paragraph's mark as inserted, while the new
    /// paragraph keeps the properties the old one had. This does the same, so rejecting joins the paragraphs back with the
    /// original properties intact, even at the end of a table cell or the body. A section break in the old paragraph moves to the new one.
    /// </remarks>
    public void InsertParagraph(MarkupNode paragraph, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        WordXml.RequireXmlText(text, nameof(text));
        RequireParagraph(paragraph);
        WmlWriter w = WmlWriter.For(paragraph);
        MarkupNode pPr = WordXml.Child(paragraph, "pPr");
        string newProps = pPr.IsNull ? string.Empty : CopyParagraphProperties(pPr, w);
        MarkupNode oldChange = pPr.IsNull ? default : WordXml.Child(pPr, "pPrChange");
        if (!oldChange.IsNull && newProps.Length > 0)
        {
            // Pressing Enter copies the paragraph's pending property change too, so that rejecting it restores the original
            // properties on the paragraph that rejecting the inserted mark leaves behind (ECMA-376 Part 1 §17.13.5.29).
            MarkupNode before = WordXml.Child(oldChange, "pPr");
            string old = before.IsNull ? w.Empty("pPr") : WordXml.Portable(before);
            string copy = w.Wrap("pPrChange", Attributes(w), old);
            newProps = newProps.Insert(newProps.Length - ("</" + w.Name("pPr") + ">").Length, copy);
        }

        if (!pPr.IsNull)
        {
            MarkupNode sect = WordXml.Child(pPr, "sectPr");
            if (!sect.IsNull)
            {
                sect.Remove();
            }
        }

        MarkParagraphMark(paragraph, "ins");
        string content = text.Length == 0 ? string.Empty : w.Wrap("ins", Attributes(w), w.Wrap("r", string.Empty, RunContent(w, text)));
        paragraph.InsertXmlAfter(WordXml.Bytes(w.Wrap("p", string.Empty, newProps + content)));
        _set.Invalidate();
    }

    /// <summary>Marks a whole paragraph, its text and its paragraph mark, as deleted.</summary>
    /// <param name="paragraph">A <c>w:p</c> element.</param>
    public void DeleteParagraph(MarkupNode paragraph)
    {
        RequireParagraph(paragraph);
        int total = TotalLength(paragraph);
        MarkRange(paragraph, 0, total, "del", null, out _, includeTrailingZeroWidth: true);
        MarkParagraphMark(paragraph, "del");
        _set.Invalidate();
    }

    /// <summary>Moves a whole paragraph to after another one: the paragraph becomes a <c>w:moveFrom</c> and a copy a <c>w:moveTo</c>.</summary>
    /// <param name="paragraph">The paragraph to move.</param>
    /// <param name="after">The paragraph it goes after; not the same paragraph.</param>
    /// <param name="moveName">The name that pairs the two ends; null makes one from the id. A name another move in the parts already uses is refused.</param>
    public void MoveParagraph(MarkupNode paragraph, MarkupNode after, string? moveName = null)
    {
        CheckMoveName(moveName);
        RequireParagraph(paragraph);
        RequireParagraph(after);
        if (paragraph == after)
        {
            throw new ArgumentException("A paragraph cannot move after itself.", nameof(after));
        }

        WmlWriter w = WmlWriter.For(paragraph);
        int total = TotalLength(paragraph);
        IsolateBoundary(paragraph, total);
        List<MarkupNode> runs = RunsInRange(paragraph, 0, total, includeTrailingZeroWidth: true);
        var copy = new StringBuilder();
        foreach (MarkupNode r in runs)
        {
            copy.Append(WordXml.Portable(r));
        }

        MarkupNode pPr = WordXml.Child(paragraph, "pPr");
        string props = pPr.IsNull ? string.Empty : CopyParagraphProperties(pPr, w, withoutSection: true);
        int toId = NextId();
        string name = moveName ?? "move" + toId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int endId = NextId();
        string moved = Range(w, "moveTo", toId, name, copy.ToString(), endId);
        var newParagraph = new StringBuilder();
        newParagraph.Append(w.Wrap("p", string.Empty, props + moved));
        // The new paragraph's own mark is a move destination, so rejecting the move removes it again.
        after.InsertXmlAfter(WordXml.Bytes(newParagraph.ToString()));
        MarkupNode created = after.NextSibling;
        while (!created.IsNull && created.Kind != MarkupNodeKind.Element)
        {
            created = created.NextSibling;
        }

        MarkParagraphMark(created, "moveTo");
        MarkRange(paragraph, 0, total, "moveFrom", (NextId(), name), out _, includeTrailingZeroWidth: true);
        MarkParagraphMark(paragraph, "moveFrom");
        _set.Invalidate();
    }

    // ----------------------------------------------------------- formatting

    /// <summary>Changes the formatting of a stretch of text as a tracked change, merging the given run properties over the existing ones.</summary>
    /// <param name="paragraph">A <c>w:p</c> element.</param>
    /// <param name="offset">The first character.</param>
    /// <param name="length">How many characters; zero does nothing.</param>
    /// <param name="runPropertiesXml">The property elements to set, such as <c>&lt;w:b/&gt;&lt;w:color w:val="FF0000"/&gt;</c> (use <see cref="Prefix"/> if the part's prefix is not <c>w</c>). An element replaces the one of the same name; others stay.</param>
    public void FormatText(MarkupNode paragraph, int offset, int length, string runPropertiesXml)
    {
        ArgumentNullException.ThrowIfNull(runPropertiesXml);
        RequireParagraph(paragraph);
        CheckRange(paragraph, offset, length);
        if (length == 0)
        {
            return;
        }

        WmlWriter w = WmlWriter.For(paragraph);
        HashSet<string> names = ElementNames(runPropertiesXml, w);
        FormatCore(paragraph, offset, length, run =>
        {
            MarkupNode rPr = WordXml.Child(run, "rPr");
            var keep = new StringBuilder();
            if (!rPr.IsNull)
            {
                foreach (MarkupNode c in rPr.ChildElements)
                {
                    if (!WordXml.InMain(c) || (!names.Contains(c.LocalNameString) && !WordXml.IsAny(c, "rPrChange")))
                    {
                        keep.Append(WordXml.Portable(c));
                    }
                }
            }

            return keep + runPropertiesXml;
        });
    }

    /// <summary>Splits the runs at the edges of the range and records a formatting change on each run in it, with the new properties computed per run.</summary>
    private void FormatCore(MarkupNode paragraph, int offset, int length, Func<MarkupNode, string> newProperties)
    {
        IsolateBoundary(paragraph, offset + length);
        IsolateBoundary(paragraph, offset);
        foreach (MarkupNode run in RunsInRange(paragraph, offset, offset + length))
        {
            ChangeRunProperties(run, newProperties(run));
        }

        _set.Invalidate();
    }

    /// <summary>Replaces the properties of a run and records the old ones as <c>w:rPrChange</c>.</summary>
    /// <param name="run">A <c>w:r</c> element.</param>
    /// <param name="propertiesXml">The complete new property elements (may be empty).</param>
    /// <remarks>If the run already has a pending change, its record of the original properties is kept.</remarks>
    public void ChangeRunProperties(MarkupNode run, string propertiesXml)
    {
        if (!WordXml.Is(run, "r"))
        {
            throw new ArgumentException("The node is not a w:r element.", nameof(run));
        }

        TrackProperties(run, "rPr", "rPrChange", propertiesXml, ["ins", "del", "moveFrom", "moveTo"], [], atStart: true);
        _set.Invalidate();
    }

    /// <summary>Replaces the paragraph's properties (everything except the paragraph mark's run properties and a section break) and records the old ones as <c>w:pPrChange</c>.</summary>
    /// <param name="paragraph">A <c>w:p</c> element.</param>
    /// <param name="propertiesXml">The complete new property elements, such as <c>&lt;w:jc w:val="center"/&gt;</c>.</param>
    public void ChangeParagraphProperties(MarkupNode paragraph, string propertiesXml)
    {
        RequireParagraph(paragraph);
        TrackProperties(paragraph, "pPr", "pPrChange", propertiesXml, [], ["rPr", "sectPr"], atStart: true);
        _set.Invalidate();
    }

    /// <summary>Replaces a section's properties and records the old ones as <c>w:sectPrChange</c>. Header and footer references stay.</summary>
    /// <param name="sectionProperties">A <c>w:sectPr</c> element.</param>
    /// <param name="propertiesXml">The complete new property elements (page size, margins and so on).</param>
    public void ChangeSectionProperties(MarkupNode sectionProperties, string propertiesXml)
    {
        if (!WordXml.Is(sectionProperties, "sectPr"))
        {
            throw new ArgumentException("The node is not a w:sectPr element.", nameof(sectionProperties));
        }

        string[] refs = ["headerReference", "footerReference"];
        MarkupNode container = sectionProperties;
        ApplyChange(container, "sectPr", "sectPrChange", propertiesXml, refs, []);
        _set.Invalidate();
    }

    /// <summary>Replaces a table's properties and records the old ones as <c>w:tblPrChange</c>.</summary>
    /// <param name="table">A <c>w:tbl</c> element.</param>
    /// <param name="propertiesXml">The complete new property elements.</param>
    public void ChangeTableProperties(MarkupNode table, string propertiesXml)
    {
        if (!WordXml.Is(table, "tbl"))
        {
            throw new ArgumentException("The node is not a w:tbl element.", nameof(table));
        }

        TrackProperties(table, "tblPr", "tblPrChange", propertiesXml, [], [], atStart: true);
        _set.Invalidate();
    }

    /// <summary>Replaces a table's column grid and records the old one as <c>w:tblGridChange</c>.</summary>
    /// <param name="table">A <c>w:tbl</c> element.</param>
    /// <param name="gridColumnsXml">The new <c>w:gridCol</c> elements.</param>
    public void ChangeTableGrid(MarkupNode table, string gridColumnsXml)
    {
        if (!WordXml.Is(table, "tbl"))
        {
            throw new ArgumentException("The node is not a w:tbl element.", nameof(table));
        }

        MarkupNode grid = WordXml.Child(table, "tblGrid");
        if (grid.IsNull)
        {
            throw new ArgumentException("The table has no w:tblGrid.", nameof(table));
        }

        ApplyChange(grid, "tblGrid", "tblGridChange", gridColumnsXml, [], []);
        _set.Invalidate();
    }

    /// <summary>Replaces a table row's properties and records the old ones as <c>w:trPrChange</c>.</summary>
    /// <param name="row">A <c>w:tr</c> element.</param>
    /// <param name="propertiesXml">The complete new property elements.</param>
    public void ChangeTableRowProperties(MarkupNode row, string propertiesXml)
    {
        if (!WordXml.Is(row, "tr"))
        {
            throw new ArgumentException("The node is not a w:tr element.", nameof(row));
        }

        TrackProperties(row, "trPr", "trPrChange", propertiesXml, [], ["ins", "del"], atStart: false, afterTblPrEx: true);
        _set.Invalidate();
    }

    /// <summary>Replaces a table cell's properties and records the old ones as <c>w:tcPrChange</c>.</summary>
    /// <param name="cell">A <c>w:tc</c> element.</param>
    /// <param name="propertiesXml">The complete new property elements.</param>
    public void ChangeTableCellProperties(MarkupNode cell, string propertiesXml)
    {
        if (!WordXml.Is(cell, "tc"))
        {
            throw new ArgumentException("The node is not a w:tc element.", nameof(cell));
        }

        TrackProperties(cell, "tcPr", "tcPrChange", propertiesXml, [], ["cellIns", "cellDel", "cellMerge"], atStart: true);
        _set.Invalidate();
    }

    // ---------------------------------------------------------------- tables

    /// <summary>Marks a table row, which the caller has just added, as an inserted row: the row property, and its paragraphs' text and marks as inserted.</summary>
    /// <param name="row">A <c>w:tr</c> element.</param>
    public void MarkRowInserted(MarkupNode row)
    {
        MarkRow(row, "ins");
        foreach (MarkupNode p in ParagraphsIn(row))
        {
            MarkParagraphInserted(p);
        }

        _set.Invalidate();
    }

    /// <summary>Marks a table row as deleted: the row property, and its paragraphs' text and marks as deleted.</summary>
    /// <param name="row">A <c>w:tr</c> element.</param>
    public void MarkRowDeleted(MarkupNode row)
    {
        MarkRow(row, "del");
        foreach (MarkupNode p in ParagraphsIn(row))
        {
            DeleteParagraph(p);
        }

        _set.Invalidate();
    }

    /// <summary>Marks a table cell, which the caller has just added, as an inserted cell, with its text as inserted.</summary>
    /// <param name="cell">A <c>w:tc</c> element.</param>
    public void MarkCellInserted(MarkupNode cell)
    {
        MarkCell(cell, "cellIns", string.Empty);
        foreach (MarkupNode p in ParagraphsIn(cell))
        {
            MarkParagraphInserted(p);
        }

        _set.Invalidate();
    }

    /// <summary>Marks a table cell as deleted, with its text as deleted.</summary>
    /// <param name="cell">A <c>w:tc</c> element.</param>
    public void MarkCellDeleted(MarkupNode cell)
    {
        MarkCell(cell, "cellDel", string.Empty);
        foreach (MarkupNode p in ParagraphsIn(cell))
        {
            DeleteParagraph(p);
        }

        _set.Invalidate();
    }

    /// <summary>Records a change in how a cell is merged vertically, and sets the cell's merge to the new state.</summary>
    /// <param name="cell">A <c>w:tc</c> element.</param>
    /// <param name="mergeNow">The new state: <c>restart</c> (first cell of a merged range), <c>continue</c> (a cell merged into the one above) or null (not merged).</param>
    /// <param name="mergeBefore">The state before: the same three values.</param>
    public void MergeCells(MarkupNode cell, string? mergeNow, string? mergeBefore)
    {
        if (!WordXml.Is(cell, "tc"))
        {
            throw new ArgumentException("The node is not a w:tc element.", nameof(cell));
        }

        string? now = MergeCode(mergeNow, nameof(mergeNow));
        string? before = MergeCode(mergeBefore, nameof(mergeBefore));
        WmlWriter w = WmlWriter.For(cell);
        string attrs = Attributes(w) + (now is null ? string.Empty : w.Attr("vMerge", now)) + (before is null ? string.Empty : w.Attr("vMergeOrig", before));
        MarkCell(cell, "cellMerge", attrs);
        MarkupNode tcPr = WordXml.Child(cell, "tcPr");
        MarkupNode existing = WordXml.Child(tcPr, "vMerge");
        string? fragment = mergeNow switch
        {
            "restart" => w.Empty("vMerge", w.Attr("val", "restart")),
            "continue" => w.Empty("vMerge"),
            _ => null,
        };
        if (!existing.IsNull)
        {
            if (fragment is null)
            {
                existing.Remove();
            }
            else
            {
                existing.ReplaceWithXml(WordXml.Bytes(fragment));
            }
        }
        else if (fragment is not null)
        {
            MarkupNode anchor = WordXml.Child(tcPr, "cellMerge");
            foreach (MarkupNode c in tcPr.ChildElements)
            {
                if (WordXml.IsAny(c, "tcBorders", "shd", "noWrap", "tcMar", "textDirection", "tcFitText", "vAlign", "hideMark", "headers", "cellIns", "cellDel", "cellMerge", "tcPrChange"))
                {
                    anchor = c;
                    break;
                }
            }

            anchor.InsertXmlBefore(WordXml.Bytes(fragment));
        }

        _set.Invalidate();
    }

    private static string? MergeCode(string? state, string param) => state switch
    {
        null => null,
        "restart" => "rest",
        "continue" => "cont",
        _ => throw new ArgumentException("Use 'restart', 'continue' or null.", param),
    };

    // ------------------------------------------------------------ internals

    private void CheckMoveName(string? name)
    {
        if (name is null)
        {
            return;
        }

        WordXml.RequireXmlText(name, nameof(name));
        if (name.Length == 0 || _set.MoveNameInUse(name))
        {
            throw new ArgumentException("The move name is empty or already used by another move.", nameof(name));
        }
    }

    private int NextId() => _set.Allocate(_ids);

    private string Attributes(WmlWriter w)
    {
        string s = w.Attr("id", NextId().ToString(System.Globalization.CultureInfo.InvariantCulture)) + w.Attr("author", Name);
        if (Date is { } d)
        {
            s += w.Attr("date", WordXml.FormatDate(d));
        }

        return s;
    }

    private string AttributesWithId(WmlWriter w, int id)
    {
        string s = w.Attr("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)) + w.Attr("author", Name);
        if (Date is { } d)
        {
            s += w.Attr("date", WordXml.FormatDate(d));
        }

        return s;
    }

    private void RequireParagraph(MarkupNode node)
    {
        if (!WordXml.Is(node, "p"))
        {
            throw new ArgumentException("The node is not a w:p element.", nameof(node));
        }

        if (!_set.Parts.Any(p => ReferenceEquals(p.Markup, node.Document)))
        {
            throw new ArgumentException("The node is not in one of the revision set's parts.", nameof(node));
        }
    }

    /// <summary>A text run's content for a string: tabs and line feeds become elements.</summary>
    private static string RunContent(WmlWriter w, string text)
    {
        var sb = new StringBuilder();
        var chunk = new StringBuilder();
        void Flush()
        {
            if (chunk.Length > 0)
            {
                sb.Append(w.Wrap("t", " xml:space=\"preserve\"", WordXml.EscapeText(chunk.ToString())));
                chunk.Clear();
            }
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\t')
            {
                Flush();
                sb.Append(w.Empty("tab"));
            }
            else if (c == '\n' || c == '\r')
            {
                Flush();
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                sb.Append(w.Empty("br"));
            }
            else
            {
                chunk.Append(c);
            }
        }

        Flush();
        return sb.ToString();
    }

    // The characters of a paragraph's current text, as the runs hold them.
    private readonly record struct Atom(MarkupNode Run, MarkupNode Child, int Start, int Length);

    private static List<Atom> Atoms(MarkupNode paragraph)
    {
        var atoms = new List<Atom>();
        int pos = 0;
        WalkInline(paragraph, atoms, ref pos, 0);
        return atoms;
    }

    private static void WalkInline(MarkupNode container, List<Atom> atoms, ref int pos, int depth)
    {
        if (depth > 128)
        {
            return;
        }

        foreach (MarkupNode c in container.ChildElements)
        {
            if (!WordXml.InMain(c))
            {
                continue;
            }

            if (WordXml.Is(c, "r"))
            {
                foreach (MarkupNode rc in c.ChildElements)
                {
                    if (WordXml.Is(rc, "rPr"))
                    {
                        continue;
                    }

                    int len = 0;
                    if (WordXml.Is(rc, "t"))
                    {
                        len = rc.InnerText.Length;
                    }
                    else if (WordXml.IsAny(rc, "tab", "br", "cr", "noBreakHyphen", "ptab"))
                    {
                        len = 1;
                    }

                    atoms.Add(new Atom(c, rc, pos, len));
                    pos += len;
                }
            }
            else if (WordXml.IsAny(c, "ins", "moveTo", "hyperlink", "fldSimple", "smartTag", "customXml", "dir", "bdo", "sdt", "sdtContent"))
            {
                WalkInline(c, atoms, ref pos, depth + 1);
            }
        }
    }

    private static int TotalLength(MarkupNode paragraph)
    {
        List<Atom> a = Atoms(paragraph);
        int total = 0;
        foreach (Atom x in a)
        {
            total = Math.Max(total, x.Start + x.Length);
        }

        return total;
    }

    private static void CheckRange(MarkupNode paragraph, int offset, int length)
    {
        int total = TotalLength(paragraph);
        if (offset < 0 || offset > total)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), $"The paragraph's text has {total} characters.");
        }

        if (length < 0 || offset + (long)length > total)
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"The range runs past the paragraph's {total} characters.");
        }
    }

    private static (string StartTag, string RPr, List<MarkupNode> Children) RunParts(MarkupNode run)
    {
        string rPr = string.Empty;
        var kids = new List<MarkupNode>();
        foreach (MarkupNode c in run.ChildElements)
        {
            if (WordXml.Is(c, "rPr"))
            {
                rPr = WordXml.Portable(c);
            }
            else
            {
                kids.Add(c);
            }
        }

        return (WordXml.StartTag(run), rPr, kids);
    }

    private static string RunXml(MarkupNode run, string startTag, string rPr, IEnumerable<string> childXml) =>
        startTag + rPr + string.Concat(childXml) + "</" + run.Name + ">";

    private static string TextElement(MarkupNode t, string text) =>
        "<" + t.Name + " xml:space=\"preserve\">" + WordXml.EscapeText(text) + "</" + t.Name + ">";

    /// <summary>Makes sure a run boundary exists at the position, and returns the first run after it (or a null node at the end).</summary>
    private static MarkupNode IsolateBoundary(MarkupNode paragraph, int offset)
    {
        List<Atom> atoms = Atoms(paragraph);
        foreach (Atom a in atoms)
        {
            if (a.Length > 0 && a.Start < offset && offset < a.Start + a.Length && WordXml.Is(a.Child, "t"))
            {
                SplitText(a, offset - a.Start);
                atoms = Atoms(paragraph);
                break;
            }
        }

        foreach (Atom a in atoms)
        {
            if (a.Start >= offset)
            {
                MarkupNode first = default;
                foreach (MarkupNode c in a.Run.ChildElements)
                {
                    if (!WordXml.Is(c, "rPr"))
                    {
                        first = c;
                        break;
                    }
                }

                return a.Child == first ? a.Run : SplitBefore(a.Run, a.Child);
            }
        }

        return default;
    }

    private static MarkupNode SplitBefore(MarkupNode run, MarkupNode child)
    {
        (string tag, string rPr, List<MarkupNode> kids) = RunParts(run);
        int at = kids.IndexOf(child);
        string xml = RunXml(run, tag, rPr, kids.Take(at).Select(k => WordXml.Portable(k))) + RunXml(run, tag, rPr, kids.Skip(at).Select(k => WordXml.Portable(k)));
        return ReplaceRun(run, xml);
    }

    private static void SplitText(Atom a, int within)
    {
        MarkupNode run = a.Run;
        (string tag, string rPr, List<MarkupNode> kids) = RunParts(run);
        int at = kids.IndexOf(a.Child);
        string text = a.Child.InnerText;
        string left = RunXml(run, tag, rPr, kids.Take(at).Select(k => WordXml.Portable(k)).Append(TextElement(a.Child, text[..within])));
        string right = RunXml(run, tag, rPr, kids.Skip(at + 1).Select(k => WordXml.Portable(k)).Prepend(TextElement(a.Child, text[within..])));
        ReplaceRun(run, left + right);
    }

    /// <summary>Replaces a run by two runs given as XML; returns the second.</summary>
    private static MarkupNode ReplaceRun(MarkupNode run, string twoRuns)
    {
        run.InsertXmlBefore(WordXml.Bytes(twoRuns));
        MarkupNode second = WordXml.PreviousElement(run);
        run.Remove();
        return second;
    }

    private static string FormattingSource(MarkupNode paragraph, int offset)
    {
        List<Atom> atoms = Atoms(paragraph);
        MarkupNode run = default;
        foreach (Atom a in atoms)
        {
            if (a.Length > 0 && a.Start < offset)
            {
                run = a.Run;
            }
        }

        if (run.IsNull)
        {
            foreach (Atom a in atoms)
            {
                if (a.Length > 0)
                {
                    run = a.Run;
                    break;
                }
            }
        }

        if (run.IsNull)
        {
            return string.Empty;
        }

        MarkupNode rPr = WordXml.Child(run, "rPr");
        if (rPr.IsNull)
        {
            return string.Empty;
        }

        // Copy the formatting but not a pending formatting change or revision marks.
        var sb = new StringBuilder();
        foreach (MarkupNode c in rPr.ChildElements)
        {
            if (!WordXml.IsAny(c, "rPrChange", "ins", "del", "moveFrom", "moveTo"))
            {
                sb.Append(WordXml.Portable(c));
            }
        }

        return sb.Length == 0 ? string.Empty : "<" + rPr.Name + ">" + sb + "</" + rPr.Name + ">";
    }

    private static bool HasElement(MarkupNode n)
    {
        foreach (MarkupNode unused in n.ChildElements)
        {
            _ = unused;
            return true;
        }

        return false;
    }

    private static bool IsWrapper(MarkupNode n) => WordXml.IsAny(n, "ins", "moveTo");

    /// <summary>Inserts XML before a node, splitting an insertion wrapper around it when the node is in the middle of one.</summary>
    private static void PlaceBefore(MarkupNode node, string xml)
    {
        MarkupNode parent = node.Parent;
        if (IsWrapper(parent))
        {
            if (WordXml.PreviousElement(node).IsNull)
            {
                parent.InsertXmlBefore(WordXml.Bytes(xml));
                return;
            }

            var rest = new StringBuilder();
            var moved = new List<MarkupNode>();
            for (MarkupNode s = node; !s.IsNull; s = s.NextSibling)
            {
                rest.Append(WordXml.Portable(s));
                moved.Add(s);
            }

            string tag = WordXml.StartTag(parent);
            foreach (MarkupNode m in moved)
            {
                m.Remove();
            }

            parent.InsertXmlAfter(WordXml.Bytes(xml + tag + rest + "</" + parent.Name + ">"));
            return;
        }

        node.InsertXmlBefore(WordXml.Bytes(xml));
    }

    private static void PlaceAt(MarkupNode paragraph, MarkupNode boundary, string xml)
    {
        if (!boundary.IsNull)
        {
            PlaceBefore(boundary, xml);
            return;
        }

        // At the end: after the last live run (or wrapper), or at the end of the paragraph when it has none.
        List<Atom> atoms = Atoms(paragraph);
        if (atoms.Count == 0)
        {
            paragraph.AppendXml(WordXml.Bytes(xml));
            return;
        }

        MarkupNode last = atoms[^1].Run;
        MarkupNode next = WordXml.NextElement(last);
        if (!next.IsNull)
        {
            PlaceBefore(next, xml);
        }
        else if (IsWrapper(last.Parent))
        {
            last.Parent.InsertXmlAfter(WordXml.Bytes(xml));
        }
        else
        {
            last.InsertXmlAfter(WordXml.Bytes(xml));
        }
    }

    private static List<MarkupNode> RunsInRange(MarkupNode paragraph, int start, int end, bool includeTrailingZeroWidth = false)
    {
        List<Atom> atoms = Atoms(paragraph);
        int total = 0;
        foreach (Atom a in atoms)
        {
            total = Math.Max(total, a.Start + a.Length);
        }

        var seen = new HashSet<MarkupNode>();
        var runs = new List<MarkupNode>();
        foreach (Atom a in atoms)
        {
            bool inside = a.Start >= start && (a.Start < end || (includeTrailingZeroWidth && end >= total && a.Start == end));
            if (inside && seen.Add(a.Run))
            {
                runs.Add(a.Run);
            }
        }

        return runs;
    }

    /// <summary>Wraps the runs of a range in deletion or move-source wrappers. Returns the last wrapper created in document order.</summary>
    private void MarkRange(MarkupNode paragraph, int start, int end, string wrapperName, (int Id, string Name)? move, out MarkupNode lastWrapper, bool includeTrailingZeroWidth = false)
    {
        lastWrapper = default;
        WmlWriter w = WmlWriter.For(paragraph);
        IsolateBoundary(paragraph, end);
        IsolateBoundary(paragraph, start);
        List<MarkupNode> runs = RunsInRange(paragraph, start, end, includeTrailingZeroWidth);
        var rest = new List<MarkupNode>();
        foreach (MarkupNode r in runs)
        {
            // Deleting text this author inserted just removes it.
            if (wrapperName == "del" && IsWrapper(r.Parent) && string.Equals(WordXml.Attr(r.Parent, "author"), Name, StringComparison.Ordinal) && WordXml.Is(r.Parent, "ins"))
            {
                MarkupNode wrapper = r.Parent;
                r.Remove();
                if (!HasElement(wrapper))
                {
                    wrapper.Remove();
                }

                continue;
            }

            rest.Add(r);
        }

        var groups = new List<List<MarkupNode>>();
        foreach (MarkupNode r in rest)
        {
            if (groups.Count > 0 && groups[^1][^1].Parent == r.Parent && WordXml.NextElement(groups[^1][^1]) == r)
            {
                groups[^1].Add(r);
            }
            else
            {
                groups.Add([r]);
            }
        }

        for (int g = groups.Count - 1; g >= 0; g--)
        {
            List<MarkupNode> group = groups[g];
            var inner = new StringBuilder();
            foreach (MarkupNode r in group)
            {
                // Observed in Word 365 (16.0.20527): moved-away text stays w:t inside w:moveFrom (only w:del uses w:delText);
                // a w:moveFrom holding w:delText makes Word offer to repair the file.
                inner.Append(wrapperName == "moveFrom" ? WordXml.Portable(r) : DeletedRunXml(r));
            }

            int id = NextId();
            string wrapped = w.Wrap(wrapperName, AttributesWithId(w, id), inner.ToString());
            string xml = move is { } m ? Range(w, wrapperName, m.Id, m.Name, inner.ToString(), -1, wrapped) : wrapped;
            group[0].InsertXmlBefore(WordXml.Bytes(xml));
            foreach (MarkupNode r in group)
            {
                r.Remove();
            }

            if (lastWrapper.IsNull)
            {
                lastWrapper = FindById(paragraph, wrapperName, id);
            }
        }
    }

    /// <summary>Range markers around a move wrapper: start (with the pairing name), the wrapper, end.</summary>
    private string Range(WmlWriter w, string wrapperName, int markerId, string name, string content, int endIdUnused, string? wrappedAlready = null)
    {
        _ = endIdUnused;
        string startName = wrapperName + "RangeStart";
        string endName = wrapperName + "RangeEnd";
        string wrapper = wrappedAlready ?? w.Wrap(wrapperName, Attributes(w), content);
        return w.Empty(startName, AttributesWithId(w, markerId) + w.Attr("name", name))
            + wrapper
            + w.Empty(endName, w.Attr("id", markerId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private void InsertMoveTo(MarkupNode paragraph, int offset, string runsXml, string name)
    {
        WmlWriter w = WmlWriter.For(paragraph);
        MarkupNode boundary = IsolateBoundary(paragraph, offset);
        string xml = Range(w, "moveTo", NextId(), name, runsXml, -1);
        PlaceAt(paragraph, boundary, xml);
    }

    private static MarkupNode FindById(MarkupNode root, string local, int id)
    {
        string text = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (MarkupNode n = root.FirstChild; !n.IsNull; n = WordXml.Next(n, root, true))
        {
            if (WordXml.Is(n, local) && WordXml.Attr(n, "id") == text)
            {
                return n;
            }
        }

        return default;
    }

    /// <summary>A run as it must stand in a deletion: w:t becomes w:delText and w:instrText w:delInstrText (§17.3.3.7, §17.16.13).</summary>
    private static string DeletedRunXml(MarkupNode run)
    {
        (string tag, string rPr, List<MarkupNode> kids) = RunParts(run);
        var parts = new List<string>();
        foreach (MarkupNode k in kids)
        {
            if (WordXml.Is(k, "t") || WordXml.Is(k, "instrText"))
            {
                string local = WordXml.Is(k, "t") ? "delText" : "delInstrText";
                string prefix = k.Name[..^k.LocalNameString.Length];
                string raw = prefix + local;
                parts.Add("<" + raw + " xml:space=\"preserve\">" + WordXml.EscapeText(k.InnerText) + "</" + raw + ">");
            }
            else
            {
                parts.Add(WordXml.Portable(k));
            }
        }

        return RunXml(run, tag, rPr, parts);
    }

    private static bool IsAfter(MarkupNode a, MarkupNode b)
    {
        MarkupNode root = a.Document!.Root;
        for (MarkupNode n = root; !n.IsNull; n = WordXml.Next(n, root, true))
        {
            if (n == b)
            {
                return true;
            }

            if (n == a)
            {
                return false;
            }
        }

        return false;
    }

    // --- paragraph marks and properties

    private static MarkupNode EnsureChildFirst(MarkupNode parent, string local, WmlWriter w)
    {
        MarkupNode c = WordXml.Child(parent, local);
        if (c.IsNull)
        {
            parent.PrependXml(WordXml.Bytes(w.Empty(local)));
            c = WordXml.Child(parent, local);
        }

        return c;
    }

    /// <summary>Puts a revision marker into the paragraph mark's run properties (§17.13.5.16 and its siblings).</summary>
    private void MarkParagraphMark(MarkupNode paragraph, string markerName)
    {
        WmlWriter w = WmlWriter.For(paragraph);
        MarkupNode pPr = EnsureChildFirst(paragraph, "pPr", w);
        MarkupNode rPr = WordXml.Child(pPr, "rPr");
        if (rPr.IsNull)
        {
            MarkupNode anchor = default;
            foreach (MarkupNode c in pPr.ChildElements)
            {
                if (WordXml.IsAny(c, "sectPr", "pPrChange"))
                {
                    anchor = c;
                    break;
                }
            }

            string empty = w.Empty("rPr");
            if (anchor.IsNull)
            {
                pPr.AppendXml(WordXml.Bytes(empty));
            }
            else
            {
                anchor.InsertXmlBefore(WordXml.Bytes(empty));
            }

            rPr = WordXml.Child(pPr, "rPr");
        }

        rPr.PrependXml(WordXml.Bytes(w.Empty(markerName, Attributes(w))));
    }

    private void MarkParagraphInserted(MarkupNode p)
    {
        WmlWriter w = WmlWriter.For(p);
        var inner = new StringBuilder();
        var nodes = new List<MarkupNode>();
        foreach (MarkupNode c in p.Children)
        {
            if (WordXml.Is(c, "pPr"))
            {
                continue;
            }

            nodes.Add(c);
            inner.Append(WordXml.Portable(c));
        }

        if (nodes.Count > 0)
        {
            string wrapped = w.Wrap("ins", Attributes(w), inner.ToString());
            foreach (MarkupNode c in nodes)
            {
                c.Remove();
            }

            p.AppendXml(WordXml.Bytes(wrapped));
        }

        MarkParagraphMark(p, "ins");
    }

    private static string CopyParagraphProperties(MarkupNode pPr, WmlWriter w, bool withoutSection = false)
    {
        var sb = new StringBuilder();
        foreach (MarkupNode c in pPr.ChildElements)
        {
            if (WordXml.Is(c, "pPrChange") || (withoutSection && WordXml.Is(c, "sectPr")))
            {
                continue;
            }

            if (WordXml.Is(c, "rPr"))
            {
                var inner = new StringBuilder();
                foreach (MarkupNode rc in c.ChildElements)
                {
                    if (!WordXml.IsAny(rc, "ins", "del", "moveFrom", "moveTo", "rPrChange"))
                    {
                        inner.Append(WordXml.Portable(rc));
                    }
                }

                if (inner.Length > 0)
                {
                    sb.Append(w.Wrap("rPr", string.Empty, inner.ToString()));
                }
            }
            else
            {
                sb.Append(WordXml.Portable(c));
            }
        }

        return sb.Length == 0 ? string.Empty : w.Wrap("pPr", string.Empty, sb.ToString());
    }

    private static HashSet<string> ElementNames(string fragment, WmlWriter w)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        string uri = w.IsStrict ? WordXml.MainStrict : WordXml.Main;
        string prefix = w.Name("")[..^1];
        string doc = "<root xmlns:" + prefix + "=\"" + uri + "\">" + fragment + "</root>";
        if (!MarkupDocument.TryParse(WordXml.Bytes(doc), out MarkupDocument? parsed, null) || parsed is null)
        {
            throw new ArgumentException("The property fragment is not well-formed XML (use the part's prefix for the main namespace).");
        }

        foreach (MarkupNode c in parsed.Root.ChildElements)
        {
            names.Add(c.LocalNameString);
        }

        return names;
    }

    private static void CheckFragment(string fragment, WmlWriter w) => _ = ElementNames(fragment, w);

    private void TrackProperties(MarkupNode owner, string containerName, string changeName, string newXml, string[] leading, string[] trailing, bool atStart, bool afterTblPrEx = false)
    {
        ArgumentNullException.ThrowIfNull(newXml);
        WmlWriter w = WmlWriter.For(owner);
        CheckFragment(newXml, w);
        MarkupNode container = WordXml.Child(owner, containerName);
        if (container.IsNull)
        {
            string empty = w.Empty(containerName);
            if (afterTblPrEx)
            {
                MarkupNode ex = WordXml.Child(owner, "tblPrEx");
                if (ex.IsNull)
                {
                    owner.PrependXml(WordXml.Bytes(empty));
                }
                else
                {
                    ex.InsertXmlAfter(WordXml.Bytes(empty));
                }
            }
            else if (atStart)
            {
                // The run, paragraph, table and cell property containers come first (§17.3.2.1, §17.3.1.1, §17.4.1, §17.4.66).
                owner.PrependXml(WordXml.Bytes(empty));
            }
            else
            {
                owner.AppendXml(WordXml.Bytes(empty));
            }

            container = WordXml.Child(owner, containerName);
        }

        ApplyChange(container, containerName, changeName, newXml, leading, trailing);
    }

    private void ApplyChange(MarkupNode container, string containerName, string changeName, string newXml, string[] leading, string[] trailing)
    {
        WmlWriter w = WmlWriter.For(container);
        CheckFragment(newXml, w);
        var head = new StringBuilder();
        var tail = new StringBuilder();
        var oldBase = new StringBuilder();
        string? existingChange = null;
        foreach (MarkupNode c in container.ChildElements)
        {
            if (WordXml.Is(c, changeName))
            {
                existingChange = WordXml.Portable(c);
            }
            else if (WordXml.IsAny(c, leading))
            {
                head.Append(WordXml.Portable(c));
            }
            else if (WordXml.IsAny(c, trailing))
            {
                tail.Append(WordXml.Portable(c));
            }
            else
            {
                oldBase.Append(WordXml.Portable(c));
            }
        }

        string change = existingChange ?? w.Wrap(changeName, Attributes(w), oldBase.Length == 0 ? w.Empty(containerName) : w.Wrap(containerName, string.Empty, oldBase.ToString()));
        WordXml.SetContent(container, head.ToString() + newXml + tail + change);
    }

    private void MarkRow(MarkupNode row, string marker)
    {
        if (!WordXml.Is(row, "tr"))
        {
            throw new ArgumentException("The node is not a w:tr element.", nameof(row));
        }

        WmlWriter w = WmlWriter.For(row);
        MarkupNode trPr = WordXml.Child(row, "trPr");
        if (trPr.IsNull)
        {
            MarkupNode ex = WordXml.Child(row, "tblPrEx");
            string empty = w.Empty("trPr");
            if (ex.IsNull)
            {
                row.PrependXml(WordXml.Bytes(empty));
            }
            else
            {
                ex.InsertXmlAfter(WordXml.Bytes(empty));
            }

            trPr = WordXml.Child(row, "trPr");
        }

        string xml = w.Empty(marker, Attributes(w));
        MarkupNode change = WordXml.Child(trPr, "trPrChange");
        if (change.IsNull)
        {
            trPr.AppendXml(WordXml.Bytes(xml));
        }
        else
        {
            change.InsertXmlBefore(WordXml.Bytes(xml));
        }
    }

    private void MarkCell(MarkupNode cell, string marker, string extraAttributes)
    {
        if (!WordXml.Is(cell, "tc"))
        {
            throw new ArgumentException("The node is not a w:tc element.", nameof(cell));
        }

        WmlWriter w = WmlWriter.For(cell);
        MarkupNode tcPr = EnsureChildFirst(cell, "tcPr", w);
        string xml = w.Empty(marker, extraAttributes.Length == 0 ? Attributes(w) : extraAttributes);
        MarkupNode change = WordXml.Child(tcPr, "tcPrChange");
        if (change.IsNull)
        {
            tcPr.AppendXml(WordXml.Bytes(xml));
        }
        else
        {
            change.InsertXmlBefore(WordXml.Bytes(xml));
        }
    }

    private static List<MarkupNode> ParagraphsIn(MarkupNode container)
    {
        var list = new List<MarkupNode>();
        for (MarkupNode n = container.FirstChild; !n.IsNull; n = WordXml.Next(n, container, !WordXml.Is(n, "p")))
        {
            if (WordXml.Is(n, "p"))
            {
                list.Add(n);
            }
        }

        return list;
    }
}
