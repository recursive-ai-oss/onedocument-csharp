using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Calc;

/// <summary>
/// Supplies live workbooks for references to other workbooks. Without one, nothing outside the file is read: such references use the values
/// cached in the file's external-link parts. The library never opens a path or an address by itself.
/// </summary>
public interface IExternalWorkbookResolver
{
    /// <summary>Finds the workbook an external link points at.</summary>
    /// <param name="link">The link: its target as the file records it (a path or address) and the sheets the file lists.</param>
    /// <returns>The workbook, or null when it cannot be had; the references then use the cached values. The caller keeps ownership: the library never disposes it. The values read are the cell values the workbook holds, not a recalculation of it.</returns>
    Workbook? Resolve(ExternalLink link);
}

/// <summary>Supplies answers for <c>WEBSERVICE</c> and <c>FILTERXML</c>. Without one both are denied: no request is made, the function gives <c>#VALUE!</c>, and a cell that has a cached value keeps it.</summary>
public interface IWebServiceResolver
{
    /// <summary>Answers <c>WEBSERVICE(url)</c>.</summary>
    /// <param name="url">The address as the formula gives it.</param>
    /// <returns>The response text, or null when it cannot be had (the formula then gives <c>#VALUE!</c>).</returns>
    string? Fetch(string url);

    /// <summary>Answers <c>FILTERXML(xml, xpath)</c>.</summary>
    /// <param name="xml">The XML text.</param>
    /// <param name="xpath">The XPath expression.</param>
    /// <returns>A value or an array of values, or null when it cannot be answered (the formula then gives <c>#VALUE!</c>).</returns>
    CalcValue? FilterXml(string xml, string xpath);
}

/// <summary>
/// A link to another workbook (ECMA-376 Part 1 §18.14, <c>externalLink</c>): where it points, the sheets of it the file knows, and the
/// values of the cells the formulas read, cached when the file was last calculated. <c>[1]Sheet1!A1</c> in a formula refers to link 1.
/// </summary>
public sealed class ExternalLink
{
    private readonly Dictionary<(int Sheet, int Row, int Column), CalcValue> _cells = [];
    private readonly MarkupDocument? _tree;
    private readonly string _partName;

    internal ExternalLink(int index, string partName, string target, string[] sheetNames, MarkupDocument? tree)
    {
        Index = index;
        _partName = partName;
        Target = target;
        SheetNames = sheetNames;
        _tree = tree;
    }

    /// <summary>Gets the link's number as formulas write it (<c>[1]</c>): its position in the workbook's list of external references, from 1.</summary>
    public int Index { get; }

    /// <summary>Gets the target as the file records it (a path or address, often <c>file:///…</c>), or an empty string when the file gives none. It is never opened by the library.</summary>
    public string Target { get; }

    /// <summary>Gets the names of the other workbook's sheets, in its order, as the file lists them.</summary>
    public IReadOnlyList<string> SheetNames { get; }

    /// <summary>Gets the number of cells whose values the file caches.</summary>
    public int CachedCellCount => _cells.Count;

    internal bool Dirty { get; private set; }

    internal string PartName => _partName;

