using System.Globalization;
using System.Linq;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>One sheet entry of the workbook part (<c>BrtBundleSh</c>, [MS-XLSB] §2.4.316).</summary>
internal sealed record XlsbSheetEntry(string Name, int SheetId, string? RelationshipId, int State);

/// <summary>Translates the workbook part between <c>workbook.bin</c> and <c>workbook.xml</c> ([MS-XLSB] §2.1.7.61).</summary>
internal static class XlsbWorkbookPart
{
    private static readonly string[] Visibility = ["visible", "hidden", "veryHidden"];

    private sealed class RawName
    {
        public int Flags;
        public int Key;
        public int Sheet;
        public string Name = string.Empty;
        public byte[] Rgce = [];
        public byte[] Rgcb = [];
        public string? Comment;
        public string? Description;
        public string? Help;
        public bool HasProcStrings;
    }

    /// <summary>Reads <c>workbook.bin</c>: fills <paramref name="formulas"/> (sheets, books, extern-sheet table, names) and returns the markup.</summary>
    public static byte[] ToXml(ReadOnlyMemory<byte> bin, OpenContext context, string partName, XlsbFormulaContext formulas, Func<string, List<string>?> externalSheetNames,
        out List<XlsbSheetEntry> sheets)
    {
        sheets = [];
        var head = new XmlSink();
        var names = new List<RawName>();
        var supBooks = new List<(XlsbSupBookKind Kind, string? Rel)>();
        var xtis = new List<XlsbXti>();
        var calc = new XmlSink();
        var pre = new XmlSink();
        var views = new XmlSink();
        var tail = new XmlSink();
        bool hasProps = false;
        string? workbookPr = null;
        var r = new XlsbRecordReader(bin, context, partName);
        while (r.Read())
        {
            ReadOnlySpan<byte> d = r.Data;
            switch (r.Type)
            {
                case XlsbRecordType.BeginBook:
                case XlsbRecordType.EndBook:
                case XlsbRecordType.BeginBundleShs:
                case XlsbRecordType.EndBundleShs:
                case XlsbRecordType.BeginExternals:
                case XlsbRecordType.EndExternals:
                case XlsbRecordType.BeginBookViews:
                case XlsbRecordType.EndBookViews:
                    break;
                case XlsbRecordType.WbProp:
                    workbookPr = WorkbookPr(d);
                    hasProps = true;
                    break;
                case XlsbRecordType.BookView:
                    views.Raw(BookView(d));
                    break;
                case XlsbRecordType.BundleSh:
                {
                    var f = new XlsbRecordFields(d);
                    int state = f.I32();
                    int id = f.I32();
                    string? rel = f.NullableWideString();
                    string name = f.WideString();
                    sheets.Add(new XlsbSheetEntry(name, id, rel, state));
                    break;
                }

                case XlsbRecordType.SupSelf: supBooks.Add((XlsbSupBookKind.Self, null)); break;
                case XlsbRecordType.SupSame: supBooks.Add((XlsbSupBookKind.Same, null)); break;
                case XlsbRecordType.SupAddin: supBooks.Add((XlsbSupBookKind.AddIn, null)); break;
                case XlsbRecordType.SupBookSrc:
                {
                    var f = new XlsbRecordFields(d);
                    supBooks.Add((XlsbSupBookKind.Source, f.NullableWideString()));
                    break;
                }

                case XlsbRecordType.ExternSheet:
                {
                    var f = new XlsbRecordFields(d);
                    int n = (int)f.U32();
                    for (int i = 0; i < n && !f.Overrun && i < 1 << 20; i++)
                    {
                        xtis.Add(new XlsbXti(f.I32(), f.I32(), f.I32()));
                    }

                    break;
                }

                case XlsbRecordType.Name:
                    names.Add(ReadName(d));
                    break;
                case XlsbRecordType.CalcProp:
                    calc.Raw(CalcProp(d));
                    break;
                default:
                    // Everything else (file version, protection, function groups, pivot cache ids, extensions) is kept as stored, in order.
                    XlsbMarkup.Raw(names.Count == 0 && sheets.Count == 0 ? pre : tail, r.Type, d);
                    break;
            }
        }

        _ = hasProps;
        foreach (XlsbSheetEntry s in sheets)
        {
            formulas.SheetNames.Add(s.Name);
        }

        foreach ((XlsbSupBookKind kind, string? rel) in supBooks)
        {
            var sb = new XlsbSupBook(kind, rel);
            if (kind == XlsbSupBookKind.Source && rel is not null && externalSheetNames(rel) is { } list)
            {
                sb.SheetNames.AddRange(list);
            }

            formulas.SupBooks.Add(sb);
        }

        formulas.Xtis.AddRange(xtis);
        foreach (RawName n in names)
        {
            formulas.Names.Add(new XlsbDefinedName { Name = Display(n), SheetIndex = n.Sheet == -1 || n.Sheet >= sheets.Count ? -1 : n.Sheet, IsFutureFunction = (n.Flags & (1 << 17)) != 0 });
        }

        var x = new XmlSink(4096);
        x.Raw("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<workbook xmlns=\"").Raw(XlsbMarkup.MainNamespace).Raw("\" xmlns:r=\"").Raw(XlsbMarkup.RelationshipNamespace)
            .Raw("\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:xb=\"").Raw(XlsbMarkup.Namespace).Raw("\" mc:Ignorable=\"xb\">");
        x.Raw(pre.Memory.Span);
        if (workbookPr is not null)
        {
            x.Raw(workbookPr);
        }

        if (views.Length > 0)
        {
            x.Raw("<bookViews>").Raw(views.Memory.Span).Raw("</bookViews>");
        }

        x.Raw("<sheets>");
        int ord = 0;
        foreach (XlsbSheetEntry s in sheets)
        {
            x.Raw("<sheet").Attr("name", s.Name).Attr("sheetId", s.SheetId);
            if (s.State is 1 or 2)
            {
                x.Attr("state", Visibility[s.State]);
            }

            x.Attr("r:id", s.RelationshipId ?? string.Empty).Raw("/>");
            ord++;
        }

        x.Raw("</sheets>");
        bool anySource = supBooks.Any(b => b.Kind == XlsbSupBookKind.Source);
        if (anySource)
        {
            x.Raw("<externalReferences>");
            foreach ((XlsbSupBookKind kind, string? rel) in supBooks)
            {
                if (kind == XlsbSupBookKind.Source)
                {
                    x.Raw("<externalReference").Attr("r:id", rel ?? string.Empty).Raw("/>");
                }
            }

            x.Raw("</externalReferences>");
        }

        if (names.Count > 0)
        {
            x.Raw("<definedNames>");
            foreach (RawName n in names)
            {
                NameElement(x, n, formulas, context, partName);
            }

            x.Raw("</definedNames>");
        }

        x.Raw(calc.Memory.Span);
        if (supBooks.Count > 0 || xtis.Count > 0)
        {
            x.Raw("<xb:externs>");
            foreach ((XlsbSupBookKind kind, string? rel) in supBooks)
            {
                x.Raw("<xb:supBook").Attr("kind", kind.ToString());
                if (rel is not null)
                {
                    x.Attr("r:id", rel);
                }

                x.Raw("/>");
            }

            foreach (XlsbXti t in xtis)
            {
                x.Raw("<xb:xti").Attr("book", t.SupBook).Attr("first", t.FirstSheet).Attr("last", t.LastSheet).Raw("/>");
            }

            x.Raw("</xb:externs>");
        }

        x.Raw(tail.Memory.Span);
        x.Raw("</workbook>");
        return x.ToArray();
    }

