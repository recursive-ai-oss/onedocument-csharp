using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Accessibility;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>Checks a presentation against the accessibility rules: slide by slide, then the sections and the file properties.</summary>
internal sealed class PresentationAccessibilityChecker
{
    private readonly Presentation _deck;
    private readonly AccessibilityCollector _c;
    private readonly CancellationToken _ct;
    private bool _anyRun;
    private bool _anyLang;

    public PresentationAccessibilityChecker(Presentation deck, CancellationToken ct)
    {
        _deck = deck;
        _c = new AccessibilityCollector(OfficeKind.Presentation, deck.Limits.MaxDefects);
        _ct = ct;
    }

    public AccessibilityReport Run()
    {
        var titles = new Dictionary<string, int>(StringComparer.Ordinal);
        IReadOnlyList<Slide> slides = _deck.Slides;
        for (int i = 0; i < slides.Count; i++)
        {
            _ct.ThrowIfCancellationRequested();
            CheckSlide(slides[i], i + 1, titles);
        }

        CheckSections();
        string? title = null;
        try
        {
            var props = new DocumentProperties(_deck.Package, _deck.Context);
            title = props.Core.Title;
        }
        catch (OfficeException)
        {
            // An unreadable properties part is a defect of the open, not a finding.
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.DocumentTitle, new AccessibilityLocation(), null, "The presentation has no title property.",
                "Set the title in the file properties; screen readers announce it when the file opens."));
        }

        if (_anyRun && !_anyLang)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.DocumentLanguage, new AccessibilityLocation(), null, "No text run declares a language.",
                "Set the proofing language of the text so screen readers choose the right voice."));
        }

        return _c.ToReport();
    }

    private void CheckSections()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PresentationSection section in _deck.Sections)
        {
            var loc = new AccessibilityLocation { Section = section.Name };
            if (AccessibilityText.IsDefaultSectionName(section.Name))
            {
                _c.Add(new AccessibilityIssue(AccessibilityRules.SectionDefaultName, loc, section.Name,
                    "The section “" + section.Name + "” has a placeholder name.", "Rename the section so it says what its slides are about."));
            }

            if (!seen.Add(AccessibilityText.TitleKey(section.Name)))
            {
                _c.Add(new AccessibilityIssue(AccessibilityRules.SectionDuplicateName, loc, section.Name,
                    "Another section is also called “" + section.Name + "”.", "Give each section its own name."));
            }
        }
    }

    // ---- slides ------------------------------------------------------------------------------------------------------------------------------

    private static MarkupNode Child(MarkupNode n, string local)
    {
        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                return c;
            }
        }

        return default;
    }

    private static IEnumerable<Shape> Flat(IEnumerable<Shape> shapes)
    {
        foreach (Shape s in shapes)
        {
            if (s.Kind == ShapeKind.AlternateContent)
            {
                foreach (Shape inner in Flat(s.Children))
                {
                    yield return inner;
                }
            }
            else
            {
                yield return s;
            }
        }
    }

    private void CheckSlide(Slide slide, int number, Dictionary<string, int> titles)
    {
        var slideLoc = new AccessibilityLocation { Part = slide.PartName, Slide = number };
        List<Shape> top = [.. Flat(slide.Shapes)];

        // ECMA-376 Part 1 §19.3.1.36 (ph): the title placeholder is type title or ctrTitle.
        Shape? titleShape = null;
        foreach (Shape s in slide.AllShapes())
        {
            if (s.Kind == ShapeKind.Shape && s.Placeholder is { } ph && ph.Type is "title" or "ctrTitle")
            {
                titleShape = s;
                break;
            }
        }

        string title = titleShape?.TextBody?.Text.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.SlideTitleMissing, slideLoc, null, "Slide " + number.ToString(CultureInfo.InvariantCulture) + " has no title.",
                "Add a title to the slide (a hidden or off-slide title still counts), so screen readers and the slide navigator can name it."));
        }
        else
        {
            string key = AccessibilityText.TitleKey(title);
            if (titles.TryGetValue(key, out int first))
            {
                _c.Add(new AccessibilityIssue(AccessibilityRules.SlideTitleDuplicate, slideLoc with { ShapeId = titleShape?.Id }, titleShape?.Name,
                    "Slide " + number.ToString(CultureInfo.InvariantCulture) + " has the same title as slide " + first.ToString(CultureInfo.InvariantCulture) + " (“" + title + "”).",
                    "Give each slide a unique title."));
            }
            else
            {
                titles[key] = number;
            }
        }

        // The reading order of a slide is its shape tree from back to front; the title belongs first.
        if (titleShape is not null)
        {
            int order = 0;
            bool topLevel = false;
            foreach (Shape s in top)
            {
                if (s.Placeholder is { } p && p.Type is "dt" or "ftr" or "sldNum")
                {
                    continue;
                }

                if (s.Markup == titleShape.Markup)
                {
                    topLevel = true;
                    break;
                }

                order++;
            }

            if (order > 0 && topLevel)
            {
                _c.Add(new AccessibilityIssue(AccessibilityRules.SlideReadingOrder, slideLoc with { ShapeId = titleShape.Id }, titleShape.Name,
                    "The title is not first in the reading order of slide " + number.ToString(CultureInfo.InvariantCulture) + ".",
                    "Move the title to the back of the stacking order (Selection Pane: drag it to the bottom of the list) so it is read first."));
            }
        }

        var scope = new Scope(slide, number, BackgroundOf(slide));
        foreach (Shape s in top)
        {
            CheckShape(s, scope, groupAltCovers: false);
        }
    }

    private sealed record Scope(Slide Slide, int Number, Fill Background);

    // ---- shapes ------------------------------------------------------------------------------------------------------------------------------

    private static AccessibilityLocation Loc(Scope scope, Shape s) => new() { Part = scope.Slide.PartName, Slide = scope.Number, ShapeId = s.Id };

    private void CheckShape(Shape s, Scope scope, bool groupAltCovers)
    {
        switch (s.Kind)
        {
            case ShapeKind.AlternateContent:
                foreach (Shape inner in s.Children)
                {
                    CheckShape(inner, scope, groupAltCovers);
                }

                return;
            case ShapeKind.Group:
                Alt(s, scope, "group", groupAltCovers);
                foreach (Shape inner in s.Children)
                {
                    CheckShape(inner, scope, groupAltCovers: true);
                }

                return;
            case ShapeKind.Picture:
                {
                    MarkupNode nvPr = NvPr(s);
                    bool video = !Child(nvPr, "videoFile").IsNull, audio = !Child(nvPr, "audioFile").IsNull;
                    Alt(s, scope, video ? "video" : audio ? "audio" : "picture", groupAltCovers);
                    if ((video || audio) && !HasTrack(nvPr))
                    {
                        _c.Add(new AccessibilityIssue(AccessibilityRules.MediaCaptions, Loc(scope, s), s.Name,
                            "The " + (video ? "video" : "audio") + " has no closed captions.", "Add a captions file to the media (Playback, Insert Captions), unless the recording has none to give."));
                    }

                    return;
                }

            case ShapeKind.GraphicFrame:
                switch (s.FrameKind)
                {
                    case GraphicFrameKind.Table when s.Table is { } table:
                        CheckTable(s, table, scope);
                        break;
                    case GraphicFrameKind.Chart:
                        Alt(s, scope, "chart", groupAltCovers);
                        break;
                    case GraphicFrameKind.Diagram:
                        Alt(s, scope, "SmartArt graphic", groupAltCovers);
                        break;
                    case GraphicFrameKind.OleObject:
                        Alt(s, scope, "embedded object", groupAltCovers);
                        break;
                }

                return;
            case ShapeKind.Shape:
                {
                    TextBody? body = s.TextBody;
                    bool hasText = body is not null && body.Text.Trim().Length > 0;
                    if (s.Placeholder is null && !hasText)
                    {
                        Alt(s, scope, "shape", groupAltCovers);
                    }

                    if (body is not null)
                    {
                        CheckText(s, body, scope);
                    }

                    return;
                }
        }
    }

    private static MarkupNode NvPr(Shape s)
    {
        foreach (MarkupNode c in s.Markup.Node.ChildElements)
        {
            if (c.LocalNameString.StartsWith("nv", StringComparison.Ordinal) && c.LocalNameString.EndsWith("Pr", StringComparison.Ordinal))
            {
                return Child(c, "nvPr");
            }
        }

        return default;
    }

    // [MS-PPTX] §2.6 (Track part): the captions of a media object are listed as p2017:track elements in the extension list of its p:nvPr.
    private static bool HasTrack(MarkupNode nvPr, int depth = 0)
    {
        if (nvPr.IsNull || depth > 6)
        {
            return false;
        }

        foreach (MarkupNode c in nvPr.ChildElements)
        {
            if (c.LocalNameString == "track" || HasTrack(c, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private void Alt(Shape s, Scope scope, string what, bool groupCovers)
    {
        if (groupCovers || !s.CommonProperties.IsValid)
        {
            return;
        }

        DrawingAccessibility.Check(_c, s.CommonProperties.Node, Loc(scope, s), what);
    }

    // ---- tables ------------------------------------------------------------------------------------------------------------------------------

    private void CheckTable(Shape frame, Table table, Scope scope)
    {
        AccessibilityLocation loc = Loc(scope, frame);
        if (!table.HasHeaderRow)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.TableHeaderRow, loc, frame.Name, "The table has no header row.",
                "Turn on “Header Row” in the table design options.", AccessibilityFixKind.SetHeaderRow, table.Markup.Node));
        }

        IReadOnlyList<TableRow> rows = table.Rows;
        int columns = table.ColumnCount;
        bool merged = false, irregular = false;
        var blank = new List<List<bool>>();
        foreach (TableRow row in rows)
        {
            var empties = new List<bool>();
            foreach (TableCell cell in row.Cells)
            {
                if (cell.ColumnSpan > 1 || cell.RowSpan > 1 || cell.IsMergedContinuation)
                {
                    merged = true;
                }

                empties.Add(cell.Text.Trim().Length == 0);
                if (cell.TextBody is { } tb)
                {
                    CheckText(frame, tb, scope, cell);
                }
            }

            if (row.Cells.Count != columns)
            {
                irregular = true;
            }

            blank.Add(empties);
        }

        if (merged)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.TableMergedCells, loc, frame.Name, "The table has merged cells.", "Avoid merging cells; use a simple grid."));
        }

        if (irregular && !merged)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.TableIrregularGrid, loc, frame.Name, "Rows of the table have different numbers of columns.", "Use the same number of columns in every row."));
        }

        if (rows.Count > 1)
        {
            for (int r = 0; r < blank.Count; r++)
            {
                if (blank[r].Count > 0 && blank[r].TrueForAll(static b => b))
                {
                    _c.Add(new AccessibilityIssue(AccessibilityRules.TableBlankCells, loc, frame.Name, "Row " + (r + 1).ToString(CultureInfo.InvariantCulture) + " of the table is blank.", "Delete the blank row."));
                }
            }
        }

        if (columns > 1 && rows.Count > 1)
        {
            for (int col = 0; col < columns; col++)
            {
                bool all = true, any = false;
                foreach (List<bool> row in blank)
                {
                    if (col < row.Count)
                    {
                        any = true;
                        all &= row[col];
                    }
                }

                if (any && all)
                {
                    _c.Add(new AccessibilityIssue(AccessibilityRules.TableBlankCells, loc, frame.Name, "Column " + (col + 1).ToString(CultureInfo.InvariantCulture) + " of the table is blank.", "Delete the blank column."));
                }
            }
        }
    }

    // ---- text: hyperlinks and contrast ---------------------------------------------------------------------------------------------

    private void CheckText(Shape shape, TextBody body, Scope scope, TableCell? cell = null)
    {
        AccessibilityLocation loc = Loc(scope, shape);
        Fill background = scope.Background;
        bool explicitCellFill = false;
        if (cell is not null)
        {
            // Table styles are not resolved: only a cell fill written on the cell is known.
            MarkupNode fill = FillOf(Child(cell.Markup.Node, "tcPr"));
            if (!fill.IsNull)
            {
                background = ReadFill(fill, scope.Slide);
                explicitCellFill = true;
            }
        }
        else
        {
            MarkupNode spPr = Child(shape.Markup.Node, "spPr");
            MarkupNode fill = FillOf(spPr);
            if (!fill.IsNull)
            {
                background = ReadFill(fill, scope.Slide) is { Kind: not FillKind.None } f ? f : background;
            }
            else if (Child(shape.Markup.Node, "style") is { IsNull: false } style && Child(style, "fillRef") is { IsNull: false } fillRef)
            {
                // [ECMA-376 Part 1 §20.1.4.2.10 (fillRef)]: idx 0 is no fill, 1 is a solid fill in the colour the reference gives; others use the theme's style list.
                if (fillRef.GetAttribute("idx") == "1" && ColorOf(fillRef, scope.Slide) is { } rgb)
                {
                    background = Fill.Solid(rgb);
                }
                else if (fillRef.GetAttribute("idx") is not ("0" or null))
                {
                    background = Fill.Unknown;
                }
            }
        }

        var reported = new HashSet<(int, int)>();
        bool skipped = false;
        foreach (TextParagraph paragraph in body.Paragraphs)
        {
            foreach (TextRun run in paragraph.Runs)
            {
                if (run.Kind != TextRunKind.Text)
                {
                    continue;
                }

                _anyRun = true;
                if (!string.IsNullOrEmpty(run.Language))
                {
                    _anyLang = true;
                }

                string text = run.Text;
                MarkupNode rPr = run.Properties.IsValid ? run.Properties.Node : default;
                MarkupNode link = rPr.IsNull ? default : Child(rPr, "hlinkClick");
                if (!link.IsNull)
                {
                    CheckLink(text, link, scope, loc, shape);
                }

                if (text.Trim().Length == 0)
                {
                    continue;
                }

                if (background.Kind == FillKind.Unknown)
                {
                    if (!skipped)
                    {
                        skipped = true;
                        _c.Skip(AccessibilityRules.ContrastLow, loc, "The background of “" + Shorten(text) + "” is a gradient, picture or pattern, whose contrast is not evaluated.");
                    }

                    continue;
                }

                int fg;
                bool known;
                if (!link.IsNull && scope.Slide.ResolveSchemeColor("hlink") is { } hl && Contrast.TryParseHex(hl, out int hlRgb))
                {
                    // A hyperlink takes the theme's hyperlink colour whatever the run says.
                    fg = hlRgb;
                    known = true;
                }
                else
                {
                    known = ForegroundOf(shape, paragraph, rPr, scope.Slide, cell is not null, explicitCellFill, out fg);
                }

                if (!known)
                {
                    continue;
                }

                int bg = background.Kind == FillKind.Solid ? background.Rgb : 0xFFFFFF;
                double ratio = Contrast.Ratio(fg, bg);
                double size = SizeOf(rPr);
                bool bold = !rPr.IsNull && rPr.GetAttribute("b") is "1" or "true";
                if (!Contrast.Passes(ratio, size, bold) && reported.Add((fg, bg)))
                {
                    _c.Add(new AccessibilityIssue(AccessibilityRules.ContrastLow, loc, shape.Name,
                        "Text colour " + fg.ToString("X6", CultureInfo.InvariantCulture) + " on " + bg.ToString("X6", CultureInfo.InvariantCulture) + " has a contrast ratio of "
                        + ratio.ToString("0.0#", CultureInfo.InvariantCulture) + ":1.",
                        "Use a darker or lighter text colour, or change the fill, so the ratio is at least " + (size >= 18 || (bold && size >= 14) ? "3" : "4.5") + ":1."));
                }
            }
        }
    }

    private static string Shorten(string s) => s.Length <= 24 ? s : s[..24] + "…";

    private static double SizeOf(MarkupNode rPr) =>
        !rPr.IsNull && double.TryParse(rPr.GetAttribute("sz"), NumberStyles.Float, CultureInfo.InvariantCulture, out double sz) ? sz / 100.0 : 18;

    private void CheckLink(string text, MarkupNode link, Scope scope, AccessibilityLocation loc, Shape shape)
    {
        string? target = null;
        string? rid = null;
        foreach (MarkupAttributeRef a in link.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && a.LocalName.SequenceEqual("id"u8))
            {
                rid = a.Value;
            }
        }

        if (rid is not null)
        {
            foreach (Relationship r in scope.Slide.Relationships)
            {
                if (r.Id == rid)
                {
                    target = r.Target;
                }
            }
        }

        if (AccessibilityText.MeaninglessLinkReason(text, target) is { } reason)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.HyperlinkText, loc, shape.Name, "Hyperlink text is not meaningful: " + reason + ".",
                "Write link text that says where the link goes or what it opens."));
        }
    }

    // ---- colours -----------------------------------------------------------------------------------------------------------------------------

    private enum FillKind { None, Solid, Unknown }

    private readonly record struct Fill(FillKind Kind, int Rgb)
    {
        public static Fill Solid(int rgb) => new(FillKind.Solid, rgb);

        public static Fill Unknown => new(FillKind.Unknown, 0);

        public static Fill None => new(FillKind.None, 0);
    }

    private static MarkupNode FillOf(MarkupNode props)
    {
        if (props.IsNull)
        {
            return default;
        }

        foreach (MarkupNode c in props.ChildElements)
        {
            if (c.LocalNameString is "solidFill" or "noFill" or "gradFill" or "blipFill" or "pattFill" or "grpFill")
            {
                return c;
            }
        }

        return default;
    }

    private static Func<string, int?> SchemeOf(PresentationPart part) =>
        name => part.ResolveSchemeColor(name) is { } hex && Contrast.TryParseHex(hex, out int rgb) ? rgb : null;

    private static int? ColorOf(MarkupNode holder, PresentationPart part)
    {
        foreach (MarkupNode c in holder.ChildElements)
        {
            if (c.LocalNameString is "srgbClr" or "schemeClr" or "sysClr" or "prstClr")
            {
                return DrawingColor.TryResolve(c, SchemeOf(part), out int rgb, out _) ? rgb : null;
            }
        }

        return null;
    }

    private static Fill ReadFill(MarkupNode fill, PresentationPart part)
    {
        switch (fill.LocalNameString)
        {
            case "noFill":
                return Fill.None;
            case "solidFill":
                foreach (MarkupNode c in fill.ChildElements)
                {
                    if (DrawingColor.TryResolve(c, SchemeOf(part), out int rgb, out double alpha))
                    {
                        // Partly transparent fills let the slide show through; composite over white, the usual slide.
                        return Fill.Solid(alpha < 1 ? Contrast.Blend(rgb, alpha, 0xFFFFFF) : rgb);
                    }
                }

                return Fill.Unknown;
            default:
                return Fill.Unknown;
        }
    }

    // p:bg is a p:bgPr (a fill) or a p:bgRef (an index into the theme's background styles, with the colour to use).
    private static Fill BackgroundOf(Slide slide)
    {
        PresentationPart?[] chain = [slide, slide.Layout, slide.Master];
        foreach (PresentationPart? part in chain)
        {
            if (part is null || !part.Markup.IsValid)
            {
                continue;
            }

            MarkupNode cSld = Child(part.Markup.Node, "cSld");
            MarkupNode bg = cSld.IsNull ? default : Child(cSld, "bg");
            if (bg.IsNull)
            {
                continue;
            }

            if (Child(bg, "bgPr") is { IsNull: false } pr)
            {
                MarkupNode fill = FillOf(pr);
                return fill.IsNull ? Fill.Unknown : ReadFill(fill, part) is { Kind: FillKind.None } ? Fill.Solid(0xFFFFFF) : ReadFill(fill, part);
            }

            if (Child(bg, "bgRef") is { IsNull: false } reference)
            {
                // idx 1000 and above name the theme's background fill styles (ECMA-376 Part 1 §20.1.4.2.7); 1001 is normally a solid fill.
                return reference.GetAttribute("idx") == "1001" && ColorOf(reference, part) is { } rgb ? Fill.Solid(rgb) : Fill.Unknown;
            }
        }

        return slide.ResolveSchemeColor("bg1") is { } hex && Contrast.TryParseHex(hex, out int white) ? Fill.Solid(white) : Fill.Solid(0xFFFFFF);
    }

    private bool ForegroundOf(Shape shape, TextParagraph paragraph, MarkupNode rPr, Slide slide, bool inCell, bool explicitCellFill, out int rgb)
    {
        rgb = 0;
        Func<string, int?> scheme = SchemeOf(slide);
        if (!rPr.IsNull && FillOf(rPr) is { IsNull: false } own)
        {
            return own.LocalNameString == "solidFill" && ResolveSolid(own, scheme, out rgb);
        }

        if (inCell)
        {
            // Without an explicit run colour, a cell's text takes its colour from the table style, which is not resolved.
            return false;
        }

        int level = paragraph.Level + 1;
        string lvl = "lvl" + level.ToString(CultureInfo.InvariantCulture) + "pPr";

        // shape list style, then the master's text style for the placeholder kind, then the presentation default, then the shape's style reference.
        MarkupNode list = Child(paragraph.Body.Markup.Node, "lstStyle");
        if (DefRPrFill(list, lvl) is { IsNull: false } f1)
        {
            return ResolveSolid(f1, scheme, out rgb);
        }

        if (shape.Placeholder is { } ph && slide.Master is { } master)
        {
            MarkupNode style = ph.Type is "title" or "ctrTitle" ? master.TitleStyle.Node : ph.Type is "dt" or "ftr" or "sldNum" ? master.OtherStyle.Node : master.BodyStyle.Node;
            if (DefRPrFill(style, lvl) is { IsNull: false } f2)
            {
                return ResolveSolid(f2, scheme, out rgb);
            }
        }

        if (Child(shape.Markup.Node, "style") is { IsNull: false } st && Child(st, "fontRef") is { IsNull: false } fontRef && ColorOf(fontRef, slide) is { } fr)
        {
            rgb = fr;
            return true;
        }

        if (_deck.DefaultTextStyle.IsValid && DefRPrFill(_deck.DefaultTextStyle.Node, lvl) is { IsNull: false } f3)
        {
            return ResolveSolid(f3, scheme, out rgb);
        }

        if (scheme("tx1") is { } tx1)
        {
            rgb = tx1;
            return true;
        }

        return false;
    }

    private static MarkupNode DefRPrFill(MarkupNode list, string lvl)
    {
        if (list.IsNull)
        {
            return default;
        }

        MarkupNode level = Child(list, lvl);
        MarkupNode def = level.IsNull ? default : Child(level, "defRPr");
        return def.IsNull ? default : FillOf(def);
    }

    private static bool ResolveSolid(MarkupNode fill, Func<string, int?> scheme, out int rgb)
    {
        rgb = 0;
        if (fill.LocalNameString != "solidFill")
        {
            return false;
        }

        foreach (MarkupNode c in fill.ChildElements)
        {
            if (DrawingColor.TryResolve(c, scheme, out rgb, out _))
            {
                return true;
            }
        }

        return false;
    }
}
