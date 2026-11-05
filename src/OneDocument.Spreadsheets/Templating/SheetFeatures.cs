using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Spreadsheets.Templating;

// A piece of text outside the cells that can hold tags: a header or footer string, or a paragraph of a text box.
internal sealed record Slot(string Kind, string Key, int Ordinal, string Text);

// A comment of the template: where it sits, and its markup (and the matching legacy drawing shape) to copy for every place it lands.
internal sealed class CapturedComment
{
    public int Row;
    public int Column;
    public string Xml = string.Empty;
}

internal sealed class TableInfo
{
    public string Name = string.Empty;
    public CellRange Range;
    public bool HasHeader;
    public string? Style;
}

internal sealed record PendingImage(int Row, int Column, TemplateInsertion Insertion);

// Everything a sheet has beside its cells, copied before any editing so every copy of a sheet block starts from the template.
internal sealed class SheetSnapshot
{
    public List<string> FrameXml = [];
    public string? DrawingPart;
    public string? CommentsPart;
    public byte[]? CommentsBytes;
    public string? CommentsType;
    public string? VmlPart;
    public byte[]? VmlBytes;
    public string? VmlType;
    public List<TableInfo> Tables = [];
    public List<Slot> Slots = [];
}

internal static class SheetFeatures
{
    private static readonly string[] CopiedElements = ["conditionalFormatting", "dataValidations", "printOptions", "pageMargins", "pageSetup", "headerFooter", "rowBreaks", "colBreaks"];
    private static readonly string[] HeaderFooterElements = ["oddHeader", "oddFooter", "evenHeader", "evenFooter", "firstHeader", "firstFooter"];
    private const string SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    // ---- reading what the template sheet has -------------------------------------------------------------------------------------

    /// <summary>
    /// Takes the snapshot, finds the tags outside the cells, and returns the comments as pseudo-cells so they travel with their row.
    /// Returns null when there is nothing beside the cells to carry (the common case).
    /// </summary>
    public static SheetSnapshot Take(Worksheet ws, TemplateDelimiters d, out List<CapturedComment> comments, out bool hasTags)
    {
        Workbook wb = ws.Workbook;
        var snap = new SheetSnapshot();
        comments = [];
        hasTags = false;
        MarkupNode root = ws.FrameRoot;
        foreach (MarkupNode c in root.ChildElements)
        {
            string local = c.LocalNameString;
            if (Array.IndexOf(CopiedElements, local) >= 0)
            {
                snap.FrameXml.Add(Declare(StripExtensions(c.OuterXml), c));
            }

            if (local == "headerFooter")
            {
                foreach (MarkupNode h in c.ChildElements)
                {
                    if (Array.IndexOf(HeaderFooterElements, h.LocalNameString) >= 0 && HasTag(h.InnerText, d))
                    {
                        hasTags = true;
                        snap.Slots.Add(new Slot("hf", h.LocalNameString, 0, h.InnerText));
                    }
                }
            }
        }

        string? sheetPart = ws.PartName;
        if (sheetPart is not null)
        {
            if (PartTools.Related(wb, sheetPart, "/drawing") is [{ } dr, ..])
            {
                snap.DrawingPart = dr.Part;
                MarkupDocument doc = MarkupDocument.Parse(wb.Package.FindPart(dr.Part)!.GetBytes(), wb.OpenContext, dr.Part);
                int ordinal = 0;
                foreach (MarkupNode p in Paragraphs(doc.Root))
                {
                    string pt = ParagraphText(p);
                    if (HasTag(pt, d))
                    {
                        hasTags = true;
                        snap.Slots.Add(new Slot("shape", string.Empty, ordinal, pt));
                    }

                    ordinal++;
                }
            }

            if (PartTools.Related(wb, sheetPart, "/comments") is [{ } cm, ..])
            {
                IPackagePart part = wb.Package.FindPart(cm.Part)!;
                snap.CommentsPart = cm.Part;
                snap.CommentsBytes = part.GetBytes().ToArray();
                snap.CommentsType = part.ContentType;
                MarkupDocument doc = MarkupDocument.Parse(snap.CommentsBytes, wb.OpenContext, cm.Part);
                foreach (MarkupNode comment in PartTools.Child(doc.Root, "commentList").ChildElements)
                {
                    if (comment.LocalNameString == "comment" && comment.GetAttribute("ref") is { } r && CellAddress.TryParse(r, out CellAddress a))
                    {
                        comments.Add(new CapturedComment { Row = a.Row, Column = a.Column, Xml = comment.OuterXml });
                        hasTags |= HasTag(CommentText(comment), d);
                    }
                }

                if (PartTools.Related(wb, sheetPart, "/vmlDrawing") is [{ } vm, ..] && wb.Package.FindPart(vm.Part) is { } vpart)
                {
                    snap.VmlPart = vm.Part;
                    snap.VmlBytes = vpart.GetBytes().ToArray();
                    snap.VmlType = vpart.ContentType;
                }
            }
        }

        if (!hasTags)
        {
            comments = [];
        }

        return snap;
    }