    private static string Display(RawName n) => (n.Flags & (1 << 5)) != 0 ? "_xlnm." + n.Name : n.Name;

    private static RawName ReadName(ReadOnlySpan<byte> d)
    {
        var f = new XlsbRecordFields(d);
        var n = new RawName { Flags = (int)f.U32(), Key = f.U8(), Sheet = f.I32(), Name = f.WideString() };
        int cce = (int)f.U32();
        if (cce >= 0 && cce <= f.Remaining)
        {
            n.Rgce = f.Bytes(cce).ToArray();
            int cb = (int)f.U32();
            if (cb >= 0 && cb <= f.Remaining)
            {
                n.Rgcb = f.Bytes(cb).ToArray();
            }
        }

        n.Comment = f.NullableWideString();
        if ((n.Flags & 8) != 0)
        {
            n.HasProcStrings = true;
            f.NullableWideString();
            n.Description = f.NullableWideString();
            n.Help = f.NullableWideString();
            f.NullableWideString();
        }

        return n;
    }

    private static void NameElement(XmlSink x, RawName n, XlsbFormulaContext cx, OpenContext context, string partName)
    {
        if ((n.Flags & (1 << 17)) != 0)
        {
            x.Raw("<xb:futureName").Attr("name", n.Name).Raw("/>");
            return;
        }

        x.Raw("<definedName").Attr("name", Display(n));
        if (n.Sheet >= 0)
        {
            x.Attr("localSheetId", n.Sheet);
        }

        if ((n.Flags & 1) != 0)
        {
            x.Attr("hidden", 1);
        }

        if ((n.Flags & 2) != 0)
        {
            x.Attr("function", 1);
        }

        if ((n.Flags & 4) != 0)
        {
            x.Attr("vbProcedure", 1);
        }

        if ((n.Flags & 8) != 0)
        {
            x.Attr("xlm", 1);
        }

        int group = (n.Flags >> 6) & 0x1FF;
        if (group != 0)
        {
            x.Attr("functionGroupId", group);
        }

        if (n.Key != 0)
        {
            x.Attr("shortcutKey", ((char)n.Key).ToString());
        }

        if ((n.Flags & (1 << 15)) != 0)
        {
            x.Attr("publishToServer", 1);
        }

        if ((n.Flags & (1 << 16)) != 0)
        {
            x.Attr("workbookParameter", 1);
        }

        if (!string.IsNullOrEmpty(n.Comment))
        {
            x.Attr("comment", n.Comment);
        }

        if (n.Description is { Length: > 0 })
        {
            x.Attr("description", n.Description);
        }

        if (n.Help is { Length: > 0 })
        {
            x.Attr("help", n.Help);
        }

        x.Raw(">");
        if (n.Rgce.Length > 0)
        {
            if (XlsbFormula.TryDecode(n.Rgce, n.Rgcb, cx, XlsbFormulaKind.Name, 0, 0, out FormulaNode node, out string? problem))
            {
                x.Text(FormulaPrinter.Print(node, new FormulaPrintOptions { Form = FormulaForm.File }));
            }
            else
            {
                context.Defects.Report(new Defect(DefectKind.Formula, "xlsb.formula-unreadable", DefectSeverity.Dropped,
                    "The formula of the name '" + Display(n) + "' could not be read (" + problem + ").", DefectLocation.InPart(partName)));
                x.Text("#NAME?");
            }
        }

        x.Raw("</definedName>");
    }

