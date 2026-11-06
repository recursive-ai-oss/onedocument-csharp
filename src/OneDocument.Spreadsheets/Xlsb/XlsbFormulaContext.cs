using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>The kind of book a <see cref="XlsbSupBook"/> stands for ([MS-XLSB] §2.4.813, §2.4.825, §2.4.826, §2.4.812).</summary>
public enum XlsbSupBookKind
{
    /// <summary>This workbook (<c>BrtSupSelf</c>).</summary>
    Self = 0,

    /// <summary>The same workbook, for names that live in the sheet (<c>BrtSupSame</c>).</summary>
    Same = 1,

    /// <summary>An add-in (<c>BrtSupAddin</c>).</summary>
    AddIn = 2,

    /// <summary>Another workbook (<c>BrtSupBookSrc</c>); its target is a relationship id of the workbook part.</summary>
    Source = 3,
}

/// <summary>One book that formulas reference with a 3-D prefix: this workbook, an add-in or an external workbook.</summary>
public sealed class XlsbSupBook
{
    /// <summary>Creates a supporting book.</summary>
    /// <param name="kind">What it stands for.</param>
    /// <param name="relationshipId">For <see cref="XlsbSupBookKind.Source"/>, the relationship id in the workbook part that leads to the external link part.</param>
    public XlsbSupBook(XlsbSupBookKind kind, string? relationshipId = null)
    {
        Kind = kind;
        RelationshipId = relationshipId;
    }

    /// <summary>Gets what the book stands for.</summary>
    public XlsbSupBookKind Kind { get; }

    /// <summary>Gets the relationship id of an external workbook's link part, or null.</summary>
    public string? RelationshipId { get; }

    /// <summary>Gets the sheet names of an external workbook (from its link part), by sheet index.</summary>
    public List<string> SheetNames { get; } = [];
}

/// <summary>An entry of the extern-sheet table ([MS-XLSB] §2.5.173): a book and a range of its sheets.</summary>
/// <param name="SupBook">Index into <see cref="XlsbFormulaContext.SupBooks"/>.</param>
/// <param name="FirstSheet">Index of the first sheet; -2 is the workbook scope and -1 a deleted sheet.</param>
/// <param name="LastSheet">Index of the last sheet.</param>
public readonly record struct XlsbXti(int SupBook, int FirstSheet, int LastSheet);

/// <summary>A defined name as formulas address it (the <c>BrtName</c> records, in order).</summary>
public sealed class XlsbDefinedName
{
    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the index of the sheet the name is local to, or -1 for a workbook-scoped name.</summary>
    public int SheetIndex { get; set; } = -1;

    /// <summary>Gets or sets a value indicating whether the name stands for a function that is newer than the file format (the <c>_xlfn.</c> names).</summary>
    public bool IsFutureFunction { get; set; }
}

/// <summary>What a structured reference needs to know about a table.</summary>
/// <param name="Name">The table name.</param>
/// <param name="Columns">The column names in order.</param>
public sealed record XlsbTableInfo(string Name, IReadOnlyList<string> Columns);

/// <summary>
/// The workbook-wide facts a binary formula refers to by index: sheet names, the extern-sheet table, defined names and tables.
/// Decoding reads them; encoding looks entries up and appends what is missing, so the workbook part can be written afterwards
/// from the grown lists. One context serves every formula of a workbook.
/// </summary>
public sealed class XlsbFormulaContext
{
    /// <summary>Gets the sheet names in tab order.</summary>
    public List<string> SheetNames { get; } = [];

    /// <summary>Gets the books formulas reference.</summary>
    public List<XlsbSupBook> SupBooks { get; } = [];

    /// <summary>Gets the extern-sheet table (<c>BrtExternSheet</c>).</summary>
    public List<XlsbXti> Xtis { get; } = [];

    /// <summary>Gets the defined names in the order of the <c>BrtName</c> records; a formula refers to one by its one-based position.</summary>
    public List<XlsbDefinedName> Names { get; } = [];

    /// <summary>Gets or sets the lookup of a table by its numeric id (the <c>idList</c> of the table part), used by structured references.</summary>
    public Func<int, XlsbTableInfo?>? TableById { get; set; }

    /// <summary>Gets or sets the lookup of a table's numeric id by name.</summary>
    public Func<string, int>? TableIdByName { get; set; }

    /// <summary>Gets a value indicating whether the extern-sheet table or the name list grew while encoding.</summary>
    public bool Grew { get; internal set; }

    /// <summary>Gets the number of formulas that could not be encoded exactly, with the first reason, for the loss report.</summary>
    public int ApproximatedCount { get; internal set; }