    private static bool HasTag(string text, TemplateDelimiters d) =>
        text.Contains(d.ExpressionOpen, StringComparison.Ordinal) || text.Contains(d.BlockOpen, StringComparison.Ordinal) || text.Contains(d.CommentOpen, StringComparison.Ordinal);

    private static string StripExtensions(string xml) => Regex.Replace(xml, @"<(\w+:)?extLst>.*?</(\w+:)?extLst>", string.Empty, RegexOptions.Singleline);

    // The element is written into another sheet's tree, which may not declare the prefix the template used.
    private static string Declare(string xml, MarkupNode node)
    {
        int end = xml.IndexOfAny([' ', '>', '/']);
        string ns = node.NamespaceUri;
        string decl = node.Prefix.Length == 0 ? " xmlns=\"" + ns + "\"" : " xmlns:" + Encoding.UTF8.GetString(node.Prefix) + "=\"" + ns + "\"";
        return xml.Contains(decl.TrimStart(), StringComparison.Ordinal) ? xml : xml.Insert(end, decl);
    }

    public static string CommentText(MarkupNode comment)
    {
        MarkupNode text = PartTools.Child(comment, "text");
        var sb = new StringBuilder();
        foreach (MarkupNode n in PartTools.Descendants(text))
        {
            if (n.LocalNameString == "t" && n.Parent.LocalNameString != "rPh")
            {
                sb.Append(n.InnerText);
            }
        }

        return sb.ToString();
    }

    private static IEnumerable<MarkupNode> Paragraphs(MarkupNode root) =>
        PartTools.Descendants(root).Where(static n => n.LocalNameString == "p" && n.NamespaceUri == PartTools.DrawingNs);

    private static string ParagraphText(MarkupNode p) =>
        string.Concat(PartTools.Descendants(p).Where(static n => n.LocalNameString == "t" && n.NamespaceUri == PartTools.DrawingNs).Select(static n => n.InnerText));

    public static void ReadTables(SheetSnapshot snap, Worksheet ws)
    {
        foreach (Table t in ws.Tables)
        {
            snap.Tables.Add(new TableInfo { Name = t.Name, Range = t.Range, HasHeader = t.HeaderRowCount > 0, Style = t.StyleName });
        }
    }

    // ---- sheet copies ------------------------------------------------------------------------------------------------------------

    /// <summary>Copies the drawing parts of the template for every extra copy of a sheet block, before the template's own are edited.</summary>
    public static void PrepareCopies(Workbook wb, SheetPlan plan)
    {
        if (plan.Snap?.DrawingPart is not { } drawing)
        {
            return;
        }

        for (int i = 1; i < plan.Output.Count; i++)
        {
            plan.Output[i].DrawingPart = PartTools.Duplicate(wb, drawing, []);
        }
    }