    private static string WorkbookPr(ReadOnlySpan<byte> d)
    {
        var f = new XlsbRecordFields(d);
        uint flags = f.U32();
        uint theme = f.U32();
        string code = f.WideString();
        var x = new XmlSink();
        x.Raw("<workbookPr");
        if ((flags & 1) != 0)
        {
            x.Attr("date1904", 1);
        }

        int showObjects = (int)((flags >> 13) & 3);
        if (showObjects == 1)
        {
            x.Attr("showObjects", "placeholders");
        }
        else if (showObjects == 2)
        {
            x.Attr("showObjects", "none");
        }

        if ((flags & 4) != 0)
        {
            x.Attr("showBorderUnselectedTables", 0);
        }

        if ((flags & 8) != 0)
        {
            x.Attr("filterPrivacy", 1);
        }

        if ((flags & 16) != 0)
        {
            x.Attr("promptedSolutions", 1);
        }

        if ((flags & 32) == 0)
        {
            x.Attr("showInkAnnotation", 0);
        }

        if ((flags & 64) != 0)
        {
            x.Attr("backupFile", 1);
        }

        if ((flags & 128) != 0)
        {
            x.Attr("saveExternalLinkValues", 0);
        }

        int update = (int)((flags >> 8) & 3);
        if (update == 1)
        {
            x.Attr("updateLinks", "never");
        }
        else if (update == 2)
        {
            x.Attr("updateLinks", "always");
        }

        if ((flags & (1 << 10)) != 0)
        {
            x.Attr("hidePivotFieldList", 1);
        }

        if ((flags & (1 << 11)) != 0)
        {
            x.Attr("publishItems", 1);
        }

        if ((flags & (1 << 12)) == 0)
        {
            x.Attr("checkCompatibility", 0);
        }

        if ((flags & (1 << 15)) == 0)
        {
            x.Attr("showPivotChartFilter", 0);
        }

        if ((flags & (1 << 16)) != 0)
        {
            x.Attr("autoCompressPictures", 1);
        }

        if ((flags & (1 << 18)) != 0)
        {
            x.Attr("refreshAllConnections", 1);
        }

        if (theme != 0)
        {
            x.Attr("defaultThemeVersion", theme);
        }

        if (code.Length > 0)
        {
            x.Attr("codeName", code);
        }

        x.Raw("/>");
        return x.ToString();
    }

