using System.Text;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets.Pivots;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

/// <summary>Builds workbooks with a data sheet and one pivot table whose cache holds only field names, so a refresh has to build everything.</summary>
internal sealed class PivotBook
{
    private const string Main = TestBook.Main;
    private const string R = TestBook.R;
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

    public static readonly string[] Headers = ["Region", "Product", "Qtr", "Sales", "Units"];

    /// <summary>Eight records: Sales by region is East 530, West 540.</summary>
    public static readonly object[][] Rows =
    [
        ["East", "A", "Q1", 100.0, 1.0],
        ["East", "B", "Q1", 200.0, 2.0],
        ["East", "A", "Q2", 150.0, 3.0],
        ["West", "A", "Q1", 300.0, 4.0],
        ["West", "B", "Q2", 50.0, 5.0],
        ["West", "B", "Q2", 70.0, 6.0],
        ["East", "B", "Q2", 80.0, 7.0],
        ["West", "A", "Q2", 120.0, 8.0],
    ];

    public int[] RowFields = [];
    public int[] ColFields = [];
    public (int Field, int? Item)[] Pages = [];
    public List<string> DataFields = [];
    public string TableAttrs = string.Empty;
    public Dictionary<int, string> FieldAttrs = [];
    public Dictionary<int, string> FieldItems = [];
    public string Location = "A3:B7";
    public string PivotSheet = "Pivot";
    public string SourceXml = "<worksheetSource ref=\"A1:E9\" sheet=\"Data\"/>";
    public string SourceType = "worksheet";
    public string[] CacheFieldXml = [];
    public string CacheAttrs = string.Empty;
    public string TableXmlExtra = string.Empty;
    public string DataSheetExtra = string.Empty;
    public object[][] Data = Rows;
    public string[] DataHeaders = Headers;
    public string? TableName { get; set; }
    public string? CacheFieldsOverride;

    public static string Cell(string r, object v) => v switch
    {
        string s => "<c r=\"" + r + "\" t=\"inlineStr\"><is><t>" + System.Security.SecurityElement.Escape(s) + "</t></is></c>",
        double d => "<c r=\"" + r + "\"><v>" + d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "</v></c>",
        DateTime dt => "<c r=\"" + r + "\" s=\"1\"><v>" + (dt - new DateTime(1899, 12, 30)).TotalDays.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "</v></c>",
        bool b => "<c r=\"" + r + "\" t=\"b\"><v>" + (b ? 1 : 0) + "</v></c>",
        _ => string.Empty,
    };

    public static string DataSheet(string[] headers, object[][] rows, string extra = "")
    {
        var sb = new StringBuilder();
        sb.Append(Decl).Append("<worksheet xmlns=\"").Append(Main).Append("\" xmlns:r=\"").Append(R).Append("\"><sheetData>");
        sb.Append("<row r=\"1\">");
        for (int c = 0; c < headers.Length; c++)
        {
            sb.Append(Cell(CellAddress.ColumnName(c + 1) + "1", headers[c]));
        }

        sb.Append("</row>");
        for (int r = 0; r < rows.Length; r++)
        {
            sb.Append("<row r=\"").Append(r + 2).Append("\">");
            for (int c = 0; c < rows[r].Length; c++)
            {
                sb.Append(Cell(CellAddress.ColumnName(c + 1) + (r + 2), rows[r][c]));
            }

            sb.Append("</row>");
        }

        sb.Append("</sheetData>").Append(extra).Append("</worksheet>");
        return sb.ToString();
    }

    public static string DataField(string name, int fld, string? subtotal = null, string? showAs = null, string? baseField = null, string? baseItem = null, string? extra = null) =>
        "<dataField name=\"" + name + "\" fld=\"" + fld + "\"" + (subtotal is null ? string.Empty : " subtotal=\"" + subtotal + "\"") + (showAs is null ? string.Empty : " showDataAs=\"" + showAs + "\"")
        + (baseField is null ? string.Empty : " baseField=\"" + baseField + "\"") + (baseItem is null ? string.Empty : " baseItem=\"" + baseItem + "\"") + (extra ?? string.Empty) + "/>";

