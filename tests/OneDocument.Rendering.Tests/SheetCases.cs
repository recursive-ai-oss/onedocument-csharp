using System.Globalization;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// The generated worksheets the print-rendering tests use and the Excel differential prints. One case per feature, small enough that the
// expected pagination follows from the page setup by arithmetic.
public static class SheetCases
{
    private static readonly Lazy<IReadOnlyList<(string Name, byte[] Xlsx)>> Cached = new(Build);

    public static IReadOnlyList<(string Name, byte[] Xlsx)> All() => Cached.Value;

    private static string Margins(double l = 0.7, double r = 0.7, double t = 0.75, double b = 0.75, double h = 0.3, double f = 0.3) =>
        string.Create(CultureInfo.InvariantCulture, $"<pageMargins left=\"{l}\" right=\"{r}\" top=\"{t}\" bottom=\"{b}\" header=\"{h}\" footer=\"{f}\"/>");

    private static string PageSetup(string attributes) => "<pageSetup " + attributes + "/>";

    private static string Name(string name, string reference, int? sheet = 0) =>
        "<definedName name=\"" + name + "\"" + (sheet is { } s ? " localSheetId=\"" + s + "\"" : string.Empty) + ">" + reference + "</definedName>";

    private static TestXlsx.Sheet Grid(string name, int rows, int cols, bool header = true)
    {
        var s = new TestXlsx.Sheet(name);
        s.Fill(rows, cols, (r, c) => header && r == 1 ? "Col " + TestXlsx.ColumnName(c) : r * 100 + c);
        if (header)
        {
            for (int c = 1; c <= cols; c++)
            {
                s.Cells[(1, c)] = ("Col " + TestXlsx.ColumnName(c), TestXlsx.Bold);
            }
        }

        return s;
    }

