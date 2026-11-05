using System.Collections;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>The sheets of a workbook, in workbook order. Names are compared ignoring case, as Excel does.</summary>
public sealed class SheetCollection : IReadOnlyList<Worksheet>
{
    private readonly Workbook _workbook;
    private readonly List<Worksheet> _sheets;

    internal SheetCollection(Workbook workbook, List<Worksheet> sheets)
    {
        _workbook = workbook;
        _sheets = sheets;
    }

    /// <summary>Gets the number of sheets.</summary>
    public int Count => _sheets.Count;

    /// <summary>Gets a sheet by 0-based position.</summary>
    /// <param name="index">The position.</param>
    /// <returns>The sheet.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No sheet at that position.</exception>
    public Worksheet this[int index] => _sheets[index];

    /// <summary>Gets a sheet by name, ignoring case. The first of two sheets with the same name is returned.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The sheet.</returns>
    /// <exception cref="KeyNotFoundException">No sheet has that name.</exception>
    public Worksheet this[string name] => TryGet(name, out Worksheet? s) ? s : throw new KeyNotFoundException("The workbook has no sheet named '" + name + "'.");

    /// <summary>Finds a sheet by name, ignoring case.</summary>
    /// <param name="name">The name.</param>
    /// <param name="sheet">The sheet when the result is true.</param>
    /// <returns>True when found.</returns>
    public bool TryGet(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Worksheet? sheet)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (Worksheet s in _sheets)
        {
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                sheet = s;
                return true;
            }
        }

        sheet = null;
        return false;
    }

    /// <summary>Gets the position of a sheet.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <returns>The 0-based position, or -1.</returns>
    public int IndexOf(Worksheet sheet) => _sheets.IndexOf(sheet);

    /// <summary>Appends a new empty worksheet.</summary>
    /// <param name="name">The name: 1 to 31 characters, none of <c>\ / ? * [ ] :</c>, not starting or ending with an apostrophe, not used by another sheet (ignoring case).</param>
    /// <returns>The sheet.</returns>
    /// <exception cref="ArgumentException">The name is not valid or is taken.</exception>
    public Worksheet Add(string name) => _workbook.InsertSheet(_sheets.Count, name);

    /// <summary>Inserts a new empty worksheet.</summary>
    /// <param name="index">The position it takes, 0 to <see cref="Count"/>.</param>
    /// <param name="name">The name; see <see cref="Add"/>.</param>
    /// <returns>The sheet.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the list.</exception>
    /// <exception cref="ArgumentException">The name is not valid or is taken.</exception>
    public Worksheet Insert(int index, string name) => _workbook.InsertSheet(index, name);

    /// <summary>
    /// Removes a sheet with its part and the parts only it uses (drawings, tables, comments). Defined names scoped to it are removed and
    /// the scope of later ones shifts. Formulas that mention the sheet are not rewritten.
    /// </summary>
    /// <param name="sheet">The sheet.</param>
    /// <exception cref="InvalidOperationException">It is the last visible sheet.</exception>
    /// <exception cref="ArgumentException">The sheet belongs to another workbook.</exception>
    public void Remove(Worksheet sheet) => _workbook.RemoveSheet(sheet);

    /// <summary>Moves a sheet to another position. Scoped defined names and the active tab follow the sheets.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <param name="newIndex">The new 0-based position.</param>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the list.</exception>
    public void Move(Worksheet sheet, int newIndex) => _workbook.MoveSheet(sheet, newIndex);

    /// <inheritdoc />
    public IEnumerator<Worksheet> GetEnumerator() => _sheets.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed partial class Worksheet
{
    internal void AdoptState(SheetState state) => _state = state;

    /// <summary>
    /// Renames the sheet. Formulas, defined names and chart references that mention the old name are not rewritten (that is the
    /// formula layer's job), so rename before such references exist or fix them yourself.
    /// </summary>
    /// <param name="newName">The name; see <see cref="SheetCollection.Add"/> for the rules.</param>
    /// <exception cref="ArgumentException">The name is not valid or is taken by another sheet.</exception>
    public void Rename(string newName) => _workbook.RenameSheet(this, newName);
}