    /// <summary>A cache field list whose named fields already list their items, as a stored cache would.</summary>
    public static string CacheWith(string[] headers, Dictionary<int, string[]> shared)
    {
        var sb = new StringBuilder();
        sb.Append("<cacheFields count=\"").Append(headers.Length).Append("\">");
        for (int i = 0; i < headers.Length; i++)
        {
            sb.Append("<cacheField name=\"").Append(headers[i]).Append("\" numFmtId=\"0\">");
            if (shared.TryGetValue(i, out string[]? items))
            {
                sb.Append("<sharedItems count=\"").Append(items.Length).Append("\">").Append(string.Concat(items.Select(x => "<s v=\"" + x + "\"/>"))).Append("</sharedItems>");
            }
            else
            {
                sb.Append("<sharedItems/>");
            }

            sb.Append("</cacheField>");
        }

        return sb.Append("</cacheFields>").ToString();
    }

    public string CacheDef()
    {
        var sb = new StringBuilder();
        sb.Append(Decl).Append("<pivotCacheDefinition xmlns=\"").Append(Main).Append("\" xmlns:r=\"").Append(R).Append("\" recordCount=\"0\"").Append(CacheAttrs).Append('>');
        sb.Append("<cacheSource type=\"").Append(SourceType).Append("\">").Append(SourceXml).Append("</cacheSource>");
        if (CacheFieldsOverride is not null)
        {
            sb.Append(CacheFieldsOverride);
        }
        else
        {
            sb.Append("<cacheFields count=\"").Append(DataHeaders.Length).Append("\">");
            for (int i = 0; i < DataHeaders.Length; i++)
            {
                sb.Append("<cacheField name=\"").Append(DataHeaders[i]).Append("\" numFmtId=\"0\"><sharedItems/></cacheField>");
            }

            sb.Append("</cacheFields>");
        }

        sb.Append("</pivotCacheDefinition>");
        return sb.ToString();
    }

    public string TableDef()
    {
        int n = CacheFieldsOverride is null ? DataHeaders.Length : FieldAttrs.Keys.DefaultIfEmpty(0).Max() + 1;
        n = Math.Max(n, DataHeaders.Length);
        var sb = new StringBuilder();
        sb.Append(Decl).Append("<pivotTableDefinition xmlns=\"").Append(Main).Append("\" name=\"").Append(TableName ?? "PT1").Append("\" cacheId=\"1\" dataCaption=\"Values\"").Append(TableAttrs).Append('>');
        sb.Append("<location ref=\"").Append(Location).Append("\" firstHeaderRow=\"1\" firstDataRow=\"1\" firstDataCol=\"1\"/>");
        sb.Append("<pivotFields count=\"").Append(n).Append("\">");
        for (int i = 0; i < n; i++)
        {
            string axis = RowFields.Contains(i) ? " axis=\"axisRow\"" : ColFields.Contains(i) ? " axis=\"axisCol\"" : Pages.Any(p => p.Field == i) ? " axis=\"axisPage\"" : string.Empty;
            string data = DataFields.Any(d => d.Contains("fld=\"" + i + "\"", StringComparison.Ordinal)) ? " dataField=\"1\"" : string.Empty;
            FieldAttrs.TryGetValue(i, out string? extra);
            sb.Append("<pivotField").Append(axis).Append(data).Append(" showAll=\"0\"").Append(extra ?? string.Empty);
            if (FieldItems.TryGetValue(i, out string? items))
            {
                sb.Append('>').Append(items).Append("</pivotField>");
            }
            else if (axis.Length > 0)
            {
                sb.Append("><items count=\"1\"><item t=\"default\"/></items></pivotField>");
            }
            else
            {
                sb.Append("/>");
            }
        }

        sb.Append("</pivotFields>");
        if (RowFields.Length > 0)
        {
            sb.Append("<rowFields count=\"").Append(RowFields.Length).Append("\">").Append(string.Concat(RowFields.Select(f => "<field x=\"" + f + "\"/>"))).Append("</rowFields>");
        }

        if (ColFields.Length > 0)
        {
            sb.Append("<colFields count=\"").Append(ColFields.Length).Append("\">").Append(string.Concat(ColFields.Select(f => "<field x=\"" + f + "\"/>"))).Append("</colFields>");
        }

        if (Pages.Length > 0)
        {
            sb.Append("<pageFields count=\"").Append(Pages.Length).Append("\">").Append(string.Concat(Pages.Select(p => "<pageField fld=\"" + p.Field + "\"" + (p.Item is { } it ? " item=\"" + it + "\"" : string.Empty) + " hier=\"-1\"/>"))).Append("</pageFields>");
        }

        if (DataFields.Count > 0)
        {
            sb.Append("<dataFields count=\"").Append(DataFields.Count).Append("\">").Append(string.Concat(DataFields)).Append("</dataFields>");
        }

        sb.Append("<pivotTableStyleInfo name=\"PivotStyleLight16\" showRowHeaders=\"1\" showColHeaders=\"1\" showRowStripes=\"0\" showColStripes=\"0\" showLastColumn=\"1\"/>");
        sb.Append(TableXmlExtra).Append("</pivotTableDefinition>");
        return sb.ToString();
    }

