using System.Runtime.CompilerServices;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Spreadsheets.Odf;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class OdsTests
{
    static OdsTests() => SpreadsheetFormats.Register(FormatRegistry.Default);

    private static string TestData([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "../../testdata/ods"));

    private const string Header =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
        "xmlns:number=\"urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" " +
        "xmlns:calcext=\"urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0\" xmlns:config=\"urn:oasis:names:tc:opendocument:xmlns:config:1.0\" " +
        "office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.spreadsheet\">";

    private static Workbook Fods(string body, string styles = "", string settings = "", OpenContext? context = null)
    {
        string xml = Header + (settings.Length > 0 ? "<office:settings>" + settings + "</office:settings>" : string.Empty) +
            "<office:automatic-styles>" + styles + "</office:automatic-styles><office:body><office:spreadsheet>" + body + "</office:spreadsheet></office:body></office:document>";
        return Workbook.Open(new MemoryStream(Encoding.UTF8.GetBytes(xml)), context ?? new OpenContext(), leaveOpen: false);
    }

    private static string[] Codes(OpenContext ctx) => [.. ((DefectCollection)ctx.Defects).Select(static d => d.Code)];

    private static Dictionary<string, string> Parts(byte[] zip)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.IO.Compression.ZipArchiveEntry e in archive.Entries)
        {
            using var reader = new StreamReader(e.Open(), Encoding.UTF8);
            result[e.FullName] = reader.ReadToEnd();
        }

        return result;
    }

    // ---- a file written by a spreadsheet application ----------------------------------------------------------------------------------------------

    [Fact]
    public void Reads_a_workbook_written_by_a_spreadsheet_application()
    {
        var ctx = new OpenContext();
        using Workbook wb = Workbook.Open(Path.Combine(TestData(), "ods-rich.ods"), ctx);
        Assert.Equal(OfficeFormat.Ods, wb.Format);
        Assert.Equal(["Values", "Other", "My Sheet"], wb.Sheets.Select(static s => s.Name));
        Assert.Equal(SheetState.Hidden, wb.Sheets["Other"].State);

        Worksheet v = wb.Sheets["Values"];
        Assert.Equal("Text", v["A1"].Text);
        Assert.Equal(1234.5, v["B1"].Number);
        Assert.True(v["C1"].Value.Boolean);
        Assert.Equal(45366.57326388889, v["D1"].Number, 9);
        Assert.Equal(0.3541666666666667, v["E1"].Number, 12);
        Assert.Equal("B1*2", v["H1"].Formula);
        Assert.Equal(2469, v["H1"].Number);
        Assert.Equal("Other!A1+1", v["L1"].Formula);
        Assert.Equal(CellError.NotAvailable, v["N1"].Value.Error);

        // Cached values stay as the file has them: LibreOffice's #VALUE! for the implicit intersection is kept, not recalculated.
        Assert.Equal(CellError.Value, v["M1"].Value.Error);

        Assert.Equal("#,##0.00", v["B2"].Style.NumberFormatCode);
        Assert.Equal("0.0%", v["C2"].Style.NumberFormatCode);
        Assert.Equal("$#,##0.00", v["D2"].Style.NumberFormatCode);
        Assert.Equal("0.00E+00", v["E2"].Style.NumberFormatCode);
        Assert.Equal("dd/mm/yyyy", v["F2"].Style.NumberFormatCode);
        Assert.Equal("# ?/?", v["H2"].Style.NumberFormatCode);
        Assert.Equal("[Red]0.00;[Blue]-0.00;\"zero\"", v["I2"].Style.NumberFormatCode);
        Assert.Equal("@", v["J2"].Style.NumberFormatCode);
        Assert.Equal("h:mm:ss AM/PM", v["K2"].Style.NumberFormatCode);
        Assert.StartsWith("[>1]", v["L2"].Style.NumberFormatCode, StringComparison.Ordinal);

        ResolvedCellStyle bold = v["A3"].Style;
        Assert.True(bold.Font.Bold);
        Assert.True(bold.Font.Italic);
        Assert.True(bold.Font.Strike);
        Assert.Equal(14, bold.Font.Size);
        Assert.Equal("Arial", bold.Font.Name);
        Assert.Equal(CellHorizontalAlignment.Center, bold.Alignment.Horizontal);
        Assert.Equal(CellVerticalAlignment.Top, bold.Alignment.Vertical);
        Assert.True(bold.Alignment.WrapText);
        Assert.Equal(CellFill.Solid(StyleColor.FromRgb(0xFFFF00)).Foreground.Value, bold.Fill.Foreground.Value);
        Assert.Equal(BorderLineStyle.Thin, v["B3"].Style.Border.Left.Style);
        Assert.Equal(BorderLineStyle.Thick, v["B3"].Style.Border.Top.Style);
        Assert.False(v["C3"].Style.Protection.Locked);
        Assert.Equal(45, v["D3"].Style.Alignment.TextRotation);

        Assert.Equal("A5:C6", string.Join(",", v.MergedCells.Select(static m => m.TopLeft + ":" + m.BottomRight)).Split(',')[0]);
        Assert.True(v.GetRow(9).Hidden);
        Assert.Equal(30 * 1.0, v.GetRow(10).Height, 0);
        Assert.Contains(v.Columns, static c => c.Min == 3 && (c.Flags & ColumnOptions.Hidden) != 0);
        Assert.Equal("line1\nline2", v["A14"].Text);
        Assert.Equal("  spaced   text  ", v["A15"].Text);
        Assert.Equal("tab\there", v["A16"].Text);

        Assert.Equal(4, v.Validations.Count);
        Assert.Equal(7, v.ConditionalFormats.Count);
        Assert.Equal(2, wb.Names.Count(static n => !n.IsBuiltIn && n.LocalSheetId is null));
        Assert.Contains(wb.Names, static n => n.BuiltIn == BuiltInName.PrintArea && n.Formula == "Values!$A$1:$F$30");
        Assert.Contains(wb.Names, static n => n.Name == "Local" && n.LocalSheetId == 2);
        Assert.Equal(CellError.DivideByZero, wb.Sheets["My Sheet"]["C2"].Value.Error);
        Assert.Contains("autoFilter", v.LayoutMarkup.OuterXml, StringComparison.Ordinal);
        Assert.Contains("landscape", v.LayoutMarkup.OuterXml, StringComparison.Ordinal);
        Assert.Contains("Header text", v.LayoutMarkup.OuterXml, StringComparison.Ordinal);

        Assert.Equal("Rich ODS source", new RecursiveAI.OneDocument.Properties.DocumentProperties(wb.Package).Core.Title);
        Assert.DoesNotContain(Codes(ctx), static c => c is "ods.bad-formula" or "ods.style-missing");
    }

    [Fact]
    public void An_untouched_file_saves_its_own_bytes_and_an_edit_writes_a_new_one()
    {
        string path = Path.Combine(TestData(), "ods-rich.ods");
        byte[] original = File.ReadAllBytes(path);
        using Workbook wb = Workbook.Open(path);
        using var same = new MemoryStream();
        wb.SaveAs(same, OfficeFormat.Ods);
        Assert.Equal(original, same.ToArray());

        wb.Sheets["Values"]["A1"].SetString("Changed");
        using var edited = new MemoryStream();
        wb.SaveAs(edited, OfficeFormat.Ods);
        Assert.NotEqual(original, edited.ToArray());
        using Workbook again = Workbook.Open(new MemoryStream(edited.ToArray()));
        Assert.Equal("Changed", again.Sheets["Values"]["A1"].Text);
        Assert.Equal(1234.5, again.Sheets["Values"]["B1"].Number);
        Assert.Equal("B1*2", again.Sheets["Values"]["H1"].Formula);
    }

    [Fact]
    public void Saving_to_a_flat_file_goes_through_the_exporter()
    {
        using Workbook wb = Workbook.Open(Path.Combine(TestData(), "ods-rich.ods"));
        using var flat = new MemoryStream();
        wb.SaveAs(flat, OfficeFormat.Fods);
        string xml = Encoding.UTF8.GetString(flat.ToArray());
        Assert.Contains("office:document ", xml, StringComparison.Ordinal);
        using Workbook back = Workbook.Open(new MemoryStream(flat.ToArray()));
        Assert.Equal(OfficeFormat.Fods, back.Format);
        Assert.Equal(1234.5, back.Sheets["Values"]["B1"].Number);
        Assert.Equal("#,##0.00", back.Sheets["Values"]["B2"].Style.NumberFormatCode);
        Assert.Equal(4, back.Sheets["Values"].Validations.Count);
    }

    [Fact]
    public void Reads_a_flat_file_a_spreadsheet_application_wrote()
    {
        using Workbook wb = Workbook.Open(Path.Combine(TestData(), "ods-rich.fods"));
        Assert.Equal(OfficeFormat.Fods, wb.Format);
        Assert.Equal(1234.5, wb.Sheets["Values"]["B1"].Number);
        Assert.Equal("B1*2", wb.Sheets["Values"]["H1"].Formula);
    }

    // ---- constructs the application does not write -------------------------------------------------------------------------------------------------

    [Fact]
    public void Reads_value_types_repeats_spans_and_arrays()
    {
        var ctx = new OpenContext();
        using Workbook wb = Fods(
            "<table:calculation-settings><table:null-date table:value-type=\"date\" table:date-value=\"1904-01-01\"/></table:calculation-settings>" +
            "<table:table table:name=\"S\"><table:table-column table:number-columns-repeated=\"4\"/>" +
            "<table:table-row table:number-rows-repeated=\"2\"><table:table-cell office:value-type=\"float\" office:value=\"1\" table:number-columns-repeated=\"2\"/><table:table-cell office:value-type=\"percentage\" office:value=\"0.5\"/>" +
            "<table:table-cell office:value-type=\"currency\" office:currency=\"USD\" office:value=\"9.5\"/></table:table-row>" +
            "<table:table-row><table:table-cell office:value-type=\"time\" office:time-value=\"PT30H15M00S\"/><table:table-cell office:value-type=\"date\" office:date-value=\"2024-01-02\"/>" +
            "<table:table-cell office:value-type=\"boolean\" office:boolean-value=\"true\"/><table:table-cell office:value-type=\"string\"><text:p>a</text:p><text:p>b<text:s text:c=\"3\"/>c<text:tab/>d</text:p></table:table-cell></table:table-row>" +
            "<table:table-row><table:table-cell table:formula=\"of:=SUM([.A1:.B1])\" office:value-type=\"float\" office:value=\"2\" table:number-matrix-columns-spanned=\"2\" table:number-matrix-rows-spanned=\"2\"/><table:covered-table-cell office:value-type=\"float\" office:value=\"7\"/></table:table-row>" +
            "<table:table-row><table:covered-table-cell office:value-type=\"float\" office:value=\"8\"/><table:covered-table-cell office:value-type=\"float\" office:value=\"9\"/><table:table-cell table:number-columns-spanned=\"2\" table:number-rows-spanned=\"2\" office:value-type=\"string\"><text:p>m</text:p></table:table-cell><table:covered-table-cell/></table:table-row>" +
            "<table:table-row><table:table-cell table:number-columns-repeated=\"2\"/><table:covered-table-cell/><table:covered-table-cell/></table:table-row>" +
            "<table:table-row table:number-rows-repeated=\"1000000\"><table:table-cell table:number-columns-repeated=\"1024\"/></table:table-row>" +
            "</table:table>",
            context: ctx);
        Worksheet s = wb.Sheets["S"];
        Assert.Equal(DateSystem.Date1904, wb.DateSystem);
        Assert.Equal(1, s["B2"].Number);
        Assert.Equal(1, s["A2"].Number);
        Assert.Equal(0.5, s["C1"].Number);
        Assert.Equal(9.5, s["D2"].Number);
        Assert.Equal(30.25 / 24, s["A3"].Number, 12);
        Assert.True(s["C3"].Value.Boolean);
        Assert.Equal("a\nb   c\td", s["D3"].Text);
        Assert.Equal(DateSystem.Date1904, wb.DateSystem);

        CellFormula? array = s["A4"].FormulaDetail;
        Assert.Equal(CellFormulaKind.Array, array?.Kind);
        Assert.Equal("A4:B5", array?.Reference);
        Assert.Equal(7, s["B4"].Number);
        Assert.Equal(8, s["A5"].Number);
        Assert.Contains(s.MergedCells, static m => m.TopLeft.ToString() == "C5" && m.BottomRight.ToString() == "D6");
        Assert.Equal(CellValueKind.Empty, s["A7"].Value.Kind);
        Assert.Equal(5, s.UsedRange().BottomRight.Row);
    }

    [Fact]
    public void The_null_date_sets_the_date_system()
    {
        using Workbook wb = Fods(
            "<table:calculation-settings><table:null-date table:value-type=\"date\" table:date-value=\"1904-01-01\"/></table:calculation-settings>" +
            "<table:table table:name=\"S\"><table:table-row><table:table-cell office:value-type=\"date\" office:date-value=\"1904-01-02\"/></table:table-row></table:table>");
        Assert.Equal(DateSystem.Date1904, wb.DateSystem);
        Assert.Equal(1, wb.Sheets["S"]["A1"].Number);
    }

    [Fact]
    public void Formulas_in_other_dialects_and_bad_formulas_keep_their_cached_values_and_say_so()
    {
        var ctx = new OpenContext();
        using Workbook wb = Fods(
            "<table:table table:name=\"S\"><table:table-row>" +
            "<table:table-cell table:formula=\"msoxl:=SUM(A2:B2)\" office:value-type=\"float\" office:value=\"3\"/>" +
            "<table:table-cell table:formula=\"of:=[.A1\" office:value-type=\"float\" office:value=\"4\"/>" +
            "<table:table-cell table:formula=\"oooc:=A1\" office:value-type=\"float\" office:value=\"5\"/>" +
            "<table:table-cell table:formula=\"of:=['file:///other.ods'#$Sheet1.A1]\" office:value-type=\"float\" office:value=\"6\"/>" +
            "</table:table-row></table:table>",
            context: ctx);
        Worksheet s = wb.Sheets["S"];
        Assert.Equal("SUM(A2:B2)", s["A1"].Formula);
        Assert.Null(s["B1"].Formula);
        Assert.Equal(4, s["B1"].Number);
        Assert.Equal(5, s["C1"].Number);
        Assert.Equal(6, s["D1"].Number);
        string[] codes = Codes(ctx);
        Assert.Equal(2, codes.Count(static c => c == "ods.bad-formula"));
        Assert.Contains("ods.formula-dialect", codes);
        Assert.Equal(3, wb.ImportLoss!.CountOf("loss.formulas"));
        Assert.All(((DefectCollection)ctx.Defects).Where(static d => d.Code == "ods.bad-formula"), static d => Assert.Equal("S", d.Location.Container));
    }

    [Fact]
    public void Missing_styles_and_bad_values_are_repaired_with_a_located_defect()
    {
        var ctx = new OpenContext();
        using Workbook wb = Fods(
            "<table:table table:name=\"S\"><table:table-row>" +
            "<table:table-cell table:style-name=\"nope\" office:value-type=\"float\" office:value=\"x\"><text:p>shown</text:p></table:table-cell>" +
            "<table:table-cell office:value-type=\"date\" office:date-value=\"not a date\"><text:p>d</text:p></table:table-cell>" +
            "<table:table-cell office:value-type=\"mystery\"><text:p>m</text:p></table:table-cell>" +
            "</table:table-row></table:table>",
            context: ctx);
        Worksheet s = wb.Sheets["S"];
        Assert.Equal("shown", s["A1"].Text);
        Assert.Equal("d", s["B1"].Text);
        Assert.Equal("m", s["C1"].Text);
        string[] codes = Codes(ctx);
        Assert.Contains("ods.style-missing", codes);
        Assert.Contains("ods.bad-number", codes);
        Assert.Contains("ods.bad-date", codes);
        Assert.Contains("ods.bad-value-type", codes);
    }

    [Fact]
    public void Sheet_names_are_made_valid_and_unique()
    {
        var ctx = new OpenContext();
        using Workbook wb = Fods(
            "<table:table table:name=\"A/B\"/><table:table table:name=\"a/b\"/><table:table table:name=\"\"/><table:table table:name=\"" + new string('x', 40) + "\"/>", context: ctx);
        Assert.Equal(4, wb.Sheets.Count);
        Assert.Equal(4, wb.Sheets.Select(static s => s.Name.ToUpperInvariant()).Distinct().Count());
        Assert.All(wb.Sheets, static s => Assert.True(s.Name.Length is > 0 and <= 31));
        Assert.Contains("ods.sheet-name", Codes(ctx));
    }

    [Fact]
    public void Freeze_panes_and_the_active_sheet_come_from_the_settings()
    {
        using Workbook wb = Fods(
            "<table:table table:name=\"One\"/><table:table table:name=\"Two\"><table:table-row><table:table-cell office:value-type=\"float\" office:value=\"1\"/></table:table-row></table:table>",
            settings: "<config:config-item-set config:name=\"ooo:view-settings\"><config:config-item-map-indexed config:name=\"Views\"><config:config-item-map-entry>" +
                "<config:config-item config:name=\"ActiveTable\" config:type=\"string\">Two</config:config-item>" +
                "<config:config-item-map-named config:name=\"Tables\"><config:config-item-map-entry config:name=\"Two\">" +
                "<config:config-item config:name=\"HorizontalSplitMode\" config:type=\"short\">2</config:config-item><config:config-item config:name=\"VerticalSplitMode\" config:type=\"short\">2</config:config-item>" +
                "<config:config-item config:name=\"HorizontalSplitPosition\" config:type=\"int\">1</config:config-item><config:config-item config:name=\"VerticalSplitPosition\" config:type=\"int\">2</config:config-item>" +
                "</config:config-item-map-entry></config:config-item-map-named></config:config-item-map-entry></config:config-item-map-indexed></config:config-item-set>");
        Assert.True(wb.Sheets["Two"].TryGetFreezePane(out int rows, out int columns));
        Assert.Equal(2, rows);
        Assert.Equal(1, columns);
        Assert.False(wb.Sheets["One"].TryGetFreezePane(out _, out _));
        Assert.Contains("tabSelected", wb.Sheets["Two"].LayoutMarkup.OuterXml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cell_budget_stops_a_repeat_bomb()
    {
        var ctx = new OpenContext(new OfficeLimits { MaxRecordCount = 1000 });
        string body = "<table:table table:name=\"S\"><table:table-row table:number-rows-repeated=\"100000\"><table:table-cell office:value-type=\"float\" office:value=\"1\" table:number-columns-repeated=\"100\"/></table:table-row></table:table>";
        Assert.Throws<OfficeLimitException>(() => Fods(body, context: ctx));
    }

    [Fact]
    public void Empty_repeated_rows_cost_nothing()
    {
        using Workbook wb = Fods(
            "<table:table table:name=\"S\"><table:table-row table:number-rows-repeated=\"1048000\"><table:table-cell table:number-columns-repeated=\"16384\"/></table:table-row>" +
            "<table:table-row><table:table-cell office:value-type=\"float\" office:value=\"5\"/></table:table-row></table:table>");
        Assert.Equal(5, wb.Sheets["S"]["A1048001"].Number);
    }

    [Fact]
    public void Data_styles_read_as_number_formats()
    {
        using Workbook wb = Fods(
            "<table:table table:name=\"S\"><table:table-row>" +
            "<table:table-cell table:style-name=\"a\" office:value-type=\"float\" office:value=\"1\"/>" +
            "<table:table-cell table:style-name=\"b\" office:value-type=\"float\" office:value=\"1\"/>" +
            "<table:table-cell table:style-name=\"c\" office:value-type=\"float\" office:value=\"1\"/>" +
            "<table:table-cell table:style-name=\"d\" office:value-type=\"float\" office:value=\"1\"/>" +
            "<table:table-cell table:style-name=\"e\" office:value-type=\"float\" office:value=\"1\"/>" +
            "</table:table-row></table:table>",
            styles:
                "<number:number-style style:name=\"N1\"><number:number number:decimal-places=\"3\" number:min-decimal-places=\"1\" number:min-integer-digits=\"2\" number:grouping=\"true\"/></number:number-style>" +
                "<number:percentage-style style:name=\"N2\"><number:number number:decimal-places=\"0\" number:min-integer-digits=\"1\"/><number:text>%</number:text></number:percentage-style>" +
                "<number:currency-style style:name=\"N3\"><number:currency-symbol>€</number:currency-symbol><number:text> </number:text><number:number number:decimal-places=\"2\" number:min-integer-digits=\"1\"/></number:currency-style>" +
                "<number:time-style style:name=\"N4\" number:truncate-on-overflow=\"false\"><number:hours number:style=\"long\" number:truncate-on-overflow=\"false\"/><number:text>:</number:text><number:minutes number:style=\"long\"/><number:text>:</number:text><number:seconds number:style=\"long\" number:decimal-places=\"2\"/></number:time-style>" +
                "<number:number-style style:name=\"N5P0\"><style:text-properties fo:color=\"#ff0000\"/><number:text>-</number:text><number:number number:decimal-places=\"1\" number:min-integer-digits=\"1\"/></number:number-style>" +
                "<number:number-style style:name=\"N5\"><number:number number:decimal-places=\"1\" number:min-integer-digits=\"1\"/><style:map style:condition=\"value()&lt;0\" style:apply-style-name=\"N5P0\"/></number:number-style>" +
                "<style:style style:name=\"a\" style:family=\"table-cell\" style:data-style-name=\"N1\"/><style:style style:name=\"b\" style:family=\"table-cell\" style:data-style-name=\"N2\"/>" +
                "<style:style style:name=\"c\" style:family=\"table-cell\" style:data-style-name=\"N3\"/><style:style style:name=\"d\" style:family=\"table-cell\" style:data-style-name=\"N4\"/>" +
                "<style:style style:name=\"e\" style:family=\"table-cell\" style:data-style-name=\"N5\"/>");
        Worksheet s = wb.Sheets["S"];
        Assert.Equal("#,00.0##", s["A1"].Style.NumberFormatCode);
        Assert.Equal("0%", s["B1"].Style.NumberFormatCode);
        Assert.Equal("[$€] 0.00", s["C1"].Style.NumberFormatCode);
        Assert.Equal("[hh]:mm:ss.00", s["D1"].Style.NumberFormatCode);
        Assert.Equal("0.0;[Red]-0.0", s["E1"].Style.NumberFormatCode);
    }

    // ---- writing ------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Writes_a_workbook_built_in_memory()
    {
        using Workbook wb = Workbook.Create();
        Worksheet ws = wb.Sheets.Add("Data");
        ws["A1"].SetString("name");
        ws["B1"].SetNumber(2.5);
        ws["C1"].SetBoolean(true);
        ws["A2"].Formula = "B1*2";
        ws["B2"].Formula = "SUM(B1:B1)+Data!B1";
        ws["C2"].Formula = "LAMBDA(x,x+1)(2)";
        ws["A3"].SetString("  indented\nline");
        ws.Merge(CellRange.Parse("D1:E2"));
        ws.Validations.Add(CellRange.Parse("A5:A8"), DataValidationSpec.WholeBetween(1, 10));
        wb.Names.Add("Rate", "Data!$B$1");
        wb.Names.Add("Twice", "Data!$B$1*2");

        using var ods = new MemoryStream();
        wb.SaveAs(ods, OfficeFormat.Ods);
        Assert.NotNull(wb.ExportLoss);
        Assert.Equal(1, wb.ExportLoss!.CountOf("loss.formulas"));

        byte[] bytes = ods.ToArray();
        Dictionary<string, string> parts = Parts(bytes);
        Assert.Equal("application/vnd.oasis.opendocument.spreadsheet", parts["mimetype"]);
        Assert.Contains("xmlns:of=\"urn:oasis:names:tc:opendocument:xmlns:of:1.2\"", parts["content.xml"], StringComparison.Ordinal);
        Assert.Contains("table:formula=\"of:=[.B1]*2\"", parts["content.xml"], StringComparison.Ordinal);
        Assert.Contains("table:formula=\"of:=SUM([.B1:.B1])+[$Data.B1]\"", parts["content.xml"], StringComparison.Ordinal);

        using Workbook back = Workbook.Open(new MemoryStream(bytes));
        Worksheet b = back.Sheets["Data"];
        Assert.Equal("name", b["A1"].Text);
        Assert.Equal(2.5, b["B1"].Number);
        Assert.True(b["C1"].Value.Boolean);
        Assert.Equal("B1*2", b["A2"].Formula);
        Assert.Equal("SUM(B1:B1)+Data!B1", b["B2"].Formula);
        Assert.Null(b["C2"].Formula);
        Assert.Equal("  indented\nline", b["A3"].Text);
        Assert.Contains(b.MergedCells, static m => m.TopLeft.ToString() == "D1" && m.BottomRight.ToString() == "E2");
        Assert.Single(b.Validations);
        Assert.Equal(DataValidationType.WholeNumber, b.Validations.First().Type);
        Assert.Contains(back.Names, static n => n.Name == "Rate" && n.Formula == "Data!$B$1");
        Assert.Contains(back.Names, static n => n.Name == "Twice" && n.Formula == "Data!$B$1*2");
    }

    [Fact]
    public void A_template_writes_its_own_media_type()
    {
        using Workbook wb = Workbook.Create();
        wb.Sheets.Add("S")["A1"].SetNumber(1);
        using var ots = new MemoryStream();
        wb.SaveAs(ots, OfficeFormat.Ots);
        Assert.Equal(OdfMediaTypes.SpreadsheetTemplate, Parts(ots.ToArray())["mimetype"]);
        using Workbook back = Workbook.Open(new MemoryStream(ots.ToArray()));
        Assert.Equal(OfficeFormat.Ots, back.Format);
    }

    [Fact]
    public void The_registry_opens_and_converts_both_ways()
    {
        string path = Path.Combine(TestData(), "ods-rich.ods");
        string text = Office.ExtractText(path);
        Assert.Contains("Bold red on yellow", text, StringComparison.Ordinal);
        string tmp = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "ods-convert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            string xlsx = Path.Combine(tmp, "a.xlsx");
            Office.Convert(path, xlsx);
            using (Workbook x = Workbook.Open(xlsx))
            {
                Assert.Equal(OfficeFormat.Xlsx, x.Format);
                Assert.Equal("B1*2", x.Sheets["Values"]["H1"].Formula);
            }

            string back = Path.Combine(tmp, "b.ods");
            Office.Convert(xlsx, back);
            using Workbook b = Workbook.Open(back);
            Assert.Equal(OfficeFormat.Ods, b.Format);
            Assert.Equal(1234.5, b.Sheets["Values"]["B1"].Number);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void A_document_that_is_not_a_spreadsheet_is_a_typed_error()
    {
        using var text = new MemoryStream(Encoding.UTF8.GetBytes(Header.Replace("spreadsheet\"", "text\"", StringComparison.Ordinal) + "<office:body><office:text/></office:body></office:document>"));
        Assert.Throws<FormatNotAvailableException>(() => Workbook.Open(text));
    }

    [Fact]
    public void Garbage_after_a_valid_start_is_a_typed_error()
    {
        byte[] truncated = File.ReadAllBytes(Path.Combine(TestData(), "ods-rich.fods"))[..5000];
        Assert.ThrowsAny<OfficeException>(() => Workbook.Open(new MemoryStream(truncated)));
    }

    [Fact]
    public void The_converter_is_public_and_reports_losses()
    {
        string body = "<table:table table:name=\"S\"><table:shapes><draw:frame xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\"/></table:shapes>" +
            "<table:table-row><table:table-cell office:value-type=\"string\"><text:p>a</text:p></table:table-cell></table:table-row></table:table><table:data-pilot-tables/>";
        string xml = Header + "<office:body><office:spreadsheet>" + body + "</office:spreadsheet></office:body></office:document>";
        var loss = new LossReport();
        using RecursiveAI.OneDocument.Packaging.OpcPackage package = OpenDocumentSheet.ToPackage(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(Encoding.UTF8.GetBytes(xml)), null, loss);
        Assert.NotNull(package.FindPart("/xl/workbook.xml"));
        Assert.True(loss.CountOf("loss.shapes") > 0);
        Assert.True(loss.CountOf("loss.data-pilot-tables") > 0);
    }

    [Fact]
    public void Damaged_counts_names_limits_and_validations_are_located_defects()
    {
        var ctx = new OpenContext();
        using Workbook wb = Fods(
            "<table:table table:name=\"S\"><table:table-row>" +
            "<table:table-cell office:value-type=\"time\" office:time-value=\"later\"><text:p>t</text:p></table:table-cell>" +
            "<table:table-cell table:number-columns-repeated=\"minus\" office:value-type=\"float\" office:value=\"1\"/>" +
            "<table:table-cell table:content-validation-name=\"ghost\" office:value-type=\"float\" office:value=\"2\"/>" +
            "<table:table-cell table:number-columns-repeated=\"16384\"/><table:table-cell office:value-type=\"float\" office:value=\"3\"/>" +
            "</table:table-row>" +
            "<table:table-row table:number-rows-repeated=\"1048576\"><table:table-cell office:value-type=\"float\" office:value=\"4\"/></table:table-row>" +
            "</table:table>" +
            "<table:named-expressions><table:named-range table:name=\"NoRange\"/><table:named-expression table:name=\"1 bad name\" table:expression=\"of:=1\"/></table:named-expressions>",
            context: ctx);
        Assert.True(wb.Sheets.Count >= 1);
        string[] codes = Codes(ctx);
        foreach (string code in new[] { "ods.bad-time", "ods.bad-repeat", "ods.validation-missing", "ods.column-limit", "ods.row-limit", "ods.bad-name", "ods.name" })
        {
            Assert.Contains(code, codes);
        }
    }

    [Fact]
    public void A_spreadsheet_body_without_tables_gets_an_empty_sheet_and_a_text_body_is_refused()
    {
        var ctx = new OpenContext();
        using Workbook wb = Fods(string.Empty, context: ctx);
        Assert.Single(wb.Sheets);
        Assert.Contains("ods.no-sheets", Codes(ctx));

        string text = Header.Replace("application/vnd.oasis.opendocument.spreadsheet", "application/vnd.oasis.opendocument.text", StringComparison.Ordinal) +
            "<office:body><office:text><text:p>hello</text:p></office:text></office:body></office:document>";
        var loss = new LossReport();
        OfficeFormatException e = Assert.Throws<OfficeFormatException>(() =>
            OpenDocumentSheet.ToPackage(RecursiveAI.OneDocument.IO.ByteSource.FromMemory(Encoding.UTF8.GetBytes(text)), null, loss));
        Assert.Equal("ods.not-spreadsheet", e.Defect.Code);
    }
}