public sealed partial class Workbook
{
    private static readonly System.Buffers.SearchValues<char> InvalidSheetNameChars = System.Buffers.SearchValues.Create("\\/?*[]:");

    private void CheckSheetName(string name, Worksheet? self)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length is 0 or > 31 || name.AsSpan().IndexOfAny(InvalidSheetNameChars) >= 0 || name[0] == '\'' || name[^1] == '\'')
        {
            throw new ArgumentException("A sheet name has 1 to 31 characters, none of \\ / ? * [ ] :, and does not start or end with an apostrophe.", nameof(name));
        }

        foreach (Worksheet s in _sheets)
        {
            if (!ReferenceEquals(s, self) && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Another sheet is already named '" + name + "'.", nameof(name));
            }
        }
    }

    internal void RenameSheet(Worksheet sheet, string newName)
    {
        CheckOwn(sheet);
        CheckSheetName(newName, sheet);
        if (sheet.Name == newName)
        {
            return;
        }

        sheet.Name = newName;
        if (!sheet.Node.IsNull)
        {
            sheet.Node.SetAttribute("name", newName);
        }
    }

    private void CheckOwn(Worksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (!ReferenceEquals(sheet.Workbook, this) || !_sheets.Contains(sheet))
        {
            throw new ArgumentException("The sheet does not belong to this workbook.", nameof(sheet));
        }
    }

    private static byte[] EmptySheetBytes()
    {
        SheetFrame f = Worksheet.NewEmptyFrame();
        return [.. f.Head, .. "<sheetData/>"u8, .. f.Tail];
    }

    private static string AttributeEscape(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    internal Worksheet InsertSheet(int index, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _sheets.Count);
        CheckSheetName(name, null);

        int maxId = 0;
        foreach (Worksheet s in _sheets)
        {
            maxId = Math.Max(maxId, s.SheetId);
        }

        int n = _sheets.Count + 1;
        string part;
        while (Package.FindPart(part = "/xl/worksheets/sheet" + n.ToString(CultureInfo.InvariantCulture) + ".xml") is not null)
        {
            n++;
        }

        byte[] bytes = EmptySheetBytes();
        Package.AddPart(part, WorksheetContentType, bytes);
        string rid = NewRelationshipId();
        var rels = new List<Relationship>(Package.GetRelationships(MainPartName)) { new Relationship(rid, RelBase + "worksheet", RelativeTo(part)) };
        Package.SetRelationships(MainPartName, rels);

        var sheet = new Worksheet(this, name, maxId + 1, rid, part, SheetKind.Worksheet, SheetState.Visible, default);
        sheet.InitialiseEmpty();
        List<Worksheet> before = [.. _sheets];
        _sheets.Insert(index, sheet);
        RewriteSheetsElement();
        RemapSheetIndexes(before);
        _structureChanged = true;
        return sheet;
    }

    internal void RemoveSheet(Worksheet sheet)
    {
        CheckOwn(sheet);
        if (VisibleSheetCount(sheet) == 0)
        {
            throw new InvalidOperationException("A workbook needs at least one visible sheet.");
        }

        List<Worksheet> before = [.. _sheets];
        _sheets.Remove(sheet);
        var rels = new List<Relationship>();
        foreach (Relationship r in Package.GetRelationships(MainPartName))
        {
            if (r.Id != sheet.RelationshipId)
            {
                rels.Add(r);
            }
        }

        Package.SetRelationships(MainPartName, rels);
        if (sheet.PartName is not null)
        {
            RemoveOwnedParts(sheet.PartName);
        }

        RewriteSheetsElement();
        RemapSheetIndexes(before);
        _structureChanged = true;
    }

    internal void MoveSheet(Worksheet sheet, int newIndex)
    {
        CheckOwn(sheet);
        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newIndex, _sheets.Count);
        int old = _sheets.IndexOf(sheet);
        if (old == newIndex)
        {
            return;
        }

        List<Worksheet> before = [.. _sheets];
        _sheets.RemoveAt(old);
        _sheets.Insert(newIndex, sheet);
        RewriteSheetsElement();
        RemapSheetIndexes(before);
        _structureChanged = true;
    }

    // Rebuilds the children of <sheets> in the order of the list, keeping each existing element exactly as written (unknown
    // attributes included) and generating the one for a new sheet.
    private void RewriteSheetsElement()
    {
        string pc = Pc();
        string? rPrefix = null;
        if (_tree.Root.TryGetPrefixInScope(RelNs, out string p1))
        {
            rPrefix = p1;
        }
        else if (_tree.Root.TryGetPrefixInScope(RelNsStrict, out string p2))
        {
            rPrefix = p2;
        }

        string relNs = IsStrict ? RelNsStrict : RelNs;
        var sb = new StringBuilder();
        foreach (Worksheet s in _sheets)
        {
            if (!s.Node.IsNull)
            {
                sb.Append(s.Node.OuterXml);
                continue;
            }

            sb.Append('<').Append(pc).Append("sheet name=\"").Append(AttributeEscape(s.Name)).Append("\" sheetId=\"").Append(s.SheetId.ToString(CultureInfo.InvariantCulture)).Append('"');
            if (s.State != SheetState.Visible)
            {
                sb.Append(" state=\"").Append(s.State == SheetState.Hidden ? "hidden" : "veryHidden").Append('"');
            }

            if (rPrefix is { Length: > 0 })
            {
                sb.Append(' ').Append(rPrefix).Append(":id=\"").Append(s.RelationshipId).Append('"');
            }
            else
            {
                sb.Append(" xmlns:r=\"").Append(relNs).Append("\" r:id=\"").Append(s.RelationshipId).Append('"');
            }

            sb.Append("/>");
        }

        if (_sheetsNode.IsNull)
        {
            InsertWorkbookChild("sheets", "<" + pc + "sheets>" + sb + "</" + pc + "sheets>");
            foreach (MarkupNode c in _tree.Root.ChildElements)
            {
                if (c.LocalName.SequenceEqual("sheets"u8))
                {
                    _sheetsNode = c;
                    break;
                }
            }
        }
        else
        {
            var old = new List<MarkupNode>();
            foreach (MarkupNode c in _sheetsNode.ChildElements)
            {
                old.Add(c);
            }

            foreach (MarkupNode c in old)
            {
                c.Remove();
            }

            _sheetsNode.AppendXml(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        int i = 0;
        foreach (MarkupNode c in _sheetsNode.ChildElements)
        {
            if (c.LocalName.SequenceEqual("sheet"u8) && i < _sheets.Count)
            {
                _sheets[i++].Node = c;
            }
        }
    }

    // Defined names scoped to a sheet, and the active tab, name sheets by position, so they follow the sheets when positions change.
    private void RemapSheetIndexes(List<Worksheet> before)
    {
        var map = new int[before.Count];
        bool identity = true;
        for (int i = 0; i < before.Count; i++)
        {
            map[i] = _sheets.IndexOf(before[i]);
            identity &= map[i] == i;
        }

        if (identity)
        {
            return;
        }

        MarkupNode emptyNames = default;
        foreach (MarkupNode child in _tree.Root.ChildElements)
        {
            if (child.LocalName.SequenceEqual("definedNames"u8))
            {
                var names = new List<MarkupNode>();
                foreach (MarkupNode d in child.ChildElements)
                {
                    names.Add(d);
                }

                foreach (MarkupNode d in names)
                {
                    if (d.GetAttribute("localSheetId") is { } ls && int.TryParse(ls, NumberStyles.Integer, CultureInfo.InvariantCulture, out int old) && old >= 0 && old < map.Length)
                    {
                        if (map[old] < 0)
                        {
                            d.Remove();
                        }
                        else if (map[old] != old)
                        {
                            d.SetAttribute("localSheetId", map[old].ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }

                // The schema needs at least one definedName inside definedNames.
                bool any = false;
                foreach (MarkupNode d in child.ChildElements)
                {
                    any = true;
                    break;
                }

                if (!any)
                {
                    emptyNames = child;
                }
            }
            else if (child.LocalName.SequenceEqual("bookViews"u8))
            {
                foreach (MarkupNode view in child.ChildElements)
                {
                    foreach (string attr in new[] { "activeTab", "firstSheet" })
                    {
                        if (view.GetAttribute(attr) is { } v && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int old) && old >= 0 && old < map.Length)
                        {
                            int now = map[old] >= 0 ? map[old] : Math.Max(0, Math.Min(old, _sheets.Count - 1));
                            if (now != old)
                            {
                                view.SetAttribute(attr, now.ToString(CultureInfo.InvariantCulture));
                            }
                        }
                    }
                }
            }
        }

        if (!emptyNames.IsNull)
        {
            emptyNames.Remove();
        }
    }

    // Removes a part and the parts that only it (through other removed parts) refers to. A part something else still refers to stays.
    private void RemoveOwnedParts(string rootPart)
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> reach;
        while (true)
        {
            reach = Reach(rootPart, blocked);
            bool added = false;
            var all = new List<string?> { null };
            foreach (IPackagePart p in Package.Parts)
            {
                all.Add(p.Name);
            }

            foreach (string? owner in all)
            {
                if (owner is not null && reach.Contains(owner))
                {
                    continue;
                }

                foreach (Relationship r in Package.GetRelationships(owner))
                {
                    if (r.TargetMode == RelationshipTargetMode.Internal
                        && PartUri.TryResolve(owner is null ? null : new PartUri(owner), r.Target, out PartUri uri)
                        && !string.Equals(uri.Value, rootPart, StringComparison.OrdinalIgnoreCase)
                        && reach.Contains(uri.Value) && blocked.Add(uri.Value))
                    {
                        added = true;
                    }
                }
            }

            if (!added)
            {
                break;
            }
        }

        foreach (string name in reach)
        {
            Package.RemovePart(name);
        }
    }

    private HashSet<string> Reach(string root, HashSet<string> blocked)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            string cur = queue.Dequeue();
            foreach (Relationship r in Package.GetRelationships(cur))
            {
                if (r.TargetMode == RelationshipTargetMode.Internal && PartUri.TryResolve(new PartUri(cur), r.Target, out PartUri uri)
                    && Package.FindPart(uri.Value) is { } target && !blocked.Contains(target.Name) && seen.Add(target.Name))
                {
                    queue.Enqueue(target.Name);
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// Creates a workbook with one empty worksheet named <c>Sheet1</c> and a minimal style sheet. The bytes written for it are the same on every run.
    /// </summary>
    /// <param name="limits">The limits; null for the defaults.</param>
    /// <returns>The workbook, which has no <see cref="OfficeFile.FilePath"/>, so use <c>SaveAs</c>.</returns>
    public static Workbook Create(OfficeLimits? limits = null)
    {
        OpcPackage package = OpcPackage.Create(limits);
        const string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
        package.SetRelationships(null, [new Relationship("rId1", RelBase + "officeDocument", "xl/workbook.xml")]);
        package.AddPart("/xl/workbook.xml", MainXlsx, Encoding.UTF8.GetBytes(xml
            + "<workbook xmlns=\"" + MainNs + "\" xmlns:r=\"" + RelNs + "\"><bookViews><workbookView/></bookViews><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets><calcPr calcId=\"191029\"/></workbook>"));
        package.SetRelationships("/xl/workbook.xml",
        [
            new Relationship("rId1", RelBase + "worksheet", "worksheets/sheet1.xml"),
            new Relationship("rId2", RelBase + "styles", "styles.xml"),
        ]);
        package.AddPart("/xl/worksheets/sheet1.xml", WorksheetContentType, EmptySheetBytes());
        package.AddPart("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", Encoding.UTF8.GetBytes(xml
            + "<styleSheet xmlns=\"" + MainNs + "\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font></fonts>"
            + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
            + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
            + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
            + "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>"
            + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>"));
        return FromPackage(package, new OpenContext(package.Limits), ownsPackage: true);
    }
}
