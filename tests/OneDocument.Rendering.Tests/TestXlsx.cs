using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Builds small XLSX files in memory for the print-rendering tests and the Excel differential. Every number in the cases is chosen so the
// expected page geometry follows from the page setup: Calibri 11 (maximum digit width 7 px), default column 64 px = 48 pt, default row 15 pt.
internal static class TestXlsx
{
    internal const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    internal const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // Cell format indexes of Styles.
    internal const int Normal = 0, Bold = 1, Yellow = 2, Box = 3, Money = 4, Percent = 5, Date = 6, Wrap = 7, Centre = 8, Right = 9, Rot45 = 10, Rot90 = 11,
        Stacked = 12, Shrink = 13, Indent = 14, FillAlign = 15, RedFont = 16, ThickDouble = 17, IsoDate = 18, NegRed = 19, StarFill = 20, CentreContinuous = 21,
        Justify = 22, Diagonal = 23, Large = 24, Gradient = 25, MediumBox = 26, Top = 27, MiddleCentre = 28, DottedBox = 29, Hatch = 30, Underline = 31;

    internal const string Styles =
        "<styleSheet xmlns=\"" + Main + "\">" +
        "<numFmts count=\"3\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\"/><numFmt numFmtId=\"165\" formatCode=\"[Red]#,##0;[Red]\\-#,##0\"/><numFmt numFmtId=\"166\" formatCode=\"0.00*-\"/></numFmts>" +
        "<fonts count=\"6\">" +
        "<font><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><b/><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><sz val=\"11\"/><color rgb=\"FFC00000\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><b/><i/><u/><sz val=\"14\"/><color rgb=\"FF1F4E79\"/><name val=\"Arial\"/><family val=\"2\"/></font>" +
        "<font><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/><u/></font>" +
        "<font><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/><strike/></font>" +
        "</fonts>" +
        "<fills count=\"5\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFFF00\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "<fill><gradientFill degree=\"90\"><stop position=\"0\"><color rgb=\"FFFFFFFF\"/></stop><stop position=\"1\"><color rgb=\"FF4472C4\"/></stop></gradientFill></fill>" +
        "<fill><patternFill patternType=\"lightGrid\"><fgColor rgb=\"FF000000\"/><bgColor rgb=\"FFFFFFFF\"/></patternFill></fill></fills>" +
        "<borders count=\"6\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
        "<border><left style=\"thin\"><color auto=\"1\"/></left><right style=\"thin\"><color auto=\"1\"/></right><top style=\"thin\"><color auto=\"1\"/></top><bottom style=\"thin\"><color auto=\"1\"/></bottom><diagonal/></border>" +
        "<border><left style=\"thick\"><color rgb=\"FFFF0000\"/></left><right style=\"double\"><color rgb=\"FF0000FF\"/></right><top style=\"dashed\"><color auto=\"1\"/></top><bottom style=\"dotted\"><color auto=\"1\"/></bottom><diagonal/></border>" +
        "<border diagonalUp=\"1\" diagonalDown=\"1\"><left/><right/><top/><bottom/><diagonal style=\"thin\"><color auto=\"1\"/></diagonal></border>" +
        "<border><left style=\"medium\"><color auto=\"1\"/></left><right style=\"medium\"><color auto=\"1\"/></right><top style=\"medium\"><color auto=\"1\"/></top><bottom style=\"medium\"><color auto=\"1\"/></bottom><diagonal/></border>" +
        "<border><left style=\"dotted\"><color auto=\"1\"/></left><right style=\"dotted\"><color auto=\"1\"/></right><top style=\"dashDot\"><color auto=\"1\"/></top><bottom style=\"hair\"><color auto=\"1\"/></bottom><diagonal/></border>" +
        "</borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"32\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +                                                               // 0
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                                // 1 bold
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +                                                // 2 yellow
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\"/>" +                                              // 3 box
        "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +                                        // 4 #,##0.00
        "<xf numFmtId=\"9\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +                                        // 5 0%
        "<xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +                                       // 6 date
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment wrapText=\"1\" vertical=\"top\"/></xf>" + // 7 wrap
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>" +       // 8 centre
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"right\"/></xf>" +        // 9 right
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment textRotation=\"45\"/></xf>" +          // 10
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment textRotation=\"90\" horizontal=\"center\"/></xf>" + // 11
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment textRotation=\"255\"/></xf>" +         // 12
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment shrinkToFit=\"1\"/></xf>" +             // 13
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"left\" indent=\"2\"/></xf>" +  // 14
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"fill\"/></xf>" +            // 15
        "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                                // 16
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"2\" xfId=\"0\" applyBorder=\"1\"/>" +                                              // 17
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +                                      // 18
        "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +                                      // 19
        "<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +                                      // 20
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"centerContinuous\"/></xf>" + // 21
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"justify\" wrapText=\"1\" vertical=\"top\"/></xf>" + // 22
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"3\" xfId=\"0\" applyBorder=\"1\"/>" +                                              // 23
        "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                                // 24
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +                                                // 25
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"4\" xfId=\"0\" applyBorder=\"1\"/>" +                                              // 26
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"top\"/></xf>" +             // 27
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" + // 28
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"5\" xfId=\"0\" applyBorder=\"1\"/>" +                                              // 29
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"4\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +                                                // 30
        "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                                // 31
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "<dxfs count=\"3\"><dxf><font><color rgb=\"FF9C0006\"/></font><fill><patternFill><bgColor rgb=\"FFFFC7CE\"/></patternFill></fill></dxf>" +
        "<dxf><font><b/><color rgb=\"FF006100\"/></font></dxf>" +
        "<dxf><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFEB9C\"/><bgColor rgb=\"FFFFEB9C\"/></patternFill></fill></dxf></dxfs>" +
        "</styleSheet>";

