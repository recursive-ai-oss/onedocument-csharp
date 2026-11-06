using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Accessibility;
using RecursiveAI.OneDocument.Documents.Notes;
using RecursiveAI.OneDocument.Documents.Styles;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>
/// Checks a word-processing document against the accessibility rules. It walks each story's markup once (body, headers, footers,
/// footnotes, endnotes) and asks the style sheet for what a run or paragraph inherits.
/// </summary>
internal sealed class DocumentAccessibilityChecker
{
    // Observed Word behaviour is the final word (see docs/accessibility.md); until the Office check settles it, a document is "long"
    // from about a page of text.
    private const int LongDocumentWords = 500;

    private readonly Document _doc;
    private readonly AccessibilityCollector _c;
    private readonly CancellationToken _ct;
    private readonly int _maxDepth;

    private string _story = "body";
    private string _part = "";
    private string? _partForRels;
    private int _para;
    private int _table;
    private int _headingLevel;
    private bool _inBody;
    private bool _anyHeading;
    private bool _anyToc;
    private long _words;
    private int _blankRun;
    private int _runInPara;
    private bool _pageBackground;

    public DocumentAccessibilityChecker(Document doc, CancellationToken ct)
    {
        _doc = doc;
        _ct = ct;
        _maxDepth = doc.Limits.MaxRecursionDepth;
        _c = new AccessibilityCollector(OfficeKind.Document, doc.Limits.MaxDefects);
    }

    private readonly record struct Ctx(bool InTextBox, bool InTable, int? Background);

