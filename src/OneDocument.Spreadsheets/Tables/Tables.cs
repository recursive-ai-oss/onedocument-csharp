using System.Collections;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>A column of a table (CT_TableColumn, ECMA-376 Part 1 §18.5.1.3).</summary>
public sealed class TableColumn
{
    private readonly MarkupNode _node;

    internal TableColumn(MarkupNode node) => _node = node;

    /// <summary>Gets the markup element.</summary>
    public MarkupNode Markup => _node;

    /// <summary>Gets the column's id, unique within the table.</summary>
    public int Id => Xl.Int(_node.GetAttribute("id"));

    /// <summary>Gets the column's name, which is also the text of its header cell.</summary>
    public string Name => _node.GetAttribute("name") ?? string.Empty;

    /// <summary>Gets the totals row function (<c>sum</c>, <c>count</c>, <c>average</c>, <c>custom</c>, …), or null.</summary>
    public string? TotalsRowFunction => _node.GetAttribute("totalsRowFunction");

    /// <summary>Gets the totals row label, or null.</summary>
    public string? TotalsRowLabel => _node.GetAttribute("totalsRowLabel");

    /// <summary>Gets the formula of a calculated column, or null.</summary>
    public string? CalculatedColumnFormula => ChildText("calculatedColumnFormula");

    /// <summary>Gets the totals row formula of a custom total, or null.</summary>
    public string? TotalsRowFormula => ChildText("totalsRowFormula");

    private string? ChildText(string local)
    {
        foreach (MarkupNode c in _node.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                return c.InnerText;
            }
        }

        return null;
    }
}

/// <summary>
/// A table (CT_Table, §18.5.1.2): a named range with a header row, optional totals row, columns and a style. A view over the table part;
/// structured-reference evaluation belongs to the calculation layer. Edits go through <see cref="Resize"/>, <see cref="Rename"/> and
/// <see cref="SetStyle"/>, which keep the header cells, columns and autofilter consistent (Excel repairs a file where they disagree).
/// </summary>
public sealed class Table
{
    private readonly Worksheet _sheet;
    private readonly MarkupDocument _doc;

    internal Table(Worksheet sheet, string partName, MarkupDocument doc, string relationshipId)
    {
        _sheet = sheet;
        PartName = partName;
        _doc = doc;
        RelationshipId = relationshipId;
    }

    /// <summary>Gets the worksheet that holds the table.</summary>
    public Worksheet Sheet => _sheet;

    /// <summary>Gets the part name, such as <c>/xl/tables/table1.xml</c>.</summary>
    public string PartName { get; }

    internal string RelationshipId { get; }

    /// <summary>Gets the markup tree of the table part.</summary>
    public MarkupDocument Markup => _doc;

    internal bool IsModified => _doc.IsModified;

    private MarkupNode Root => _doc.Root;

    /// <summary>Gets the table's id, unique among the tables of the workbook.</summary>
    public int Id => Xl.Int(Root.GetAttribute("id"));

    /// <summary>Gets the table's name (what structured references use).</summary>
    public string Name => Root.GetAttribute("name") ?? string.Empty;

    /// <summary>Gets the name shown in the interface.</summary>
    public string DisplayName => Root.GetAttribute("displayName") ?? Name;

    /// <summary>Gets the range, header and totals rows included.</summary>
    public CellRange Range => CellRange.TryParse(Root.GetAttribute("ref"), out CellRange r) ? r : default;

    /// <summary>Gets the number of header rows (0 or 1).</summary>
    public int HeaderRowCount => Xl.Int(Root.GetAttribute("headerRowCount"), 1);

    /// <summary>Gets the number of totals rows (0 or 1).</summary>
    public int TotalsRowCount => Xl.Int(Root.GetAttribute("totalsRowCount"), 0);

    /// <summary>Gets the range of the data rows (without header and totals).</summary>
    public CellRange DataRange
    {
        get
        {
            CellRange r = Range;
            if (!r.IsValid)
            {
                return r;
            }

            int top = r.TopLeft.Row + HeaderRowCount;
            int bottom = r.BottomRight.Row - TotalsRowCount;
            return bottom < top ? default : new CellRange(new CellAddress(top, r.TopLeft.Column), new CellAddress(bottom, r.BottomRight.Column));
        }
    }

    /// <summary>Gets the range of the autofilter, or an invalid range when the table has none.</summary>
    public CellRange AutoFilterRange
    {
        get
        {
            MarkupNode af = Xl.Child(Root, "autoFilter");
            return !af.IsNull && CellRange.TryParse(af.GetAttribute("ref"), out CellRange r) ? r : default;
        }
    }

