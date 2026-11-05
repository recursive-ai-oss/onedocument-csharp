using System.Collections;
using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>The built-in defined names Excel creates for print settings and filters (ECMA-376 Part 1 §18.2.5, the <c>_xlnm.</c> names).</summary>
public enum BuiltInName
{
    /// <summary>A user name, or a built-in the model does not list.</summary>
    None = 0,

    /// <summary><c>_xlnm.Print_Area</c>.</summary>
    PrintArea,

    /// <summary><c>_xlnm.Print_Titles</c>: the rows and columns repeated on every printed page.</summary>
    PrintTitles,

    /// <summary><c>_xlnm._FilterDatabase</c>: the range of an autofilter.</summary>
    FilterDatabase,

    /// <summary><c>_xlnm.Criteria</c>.</summary>
    Criteria,

    /// <summary><c>_xlnm.Extract</c>.</summary>
    Extract,

    /// <summary><c>_xlnm.Consolidate_Area</c>.</summary>
    ConsolidateArea,

    /// <summary><c>_xlnm.Database</c>.</summary>
    Database,

    /// <summary><c>_xlnm.Sheet_Title</c>.</summary>
    SheetTitle,
}

/// <summary>
/// A defined name (CT_DefinedName, §18.2.5): a name for a formula, range or constant, scoped to the workbook or to one sheet. A view over the
/// <c>definedName</c> element; the formula is text and is not parsed or rewritten here.
/// </summary>
public sealed class DefinedName
{
    private static readonly string[] BuiltInNames = ["", "Print_Area", "Print_Titles", "_FilterDatabase", "Criteria", "Extract", "Consolidate_Area", "Database", "Sheet_Title"];
    private readonly Workbook _workbook;
    private readonly MarkupNode _node;

    internal DefinedName(Workbook workbook, MarkupNode node)
    {
        _workbook = workbook;
        _node = node;
    }

    /// <summary>Gets the markup element.</summary>
    public MarkupNode Markup => _node;

    internal Workbook Workbook => _workbook;

    /// <summary>Gets the name as written (a built-in keeps its <c>_xlnm.</c> prefix).</summary>
    public string Name => _node.GetAttribute("name") ?? string.Empty;

    /// <summary>Gets the index of the sheet the name is scoped to (its position in the sheet list), or null for a workbook-scoped name.</summary>
    public int? LocalSheetId => int.TryParse(_node.GetAttribute("localSheetId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : null;

    /// <summary>Gets the sheet the name is scoped to, or null for a workbook-scoped name.</summary>
    public Worksheet? Scope => LocalSheetId is { } i && i >= 0 && i < _workbook.Sheets.Count ? _workbook.Sheets[i] : null;

    /// <summary>Gets the built-in name this is, or <see cref="BuiltInName.None"/>.</summary>
    public BuiltInName BuiltIn
    {
        get
        {
            string n = Name;
            if (!n.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase))
            {
                return BuiltInName.None;
            }

            int i = Array.FindIndex(BuiltInNames, b => b.Length > 0 && string.Equals(b, n[6..], StringComparison.OrdinalIgnoreCase));
            return i < 0 ? BuiltInName.None : (BuiltInName)i;
        }
    }

    /// <summary>Gets a value indicating whether the name starts with <c>_xlnm.</c>.</summary>
    public bool IsBuiltIn => Name.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets or sets the formula text, without a leading <c>=</c>. Setting it does not check the syntax.</summary>
    public string Formula
    {
        get => _node.InnerText;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _node.SetText(value);
        }
    }

    /// <summary>Gets or sets a value indicating whether the name is hidden from the name list.</summary>
    public bool IsHidden
    {
        get => Xl.FlagAttr(_node, "hidden");
        set => SetFlag("hidden", value);
    }

    /// <summary>Gets or sets the comment.</summary>
    public string? Comment
    {
        get => _node.GetAttribute("comment");
        set => SetOrRemove("comment", value);
    }

    /// <summary>Gets a value indicating whether the name is a function (<c>function="1"</c>).</summary>
    public bool IsFunction => Xl.FlagAttr(_node, "function");

    /// <summary>Gets a value indicating whether the name refers to a macro-sheet (XLM) formula.</summary>
    public bool IsMacro => Xl.FlagAttr(_node, "xlm");

    /// <summary>Gets a value indicating whether the name is a VBA procedure.</summary>
    public bool IsVbProcedure => Xl.FlagAttr(_node, "vbProcedure");

    private void SetFlag(string name, bool on)
    {
        if (on)
        {
            _node.SetAttribute(name, "1");
        }
        else
        {
            _node.RemoveAttribute(name);
        }
    }