    internal sealed class Sheet(string name)
    {
        public string Name { get; } = name;
        public string State { get; set; } = "visible";
        public Dictionary<(int Row, int Col), (object? Value, int Style)> Cells { get; } = [];
        public Dictionary<int, string> RowAttributes { get; } = [];
        public string? SheetPr { get; set; }
        public string? FormatPr { get; set; }
        public string? Cols { get; set; }
        public string? Merges { get; set; }
        public string? Conditional { get; set; }
        public string? PrintOptions { get; set; }
        public string? PageMargins { get; set; }
        public string? PageSetup { get; set; }
        public string? HeaderFooter { get; set; }
        public string? RowBreaks { get; set; }
        public string? ColBreaks { get; set; }
        public string? Drawing { get; set; }
        public string? DrawingXml { get; set; }

        public Sheet Set(string address, object? value, int style = 0)
        {
            (int r, int c) = Parse(address);
            Cells[(r, c)] = (value, style);
            return this;
        }

        public Sheet Fill(int rows, int cols, Func<int, int, object?> value, int style = 0)
        {
            for (int r = 1; r <= rows; r++)
            {
                for (int c = 1; c <= cols; c++)
                {
                    Cells[(r, c)] = (value(r, c), style);
                }
            }

            return this;
        }
    }

    internal static (int Row, int Col) Parse(string address)
    {
        int i = 0;
        int col = 0;
        while (i < address.Length && char.IsAsciiLetter(address[i]))
        {
            col = (col * 26) + (char.ToUpperInvariant(address[i]) - 'A' + 1);
            i++;
        }

        return (int.Parse(address.AsSpan(i), CultureInfo.InvariantCulture), col);
    }

    internal static string ColumnName(int col)
    {
        string s = string.Empty;
        while (col > 0)
        {
            col--;
            s = (char)('A' + (col % 26)) + s;
            col /= 26;
        }

        return s;
    }

