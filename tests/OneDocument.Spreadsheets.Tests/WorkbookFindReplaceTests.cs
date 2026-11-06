using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public sealed class WorkbookFindReplaceTests
{
    private const string Main = TestBook.Main;
    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

    private static string Sheet(string rows) => Decl + "<worksheet xmlns=\"" + Main + "\"><sheetData>" + rows + "</sheetData><mergeCells count=\"1\"><mergeCell ref=\"A9:B9\"/></mergeCells></worksheet>";

    private static string Shared(params string[] items) =>
        Decl + "<sst xmlns=\"" + Main + "\" count=\"" + items.Length + "\" uniqueCount=\"" + items.Length + "\">" + string.Concat(items) + "</sst>";

    private static Workbook Open(string rows, params string[] items) => TestBook.Open(TestBook.Bytes(sheet1: Sheet(rows), shared: Shared(items)));

    private static string Part(byte[] file, string name)
    {
        using OpcPackage p = OpcPackage.Open(new MemoryStream(file));
        return Encoding.UTF8.GetString(p.FindPart(name)!.GetBytes().Span);
    }

    [Fact]
    public void PlainTextCellsAreReplacedAndKeepTheirStyle()
    {
        using Workbook wb = Open("<row r=\"1\"><c r=\"A1\" s=\"3\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"C1\"><v>1.5</v></c></row>", "<si><t>Hello world</t></si>", "<si><t>nothing</t></si>");
        ReplaceReport r = wb.Replace("world", "there");
        Assert.Equal(1, r.Count);
        Assert.Equal(new TextLocation("Data", "cell", 0, 6, "A1"), r.Items[0].Location);
        Cell a1 = wb.Sheets["Data"]["A1"];
        Assert.Equal("Hello there", a1.Text);
        Assert.Equal(3, a1.StyleIndex);
        Assert.Equal("nothing", wb.Sheets["Data"]["B1"].Text);
        // numbers are not text: nothing matches in C1
        Assert.Empty(wb.Find("1.5"));
    }

    [Fact]
    public void RichTextKeepsItsRunsAndThePhoneticPart()
    {
        using Workbook wb = Open("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>0</v></c></row>", "<si><r><t>Rich</t></r><r><rPr><b/></rPr><t> text</t></r><rPh sb=\"0\" eb=\"1\"><t>ph</t></rPh></si>");
        ReplaceReport r = wb.Replace("ch te", "X");
        Assert.Equal(2, r.Count);
        Assert.Equal("RiXxt", wb.Sheets["Data"]["A1"].Text);
        byte[] file = TestBook.Save(wb);
        string sst = Part(file, "/xl/sharedStrings.xml");
        // the first run keeps its (absent) formatting and takes the new text; the bold run keeps its bold and the rest of its text
        Assert.Contains("<r><t>RiX</t></r><r><rPr><b/></rPr><t>xt</t></r><rPh sb=\"0\" eb=\"1\"><t>ph</t></rPh>", sst);
        using Workbook back = TestBook.Open(file);
        Assert.Equal("RiXxt", back.Sheets["Data"]["A1"].Text);
        Assert.Equal("RiXxt", back.Sheets["Data"]["B1"].Text);
    }

    [Fact]
    public void MatchesAcrossRichRunsOnlyInTheTextOfTheRuns()
    {
        using Workbook wb = Open("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>", "<si><r><t>ab</t></r><r><rPr><i/></rPr><t>cd</t></r><rPh sb=\"0\" eb=\"1\"><t>bc</t></rPh></si>");
        Assert.Single(wb.Find("bc"));
        wb.Replace("bc", "|");
        Assert.Equal("a|d", wb.Sheets["Data"]["A1"].Text);
    }

    [Fact]
    public void FormulasAreChangedOnlyWhenTheNewTextParses()
    {
        string rows = "<row r=\"1\"><c r=\"A1\"><v>2</v></c><c r=\"B1\"><v>3</v></c></row>"
            + "<row r=\"2\"><c r=\"A2\"><f>B1*2</f><v>6</v></c><c r=\"B2\"><f>A1+B1</f><v>5</v></c>"
            + "<c r=\"C2\"><f t=\"array\" ref=\"C2:C3\">SUM(B1:B1)</f><v>3</v></c></row>"
            + "<row r=\"4\"><c r=\"A4\"><f t=\"shared\" ref=\"A4:B4\" si=\"0\">A1+1</f><v>3</v></c><c r=\"B4\"><f t=\"shared\" si=\"0\"/><v>4</v></c></row>";
        using Workbook wb = Open(rows, "<si><t>x</t></si>");
        var options = new WorkbookReplaceOptions { Formulas = true, Values = false };
        ReplaceReport r = wb.Replace("B1", "A1", options);
        Assert.Equal(["A1*2", "A1+A1", "SUM(A1:A1)"], new[] { wb.Sheets["Data"]["A2"].Formula, wb.Sheets["Data"]["B2"].Formula, wb.Sheets["Data"]["C2"].Formula });
        Assert.Equal(CellFormulaKind.Array, wb.Sheets["Data"]["C2"].FormulaDetail!.Kind);
        Assert.Equal("C2:C3", wb.Sheets["Data"]["C2"].FormulaDetail!.Reference);
        Assert.Equal(4, r.Count);
        Assert.All(r.Items, i => Assert.Equal("formula", i.Location.Story));

        // a replacement that breaks the formula is reported and not written
        r = wb.Replace("A1*2", "A1*(", options);
        Assert.Equal(0, r.Count);
        ReplaceIssue issue = Assert.Single(r.Issues);
        Assert.Equal("formula-invalid", issue.Code);
        Assert.Equal("A2", issue.Location.Address);
        Assert.Equal("A1*2", wb.Sheets["Data"]["A2"].Formula);

        // the text of a shared formula serves other cells too
        r = wb.Replace("A1+1", "A1+2", options);
        Assert.Equal("formula-shared", Assert.Single(r.Issues).Code);
        Assert.Equal("A1+1", wb.Sheets["Data"]["A4"].Formula);

        // formulas are left alone unless asked
        Assert.Empty(wb.Replace("A1", "B1", new WorkbookReplaceOptions { Values = false }).Items);
    }

    [Fact]
    public void SheetsCanBeChosenAndValuesCanBeSwitchedOff()
    {
        using Workbook wb = Open("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>", "<si><t>find me</t></si>");
        wb.Sheets["Other"]["B2"].SetString("find me too");
        Assert.Equal(2, wb.Find("find me").Count);
        IReadOnlyList<TextHit> only = wb.Find("find me", new WorkbookFindOptions { Sheets = ["other"] });
        TextHit hit = Assert.Single(only);
        Assert.Equal(new TextLocation("Other", "cell", 0, 0, "B2"), hit.Location);
        Assert.Empty(wb.Find("find me", new WorkbookFindOptions { Values = false }));
        Assert.Equal(1, wb.Replace("find me", "gone", new WorkbookReplaceOptions { Sheets = ["Data"] }).Count);
        Assert.Equal("find me too", wb.Sheets["Other"]["B2"].Text);
    }

    [Fact]
    public void RegexGroupsAndTheTimeoutAreTyped()
    {
        using Workbook wb = Open("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>", "<si><t>2024-05-06</t></si>");
        wb.Replace("(\\d+)-(\\d+)-(\\d+)", "$3.$2.$1", new WorkbookReplaceOptions { Search = new SearchOptions { UseRegex = true } });
        Assert.Equal("06.05.2024", wb.Sheets["Data"]["A1"].Text);

        var ctx = new OpenContext(new OfficeLimits { RegexMatchTimeoutMilliseconds = 40 });
        using Workbook slow = TestBook.Open(TestBook.Bytes(sheet1: Sheet("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>"), shared: Shared("<si><t>" + new string('a', 5000) + "!</t></si>")), ctx);
        var opt = new WorkbookFindOptions { Search = new SearchOptions { UseRegex = true } };
        Assert.Throws<OfficeLimitException>(() => slow.Find("^(a+)+\\1?$", opt));
    }

    [Fact]
    public void SamplesForExternalChecksAreWritten()
    {
        string dir = Path.Combine(Path.GetTempPath(), "operation-samples");
        Directory.CreateDirectory(dir);
        string rows = "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\"><v>2</v></c></row><row r=\"2\"><c r=\"A2\"><f>B1*2</f><v>4</v></c></row>";
        using Workbook wb = Open(rows, "<si><r><t>Rich</t></r><r><rPr><b/></rPr><t> text</t></r></si>");
        wb.Replace("ch te", "X");
        wb.Replace("B1", "B3", new WorkbookReplaceOptions { Formulas = true, Values = false });
        wb.Sheets["Data"].Protection.Protect(SheetActions.FormatCells | SheetActions.SelectLockedCells | SheetActions.SelectUnlockedCells, "sample", new RecursiveAI.OneDocument.Protection.ProtectionOptions { SpinCount = 1000 });
        wb.Sheets["Data"].Protection.AddProtectedRange("Inputs", "B1:B3", "range", new RecursiveAI.OneDocument.Protection.ProtectionOptions { SpinCount = 1000 });
        wb.Protection.Protect(true, true, "book", new RecursiveAI.OneDocument.Protection.ProtectionOptions { SpinCount = 1000 });
        wb.FileSharing.Set("reserve", true, "Sample", new RecursiveAI.OneDocument.Protection.ProtectionOptions { SpinCount = 1000 });
        wb.SaveAs(Path.Combine(dir, "replace-protect.xlsx"));
    }

    [Fact]
    public void OtherPartsAreUntouchedAndTheFileReopens()
    {
        byte[] original = TestBook.Bytes(sheet1: Sheet("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>"), shared: Shared("<si><t>abc</t></si>"));
        using Workbook wb = TestBook.Open(original);
        Assert.Equal(1, wb.Replace("b", "BB").Count);
        byte[] file = TestBook.Save(wb);
        Dictionary<string, string> before = TestBook.PartHashes(original);
        Dictionary<string, string> after = TestBook.PartHashes(file);
        Assert.Equal(before["/xl/styles.xml"], after["/xl/styles.xml"]);
        Assert.Equal(before["/xl/worksheets/sheet2.xml"], after["/xl/worksheets/sheet2.xml"]);
        Assert.Equal("aBBc", TestBook.Open(file).Sheets["Data"]["A1"].Text);
        Assert.Contains("<mergeCell ref=\"A9:B9\"/>", Part(file, "/xl/worksheets/sheet1.xml"));
    }
}