    private static List<(string Name, byte[] Xlsx)> Build()
    {
        var list = new List<(string Name, byte[] Xlsx)>();
        void Add(string name, TestXlsx.Sheet sheet, string? names = null) => list.Add((name, TestXlsx.Build([sheet], names)));
        void AddMany(string name, IReadOnlyList<TestXlsx.Sheet> sheets, string? names = null) => list.Add((name, TestXlsx.Build(sheets, names)));

        // Pagination.
        Add("x01-basic", Grid("Basic", 12, 5).Set("A14", "Total", TestXlsx.Bold).Set("B14", 1234.5, TestXlsx.Money));
        {
            TestXlsx.Sheet s = Grid("Area", 40, 10);
            s.PageMargins = Margins();
            Add("x02-print-area", s, Name("_xlnm.Print_Area", "Area!$C$3:$F$12"));
        }

        {
            TestXlsx.Sheet s = Grid("Titles", 150, 4);
            s.Cells[(2, 1)] = ("Second title row", TestXlsx.Bold);
            Add("x03-titles", s, Name("_xlnm.Print_Titles", "Titles!$1:$2"));
        }

        {
            TestXlsx.Sheet s = Grid("Wide", 40, 30);
            s.SheetPr = "<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>";
            s.PageSetup = PageSetup("fitToWidth=\"1\" fitToHeight=\"0\" orientation=\"landscape\"");
            Add("x04-fit-width", s);
        }

        {
            TestXlsx.Sheet s = Grid("FitOne", 60, 20);
            s.SheetPr = "<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>";
            s.PageSetup = PageSetup("fitToWidth=\"1\" fitToHeight=\"1\"");
            Add("x05-fit-one", s);
        }

        {
            TestXlsx.Sheet s = Grid("Scale", 100, 6);
            s.PageSetup = PageSetup("scale=\"50\"");
            Add("x06-scale-50", s);
        }

        {
            TestXlsx.Sheet s = Grid("Breaks", 40, 8);
            s.RowBreaks = "<rowBreaks count=\"1\" manualBreakCount=\"1\"><brk id=\"10\" max=\"16383\" man=\"1\"/></rowBreaks>";
            s.ColBreaks = "<colBreaks count=\"1\" manualBreakCount=\"1\"><brk id=\"3\" max=\"1048575\" man=\"1\"/></colBreaks>";
            Add("x07-manual-breaks", s);
        }

        {
            TestXlsx.Sheet s = Grid("Order", 120, 30);
            s.PageSetup = PageSetup("pageOrder=\"overThenDown\"");
            Add("x08-over-then-down", s);
        }

        {
            TestXlsx.Sheet s = Grid("A4", 20, 6);
            s.PageSetup = PageSetup("paperSize=\"9\" orientation=\"landscape\"");
            s.PrintOptions = "<printOptions horizontalCentered=\"1\" verticalCentered=\"1\"/>";
            Add("x09-a4-landscape-centred", s);
        }

        {
            TestXlsx.Sheet s = Grid("Hidden", 20, 8);
            s.RowAttributes[5] = " hidden=\"1\"";
            s.RowAttributes[6] = " ht=\"30\" customHeight=\"1\"";
            s.Cols = "<cols><col min=\"3\" max=\"4\" width=\"9.140625\" hidden=\"1\" customWidth=\"1\"/><col min=\"6\" max=\"6\" width=\"20.7109375\" customWidth=\"1\"/></cols>";
            Add("x10-hidden-and-custom-sizes", s);
        }

        // Headers and footers.
        {
            TestXlsx.Sheet s = Grid("Header", 100, 5);
            s.HeaderFooter = "<headerFooter><oddHeader>&amp;L&amp;\"Arial,Bold\"&amp;14Quarterly report&amp;C&amp;A&amp;R&amp;D</oddHeader><oddFooter>&amp;LFile &amp;F&amp;CPage &amp;P of &amp;N&amp;RSheet &amp;A</oddFooter></headerFooter>";
            Add("x11-header-footer", s);
        }

        {
            TestXlsx.Sheet s = Grid("OddEven", 130, 3);
            s.HeaderFooter = "<headerFooter differentOddEven=\"1\" differentFirst=\"1\"><oddHeader>&amp;COdd page &amp;P</oddHeader><evenHeader>&amp;CEven page &amp;P</evenHeader><firstHeader>&amp;CFirst page</firstHeader><oddFooter>&amp;RodF</oddFooter><evenFooter>&amp;LevF</evenFooter></headerFooter>";
            Add("x12-first-odd-even", s);
        }

        // Cell layout.
        {
            var s = new TestXlsx.Sheet("Alignment");
            s.Cols = "<cols><col min=\"1\" max=\"1\" width=\"28.7109375\" customWidth=\"1\"/><col min=\"2\" max=\"3\" width=\"18.7109375\" customWidth=\"1\"/></cols>";
            s.Set("A1", "General text", 0).Set("B1", 12345.678, 0).Set("C1", true, 0);
            s.Set("A2", "Centre", TestXlsx.Centre).Set("B2", "Right", TestXlsx.Right).Set("C2", "Indent 2", TestXlsx.Indent);
            s.Set("A3", "A long sentence that has to wrap onto several lines inside its cell", TestXlsx.Wrap);
            s.Set("B3", "Rotated 45", TestXlsx.Rot45).Set("C3", "Rotated 90", TestXlsx.Rot90);
            s.Set("A4", "Stacked", TestXlsx.Stacked).Set("B4", "Shrink this text so it fits the column", TestXlsx.Shrink).Set("C4", "ab", TestXlsx.FillAlign);
            s.Set("A5", "Centred across", TestXlsx.CentreContinuous).Set("B5", null, TestXlsx.CentreContinuous).Set("C5", null, TestXlsx.CentreContinuous);
            s.Set("A6", "Justified text that wraps over a few lines, spread out to both edges of the cell", TestXlsx.Justify);
            s.Set("B6", "Top", TestXlsx.Top).Set("C6", "Middle", TestXlsx.MiddleCentre);
            s.RowAttributes[3] = " ht=\"60\" customHeight=\"1\"";
            s.RowAttributes[4] = " ht=\"110\" customHeight=\"1\"";
            s.RowAttributes[6] = " ht=\"60\" customHeight=\"1\"";
            Add("x13-alignment", s);
        }

        {
            var s = new TestXlsx.Sheet("Numbers");
            s.Cols = "<cols><col min=\"1\" max=\"1\" width=\"14.7109375\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"6.7109375\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\"14.7109375\" customWidth=\"1\"/></cols>";
            s.Set("A1", 1234567.891, TestXlsx.Money).Set("B1", 1234567.891, TestXlsx.Money).Set("C1", 0.256, TestXlsx.Percent);
            s.Set("A2", 45000.5, TestXlsx.Date).Set("B2", 45000.5, TestXlsx.IsoDate).Set("C2", -1234.5, TestXlsx.NegRed);
            s.Set("A3", 3.14159265358979, 0).Set("B3", 3.14159265358979, 0).Set("C3", 12.5, TestXlsx.StarFill);
            s.Set("A4", 123456789012.0, 0).Set("B4", 123456789012.0, 0).Set("C4", 0.000000123, 0);
            s.Set("A5", true, 0).Set("B5", ("#DIV/0!", string.Empty), 0).Set("C5", ("#N/A", string.Empty), 0);
            s.PageSetup = PageSetup("errors=\"dash\"");
            Add("x14-numbers-and-errors", s);
        }

        {
            var s = new TestXlsx.Sheet("Overflow");
            s.Cols = "<cols><col min=\"1\" max=\"6\" width=\"9.140625\" customWidth=\"1\"/></cols>";
            s.Set("A1", "This left text runs over the empty cells on its right");
            s.Set("A2", "Blocked by its neighbour overflowing text").Set("C2", "X");
            s.Set("F3", "Right aligned text runs left over empty cells", TestXlsx.Right);
            s.Set("C4", "Centred text spills to both sides evenly", TestXlsx.Centre);
            s.Set("A5", "Wrapped text does not overflow but wraps in the cell width", TestXlsx.Wrap);
            s.RowAttributes[5] = " ht=\"45\" customHeight=\"1\"";
            Add("x15-overflow", s);
        }

        {
            var s = new TestXlsx.Sheet("Merge");
            s.Merges = "<mergeCells count=\"3\"><mergeCell ref=\"B2:D3\"/><mergeCell ref=\"F2:G2\"/><mergeCell ref=\"B5:C8\"/></mergeCells>";
            s.Set("B2", "Merged block", TestXlsx.MiddleCentre).Set("F2", "Header", TestXlsx.Yellow);
            s.Set("G2", null, TestXlsx.Box).Set("C2", null, TestXlsx.Box).Set("B3", null, TestXlsx.Box);
            s.Set("B5", "Tall merged cell with wrapped text", TestXlsx.Wrap).Set("D5", 1).Set("D6", 2);
            Add("x16-merged-cells", s);
        }

        {
            var s = new TestXlsx.Sheet("Borders");
            s.Cols = "<cols><col min=\"2\" max=\"7\" width=\"11.7109375\" customWidth=\"1\"/></cols>";
            s.Set("B2", "thin", TestXlsx.Box).Set("C2", "mixed", TestXlsx.ThickDouble).Set("D2", "diagonal", TestXlsx.Diagonal);
            s.Set("B4", "medium", TestXlsx.MediumBox).Set("C4", "medium", TestXlsx.MediumBox);
            s.Set("D4", "dotted/dashDot", TestXlsx.DottedBox).Set("B6", "thin", TestXlsx.Box).Set("B7", "medium", TestXlsx.MediumBox);
            Add("x17-borders", s);
        }

        {
            var s = new TestXlsx.Sheet("Fills");
            s.Set("A1", "solid", TestXlsx.Yellow).Set("B1", "gradient", TestXlsx.Gradient).Set("C1", "hatch", TestXlsx.Hatch).Set("D1", "underline", TestXlsx.Underline);
            s.Set("A2", "Big Arial", TestXlsx.Large);
            s.RowAttributes[2] = " ht=\"24\" customHeight=\"1\"";
            Add("x18-fills-and-fonts", s);
        }

        {
            TestXlsx.Sheet s = Grid("Grid", 15, 6);
            s.PrintOptions = "<printOptions headings=\"1\" gridLines=\"1\"/>";
            Add("x19-gridlines-headings", s);
        }

        // Conditional formats.
        {
            TestXlsx.Sheet s = Grid("CfBasic", 12, 4, header: false);
            s.Conditional =
                "<conditionalFormatting sqref=\"A1:A12\"><cfRule type=\"cellIs\" dxfId=\"0\" priority=\"1\" operator=\"greaterThan\"><formula>600</formula></cfRule></conditionalFormatting>" +
                "<conditionalFormatting sqref=\"B1:B12\"><cfRule type=\"expression\" dxfId=\"1\" priority=\"2\"><formula>MOD(ROW(),2)=0</formula></cfRule></conditionalFormatting>" +
                "<conditionalFormatting sqref=\"C1:C12\"><cfRule type=\"top10\" dxfId=\"2\" priority=\"3\" rank=\"3\"/></conditionalFormatting>" +
                "<conditionalFormatting sqref=\"D1:D12\"><cfRule type=\"aboveAverage\" dxfId=\"0\" priority=\"4\"/></conditionalFormatting>";
            Add("x20-cf-formats", s);
        }

        {
            var s = new TestXlsx.Sheet("CfBars");
            s.Cols = "<cols><col min=\"1\" max=\"2\" width=\"18.7109375\" customWidth=\"1\"/></cols>";
            double[] values = [5, 12, 20, 32, 45, 50, 8];
            for (int i = 0; i < values.Length; i++)
            {
                s.Set("A" + (i + 1), values[i]).Set("B" + (i + 1), values[i] - 25);
            }

            s.Conditional =
                "<conditionalFormatting sqref=\"A1:A7\"><cfRule type=\"dataBar\" priority=\"1\"><dataBar><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FF638EC6\"/></dataBar></cfRule></conditionalFormatting>" +
                "<conditionalFormatting sqref=\"B1:B7\"><cfRule type=\"dataBar\" priority=\"2\"><dataBar><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FF63C384\"/></dataBar></cfRule></conditionalFormatting>";
            Add("x21-cf-databars", s);
        }

        {
            var s = new TestXlsx.Sheet("CfScale");
            for (int i = 1; i <= 10; i++)
            {
                s.Set("A" + i, i * 10).Set("B" + i, i * 10);
            }

            s.Conditional =
                "<conditionalFormatting sqref=\"A1:A10\"><cfRule type=\"colorScale\" priority=\"1\"><colorScale><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FFF8696B\"/><color rgb=\"FF63BE7B\"/></colorScale></cfRule></conditionalFormatting>" +
                "<conditionalFormatting sqref=\"B1:B10\"><cfRule type=\"colorScale\" priority=\"2\"><colorScale><cfvo type=\"min\"/><cfvo type=\"percentile\" val=\"50\"/><cfvo type=\"max\"/><color rgb=\"FFF8696B\"/><color rgb=\"FFFFEB84\"/><color rgb=\"FF63BE7B\"/></colorScale></cfRule></conditionalFormatting>";
            Add("x22-cf-colorscales", s);
        }

        {
            var s = new TestXlsx.Sheet("CfIcons");
            string[] sets = ["3Arrows", "3ArrowsGray", "3Flags", "3TrafficLights1", "3TrafficLights2", "3Signs", "3Symbols", "3Symbols2", "3Stars", "3Triangles", "4Arrows", "4ArrowsGray", "4RedToBlack", "4Rating", "4TrafficLights", "5Arrows", "5ArrowsGray", "5Rating", "5Quarters", "5Boxes"];
            var cf = new System.Text.StringBuilder();
            for (int c = 0; c < sets.Length; c++)
            {
                int n = sets[c][0] - '0';
                string col = TestXlsx.ColumnName(c + 1);
                for (int r = 1; r <= n; r++)
                {
                    s.Set(col + r, (r - 1) * (100.0 / n) + 1);
                }

                string cfvos = string.Concat(Enumerable.Range(0, n).Select(i => "<cfvo type=\"percent\" val=\"" + (int)(i * 100.0 / n) + "\"/>"));
                cf.Append("<conditionalFormatting sqref=\"" + col + "1:" + col + n + "\"><cfRule type=\"iconSet\" priority=\"" + (c + 1) + "\"><iconSet iconSet=\"" + sets[c] + "\">" + cfvos + "</iconSet></cfRule></conditionalFormatting>");
            }

            s.Conditional = cf.ToString();
            s.Cols = "<cols><col min=\"1\" max=\"20\" width=\"7.7109375\" customWidth=\"1\"/></cols>";
            s.PageSetup = PageSetup("orientation=\"landscape\"");
            Add("x23-cf-iconsets", s);
        }

        {
            TestXlsx.Sheet s = Grid("CfDup", 10, 2, header: false);
            for (int r = 1; r <= 10; r++)
            {
                s.Cells[(r, 1)] = (r % 3, 0);
                s.Cells[(r, 2)] = ("item " + (r % 4), 0);
            }

            s.Conditional =
                "<conditionalFormatting sqref=\"A1:A10\"><cfRule type=\"duplicateValues\" dxfId=\"0\" priority=\"1\"/></conditionalFormatting>" +
                "<conditionalFormatting sqref=\"B1:B10\"><cfRule type=\"containsText\" dxfId=\"1\" priority=\"2\" operator=\"containsText\" text=\"1\"><formula>NOT(ISERROR(SEARCH(\"1\",B1)))</formula></cfRule></conditionalFormatting>";
            Add("x24-cf-duplicates-text", s);
        }

        // Rich text, errors, drawings, several sheets, right-to-left text.
        {
            var s = new TestXlsx.Sheet("Rich");
            s.Cols = "<cols><col min=\"1\" max=\"1\" width=\"40.7109375\" customWidth=\"1\"/></cols>";
            s.Set("A1", "<si><r><rPr><b/><sz val=\"11\"/><rFont val=\"Calibri\"/></rPr><t>Bold </t></r><r><rPr><i/><sz val=\"14\"/><color rgb=\"FFFF0000\"/><rFont val=\"Calibri\"/></rPr><t>big red italic </t></r><r><rPr><vertAlign val=\"superscript\"/><sz val=\"11\"/><rFont val=\"Calibri\"/></rPr><t>sup</t></r></si>");
            s.Set("A2", "Line one\nLine two", TestXlsx.Wrap);
            s.RowAttributes[2] = " ht=\"30\" customHeight=\"1\"";
            Add("x25-rich-text", s);
        }

        {
            var s = new TestXlsx.Sheet("Objects");
            s.Fill(12, 6, (r, c) => r * 10 + c);
            s.DrawingXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
                "<xdr:twoCellAnchor><xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>2</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>" +
                "<xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"2\" name=\"Chart 1\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"/></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>" +
                "<xdr:oneCellAnchor><xdr:from><xdr:col>5</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>9</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:ext cx=\"952500\" cy=\"476250\"/>" +
                "<xdr:sp macro=\"\" textlink=\"\"><xdr:nvSpPr><xdr:cNvPr id=\"3\" name=\"Rectangle 2\"/><xdr:cNvSpPr/></xdr:nvSpPr><xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"952500\" cy=\"476250\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:sp><xdr:clientData/></xdr:oneCellAnchor></xdr:wsDr>";
            Add("x26-drawing-anchors", s);
        }

        {
            TestXlsx.Sheet a = Grid("First", 70, 4);
            a.HeaderFooter = "<headerFooter><oddFooter>&amp;CPage &amp;P of &amp;N</oddFooter></headerFooter>";
            TestXlsx.Sheet b = Grid("Second", 70, 4);
            b.HeaderFooter = "<headerFooter><oddFooter>&amp;CPage &amp;P of &amp;N</oddFooter></headerFooter>";
            TestXlsx.Sheet c = Grid("Third", 10, 4);
            c.State = "hidden";
            TestXlsx.Sheet d = Grid("Fourth", 5, 3);
            d.PageSetup = PageSetup("firstPageNumber=\"7\" useFirstPageNumber=\"1\"");
            d.HeaderFooter = "<headerFooter><oddFooter>&amp;CPage &amp;P</oddFooter></headerFooter>";
            AddMany("x27-several-sheets", [a, b, c, d]);
        }

        {
            var s = new TestXlsx.Sheet("Scripts");
            s.Cols = "<cols><col min=\"1\" max=\"1\" width=\"30.7109375\" customWidth=\"1\"/></cols>";
            s.Set("A1", "مرحبا بالعالم").Set("A2", "שלום עולם").Set("A3", "Mixed English مرحبا text").Set("A4", "你好，世界");
            Add("x28-scripts", s);
        }

        {
            TestXlsx.Sheet s = Grid("Used", 3, 3);
            s.Set("F8", null, TestXlsx.Yellow);
            s.Set("A20", 1, 0);
            Add("x29-used-range-with-format-only-cell", s);
        }

        return list;
    }
}