    internal static byte[] Build(IReadOnlyList<Sheet> sheets, string? definedNames = null, string? workbookExtra = null)
    {
        var strings = new List<string>();
        var index = new Dictionary<string, int>();
        int Sst(string s)
        {
            if (!index.TryGetValue(s, out int i))
            {
                index[s] = i = strings.Count;
                strings.Add(s);
            }

            return i;
        }

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var types = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>");
            var wbRels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdS\" Type=\"" + R + "/styles\" Target=\"styles.xml\"/><Relationship Id=\"rIdSS\" Type=\"" + R + "/sharedStrings\" Target=\"sharedStrings.xml\"/>");
            var wbSheets = new StringBuilder();
            for (int s = 0; s < sheets.Count; s++)
            {
                Sheet sh = sheets[s];
                int n = s + 1;
                types.Append("<Override PartName=\"/xl/worksheets/sheet" + n + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                wbRels.Append("<Relationship Id=\"rId" + n + "\" Type=\"" + R + "/worksheet\" Target=\"worksheets/sheet" + n + ".xml\"/>");
                wbSheets.Append("<sheet name=\"" + Esc(sh.Name) + "\" sheetId=\"" + n + "\"" + (sh.State == "visible" ? string.Empty : " state=\"" + sh.State + "\"") + " r:id=\"rId" + n + "\"/>");
                Add(zip, "xl/worksheets/sheet" + n + ".xml", SheetXml(sh, Sst));
                if (sh.DrawingXml is not null)
                {
                    types.Append("<Override PartName=\"/xl/drawings/drawing" + n + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>");
                    Add(zip, "xl/drawings/drawing" + n + ".xml", sh.DrawingXml);
                    Add(zip, "xl/worksheets/_rels/sheet" + n + ".xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdD\" Type=\"" + R + "/drawing\" Target=\"../drawings/drawing" + n + ".xml\"/></Relationships>");
                }
            }

            types.Append("</Types>");
            wbRels.Append("</Relationships>");
            Add(zip, "[Content_Types].xml", types.ToString());
            Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + R + "/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(zip, "xl/_rels/workbook.xml.rels", wbRels.ToString());
            Add(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\"><sheets>" + wbSheets + "</sheets>" +
                (definedNames is null ? string.Empty : "<definedNames>" + definedNames + "</definedNames>") + workbookExtra + "</workbook>");
            Add(zip, "xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + Styles);
            var sst = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><sst xmlns=\"" + Main + "\" count=\"" + strings.Count + "\" uniqueCount=\"" + strings.Count + "\">");
            foreach (string s in strings)
            {
                sst.Append(s.StartsWith("<si>", StringComparison.Ordinal) ? s : "<si><t xml:space=\"preserve\">" + Esc(s) + "</t></si>");
            }

            Add(zip, "xl/sharedStrings.xml", sst.Append("</sst>").ToString());
        }

        return ms.ToArray();
    }

    private static string SheetXml(Sheet sh, Func<string, int> sst)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\">");
        sb.Append(sh.SheetPr).Append(sh.FormatPr).Append(sh.Cols).Append("<sheetData>");
        var byRow = sh.Cells.GroupBy(k => k.Key.Row).ToDictionary(g => g.Key, g => g.OrderBy(k => k.Key.Col).ToList());
        foreach (int rowNumber in byRow.Keys.Union(sh.RowAttributes.Keys).Order())
        {
            sb.Append("<row r=\"").Append(rowNumber).Append('"').Append(sh.RowAttributes.GetValueOrDefault(rowNumber)).Append('>');
            foreach (KeyValuePair<(int Row, int Col), (object? Value, int Style)> c in byRow.GetValueOrDefault(rowNumber) ?? [])
            {
                string r = ColumnName(c.Key.Col) + c.Key.Row.ToString(CultureInfo.InvariantCulture);
                string st = c.Value.Style == 0 ? string.Empty : " s=\"" + c.Value.Style + "\"";
                switch (c.Value.Value)
                {
                    case null: sb.Append("<c r=\"" + r + "\"" + st + "/>"); break;
                    case string s: sb.Append("<c r=\"" + r + "\"" + st + " t=\"s\"><v>" + sst(s) + "</v></c>"); break;
                    case bool b: sb.Append("<c r=\"" + r + "\"" + st + " t=\"b\"><v>" + (b ? 1 : 0) + "</v></c>"); break;
                    case double d: sb.Append("<c r=\"" + r + "\"" + st + "><v>" + d.ToString("R", CultureInfo.InvariantCulture) + "</v></c>"); break;
                    case int i: sb.Append("<c r=\"" + r + "\"" + st + "><v>" + i.ToString(CultureInfo.InvariantCulture) + "</v></c>"); break;
                    case ValueTuple<string, string> err: sb.Append("<c r=\"" + r + "\"" + st + " t=\"e\"><v>" + err.Item1 + "</v></c>"); break;
                    default: throw new InvalidOperationException();
                }
            }

            sb.Append("</row>");
        }

        sb.Append("</sheetData>");
        sb.Append(sh.Merges).Append(sh.Conditional).Append(sh.PrintOptions).Append(sh.PageMargins).Append(sh.PageSetup).Append(sh.HeaderFooter).Append(sh.RowBreaks).Append(sh.ColBreaks);
        if (sh.DrawingXml is not null)
        {
            sb.Append("<drawing r:id=\"rIdD\"/>");
        }

        return sb.Append("</worksheet>").ToString();
    }

    private static string Esc(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static void Add(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(name);
        using Stream s = e.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