    private static string BookView(ReadOnlySpan<byte> d)
    {
        var f = new XlsbRecordFields(d);
        int x0 = f.I32();
        int y0 = f.I32();
        uint dx = f.U32();
        uint dy = f.U32();
        uint ratio = f.U32();
        uint first = f.U32();
        uint cur = f.U32();
        int flags = f.U8();
        var x = new XmlSink();
        x.Raw("<workbookView");
        if ((flags & 2) != 0)
        {
            x.Attr("visibility", "veryHidden");
        }
        else if ((flags & 1) != 0)
        {
            x.Attr("visibility", "hidden");
        }

        if ((flags & 4) != 0)
        {
            x.Attr("minimized", 1);
        }

        if ((flags & 8) == 0)
        {
            x.Attr("showHorizontalScroll", 0);
        }

        if ((flags & 16) == 0)
        {
            x.Attr("showVerticalScroll", 0);
        }

        if ((flags & 32) == 0)
        {
            x.Attr("showSheetTabs", 0);
        }

        x.Attr("xWindow", x0).Attr("yWindow", y0).Attr("windowWidth", dx).Attr("windowHeight", dy);
        if (ratio != 600)
        {
            x.Attr("tabRatio", ratio);
        }

        if (first != 0)
        {
            x.Attr("firstSheet", first);
        }

        if (cur != 0)
        {
            x.Attr("activeTab", cur);
        }

        if ((flags & 64) == 0)
        {
            x.Attr("autoFilterDateGrouping", 0);
        }

        x.Raw("/>");
        return x.ToString();
    }

    private static string CalcProp(ReadOnlySpan<byte> d)
    {
        var f = new XlsbRecordFields(d);
        uint id = f.U32();
        uint auto = f.U32();
        uint count = f.U32();
        double delta = f.Number();
        int threads = f.I32();
        int flags = f.U16();
        var x = new XmlSink();
        x.Raw("<calcPr").Attr("calcId", id);
        if (auto == 0)
        {
            x.Attr("calcMode", "manual");
        }
        else if (auto == 2)
        {
            x.Attr("calcMode", "autoNoTable");
        }

        if ((flags & 2) == 0)
        {
            x.Attr("refMode", "R1C1");
        }

        if ((flags & 4) != 0)
        {
            x.Attr("iterate", 1);
        }

        if (count != 100)
        {
            x.Attr("iterateCount", count);
        }

        if (delta != 0.001)
        {
            x.Attr("iterateDelta", delta);
        }

        if ((flags & 8) == 0)
        {
            x.Attr("fullPrecision", 0);
        }

        if ((flags & 32) == 0)
        {
            x.Attr("calcOnSave", 0);
        }

        if ((flags & 64) == 0)
        {
            x.Attr("concurrentCalc", 0);
        }

        if ((flags & 128) != 0)
        {
            x.Attr("concurrentManualCount", threads);
        }

        if ((flags & 1) != 0)
        {
            x.Attr("fullCalcOnLoad", 1);
        }

        x.Raw("/>");
        return x.ToString();
    }