    /// <summary>Gives a new copy of a sheet the template's page setup, conditional formats and validations, comments and drawing.</summary>
    public static void CopyFrame(Workbook wb, SheetPlan plan, OutSheet o, Worksheet target)
    {
        SheetSnapshot snap = plan.Snap!;
        foreach (string xml in snap.FrameXml)
        {
            int start = xml.IndexOf('<', StringComparison.Ordinal) + 1;
            int end = xml.IndexOfAny([' ', '>', '/'], start);
            string name = xml[start..end];
            int colon = name.IndexOf(':', StringComparison.Ordinal);
            target.InsertFrameElement(colon >= 0 ? name[(colon + 1)..] : name, xml);
        }

        target.NoteFrameEdited();
        if (target.PartName is not { } sheetPart)
        {
            return;
        }

        string pc = Xl.Prefix(target.FrameRoot);
        if (o.DrawingPart is { } dp)
        {
            string rid = AddRelationship(wb, sheetPart, PartTools.RelBase + "drawing", dp);
            target.InsertFrameElement("drawing", "<" + pc + "drawing xmlns:r=\"" + PartTools.RelNs + "\" r:id=\"" + rid + "\"/>");
        }
    }

    private static string AddRelationship(Workbook wb, string source, string type, string target)
    {
        var rels = new List<Relationship>(wb.Package.GetRelationships(source));
        string id = PartTools.NextRelationshipId(rels);
        rels.Add(new Relationship(id, type, PartTools.Relative(source, target)));
        wb.Package.SetRelationships(source, rels);
        return id;
    }

