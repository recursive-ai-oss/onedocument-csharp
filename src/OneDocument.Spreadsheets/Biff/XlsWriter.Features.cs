using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsWriter
{
    private sealed record NoteOut(int Row, int Col, string Author, string Text);

    // ---- page setup, breaks, protection ----

    private void WritePageBreaks(MarkupNode root)
    {
        MarkupNode rb = Child(root, "rowBreaks");
        var rows = new List<int>();
        foreach (MarkupNode b in rb.IsNull ? [] : rb.ChildElements)
        {
            int id = AttrInt(b, "id", -1);
            if (id is > 0 and <= MaxRows && Attr(b, "man", true))
            {
                rows.Add(id);
            }
        }

        if (rows.Count > 0)
        {
            var b = new BiffBuffer();
            b.U16(rows.Count);
            foreach (int r in rows.Order())
            {
                b.U16(r);
                b.U16(0);
                b.U16(255);
            }

            _s.Record(0x001B, b);
        }

        MarkupNode cb = Child(root, "colBreaks");
        var cols = new List<int>();
        foreach (MarkupNode b in cb.IsNull ? [] : cb.ChildElements)
        {
            int id = AttrInt(b, "id", -1);
            if (id is > 0 and <= MaxColumns && Attr(b, "man", true))
            {
                cols.Add(id);
            }
        }

        if (cols.Count > 0)
        {
            var b = new BiffBuffer();
            b.U16(cols.Count);
            foreach (int c in cols.Order())
            {
                b.U16(c);
                b.U16(0);
                b.U16(0xFFFF);
            }

            _s.Record(0x001A, b);
        }
    }

    private void HeaderFooterRecord(int type, string text)
    {
        var b = new BiffBuffer();
        if (text.Length > 0)
        {
            b.String16(text.Length > 255 ? text[..255] : text);
        }

        _s.Record(type, b);
    }

    private void WritePageSetup(MarkupNode root)
    {
        MarkupNode hf = Child(root, "headerFooter");
        HeaderFooterRecord(0x0014, Child(hf, "oddHeader").InnerText);
        HeaderFooterRecord(0x0015, Child(hf, "oddFooter").InnerText);
        if (Attr(hf, "differentOddEven", false) || Attr(hf, "differentFirst", false))
        {
            _loss.Report("loss.header-footer", "different first and even headers and footers", 1, "Only the odd-page header and footer are written.");
        }

        MarkupNode po = Child(root, "printOptions");
        U16Record(0x0083, Attr(po, "horizontalCentered", false) ? 1 : 0);
        U16Record(0x0084, Attr(po, "verticalCentered", false) ? 1 : 0);
        MarkupNode pm = Child(root, "pageMargins");
        foreach ((int type, string name, double dflt) in new[] { (0x0026, "left", 0.75), (0x0027, "right", 0.75), (0x0028, "top", 1.0), (0x0029, "bottom", 1.0) })
        {
            var b = new BiffBuffer();
            b.F64(AttrDouble(pm, name, dflt));
            _s.Record(type, b);
        }

        MarkupNode ps = Child(root, "pageSetup");
        var setup = new BiffBuffer();
        setup.U16(AttrInt(ps, "paperSize", 1));
        setup.U16(AttrInt(ps, "scale", 100));
        setup.U16(Attr(ps, "useFirstPageNumber", false) ? AttrInt(ps, "firstPageNumber", 1) : 1);
        setup.U16(AttrInt(ps, "fitToWidth", 1));
        setup.U16(AttrInt(ps, "fitToHeight", 1));
        string orientation = ps.IsNull ? "portrait" : ps.GetAttribute("orientation") ?? "portrait";
        string comments = ps.IsNull ? "none" : ps.GetAttribute("cellComments") ?? "none";
        int flags = (ps.GetAttribute("pageOrder") == "overThenDown" ? 1 : 0) | (orientation == "landscape" ? 0 : 2) | (Attr(ps, "blackAndWhite", false) ? 8 : 0)
            | (Attr(ps, "draft", false) ? 0x10 : 0) | (comments != "none" ? 0x20 : 0) | (Attr(ps, "useFirstPageNumber", false) ? 0x80 : 0) | (comments == "atEnd" ? 0x200 : 0);
        setup.U16(flags);
        setup.U16(AttrInt(ps, "horizontalDpi", 600));
        setup.U16(AttrInt(ps, "verticalDpi", 600));
        setup.F64(AttrDouble(pm, "header", 0.5));
        setup.F64(AttrDouble(pm, "footer", 0.5));
        setup.U16(AttrInt(ps, "copies", 1));
        _s.Record(0x00A1, setup);
    }

    private void WriteSheetProtection(MarkupNode root)
    {
        MarkupNode sp = Child(root, "sheetProtection");
        if (sp.IsNull)
        {
            return;
        }

        U16Record(0x0012, Attr(sp, "sheet", false) ? 1 : 0);
        if (Attr(sp, "objects", false))
        {
            U16Record(0x0063, 1);
        }

        if (Attr(sp, "scenarios", false))
        {
            U16Record(0x00DD, 1);
        }

        string? password = sp.GetAttribute("password");
        if (password is not null && int.TryParse(password, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hash))
        {
            U16Record(0x0013, hash);
        }
        else if (sp.GetAttribute("hashValue") is not null)
        {
            _loss.Report("loss.protection-password", "sheet protection password", 1, "A modern password hash cannot be stored as a BIFF8 password; the sheet is protected without one.");
        }

        foreach (MarkupAttributeRef a in sp.Attributes)
        {
            string n = Encoding.UTF8.GetString(a.LocalName);
            if (n is not ("sheet" or "objects" or "scenarios" or "password" or "algorithmName" or "hashValue" or "saltValue" or "spinCount"))
            {
                _loss.Report("loss.protection-options", "sheet protection permissions", 1, "The permissions a protected sheet allows (formatting, inserting, sorting, ...) are not written.");
                break;
            }
        }
    }

    // ---- windows ----

    private void WriteWindows(int si, Worksheet ws, MarkupNode root)
    {
        MarkupNode views = Child(root, "sheetViews");
        MarkupNode view = Child(views, "sheetView");
        MarkupNode pane = Child(view, "pane");
        int top = 0;
        int left = 0;
        if (view.GetAttribute("topLeftCell") is { } tl && CellAddress.TryParse(tl, out CellAddress a))
        {
            top = a.Row - 1;
            left = a.Column - 1;
        }

        bool frozen = pane.GetAttribute("state") is "frozen" or "frozenSplit";
        int flags = (Attr(view, "showFormulas", false) ? 0x01 : 0) | (Attr(view, "showGridLines", true) ? 0x02 : 0) | (Attr(view, "showRowColHeaders", true) ? 0x04 : 0)
            | (frozen ? 0x08 : 0) | (Attr(view, "showZeros", true) ? 0x10 : 0) | 0x20 | (Attr(view, "rightToLeft", false) ? 0x40 : 0) | 0x80
            | (frozen && pane.GetAttribute("state") == "frozen" ? 0x100 : 0) | (si == ActiveSheet() ? 0x200 : 0) | (Attr(view, "tabSelected", false) || si == ActiveSheet() ? 0x400 : 0);
        flags &= ~0x400;
        var w2 = new BiffBuffer();
        w2.U16(flags);
        w2.U16(top);
        w2.U16(left);
        w2.U32(0x40);
        int zoom = AttrInt(view, "zoomScale", 100);
        w2.U16(0);
        w2.U16(AttrInt(view, "zoomScaleNormal", 0));
        w2.U32(0);
        _s.Record(0x023E, w2);
        if (zoom != 100)
        {
            var scl = new BiffBuffer();
            scl.U16(zoom);
            scl.U16(100);
            _s.Record(0x00A0, scl);
        }

        int active = 3;
        if (!pane.IsNull && (AttrInt(pane, "xSplit", 0) > 0 || AttrInt(pane, "ySplit", 0) > 0))
        {
            int x = AttrInt(pane, "xSplit", 0);
            int y = AttrInt(pane, "ySplit", 0);
            int pt = 0;
            int pl = 0;
            if (pane.GetAttribute("topLeftCell") is { } ptl && CellAddress.TryParse(ptl, out CellAddress pa))
            {
                pt = pa.Row - 1;
                pl = pa.Column - 1;
            }

            active = pane.GetAttribute("activePane") switch { "bottomRight" => 0, "topRight" => 1, "bottomLeft" => 2, _ => 3 };
            var p = new BiffBuffer();
            p.U16(x);
            p.U16(y);
            p.U16(pt);
            p.U16(pl);
            p.U8(active);
            p.U8(0);
            _s.Record(0x0041, p);
        }

        // Selections: the ones the view lists, or A1 in the active pane.
        bool wrote = false;
        foreach (MarkupNode sel in view.IsNull ? [] : view.ChildElements)
        {
            if (sel.LocalNameString != "selection")
            {
                continue;
            }

            int pn = sel.GetAttribute("pane") switch { "bottomRight" => 0, "topRight" => 1, "bottomLeft" => 2, "topLeft" => 3, _ => 3 };
            string cell = sel.GetAttribute("activeCell") ?? "A1";
            string sqref = sel.GetAttribute("sqref") ?? cell;
            WriteSelection(pn, cell, sqref);
            wrote = true;
        }

        if (!wrote)
        {
            WriteSelection(active, "A1", "A1");
        }
    }

    private void WriteSelection(int pane, string activeCell, string sqref)
    {
        if (!CellAddress.TryParse(activeCell, out CellAddress a))
        {
            a = new CellAddress(1, 1);
        }

        var refs = new List<(int, int, int, int)>();
        foreach (string part in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryRange(part, out int r1, out int c1, out int r2, out int c2) && r2 < MaxRows && c2 < MaxColumns)
            {
                refs.Add((r1, r2, c1, c2));
            }
        }

        if (refs.Count == 0)
        {
            refs.Add((a.Row - 1, a.Row - 1, a.Column - 1, a.Column - 1));
        }

        var b = new BiffBuffer();
        b.U8(pane);
        b.U16(Math.Min(a.Row - 1, MaxRows - 1));
        b.U16(Math.Min(a.Column - 1, MaxColumns - 1));
        b.U16(0);
        b.U16(refs.Count);
        foreach ((int r1, int r2, int c1, int c2) in refs)
        {
            b.U16(r1);
            b.U16(r2);
            b.U8(c1);
            b.U8(c2);
        }

        _s.Record(0x001D, b);
    }

    private void WriteMerges(Worksheet ws)
    {
        var ranges = new List<CellRange>();
        long outside = 0;
        foreach (CellRange r in ws.MergedCells)
        {
            if (r.BottomRight.Row > MaxRows || r.BottomRight.Column > MaxColumns)
            {
                outside++;
            }
            else
            {
                ranges.Add(r);
            }
        }

        if (outside > 0)
        {
            _loss.Report("loss.cells-outside-grid", "cells outside the BIFF8 grid", outside, "Merged ranges outside the BIFF8 grid were not written.");
        }

        // [MS-XLS] §2.4.168 MergeCells: at most 1,026 ranges to a record.
        for (int i = 0; i < ranges.Count; i += 1026)
        {
            int n = Math.Min(1026, ranges.Count - i);
            var b = new BiffBuffer();
            b.U16(n);
            for (int k = 0; k < n; k++)
            {
                CellRange r = ranges[i + k];
                b.U16(r.TopLeft.Row - 1);
                b.U16(r.BottomRight.Row - 1);
                b.U16(r.TopLeft.Column - 1);
                b.U16(r.BottomRight.Column - 1);
            }

            _s.Record(0x00E5, b);
        }
    }

    // ---- hyperlinks ----

    private static readonly Guid HLinkClassId = new("79EAC9D0-BAF9-11CE-8C82-00AA004BA90B");
    private static readonly Guid UrlMonikerId = new("79EAC9E0-BAF9-11CE-8C82-00AA004BA90B");

    private void WriteHyperlinks(Worksheet ws, MarkupNode root)
    {
        MarkupNode links = Child(root, "hyperlinks");
        if (links.IsNull)
        {
            return;
        }

        IReadOnlyList<Relationship> rels = ws.PartName is null ? [] : _wb.Package.GetRelationships(ws.PartName);
        foreach (MarkupNode h in links.ChildElements)
        {
            if (h.LocalNameString != "hyperlink" || h.GetAttribute("ref") is not { } reference || !TryRange(reference, out int r1, out int c1, out int r2, out int c2) || r2 >= MaxRows || c2 >= MaxColumns)
            {
                _loss.Report("loss.hyperlinks", "hyperlinks", 1, "A hyperlink outside the BIFF8 grid or without a range was not written.");
                continue;
            }

            string? target = null;
            if (h.GetAttribute(_wb.RelationshipNamespace, "id") is { } rid)
            {
                target = rels.FirstOrDefault(r => r.Id == rid)?.Target;
            }

            string? location = h.GetAttribute("location");
            string? display = h.GetAttribute("display");
            if (target is null && location is null)
            {
                continue;
            }

            var b = new BiffBuffer();
            b.U16(r1);
            b.U16(r2);
            b.U16(c1);
            b.U16(c2);
            b.Bytes(HLinkClassId.ToByteArray());
            b.U32(2);
            uint flags = 0;
            if (target is not null)
            {
                flags |= 0x03;
            }

            if (location is not null)
            {
                flags |= 0x08;
            }

            if (!string.IsNullOrEmpty(display))
            {
                flags |= 0x14;
            }

            b.U32(flags);
            if (!string.IsNullOrEmpty(display))
            {
                HyperlinkString(b, display);
            }

            if (target is not null)
            {
                b.Bytes(UrlMonikerId.ToByteArray());
                b.U32((uint)((target.Length + 1) * 2));
                b.Utf16(target);
                b.U16(0);
            }

            if (location is not null)
            {
                HyperlinkString(b, location);
            }

            _s.Record(0x01B8, b);
            if (h.GetAttribute("tooltip") is not null)
            {
                _loss.Report("loss.hyperlink-tooltips", "hyperlink screen tips", 1, "Hyperlink screen tips are not written.");
            }
        }
    }

    private static void HyperlinkString(BiffBuffer b, string s)
    {
        b.U32((uint)(s.Length + 1));
        b.Utf16(s);
        b.U16(0);
    }

    // ---- data validation ----

    private static readonly string[] ValidationOperators = ["between", "notBetween", "equal", "notEqual", "greaterThan", "lessThan", "greaterThanOrEqual", "lessThanOrEqual"];

    private void WriteDataValidations(Worksheet ws)
    {
        var items = new List<DataValidation>();
        foreach (DataValidation dv in ws.Validations)
        {
            if (dv.IsExtended)
            {
                _loss.Report("loss.data-validation", "data validation", 1, "Validation that refers to other sheets (the Excel 2010 extension) cannot be written to BIFF8.");
                continue;
            }

            items.Add(dv);
        }

        if (items.Count == 0)
        {
            return;
        }

        var head = new BiffBuffer();
        head.U16(0);
        head.U32(0);
        head.U32(0);
        head.U32(0xFFFFFFFF);
        head.U32((uint)Math.Min(items.Count, 65534));
        _s.Record(0x01B2, head);
        foreach (DataValidation dv in items.Take(65534))
        {
            WriteDv(dv);
        }

        if (items.Count > 65534)
        {
            _loss.Report("loss.data-validation", "data validation", items.Count - 65534, "A sheet holds at most 65,534 validation rules.");
        }
    }

    private void WriteDv(DataValidation dv)
    {
        int type = dv.Type switch
        {
            DataValidationType.None => 0,
            DataValidationType.WholeNumber => 1,
            DataValidationType.DecimalNumber => 2,
            DataValidationType.List => 3,
            DataValidationType.Date => 4,
            DataValidationType.Time => 5,
            DataValidationType.TextLength => 6,
            DataValidationType.Custom => 7,
            _ => 0,
        };
        var refs = dv.Ranges.Where(r => r.BottomRight.Row <= MaxRows && r.BottomRight.Column <= MaxColumns).ToList();
        if (refs.Count == 0)
        {
            _loss.Report("loss.data-validation", "data validation", 1, "Validation outside the BIFF8 grid was not written.");
            return;
        }

        CellRange first = refs[0];
        string? f1 = dv.Formula1;
        string? f2 = dv.Formula2;
        byte[] g1 = [];
        byte[] g2 = [];
        bool strLookup = false;
        if (type != 0)
        {
            if (type == 3 && f1 is not null && f1.Length >= 2 && f1[0] == '"' && f1[^1] == '"')
            {
                string literal = f1[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal).Replace(',', '\0');
                if (literal.Length > 255)
                {
                    _loss.Report("loss.data-validation", "data validation", 1, "A typed list longer than 255 characters cannot be written.");
                    return;
                }

                var s = new BiffBuffer();
                s.U8(0x17);
                s.String8(literal);
                g1 = s.Span.ToArray();
                strLookup = true;
            }
            else
            {
                if (f1 is null || !TryEncodeText(f1, first.TopLeft.Row - 1, first.TopLeft.Column - 1, out Encoded? e1, out _))
                {
                    _loss.Report("loss.data-validation", "data validation", 1, "A validation formula BIFF8 cannot express was dropped with its rule.");
                    return;
                }

                g1 = e1!.Rgce;
                if (f2 is not null && type is not (3 or 7))
                {
                    if (!TryEncodeText(f2, first.TopLeft.Row - 1, first.TopLeft.Column - 1, out Encoded? e2, out _))
                    {
                        _loss.Report("loss.data-validation", "data validation", 1, "A validation formula BIFF8 cannot express was dropped with its rule.");
                        return;
                    }

                    g2 = e2!.Rgce;
                }
            }
        }

        int op = Math.Max(Array.IndexOf(ValidationOperators, dv.Operator), 0);
        uint flags = (uint)type | ((uint)(int)dv.ErrorStyle << 4) | (strLookup ? 0x80u : 0) | (dv.AllowBlank ? 0x100u : 0) | (!dv.InCellDropDown ? 0x200u : 0)
            | (dv.ShowInputMessage ? 0x40000u : 0) | (dv.ShowErrorMessage ? 0x80000u : 0) | ((uint)op << 20);
        var b = new BiffBuffer();
        b.U32(flags);
        foreach (string? text in new[] { dv.PromptTitle, dv.ErrorTitle, dv.Prompt, dv.Error })
        {
            string t = text ?? string.Empty;
            b.String16(t.Length > 255 ? t[..255] : t);
        }

        b.U16(g1.Length);
        b.U16(0);
        b.Bytes(g1);
        b.U16(g2.Length);
        b.U16(0);
        b.Bytes(g2);
        b.U16(refs.Count);
        foreach (CellRange r in refs)
        {
            b.U16(r.TopLeft.Row - 1);
            b.U16(r.BottomRight.Row - 1);
            b.U16(r.TopLeft.Column - 1);
            b.U16(r.BottomRight.Column - 1);
        }

        _s.Record(0x01BE, b);
    }

    // ---- losses read off the frame ----

    private void ReportSheetLosses(Worksheet ws, MarkupNode root)
    {
        if (ws.Tables.Count > 0)
        {
            _loss.Report("loss.tables", "tables", ws.Tables.Count, "Tables (list objects) are not written; their cells and formulas are.");
        }

        foreach (MarkupNode ext in Find(root, "sparklineGroup"))
        {
            _ = ext;
            _loss.Report("loss.sparklines", "sparkline groups", 1, "Sparklines are not written.");
        }

        MarkupNode drawing = Child(root, "drawing");
        if (!drawing.IsNull && drawing.GetAttribute(_wb.RelationshipNamespace, "id") is { } rid && ws.PartName is not null)
        {
            Relationship? rel = _wb.Package.GetRelationships(ws.PartName).FirstOrDefault(r => r.Id == rid);
            if (rel is not null && OpcPackage.TryResolveTarget(ws.PartName, rel, out PartUri target) && _wb.Package.FindPart(target.ToString()) is { } part)
            {
                try
                {
                    MarkupDocument d = MarkupDocument.Parse(part.GetBytes(), _wb.OpenContext, part.Name);
                    long pics = 0;
                    long charts = 0;
                    long shapes = 0;
                    foreach (MarkupNode n in All(d.Root))
                    {
                        switch (n.LocalNameString)
                        {
                            case "pic":
                                pics++;
                                break;
                            case "graphicFrame":
                                charts++;
                                break;
                            case "sp":
                            case "cxnSp":
                                shapes++;
                                break;
                        }
                    }

                    if (pics > 0)
                    {
                        _loss.Report("loss.pictures", "pictures", pics, "Pictures are not written.");
                    }

                    if (charts > 0)
                    {
                        _loss.Report("loss.charts", "charts", charts, "Charts are not written.");
                    }

                    if (shapes > 0)
                    {
                        _loss.Report("loss.drawing-shapes", "drawing shapes", shapes, "Drawing shapes are not written.");
                    }
                }
                catch (OfficeException)
                {
                    _loss.Report("loss.drawing-shapes", "drawing shapes", 1, "A drawing that could not be read was not written.");
                }
            }
        }

        if (!Child(Child(root, "sheetPr"), "tabColor").IsNull)
        {
            _loss.Report("loss.tab-colors", "sheet tab colours", 1, "Sheet tab colours are not written.");
        }
    }

    private static IEnumerable<MarkupNode> All(MarkupNode n)
    {
        foreach (MarkupNode c in n.ChildElements)
        {
            yield return c;
            foreach (MarkupNode d in All(c))
            {
                yield return d;
            }
        }
    }

    private static IEnumerable<MarkupNode> Find(MarkupNode root, string local)
    {
        foreach (MarkupNode n in All(root))
        {
            if (n.LocalNameString == local)
            {
                yield return n;
            }
        }
    }
}