    /// <summary>Gets the name of the table style, or null when the table has none.</summary>
    public string? StyleName => Xl.Child(Root, "tableStyleInfo") is { IsNull: false } s ? s.GetAttribute("name") : null;

    /// <summary>Gets the columns, in order.</summary>
    public IReadOnlyList<TableColumn> Columns
    {
        get
        {
            var list = new List<TableColumn>();
            MarkupNode cols = Xl.Child(Root, "tableColumns");
            if (!cols.IsNull)
            {
                foreach (MarkupNode c in cols.ChildElements)
                {
                    if (c.LocalName.SequenceEqual("tableColumn"u8))
                    {
                        list.Add(new TableColumn(c));
                    }
                }
            }

            return list;
        }
    }

    /// <summary>Renames the table. Formulas that use the old name are not rewritten.</summary>
    /// <param name="name">The new name: valid as a defined name and unused by any table or defined name in the workbook.</param>
    /// <exception cref="ArgumentException">The name is invalid or taken.</exception>
    public void Rename(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _sheet.Workbook.CheckNewTableName(name, this);
        Root.SetAttribute("name", name);
        Root.SetAttribute("displayName", name);
    }

    /// <summary>Sets the table style.</summary>
    /// <param name="styleName">The style name, such as <c>TableStyleMedium9</c>, or null to remove the style.</param>
    public void SetStyle(string? styleName)
    {
        MarkupNode info = Xl.Child(Root, "tableStyleInfo");
        string pc = Xl.Prefix(Root);
        if (styleName is null)
        {
            if (!info.IsNull) info.Remove();
            return;
        }

        if (info.IsNull)
        {
            // CT_Table: tableStyleInfo comes after tableColumns and before extLst.
            string xml = "<" + pc + "tableStyleInfo name=\"" + Xl.Esc(styleName) + "\" showFirstColumn=\"0\" showLastColumn=\"0\" showRowStripes=\"1\" showColumnStripes=\"0\"/>";
            Xl.InsertInOrder(Root, TableOrder, "tableStyleInfo", xml);
        }
        else
        {
            info.SetAttribute("name", styleName);
        }
    }

    // ECMA-376 Part 1 CT_Table: the child elements, in schema order.
    internal static readonly string[] TableOrder = ["autoFilter", "sortState", "tableColumns", "tableStyleInfo", "extLst"];

    /// <summary>
    /// Changes the table's range. Header cells of new columns get the names <c>Column</c><c>N</c>, columns that fall away are removed, and
    /// the autofilter follows (without the totals row). The header row and totals row counts stay as they are. The cells below the range
    /// are not moved or cleared.
    /// </summary>
    /// <param name="newRange">The new range, including the header and totals rows.</param>
    /// <exception cref="ArgumentException">The range is invalid, too small for its header and totals rows, or overlaps another table or a merged range.</exception>
    public void Resize(CellRange newRange)
    {
        int header = HeaderRowCount, totals = TotalsRowCount;
        if (!newRange.IsValid || newRange.RowCount < header + totals + (header == 0 && totals == 0 ? 1 : 0) || newRange.RowCount < 1)
        {
            throw new ArgumentException("The range is invalid or too small for the table's header and totals rows.", nameof(newRange));
        }

        foreach (Table other in _sheet.Tables)
        {
            if (!ReferenceEquals(other, this) && other.Range.Intersects(newRange))
            {
                throw new ArgumentException("The range overlaps the table '" + other.Name + "'.", nameof(newRange));
            }
        }

        foreach (CellRange m in _sheet.MergedCells)
        {
            if (m.Intersects(newRange) && header > 0 && m.Intersects(new CellRange(newRange.TopLeft, new CellAddress(newRange.TopLeft.Row, newRange.BottomRight.Column))))
            {
                throw new ArgumentException("The header row of a table cannot contain merged cells.", nameof(newRange));
            }
        }

        string pc = Xl.Prefix(Root);
        MarkupNode cols = Xl.Child(Root, "tableColumns");
        if (cols.IsNull)
        {
            // A damaged table can lack its column list; make one so the resize has somewhere to put the columns.
            cols = Xl.InsertInOrder(Root, TableOrder, "tableColumns", "<" + pc + "tableColumns count=\"0\"/>");
        }

        var existing = new List<MarkupNode>();
        foreach (MarkupNode c in cols.ChildElements)
        {
            if (c.LocalName.SequenceEqual("tableColumn"u8)) existing.Add(c);
        }

        int want = newRange.ColumnCount;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Math.Min(want, existing.Count); i++)
        {
            names.Add(existing[i].GetAttribute("name") ?? string.Empty);
        }