    internal int EnsureSelfSupBook()
    {
        for (int i = 0; i < SupBooks.Count; i++)
        {
            if (SupBooks[i].Kind == XlsbSupBookKind.Self)
            {
                return i;
            }
        }

        SupBooks.Add(new XlsbSupBook(XlsbSupBookKind.Self));
        Grew = true;
        return SupBooks.Count - 1;
    }

    internal int EnsureXti(XlsbXti xti)
    {
        int i = Xtis.IndexOf(xti);
        if (i >= 0)
        {
            return i;
        }

        Xtis.Add(xti);
        Grew = true;
        return Xtis.Count - 1;
    }

    internal int EnsureName(string name, int sheetIndex, bool future)
    {
        for (int i = 0; i < Names.Count; i++)
        {
            if (Names[i].SheetIndex == sheetIndex && string.Equals(Names[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        Names.Add(new XlsbDefinedName { Name = name, SheetIndex = sheetIndex, IsFutureFunction = future });
        Grew = true;
        return Names.Count;
    }

    internal int FindName(string name, int sheetIndex)
    {
        for (int i = 0; i < Names.Count; i++)
        {
            if (Names[i].SheetIndex == sheetIndex && string.Equals(Names[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        return 0;
    }

    internal SheetPrefix? PrefixOf(int ixti, out bool isError)
    {
        isError = false;
        if ((uint)ixti >= (uint)Xtis.Count)
        {
            isError = true;
            return null;
        }

        XlsbXti x = Xtis[ixti];
        if ((uint)x.SupBook >= (uint)SupBooks.Count)
        {
            isError = true;
            return null;
        }

        XlsbSupBook book = SupBooks[x.SupBook];
        if (book.Kind is XlsbSupBookKind.Same or XlsbSupBookKind.Self && x.FirstSheet == -2)
        {
            return null;
        }

        if (x.FirstSheet < 0)
        {
            isError = true;
            return null;
        }

        string? workbook = null;
        List<string> names = SheetNames;
        if (book.Kind == XlsbSupBookKind.Source)
        {
            int ordinal = 0;
            for (int i = 0; i <= x.SupBook; i++)
            {
                if (SupBooks[i].Kind == XlsbSupBookKind.Source)
                {
                    ordinal++;
                }
            }

            workbook = ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            names = book.SheetNames;
        }
        else if (book.Kind == XlsbSupBookKind.AddIn)
        {
            return null;
        }

        string first = x.FirstSheet < names.Count ? names[x.FirstSheet] : "#REF";
        if (x.LastSheet == x.FirstSheet || x.LastSheet < 0)
        {
            return new SheetPrefix(first, null, workbook);
        }

        string last = x.LastSheet < names.Count ? names[x.LastSheet] : "#REF";
        return new SheetPrefix(first, last, workbook);
    }

    internal int XtiOf(SheetPrefix? prefix)
    {
        if (prefix is null)
        {
            return EnsureXti(new XlsbXti(EnsureSelfSupBook(), -2, -2));
        }

        int book;
        List<string> names;
        if (prefix.Workbook is not null && int.TryParse(prefix.Workbook, out int ordinal) && ordinal >= 1)
        {
            book = -1;
            int seen = 0;
            for (int i = 0; i < SupBooks.Count; i++)
            {
                if (SupBooks[i].Kind == XlsbSupBookKind.Source && ++seen == ordinal)
                {
                    book = i;
                    break;
                }
            }

            if (book < 0)
            {
                return 0xFFFF;
            }

            names = SupBooks[book].SheetNames;
        }
        else
        {
            book = EnsureSelfSupBook();
            names = SheetNames;
        }

        int first = IndexOf(names, prefix.Sheet);
        int last = prefix.LastSheet is null ? first : IndexOf(names, prefix.LastSheet);
        if (first < 0 || last < 0)
        {
            if (book >= 0 && SupBooks[book].Kind == XlsbSupBookKind.Source)
            {
                first = Add(names, prefix.Sheet, first);
                last = prefix.LastSheet is null ? first : Add(names, prefix.LastSheet, last);
            }
            else
            {
                return EnsureXti(new XlsbXti(book, -1, -1));
            }
        }

        return EnsureXti(new XlsbXti(book, first, last));
    }

    private static int IndexOf(List<string> names, string? name)
    {
        if (name is null)
        {
            return -1;
        }

        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int Add(List<string> names, string? name, int existing)
    {
        if (existing >= 0 || name is null)
        {
            return existing;
        }

        names.Add(name);
        Grew = true;
        return names.Count - 1;
    }
}