    public byte[] Build(string? tableXml = null)
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Encoding.UTF8.GetBytes(Decl
            + "<workbook xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\"><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"" + PivotSheet + "\" sheetId=\"2\" r:id=\"rId2\"/></sheets>"
            + "<calcPr calcId=\"191029\"/><pivotCaches><pivotCache cacheId=\"1\" r:id=\"rId3\"/></pivotCaches></workbook>"));
        p.SetRelationships("/xl/workbook.xml",
        [
            new Relationship("rId1", Rel + "worksheet", "worksheets/sheet1.xml"),
            new Relationship("rId2", Rel + "worksheet", "worksheets/sheet2.xml"),
            new Relationship("rId4", Rel + "styles", "styles.xml"),
            new Relationship("rId3", Rel + "pivotCacheDefinition", "pivotCache/pivotCacheDefinition1.xml"),
        ]);
        p.AddPart("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", Encoding.UTF8.GetBytes(Decl
            + "<styleSheet xmlns=\"" + Main + "\"><numFmts count=\"0\"/><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
            + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
            + "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs>"
            + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>"));
        p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", Encoding.UTF8.GetBytes(DataSheet(DataHeaders, Data, DataSheetExtra)));
        p.AddPart("/xl/worksheets/sheet2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", Encoding.UTF8.GetBytes(Decl
            + "<worksheet xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\"><sheetData/></worksheet>"));
        p.SetRelationships("/xl/worksheets/sheet2.xml", [new Relationship("rId1", Rel + "pivotTable", "../pivotTables/pivotTable1.xml")]);
        p.AddPart("/xl/pivotTables/pivotTable1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml", Encoding.UTF8.GetBytes(tableXml ?? TableDef()));
        p.SetRelationships("/xl/pivotTables/pivotTable1.xml", [new Relationship("rId1", Rel + "pivotCacheDefinition", "../pivotCache/pivotCacheDefinition1.xml")]);
        p.AddPart("/xl/pivotCache/pivotCacheDefinition1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml", Encoding.UTF8.GetBytes(CacheDef()));
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>Opens, refreshes, and returns the workbook.</summary>
    public Workbook Refreshed(out PivotRefreshResult result)
    {
        Workbook wb = TestBook.Open(Build());
        result = wb.RefreshPivots();
        return wb;
    }
}