    private void SetOrRemove(string name, string? value)
    {
        if (value is null)
        {
            _node.RemoveAttribute(name);
        }
        else
        {
            _node.SetAttribute(name, value);
        }
    }

    internal static string BuiltInText(BuiltInName name) => "_xlnm." + BuiltInNames[(int)name];
}

/// <summary>The defined names of a workbook, in the order the part lists them. Editing changes only the <c>definedNames</c> element.</summary>
/// <remarks>Renaming a name or a sheet does not rewrite formulas that use the old name: that belongs to the formula layer.</remarks>
public sealed class DefinedNameCollection : IEnumerable<DefinedName>
{
    // ECMA-376 Part 1 CT_Workbook: the child elements of workbook, in schema order.
    private static readonly string[] WorkbookOrder =
    [
        "fileVersion", "fileSharing", "workbookPr", "workbookProtection", "bookViews", "sheets", "functionGroups", "externalReferences",
        "definedNames", "calcPr", "oleSize", "customWorkbookViews", "pivotCaches", "smartTagPr", "smartTagTypes", "webPublishing",
        "fileRecoveryPr", "webPublishObjects", "extLst",
    ];

    private readonly Workbook _workbook;

    internal DefinedNameCollection(Workbook workbook) => _workbook = workbook;

    private MarkupNode Container
    {
        get
        {
            foreach (MarkupNode c in _workbook.Tree.Root.ChildElements)
            {
                if (c.LocalName.SequenceEqual("definedNames"u8))
                {
                    return c;
                }
            }

            return default;
        }
    }

    /// <summary>Gets the number of names.</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (DefinedName _ in this)
            {
                n++;
            }