    // ------------------------------------------------------------ XML to binary

    /// <summary>The facts a workbook part holds that other parts' encoding depends on, read from <c>workbook.xml</c>.</summary>
    public static XlsbFormulaContext ContextFromXml(MarkupNode root, Func<string, List<string>?> externalSheetNames, out List<XlsbSheetEntry> sheets)
    {
        var cx = new XlsbFormulaContext();
        sheets = [];
        foreach (MarkupNode s in root.Element("sheets").Elements("sheet"))
        {
            string name = s.Str("name");
            string state = s.Str("state", "visible");
            string? rel = s.GetAttribute(XlsbMarkup.RelationshipNamespace, "id");
            sheets.Add(new XlsbSheetEntry(name, s.Int("sheetId"), string.IsNullOrEmpty(rel) ? null : rel, state == "veryHidden" ? 2 : state == "hidden" ? 1 : 0));
            cx.SheetNames.Add(name);
        }

        MarkupNode externs = default;
        foreach (MarkupNode c in root.ChildElements)
        {
            if (c.LocalName.SequenceEqual("externs"u8) && c.NamespaceUri == XlsbMarkup.Namespace)
            {
                externs = c;
            }
        }

        if (!externs.IsNull)
        {
            foreach (MarkupNode c in externs.ChildElements)
            {
                if (c.LocalName.SequenceEqual("supBook"u8))
                {
                    _ = Enum.TryParse(c.Str("kind"), out XlsbSupBookKind kind);
                    string? rel = c.GetAttribute(XlsbMarkup.RelationshipNamespace, "id");
                    var sb = new XlsbSupBook(kind, rel);
                    if (kind == XlsbSupBookKind.Source && rel is not null && externalSheetNames(rel) is { } list)
                    {
                        sb.SheetNames.AddRange(list);
                    }

                    cx.SupBooks.Add(sb);
                }
                else if (c.LocalName.SequenceEqual("xti"u8))
                {
                    cx.Xtis.Add(new XlsbXti(c.Int("book"), c.Int("first"), c.Int("last")));
                }
            }
        }
        else
        {
            // A workbook that never was binary has no extern-sheet table: it starts with this workbook as the only book. Its external links have no binary form written yet.
            cx.SupBooks.Add(new XlsbSupBook(XlsbSupBookKind.Self));
        }

        foreach (MarkupNode n in root.Element("definedNames").ChildElements)
        {
            if (n.Is("definedName"))
            {
                string name = n.Str("name");
                cx.Names.Add(new XlsbDefinedName { Name = name, SheetIndex = n.Int("localSheetId", -1) });
            }
            else if (n.Is("futureName") && n.NamespaceUri == XlsbMarkup.Namespace)
            {
                cx.Names.Add(new XlsbDefinedName { Name = n.Str("name"), SheetIndex = -1, IsFutureFunction = true });
            }
        }

        return cx;
    }