    // ECMA-376 Part 1 §18.5.1: a table's name and id are unique in the workbook, so a copy of a sheet gets its own.
    public static void CopyTables(Workbook wb, SheetPlan plan, OutSheet o, Worksheet target, Func<CellRange, CellRange?> map)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Worksheet s in wb.Sheets)
        {
            if (s.Kind == SheetKind.Worksheet)
            {
                foreach (Table t in s.Tables)
                {
                    used.Add(t.Name);
                }
            }
        }

        foreach (DefinedName n in wb.Names)
        {
            used.Add(n.Name);
        }

        foreach (TableInfo info in plan.Snap!.Tables)
        {
            if (map(info.Range) is not { IsValid: true } range)
            {
                continue;
            }

            string name = info.Name;
            for (int k = 2; used.Contains(name); k++)
            {
                name = info.Name + "_" + k.ToString(CultureInfo.InvariantCulture);
            }

            used.Add(name);
            if (info.HasHeader ? range.RowCount < 2 : range.RowCount < 1)
            {
                continue;
            }

            target.Tables.Add(name, range, info.HasHeader, info.Style);
        }

        _ = o;
    }

    // ---- text outside the cells --------------------------------------------------------------------------------------------------

    public static void ApplyHeaderFooter(SheetPlan plan, OutSheet o, Worksheet target)
    {
        MarkupNode hf = PartTools.Child(target.FrameRoot, "headerFooter");
        if (hf.IsNull)
        {
            return;
        }

        foreach ((int unit, Slot slot) in plan.SlotUnits)
        {
            if (slot.Kind == "hf" && o.Extra.TryGetValue(unit, out string? text) && PartTools.Child(hf, slot.Key) is { IsNull: false } node)
            {
                node.SetText(text);
                target.NoteFrameEdited();
            }
        }
    }

    // ---- comments ----------------------------------------------------------------------------------------------------------------

    public static void WriteComments(Workbook wb, SheetPlan plan, OutSheet o, Worksheet target, bool isOriginal)
    {
        SheetSnapshot snap = plan.Snap!;
        if (snap.CommentsBytes is null || target.PartName is not { } sheetPart)
        {
            return;
        }

        MarkupDocument doc = MarkupDocument.Parse(snap.CommentsBytes, wb.OpenContext, snap.CommentsPart);
        MarkupNode list = PartTools.Child(doc.Root, "commentList");
        var originals = list.ChildElements.ToList();
        var moved = new List<(CapturedComment Source, int Row, int Column)>();
        foreach (OutComment oc in o.Comments)
        {
            list.AppendXml(Encoding.UTF8.GetBytes(oc.Source.Xml));
            MarkupNode n = Xl.LastElement(list);
            n.SetAttribute("ref", new CellAddress(oc.Row, oc.Column).ToString());
            if (oc.Text is not null)
            {
                ReplaceCommentText(n, oc.Text);
            }

            moved.Add((oc.Source, oc.Row, oc.Column));
        }

        foreach (MarkupNode old in originals)
        {
            old.Remove();
        }

        byte[]? vml = snap.VmlBytes is null ? null : BuildVml(wb, plan, snap.VmlBytes, moved);
        if (isOriginal)
        {
            PartTools.Write(wb.Package.FindPart(snap.CommentsPart!)!, doc);
            if (vml is not null && snap.VmlPart is not null)
            {
                PartTools.Write(wb.Package.FindPart(snap.VmlPart)!, vml);
            }

            return;
        }

        string commentsName = PartTools.NextName(wb.Package, "/xl/comments", ".xml");
        wb.Package.AddPart(commentsName, snap.CommentsType!, doc.ToArray());
        AddRelationship(wb, sheetPart, PartTools.RelBase + "comments", commentsName);
        if (vml is not null)
        {
            string vmlName = PartTools.NextName(wb.Package, "/xl/drawings/vmlDrawing", ".vml");
            wb.Package.AddPart(vmlName, snap.VmlType!, vml);
            string rid = AddRelationship(wb, sheetPart, PartTools.RelBase + "vmlDrawing", vmlName);
            target.InsertFrameElement("legacyDrawing", "<" + Xl.Prefix(target.FrameRoot) + "legacyDrawing xmlns:r=\"" + PartTools.RelNs + "\" r:id=\"" + rid + "\"/>");
            target.NoteFrameEdited();
        }
    }

    // A comment keeps the formatting of its first run; the rendered text replaces what the runs said.
    private static void ReplaceCommentText(MarkupNode comment, string text)
    {
        MarkupNode body = PartTools.Child(comment, "text");
        if (body.IsNull)
        {
            return;
        }

        bool first = true;
        foreach (MarkupNode child in body.ChildElements.ToList())
        {
            if (!first || child.LocalNameString is not ("r" or "t"))
            {
                if (child.LocalNameString is "r" or "t" or "rPh" or "phoneticPr")
                {
                    child.Remove();
                }

                continue;
            }

            first = false;
            MarkupNode t = child.LocalNameString == "t" ? child : PartTools.Child(child, "t");
            if (!t.IsNull)
            {
                t.SetText(text);
                t.SetAttribute("xml:space", "preserve");
            }
        }
    }

    // The note shapes of the legacy drawing: one per comment, positioned by its own row and column (0-based), which move with the comment.
    private static byte[] BuildVml(Workbook wb, SheetPlan plan, byte[] source, List<(CapturedComment Source, int Row, int Column)> moved)
    {
        MarkupDocument doc;
        try
        {
            doc = MarkupDocument.Parse(source, wb.OpenContext);
        }
        catch (OfficeFormatException)
        {
            return source;
        }

        MarkupNode root = doc.Root;
        var shapes = new Dictionary<(int, int), MarkupNode>();
        var notes = new List<MarkupNode>();
        foreach (MarkupNode s in PartTools.Descendants(root))
        {
            if (s.LocalNameString != "shape")
            {
                continue;
            }

            MarkupNode cd = PartTools.Child(s, "ClientData");
            if (cd.IsNull || cd.GetAttribute("ObjectType") != "Note")
            {
                continue;
            }

            notes.Add(s);
            if (int.TryParse(PartTools.Child(cd, "Row").InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)
                && int.TryParse(PartTools.Child(cd, "Column").InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int c))
            {
                shapes[(r, c)] = s;
            }
        }

        int id = 1025;
        foreach ((CapturedComment src, int row, int col) in moved)
        {
            if (!shapes.TryGetValue((src.Row - 1, src.Column - 1), out MarkupNode shape))
            {
                continue;
            }

            string xml = shape.OuterXml;
            MarkupNode parent = shape.Parent;
            parent.AppendXml(Encoding.UTF8.GetBytes(xml));
            MarkupNode copy = Xl.LastElement(parent);
            copy.SetAttribute("id", "_x0000_s" + id.ToString(CultureInfo.InvariantCulture));
            id++;
            MarkupNode cd = PartTools.Child(copy, "ClientData");
            PartTools.Child(cd, "Row").SetText((row - 1).ToString(CultureInfo.InvariantCulture));
            PartTools.Child(cd, "Column").SetText((col - 1).ToString(CultureInfo.InvariantCulture));
            MarkupNode anchor = PartTools.Child(cd, "Anchor");
            if (!anchor.IsNull)
            {
                string[] parts = anchor.InnerText.Split(',');
                if (parts.Length == 8 && int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r1) && int.TryParse(parts[6].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r2)
                    && int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c1) && int.TryParse(parts[4].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c2))
                {
                    int dr = plan.RowMode ? row - src.Row : 0;
                    int dc = plan.RowMode ? 0 : col - src.Column;
                    parts[0] = (c1 + dc).ToString(CultureInfo.InvariantCulture);
                    parts[4] = (c2 + dc).ToString(CultureInfo.InvariantCulture);
                    parts[2] = (r1 + dr).ToString(CultureInfo.InvariantCulture);
                    parts[6] = (r2 + dr).ToString(CultureInfo.InvariantCulture);
                    anchor.SetText(string.Join(", ", parts.Select(static p => p.Trim())));
                }
            }
        }

        foreach (MarkupNode n in notes)
        {
            n.Remove();
        }

        return doc.ToArray();
    }

    // ---- drawings and charts -----------------------------------------------------------------------------------------------------

    public static void ProcessDrawing(Workbook wb, SheetPlan plan, OutSheet o, Rewriter rw, string drawingPart, HashSet<string> charts)
    {
        IPackagePart? part = wb.Package.FindPart(drawingPart);
        if (part is null)
        {
            return;
        }

        MarkupDocument doc = MarkupDocument.Parse(part.GetBytes(), wb.OpenContext, drawingPart);
        foreach (MarkupNode anchor in doc.Root.ChildElements)
        {
            MapAnchor(plan, o, anchor);
        }

        int ordinal = 0;
        foreach (MarkupNode p in Paragraphs(doc.Root).ToList())
        {
            foreach ((int unit, Slot slot) in plan.SlotUnits)
            {
                if (slot.Kind == "shape" && slot.Ordinal == ordinal && o.Extra.TryGetValue(unit, out string? text))
                {
                    bool first = true;
                    foreach (MarkupNode t in PartTools.Descendants(p).Where(static n => n.LocalNameString == "t" && n.NamespaceUri == PartTools.DrawingNs).ToList())
                    {
                        t.SetText(first ? text : string.Empty);
                        first = false;
                    }
                }
            }

            ordinal++;
        }

        if (doc.IsModified)
        {
            PartTools.Write(part, doc);
        }

        var ctx = new OwnerContext(plan, o, -1, 0);
        foreach ((Relationship _, string chart) in PartTools.Related(wb, drawingPart, "/chart"))
        {
            ProcessChart(wb, rw, chart, ctx, plan.OldName, charts, forceRefresh: true);
        }
    }

    // A drawing anchored to rows that moved moves with them (ECMA-376 Part 1 §20.5.2.33, from and to are 0-based).
    private static void MapAnchor(SheetPlan plan, OutSheet o, MarkupNode anchor)
    {
        if (anchor.LocalNameString is not ("twoCellAnchor" or "oneCellAnchor"))
        {
            return;
        }

        MarkupNode from = PartTools.Child(anchor, "from");
        MarkupNode to = PartTools.Child(anchor, "to");
        string axis = plan.RowMode ? "row" : "col";
        MapAnchorEnd(o, from, axis, start: true);
        MapAnchorEnd(o, to, axis, start: false);
    }

    private static void MapAnchorEnd(OutSheet o, MarkupNode end, string axis, bool start)
    {
        if (end.IsNull || PartTools.Child(end, axis) is not { IsNull: false } n || !int.TryParse(n.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int zero))
        {
            return;
        }

        int mapped = start ? o.Map.Start(zero + 1) : o.Map.End(zero + 1);
        if (mapped > 0 && mapped - 1 != zero)
        {
            n.SetText((mapped - 1).ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Rewrites the series references of a chart part like formulas; the cached values are refreshed later, from the rendered cells.</summary>
    public static void ProcessChart(Workbook wb, Rewriter rw, string chartPart, OwnerContext? ctx, string ownerSheetOld, HashSet<string> charts, bool forceRefresh)
    {
        IPackagePart? part = wb.Package.FindPart(chartPart);
        if (part is null)
        {
            return;
        }

        MarkupDocument doc;
        try
        {
            doc = MarkupDocument.Parse(part.GetBytes(), wb.OpenContext, chartPart);
        }
        catch (OfficeFormatException)
        {
            return;
        }

        bool changed = false;
        foreach (MarkupNode f in PartTools.Descendants(doc.Root))
        {
            if (f.LocalNameString != "f" || f.Parent.LocalNameString is not ("numRef" or "strRef" or "multiLvlStrRef" or "datalabelsRange"))
            {
                continue;
            }

            string text = f.InnerText;
            string rewritten = rw.RewriteText(text, ownerSheetOld, ctx, null);
            if (rewritten != text)
            {
                f.SetText(rewritten);
                changed = true;
            }
        }

        if (changed)
        {
            PartTools.Write(part, doc);
        }

        if (changed || forceRefresh)
        {
            charts.Add(chartPart);
        }
    }

    /// <summary>Rebuilds the cached points of every series of the given charts from the cells they now refer to.</summary>
    public static void RefreshCaches(Workbook wb, IEnumerable<string> chartParts)
    {
        foreach (string name in chartParts.Order(StringComparer.Ordinal))
        {
            IPackagePart? part = wb.Package.FindPart(name);
            if (part is null)
            {
                continue;
            }

            MarkupDocument doc = MarkupDocument.Parse(part.GetBytes(), wb.OpenContext, name);
            foreach (MarkupNode r in PartTools.Descendants(doc.Root).ToList())
            {
                if (r.LocalNameString is "numRef" or "strRef")
                {
                    RefreshCache(wb, r);
                }
            }

            if (doc.IsModified)
            {
                PartTools.Write(part, doc);
            }
        }
    }

    private static void RefreshCache(Workbook wb, MarkupNode reference)
    {
        MarkupNode f = PartTools.Child(reference, "f");
        if (f.IsNull || !FormulaParser.TryParse(f.InnerText, out FormulaNode? node, out _) || node is null)
        {
            return;
        }

        string? sheetName;
        int r1;
        int c1;
        int r2;
        int c2;
        switch (node)
        {
            case CellReferenceNode c:
                sheetName = c.Prefix?.Sheet;
                r1 = r2 = c.Row.Index;
                c1 = c2 = c.Column.Index;
                break;
            case AreaReferenceNode a when a.AreaKind == AreaKind.Cells:
                sheetName = a.Prefix?.Sheet;
                r1 = a.Row1.Index;
                r2 = a.Row2.Index;
                c1 = a.Column1.Index;
                c2 = a.Column2.Index;
                break;
            default:
                return;
        }

        if (sheetName is null || !wb.Sheets.TryGet(sheetName, out Worksheet? ws) || ws.Kind != SheetKind.Worksheet || (r2 - r1 + 1) * (c2 - c1 + 1) > 1_000_000)
        {
            return;
        }

        bool numeric = reference.LocalNameString == "numRef";
        var points = new List<(int Index, string Text)>();
        int idx = 0;
        for (int r = r1; r <= r2; r++)
        {
            for (int c = c1; c <= c2; c++, idx++)
            {
                CellValue v = ws[r, c].Value;
                switch (v.Kind)
                {
                    case CellValueKind.Number:
                        points.Add((idx, v.Number.ToString("R", CultureInfo.InvariantCulture)));
                        break;
                    case CellValueKind.Boolean:
                        points.Add((idx, numeric ? (v.Boolean ? "1" : "0") : (v.Boolean ? "TRUE" : "FALSE")));
                        break;
                    case CellValueKind.Text when !numeric:
                        points.Add((idx, ws.GetText(v)));
                        break;
                }
            }
        }

        string p = reference.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(reference.Prefix) + ":";
        string cacheName = numeric ? "numCache" : "strCache";
        MarkupNode old = PartTools.Child(reference, cacheName);
        string format = "General";
        if (!old.IsNull && PartTools.Child(old, "formatCode") is { IsNull: false } fc)
        {
            format = fc.InnerText;
        }

        var sb = new StringBuilder();
        // The namespace is declared by the element this goes into.
        sb.Append('<').Append(p).Append(cacheName).Append('>');
        if (numeric)
        {
            sb.Append('<').Append(p).Append("formatCode>").Append(PartTools.Escape(format)).Append("</").Append(p).Append("formatCode>");
        }

        sb.Append('<').Append(p).Append("ptCount val=\"").Append(idx.ToString(CultureInfo.InvariantCulture)).Append("\"/>");
        foreach ((int i, string text) in points)
        {
            sb.Append('<').Append(p).Append("pt idx=\"").Append(i.ToString(CultureInfo.InvariantCulture)).Append("\"><").Append(p).Append("v>").Append(PartTools.Escape(text)).Append("</").Append(p).Append("v></").Append(p).Append("pt>");
        }

        sb.Append("</").Append(p).Append(cacheName).Append('>');
        if (old.IsNull)
        {
            f.InsertXmlAfter(Encoding.UTF8.GetBytes(sb.ToString()));
        }
        else
        {
            old.ReplaceWithXml(Encoding.UTF8.GetBytes(sb.ToString()));
        }
    }

    // ---- pictures ----------------------------------------------------------------------------------------------------------------

    public sealed class ImageStore
    {
        public readonly Dictionary<string, string> Media = [];
    }

    public static void AddImages(Workbook wb, OutSheet o, Worksheet target, List<Defect> warnings, ImageStore store)
    {
        if (o.Images.Count == 0 || target.PartName is not { } sheetPart)
        {
            return;
        }

        string drawingPart = FindOrCreateDrawing(wb, target, sheetPart);
        IPackagePart part = wb.Package.FindPart(drawingPart)!;
        MarkupDocument doc = MarkupDocument.Parse(part.GetBytes(), wb.OpenContext, drawingPart);
        int shapeId = 1;
        foreach (MarkupNode n in PartTools.Descendants(doc.Root))
        {
            if (n.LocalNameString == "cNvPr" && int.TryParse(n.GetAttribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int existing))
            {
                shapeId = Math.Max(shapeId, existing + 1);
            }
        }

        foreach (PendingImage image in o.Images)
        {
            var location = DefectLocation.InCell(o.FinalName, new CellAddress(image.Row, image.Column).ToString());
            byte[]? bytes = ImageHeader.Bytes(image.Insertion.Value.Raw);
            if (bytes is null || !ImageHeader.TryRead(bytes, out ImageHeader info))
            {
                warnings.Add(new Defect(DefectKind.Value, "template.image-invalid", DefectSeverity.Dropped, "The image value is not PNG, JPEG, GIF or BMP data (bytes, or base64 text); no picture was added.", location));
                continue;
            }

            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!store.Media.TryGetValue(hash, out string? media))
            {
                media = PartTools.NextName(wb.Package, "/xl/media/image", info.Extension);
                wb.Package.AddPart(media, info.ContentType, bytes);
                store.Media[hash] = media;
            }

            var rels = new List<Relationship>(wb.Package.GetRelationships(drawingPart));
            string rid = PartTools.NextRelationshipId(rels);
            rels.Add(new Relationship(rid, PartTools.RelBase + "image", PartTools.Relative(drawingPart, media)));
            wb.Package.SetRelationships(drawingPart, rels);

            image.Insertion.Options.TryGetValue("alt", out string? alt);
            int w = info.Width > 0 ? info.Width : 96;
            int h = info.Height > 0 ? info.Height : 96;
            bool hasW = image.Insertion.Options.TryGetValue("width", out string? ws) && int.TryParse(ws, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pw) && pw > 0;
            bool hasH = image.Insertion.Options.TryGetValue("height", out string? hs) && int.TryParse(hs, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ph) && ph > 0;
            int widthPx = hasW ? int.Parse(ws!, CultureInfo.InvariantCulture) : w;
            int heightPx = hasH ? int.Parse(hs!, CultureInfo.InvariantCulture) : h;
            if (hasW && !hasH)
            {
                heightPx = Math.Max(1, (int)Math.Round((double)widthPx * h / w));
            }
            else if (hasH && !hasW)
            {
                widthPx = Math.Max(1, (int)Math.Round((double)heightPx * w / h));
            }

            (int col2, int colOff) = Walk(image.Column, widthPx, c => ColumnPixels(target, c));
            (int row2, int rowOff) = Walk(image.Row, heightPx, r => RowPixels(target, r));
            var sb = new StringBuilder();
            sb.Append("<xdr:twoCellAnchor xmlns:xdr=\"").Append(SpreadsheetDrawingNs).Append("\" xmlns:a=\"").Append(PartTools.DrawingNs).Append("\" xmlns:r=\"").Append(PartTools.RelNs).Append("\" editAs=\"oneCell\">");
            sb.Append("<xdr:from><xdr:col>").Append(image.Column - 1).Append("</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>").Append(image.Row - 1).Append("</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>");
            sb.Append("<xdr:to><xdr:col>").Append(col2 - 1).Append("</xdr:col><xdr:colOff>").Append(colOff * 9525L).Append("</xdr:colOff><xdr:row>").Append(row2 - 1).Append("</xdr:row><xdr:rowOff>").Append(rowOff * 9525L).Append("</xdr:rowOff></xdr:to>");
            sb.Append("<xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"").Append(shapeId).Append("\" name=\"Picture ").Append(shapeId).Append('"');
            if (alt is not null)
            {
                sb.Append(" descr=\"").Append(PartTools.Escape(alt)).Append('"');
            }

            sb.Append("/><xdr:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></xdr:cNvPicPr></xdr:nvPicPr><xdr:blipFill><a:blip r:embed=\"").Append(rid).Append("\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>");
            sb.Append("<xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(widthPx * 9525L).Append("\" cy=\"").Append(heightPx * 9525L).Append("\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:twoCellAnchor>");
            doc.Root.AppendXml(Encoding.UTF8.GetBytes(sb.ToString()));
            shapeId++;
        }

        PartTools.Write(part, doc);
    }

    // Walks from a cell over as many columns (or rows) as the picture covers; returns the 1-based cell it ends in and the offset in pixels.
    private static (int Index, int Offset) Walk(int start, int pixels, Func<int, int> size)
    {
        int i = start;
        int left = pixels;
        while (i < CellAddress.MaxRows)
        {
            int s = size(i);
            if (left <= s)
            {
                return (i, left);
            }

            left -= s;
            i++;
        }

        return (i, 0);
    }

    private static int ColumnPixels(Worksheet ws, int column)
    {
        foreach (ColumnProperties c in ws.Columns)
        {
            if (column >= c.Min && column <= c.Max && c.Width > 0)
            {
                return (int)Math.Round(c.Width * 7);
            }
        }

        return 64;
    }

    private static int RowPixels(Worksheet ws, int row)
    {
        double h = ws.GetRow(row).Height;
        return h > 0 ? (int)Math.Round(h * 96 / 72) : 20;
    }

    private static string FindOrCreateDrawing(Workbook wb, Worksheet target, string sheetPart)
    {
        if (PartTools.Related(wb, sheetPart, "/drawing") is [{ } existing, ..])
        {
            return existing.Part;
        }

        string name = PartTools.NextName(wb.Package, "/xl/drawings/drawing", ".xml");
        wb.Package.AddPart(name, "application/vnd.openxmlformats-officedocument.drawing+xml",
            Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<xdr:wsDr xmlns:xdr=\"" + SpreadsheetDrawingNs + "\" xmlns:a=\"" + PartTools.DrawingNs + "\"/>"));
        string rid = AddRelationship(wb, sheetPart, PartTools.RelBase + "drawing", name);
        target.InsertFrameElement("drawing", "<" + Xl.Prefix(target.FrameRoot) + "drawing xmlns:r=\"" + PartTools.RelNs + "\" r:id=\"" + rid + "\"/>");
        target.NoteFrameEdited();
        return name;
    }
}

internal sealed record OutComment(CapturedComment Source, int Row, int Column, string? Text);