            return n;
        }
    }

    /// <inheritdoc />
    public IEnumerator<DefinedName> GetEnumerator()
    {
        MarkupNode c = Container;
        if (c.IsNull)
        {
            yield break;
        }

        foreach (MarkupNode d in c.ChildElements)
        {
            if (d.LocalName.SequenceEqual("definedName"u8))
            {
                yield return new DefinedName(_workbook, d);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Finds a name by its text (case-insensitive, as Excel compares names) and scope.</summary>
    /// <param name="name">The name.</param>
    /// <param name="scope">The sheet the name is scoped to, or null for a workbook-scoped name.</param>
    /// <returns>The name, or null.</returns>
    public DefinedName? Find(string name, Worksheet? scope = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        int? local = scope?.Index;
        foreach (DefinedName d in this)
        {
            if (d.LocalSheetId == local && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return d;
            }
        }

        return null;
    }

    /// <summary>Gets the names scoped to one sheet.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <returns>The names, in file order.</returns>
    public IEnumerable<DefinedName> ForSheet(Worksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        int i = sheet.Index;
        foreach (DefinedName d in this)
        {
            if (d.LocalSheetId == i)
            {
                yield return d;
            }
        }
    }

    /// <summary>Adds a name.</summary>
    /// <param name="name">The name (see <see cref="IsValidName"/>).</param>
    /// <param name="formula">The formula text, for example <c>Sheet1!$A$1:$B$10</c>; no leading <c>=</c>.</param>
    /// <param name="scope">The sheet the name is scoped to, or null for the workbook.</param>
    /// <param name="hidden">Hide the name from the name list.</param>
    /// <returns>The new name.</returns>
    /// <exception cref="ArgumentException">The name is not valid, or a name of that text exists in the same scope.</exception>
    public DefinedName Add(string name, string formula, Worksheet? scope = null, bool hidden = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(formula);
        if (!IsValidName(name, allowBuiltIn: true))
        {
            throw new ArgumentException("'" + name + "' is not a valid name.", nameof(name));
        }

        if (Find(name, scope) is not null)
        {
            throw new ArgumentException("A name '" + name + "' already exists in that scope.", nameof(name));
        }

        string pc = Xl.Prefix(_workbook.Tree.Root);
        var sb = new StringBuilder("<").Append(pc).Append("definedName name=\"").Append(Xl.Esc(name)).Append('"');
        if (scope is not null)
        {
            sb.Append(" localSheetId=\"").Append(scope.Index.ToString(CultureInfo.InvariantCulture)).Append('"');
        }

        if (hidden)
        {
            sb.Append(" hidden=\"1\"");
        }

        sb.Append('>').Append(Xl.Esc(formula)).Append("</").Append(pc).Append("definedName>");
        MarkupNode c = Container;
        if (c.IsNull)
        {
            Xl.InsertInOrder(_workbook.Tree.Root, WorkbookOrder, "definedNames", "<" + pc + "definedNames>" + sb + "</" + pc + "definedNames>");
        }
        else
        {
            c.AppendXml(Encoding.UTF8.GetBytes(sb.ToString()));
        }
        return Find(name, scope)!;
    }

    /// <summary>Adds a built-in name such as the print area of a sheet. Built-in names are always sheet-scoped.</summary>
    /// <param name="kind">Which built-in name.</param>
    /// <param name="sheet">The sheet.</param>
    /// <param name="formula">The formula text, for example <c>Sheet1!$A$1:$H$40</c>.</param>
    /// <returns>The new name.</returns>
    /// <exception cref="ArgumentException">The kind is <see cref="BuiltInName.None"/>, or the sheet already has that name.</exception>
    public DefinedName AddBuiltIn(BuiltInName kind, Worksheet sheet, string formula)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (kind == BuiltInName.None)
        {
            throw new ArgumentException("Choose a built-in name.", nameof(kind));
        }

        return Add(DefinedName.BuiltInText(kind), formula, sheet, kind == BuiltInName.FilterDatabase);
    }

    /// <summary>Removes a name.</summary>
    /// <param name="name">The name to remove.</param>
    /// <exception cref="ArgumentException">The name has already been removed.</exception>
    public void Remove(DefinedName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!ReferenceEquals(name.Workbook, _workbook))
        {
            throw new ArgumentException("The name belongs to another workbook.", nameof(name));
        }

        MarkupNode node = name.Markup;
        MarkupNode parent = node.Parent;
        if (parent.IsNull)
        {
            throw new ArgumentException("The name has already been removed.", nameof(name));
        }

        node.Remove();
        bool any = false;
        foreach (MarkupNode _ in parent.ChildElements)
        {
            any = true;
            break;
        }

        if (!any)
        {
            // CT_DefinedNames needs at least one definedName.
            parent.Remove();
        }
    }

    /// <summary>Renames a name. Formulas that use the old name are not rewritten.</summary>
    /// <param name="name">The name.</param>
    /// <param name="newName">The new text.</param>
    /// <exception cref="ArgumentException">The new text is not valid or is taken in the same scope.</exception>
    public void Rename(DefinedName name, string newName)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(newName);
        if (!IsValidName(newName, allowBuiltIn: true))
        {
            throw new ArgumentException("'" + newName + "' is not a valid name.", nameof(newName));
        }

        if (Find(newName, name.Scope) is { } other && other.Markup != name.Markup)
        {
            throw new ArgumentException("A name '" + newName + "' already exists in that scope.", nameof(newName));
        }

        name.Markup.SetAttribute("name", newName);
    }

    /// <summary>
    /// Checks a name against Excel's rules: it starts with a letter, underscore or backslash; continues with letters, digits, underscores,
    /// periods, question marks or backslashes; is at most 255 characters; and does not look like a cell reference (<c>A1</c>, <c>XFD1048576</c>,
    /// <c>R1C1</c>, <c>R</c>, <c>C</c>).
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="allowBuiltIn">Accept the <c>_xlnm.</c> built-in names.</param>
    /// <returns>True when Excel would accept the name.</returns>
    public static bool IsValidName(string name, bool allowBuiltIn = false)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 255)
        {
            return false;
        }

        if (!(char.IsLetter(name[0]) || name[0] is '_' or '\\'))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '.' or '?' or '\\'))
            {
                return false;
            }
        }

        if (allowBuiltIn && name.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !LooksLikeReference(name);
    }

    private static bool LooksLikeReference(string s)
    {
        // A1 style: 1–3 letters up to XFD, then digits only, row 1..1048576.
        int i = 0;
        while (i < s.Length && i < 4 && s[i] is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
        {
            i++;
        }

        if (i is >= 1 and <= 3 && i < s.Length && CellAddress.TryParseColumn(s.AsSpan(0, i), out _) && AllDigits(s.AsSpan(i)) && long.TryParse(s.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out long row) && row is >= 1 and <= CellAddress.MaxRows)
        {
            return true;
        }

        // R1C1 style: R, C, R<n>, C<n>, R<n>C<n> (n optional, digits only).
        if (s[0] is 'R' or 'r' or 'C' or 'c')
        {
            int j = 1;
            while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
            if (j == s.Length)
            {
                return true;
            }

            if (s[0] is 'R' or 'r' && s[j] is 'C' or 'c')
            {
                int k = j + 1;
                while (k < s.Length && char.IsAsciiDigit(s[k])) k++;
                return k == s.Length;
            }
        }

        return false;
    }

    private static bool AllDigits(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