    public AccessibilityReport Run()
    {
        // ECMA-376 Part 1 §17.2.1 (background) is drawn only when the settings turn on w:displayBackgroundShape (§17.15.1.18).
        var settings = _doc.Parts.LoadRelated(_doc.MainPartName, "settings", out _);
        _pageBackground = settings is not null && Has(settings.Root, "displayBackgroundShape", 0);

        _story = "body";
        _part = _doc.MainPartName;
        _partForRels = _doc.MainPartName;
        _inBody = true;
        MarkupRef bodyRoot = _doc.Body.Container;
        if (bodyRoot.IsValid)
        {
            Walk(bodyRoot.Node, new Ctx(false, false, null), 0);
        }

        _inBody = false;
        CheckHeadersAndFooters();
        CheckNotes();

        if (!_anyHeading && !_anyToc && _words >= LongDocumentWords)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.HeadingsMissing, new AccessibilityLocation { Part = _doc.MainPartName, Story = "body" }, null,
                "The document has about " + _words.ToString(CultureInfo.InvariantCulture) + " words and uses no heading styles or table of contents.",
                "Apply Heading 1, Heading 2 … to the section titles, or add a table of contents."));
        }

        CheckProperties();
        return _c.ToReport();
    }

    private void CheckProperties()
    {
        var loc = new AccessibilityLocation();
        string? title = null;
        try
        {
            title = _doc.Properties.Core.Title;
        }
        catch (OfficeException)
        {
            // An unreadable core properties part is a defect of the open, not a finding.
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.DocumentTitle, loc, null, "The document has no title property.",
                "Set the document title in the file properties; screen readers announce it when the file opens."));
        }

        if (!LanguageDeclared())
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.DocumentLanguage, loc, null, "No proofing language is declared in the styles or the text.",
                "Set the language of the text so screen readers choose the right voice."));
        }
    }

    private bool LanguageDeclared()
    {
        // ECMA-376 Part 1 §17.3.2.20 (lang): the language of a run; docDefaults gives the default for the whole document.
        var styles = _doc.Parts.LoadRelated(_doc.MainPartName, "styles", out _);
        if (styles is not null && HasLang(styles.Root, 0))
        {
            return true;
        }

        return HasLang(_doc.MainMarkup.Root, 0);
    }

    private static bool HasLang(MarkupNode n, int depth)
    {
        if (depth > 64)
        {
            return false;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.LocalNameString == "lang" && (Attr(c, "val") is { Length: > 0 } || Attr(c, "eastAsia") is { Length: > 0 } || Attr(c, "bidi") is { Length: > 0 }))
            {
                return true;
            }

            if (HasLang(c, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Has(MarkupNode n, string local, int depth)
    {
        if (depth > 8)
        {
            return false;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.LocalNameString == local || Has(c, local, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private void CheckHeadersAndFooters()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Section section in _doc.Sections)
        {
            foreach (HeaderFooterKind kind in (ReadOnlySpan<HeaderFooterKind>)[HeaderFooterKind.Header, HeaderFooterKind.Footer])
            {
                foreach (HeaderFooterType type in (ReadOnlySpan<HeaderFooterType>)[HeaderFooterType.Default, HeaderFooterType.First, HeaderFooterType.Even])
                {
                    if (section.GetOwn(kind, type) is { } story && seen.Add(story.PartName) && story.Container.IsValid)
                    {
                        _story = kind == HeaderFooterKind.Header ? "header" : "footer";
                        _part = story.PartName;
                        _partForRels = story.PartName;
                        _para = 0;
                        _table = 0;
                        _blankRun = 0;
                        Walk(story.Container.Node, new Ctx(false, false, null), 0);
                    }
                }
            }
        }
    }

    private void CheckNotes()
    {
        foreach (var (notes, name) in new[] { (_doc.Footnotes, "footnotes"), (_doc.Endnotes, "endnotes") })
        {
            foreach (Note note in notes)
            {
                if (note.Type != NoteType.Normal || !note.Container.IsValid)
                {
                    continue;
                }

                _story = name;
                _part = name == "footnotes" ? "/word/footnotes.xml" : "/word/endnotes.xml";
                _partForRels = _part;
                _para = 0;
                _table = 0;
                _blankRun = 0;
                Walk(note.Container.Node, new Ctx(false, false, null), 0);
            }
        }
    }

    private AccessibilityLocation Loc(int? run = null, int? paragraph = null) =>
        new() { Part = _part, Story = _story, Paragraph = paragraph ?? (_para > 0 ? _para : null), Run = run, Table = _table > 0 ? _table : null };

    private static string? Attr(MarkupNode n, string local) => Wml.Attr(new MarkupRef(n), local);

    private static string? RelId(MarkupNode n)
    {
        foreach (MarkupAttributeRef a in n.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && Wml.AsciiEquals(a.LocalName, "id"))
            {
                return a.Value;
            }
        }

        return null;
    }

    private static bool IsW(MarkupNode n, string local) =>
        n.Kind == MarkupNodeKind.Element && n.LocalNameString == local && SchemaNamespaces.Resolve(n.NamespaceUri) == SchemaNamespace.Wordprocessing;

    // ECMA-376 Part 3 (MCE) §10.2: take the first Choice, else the Fallback; a consumer that understands Choice never reads Fallback.
    private static IEnumerable<MarkupNode> Resolved(MarkupNode n)
    {
        foreach (MarkupNode c in n.ChildElements)
        {
            if (MarkupCompatibility.IsAlternateContent(c))
            {
                MarkupNode pick = default;
                foreach (MarkupNode branch in c.ChildElements)
                {
                    if (branch.LocalNameString == "Choice")
                    {
                        pick = branch;
                        break;
                    }
                }

                if (pick.IsNull)
                {
                    foreach (MarkupNode branch in c.ChildElements)
                    {
                        if (branch.LocalNameString == "Fallback")
                        {
                            pick = branch;
                        }
                    }
                }

                if (!pick.IsNull)
                {
                    foreach (MarkupNode inner in Resolved(pick))
                    {
                        yield return inner;
                    }
                }
            }
            else
            {
                yield return c;
            }
        }
    }

    private void Walk(MarkupNode container, Ctx ctx, int depth)
    {
        _ct.ThrowIfCancellationRequested();
        if (depth > _maxDepth)
        {
            return;
        }

        foreach (MarkupNode c in Resolved(container))
        {
            if (IsW(c, "p"))
            {
                Paragraph(c, ctx, depth);
            }
            else if (IsW(c, "tbl"))
            {
                _blankRun = 0;
                Table(c, ctx, depth);
            }
            else if (IsW(c, "sdt"))
            {
                Sdt(c, ctx, depth, block: true);
            }
            else if (IsW(c, "sectPr") || IsW(c, "sdtPr") || IsW(c, "tcPr") || IsW(c, "sdtEndPr"))
            {
                continue;
            }
            else
            {
                Walk(c, ctx, depth + 1);
            }
        }
    }

    private void Sdt(MarkupNode sdt, Ctx ctx, int depth, bool block)
    {
        MarkupNode pr = default;
        MarkupNode content = default;
        foreach (MarkupNode c in sdt.ChildElements)
        {
            if (IsW(c, "sdtPr"))
            {
                pr = c;
            }
            else if (IsW(c, "sdtContent"))
            {
                content = c;
            }
        }

        // ECMA-376 Part 1 §17.5.2.38 (sdtPr): w:alias is the title of the control. Building-block galleries (tables of contents, cover
        // pages, bibliographies) are generated parts, not form fields.
        bool generated = false;
        bool hasAlias = false;
        string? tag = null;
        if (!pr.IsNull)
        {
            foreach (MarkupNode p in pr.ChildElements)
            {
                switch (p.LocalNameString)
                {
                    case "docPartObj" or "docPartList" or "bibliography" or "citation":
                        generated = true;
                        break;
                    case "alias":
                        hasAlias = !string.IsNullOrWhiteSpace(Attr(p, "val"));
                        break;
                    case "tag":
                        tag = Attr(p, "val");
                        break;
                }
            }
        }

        if (generated && Has(pr, "docPartGallery", 0) && DocPartIsToc(pr))
        {
            _anyToc = true;
        }

        if (!generated && !hasAlias)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.ContentControlTitle, Loc(paragraph: block ? _para + 1 : _para), tag,
                "A content control has no title.", "Give the content control a title in its properties, so screen readers can name the field."));
        }

        if (!content.IsNull)
        {
            Walk(content, ctx, depth + 1);
        }
    }

    private static bool DocPartIsToc(MarkupNode pr)
    {
        foreach (MarkupNode d in pr.ChildElements)
        {
            foreach (MarkupNode g in d.ChildElements)
            {
                if (g.LocalNameString == "docPartGallery" && Attr(g, "val") is { } v && v.Contains("Table of Contents", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ---- paragraphs -------------------------------------------------------------------------------------------------------------------------

    private void Paragraph(MarkupNode p, Ctx ctx, int depth)
    {
        _para++;
        _runInPara = 0;
        var para = new Paragraph(_doc, new MarkupRef(p));
        string text = para.Text;
        bool blank = false;

        if (!ctx.InTextBox)
        {
            if (_inBody)
            {
                _words += CountWords(text);
            }

            Headings(para, p);
            blank = IsBlank(p, text);
            if (!ctx.InTable)
            {
                if (blank)
                {
                    _blankRun++;
                    if (_blankRun == 2)
                    {
                        _c.Add(new AccessibilityIssue(AccessibilityRules.SpacingBlankParagraphs, Loc(), null,
                            "Empty paragraphs are used to create space.", "Delete the empty paragraphs and set space before or after the paragraph instead."));
                    }
                }
                else
                {
                    _blankRun = 0;
                }
            }

            if (HasRepeatedBlanks(text))
            {
                _c.Add(new AccessibilityIssue(AccessibilityRules.SpacingBlankCharacters, Loc(), null,
                    "Runs of spaces or tabs are used to position text.", "Use paragraph indents, tab stops or a table instead of repeated spaces or tabs."));
            }
        }

        var runs = new List<(int Index, Run Run)>();
        Inline(p, ctx, depth, runs);
        Contrast(para, runs, ctx);
    }

    private void Inline(MarkupNode parent, Ctx ctx, int depth, List<(int, Run)> runs)
    {
        if (depth > _maxDepth)
        {
            return;
        }

        foreach (MarkupNode c in Resolved(parent))
        {
            if (IsW(c, "pPr") || IsW(c, "rPr"))
            {
                continue;
            }

            if (IsW(c, "r"))
            {
                _runInPara++;
                runs.Add((_runInPara, new Run(_doc, new MarkupRef(c))));
                RunContent(c, ctx, depth);
            }
            else if (IsW(c, "hyperlink"))
            {
                Hyperlink(c, _doc.Package.GetRelationships(_partForRels));
                Inline(c, ctx, depth + 1, runs);
            }
            else if (IsW(c, "fldSimple"))
            {
                string instr = Attr(c, "instr") ?? string.Empty;
                if (instr.TrimStart().StartsWith("TOC", StringComparison.OrdinalIgnoreCase))
                {
                    _anyToc = true;
                }

                Inline(c, ctx, depth + 1, runs);
            }
            else if (IsW(c, "sdt"))
            {
                Sdt(c, ctx, depth, block: false);
            }
            else if (IsW(c, "sdtContent") || IsW(c, "ins") || IsW(c, "smartTag") || IsW(c, "customXml") || IsW(c, "moveTo"))
            {
                Inline(c, ctx, depth + 1, runs);
            }
        }
    }

    private void RunContent(MarkupNode run, Ctx ctx, int depth)
    {
        foreach (MarkupNode c in Resolved(run))
        {
            if (IsW(c, "drawing"))
            {
                Drawing(c, ctx, depth);
            }
            else if (IsW(c, "pict") || IsW(c, "object"))
            {
                Vml(c, ctx, depth);
            }
            else if (IsW(c, "instrText") && c.InnerText.TrimStart().StartsWith("TOC", StringComparison.OrdinalIgnoreCase))
            {
                _anyToc = true;
            }
        }
    }

    private static long CountWords(string text)
    {
        long n = 0;
        bool inWord = false;
        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                n++;
            }
        }

        return n;
    }

    private static bool HasRepeatedBlanks(string text)
    {
        int spaces = 0, tabs = 0;
        bool leading = true;
        foreach (char ch in text)
        {
            if (ch is ' ' or '\u00A0')
            {
                spaces++;
                tabs = 0;
                if (spaces >= 3 && !leading)
                {
                    return true;
                }
            }
            else if (ch == '\t')
            {
                tabs++;
                spaces = 0;
                if (tabs >= 2)
                {
                    return true;
                }
            }
            else
            {
                spaces = 0;
                tabs = 0;
                leading = false;
            }
        }

        return false;
    }

    private static bool IsBlank(MarkupNode p, string text)
    {
        if (text.Trim().Length > 0)
        {
            return false;
        }

        // A paragraph that carries a drawing, a field, a break or a section end is not just spacing.
        return !HasContent(p, 0);
    }

    private static bool HasContent(MarkupNode n, int depth)
    {
        if (depth > 16)
        {
            return true;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            switch (c.LocalNameString)
            {
                case "drawing" or "pict" or "object" or "fldChar" or "fldSimple" or "sectPr" or "sym" or "footnoteReference" or "endnoteReference" or "commentReference" or "oMath" or "oMathPara":
                    return true;
                case "br" when Attr(c, "type") is "page" or "column":
                    return true;
                case "pPr" or "rPr":
                    if (c.LocalNameString == "pPr" && HasContent(c, depth + 1))
                    {
                        return true;
                    }

                    break;
                default:
                    if (HasContent(c, depth + 1))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    // ---- headings -----------------------------------------------------------------------------------------------------------------------------

    private void Headings(Paragraph para, MarkupNode p)
    {
        string? styleId = para.StyleId;
        string? name = styleId is null ? null : _doc.Styles.Find(styleId)?.Name;
        if ((name ?? styleId ?? "").StartsWith("toc", StringComparison.OrdinalIgnoreCase))
        {
            _anyToc = true;
        }

        int level = HeadingLevel(para, styleId, name);
        if (level == 0)
        {
            return;
        }

        _anyHeading = true;
        // A heading may go back up any number of levels but down one level at a time.
        if (_headingLevel > 0 && level > _headingLevel + 1)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.HeadingsSkipped, Loc(), null,
                "A heading of level " + level.ToString(CultureInfo.InvariantCulture) + " follows a heading of level " + _headingLevel.ToString(CultureInfo.InvariantCulture) + ".",
                "Use the next heading level down, so the outline has no gaps."));
        }

        _headingLevel = level;
        _ = p;
    }

    private static int HeadingLevel(Paragraph para, string? styleId, string? name)
    {
        // The built-in heading styles are named "heading N" (ECMA-376 Part 1 §17.7.4.17 lists them by that name).
        foreach (string? s in (ReadOnlySpan<string?>)[name, styleId])
        {
            if (s is not null && s.StartsWith("heading", StringComparison.OrdinalIgnoreCase))
            {
                string rest = s[7..].Trim();
                if (int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= 9)
                {
                    return n;
                }
            }
        }

        long? outline = para.ResolvedProperties.OutlineLevel;
        return outline is >= 0 and <= 8 ? (int)outline.Value + 1 : 0;
    }

    // ---- drawings ----------------------------------------------------------------------------------------------------------------------------

    private void Drawing(MarkupNode drawing, Ctx ctx, int depth)
    {
        foreach (MarkupNode holder in drawing.ChildElements)
        {
            bool anchor = holder.LocalNameString == "anchor";
            if (!anchor && holder.LocalNameString != "inline")
            {
                continue;
            }

            MarkupNode docPr = default;
            MarkupNode graphicData = default;
            foreach (MarkupNode c in holder.ChildElements)
            {
                if (c.LocalNameString == "docPr")
                {
                    docPr = c;
                }
                else if (c.LocalNameString == "graphic")
                {
                    foreach (MarkupNode g in c.ChildElements)
                    {
                        if (g.LocalNameString == "graphicData")
                        {
                            graphicData = g;
                        }
                    }
                }
            }

            string uri = graphicData.IsNull ? "" : graphicData.GetAttribute("uri") ?? "";
            string what = uri switch
            {
                _ when uri.EndsWith("/picture", StringComparison.Ordinal) => "picture",
                _ when uri.EndsWith("/chart", StringComparison.Ordinal) || uri.Contains("chartex", StringComparison.Ordinal) => "chart",
                _ when uri.EndsWith("/diagram", StringComparison.Ordinal) => "SmartArt graphic",
                _ when uri.EndsWith("/wordprocessingShape", StringComparison.Ordinal) => "shape",
                _ when uri.EndsWith("/wordprocessingGroup", StringComparison.Ordinal) => "group",
                _ when uri.EndsWith("/wordprocessingCanvas", StringComparison.Ordinal) => "drawing canvas",
                _ when uri.Contains("ink", StringComparison.OrdinalIgnoreCase) => "ink drawing",
                _ => "object",
            };

            // A shape with text in it is a text box, whose words are its description; Office asks for alternative text on the rest.
            bool textBox = what == "shape" && ShapeHasText(graphicData);
            MarkupNode[] textStories = FindTextBoxContent(graphicData);
            AccessibilityLocation loc = Loc(_runInPara);
            if (!docPr.IsNull && !textBox)
            {
                DrawingAccessibility.Check(_c, docPr, loc, what);
            }

            if (anchor && !docPr.IsNull)
            {
                _c.Add(new AccessibilityIssue(AccessibilityRules.FloatingObject, loc, docPr.GetAttribute("name"),
                    "The " + what + " is not in line with the text.", "Change the wrapping style to “In line with text”, so assistive technology reads it where it appears."));
            }

            foreach (MarkupNode story in textStories)
            {
                int savePara = _para;
                int saveRun = _runInPara;
                Walk(story, ctx with { InTextBox = true }, depth + 1);
                _para = savePara + 0;
                _runInPara = saveRun;
            }
        }
    }

    private static bool ShapeHasText(MarkupNode graphicData)
    {
        foreach (MarkupNode box in FindTextBoxContent(graphicData))
        {
            if (box.InnerText.Trim().Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static MarkupNode[] FindTextBoxContent(MarkupNode graphicData)
    {
        var list = new List<MarkupNode>();
        if (!graphicData.IsNull)
        {
            CollectTextBoxes(graphicData, list, 0);
        }

        return [.. list];
    }

    private static void CollectTextBoxes(MarkupNode n, List<MarkupNode> list, int depth)
    {
        if (depth > 32)
        {
            return;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.LocalNameString == "txbxContent")
            {
                list.Add(c);
            }
            else
            {
                if (c.LocalNameString == "Fallback")
                {
                    continue;
                }

                CollectTextBoxes(c, list, depth + 1);
            }
        }
    }

    // Legacy drawing (VML): a shape with image data is a picture; Word writes a header picture named PowerPlusWaterMarkObject for a watermark.
    private void Vml(MarkupNode holder, Ctx ctx, int depth)
    {
        VmlWalk(holder, ctx, depth, 0);
    }

    private void VmlWalk(MarkupNode n, Ctx ctx, int depth, int level)
    {
        if (level > 16)
        {
            return;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            string local = c.LocalNameString;
            if (local == "shape" || local == "rect" || local == "roundrect" || local == "oval")
            {
                MarkupNode image = default;
                MarkupNode textbox = default;
                foreach (MarkupNode k in c.ChildElements)
                {
                    if (k.LocalNameString == "imagedata")
                    {
                        image = k;
                    }
                    else if (k.LocalNameString == "textbox")
                    {
                        textbox = k;
                    }
                }

                string? id = c.GetAttribute("id");
                string? style = c.GetAttribute("style");
                AccessibilityLocation loc = Loc(_runInPara);
                if (!image.IsNull)
                {
                    if (id is not null && id.StartsWith("PowerPlusWaterMarkObject", StringComparison.Ordinal))
                    {
                        _c.Add(new AccessibilityIssue(AccessibilityRules.WatermarkImage, loc, id,
                            "A picture is used as a watermark.", "Remove the picture watermark; put any information it carries in the text."));
                    }
                    else
                    {
                        DrawingAccessibility.CheckAlt(_c, id, c.GetAttribute("alt"), loc, local == "shape" && n.LocalNameString == "object" ? "embedded object" : "picture", AccessibilityFixKind.None, c);
                        if (style is not null && style.Contains("position:absolute", StringComparison.Ordinal))
                        {
                            _c.Add(new AccessibilityIssue(AccessibilityRules.FloatingObject, loc, id,
                                "The picture is not in line with the text.", "Change the wrapping style to “In line with text”."));
                        }
                    }
                }

                if (!textbox.IsNull)
                {
                    foreach (MarkupNode box in textbox.ChildElements)
                    {
                        if (box.LocalNameString == "txbxContent")
                        {
                            int savePara = _para;
                            Walk(box, ctx with { InTextBox = true }, depth + 1);
                            _para = savePara;
                        }
                    }
                }
            }
            else if (local is "group" or "object" or "pict")
            {
                VmlWalk(c, ctx, depth, level + 1);
            }
        }
    }

    // ---- hyperlinks --------------------------------------------------------------------------------------------------------------------------

    private void Hyperlink(MarkupNode link, IReadOnlyList<Relationship> rels)
    {
        var sb = new StringBuilder();
        CollectText(link, sb, 0);
        string? target = null;
        if (RelId(link) is { } rid)
        {
            foreach (Relationship r in rels)
            {
                if (r.Id == rid)
                {
                    target = r.Target;
                }
            }
        }
        else
        {
            target = Attr(link, "anchor");
        }

        string text = sb.ToString();
        // Text that merely repeats an in-document bookmark name is still text; only address-like or empty text is judged.
        if (AccessibilityText.MeaninglessLinkReason(text, target) is { } reason)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.HyperlinkText, Loc(_runInPara + 1), null, "Hyperlink text is not meaningful: " + reason + ".",
                "Write link text that says where the link goes or what it opens."));
        }
    }

    private static void CollectText(MarkupNode n, StringBuilder sb, int depth)
    {
        if (depth > 16)
        {
            return;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (c.LocalNameString == "t")
            {
                sb.Append(c.InnerText);
            }
            else if (c.LocalNameString is not ("rPr" or "instrText"))
            {
                CollectText(c, sb, depth + 1);
            }
        }
    }

    // ---- tables ------------------------------------------------------------------------------------------------------------------------------

    private void Table(MarkupNode tbl, Ctx ctx, int depth)
    {
        _table++;
        int tableNumber = _table;
        int? tableBg = null;
        var rows = new List<MarkupNode>();
        foreach (MarkupNode c in tbl.ChildElements)
        {
            if (IsW(c, "tr"))
            {
                rows.Add(c);
            }
            else if (IsW(c, "tblPr"))
            {
                foreach (MarkupNode k in c.ChildElements)
                {
                    if (k.LocalNameString == "shd")
                    {
                        tableBg = ShadingFill(k) ?? tableBg;
                    }
                }
            }
        }

        AccessibilityLocation loc = new() { Part = _part, Story = _story, Table = tableNumber, Paragraph = _para + 1 };
        if (rows.Count > 0)
        {
            bool header = false;
            foreach (MarkupNode k in rows[0].ChildElements)
            {
                if (k.LocalNameString == "trPr")
                {
                    foreach (MarkupNode f in k.ChildElements)
                    {
                        // ECMA-376 Part 1 §17.4.49 (tblHeader): repeat as header row at the top of each page.
                        if (f.LocalNameString == "tblHeader" && (Attr(f, "val")) is not ("0" or "false" or "off"))
                        {
                            header = true;
                        }
                    }
                }
            }

            if (!header && !ctx.InTable)
            {
                _c.Add(new AccessibilityIssue(AccessibilityRules.TableHeaderRow, loc, null, "The table has no header row.",
                    "Mark the first row as a header row (Table Properties, Row, “Repeat as header row at the top of each page”).",
                    AccessibilityFixKind.SetHeaderRow, rows[0]));
            }
        }

        // Grid analysis: each row's width in grid columns, merges and nesting.
        bool merged = false, nested = false, irregular = false;
        int? width = null;
        var cellTexts = new List<List<bool>>();
        int cols = 0;
        foreach (MarkupNode tr in rows)
        {
            int w = 0;
            var empties = new List<bool>();
            foreach (MarkupNode tc in tr.ChildElements)
            {
                if (IsW(tc, "trPr") || IsW(tc, "tblPrEx"))
                {
                    foreach (MarkupNode k in tc.ChildElements)
                    {
                        if (k.LocalNameString is "gridBefore" or "gridAfter" && int.TryParse(Attr(k, "val"), NumberStyles.None, CultureInfo.InvariantCulture, out int extra))
                        {
                            w += extra;
                        }
                    }

                    continue;
                }

                if (!IsW(tc, "tc"))
                {
                    continue;
                }

                int span = 1;
                foreach (MarkupNode pr in tc.ChildElements)
                {
                    if (IsW(pr, "tcPr"))
                    {
                        foreach (MarkupNode k in pr.ChildElements)
                        {
                            string v = Attr(k, "val") ?? "";
                            if (k.LocalNameString == "gridSpan" && int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int gs) && gs > 1)
                            {
                                span = gs;
                                merged = true;
                            }
                            else if (k.LocalNameString == "vMerge")
                            {
                                merged = true;
                            }
                            else if (k.LocalNameString == "hMerge")
                            {
                                merged = true;
                            }
                        }
                    }
                    else if (IsW(pr, "tbl"))
                    {
                        nested = true;
                    }
                }

                w += span;
                empties.Add(CellIsEmpty(tc));
            }

            width ??= w;
            if (width != w)
            {
                irregular = true;
            }

            cols = Math.Max(cols, empties.Count);
            cellTexts.Add(empties);
        }

        if (merged)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.TableMergedCells, loc, null, "The table has merged cells.", "Avoid merging cells; use a simple grid with the same number of columns in every row."));
        }

        if (irregular && !merged)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.TableIrregularGrid, loc, null, "Rows of the table have different numbers of columns.", "Use the same number of columns in every row; avoid split cells."));
        }

        if (nested)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.TableNested, loc, null, "A table is nested inside a cell of this table.", "Replace the nested table with a simple structure."));
        }

        BlankLines(cellTexts, rows.Count, cols, loc);

        // Content of the cells: walk with the cell's shading as the background for contrast.
        foreach (MarkupNode tr in rows)
        {
            foreach (MarkupNode tc in tr.ChildElements)
            {
                if (!IsW(tc, "tc"))
                {
                    continue;
                }

                int? bg = tableBg;
                foreach (MarkupNode pr in tc.ChildElements)
                {
                    if (IsW(pr, "tcPr"))
                    {
                        foreach (MarkupNode k in pr.ChildElements)
                        {
                            if (k.LocalNameString == "shd")
                            {
                                bg = ShadingFill(k) ?? bg;
                            }
                        }
                    }
                }

                Walk(tc, new Ctx(ctx.InTextBox, true, bg), depth + 1);
            }
        }

        _blankRun = 0;
    }

    private void BlankLines(List<List<bool>> empties, int rowCount, int cols, AccessibilityLocation loc)
    {
        if (rowCount == 0)
        {
            return;
        }

        for (int r = 0; r < empties.Count; r++)
        {
            if (empties[r].Count > 0 && empties[r].TrueForAll(static e => e) && rowCount > 1)
            {
                _c.Add(new AccessibilityIssue(AccessibilityRules.TableBlankCells, loc, null, "Row " + (r + 1).ToString(CultureInfo.InvariantCulture) + " of the table is blank.",
                    "Delete the blank row and use cell padding or paragraph spacing instead."));
            }
        }

        if (cols > 1)
        {
            for (int col = 0; col < cols; col++)
            {
                bool all = true;
                bool any = false;
                foreach (List<bool> row in empties)
                {
                    if (col < row.Count)
                    {
                        any = true;
                        all &= row[col];
                    }
                }

                if (any && all && empties.Count > 1)
                {
                    _c.Add(new AccessibilityIssue(AccessibilityRules.TableBlankCells, loc, null, "Column " + (col + 1).ToString(CultureInfo.InvariantCulture) + " of the table is blank.",
                        "Delete the blank column and set the column widths or cell padding instead."));
                }
            }
        }
    }

    private static bool CellIsEmpty(MarkupNode tc)
    {
        foreach (MarkupNode c in tc.ChildElements)
        {
            if (IsW(c, "tcPr"))
            {
                continue;
            }

            if (c.InnerText.Trim().Length > 0 || HasContent(c, 0))
            {
                return false;
            }
        }

        return true;
    }

    // ---- contrast ----------------------------------------------------------------------------------------------------------------------------

    private static int? ShadingFill(MarkupNode shd)
    {
        // ECMA-376 Part 1 §17.3.5 (shd): w:fill is the background of the pattern; "auto" means no explicit colour.
        string? fill = Attr(shd, "fill");
        return Accessibility.Contrast.TryParseHex(fill, out int rgb) ? rgb : null;
    }

    private static readonly Dictionary<string, int> HighlightColors = new(StringComparer.Ordinal)
    {
        ["yellow"] = 0xFFFF00, ["green"] = 0x00FF00, ["cyan"] = 0x00FFFF, ["magenta"] = 0xFF00FF, ["blue"] = 0x0000FF, ["red"] = 0xFF0000,
        ["darkBlue"] = 0x000080, ["darkCyan"] = 0x008080, ["darkGreen"] = 0x008000, ["darkMagenta"] = 0x800080, ["darkRed"] = 0x800000,
        ["darkYellow"] = 0x808000, ["darkGray"] = 0x808080, ["lightGray"] = 0xC0C0C0, ["black"] = 0x000000, ["white"] = 0xFFFFFF,
    };

    private void Contrast(Paragraph para, List<(int Index, Run Run)> runs, Ctx ctx)
    {
        if (runs.Count == 0)
        {
            return;
        }

        int? paraBg = null;
        var pprops = para.ResolvedProperties;
        if (pprops.Get("shd") is { } pshd && Accessibility.Contrast.TryParseHex(pshd.Attribute("fill"), out int pf))
        {
            paraBg = pf;
        }

        HashSet<(int, int)>? reported = null;
        foreach ((int index, Run run) in runs)
        {
            if (run.Text.Trim().Length == 0)
            {
                continue;
            }

            var rp = run.ResolvedProperties;
            if (rp.Hidden)
            {
                continue;
            }

            // Automatic colour is black on a light background and white on a dark one, so it always contrasts.
            if (!ForegroundOf(rp, out int fg))
            {
                continue;
            }

            int bg = 0xFFFFFF;
            if (_pageBackground && PageBackground() is { IsNull: false } page
                && Accessibility.Contrast.TryParseHex(Attr(page, "color"), out int pageColor))
            {
                bg = pageColor;
            }

            if (ctx.Background is { } cell)
            {
                bg = cell;
            }

            if (paraBg is { } pb)
            {
                bg = pb;
            }

            if (rp.Get("shd") is { } rshd && Accessibility.Contrast.TryParseHex(rshd.Attribute("fill"), out int rf))
            {
                bg = rf;
            }

            if (rp.Highlight is { } hl && HighlightColors.TryGetValue(hl, out int hc))
            {
                bg = hc;
            }

            double ratio = Accessibility.Contrast.Ratio(fg, bg);
            double size = (rp.SizeHalfPoints ?? 22) / 2.0;
            if (!Accessibility.Contrast.Passes(ratio, size, rp.Bold) && (reported ??= []).Add((fg, bg)))
            {
                _c.Add(new AccessibilityIssue(AccessibilityRules.ContrastLow, Loc(index), null,
                    "Text colour " + fg.ToString("X6", CultureInfo.InvariantCulture) + " on " + bg.ToString("X6", CultureInfo.InvariantCulture) + " has a contrast ratio of "
                    + ratio.ToString("0.0#", CultureInfo.InvariantCulture) + ":1.",
                    "Use a darker or lighter text colour, or change the background, so the ratio is at least "
                    + (size >= 18 || (rp.Bold && size >= 14) ? "3" : "4.5") + ":1."));
            }
        }

        if (para.ResolvedProperties.Get("shd") is { } s && s.Attribute("fill") is { } f && f != "auto" && !Accessibility.Contrast.TryParseHex(f, out _))
        {
            _c.Skip(AccessibilityRules.ContrastLow, Loc(), "The shading colour “" + f + "” is not an RGB value.");
        }
    }

    private MarkupNode PageBackground()
    {
        foreach (MarkupNode c in _doc.MainMarkup.Root.ChildElements)
        {
            if (c.LocalNameString == "background")
            {
                return c;
            }
        }

        return default;
    }

    private static bool ForegroundOf(ResolvedRunProperties rp, out int rgb)
    {
        rgb = 0;
        ResolvedProperty? color = rp.Get("color");
        if (color is null)
        {
            return false;
        }

        // The fallback value of a theme colour is what Word wrote when it last saved; the theme (with tint or shade) wins when present.
        string? hex = rp.Color;
        if (!Accessibility.Contrast.TryParseHex(hex, out rgb))
        {
            return false;
        }

        if (color.Attribute("themeShade") is { } shade && int.TryParse(shade, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int sh))
        {
            rgb = Accessibility.Contrast.ApplyTint(rgb, (sh / 255.0) - 1);
        }
        else if (color.Attribute("themeTint") is { } tint && int.TryParse(tint, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int tn))
        {
            rgb = Accessibility.Contrast.ApplyTint(rgb, 1 - (tn / 255.0));
        }

        return true;
    }
}