    /// <summary>Writes <c>workbook.bin</c> from <c>workbook.xml</c>. Name formulas are encoded first so that the extern-sheet table written afterwards holds everything they need.</summary>
    public static byte[] FromXml(ReadOnlyMemory<byte> xml, OpenContext context, string partName, XlsbFormulaContext cx, ILossSink loss)
    {
        MarkupDocument doc = MarkupDocument.Parse(xml, context, partName);
        MarkupNode root = doc.Root;
        var nameRecords = new List<byte[]>();
        int nameIndex = 0;
        foreach (MarkupNode n in root.Element("definedNames").ChildElements)
        {
            var d = new XlsbRecordData();
            if (n.Is("definedName"))
            {
                string full = n.Str("name");
                bool builtin = full.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase);
                string name = builtin ? full[6..] : full;
                int flags = (n.Bool("hidden") ? 1 : 0) | (n.Bool("function") ? 2 : 0) | (n.Bool("vbProcedure") ? 4 : 0) | (n.Bool("xlm") ? 8 : 0)
                    | (builtin ? 32 : 0) | ((n.Int("functionGroupId") & 0x1FF) << 6) | (n.Bool("publishToServer") ? 1 << 15 : 0) | (n.Bool("workbookParameter") ? 1 << 16 : 0);
                d.U32((uint)flags);
                string key = n.Str("shortcutKey");
                d.U8(key.Length > 0 ? key[0] : 0);
                d.I32(n.Int("localSheetId", -1));
                d.WideString(name);
                string text = n.InnerText.Trim();
                byte[] rgce = [];
                byte[] rgcb = [];
                if (text.Length > 0)
                {
                    FormulaNode? ast = ParseFormula(text, context, partName, loss, "name '" + full + "'");
                    if (ast is not null)
                    {
                        if (!XlsbFormula.TryEncode(ast, cx, XlsbFormulaKind.Name, 0, 0, out rgce, out rgcb, out string? problem))
                        {
                            loss.Report("loss.xlsb-formula-approximated", "formula written approximately", 1, "A formula could not be encoded exactly: " + problem);
                        }
                    }
                }

                if (rgce.Length == 0)
                {
                    rgce = [0x1C, 0x1D];
                }

                d.U32((uint)rgce.Length);
                d.Bytes(rgce);
                d.U32((uint)rgcb.Length);
                d.Bytes(rgcb);
                d.NullableWideString(n.GetAttribute("comment"));
                if ((flags & 8) != 0)
                {
                    d.NullableWideString(null);
                    d.NullableWideString(n.GetAttribute("description"));
                    d.NullableWideString(n.GetAttribute("help"));
                    d.NullableWideString(null);
                }
            }
            else if (n.Is("futureName") && n.NamespaceUri == XlsbMarkup.Namespace)
            {
                d.U32(1u << 17);
                d.U8(0);
                d.I32(-1);
                d.WideString(n.Str("name"));
                d.U32(2);
                d.Bytes([0x1C, 0x1D]);
                d.U32(0);
                d.NullableWideString(null);
            }
            else
            {
                continue;
            }

            nameRecords.Add(d.Span.ToArray());
            nameIndex++;
        }

        // Names the encoder added (future functions, names used before they were defined) follow the listed ones.
        for (int i = nameIndex; i < cx.Names.Count; i++)
        {
            XlsbDefinedName n = cx.Names[i];
            var d = new XlsbRecordData();
            d.U32(n.IsFutureFunction ? 1u << 17 : 0u);
            d.U8(0);
            d.I32(n.SheetIndex);
            d.WideString(n.Name);
            d.U32(2);
            d.Bytes([0x1C, 0x1D]);
            d.U32(0);
            d.NullableWideString(null);
            nameRecords.Add(d.Span.ToArray());
        }

