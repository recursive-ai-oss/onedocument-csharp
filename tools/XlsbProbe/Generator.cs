using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets;

/// <summary>
/// Writes binary workbooks with our own writer, from SpreadsheetML written by hand, so that Excel can be asked whether it opens them
/// without a repair prompt and shows what the markup says. Each file covers one area; the list is in <c>manifest.tsv</c>.
/// </summary>
internal static class Generator
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

    private sealed class Book
    {
        public string Workbook = string.Empty;
        public List<string> Sheets = [];
        public string? SharedStrings;
        public Dictionary<string, (string Type, string Xml)> Extra = [];
        public Dictionary<int, List<(string Id, string Type, string Target)>> SheetRels = [];
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var manifest = new StringBuilder("file\twhat it covers\tsha256\n");
        foreach ((string name, string what, Book book) in Cases())
        {
            byte[] bytes = Build(book);
            File.WriteAllBytes(Path.Combine(dir, name), bytes);
            manifest.Append(name).Append('\t').Append(what).Append('\t').Append(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()).Append('\n');
            Console.WriteLine(name + " " + bytes.Length);
        }

        File.WriteAllText(Path.Combine(dir, "manifest.tsv"), manifest.ToString());
        return 0;
    }

    private static int _count;

    private static byte[] Build(Book b)
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Encoding.UTF8.GetBytes(Decl + b.Workbook));
        var wbRels = new List<Relationship>();
        for (int i = 0; i < b.Sheets.Count; i++)
        {
            wbRels.Add(new Relationship("rId" + (i + 1), Rel + "worksheet", "worksheets/sheet" + (i + 1) + ".xml"));
            p.AddPart("/xl/worksheets/sheet" + (i + 1) + ".xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", Encoding.UTF8.GetBytes(Decl + b.Sheets[i]));
            if (b.SheetRels.TryGetValue(i + 1, out var rels))
            {
                p.SetRelationships("/xl/worksheets/sheet" + (i + 1) + ".xml", rels.ConvertAll(r => new Relationship(r.Id, r.Type, r.Target, r.Target.StartsWith("http", StringComparison.Ordinal) ? RelationshipTargetMode.External : RelationshipTargetMode.Internal)));
            }
        }

        int n = b.Sheets.Count;
        wbRels.Add(new Relationship("rId" + (n + 1), Rel + "styles", "styles.xml"));
        p.AddPart("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", Encoding.UTF8.GetBytes(Decl + DefaultStyles));
        if (b.SharedStrings is not null)
        {
            wbRels.Add(new Relationship("rId" + (n + 2), Rel + "sharedStrings", "sharedStrings.xml"));
            p.AddPart("/xl/sharedStrings.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml", Encoding.UTF8.GetBytes(Decl + b.SharedStrings));
        }

        foreach ((string name, (string type, string xml)) in b.Extra)
        {
            p.AddPart(name, type, Encoding.UTF8.GetBytes(Decl + xml));
        }

        p.SetRelationships("/xl/workbook.xml", wbRels);
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        if (Environment.GetEnvironmentVariable("XLSB_GEN_XLSX") is { Length: > 0 } xlsxDir)
        {
            Directory.CreateDirectory(xlsxDir);
            File.WriteAllBytes(Path.Combine(xlsxDir, "case" + (++_count) + ".xlsx"), ms.ToArray());
        }

        using Workbook wb = Workbook.Open(new MemoryStream(ms.ToArray()));
        using var bin = new MemoryStream();
        wb.SaveAs(bin, OfficeFormat.Xlsb, new LossReport());
        return bin.ToArray();
    }

    private const string DefaultStyles = "<styleSheet xmlns=\"" + Main + "\">"
        + "<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\"/><numFmt numFmtId=\"165\" formatCode=\"#,##0.00&quot; EUR&quot;\"/></numFmts>"
        + "<fonts count=\"3\"><font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font><font><b/><sz val=\"11\"/><color rgb=\"FFFF0000\"/><name val=\"Calibri\"/><family val=\"2\"/></font><font><i/><u/><sz val=\"14\"/><color theme=\"1\"/><name val=\"Arial\"/><family val=\"2\"/></font></fonts>"
        + "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFFF00\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>"
        + "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style=\"thin\"><color auto=\"1\"/></left><right style=\"thin\"><color auto=\"1\"/></right><top style=\"medium\"><color rgb=\"FF0000FF\"/></top><bottom style=\"double\"><color auto=\"1\"/></bottom><diagonal/></border></borders>"
        + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
        + "<cellXfs count=\"6\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
        + "<xf numFmtId=\"165\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>"
        + "<xf numFmtId=\"10\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>"
        + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"top\" wrapText=\"1\" textRotation=\"45\" indent=\"1\"/></xf>"
        + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyProtection=\"1\"><protection locked=\"0\" hidden=\"1\"/></xf></cellXfs>"
        + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>"
        + "<dxfs count=\"3\"><dxf><font><b/><color rgb=\"FF9C0006\"/></font><fill><patternFill><bgColor rgb=\"FFFFC7CE\"/></patternFill></fill></dxf><dxf><numFmt numFmtId=\"2\" formatCode=\"0.00\"/></dxf><dxf><border><left style=\"thin\"><color auto=\"1\"/></left></border></dxf></dxfs>"
        + "<tableStyles count=\"0\" defaultTableStyle=\"TableStyleMedium9\" defaultPivotStyle=\"PivotStyleLight16\"/></styleSheet>";

    private static string Sheet(string body, string head = "") => "<worksheet xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\">" + head + body + "</worksheet>";

    private static string WorkbookXml(params string[] sheetNames)
    {
        var sb = new StringBuilder("<workbook xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\"><sheets>");
        for (int i = 0; i < sheetNames.Length; i++)
        {
            sb.Append("<sheet name=\"").Append(sheetNames[i]).Append("\" sheetId=\"").Append(i + 1).Append("\" r:id=\"rId").Append(i + 1).Append("\"/>");
        }

        return sb.Append("</sheets></workbook>").ToString();
    }

    private static IEnumerable<(string, string, Book)> Cases()
    {
        // 1. Every kind of cell value.
        {
            var rows = new StringBuilder("<sheetData>");
            rows.Append("<row r=\"1\"><c r=\"A1\"><v>0</v></c><c r=\"B1\"><v>42</v></c><c r=\"C1\"><v>-7</v></c><c r=\"D1\"><v>1.5</v></c><c r=\"E1\"><v>0.1</v></c><c r=\"F1\"><v>1E+300</v></c><c r=\"G1\"><v>-2.5E-300</v></c><c r=\"H1\"><v>536870911</v></c><c r=\"I1\"><v>536870912</v></c><c r=\"J1\"><v>123456.78</v></c></row>");
            rows.Append("<row r=\"2\"><c r=\"A2\" t=\"s\"><v>0</v></c><c r=\"B2\" t=\"s\"><v>1</v></c><c r=\"C2\" t=\"s\"><v>2</v></c><c r=\"D2\" t=\"inlineStr\"><is><t xml:space=\"preserve\">  padded  </t></is></c><c r=\"E2\" t=\"inlineStr\"><is><t>line1&#10;line2</t></is></c></row>");
            rows.Append("<row r=\"3\"><c r=\"A3\" t=\"b\"><v>1</v></c><c r=\"B3\" t=\"b\"><v>0</v></c><c r=\"C3\" t=\"e\"><v>#DIV/0!</v></c><c r=\"D3\" t=\"e\"><v>#N/A</v></c><c r=\"E3\" t=\"e\"><v>#NAME?</v></c><c r=\"F3\" t=\"e\"><v>#NULL!</v></c><c r=\"G3\" t=\"e\"><v>#NUM!</v></c><c r=\"H3\" t=\"e\"><v>#REF!</v></c><c r=\"I3\" t=\"e\"><v>#VALUE!</v></c></row>");
            rows.Append("<row r=\"4\"><c r=\"A4\" s=\"1\"><v>45000</v></c><c r=\"B4\" s=\"2\"><v>1234.5</v></c><c r=\"C4\" s=\"3\"><v>0.25</v></c><c r=\"D4\" s=\"4\" t=\"inlineStr\"><is><t>aligned</t></is></c><c r=\"E4\" s=\"5\"><v>9</v></c><c r=\"F4\" s=\"2\"/><c r=\"G4\" s=\"1\"/></row>");
            rows.Append("<row r=\"5\"><c r=\"A5\"><f>A1+B1</f><v>42</v></c><c r=\"B5\" t=\"str\"><f>B2&amp;\"!\"</f><v>beta!</v></c><c r=\"C5\" t=\"b\"><f>A1=0</f><v>1</v></c><c r=\"D5\" t=\"e\"><f>1/0</f><v>#DIV/0!</v></c><c r=\"E5\"><f>NOW()</f><v>45000.5</v></c></row>");
            rows.Append("<row r=\"6\"><c r=\"A6\" t=\"inlineStr\"><is><t>\u00e9\u00e8 \u65e5\u672c\u8a9e \ud83d\ude00 \u05e2\u05d1\u05e8\u05d9\u05ea</t></is></c></row>");
            rows.Append("</sheetData>");
            var b = new Book
            {
                Workbook = WorkbookXml("Values"),
                Sheets = [Sheet(rows.ToString(), "<dimension ref=\"A1:J6\"/>")],
                SharedStrings = "<sst xmlns=\"" + Main + "\" count=\"3\" uniqueCount=\"3\"><si><t>alpha</t></si><si><t>beta</t></si><si><r><rPr><b/><sz val=\"11\"/><color rgb=\"FFFF0000\"/><rFont val=\"Calibri\"/><family val=\"2\"/></rPr><t>Rich</t></r><r><rPr><i/><sz val=\"9\"/><rFont val=\"Arial\"/><family val=\"2\"/></rPr><t xml:space=\"preserve\"> text</t></r></si></sst>",
            };
            yield return ("values.xlsb", "numbers (integers that fit an RK value, doubles, extremes), shared and inline strings, rich text, booleans, all seven error values, styled and empty cells, formulas with each cached result type, non-Latin text", b);
        }

        // 2. Layout: columns, rows, panes, merges, hyperlinks, page setup.
        {
            string body = "<sheetData><row r=\"1\" ht=\"30\" customHeight=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Title</t></is></c></row><row r=\"2\" hidden=\"1\" outlineLevel=\"1\"><c r=\"A2\"><v>1</v></c></row><row r=\"3\" outlineLevel=\"1\" collapsed=\"1\"><c r=\"A3\"><v>2</v></c><c r=\"C3\" t=\"inlineStr\"><is><t>link</t></is></c></row></sheetData>"
                + "<mergeCells count=\"2\"><mergeCell ref=\"A1:C1\"/><mergeCell ref=\"E5:F8\"/></mergeCells>"
                + "<hyperlinks><hyperlink ref=\"C3\" r:id=\"rId1\" tooltip=\"external\" display=\"link\"/><hyperlink ref=\"A3\" location=\"Layout!E5\" display=\"inside\"/></hyperlinks>"
                + "<printOptions horizontalCentered=\"1\" gridLines=\"1\"/><pageMargins left=\"0.5\" right=\"0.5\" top=\"1\" bottom=\"1\" header=\"0.4\" footer=\"0.4\"/>"
                + "<pageSetup paperSize=\"9\" scale=\"80\" orientation=\"landscape\" horizontalDpi=\"300\" verticalDpi=\"300\"/>"
                + "<headerFooter differentFirst=\"1\"><oddHeader>&amp;CPage &amp;P of &amp;N</oddHeader><oddFooter>&amp;L&amp;F&amp;R&amp;D</oddFooter><firstHeader>First</firstHeader></headerFooter>";
            string head = "<sheetPr><tabColor rgb=\"FF00B050\"/><outlinePr summaryBelow=\"0\"/><pageSetUpPr fitToPage=\"1\"/></sheetPr><dimension ref=\"A1:F8\"/>"
                + "<sheetViews><sheetView showGridLines=\"0\" zoomScale=\"120\" tabSelected=\"1\" workbookViewId=\"0\"><pane xSplit=\"1\" ySplit=\"1\" topLeftCell=\"B2\" activePane=\"bottomRight\" state=\"frozen\"/><selection pane=\"bottomRight\" activeCell=\"C3\" sqref=\"C3\"/></sheetView></sheetViews>"
                + "<sheetFormatPr defaultRowHeight=\"15\" outlineLevelRow=\"1\"/><cols><col min=\"1\" max=\"1\" width=\"20.7109375\" customWidth=\"1\"/><col min=\"2\" max=\"3\" width=\"9.140625\" style=\"1\" hidden=\"1\" customWidth=\"1\"/><col min=\"5\" max=\"6\" width=\"30\" customWidth=\"1\" outlineLevel=\"1\"/></cols>";
            var b = new Book
            {
                Workbook = WorkbookXml("Layout").Replace("</sheets>", "</sheets><definedNames><definedName name=\"_xlnm.Print_Area\" localSheetId=\"0\">Layout!$A$1:$F$8</definedName><definedName name=\"_xlnm.Print_Titles\" localSheetId=\"0\">Layout!$1:$1</definedName><definedName name=\"Greeting\">\"hello\"</definedName></definedNames>"),
                Sheets = [Sheet(body, head)],
            };
            b.SheetRels[1] = [("rId1", Rel + "hyperlink", "https://example.com/a?b=1&c=2")];
            yield return ("layout.xlsb", "column widths and styles, hidden and outlined rows and columns, row height, frozen panes and selection, merged cells, external and internal hyperlinks, page setup, print options, header and footer, print area and titles, tab colour, sheet view options", b);
        }

        // 3. Formulas.
        {
            var f = new[]
            {
                "SUM(A1:A10)", "IF(A1>0,\"pos\",IF(A1<0,\"neg\",\"zero\"))", "CHOOSE(2,A1,B1,C1)", "IFERROR(A1/B1,0)", "VLOOKUP(A1,$E$1:$G$9,2,FALSE)", "INDEX(E1:G9,MATCH(A1,E1:E9,0),3)",
                "SUMPRODUCT((A1:A9>0)*(B1:B9))", "SUMIFS(B1:B9,A1:A9,\">1\",C1:C9,\"x*\")", "Greeting&\" \"&Other!A1", "SUM(Other!A1:A3,Other!B1)", "SUM(Layout:Other!A1)", "TEXT(NOW(),\"yyyy-mm-dd\")",
                "A1^2+B1/3-C1*4", "-A1%", "\"a\"&\"b\"&1", "AND(A1,B1,OR(C1,NOT(D1)))", "ROUND(A1,2)+ABS(B1)+MOD(C1,3)", "COUNTIF(A1:A9,\"<>\")", "A1:A3 B2:C4", "(A1,B2,C3)", "SUM((A1:A3,B1:B3))",
                "TRUE", "{1,2,3;4,5,6}", "{\"a\",TRUE,#N/A}", "LEN(\"h\u00e9llo\")", "_xlfn.CONCAT(A1:A3)", "_xlfn.IFS(A1>1,\"a\",A1>0,\"b\")", "LET(x,A1,x*2)", "XLOOKUP(A1,E1:E9,F1:F9)", "OFFSET(A1,1,1)", "INDIRECT(\"A1\")", "ROW()+COLUMN()",
                "$A$1+A$1+$A1+A1", "Table1[[#This Row],[Qty]]*2", "SUM(Table1[Qty])", "Table1[#Headers]", "A1#",
            };
            var rows = new StringBuilder("<sheetData>");
            for (int i = 0; i < f.Length; i++)
            {
                rows.Append("<row r=\"").Append(i + 1).Append("\"><c r=\"K").Append(i + 1).Append("\"><f>").Append(System.Security.SecurityElement.Escape(f[i])).Append("</f><v>0</v></c></row>");
            }

            rows.Append("<row r=\"50\"><c r=\"A50\"><f t=\"array\" ref=\"A50:B51\">{1,2;3,4}*2</f><v>2</v></c><c r=\"B50\"><v>4</v></c></row><row r=\"51\"><c r=\"A51\"><v>6</v></c><c r=\"B51\"><v>8</v></c></row>");
            rows.Append("<row r=\"52\"><c r=\"A52\"><f t=\"shared\" ref=\"A52:A54\" si=\"0\">B52*2</f><v>2</v></c><c r=\"B52\"><v>1</v></c></row><row r=\"53\"><c r=\"A53\"><f t=\"shared\" si=\"0\"/><v>4</v></c><c r=\"B53\"><v>2</v></c></row><row r=\"54\"><c r=\"A54\"><f t=\"shared\" si=\"0\"/><v>6</v></c><c r=\"B54\"><v>3</v></c></row>");
            rows.Append("</sheetData>");
            string table = "<table xmlns=\"" + Main + "\" id=\"1\" name=\"Table1\" displayName=\"Table1\" ref=\"M1:N4\"><tableColumns count=\"2\"><tableColumn id=\"1\" name=\"Item\"/><tableColumn id=\"2\" name=\"Qty\"/></tableColumns><tableStyleInfo name=\"TableStyleMedium2\" showFirstColumn=\"0\" showLastColumn=\"0\" showRowStripes=\"1\" showColumnStripes=\"0\"/></table>";
            var b = new Book
            {
                Workbook = WorkbookXml("Layout", "Other").Replace("</sheets>", "</sheets><definedNames><definedName name=\"Greeting\">\"hello\"</definedName><definedName name=\"Local\" localSheetId=\"1\">Other!$A$1:$A$3</definedName></definedNames>"),
                Sheets = [Sheet(rows.ToString() + "<tableParts count=\"1\"><tablePart r:id=\"rId1\"/></tableParts>"), Sheet("<sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c><c r=\"B1\"><v>2</v></c></row></sheetData>")],
            };
            b.SheetRels[1] = [("rId1", Rel + "table", "../tables/table1.xml")];
            b.Extra["/xl/tables/table1.xml"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml", table);
            yield return ("formulas.xlsb", "formula tokens: operators, references (absolute, relative, mixed, ranges, 3-D, other sheet, whole row and column), names, structured references, array constants, array and shared formulas, newer functions, spill references, union and intersection", b);
        }

        // 4. Sheet features.
        {
            string body = "<sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Item</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>Qty</t></is></c><c r=\"C1\" t=\"inlineStr\"><is><t>Price</t></is></c></row>"
                + "<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>a</t></is></c><c r=\"B2\"><v>2</v></c><c r=\"C2\"><f>B2*2</f><v>4</v></c></row><row r=\"3\"><c r=\"A3\" t=\"inlineStr\"><is><t>b</t></is></c><c r=\"B3\"><v>5</v></c><c r=\"C3\"><f>B3*2</f><v>10</v></c></row></sheetData>"
                + "<sheetProtection sheet=\"1\" objects=\"1\" scenarios=\"1\" formatCells=\"0\" insertRows=\"0\" sort=\"0\" autoFilter=\"0\"/>"
                + "<autoFilter ref=\"A1:C3\"><filterColumn colId=\"0\"><filters><filter val=\"a\"/><filter val=\"b\"/></filters></filterColumn><filterColumn colId=\"1\"><customFilters and=\"1\"><customFilter operator=\"greaterThan\" val=\"1\"/><customFilter operator=\"lessThan\" val=\"9\"/></customFilters></filterColumn></autoFilter>"
                + "<conditionalFormatting sqref=\"B2:B3\"><cfRule type=\"cellIs\" dxfId=\"0\" priority=\"1\" operator=\"between\"><formula>1</formula><formula>3</formula></cfRule><cfRule type=\"expression\" dxfId=\"1\" priority=\"2\"><formula>$B2&gt;4</formula></cfRule></conditionalFormatting>"
                + "<conditionalFormatting sqref=\"C2:C3\"><cfRule type=\"colorScale\" priority=\"3\"><colorScale><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FFF8696B\"/><color rgb=\"FF63BE7B\"/></colorScale></cfRule></conditionalFormatting>"
                + "<conditionalFormatting sqref=\"B2:B3\"><cfRule type=\"dataBar\" priority=\"4\"><dataBar><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FF638EC6\"/></dataBar></cfRule></conditionalFormatting>"
                + "<conditionalFormatting sqref=\"B2:B3\"><cfRule type=\"iconSet\" priority=\"5\"><iconSet iconSet=\"3TrafficLights1\"><cfvo type=\"percent\" val=\"0\"/><cfvo type=\"percent\" val=\"33\"/><cfvo type=\"percent\" val=\"67\"/></iconSet></cfRule></conditionalFormatting>"
                + "<conditionalFormatting sqref=\"A2:A3\"><cfRule type=\"containsText\" dxfId=\"0\" priority=\"6\" operator=\"containsText\" text=\"a\"><formula>NOT(ISERROR(SEARCH(\"a\",A2)))</formula></cfRule><cfRule type=\"top10\" dxfId=\"0\" priority=\"7\" rank=\"1\"/></conditionalFormatting>"
                + "<dataValidations count=\"2\"><dataValidation type=\"list\" allowBlank=\"1\" showInputMessage=\"1\" showErrorMessage=\"1\" errorTitle=\"Bad\" error=\"Pick one\" promptTitle=\"Item\" prompt=\"a or b\" sqref=\"A2:A3\"><formula1>\"a,b,c\"</formula1></dataValidation>"
                + "<dataValidation type=\"decimal\" operator=\"between\" errorStyle=\"warning\" sqref=\"B2:B3\"><formula1>0</formula1><formula2>100</formula2></dataValidation></dataValidations>"
                + "<rowBreaks count=\"1\" manualBreakCount=\"1\"><brk id=\"2\" max=\"16383\" man=\"1\"/></rowBreaks><colBreaks count=\"1\" manualBreakCount=\"1\"><brk id=\"1\" max=\"1048575\" man=\"1\"/></colBreaks>"
                + "<ignoredErrors><ignoredError sqref=\"C2:C3\" formula=\"1\"/></ignoredErrors>"
                + "<tableParts count=\"1\"><tablePart r:id=\"rId1\"/></tableParts>";
            string table = "<table xmlns=\"" + Main + "\" id=\"1\" name=\"Table1\" displayName=\"Table1\" ref=\"E1:G4\" totalsRowCount=\"1\"><autoFilter ref=\"E1:G3\"/><tableColumns count=\"3\"><tableColumn id=\"1\" name=\"Item\" totalsRowLabel=\"Total\"/><tableColumn id=\"2\" name=\"Qty\" totalsRowFunction=\"sum\"/><tableColumn id=\"3\" name=\"Price\"><calculatedColumnFormula>Table1[[#This Row],[Qty]]*2</calculatedColumnFormula></tableColumn></tableColumns><tableStyleInfo name=\"TableStyleMedium9\" showFirstColumn=\"0\" showLastColumn=\"0\" showRowStripes=\"1\" showColumnStripes=\"0\"/></table>";
            string comments = "<comments xmlns=\"" + Main + "\"><authors><author>Ann</author><author>Bo</author></authors><commentList><comment ref=\"B2\" authorId=\"1\"><text><r><rPr><b/><sz val=\"9\"/><color indexed=\"81\"/><rFont val=\"Tahoma\"/><family val=\"2\"/></rPr><t>Bo:</t></r><r><rPr><sz val=\"9\"/><color indexed=\"81\"/><rFont val=\"Tahoma\"/><family val=\"2\"/></rPr><t xml:space=\"preserve\">\n note</t></r></text></comment></commentList></comments>";
            var b = new Book
            {
                Workbook = WorkbookXml("Features"),
                Sheets = [Sheet(body, "<dimension ref=\"A1:G4\"/>")],
            };
            b.SheetRels[1] = [("rId1", Rel + "table", "../tables/table1.xml"), ("rId2", Rel + "comments", "../comments1.xml")];
            b.Extra["/xl/tables/table1.xml"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml", table);
            b.Extra["/xl/comments1.xml"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml", comments);
            yield return ("features.xlsb", "sheet protection, AutoFilter with custom and list filters, conditional formatting (cell value, expression, colour scale, data bar, icon set, text, top 10), data validation (list, decimal), page breaks, ignored errors, a table with totals row and calculated column, comments with rich text", b);
        }

        // 5. Several sheets with states.
        {
            var b = new Book
            {
                Workbook = "<workbook xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\"><workbookPr date1904=\"1\"/><bookViews><workbookView activeTab=\"1\" firstSheet=\"0\"/></bookViews><sheets><sheet name=\"First\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Hidden one\" sheetId=\"2\" state=\"hidden\" r:id=\"rId2\"/><sheet name=\"Very hidden\" sheetId=\"7\" state=\"veryHidden\" r:id=\"rId3\"/></sheets>"
                    + "<definedNames><definedName name=\"Global\">First!$A$1</definedName><definedName name=\"Scoped\" localSheetId=\"1\" comment=\"c\">'Hidden one'!$A$1:$B$2</definedName><definedName name=\"Hid\" hidden=\"1\">'Very hidden'!$A$1</definedName></definedNames><calcPr calcId=\"191029\" iterate=\"1\" iterateCount=\"50\" iterateDelta=\"0.01\" fullCalcOnLoad=\"1\"/></workbook>",
                Sheets =
                [
                    Sheet("<sheetData><row r=\"1\"><c r=\"A1\"><v>43000</v></c></row></sheetData>"),
                    Sheet("<sheetData><row r=\"1\"><c r=\"A1\"><f>First!A1+1</f><v>43001</v></c></row></sheetData>"),
                    Sheet("<sheetData/>"),
                ],
            };
            yield return ("sheets.xlsb", "three sheets (visible, hidden, very hidden), 1904 date system, calculation settings with iteration, global, sheet-scoped and hidden defined names", b);
        }
    }
}