        for (int i = existing.Count - 1; i >= want; i--)
        {
            existing[i].Remove();
        }

        int nextId = 1;
        foreach (MarkupNode c in existing)
        {
            nextId = Math.Max(nextId, Xl.Int(c.GetAttribute("id")) + 1);
        }

        for (int i = existing.Count; i < want; i++)
        {
            int n = i + 1;
            string name;
            do
            {
                name = "Column" + (n++).ToString(CultureInfo.InvariantCulture);
            }
            while (!names.Add(name));

            cols.AppendXml(Encoding.UTF8.GetBytes("<" + pc + "tableColumn id=\"" + (nextId++).ToString(CultureInfo.InvariantCulture) + "\" name=\"" + name + "\"/>"));
        }

        cols.SetAttribute("count", want.ToString(CultureInfo.InvariantCulture));
        Root.SetAttribute("ref", newRange.ToString());

        MarkupNode af = Xl.Child(Root, "autoFilter");
        if (header > 0)
        {
            CellRange filter = totals > 0 && newRange.RowCount - totals >= 1
                ? new CellRange(newRange.TopLeft, new CellAddress(newRange.BottomRight.Row - totals, newRange.BottomRight.Column))
                : newRange;
            if (af.IsNull)
            {
                Xl.InsertInOrder(Root, TableOrder, "autoFilter", "<" + pc + "autoFilter ref=\"" + filter + "\"/>");
            }
            else
            {
                af.SetAttribute("ref", filter.ToString());
            }

            WriteHeaderCells(newRange);
        }
    }

    internal void WriteHeaderCells(CellRange range)
    {
        int col = range.TopLeft.Column;
        foreach (TableColumn c in Columns)
        {
            Cell cell = _sheet[range.TopLeft.Row, col++];
            if (!(cell.Value.Kind == CellValueKind.Text && cell.Text == c.Name))
            {
                // Excel repairs a table whose header cell does not hold the column name as text.
                cell.Formula = null;
                cell.SetString(c.Name);
            }
        }
    }

    internal byte[] ToBytes() => _doc.ToArray();
}

/// <summary>The tables of a worksheet, in the order of its <c>tableParts</c>.</summary>
public sealed class TableCollection : IEnumerable<Table>
{
    private const string TableContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml";
    private readonly Worksheet _sheet;
    private List<Table>? _tables;

    internal TableCollection(Worksheet sheet) => _sheet = sheet;

    internal bool IsModified
    {
        get
        {
            if (_tables is null) return false;
            foreach (Table t in _tables)
            {
                if (t.IsModified) return true;
            }

            return false;
        }
    }

    internal IReadOnlyList<Table> Loaded => Load();

    private List<Table> Load()
    {
        if (_tables is not null)
        {
            return _tables;
        }

        var list = new List<Table>();
        Workbook wb = _sheet.Workbook;
        string? sheetPart = _sheet.PartName;
        if (sheetPart is not null && _sheet.Kind == SheetKind.Worksheet)
        {
            MarkupNode parts = default;
            foreach (MarkupNode c in _sheet.FrameRoot.ChildElements)
            {
                if (c.LocalName.SequenceEqual("tableParts"u8))
                {
                    parts = c;
                    break;
                }
            }

            if (!parts.IsNull)
            {
                IReadOnlyList<Relationship> rels = wb.Package.GetRelationships(sheetPart);
                foreach (MarkupNode tp in parts.ChildElements)
                {
                    string? rid = tp.GetAttribute(wb.RelationshipNamespace, "id");
                    Relationship? rel = null;
                    foreach (Relationship r in rels)
                    {
                        if (r.Id == rid)
                        {
                            rel = r;
                            break;
                        }
                    }

                    if (rel is null || rel.TargetMode != RelationshipTargetMode.Internal || !PartUri.TryResolve(new PartUri(sheetPart), rel.Target, out PartUri uri) || wb.Package.FindPart(uri.Value) is not { } part)
                    {
                        wb.OpenContext.Defects.Report(new Defect(DefectKind.Packaging, "xlsx.table-part-missing", DefectSeverity.Dropped,
                            "A tablePart does not lead to a table part; the table is ignored.", DefectLocation.InPart(sheetPart, tp.SourceStart)));
                        continue;
                    }

                    try
                    {
                        MarkupDocument doc = MarkupDocument.Parse(part.GetBytes(), wb.OpenContext, part.Name);
                        if (!doc.Root.LocalName.SequenceEqual("table"u8))
                        {
                            throw new OfficeFormatException(DefectKind.Structure, "xlsx.table-wrong-root", "A table part does not start with a table element.", DefectLocation.InPart(part.Name));
                        }

                        list.Add(new Table(_sheet, part.Name, doc, rid!));
                    }
                    catch (OfficeFormatException ex)
                    {
                        wb.OpenContext.Defects.Report(new Defect(DefectKind.Structure, "xlsx.table-unreadable", DefectSeverity.Dropped, "A table part cannot be read (" + ex.Defect.Code + "); the table is ignored and kept as stored.", DefectLocation.InPart(part.Name)));
                    }
                }
            }
        }

        _tables = list;
        return list;
    }