        using var ms = new MemoryStream();
        var w = new XlsbRecordWriter(ms);
        var rec = new XlsbRecordData();
        w.Write(XlsbRecordType.BeginBook);
        bool wroteViews = false;
        foreach (MarkupNode c in root.ChildElements)
        {
            if (c.TryRaw(out int t, out byte[] data))
            {
                w.Write(t, data);
                continue;
            }

            switch (c.LocalNameString)
            {
                case "workbookPr":
                    WriteWorkbookPr(w, rec, c);
                    break;
                case "bookViews":
                    w.Write(XlsbRecordType.BeginBookViews);
                    foreach (MarkupNode v in c.Elements("workbookView"))
                    {
                        WriteBookView(w, rec, v);
                    }

                    w.Write(XlsbRecordType.EndBookViews);
                    wroteViews = true;
                    break;
                case "sheets":
                    w.Write(XlsbRecordType.BeginBundleShs);
                    foreach (MarkupNode s in c.Elements("sheet"))
                    {
                        string? rel = s.GetAttribute(XlsbMarkup.RelationshipNamespace, "id");
                        string state = s.Str("state", "visible");
                        rec.I32(state == "veryHidden" ? 2 : state == "hidden" ? 1 : 0);
                        rec.I32(s.Int("sheetId"));
                        rec.NullableWideString(string.IsNullOrEmpty(rel) ? null : rel);
                        rec.WideString(s.Str("name"));
                        w.Write(XlsbRecordType.BundleSh, rec);
                    }

                    w.Write(XlsbRecordType.EndBundleShs);
                    break;
                case "definedNames":
                case "externalReferences":
                case "calcPr":
                    break;
                case "externs" when c.NamespaceUri == XlsbMarkup.Namespace:
                    break;
                default:
                    if (c.NamespaceUri == XlsbMarkup.MainNamespace)
                    {
                        loss.Report("loss.xlsb-workbook-element", "workbook element " + c.LocalNameString, 1, "The workbook element '" + c.LocalNameString + "' has no binary form here and is dropped.");
                    }

                    break;
            }

            // The name list, calculation properties and the extern table sit after the sheets in binary order.
            if (c.LocalName.SequenceEqual("sheets"u8))
            {
                WriteExternals(w, rec, cx);
                foreach (byte[] n in nameRecords)
                {
                    w.Write(XlsbRecordType.Name, n);
                }

                MarkupNode calc = root.Element("calcPr");
                if (!calc.IsNull)
                {
                    WriteCalcProp(w, rec, calc);
                }
            }
        }

