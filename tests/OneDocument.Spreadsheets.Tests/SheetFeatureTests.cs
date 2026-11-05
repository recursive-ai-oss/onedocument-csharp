using System.Text;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public sealed class SheetFeatureTests
{
    private const string X14 = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private const string Xm = "http://schemas.microsoft.com/office/excel/2006/main";

    private static string Part(byte[] file, string name)
    {
        using OpcPackage p = OpcPackage.Open(new MemoryStream(file));
        return Encoding.UTF8.GetString(p.FindPart(name)!.GetBytes().Span);
    }

    private static int IndexOf(string s, string needle) => s.IndexOf(needle, StringComparison.Ordinal);

    // ---- defined names -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void NamesAreListedAddedRenamedAndRemoved()
    {
        using Workbook wb = TestBook.Open();
        DefinedName local = Assert.Single(wb.Names);
        Assert.Equal("Local", local.Name);
        Assert.Equal(wb.Sheets[1], local.Scope);
        Assert.Equal("Other!$A$1", local.Formula);

        DefinedName total = wb.Names.Add("Total", "Data!$B$1:$B$3");
        wb.Names.AddBuiltIn(BuiltInName.PrintArea, wb.Sheets[0], "Data!$A$1:$D$4");
        wb.Names.Add("Local", "Data!$A$1", wb.Sheets[0]);
        Assert.Equal(4, wb.Names.Count);
        Assert.Throws<ArgumentException>(() => wb.Names.Add("total", "1"));
        Assert.Equal(BuiltInName.PrintArea, wb.Names.Find("_xlnm.Print_Area", wb.Sheets[0])!.BuiltIn);

        wb.Names.Rename(total, "Grand_Total");
        total.Formula = "SUM(Data!$B$1:$B$3)";
        total.IsHidden = true;
        wb.Names.Remove(wb.Names.Find("Local", wb.Sheets[0])!);

        using Workbook back = TestBook.Open(TestBook.Save(wb));
        Assert.Equal(3, back.Names.Count);
        DefinedName t = back.Names.Find("grand_total")!;
        Assert.True(t.IsHidden);
        Assert.Equal("SUM(Data!$B$1:$B$3)", t.Formula);
        Assert.Null(back.Names.Find("Total"));
    }

    [Theory]
    [InlineData("Sales", true)]
    [InlineData("_x", true)]
    [InlineData("a.b_c", true)]
    [InlineData("\\path", true)]
    [InlineData("Rate", true)]
    [InlineData("Row1x", true)]
    [InlineData("A1", false)]
    [InlineData("XFD1048576", false)]
    [InlineData("XFE1", true)]
    [InlineData("R1C1", false)]
    [InlineData("r", false)]
    [InlineData("C", false)]
    [InlineData("RC", false)]
    [InlineData("1abc", false)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    public void NameValidity(string name, bool valid)
    {
        Assert.Equal(valid, DefinedNameCollection.IsValidName(name));
    }

    [Fact]
    public void NamesFollowSheetRemovalAndInsertion()
    {
        using Workbook wb = TestBook.Open();
        wb.Names.Add("OnData", "Data!$A$1", wb.Sheets[0]);
        wb.Sheets.Insert(0, "First");
        Assert.Equal(wb.Sheets["Data"], wb.Names.Find("OnData", wb.Sheets["Data"])!.Scope);
    }

    // ---- data validation -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void ValidationIsAddedInSchemaOrderReadBackAndRemoved()
    {
        using Workbook wb = TestBook.Open();
        Worksheet s = wb.Sheets[0];
        DataValidation v = s.Validations.Add(CellRange.Parse("A5:A9"), DataValidationSpec.List("Yes", "No") with { Error = "Pick one", ErrorTitle = "Oops", Prompt = "Choose" });
        s.Validations.Add(CellRange.Parse("B5:B9"), DataValidationSpec.WholeBetween(1, 10));
        Assert.Equal(2, s.Validations.Count);
        Assert.Equal(DataValidationType.List, v.Type);

        byte[] saved = TestBook.Save(wb);
        string xml = Part(saved, "/xl/worksheets/sheet1.xml");
        Assert.True(IndexOf(xml, "<mergeCells") < IndexOf(xml, "<dataValidations count=\"2\">"));
        Assert.True(IndexOf(xml, "</dataValidations>") < IndexOf(xml, "<pageMargins"));
        Assert.Contains("<extLst><ext uri=\"{abc}\"", xml, StringComparison.Ordinal);

        using Workbook back = TestBook.Open(saved);
        DataValidation[] all = [.. back.Sheets[0].Validations];
        Assert.Equal(2, all.Length);
        Assert.Equal("\"Yes,No\"", all[0].Formula1);
        Assert.Equal("Pick one", all[0].Error);
        Assert.True(all[0].ShowErrorMessage);
        Assert.True(all[0].InCellDropDown);
        Assert.Equal(DataValidationType.WholeNumber, all[1].Type);
        Assert.Equal("10", all[1].Formula2);
        Assert.Equal(CellRange.Parse("B5:B9"), all[1].Ranges[0]);

        back.Sheets[0].Validations.Remove(all[0]);
        back.Sheets[0].Validations.Remove(all[1]);
        string cleaned = Part(TestBook.Save(back), "/xl/worksheets/sheet1.xml");
        Assert.DoesNotContain("dataValidation", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtendedValidationsAndRulesAreReadAndRemoved()
    {
        string sheet = TestBook.Sheet1Xml.Replace("<extLst><ext uri=\"{abc}\"",
            "<extLst><ext uri=\"{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}\" xmlns:x14=\"" + X14 + "\" xmlns:xm=\"" + Xm + "\"><x14:dataValidations count=\"1\"><x14:dataValidation type=\"list\" allowBlank=\"1\">"
            + "<x14:formula1><xm:f>Other!$A$1:$A$3</xm:f></x14:formula1><xm:sqref>C5:C9</xm:sqref></x14:dataValidation></x14:dataValidations></ext>"
            + "<ext uri=\"{78C0D931-6437-4d4b-8E6A-AA1B2B8B1F36}\" xmlns:x14=\"" + X14 + "\" xmlns:xm=\"" + Xm + "\"><x14:conditionalFormattings><x14:conditionalFormatting><x14:cfRule type=\"dataBar\" id=\"{1}\" priority=\"3\">"
            + "<x14:dataBar minLength=\"0\" maxLength=\"100\"><x14:cfvo type=\"autoMin\"/><x14:cfvo type=\"autoMax\"/><x14:fillColor rgb=\"FF638EC6\"/></x14:dataBar></x14:cfRule><xm:sqref>D5:D9</xm:sqref></x14:conditionalFormatting></x14:conditionalFormattings></ext>"
            + "<ext uri=\"{abc}\"", StringComparison.Ordinal);
        using Workbook wb = TestBook.Open(TestBook.Bytes(sheet));
        Worksheet s = wb.Sheets[0];
        DataValidation dv = Assert.Single(s.Validations);
        Assert.True(dv.IsExtended);
        Assert.Equal("Other!$A$1:$A$3", dv.Formula1);
        Assert.Equal(CellRange.Parse("C5:C9"), dv.Ranges[0]);
        ConditionalFormat cf = Assert.Single(s.ConditionalFormats);
        Assert.True(cf.IsExtended);
        ConditionalFormatRule rule = Assert.Single(cf.Rules);
        Assert.Equal(ConditionalRuleType.DataBar, rule.Type);
        Assert.Equal(3, rule.Priority);
        Assert.Equal("autoMin", rule.DataBar!.Points[0].Type);
        Assert.Equal(unchecked((int)0xFF638EC6), rule.DataBar.Color.Value);
        Assert.Equal(CellRange.Parse("D5:D9"), cf.Ranges[0]);

        // A new rule gets the next priority after the extended one.
        ConditionalFormatRule added = s.ConditionalFormats.Add(CellRange.Parse("A1:A3"), ConditionalRuleSpec.CellIs("greaterThan", "5", 0));
        Assert.Equal(4, added.Priority);

        s.Validations.Remove(dv);
        s.ConditionalFormats.Remove(cf);
        string xml = Part(TestBook.Save(wb), "/xl/worksheets/sheet1.xml");
        Assert.DoesNotContain("x14:dataValidations", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("x14:conditionalFormattings", xml, StringComparison.Ordinal);
        Assert.Contains("<ext uri=\"{abc}\"", xml, StringComparison.Ordinal);
    }

    // ---- conditional formatting ----------------------------------------------------------------------------------------------------

    [Fact]
    public void ConditionalRulesAreAddedWithPrioritiesAndReadBack()
    {
        using Workbook wb = TestBook.Open(StyleTests.Book());
        Worksheet s = wb.Sheets[0];
        int dxf = wb.Styles.AddDifferentialFormat(new DifferentialFormat(new CellFont(Bold: true)));
        s.ConditionalFormats.Add(CellRange.Parse("B1:B4"), ConditionalRuleSpec.Expression("B1>10", dxf) with { StopIfTrue = true });
        s.ConditionalFormats.Add([CellRange.Parse("C1:C4"), CellRange.Parse("E1")], ConditionalRuleSpec.ThreeColorScale(StyleColor.FromRgb(0xF8696B), StyleColor.FromRgb(0xFFEB84), StyleColor.FromRgb(0x63BE7B)));
        s.ConditionalFormats.Add(CellRange.Parse("D1:D4"), ConditionalRuleSpec.DataBars(StyleColor.FromRgb(0x638EC6)));
        s.ConditionalFormats.Add(CellRange.Parse("F1:F4"), ConditionalRuleSpec.Icons("4Arrows"));
        s.ConditionalFormats.Add(CellRange.Parse("G1:G4"), ConditionalRuleSpec.Top(3, dxf, bottom: true));
        Assert.Throws<ArgumentException>(() => s.ConditionalFormats.Add(CellRange.Parse("H1"), new ConditionalRuleSpec { Type = ConditionalRuleType.Expression }));

        byte[] saved = TestBook.Save(wb);
        string xml = Part(saved, "/xl/worksheets/sheet1.xml");
        Assert.True(IndexOf(xml, "</mergeCells>") < IndexOf(xml, "<conditionalFormatting"));
        Assert.True(IndexOf(xml, "</conditionalFormatting><pageMargins") > 0);

        using Workbook back = TestBook.Open(saved);
        ConditionalFormat[] all = [.. back.Sheets[0].ConditionalFormats];
        Assert.Equal(5, all.Length);
        ConditionalFormatRule r0 = all[0].Rules[0];
        Assert.Equal(ConditionalRuleType.Expression, r0.Type);
        Assert.Equal("B1>10", r0.Formulas[0]);
        Assert.True(r0.StopIfTrue);
        Assert.Equal(1, r0.Priority);
        Assert.Equal(dxf, r0.DxfId);
        Assert.Equal(2, all[1].Ranges.Count);
        ColorScaleInfo cs = all[1].Rules[0].ColorScale!;
        Assert.Equal(3, cs.Points.Count);
        Assert.Equal("percentile", cs.Points[1].Type);
        Assert.Equal(unchecked((int)0xFF63BE7B), cs.Colors[2].Value);
        Assert.Equal(4, all[3].Rules[0].IconSet!.Points.Count);
        Assert.Equal("25", all[3].Rules[0].IconSet!.Points[1].Value);
        Assert.True(all[4].Rules[0].Bottom);
        Assert.Equal(3, all[4].Rules[0].Rank);
        Assert.Equal([1, 2, 3, 4, 5], all.Select(a => a.Rules[0].Priority).ToArray());

        all[2].Rules[0].Priority = 9;
        back.Sheets[0].ConditionalFormats.Remove(all[0]);
        using Workbook again = TestBook.Open(TestBook.Save(back));
        Assert.Equal(4, again.Sheets[0].ConditionalFormats.Count);
    }

    // ---- tables --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ResizeMakesTheColumnListOfADamagedTable()
    {
        using Workbook wb = TestBook.Open();
        Worksheet s = wb.Sheets[1];
        s["A1"].SetString("Name");
        s.Tables.Add("People", CellRange.Parse("A1:B3"));
        byte[] saved = TestBook.Save(wb);

        var damaged = new MemoryStream();
        using (var zin = new System.IO.Compression.ZipArchive(new MemoryStream(saved)))
        using (var zout = new System.IO.Compression.ZipArchive(damaged, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var e in zin.Entries)
            {
                using Stream src = e.Open();
                using Stream dst = zout.CreateEntry(e.FullName).Open();
                if (e.FullName == "xl/tables/table1.xml")
                {
                    string xml = new StreamReader(src).ReadToEnd();
                    int a = xml.IndexOf("<tableColumns", StringComparison.Ordinal);
                    int b = xml.IndexOf("</tableColumns>", StringComparison.Ordinal) + "</tableColumns>".Length;
                    dst.Write(Encoding.UTF8.GetBytes(xml.Remove(a, b - a)));
                }
                else
                {
                    src.CopyTo(dst);
                }
            }
        }

        using Workbook back = TestBook.Open(damaged.ToArray());
        Table t = Assert.Single(back.Sheets[1].Tables);
        t.Resize(CellRange.Parse("A1:C4"));
        Assert.Equal(3, t.Columns.Count);
    }

    [Fact]
    public void TableIsAddedResizedReadBackAndRemoved()
    {
        using Workbook wb = TestBook.Open();
        Worksheet s = wb.Sheets[1];
        s["A1"].SetString("Name");
        s["B1"].SetNumber(5);
        s["C1"].SetString("Name");
        s["A2"].SetString("x");
        Table t = s.Tables.Add("People", CellRange.Parse("A1:C3"));
        Assert.Equal(1, t.Id);
        Assert.Equal(["Name", "Column2", "Name2"], t.Columns.Select(c => c.Name).ToArray());
        Assert.Equal("Column2", s["B1"].Text);
        Assert.Equal("Name2", s["C1"].Text);
        Assert.Throws<ArgumentException>(() => s.Tables.Add("people", CellRange.Parse("E1:F3")));
        Assert.Throws<ArgumentException>(() => s.Tables.Add("Other", CellRange.Parse("B2:D4")));
        Assert.Throws<ArgumentException>(() => s.Tables.Add("A1", CellRange.Parse("E1:F3")));

        byte[] saved = TestBook.Save(wb);
        Dictionary<string, string> parts = TestBook.PartHashes(saved);
        Assert.True(parts.ContainsKey("/xl/tables/table1.xml"));
        string sheetXml = Part(saved, "/xl/worksheets/sheet2.xml");
        Assert.Contains("<tableParts count=\"1\"><tablePart r:id=\"rId1\"/></tableParts>", sheetXml, StringComparison.Ordinal);
        Assert.Contains("xmlns:r=", sheetXml, StringComparison.Ordinal);
        using (OpcPackage pk = OpcPackage.Open(new MemoryStream(saved)))
        {
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml", pk.FindPart("/xl/tables/table1.xml")!.ContentType);
        }


        using Workbook back = TestBook.Open(saved);
        Table t2 = Assert.Single(back.Sheets[1].Tables);
        Assert.Equal("People", t2.Name);
        Assert.Equal(CellRange.Parse("A2:C3"), t2.DataRange);
        t2.Resize(CellRange.Parse("A1:D6"));
        t2.SetStyle("TableStyleLight1");
        Assert.Equal("Column4", back.Sheets[1]["D1"].Text);
        Assert.Equal(CellRange.Parse("A1:D6"), t2.AutoFilterRange);

        using Workbook third = TestBook.Open(TestBook.Save(back));
        Table t3 = Assert.Single(third.Sheets[1].Tables);
        Assert.Equal(4, t3.Columns.Count);
        Assert.Equal("TableStyleLight1", t3.StyleName);
        Assert.Equal(["Name", "Column2", "Name2", "Column4"], t3.Columns.Select(c => c.Name).ToArray());

        third.Sheets[1].Tables.Remove(t3);
        byte[] removed = TestBook.Save(third);
        Assert.False(TestBook.PartHashes(removed).ContainsKey("/xl/tables/table1.xml"));
        Assert.DoesNotContain("tablePart", Part(removed, "/xl/worksheets/sheet2.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void TableIdsAndNamesAreUniqueAcrossSheets()
    {
        using Workbook wb = TestBook.Open();
        Table a = wb.Sheets[0].Tables.Add("Alpha", CellRange.Parse("A10:B12"));
        Table b = wb.Sheets[1].Tables.Add("Beta", CellRange.Parse("A1:B3"), hasHeaderRow: false, styleName: null);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(0, b.HeaderRowCount);
        Assert.Throws<ArgumentException>(() => wb.Sheets[1].Tables.Add("alpha", CellRange.Parse("D1:E3")));
        b.Rename("Renamed");
        Assert.Equal("Renamed", b.DisplayName);
        Assert.Throws<ArgumentException>(() => b.Rename("Alpha"));
        wb.Names.Add("Used", "1");
        Assert.Throws<ArgumentException>(() => b.Rename("used"));
    }
}
