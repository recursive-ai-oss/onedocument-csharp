using System.Text;
using RecursiveAI.OneOffice.NumberFormats;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Spreadsheets.Tests;

public sealed class StyleTests
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private const string StylesXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<styleSheet xmlns=\"" + TestBook.Main + "\">"
        + "<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"0.000\"/><numFmt numFmtId=\"165\" formatCode=\"yyyy\\-mm\\-dd\"/></numFmts>"
        + "<fonts count=\"3\"><font><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/><scheme val=\"minor\"/></font>"
        + "<font><b/><sz val=\"11\"/><color rgb=\"FFFF0000\"/><name val=\"Calibri\"/></font><font><i/><u/><sz val=\"9\"/><color indexed=\"10\"/><name val=\"Arial\"/></font></fonts>"
        + "<fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>"
        + "<fill><patternFill patternType=\"solid\"><fgColor theme=\"4\" tint=\"0.39997558519241921\"/><bgColor indexed=\"64\"/></patternFill></fill>"
        + "<fill><gradientFill degree=\"90\"><stop position=\"0\"><color rgb=\"FFFFFFFF\"/></stop><stop position=\"1\"><color rgb=\"FF000000\"/></stop></gradientFill></fill></fills>"
        + "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style=\"thin\"><color auto=\"1\"/></left><right/><top style=\"double\"><color rgb=\"FF00FF00\"/></top><bottom/><diagonal/></border></borders>"
        + "<cellStyleXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/><xf numFmtId=\"9\" fontId=\"1\" fillId=\"2\" borderId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\"><alignment horizontal=\"center\"/></xf></cellStyleXfs>"
        + "<cellXfs count=\"5\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
        + "<xf numFmtId=\"164\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"><alignment wrapText=\"1\" vertical=\"top\"/><protection locked=\"0\"/></xf>"
        + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"1\" applyNumberFormat=\"0\" applyFont=\"0\" applyFill=\"0\"/>"
        + "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
        + "<xf numFmtId=\"14\" fontId=\"2\" fillId=\"3\" borderId=\"0\" xfId=\"0\" quotePrefix=\"1\" pivotButton=\"1\"/></cellXfs>"
        + "<cellStyles count=\"2\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/><cellStyle name=\"Percent Fancy\" xfId=\"1\"/></cellStyles>"
        + "<dxfs count=\"1\"><dxf><font><b/><color rgb=\"FF9C0006\"/></font><fill><patternFill><bgColor rgb=\"FFFFC7CE\"/></patternFill></fill></dxf></dxfs>"
        + "<tableStyles count=\"1\" defaultTableStyle=\"TableStyleMedium2\" defaultPivotStyle=\"PivotStyleLight16\"><tableStyle name=\"Mine\" pivot=\"0\" count=\"1\"><tableStyleElement type=\"headerRow\" dxfId=\"0\"/></tableStyle></tableStyles>"
        + "<colors><indexedColors><rgbColor rgb=\"FF000000\"/><rgbColor rgb=\"FFFFFFFF\"/></indexedColors></colors>"
        + "<extLst><ext uri=\"{x}\"/></extLst></styleSheet>";

    private const string ThemeXml = "<?xml version=\"1.0\"?><a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:themeElements><a:clrScheme name=\"t\">"
        + "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1><a:dk2><a:srgbClr val=\"44546A\"/></a:dk2><a:lt2><a:srgbClr val=\"E7E6E6\"/></a:lt2>"
        + "<a:accent1><a:srgbClr val=\"4472C4\"/></a:accent1><a:accent2><a:srgbClr val=\"ED7D31\"/></a:accent2><a:accent3><a:srgbClr val=\"A5A5A5\"/></a:accent3><a:accent4><a:srgbClr val=\"FFC000\"/></a:accent4>"
        + "<a:accent5><a:srgbClr val=\"5B9BD5\"/></a:accent5><a:accent6><a:srgbClr val=\"70AD47\"/></a:accent6><a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink><a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink>"
        + "</a:clrScheme></a:themeElements></a:theme>";

    internal static byte[] Book(string? styles = StylesXml, bool theme = true, string? workbookPr = null, string? sheet1 = null)
    {
        using OpcPackage p = OpcPackage.Open(new MemoryStream(TestBook.Bytes(sheet1)));
        if (styles is not null)
        {
            p.FindPart("/xl/styles.xml")!.SetBytes(Encoding.UTF8.GetBytes(styles));
        }

        if (theme)
        {
            p.AddPart("/xl/theme/theme1.xml", "application/vnd.openxmlformats-officedocument.theme+xml", Encoding.UTF8.GetBytes(ThemeXml));
            var rels = new List<Relationship>(p.GetRelationships("/xl/workbook.xml")) { new("rId9", Rel + "theme", "theme/theme1.xml") };
            p.SetRelationships("/xl/workbook.xml", rels);
        }

        if (workbookPr is not null)
        {
            IPackagePart wb = p.FindPart("/xl/workbook.xml")!;
            wb.SetBytes(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(wb.GetBytes().Span).Replace("<bookViews>", workbookPr + "<bookViews>", StringComparison.Ordinal)));
        }

        using var ms = new MemoryStream();
        p.WriteTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void ResolvesOwnIdsAndNumberFormats()
    {
        using Workbook wb = TestBook.Open(Book());
        StyleSheet st = wb.Styles;
        Assert.Equal(5, st.CellFormatCount);
        ResolvedCellStyle r = st.Resolve(1);
        Assert.Equal("0.000", r.NumberFormatCode);
        Assert.True(r.Font.Bold);
        Assert.Equal(StyleColor.FromArgb(unchecked((int)0xFFFF0000)), r.Font.Color);
        Assert.Equal(FillPattern.Solid, r.Fill.Pattern);
        Assert.Equal(BorderLineStyle.Thin, r.Border.Left.Style);
        Assert.Equal(BorderLineStyle.Doubled, r.Border.Top.Style);
        Assert.True(r.Alignment.WrapText);
        Assert.Equal(CellVerticalAlignment.Top, r.Alignment.Vertical);
        Assert.False(r.Protection.Locked);
        Assert.Same(r, st.Resolve(1));
        Assert.Equal("m/d/yyyy", st.Resolve(4).NumberFormatCode);
        Assert.True(st.Resolve(4).QuotePrefix);
        Assert.Equal(FillPattern.None, st.Resolve(4).Fill.Pattern);
        Assert.NotNull(st.Resolve(4).Fill.Gradient);
        Assert.Equal(2, st.Resolve(4).Fill.Gradient!.Stops.Count);
    }

    [Fact]
    public void ApplyFlagsDoNotChangeWhichIdsAreUsed()
    {
        using Workbook wb = TestBook.Open(Book());
        ResolvedCellStyle r = wb.Styles.Resolve(2);
        Assert.Equal("Percent Fancy", r.StyleName);
        // The cell format states applyX=0 but its own ids win, as in Excel.
        Assert.Equal("General", r.NumberFormatCode);
        Assert.False(r.Font.Bold);
        Assert.Equal(FillPattern.None, r.Fill.Pattern);
        // The alignment child comes from the named style when the cell format has none.
        Assert.Equal(CellHorizontalAlignment.Center, r.Alignment.Horizontal);
        Assert.Equal("Calibri", wb.Styles.Resolve(3).Font.Name);
        Assert.Equal("yyyy\\-mm\\-dd", wb.Styles.Resolve(3).NumberFormatCode);
    }

    [Fact]
    public void ColoursResolveThroughPaletteThemeAndTint()
    {
        using Workbook wb = TestBook.Open(Book());
        StyleSheet st = wb.Styles;
        Assert.Equal(unchecked((int)0xFF4472C4), st.ResolveColor(StyleColor.FromTheme(4)));
        Assert.Equal(unchecked((int)0xFFFFFFFF), st.ResolveColor(StyleColor.FromIndexed(1)));
        Assert.Equal(unchecked((int)0xFF000000), st.ResolveColor(StyleColor.FromIndexed(64)));
        Assert.Equal(unchecked((int)0xFFFFFFFF), st.ResolveColor(StyleColor.FromIndexed(65)));
        // Excel names this colour "Blue, Accent 1, Lighter 40%": 8FAADC.
        Assert.Equal(unchecked((int)0xFF8FAADC), st.ResolveColor(StyleColor.FromTheme(4, 0.39997558519241921)));
        // Darker 50%: 203764.
        Assert.Equal(unchecked((int)0xFF203864), st.ResolveColor(StyleColor.FromTheme(4, -0.499984740745262)));
    }

    [Fact]
    public void GetOrAddReusesEqualFormatsAndAppendsNewOnes()
    {
        using Workbook wb = TestBook.Open(Book());
        StyleSheet st = wb.Styles;
        int a = st.GetOrAdd(CellStyleSpec.From(st.Resolve(1)));
        Assert.Equal(1, a);
        var spec = new CellStyleSpec { NumberFormatCode = "#,##0.00", Font = new CellFont("Calibri", 12, Bold: true) };
        int b = st.GetOrAdd(spec);
        Assert.Equal(5, b);
        Assert.Equal(b, st.GetOrAdd(spec));
        Assert.Equal(6, st.CellFormatCount - 0);
        Assert.Equal("#,##0.00", st.Resolve(b).NumberFormatCode);
        Assert.Equal(3, st.GetOrAdd(CellStyleSpec.From(st.Resolve(3))));
        // Built-in code needs no numFmt.
        int c = st.GetOrAdd(new CellStyleSpec { NumberFormatCode = "0%" });
        Assert.Equal(9, st.Resolve(c).NumberFormatId);
        // The custom code that exists is reused.
        Assert.Equal(164, st.GetOrAddNumberFormat("0.000"));
    }

    [Fact]
    public void AddedStylesSurviveSaveAndTheRestOfThePartIsKept()
    {
        using Workbook wb = TestBook.Open(Book());
        Cell cell = wb.Sheets[0]["B2"];
        cell.SetNumber(1234.5);
        cell.SetStyle(new CellStyleSpec { NumberFormatCode = "#,##0.0000", Fill = CellFill.Solid(StyleColor.FromRgb(0xFFFF00)), Border = CellBorder.Box(BorderLineStyle.Thin) });
        byte[] saved = TestBook.Save(wb);
        string styles = ReadPart(saved, "/xl/styles.xml");
        Assert.Contains("<extLst><ext uri=\"{x}\"/></extLst>", styles, StringComparison.Ordinal);
        Assert.Contains("<cellXfs count=\"6\">", styles, StringComparison.Ordinal);
        Assert.Contains("formatCode=\"#,##0.0000\"", styles, StringComparison.Ordinal);
        using Workbook back = TestBook.Open(saved);
        Assert.Equal("1,234.5000", back.Sheets[0]["B2"].FormattedText);
        Assert.Equal(FillPattern.Solid, back.Sheets[0]["B2"].Style.Fill.Pattern);
        // Untouched parts stay identical.
        Dictionary<string, string> before = TestBook.PartHashes(Book());
        Dictionary<string, string> after = TestBook.PartHashes(saved);
        Assert.Equal(before["/xl/theme/theme1.xml"], after["/xl/theme/theme1.xml"]);
        Assert.Equal(before["/xl/sharedStrings.xml"], after["/xl/sharedStrings.xml"]);
    }

    [Fact]
    public void ReadingStylesAndFormattedTextChangesNothing()
    {
        byte[] file = Book();
        using Workbook wb = TestBook.Open(file);
        _ = wb.Styles.Resolve(1);
        _ = wb.Sheets[0]["B1"].FormattedText;
        Assert.False(wb.HasUncommittedChanges);
        Assert.Equal(TestBook.PartHashes(file), TestBook.PartHashes(TestBook.Save(wb)));
    }

    [Fact]
    public void WorkbookWithoutStylesGetsOneOnlyWhenAStyleIsAdded()
    {
        using Workbook wb = Workbook.Create();
        Assert.Equal("General", wb.Sheets[0]["A1"].Style.NumberFormatCode);
        Assert.False(wb.HasUncommittedChanges);
        Cell c = wb.Sheets[0]["A1"];
        c.SetNumber(0.256);
        c.SetStyle(new CellStyleSpec { NumberFormatCode = "0.0%" });
        Assert.Equal("25.6%", c.FormattedText);
        byte[] saved = TestBook.Save(wb);
        Assert.True(TestBook.PartHashes(saved).ContainsKey("/xl/styles.xml"));
        using Workbook back = TestBook.Open(saved);
        Assert.Equal("25.6%", back.Sheets[0]["A1"].FormattedText);
    }

    [Fact]
    public void FormattedTextAndDates()
    {
        using Workbook wb = TestBook.Open(Book());
        Worksheet s = wb.Sheets[0];
        s["B2"].SetNumber(45000.75);
        s["B2"].StyleIndex = 3;
        Assert.Equal("2023-03-15", s["B2"].FormattedText);
        Assert.True(s["B2"].IsDate);
        Assert.Equal(new DateTime(2023, 3, 15, 18, 0, 0), s["B2"].DateValue);
        s["C2"].SetNumber(1.23456);
        s["C2"].StyleIndex = 1;
        Assert.Equal("1.235", s["C2"].FormattedText);
        Assert.False(s["C2"].IsDate);
        s["D2"].SetBoolean(true);
        Assert.Equal("TRUE", s["D2"].FormattedText);
        s["E2"].SetDate(new DateTime(2000, 1, 1));
        Assert.Equal("1/1/2000", s["E2"].FormattedText);
        Span<char> buf = stackalloc char[32];
        Assert.True(s["B2"].TryFormatNumber(buf, out int n));
        Assert.Equal("2023-03-15", buf[..n].ToString());
    }

    [Fact]
    public void Date1904IsHonoured()
    {
        using Workbook wb = TestBook.Open(Book(workbookPr: "<workbookPr date1904=\"1\"/>"));
        Assert.Equal(DateSystem.Date1904, wb.DateSystem);
        Cell c = wb.Sheets[0]["B2"];
        c.SetNumber(0);
        c.StyleIndex = 3;
        Assert.Equal("1904-01-01", c.FormattedText);
        c.SetDate(new DateTime(1904, 1, 2));
        Assert.Equal(1, c.Number);
    }

    [Fact]
    public void DifferentialFormatsAndTableStylesAreRead()
    {
        using Workbook wb = TestBook.Open(Book());
        DifferentialFormat d = wb.Styles.GetDifferentialFormat(0);
        Assert.True(d.Font!.Bold);
        Assert.Equal(StyleColor.FromArgb(unchecked((int)0xFFFFC7CE)), d.Fill!.Background);
        Assert.Equal("Mine", wb.Styles.TableStyles[0].Name);
        Assert.Equal("TableStyleMedium2", wb.Styles.DefaultTableStyle);
        int idx = wb.Styles.AddDifferentialFormat(new DifferentialFormat(new CellFont(Bold: true, Color: StyleColor.FromRgb(0xFF0000)), Fill: new CellFill(FillPattern.Solid, Background: StyleColor.FromRgb(0xFFFF00))));
        Assert.Equal(1, idx);
        Assert.Equal(2, wb.Styles.DifferentialFormatCount);
    }

    [Fact]
    public void NamedStylesCanBeAddedAndUsed()
    {
        using Workbook wb = TestBook.Open(Book());
        wb.Styles.AddNamedStyle("Money", new CellStyleSpec { NumberFormatCode = "$#,##0.00" });
        int i = wb.Styles.GetOrAdd(new CellStyleSpec { StyleName = "Money", NumberFormatCode = "$#,##0.00" });
        Assert.Equal("Money", wb.Styles.Resolve(i).StyleName);
        Assert.Throws<ArgumentException>(() => wb.Styles.GetOrAdd(new CellStyleSpec { StyleName = "Nope" }));
        Assert.Throws<ArgumentException>(() => wb.Styles.AddNamedStyle("money", new CellStyleSpec()));
    }

    private static string ReadPart(byte[] file, string name)
    {
        using OpcPackage p = OpcPackage.Open(new MemoryStream(file));
        return Encoding.UTF8.GetString(p.FindPart(name)!.GetBytes().Span);
    }
}