    internal int SheetIndex(string? name)
    {
        if (name is null)
        {
            return -1;
        }

        for (int i = 0; i < SheetNames.Count; i++)
        {
            if (string.Equals(SheetNames[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    internal bool TryGetCached(int sheet, int row, int column, out CalcValue value) => _cells.TryGetValue((sheet, row, column), out value);

    internal void SetCached(int sheet, int row, int column, in CalcValue value)
    {
        if (_cells.TryGetValue((sheet, row, column), out CalcValue old) && old.Equals(value))
        {
            return;
        }

        _cells[(sheet, row, column)] = value;
        Dirty = true;
    }

    internal bool HasCachedSheet(int sheet)
    {
        foreach ((int s, int _, int _) in _cells.Keys)
        {
            if (s == sheet)
            {
                return true;
            }
        }

        return false;
    }

    internal void CachedExtent(int sheet, out int lastRow, out int lastColumn)
    {
        lastRow = 1;
        lastColumn = 1;
        foreach ((int s, int r, int c) in _cells.Keys)
        {
            if (s == sheet)
            {
                lastRow = Math.Max(lastRow, r);
                lastColumn = Math.Max(lastColumn, c);
            }
        }
    }

    // ---- reading the part -------------------------------------------------------------------------------------------------------

    internal static ExternalLink Read(int index, string partName, ReadOnlyMemory<byte> bytes, string target, OpenContext context)
    {
        if (!MarkupDocument.TryParse(bytes, out MarkupDocument? doc, context, partName) || doc is null)
        {
            Report(context, partName, "The external link part is not well-formed XML; its cached values are ignored.");
            return new ExternalLink(index, partName, target, [], null);
        }

        MarkupNode book = default;
        foreach (MarkupNode c in doc.Root.ChildElements)
        {
            if (c.LocalName.SequenceEqual("externalBook"u8))
            {
                book = c;
                break;
            }
        }

        if (book.IsNull)
        {
            // An OLE or DDE link: nothing a formula can read as cells.
            return new ExternalLink(index, partName, target, [], doc);
        }

        var names = new List<string>();
        MarkupNode dataSet = default;
        foreach (MarkupNode c in book.ChildElements)
        {
            if (c.LocalName.SequenceEqual("sheetNames"u8))
            {
                foreach (MarkupNode n in c.ChildElements)
                {
                    names.Add(n.GetAttribute("val") ?? string.Empty);
                }
            }
            else if (c.LocalName.SequenceEqual("sheetDataSet"u8))
            {
                dataSet = c;
            }
        }

        var link = new ExternalLink(index, partName, target, [.. names], doc);
        if (!dataSet.IsNull)
        {
            link.ReadCells(dataSet, context);
        }

        return link;
    }

    private void ReadCells(MarkupNode dataSet, OpenContext context)
    {
        foreach (MarkupNode sheetData in dataSet.ChildElements)
        {
            if (!int.TryParse(sheetData.GetAttribute("sheetId"), NumberStyles.None, CultureInfo.InvariantCulture, out int sheet) || sheet < 0)
            {
                continue;
            }

            foreach (MarkupNode row in sheetData.ChildElements)
            {
                foreach (MarkupNode cell in row.ChildElements)
                {
                    if (!CellAddress.TryParse(cell.GetAttribute("r"), out CellAddress at))
                    {
                        continue;
                    }

                    _cells[(sheet, at.Row, at.Column)] = ReadValue(cell);
                }
            }
        }
    }

    // ECMA-376 Part 1 §18.14.2 (CT_ExternalCell): t is n (default), b, e or str; a cell without v is blank.
    private static CalcValue ReadValue(MarkupNode cell)
    {
        string? text = null;
        foreach (MarkupNode c in cell.ChildElements)
        {
            if (c.LocalName.SequenceEqual("v"u8))
            {
                text = c.InnerText;
                break;
            }
        }

        if (text is null)
        {
            return CalcValue.Blank;
        }

        switch (cell.GetAttribute("t"))
        {
            case "str":
                return CalcValue.FromText(text);
            case "b":
                return CalcValue.FromBoolean(text is "1" or "true");
            case "e":
                return CalcErrors.TryParse(text, out CalcError e) ? CalcValue.FromError(e) : CalcValue.FromError(CalcError.Ref);
            default:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? CalcValue.FromNumber(d) : CalcValue.Blank;
        }
    }

    private static void Report(OpenContext context, string part, string message) =>
        context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.external-link-unreadable", DefectSeverity.Dropped, message, DefectLocation.InPart(part)));

    // ---- writing the cache back -------------------------------------------------------------------------------------------------

    /// <summary>Rewrites the <c>sheetDataSet</c> of the part from the cache, leaving everything else in the part as it was.</summary>
    internal void Commit(IOpcPackage package)
    {
        if (!Dirty || _tree is null || package.FindPart(_partName) is not { } part)
        {
            return;
        }

        MarkupNode book = default;
        MarkupNode old = default;
        foreach (MarkupNode c in _tree.Root.ChildElements)
        {
            if (c.LocalName.SequenceEqual("externalBook"u8))
            {
                book = c;
            }
        }

        if (book.IsNull)
        {
            return;
        }

        foreach (MarkupNode c in book.ChildElements)
        {
            if (c.LocalName.SequenceEqual("sheetDataSet"u8))
            {
                old = c;
            }
        }

        string p = book.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(book.Prefix) + ":";
        var sb = new StringBuilder();
        sb.Append('<').Append(p).Append("sheetDataSet>");
        var keys = new List<(int Sheet, int Row, int Column)>(_cells.Keys);
        keys.Sort();
        int currentSheet = -1;
        int currentRow = -1;
        foreach ((int sheet, int row, int column) in keys)
        {
            if (sheet != currentSheet)
            {
                if (currentRow >= 0)
                {
                    sb.Append("</").Append(p).Append("row>");
                }

                if (currentSheet >= 0)
                {
                    sb.Append("</").Append(p).Append("sheetData>");
                }

                sb.Append('<').Append(p).Append("sheetData sheetId=\"").Append(sheet.ToString(CultureInfo.InvariantCulture)).Append("\">");
                currentSheet = sheet;
                currentRow = -1;
            }

            if (row != currentRow)
            {
                if (currentRow >= 0)
                {
                    sb.Append("</").Append(p).Append("row>");
                }

                sb.Append('<').Append(p).Append("row r=\"").Append(row.ToString(CultureInfo.InvariantCulture)).Append("\">");
                currentRow = row;
            }

            AppendCell(sb, p, new CellAddress(row, column).ToString(), _cells[(sheet, row, column)]);
        }

        if (currentRow >= 0)
        {
            sb.Append("</").Append(p).Append("row>");
        }

        if (currentSheet >= 0)
        {
            sb.Append("</").Append(p).Append("sheetData>");
        }

        sb.Append("</").Append(p).Append("sheetDataSet>");
        byte[] xml = Encoding.UTF8.GetBytes(sb.ToString());
        if (old.IsNull)
        {
            book.AppendXml(xml);
        }
        else
        {
            old.ReplaceWithXml(xml);
        }

        part.SetBytes(_tree.ToArray());
        Dirty = false;
    }

    private static void AppendCell(StringBuilder sb, string p, string address, in CalcValue v)
    {
        sb.Append('<').Append(p).Append("cell r=\"").Append(address).Append('"');
        switch (v.Kind)
        {
            case ValueKind.Number:
                sb.Append("><").Append(p).Append('v').Append('>').Append(v.Number.ToString("R", CultureInfo.InvariantCulture)).Append("</").Append(p).Append("v></").Append(p).Append("cell>");
                break;
            case ValueKind.Boolean:
                sb.Append(" t=\"b\"><").Append(p).Append("v>").Append(v.Boolean ? '1' : '0').Append("</").Append(p).Append("v></").Append(p).Append("cell>");
                break;
            case ValueKind.Text:
                sb.Append(" t=\"str\"><").Append(p).Append("v>").Append(Escape(v.Text)).Append("</").Append(p).Append("v></").Append(p).Append("cell>");
                break;
            case ValueKind.Error:
                sb.Append(" t=\"e\"><").Append(p).Append("v>").Append(CalcErrors.ToText(v.Error)).Append("</").Append(p).Append("v></").Append(p).Append("cell>");
                break;
            default:
                sb.Append("/>");
                break;
        }
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                default:
                    if (c >= ' ' || c is '\t' or '\n' or '\r')
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }
}