    /// <inheritdoc />
    public IEnumerator<Table> GetEnumerator() => Load().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Gets the number of tables.</summary>
    public int Count => Load().Count;

    /// <summary>Gets a table by name (case-insensitive), or null.</summary>
    /// <param name="name">The table's name.</param>
    /// <returns>The table, or null.</returns>
    public Table? this[string name]
    {
        get
        {
            foreach (Table t in Load())
            {
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
            }

            return null;
        }
    }

    /// <summary>
    /// Adds a table over a range. The header cells of the first row are made to hold the column names: a text cell keeps its text (a
    /// repeated name gets a number appended), any other cell is set to <c>ColumnN</c>. With no header row, the columns are named
    /// <c>ColumnN</c> and no cell is written.
    /// </summary>
    /// <param name="name">The table's name: valid as a defined name, and not used by another table or defined name.</param>
    /// <param name="range">The range, header row included.</param>
    /// <param name="hasHeaderRow">Whether the first row is the header.</param>
    /// <param name="styleName">The table style, or null for none.</param>
    /// <returns>The new table.</returns>
    /// <exception cref="ArgumentException">The name is invalid or taken, the range overlaps another table, or has no data row below a header.</exception>
    /// <exception cref="NotSupportedException">The sheet is not a worksheet.</exception>
    public Table Add(string name, CellRange range, bool hasHeaderRow = true, string? styleName = "TableStyleMedium2")
    {
        ArgumentNullException.ThrowIfNull(name);
        Workbook wb = _sheet.Workbook;
        if (_sheet.Kind != SheetKind.Worksheet || _sheet.PartName is null)
        {
            throw new NotSupportedException("Tables can only be added to a worksheet with a part.");
        }

        if (!range.IsValid || (hasHeaderRow && range.RowCount < 2) || range.RowCount < 1)
        {
            throw new ArgumentException("The range must be valid and, with a header row, have at least one data row.", nameof(range));
        }

        wb.CheckNewTableName(name, null);
        foreach (Table t in Load())
        {
            if (t.Range.Intersects(range))
            {
                throw new ArgumentException("The range overlaps the table '" + t.Name + "'.", nameof(range));
            }
        }

        string sheetPart = _sheet.PartName;
        string pc = Xl.Prefix(_sheet.FrameRoot);
        string mpc = string.Empty; // the table part is written with the default namespace

        // Column names.
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < range.ColumnCount; i++)
        {
            string candidate = "Column" + (i + 1).ToString(CultureInfo.InvariantCulture);
            if (hasHeaderRow)
            {
                Cell h = _sheet[range.TopLeft.Row, range.TopLeft.Column + i];
                if (h.Value.Kind == CellValueKind.Text && h.Text.Length > 0)
                {
                    candidate = h.Text;
                }
            }

            string unique = candidate;
            for (int n = 2; !seen.Add(unique); n++)
            {
                unique = candidate + n.ToString(CultureInfo.InvariantCulture);
            }

            names.Add(unique);
        }