        _ = wroteViews;
        w.Write(XlsbRecordType.EndBook);
        return ms.ToArray();
    }

    private static FormulaNode? ParseFormula(string text, OpenContext context, string partName, ILossSink loss, string what)
    {
        var options = new FormulaParserOptions { Limits = context.Limits };
        if (FormulaParser.TryParse(text, out FormulaNode? node, out FormulaSyntaxError error, options))
        {
            return node;
        }

        loss.Report("loss.xlsb-formula-unparsed", "formula that could not be parsed", 1, "The formula of " + what + " in " + partName + " could not be parsed (" + error.Code + "); it is written as #NAME?.");
        return null;
    }

    private static void WriteWorkbookPr(XlsbRecordWriter w, XlsbRecordData d, MarkupNode n)
    {
        uint flags = 0;
        if (n.Bool("date1904"))
        {
            flags |= 1;
        }

        if (!n.Bool("showBorderUnselectedTables", true))
        {
            flags |= 4;
        }

        if (n.Bool("filterPrivacy"))
        {
            flags |= 8;
        }

        if (n.Bool("promptedSolutions"))
        {
            flags |= 16;
        }

        if (n.Bool("showInkAnnotation", true))
        {
            flags |= 32;
        }

        if (n.Bool("backupFile"))
        {
            flags |= 64;
        }

        if (!n.Bool("saveExternalLinkValues", true))
        {
            flags |= 128;
        }

        flags |= n.Str("updateLinks") switch { "never" => 1u << 8, "always" => 2u << 8, _ => 0u };
        if (n.Bool("hidePivotFieldList"))
        {
            flags |= 1u << 10;
        }

        if (n.Bool("publishItems"))
        {
            flags |= 1u << 11;
        }

        if (n.Bool("checkCompatibility", true))
        {
            flags |= 1u << 12;
        }

        flags |= n.Str("showObjects") switch { "placeholders" => 1u << 13, "none" => 2u << 13, _ => 0u };
        if (n.Bool("showPivotChartFilter", true))
        {
            flags |= 1u << 15;
        }

        if (n.Bool("autoCompressPictures"))
        {
            flags |= 1u << 16;
        }

        if (n.Bool("refreshAllConnections"))
        {
            flags |= 1u << 18;
        }

        d.U32(flags);
        d.U32((uint)n.Long("defaultThemeVersion"));
        d.WideString(n.Str("codeName"));
        w.Write(XlsbRecordType.WbProp, d);
    }

    private static void WriteBookView(XlsbRecordWriter w, XlsbRecordData d, MarkupNode v)
    {
        d.I32(v.Int("xWindow"));
        d.I32(v.Int("yWindow"));
        d.U32((uint)v.Long("windowWidth"));
        d.U32((uint)v.Long("windowHeight"));
        d.U32((uint)v.Long("tabRatio", 600));
        d.U32((uint)v.Long("firstSheet"));
        d.U32((uint)v.Long("activeTab"));
        string vis = v.Str("visibility", "visible");
        int flags = (vis == "hidden" ? 1 : 0) | (vis == "veryHidden" ? 2 : 0) | (v.Bool("minimized") ? 4 : 0) | (v.Bool("showHorizontalScroll", true) ? 8 : 0)
            | (v.Bool("showVerticalScroll", true) ? 16 : 0) | (v.Bool("showSheetTabs", true) ? 32 : 0) | (v.Bool("autoFilterDateGrouping", true) ? 64 : 0);
        d.U8(flags);
        w.Write(XlsbRecordType.BookView, d);
    }

    private static void WriteCalcProp(XlsbRecordWriter w, XlsbRecordData d, MarkupNode c)
    {
        d.U32((uint)c.Long("calcId", 0));
        d.U32(c.Str("calcMode") switch { "manual" => 0u, "autoNoTable" => 2u, _ => 1u });
        d.U32((uint)c.Long("iterateCount", 100));
        d.Number(c.Dbl("iterateDelta", 0.001));
        d.I32(c.Int("concurrentManualCount", 1));
        int flags = (c.Bool("fullCalcOnLoad") ? 1 : 0) | (c.Str("refMode") == "R1C1" ? 0 : 2) | (c.Bool("iterate") ? 4 : 0) | (c.Bool("fullPrecision", true) ? 8 : 0)
            | (c.Bool("calcOnSave", true) ? 32 : 0) | (c.Bool("concurrentCalc", true) ? 64 : 0) | (c.GetAttribute("concurrentManualCount") is not null ? 128 : 0);
        d.U16(flags);
        w.Write(XlsbRecordType.CalcProp, d);
    }

    private static void WriteExternals(XlsbRecordWriter w, XlsbRecordData d, XlsbFormulaContext cx)
    {
        if (cx.SupBooks.Count == 0 && cx.Xtis.Count == 0)
        {
            return;
        }

        w.Write(XlsbRecordType.BeginExternals);
        foreach (XlsbSupBook b in cx.SupBooks)
        {
            switch (b.Kind)
            {
                case XlsbSupBookKind.Self: w.Write(XlsbRecordType.SupSelf); break;
                case XlsbSupBookKind.Same: w.Write(XlsbRecordType.SupSame); break;
                case XlsbSupBookKind.AddIn: w.Write(XlsbRecordType.SupAddin); break;
                default:
                    d.NullableWideString(b.RelationshipId);
                    w.Write(XlsbRecordType.SupBookSrc, d);
                    break;
            }
        }

        d.U32((uint)cx.Xtis.Count);
        foreach (XlsbXti t in cx.Xtis)
        {
            d.I32(t.SupBook);
            d.I32(t.FirstSheet);
            d.I32(t.LastSheet);
        }

        w.Write(XlsbRecordType.ExternSheet, d);
        w.Write(XlsbRecordType.EndExternals);
    }
}