        int id = wb.NextTableId();
        string partName = "/xl/tables/table1.xml";
        for (int n = 1; wb.Package.FindPart(partName) is not null; n++)
        {
            partName = "/xl/tables/table" + (n + 1).ToString(CultureInfo.InvariantCulture) + ".xml";
        }

        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<table xmlns=\"").Append(wb.MainNamespace).Append("\" id=\"").Append(id.ToString(CultureInfo.InvariantCulture))
            .Append("\" name=\"").Append(Xl.Esc(name)).Append("\" displayName=\"").Append(Xl.Esc(name)).Append("\" ref=\"").Append(range).Append('"');
        if (!hasHeaderRow) sb.Append(" headerRowCount=\"0\"");
        sb.Append(" totalsRowShown=\"0\">");
        if (hasHeaderRow) sb.Append("<autoFilter ref=\"").Append(range).Append("\"/>");
        sb.Append("<tableColumns count=\"").Append(names.Count.ToString(CultureInfo.InvariantCulture)).Append("\">");
        for (int i = 0; i < names.Count; i++)
        {
            sb.Append("<tableColumn id=\"").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("\" name=\"").Append(Xl.Esc(names[i])).Append("\"/>");
        }

        sb.Append("</tableColumns>");
        if (styleName is not null)
        {
            sb.Append("<tableStyleInfo name=\"").Append(Xl.Esc(styleName)).Append("\" showFirstColumn=\"0\" showLastColumn=\"0\" showRowStripes=\"1\" showColumnStripes=\"0\"/>");
        }

        sb.Append("</table>");
        _ = mpc;
        wb.Package.AddPart(partName, TableContentType, Encoding.UTF8.GetBytes(sb.ToString()));

        // The relationship from the sheet part.
        var rels = new List<Relationship>(wb.Package.GetRelationships(sheetPart));
        string rid = "rId1";
        for (int n = 1; rels.Exists(r => r.Id == rid); n++)
        {
            rid = "rId" + (n + 1).ToString(CultureInfo.InvariantCulture);
        }

        rels.Add(new Relationship(rid, wb.RelationshipBase + "table", "../tables/" + partName[(partName.LastIndexOf('/') + 1)..]));
        wb.Package.SetRelationships(sheetPart, rels);

        // The tablePart element in the sheet.
        MarkupNode root = _sheet.FrameRoot;
        string rp;
        if (!root.TryGetPrefixInScope(wb.RelationshipNamespace, out rp!))
        {
            rp = "r";
            root.SetAttribute("xmlns:r", wb.RelationshipNamespace);
        }

        string tablePart = "<" + pc + "tablePart " + (rp.Length == 0 ? "id" : rp + ":id") + "=\"" + rid + "\"/>";
        MarkupNode parts = default;
        foreach (MarkupNode c in root.ChildElements)
        {
            if (c.LocalName.SequenceEqual("tableParts"u8))
            {
                parts = c;
                break;
            }
        }

        if (parts.IsNull)
        {
            _sheet.InsertFrameElement("tableParts", "<" + pc + "tableParts count=\"1\">" + tablePart + "</" + pc + "tableParts>");
        }
        else
        {
            parts.AppendXml(Encoding.UTF8.GetBytes(tablePart));
            int count = 0;
            foreach (MarkupNode c in parts.ChildElements) count++;
            parts.SetAttribute("count", count.ToString(CultureInfo.InvariantCulture));
        }

        _sheet.NoteFrameEdited();

        IPackagePart part = wb.Package.FindPart(partName)!;
        var table = new Table(_sheet, partName, MarkupDocument.Parse(part.GetBytes(), wb.OpenContext, partName), rid);
        Load().Add(table);
        if (hasHeaderRow)
        {
            table.WriteHeaderCells(range);
        }

        return table;
    }

    /// <summary>Removes a table: its part, relationship and <c>tablePart</c> element. The cells keep their values and styles.</summary>
    /// <param name="table">The table.</param>
    /// <exception cref="ArgumentException">The table does not belong to this sheet.</exception>
    public void Remove(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        List<Table> list = Load();
        if (!list.Remove(table))
        {
            throw new ArgumentException("The table does not belong to this sheet.", nameof(table));
        }

        Workbook wb = _sheet.Workbook;
        string sheetPart = _sheet.PartName!;
        MarkupNode parts = default;
        foreach (MarkupNode c in _sheet.FrameRoot.ChildElements)
        {
            if (c.LocalName.SequenceEqual("tableParts"u8))
            {
                parts = c;
                break;
            }
        }

        if (!parts.IsNull)
        {
            int left = 0;
            foreach (MarkupNode tp in parts.ChildElements)
            {
                if (tp.GetAttribute(wb.RelationshipNamespace, "id") == table.RelationshipId)
                {
                    tp.Remove();
                }
                else
                {
                    left++;
                }
            }

            if (left == 0)
            {
                parts.Remove();
            }
            else
            {
                parts.SetAttribute("count", left.ToString(CultureInfo.InvariantCulture));
            }
        }

        var keep = new List<Relationship>();
        foreach (Relationship r in wb.Package.GetRelationships(sheetPart))
        {
            if (r.Id != table.RelationshipId)
            {
                keep.Add(r);
            }
        }

        wb.Package.SetRelationships(sheetPart, keep);
        wb.Package.RemovePart(table.PartName);
        _sheet.NoteFrameEdited();
    }

    internal void Commit()
    {
        if (_tables is null)
        {
            return;
        }

        foreach (Table t in _tables)
        {
            if (t.IsModified && _sheet.Workbook.Package.FindPart(t.PartName) is { } part)
            {
                part.SetBytes(t.ToBytes());
            }
        }
    }
}
